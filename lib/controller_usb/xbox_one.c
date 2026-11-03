#include "xbox_one.h"
#include <string.h>

void uc_xbox_one_identify(uc_xbox_one *pad,uint16_t vid,uint16_t pid,uint16_t revision) {
    *pad=(uc_xbox_one){.vid=vid,.pid=pid,.model=UC_XONE_STANDARD};
    if(vid!=0x045e)return;
    if(pid==0x02e3)pad->model=UC_XONE_ELITE;
    else if(pid==0x0b00)pad->model=revision<0x0500?UC_XONE_ELITE2_OLD:
        revision<0x050b?UC_XONE_ELITE2_EARLY:UC_XONE_ELITE2_SPLIT;
}

// GIP command 0x20: three further header bytes, 16 button bits,
// two 10-bit triggers in 16-bit words, then four signed 16-bit axes.
static const uint8_t main_schema[]={
    0x05,0x01,0x09,0x05,0xa1,0x01,0x85,0x20,
    0x75,0x08,0x95,0x03,0x81,0x03,
    0x05,0x09,0x15,0x00,0x25,0x01,0x75,0x01,0x95,0x02,0x81,0x03,
    0x09,0x08,0x09,0x07,0x81,0x02,
    0x95,0x04,0x09,0x01,0x09,0x02,0x09,0x03,0x09,0x04,0x81,0x02,
    0x05,0x01,0x09,0x90,0x09,0x91,0x09,0x93,0x09,0x92,0x81,0x02,
    0x05,0x09,0x09,0x05,0x09,0x06,0x09,0x09,0x09,0x0a,0x81,0x02,
    0x05,0x01,0x75,0x10,0x95,0x02,0x26,0xff,0x03,0x09,0x32,0x09,0x35,0x81,0x02,
    0x95,0x04,0x16,0x00,0x80,0x26,0xff,0x7f,
    0x09,0x30,0x09,0x31,0x09,0x33,0x09,0x34,0x81,0x02
};
static const uint8_t guide_schema[]={
    0x85,0x07,0x75,0x08,0x95,0x03,0x81,0x03,
    0x05,0x09,0x15,0x00,0x25,0x03,0x75,0x02,0x95,0x01,0x09,0x0b,0x81,0x02
};
static const uint8_t paddle_schema[]={
    0x05,0x09,0x15,0x00,0x25,0x01,0x75,0x01,0x95,0x04,
    0x09,0x0c,0x09,0x0d,0x09,0x0e,0x09,0x0f,0x81,0x02
};
uint16_t uc_xbox_one_layout(const uc_xbox_one *pad,uint8_t out[UC_REPORT_DESCRIPTOR_CAPACITY]) {
    uint16_t n=0;
#define APPEND(a) do { memcpy(out+n,a,sizeof(a));n+=(uint16_t)sizeof(a); } while(0)
    APPEND(main_schema);
    if(pad->model>=UC_XONE_ELITE && pad->model<=UC_XONE_ELITE2_EARLY) {
        uint8_t padding[]={0x75,0x08,0x95,0,0x81,0x03};
        padding[3]=pad->model==UC_XONE_ELITE?14:pad->model==UC_XONE_ELITE2_EARLY?4:0;
        if(padding[3])APPEND(padding);
        APPEND(paddle_schema);
    }
    APPEND(guide_schema);
    if(pad->model==UC_XONE_ELITE2_SPLIT) {
        const uint8_t split[]={0x85,0x0c,0x75,0x08,0x95,0x11,0x81,0x03};
        APPEND(split);APPEND(paddle_schema);
    }
    out[n++]=0xc0;
#undef APPEND
    return n;
}
static bool packet(const uint8_t *data,size_t length) {
    // Only complete, single-packet reports for client zero. Do not interpret
    // chunk headers, extended lengths, or truncated payload as button input.
    return length>=4 && length<=64 && !(data[1]&0xcf) && !(data[3]&0x80) &&
        data[3]<=length-4;
}
bool uc_xbox_one_hello(const uc_xbox_one *pad,const uint8_t *data,size_t length) {
    // MS-GIPUSB 2.2.1: wait for the primary device before sending commands.
    // An accessory's Hello on client one cannot start the main controller.
    return length>=32 && length<=64 && data[0]==2 && data[1]==0x20 && data[2] && data[3]==28 &&
        (uint16_t)(data[12]|((uint16_t)data[13]<<8))==pad->vid &&
        (uint16_t)(data[14]|((uint16_t)data[15]<<8))==pad->pid;
}
bool uc_xbox_one_guide_ack(const uint8_t *data,size_t length) {
    return packet(data,length) && data[0]==7 && data[1]==0x30 && data[3]>=2;
}
bool uc_xbox_one_input(const uc_xbox_one *pad,const uint8_t *data,size_t length) {
    if(!packet(data,length))return false;
    if(data[0]==7)return data[3]>=2;
    if(data[0]==0x0c)return pad->model==UC_XONE_ELITE2_SPLIT && data[3]>=16;
    if(data[0]!=0x20)return false;
    unsigned minimum=pad->model==UC_XONE_ELITE?29:pad->model==UC_XONE_ELITE2_OLD?16:
        pad->model==UC_XONE_ELITE2_EARLY?20:14;
    return data[3]>=minimum;
}
void uc_xbox_one_ack(uint8_t sequence,uint8_t out[13]) {
    memset(out,0,13);
    out[0]=1;out[1]=0x20;out[2]=sequence;out[3]=9;
    out[5]=7;out[6]=0x20;out[7]=2;
}
uint8_t uc_xbox_one_init(const uc_xbox_one *pad,unsigned stage,uint8_t sequence,uint8_t out[13]) {
    memset(out,0,13);out[1]=0x20;out[2]=sequence;
    switch(stage) {
    case 0:
        if(!((pad->vid==0x0e6f && pad->pid==0x0165) || (pad->vid==0x0f0d && pad->pid==0x0067)))return 0;
        out[0]=1;out[3]=9;out[5]=4;out[6]=0x20;out[7]=0x3a;out[11]=0x80;return 13;
    case 1: out[0]=5;out[3]=1;return 5;
    case 2:
        if(pad->vid!=0x045e || (pad->pid!=0x02ea && pad->pid!=0x0b00))return 0;
        // This device-specific five-byte command deliberately declares 0x0f.
        out[0]=5;out[3]=0x0f;out[4]=6;return 5;
    case 3:
        if(pad->vid!=0x045e || pad->pid!=0x0b00)return 0;
        out[0]=0x4d;out[1]=0x10;out[3]=2;out[4]=7;return 6;
    case 4:
        // Finish startup for every GIP pad, including Microsoft models.
        out[0]=0x0a;out[3]=3;out[5]=1;out[6]=0x14;return 7;
    case 5:
        out[0]=6;out[3]=2;out[4]=1;return 6;
    case 6: case 7:
        if(pad->vid!=0x24c6 || (pad->pid!=0x541a && pad->pid!=0x542a && pad->pid!=0x543a))return 0;
        out[0]=9;out[1]=0;out[3]=9;out[5]=0x0f;
        if(stage==6) { out[8]=out[9]=0x1d;out[10]=0xff; }
        return 13;
    default:return 0;
    }
}

