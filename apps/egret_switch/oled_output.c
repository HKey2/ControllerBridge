#include "oled_output.h"
#include "xinput_wired.h"
#include <string.h>

static uint8_t pro_axis(unsigned value,bool invert) {
    // Undo this firmware's 8-to-12-bit Pro encoding, including its exact center.
    if(value==2048)return 128;
    if(invert)value=4095-value;
    return (uint8_t)((value*255+2047)/4095);
}
bool oled_output_decode(const uint8_t *report,unsigned length,uint8_t state[12]) {
    uint8_t wire[XW_REPORT_BYTES];sw_report normalized;
    if(length==14 && report[0]==0 && report[1]==XW_REPORT_BYTES) {
        memcpy(state,report+2,12);
    } else {
        if(length==sizeof(sw_report))memcpy(&normalized,report,sizeof(normalized));
        else if(length==14 && (report[0]==0x30 || report[0]==0x21)) {
            uint16_t b=((report[3]&1)?SW_Y:0)|((report[3]&2)?SW_X:0)|
                ((report[3]&4)?SW_B:0)|((report[3]&8)?SW_A:0)|
                ((report[3]&64)?SW_R:0)|((report[3]&128)?SW_ZR:0)|
                ((report[4]&1)?SW_MINUS:0)|((report[4]&2)?SW_PLUS:0)|
                ((report[4]&4)?SW_R3:0)|((report[4]&8)?SW_L3:0)|
                ((report[4]&16)?SW_HOME:0)|((report[4]&32)?SW_CAPTURE:0)|
                ((report[5]&64)?SW_L:0)|((report[5]&128)?SW_ZL:0);
            static const uint8_t hats[16]={15,4,0,15,2,3,1,15,6,5,7,15,15,15,15,15};
            normalized=(sw_report){.buttons0=(uint8_t)b,.buttons1=(uint8_t)(b>>8),.hat=hats[report[5]&15]};
            normalized.lx=pro_axis(report[6]|((unsigned)(report[7]&15)<<8),false);
            normalized.ly=pro_axis((report[7]>>4)|((unsigned)report[8]<<4),true);
            normalized.rx=pro_axis(report[9]|((unsigned)(report[10]&15)<<8),false);
            normalized.ry=pro_axis((report[10]>>4)|((unsigned)report[11]<<4),true);
        } else return false; // Handshake replies and unavailable reports are not gamepad state.
        xw_encode(&normalized,wire);memcpy(state,wire+2,12);
    }
    // Only the publicly defined XINPUT_GAMEPAD button bits are meaningful.
    // Guide/Capture have no public field here; do not change the real output.
    state[1]&=0xf3;
    return true;
}
