#ifndef OLED_OUTPUT_H
#define OLED_OUTPUT_H
#include <stdbool.h>
#include <stdint.h>
// Project the actual armed report into the 12 XINPUT_GAMEPAD bytes (LE).
// Input is the prefix from ud_input_snapshot, never unparsed controller input.
bool oled_output_decode(const uint8_t *report,unsigned length,uint8_t state[12]);
#endif
