#ifndef OLED_TEXT_H
#define OLED_TEXT_H
#include "controller_display.h"
#include <stdint.h>
#include <stdbool.h>
// Four rows of eight 8x8 cells, plus a terminator per row; slot is zero-based.
void oled_text_format(char rows[4][9],uint8_t type,char mode,unsigned slot,const char *name,uint32_t period_us);
// Detail A uses all eight cells of row 1 for VID then PID. Zero means absent.
void oled_text_detail_a(char rows[4][9],uint32_t in_us,uint32_t out_us,uint32_t bytes_per_second,uint32_t device_ids);
// VID/PID; input Button usages 1..32; native X/Y; native Rx/Ry (or Z/Rz).
void oled_text_detail_b(char rows[4][9],uint32_t device_ids,const udisplay_input *state);
// One name row, up to two bracketed button names, active output stick X/Y.
void oled_text_send_info(char rows[4][9],unsigned slot,const char *name,uint8_t labels,const udisplay_output *state);
// Signed X/Y output counts/s; R: configured input capacity/s; S: received input reports/s.
// A zero limit means unknown. Report validity is independent of motion validity.
void oled_text_detail_c(char rows[4][9],bool connected,int32_t x,int32_t y,bool motion_valid,uint32_t limit,uint32_t reports,bool reports_valid);
void oled_text_render(uint8_t pixels[256],const char rows[4][9]);
#endif
