/* WUP-028 compatibility data informed by loopj/usb-gamecube-controller-adapter.
 * Copyright (c) 2026 James Smith. MIT; see licenses/gamecube-adapter-MIT.txt.
 * Bridge mapping and command handling are implemented locally.
 */
#include "gamecube_wired.h"
#include <string.h>
static const uint8_t device[18]={18,1,0,2,0,0,0,64,0x7e,5,0x37,3,0,1,1,2,3,1};
// Each report count excludes its explicit report ID.
#define GC_HID(id,count,kind) 0xa1,1,0x85,id,0x19,0,0x2a,0xff,0,0x15,0,0x26,0xff,0,0x75,8,0x95,count,kind,0,0xc0
static const uint8_t hid[]={5,5,9,0,
    GC_HID(0x11,4,0x91),GC_HID(0x21,36,0x81),GC_HID(0x12,0,0x91),GC_HID(0x22,24,0x81),
    GC_HID(0x13,0,0x91),GC_HID(0x23,1,0x81),GC_HID(0x14,0,0x91),GC_HID(0x24,1,0x81),
    GC_HID(0x15,0,0x91),GC_HID(0x25,1,0x81)};
static const uint8_t configuration[41]={9,2,41,0,1,1,0,0xc0,0xfa,
    9,4,0,0,2,3,0,0,0,9,0x21,0x11,1,0,1,0x22,sizeof(hid)&255,sizeof(hid)>>8,
    7,5,0x81,3,37,0,1,7,5,2,3,5,0,1};
void gc_descriptors(ud_options *o) {
    o->device=device;o->device_length=sizeof(device);o->configuration=configuration;o->configuration_length=sizeof(configuration);
    o->report=hid;o->report_length=sizeof(hid);o->manufacturer="Nintendo";o->product="WUP-028";o->serial="CBGC000001";
    o->input_length=GC_REPORT_BYTES;o->variable_input_length=true;o->output_endpoint=2;
}
static uint8_t invert_y(uint8_t v) { return v==128?128:(uint8_t)(255-v); }
void gc_encode(const sw_report *s,uint8_t r[GC_REPORT_BYTES]) {
    memset(r,0,GC_REPORT_BYTES);r[0]=0x21;r[1]=0x10; // Port 1, wired; other ports absent, no rumble.
    uint16_t b=s->buttons0|((uint16_t)s->buttons1<<8);
    // Native GC names match the fixed Switch IDs; unsupported buttons have no wire bit.
    static const uint8_t hats[8]={0x80,0xa0,0x20,0x60,0x40,0x50,0x10,0x90};
    r[2]=((b&SW_A)?1:0)|((b&SW_B)?2:0)|((b&SW_X)?4:0)|((b&SW_Y)?8:0)|(s->hat<8?hats[s->hat]:0);
    r[3]=((b&SW_PLUS)?1:0)|((b&SW_ZR)?2:0)|((b&SW_R)?4:0)|((b&SW_L)?8:0);
    r[4]=s->lx;r[5]=invert_y(s->ly);r[6]=s->rx;r[7]=invert_y(s->ry);r[8]=s->gc_l;r[9]=s->gc_r;
}
unsigned gc_command(uint8_t c,bool *polling,uint8_t r[GC_REPORT_BYTES]) {
    memset(r,0,GC_REPORT_BYTES);
    switch(c) {
    case 0x12:r[0]=0x22;memset(r+1,128,4);return 25; // Fixed center and released triggers for port 1.
    case 0x13:r[0]=0x23;r[1]=!*polling;*polling=true;return 2;
    case 0x14:r[0]=0x24;r[1]=*polling;*polling=false;return 2;
    case 0x15:r[0]=0x25;r[1]=1;*polling=false;return 2;
    default:return 0; // Motor requests are accepted without driving physical feedback.
    }
}
