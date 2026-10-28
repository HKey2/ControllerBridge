#include "usb_mouse.h"

// Development identity (TinyUSB example VID/PID convention), not a registered
// production identity. Distinct from the existing PowerA gamepad identity.
const uint8_t um_device_descriptor[18]={
    18,1,0x00,0x02,0,0,0,64,0xfe,0xca,0x04,0x40,0x00,0x09,1,2,0,1
};
const uint8_t um_configuration_descriptor[59]={
    9,2,59,0,2,1,0,0x80,50,
    9,4,0,0,1,3,1,2,0, // Boot subclass; SET_PROTOCOL selects 8-bit or 16-bit XY.
    9,0x21,0x11,1,0,1,0x22,sizeof(um_report_descriptor),0,
    7,5,0x81,3,8,0,1,
    9,4,1,0,1,3,0,0,0, // Separate vendor HID for configuration over EP0.
    9,0x21,0x11,1,0,1,0x22,sizeof(um_feature_descriptor),0,
    7,5,0x82,3,8,0,16 // Required HID IN endpoint; remains NAK (Feature only).
};
const uint8_t um_report_descriptor[58]={
    0x05,0x01,0x09,0x02,0xa1,0x01, // Generic Desktop / Mouse application
    0x09,0x01,0xa1,0x00,           // Pointer physical collection
    0x05,0x09,0x19,0x01,0x29,0x02,0x15,0x00,0x25,0x01,
    0x75,0x01,0x95,0x02,0x81,0x02, // Buttons 1 and 2
    0x75,0x06,0x95,0x01,0x81,0x03, // Padding (boot button 3 is always zero)
    0x75,0x08,0x95,0x02,0x81,0x03, // Boot XY is constant/ignored in Report protocol.
    0x05,0x01,0x09,0x30,0x09,0x31,0x16,0x01,0x80,0x26,0xff,0x7f,
    0x75,0x10,0x95,0x02,0x81,0x06, // Relative signed 16-bit X/Y
    0xc0,0xc0
};
void um_encode(const um_report *report,bool wide,uint8_t wire[UM_REPORT_BYTES]) {
    wire[0]=report->buttons;wire[1]=wire[2]=0;
    wire[3]=wire[4]=wire[5]=wire[6]=0;
    if(wide) {
        wire[3]=(uint8_t)report->x;wire[4]=(uint8_t)((uint16_t)report->x>>8);
        wire[5]=(uint8_t)report->y;wire[6]=(uint8_t)((uint16_t)report->y>>8);
    } else {
        wire[1]=(uint8_t)report->x;wire[2]=(uint8_t)report->y;
    }
}
// Separate interfaces allow ID-less boot mouse and 64-byte Feature reports.
// Windows owns the mouse collection; the configurator opens the vendor HID.
const uint8_t um_feature_descriptor[21]={
    0x06,0x00,0xff,0x09,0x01,0xa1,0x01,
    0x15,0x00,0x26,0xff,0x00,0x75,0x08,0x95,0x40,0x09,0x01,0xb1,0x02,0xc0
};
const uint8_t um_combined_configuration_descriptor[59]={
    9,2,59,0,2,1,0,0x80,0xfa,
    9,4,0,0,1,3,0,0,0,
    9,0x21,0x11,1,0,1,0x22,102,0,
    7,5,0x81,3,8,0,1,
    9,4,1,0,1,3,1,2,0,
    9,0x21,0x11,1,0,1,0x22,sizeof(um_report_descriptor),0,
    7,5,0x82,3,8,0,1
};
