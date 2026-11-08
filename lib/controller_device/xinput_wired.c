/* Wired Xbox 360 protocol: Microsoft MS-XUSBI, Linux xpad, and the published
 * OpenStickCommunity GP2040-CE XInput descriptors. See docs/xinput-output.md.
 * Protocol constants are kept separate from the existing profile format.
 */
#include "xinput_wired.h"
#include "usb_mouse.h"
#include <string.h>

// Development VID/PID convention, as for our USB mouse. A distinct composite
// identity avoids the retail controller's whole-device driver match. Windows
// binds IF0 through its documented MS_COMP_XUSB10 compatible ID instead.
const uint8_t xw_device_descriptor[18]={
    18,1,0x00,0x02,0,0,0,64,0xfe,0xca,0x05,0x40,0x00,0x01,1,2,0,1
};
const uint8_t xw_combined_device_descriptor[18]={
    18,1,0x00,0x02,0,0,0,64,0xfe,0xca,0x06,0x40,0x00,0x01,1,2,0,1
};
// The gamepad function uses the original FF/5D/01 class, 17-byte XID,
// IN 0x81, OUT 0x02 and 32-byte endpoint reservations. Only the 20-byte
// gamepad report is sent; headset/security interfaces are not advertised.
#define XW_GAMEPAD_INTERFACE \
    9,4,0,0,2,0xff,0x5d,1,0, \
    17,0x21,0,1,1,0x25,0x81,20,0,0,0,0,0x13,2,8,0,0, \
    7,5,0x81,3,32,0,1, \
    7,5,0x02,3,32,0,8
#define XW_CONFIG_INTERFACE(number) \
    9,4,number,0,1,3,0,0,0, \
    9,0x21,0x11,1,0,1,0x22,sizeof(um_feature_descriptor),0, \
    7,5,0x83,3,8,0,16
const uint8_t xw_configuration_descriptor[XW_CONFIGURATION_BYTES]={
    9,2,XW_CONFIGURATION_BYTES,0,2,1,0,0x80,0xfa,
    XW_GAMEPAD_INTERFACE,
    XW_CONFIG_INTERFACE(1)
};
const uint8_t xw_combined_configuration_descriptor[XW_COMBINED_CONFIGURATION_BYTES]={
    9,2,XW_COMBINED_CONFIGURATION_BYTES,0,3,1,0,0x80,0xfa,
    XW_GAMEPAD_INTERFACE,
    9,4,1,0,1,3,1,2,0,
    9,0x21,0x11,1,0,1,0x22,sizeof(um_report_descriptor),0,
    7,5,0x82,3,8,0,1,
    XW_CONFIG_INTERFACE(2)
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
    static const uint8_t os_string[18]={18,3,'M',0,'S',0,'F',0,'T',0,'1',0,'0',0,'0',0,0x20,0};
    static const uint8_t compatible_id[40]={
        40,0,0,0,0,1,4,0,1,0,0,0,0,0,0,0,
        0,1,'X','U','S','B','1','0',0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0
    };
    static const uint8_t properties[10]={10,0,0,0,0,1,5,0,0,0};
    uint16_t value=setup[2]|((uint16_t)setup[3]<<8),index=setup[4]|((uint16_t)setup[5]<<8);
    const uint8_t *reply=NULL;uint16_t length=0;
    if(setup[0]==0x80 && setup[1]==6 && value==0x03ee) { reply=os_string;length=sizeof(os_string); }
    else if((setup[0]==0xc0 || setup[0]==0xc1) && setup[1]==0x20 && value==0 && index==4) {
        reply=compatible_id;length=sizeof(compatible_id);
    } else if(setup[0]==0xc1 && setup[1]==0x20 && value==0 && index==5) {
        reply=properties;length=sizeof(properties);
    }
    // Unsupported vendor requests stall; never fabricate zero capabilities,
    // which would tell the XUSB driver that the controller has no controls.
    if(!reply || capacity<length)return -1;
    memcpy(data,reply,length);return length;
}