static bool gip_integer(const uint8_t *data,size_t length,size_t *cursor,uint32_t *value) {
    *value=0;
    for(unsigned shift=0;shift<28;shift+=7) {
        if(*cursor>=length)return false;
        uint8_t b=data[(*cursor)++];*value|=(uint32_t)(b&0x7f)<<shift;
        if(!(b&0x80))return true;
    }
    return false;
}
bool uc_gip_parse(const uint8_t *data,size_t length,uc_gip_message *m) {
    if(length<4 || length>64 || (data[1]&8) || (data[1]&0xc0)==0x40)return false;
    size_t cursor=3;uint32_t size,position=0;
    if(!gip_integer(data,length,&cursor,&size))return false;
    if((data[1]&0x80) && !gip_integer(data,length,&cursor,&position))return false;
    if(size>length-cursor)return false;
    *m=(uc_gip_message){.payload=data+cursor,.position=position,.size=(uint16_t)size,
        .consumed=(uint16_t)(cursor+size),.command=data[0],.flags=data[1],.sequence=data[2]};
    return true;
}
bool uc_gip_fragment_receive(uc_gip_metadata *s,const uc_gip_message *m) {
    if(!(m->flags&0x80)) {
        if(!m->size)return false; // A request is not a metadata response.
        *s=(uc_gip_metadata){.total=m->size,.received=m->size,.sequence=m->sequence,.active=true,.complete=true};
        return true;
    }
    uint32_t offset=m->position;
    if(m->flags&0x40) {
        if(!m->size || !m->position || m->position>65535 || m->size>m->position)return false;
        if(!s->active || s->sequence!=m->sequence || s->total!=m->position)
            *s=(uc_gip_metadata){.total=m->position,.sequence=m->sequence,.active=true};
        offset=0;
    }
    if(!s->active || s->sequence!=m->sequence || offset>s->total || m->size>s->total-offset)return false;
    if(!m->size) {
        if(offset!=s->total || s->received!=s->total)return false;
        s->complete=true;return true;
    }
    // A retransmission must not double-count data. For gaps, ACK the last
    // contiguous offset so the device can resend the missing fragment.
    if(offset==s->received)s->received+=m->size;
    else if(offset<s->received && m->size>s->received-offset)return false;
    return true;
}
bool uc_gip_metadata_receive(uc_gip_metadata *s,const uc_gip_message *m) {
    return m->command==4 && (m->flags&0x20) && uc_gip_fragment_receive(s,m);
}
void uc_gip_ack(const uc_gip_message *m,uint32_t received,uint16_t remaining,uint8_t out[13]) {
    memset(out,0,13);out[0]=1;out[1]=(uint8_t)(0x20|(m->flags&7));out[2]=m->sequence;out[3]=9;
    out[5]=m->command;out[6]=m->flags&0x27;
    for(unsigned i=0;i<4;++i)out[7+i]=(uint8_t)(received>>(8*i));
    out[11]=(uint8_t)remaining;out[12]=(uint8_t)(remaining>>8);
}
