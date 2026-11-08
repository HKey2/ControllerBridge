#ifndef XINPUT_WIRED_H
#define XINPUT_WIRED_H
#include <stdbool.h>
#include <stdint.h>
#include "switch_wired.h"

#define XW_REPORT_BYTES 20u
#define XW_CONFIGURATION_BYTES 74u
#define XW_COMBINED_CONFIGURATION_BYTES 99u
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
