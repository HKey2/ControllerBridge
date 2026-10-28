#ifndef USB_MOUSE_H
#define USB_MOUSE_H
#include <stdbool.h>
#include <stdint.h>

// Internal values are independent of the USB byte layout.
typedef struct { uint8_t buttons; int16_t x,y; } um_report;
#define UM_REPORT_BYTES 7u
// First three bytes retain the boot layout; Report protocol ignores boot XY
// and reads the signed 16-bit relative coordinates at bytes 3 and 5 instead.
void um_encode(const um_report *report,bool wide,uint8_t wire[UM_REPORT_BYTES]);
extern const uint8_t um_device_descriptor[18];
extern const uint8_t um_configuration_descriptor[59];
extern const uint8_t um_report_descriptor[58];
extern const uint8_t um_feature_descriptor[21];
// Switch-compatible pad (IF0/IN1) plus 16-bit/boot mouse (IF1/IN2).
// Configuration Feature reports stay on the gamepad interface only.
extern const uint8_t um_combined_configuration_descriptor[59];
#endif
