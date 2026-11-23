#ifndef OLED_TEXT_H
#define OLED_TEXT_H
#include <stdint.h>
#include <stdbool.h>
// Four rows of eight 8x8 cells, plus a terminator per row; slot is zero-based.
void oled_text_format(char rows[4][9],uint8_t type,char mode,unsigned slot,const char *name,uint32_t period_us);
// Detail A uses all eight cells of row 1 for VID then PID. Zero means absent.
void oled_text_detail_a(char rows[4][9],uint32_t in_us,uint32_t out_us,uint32_t bytes_per_second,uint32_t device_ids);
// VID/PID; buttons(4)+LT(2)+RT(2); LX(4)+LY(4); RX(4)+RY(4).
// state is the little-endian XINPUT_GAMEPAD layout, or NULL when unavailable.
void oled_text_detail_b(char rows[4][9],uint32_t device_ids,const uint8_t state[12]);
// Signed X/Y output counts/s; R: configured input capacity/s; S: received input reports/s.
// A zero limit means unknown. Report validity is independent of motion validity.
void oled_text_detail_c(char rows[4][9],bool connected,int32_t x,int32_t y,bool motion_valid,uint32_t limit,uint32_t reports,bool reports_valid);
void oled_text_render(uint8_t pixels[256],const char rows[4][9]);
#endif
