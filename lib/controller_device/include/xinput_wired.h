#ifndef XINPUT_WIRED_H
#define XINPUT_WIRED_H
#include <stdbool.h>
#include <stdint.h>
#include "switch_wired.h"

#define XW_REPORT_BYTES 20u
// Default PIDs retain Windows composite/configuration access. The reference
// PID 0x028e can be selected explicitly at build time for a dedicated host.
#ifndef XW_GAMEPAD_PID
#define XW_GAMEPAD_PID 0x4005u
#endif
#ifndef XW_COMBINED_PID
#define XW_COMBINED_PID 0x4006u
#endif
#define XW_POLL_INTERVAL_MS 4u
#define XW_REPORT_PERIOD_US 11000u
#define XW_CONFIGURATION_BYTES 178u
#define XW_COMBINED_CONFIGURATION_BYTES 203u
// Zero preserves the reference producer cadence. Explicit profile intervals
// only set bInterval; state changes are armed as soon as the endpoint permits.
typedef struct { uint32_t next_report; uint8_t requested_interval; } xw_clock;
static inline uint8_t xw_poll_interval(uint8_t requested) {
    return requested?requested:XW_POLL_INTERVAL_MS;
}
static inline bool xw_report_due(const xw_clock *clock,uint32_t now) {
    return clock->requested_interval || (int32_t)(now-clock->next_report)>=0;
}
static inline void xw_report_sent(xw_clock *clock,uint32_t armed) {
    if(clock->requested_interval)return;
    clock->next_report+=XW_REPORT_PERIOD_US;
    if((int32_t)(armed-clock->next_report)>=0)clock->next_report=armed+XW_REPORT_PERIOD_US;
}
// Both X layouts place gamepad IN81's bInterval at byte 41. Other endpoints,
// including the separate relative mouse, retain their own polling periods.
void xw_set_poll_interval(uint8_t *configuration,uint8_t requested);

extern const uint8_t xw_device_descriptor[18];
extern const uint8_t xw_combined_device_descriptor[18];
extern const uint8_t xw_configuration_descriptor[XW_CONFIGURATION_BYTES];
extern const uint8_t xw_combined_configuration_descriptor[XW_COMBINED_CONFIGURATION_BYTES];
// Profile IDs represent physical positions. Convert those positions to the
// Xbox 360 wire layout, including Guide, separate triggers and signed sticks.
void xw_encode(const sw_report *report,uint8_t wire[XW_REPORT_BYTES]);
// Microsoft OS 1.0 compatible ID binds IF0 to the in-box wired XUSB driver,
// leaving the separate HID configuration interface available to the GUI.
int xw_control_read(const uint8_t setup[8],uint8_t *data,uint16_t capacity,void *context);
#endif
