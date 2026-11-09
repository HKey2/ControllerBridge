/* Wired Xbox 360 protocol: Microsoft MS-XUSBI, Linux xpad, and the published
 * OpenStickCommunity GP2040-CE XInput descriptors. See docs/xinput-output.md.
 * Protocol constants are kept separate from the existing profile format.
 */
#include "xinput_wired.h"
#include "usb_mouse.h"
#include <string.h>

// The measured Arcade Stick identity is 045E:028E, revision 0114, EP0=8.
// Device class 00 and the default non-retail PIDs keep configuration HID
// available through Windows' composite driver. IF0 binds via XUSB10.
const uint8_t xw_device_descriptor[18]={
    18,1,0x00,0x02,0,0,0,8,0x5e,0x04,XW_GAMEPAD_PID&255,XW_GAMEPAD_PID>>8,0x14,0x01,1,2,3,1
};
const uint8_t xw_combined_device_descriptor[18]={
    18,1,0x00,0x02,0,0,0,8,0x5e,0x04,XW_COMBINED_PID&255,XW_COMBINED_PID>>8,0x14,0x01,1,2,3,1
};
#define XW_GAMEPAD_INTERFACE \
    9,4,0,0,2,0xff,0x5d,1,0, \
    17,0x21,0,1,1,0x25,0x81,20,0,0,0,0,0x13,1,8,0,0, \
    7,5,0x81,3,32,0,XW_POLL_INTERVAL_MS, \
    7,5,0x01,3,32,0,8
// Byte-for-byte IF1..IF3 from the connected reference. Its accessory
// interfaces are present even though this arcade stick has no headset.
#define XW_ACCESSORY_INTERFACES \
    0x09,0x04,0x01,0x00,0x04,0xff,0x5d,0x03,0x00,0x1b,0x21,0x00, \
    0x01,0x01,0x01,0x82,0x40,0x01,0x02,0x20,0x16,0x83,0x00,0x00, \
    0x00,0x00,0x00,0x00,0x16,0x03,0x00,0x00,0x00,0x00,0x00,0x00, \
    0x07,0x05,0x82,0x03,0x20,0x00,0x02,0x07,0x05,0x02,0x03,0x20, \
    0x00,0x04,0x07,0x05,0x83,0x03,0x20,0x00,0x40,0x07,0x05,0x03, \
    0x03,0x20,0x00,0x10,0x09,0x04,0x02,0x00,0x01,0xff,0x5d,0x02, \
    0x00,0x09,0x21,0x00,0x01,0x01,0x22,0x84,0x07,0x00,0x07,0x05, \
    0x84,0x03,0x20,0x00,0x10,0x09,0x04,0x03,0x00,0x00,0xff,0xfd, \
    0x13,0x04,0x06,0x41,0x00,0x01,0x01,0x03
#define XW_CONFIG_INTERFACE(number,endpoint) \
    9,4,number,0,1,3,0,0,0, \
    9,0x21,0x11,1,0,1,0x22,sizeof(um_feature_descriptor),0, \
    7,5,endpoint,3,8,0,16
