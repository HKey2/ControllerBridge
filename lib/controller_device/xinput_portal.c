#include "xinput_portal.h"
#include <string.h>

static uint16_t crc16(const uint8_t *bytes,unsigned length,uint16_t crc) {
    for(unsigned i=0;i<length;i++) {
        crc^=(uint16_t)bytes[i]<<8;
        for(unsigned bit=0;bit<8;bit++)crc=(crc&0x8000)?(uint16_t)((crc<<1)^0x1021):(uint16_t)(crc<<1);
    }
    return crc;
}
static void put_nonce(uint8_t *data,uint32_t nonce) {
    for(unsigned i=0;i<4;i++)data[i]=(uint8_t)(nonce>>(8*i));
}
static int encoded(uint8_t *data,uint16_t capacity,const uint8_t *bytes,unsigned count) {
    unsigned length=2+2*count;
    if(length>capacity || length>254)return -2;
    data[0]=(uint8_t)length;data[1]=3;
    // U+0100..U+01FF are valid UTF-16 characters for every possible byte.
    for(unsigned i=0;i<count;i++) { data[2+2*i]=bytes[i];data[3+2*i]=1; }
    return (int)length;
}
int xw_portal_read(xw_portal *p,const uint8_t s[8],uint8_t *data,
    uint16_t capacity,uint32_t now,bool busy,const uint8_t response[64],uint32_t response_revision,
    void (*submit)(const uint8_t *,uint16_t,void *),void *context) {
    if(s[0]!=0x80 || s[1]!=6 || s[3]!=3)return -1;
    uint16_t value=s[4]|((uint16_t)s[5]<<8),length=s[6]|((uint16_t)s[7]<<8);
    if(s[2]==0xe0 && value==0x4342 && length==34) {
        static const char identity[]="CBXUSB-PORTAL-V1";
        if(capacity<34)return -2;
        data[0]=34;data[1]=3;
        for(unsigned i=0;i<16;i++) { data[2+2*i]=(uint8_t)identity[i];data[3+2*i]=0; }
        return 34;
    }
    if(s[2]==0xe1 && length==10) {
        p->nonce_low=value;p->opening=true;p->expires=now+500000;
        p->open=p->staging=p->waiting=false;
        return encoded(data,capacity,(const uint8_t *)"CBL1",4);
    }
    if(s[2]==0xe2 && length==34) {
        if(!p->opening || (int32_t)(now-p->expires)>=0)return -2;
        p->nonce=p->nonce_low|((uint32_t)value<<16);
        p->opening=false;p->open=true;p->staging=false;p->expires=now+10000000;
        uint8_t reply[16]={'C','B','H','1'};put_nonce(reply+4,p->nonce);
        memcpy(reply+8,p->device_id,sizeof(p->device_id));
        return encoded(data,capacity,reply,sizeof(reply));
    }
    if(!p->open || (int32_t)(now-p->expires)>=0) {
        p->open=p->staging=false;return -1;
    }
    uint8_t reply[68];put_nonce(reply,p->nonce);
    if(s[2]==0xe3 && length==14) {
        if(busy)return -2;
        memset(p->command,0,sizeof(p->command));memcpy(p->command,"UC2C",4);
        p->command[4]=1;p->command[5]=(uint8_t)value;p->command[6]=(uint8_t)(value>>8);
        p->staging=true;
        memcpy(reply+4,p->command+5,2);
    } else if(s[2]>=0x80 && s[2]<0x9c && length==10) {
        if(!p->staging)return -2;
        unsigned offset=8+2*(s[2]-0x80);
        p->command[offset]=(uint8_t)value;p->command[offset+1]=(uint8_t)(value>>8);
    } else if(s[2]==0xe4 && length==14) {
        uint16_t crc=crc16(p->command,64,crc16(reply,4,0xffff));
        if(!p->staging || busy || value!=crc)return -2;
        p->staging=false;p->waiting=true;p->response_revision=response_revision;
        memcpy(reply+4,p->command+5,2);
        submit(p->command,64,context);
    } else if(s[2]==0xe5 && length==138) {
        if(value!=crc16(reply,4,0xffff))return -2;
        // A second client may reuse the same 8-bit command/sequence pair.
        // Do not expose the previous reply before this commit is processed.
        if(p->waiting && p->response_revision==response_revision)memset(reply+4,0,64);
        else { p->waiting=false;memcpy(reply+4,response,64); }
    } else return -1;
    p->expires=now+10000000;
    return encoded(data,capacity,reply,(length-2)/2);
}
