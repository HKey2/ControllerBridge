#include "input_protocol.h"
#include <string.h>

static uint16_t le16(const uint8_t *p) { return p[0]|((uint16_t)p[1]<<8); }
bool uc_input_config_parse(const uint8_t *data,size_t length,uc_input_configuration *out) {
    memset(out,0,sizeof(*out));
    if(length<9 || data[0]!=9 || data[1]!=2 || le16(data+2)!=length || !data[5])return false;
    uc_input_interface *current=NULL;
    for(size_t offset=0;offset<length;) {
        const uint8_t *d=data+offset;
        if(length-offset<2 || d[0]<2 || d[0]>length-offset)return false;
        if(d[1]==4) {
            if(d[0]<9)return false;
            current=NULL;
            uint8_t protocol=d[5]==3?UC_PROTOCOL_HID:
                d[5]==0xff && d[6]==0x5d && d[7]==1?UC_PROTOCOL_XBOX360:0;
            if(!d[3] && protocol) {
                if(out->interface_count==UC_MAX_INTERFACES)return false;
                for(unsigned i=0;i<out->interface_count;++i)if(out->interfaces[i].number==d[2])return false;
                current=&out->interfaces[out->interface_count++];
                current->number=d[2];current->protocol=protocol;out->protocols|=protocol;
            }
        } else if(current && current->protocol==UC_PROTOCOL_HID && d[1]==0x21) {
            if(d[0]<6 || d[0]<6+3*d[5])return false;
            for(unsigned i=0;i<d[5];++i)if(d[6+3*i]==0x22) {
                uint16_t n=le16(d+7+3*i);
                if(!n || n>UC_REPORT_DESCRIPTOR_CAPACITY)return false;
                current->report_length=n;
            }
        } else if(current && d[1]==5) {
            if(d[0]<7)return false;
            if((d[2]&0x80) && (d[3]&3)==3) {
                if(out->endpoint_count==UC_MAX_ENDPOINTS || !(d[2]&15) || (d[2]&0x70) ||
                   !le16(d+4) || le16(d+4)>64 || !d[6] ||
                   (current->protocol==UC_PROTOCOL_XBOX360 && le16(d+4)<20))return false;
                for(unsigned i=0;i<out->endpoint_count;++i)if(out->endpoints[i].descriptor[2]==d[2])return false;
                uc_input_endpoint *ep=&out->endpoints[out->endpoint_count++];
                memcpy(ep->descriptor,d,7);ep->interface_number=current->number;ep->protocol=current->protocol;
            } else if(current->protocol==UC_PROTOCOL_XBOX360 && !(d[2]&0x80) && (d[3]&3)==3) {
                if(!(d[2]&15) || (d[2]&0x70) || le16(d+4)<3 || le16(d+4)>64 || !d[6] || current->output_descriptor[0])return false;
                memcpy(current->output_descriptor,d,7);
            } else if(current->protocol==UC_PROTOCOL_HID && !(d[2]&0x80) && (d[3]&3)==3 &&
                      (d[2]&15) && !(d[2]&0x70) && le16(d+4)>0 && le16(d+4)<=64 && d[6] && !current->output_descriptor[0]) {
                memcpy(current->output_descriptor,d,7);
            }
        }
        offset+=d[0];
    }
    if(!out->interface_count || !out->endpoint_count)return false;
    for(unsigned i=0;i<out->interface_count;++i) {
        if(out->interfaces[i].protocol==UC_PROTOCOL_HID && !out->interfaces[i].report_length)return false;
        bool found=false;
        for(unsigned j=0;j<out->endpoint_count;++j)if(out->endpoints[j].interface_number==out->interfaces[i].number)found=true;
        if(!found)return false;
    }
    return true;
}
bool uc_input_config_identify(uc_input_configuration *out,uint16_t vid,uint16_t pid) {
    if(vid!=0x057e || pid!=0x2009)return true;
    if(out->interface_count!=1 || out->endpoint_count!=1 || out->interfaces[0].number!=0 ||
       out->interfaces[0].protocol!=UC_PROTOCOL_HID || !out->interfaces[0].output_descriptor[0] ||
       le16(out->interfaces[0].output_descriptor+4)!=64 || le16(out->endpoints[0].descriptor+4)!=64)return false;
    out->interfaces[0].protocol=out->endpoints[0].protocol=out->protocols=UC_PROTOCOL_SWITCH_PRO;
    return true;
}

