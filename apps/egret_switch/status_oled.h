#ifndef STATUS_OLED_H
#define STATUS_OLED_H
#include <stdbool.h>
#include <stdint.h>
// Boot-only probe, also performed when display_type is OFF. Presence identifies
// the supported R13-removed board variant independently of display preference.
bool status_oled_init(uint8_t display_type);
// Core 0 only. False means no further task calls are needed until type changes.
// An in-flight transfer finishes before OFF is sent. Faults do not retry.
bool status_oled_set_type(uint8_t display_type);
bool status_oled_task(uint32_t now,char mode,unsigned slot,uint32_t generation,const char *name);
// Supplied by the application; called only by active Detail C, on core 0.
// Existing acknowledged mouse totals are modulo 2^32; epoch changes on flush.
typedef struct { uint32_t x,y,epoch; } oled_mouse_totals;
bool status_oled_mouse_totals(oled_mouse_totals *out);
#endif
