#ifndef XINPUT_WIRED_H
#define XINPUT_WIRED_H
#include <stdbool.h>
#include <stdint.h>
#include "switch_wired.h"

#define XW_REPORT_BYTES 20u
// Gamepad identity matches the reference. Explicit combined mouse output
// remains a separate composite device with its established identity.
#define XW_GAMEPAD_PID 0x028eu
#define XW_COMBINED_PID 0x4006u
#define XW_POLL_INTERVAL_MS 4u
#define XW_REPORT_PERIOD_US 11000u
#define XW_CONFIGURATION_BYTES 153u
#define XW_COMBINED_CONFIGURATION_BYTES 203u
// Ordinary zero-interval profiles preserve the reference producer cadence.
// Gyro/rumble profiles and explicit intervals arm changes as soon as possible;
// the descriptor's requested poll interval remains independent.
typedef struct { uint32_t next_report; uint8_t requested_interval; bool responsive; } xw_clock;
static inline void xw_set_responsive(xw_clock *clock,bool enabled,uint32_t now) {
    if(clock->responsive!=enabled)clock->next_report=now;
    clock->responsive=enabled;
}
static inline uint8_t xw_poll_interval(uint8_t requested) {
    return requested?requested:XW_POLL_INTERVAL_MS;
}
static inline bool xw_report_due(const xw_clock *clock,uint32_t now) {
    return clock->responsive || clock->requested_interval || (int32_t)(now-clock->next_report)>=0;
}
static inline void xw_report_sent(xw_clock *clock,uint32_t armed) {
    if(clock->responsive || clock->requested_interval)return;
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
// The composite extension retains XUSB10. Gamepad-only output rejects that
// extension and uses the normal retail XUSB identity plus the EP0 portal.
int xw_composite_control_read(const uint8_t setup[8],uint8_t *data,uint16_t capacity,void *context);
int xw_control_read(const uint8_t setup[8],uint8_t *data,uint16_t capacity,void *context);
#endif
