#ifndef SWITCH_WIRED_H
#define SWITCH_WIRED_H
#include <stdint.h>
enum {
    SW_Y=1u<<0, SW_B=1u<<1, SW_A=1u<<2, SW_X=1u<<3,
    SW_L=1u<<4, SW_R=1u<<5, SW_ZL=1u<<6, SW_ZR=1u<<7,
    SW_MINUS=1u<<8, SW_PLUS=1u<<9, SW_L3=1u<<10, SW_R3=1u<<11,
    SW_HOME=1u<<12, SW_CAPTURE=1u<<13
};
typedef struct { uint8_t buttons0, buttons1, hat, lx, ly, rx, ry, pad; } sw_report;
extern const uint8_t sw_device_descriptor[18];
extern const uint8_t sw_configuration_descriptor[34];
extern const uint8_t sw_report_descriptor[102];
void sw_neutral(sw_report *report);
// Combined mouse mode exposes only menu controls; clicks stay on the mouse.
void sw_navigation(const sw_report *mapped, sw_report *report);
#endif
