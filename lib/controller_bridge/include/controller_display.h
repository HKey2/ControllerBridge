#ifndef CONTROLLER_DISPLAY_H
#define CONTROLLER_DISPLAY_H
#include <stdbool.h>
#include <stdint.h>
// Persistent IDs, never UI positions. Add new IDs without renumbering old ones.
enum { UDISPLAY_OFF=0, UDISPLAY_NAME_PERIOD_SLOT=1, UDISPLAY_NAME=2, UDISPLAY_DETAIL_A=3, UDISPLAY_DETAIL_B=4, UDISPLAY_DETAIL_C=5, UDISPLAY_SEND_INFO=6 };
#define UDISPLAY_SUPPORTED_MASK ((1u<<UDISPLAY_OFF)|(1u<<UDISPLAY_NAME_PERIOD_SLOT)|(1u<<UDISPLAY_NAME)|(1u<<UDISPLAY_DETAIL_A)|(1u<<UDISPLAY_DETAIL_B)|(1u<<UDISPLAY_DETAIL_C)|(1u<<UDISPLAY_SEND_INFO))
#define UDISPLAY_DEFAULT UDISPLAY_OFF
static inline bool udisplay_valid(uint32_t type) {
    switch(type) {
    case UDISPLAY_OFF:case UDISPLAY_NAME_PERIOD_SLOT:case UDISPLAY_NAME:case UDISPLAY_DETAIL_A:case UDISPLAY_DETAIL_B:case UDISPLAY_DETAIL_C:case UDISPLAY_SEND_INFO:return true;
    default:return false;
    }
}
// Display snapshots only; no history or per-input display state is retained.
// Output buttons use the fixed 18 profile IDs; axes use XInput 16-bit units.
typedef struct { uint32_t buttons;uint16_t axes[4]; } udisplay_output;
// Input buttons are HID Button usages 1..32, before profile assignments.
// Axes retain native encoded bits. Wide fields are marked instead of truncated.
typedef struct {
    uint32_t buttons;
    uint16_t axes[4];
    uint8_t valid_axes,wide_axes;
    bool buttons_valid;
} udisplay_input;
#endif
