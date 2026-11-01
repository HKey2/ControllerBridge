#ifndef UDLR_TEST_H
#define UDLR_TEST_H
#include <stdint.h>
#include <string.h>
// USB-IF HID usage page 7: U=18, D=07, L=0F, R=15 (hex).
// Standard 8-byte keyboard input; no output LEDs are needed by this test.
static const uint8_t udlr_report[]={
    0x05,1,0x09,6,0xa1,1,
    0x05,7,0x19,0xe0,0x29,0xe7,0x15,0,0x25,1,0x75,1,0x95,8,0x81,2,
    0x75,8,0x95,1,0x81,3,
    0x19,0,0x29,0x65,0x15,0,0x25,0x65,0x75,8,0x95,6,0x81,0,0xc0
};
static const uint8_t udlr_device[]={18,1,0,2,0,0,0,64,0xfe,0xca,4,0x40,3,0x20,1,2,3,1};
static const uint8_t udlr_configuration[]={
    9,2,59,0,2,1,0,0x80,50,
    9,4,0,0,1,3,1,1,0,
    9,0x21,0x11,1,0,1,0x22,sizeof(udlr_report),0,
    7,5,0x81,3,8,0,1,
    9,4,1,0,1,3,0,0,0,
    9,0x21,0x11,1,0,1,0x22,21,0,
    7,5,0x82,3,8,0,16
};
static inline void udlr_encode_directions(uint8_t directions,uint8_t wire[8]) {
    static const uint8_t usage[4]={0x18,0x07,0x0f,0x15};
    memset(wire,0,8);
    unsigned next=2;
    for(unsigned i=0;i<4;++i)if(directions&(1u<<i)) { wire[0]=2;wire[next++]=usage[i]; }
}
static inline void udlr_encode(uint8_t key,uint8_t wire[8]) {
    udlr_encode_directions(key>=1 && key<=4?(uint8_t)(1u<<(key-1)):0,wire);
}
#endif