const uint8_t xw_configuration_descriptor[XW_CONFIGURATION_BYTES]={
    9,2,XW_CONFIGURATION_BYTES,0,5,1,0,0xa0,0xfa,
    XW_GAMEPAD_INTERFACE,
    XW_ACCESSORY_INTERFACES,
    XW_CONFIG_INTERFACE(4,0x85)
};
const uint8_t xw_combined_configuration_descriptor[XW_COMBINED_CONFIGURATION_BYTES]={
    9,2,XW_COMBINED_CONFIGURATION_BYTES,0,6,1,0,0xa0,0xfa,
    XW_GAMEPAD_INTERFACE,
    XW_ACCESSORY_INTERFACES,
    9,4,4,0,1,3,1,2,0,
    9,0x21,0x11,1,0,1,0x22,sizeof(um_report_descriptor),0,
    7,5,0x85,3,8,0,1,
    XW_CONFIG_INTERFACE(5,0x86)
};
static void put16(uint8_t *p,uint16_t value) { p[0]=(uint8_t)value;p[1]=(uint8_t)(value>>8); }
static int16_t stick(uint8_t value,bool invert) {
    int32_t delta=(int32_t)value-128;
    if(invert)return (int16_t)(delta<0?(-delta*32767)/128:-(delta*32768)/127);
    return (int16_t)(delta<0?delta*256:(delta*32767)/127);
}
void xw_encode(const sw_report *r,uint8_t wire[XW_REPORT_BYTES]) {
    static const uint8_t hats[8]={1,9,8,10,2,6,4,5};
    uint16_t source=r->buttons0|((uint16_t)r->buttons1<<8);
    uint16_t buttons=r->hat<8?hats[r->hat]:0;
    // Preserve physical positions: Switch B/A/Y/X -> Xbox A/B/X/Y.
    if(source&SW_B)buttons|=0x1000;
    if(source&SW_A)buttons|=0x2000;
    if(source&SW_Y)buttons|=0x4000;
    if(source&SW_X)buttons|=0x8000;
    if(source&SW_L)buttons|=0x0100;
    if(source&SW_R)buttons|=0x0200;
    if(source&SW_MINUS)buttons|=0x0020;
    if(source&SW_PLUS)buttons|=0x0010;
    if(source&SW_L3)buttons|=0x0040;
    if(source&SW_R3)buttons|=0x0080;
    if(source&SW_HOME)buttons|=0x0400;
    // Xbox 360 has no Capture button. Bit 0x0800 and bytes 14..19 stay zero.
    memset(wire,0,XW_REPORT_BYTES);wire[1]=XW_REPORT_BYTES;
    put16(wire+2,buttons);
    wire[4]=(source&SW_ZL)?255:0;wire[5]=(source&SW_ZR)?255:0;
    put16(wire+6,(uint16_t)stick(r->lx,false));put16(wire+8,(uint16_t)stick(r->ly,true));
    put16(wire+10,(uint16_t)stick(r->rx,false));put16(wire+12,(uint16_t)stick(r->ry,true));
}
int xw_control_read(const uint8_t setup[8],uint8_t *data,uint16_t capacity,void *context) {
    (void)context;
    // Exact strings from the connected reference, including the manufacturer's
    // terminal UTF-16 NUL. Their 16-byte over-reads STALL after the payload.
    static const uint8_t manufacturer[]={16,3,'8',0,'B',0,'i',0,'t',0,'D',0,'o',0,0,0};
    static const uint8_t product[]={22,3,'C',0,'o',0,'n',0,'t',0,'r',0,'o',0,'l',0,'l',0,'e',0,'r',0};
    static const uint8_t serial[]={16,3,'1',0,'5',0,'7',0,'F',0,'8',0,'F',0,'8',0};
    static const uint8_t device_status[]={2,0};
    static const uint8_t os_string[18]={18,3,'M',0,'S',0,'F',0,'T',0,'1',0,'0',0,'0',0,0x20,0};
    static const uint8_t compatible_id[40]={
        40,0,0,0,0,1,4,0,1,0,0,0,0,0,0,0,
        0,1,'X','U','S','B','1','0',0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0
    };
    static const uint8_t properties[10]={10,0,0,0,0,1,5,0,0,0};
    uint16_t value=setup[2]|((uint16_t)setup[3]<<8),index=setup[4]|((uint16_t)setup[5]<<8);
    const uint8_t *reply=NULL;uint16_t length=0;
    if(setup[0]==0x80 && setup[1]==6 && value==0x0301) { reply=manufacturer;length=sizeof(manufacturer); }
    else if(setup[0]==0x80 && setup[1]==6 && value==0x0302) { reply=product;length=sizeof(product); }
    else if(setup[0]==0x80 && setup[1]==6 && value==0x0303) { reply=serial;length=sizeof(serial); }
    else if(setup[0]==0x80 && setup[1]==0 && !value && !index) { reply=device_status;length=sizeof(device_status); }
    else if(setup[0]==0x81 && setup[1]==10 && index>=1 && index<=3)return -2;
    else if(setup[0]==0x82 && setup[1]==0 && (index==2 || index==3 || (index>=0x82 && index<=0x84)))return -2;
    else if(setup[0]==0x80 && setup[1]==6 && value==0x03ee) { reply=os_string;length=sizeof(os_string); }
    else if((setup[0]==0xc0 || setup[0]==0xc1) && setup[1]==0x20 && value==0 && index==4) {
        reply=compatible_id;length=sizeof(compatible_id);
    } else if(setup[0]==0xc1 && setup[1]==0x20 && value==0 && index==5) {
        reply=properties;length=sizeof(properties);
    }
    // Both capability reads return one zero byte on the measured Arcade
    // Stick, including successful Windows initialization. Do not substitute
    // invented 20/8-byte capabilities for this reference behavior.
    static const uint8_t capabilities[1]={0};
    if(setup[0]==0xc1 && setup[1]==1 && index==0 && (value==0x0100 || value==0)) {
        reply=capabilities;length=sizeof(capabilities);
    }
    // Unknown requests still stall; never claim unimplemented controls or motors.
    if(!reply || capacity<length)return -1;
    memcpy(data,reply,length);return length;
}
