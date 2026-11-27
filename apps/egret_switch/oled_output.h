#ifndef OLED_OUTPUT_H
#define OLED_OUTPUT_H
#include "controller_display.h"
// Project the actual armed report into fixed profile button IDs and XInput axes.
// Input is the prefix from ud_input_snapshot, never unparsed controller input.
bool oled_output_decode(const uint8_t *report,unsigned length,udisplay_output *state);
#endif