// Standard 20-byte wired Xbox 360 input packet: two-byte header, digital
// buttons, two unsigned triggers, four signed axes, then six reserved bytes.
static const uint8_t xbox360_schema[]={
    0x05,0x01,0x09,0x05,0xa1,0x01,
    0x75,0x08,0x95,0x02,0x81,0x03,
    0x15,0x00,0x25,0x01,0x75,0x01,0x95,0x04,
    0x09,0x90,0x09,0x91,0x09,0x93,0x09,0x92,0x81,0x02,
    0x05,0x09,0x09,0x08,0x09,0x07,0x09,0x09,0x09,0x0a,0x81,0x02,
    0x95,0x03,0x09,0x05,0x09,0x06,0x09,0x0b,0x81,0x02,
    0x95,0x01,0x81,0x03,
    0x95,0x04,0x09,0x01,0x09,0x02,0x09,0x03,0x09,0x04,0x81,0x02,
    0x05,0x01,0x75,0x08,0x95,0x02,0x26,0xff,0x00,0x09,0x32,0x09,0x35,0x81,0x02,
    0x75,0x10,0x95,0x04,0x16,0x00,0x80,0x26,0xff,0x7f,
    0x09,0x30,0x09,0x31,0x09,0x33,0x09,0x34,0x81,0x02,
    0x75,0x08,0x95,0x06,0x81,0x03,0xc0
};
const uint8_t *uc_xbox360_layout(uint16_t *length) {
    *length=(uint16_t)sizeof(xbox360_schema);return xbox360_schema;
}
// USB Pro-compatible report 0x30. Its actual packed state differs from the
// advertised generic HID layout: 3 header bytes, 3 button bytes, 4 x 12-bit axes.
// Ignore the unused SL/SR/reserved bits and battery/IMU data. Keep raw bytes.
static const uint8_t switch_pro_schema[]={
    0x05,0x01,0x09,0x05,0xa1,0x01,
    0x75,0x08,0x95,0x03,0x81,0x03,
    0x05,0x09,0x15,0x00,0x25,0x01,0x75,0x01,0x95,0x04,
    0x09,0x04,0x09,0x03,0x09,0x02,0x09,0x01,0x81,0x02,
    0x95,0x02,0x81,0x03,0x09,0x06,0x09,0x08,0x81,0x02,
    0x95,0x06,0x09,0x09,0x09,0x0a,0x09,0x0c,0x09,0x0b,0x09,0x0d,0x09,0x0e,0x81,0x02,
    0x95,0x02,0x81,0x03,
    0x05,0x01,0x95,0x04,0x09,0x91,0x09,0x90,0x09,0x92,0x09,0x93,0x81,0x02,
    0x95,0x02,0x81,0x03,0x05,0x09,0x09,0x05,0x09,0x07,0x81,0x02,
    0x05,0x01,0x75,0x0c,0x95,0x04,0x26,0xff,0x0f,
    0x09,0x30,0x09,0x31,0x09,0x33,0x09,0x34,0x81,0x02,0xc0
};
const uint8_t *uc_switch_pro_layout(uint16_t *length) {
    *length=(uint16_t)sizeof(switch_pro_schema);return switch_pro_schema;
}
bool uc_input_packet_valid(uint8_t protocol,const uint8_t *data,size_t length) {
    if(protocol==UC_PROTOCOL_HID)return length>0 && length<=UC_EVENT_BYTES;
    if(protocol==UC_PROTOCOL_SWITCH_PRO)return length>=13 && length<=64 && data[0]==0x30;
    return protocol==UC_PROTOCOL_XBOX360 && length==20 && data[0]==0 && data[1]==20;
}
