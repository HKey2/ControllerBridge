#ifndef BRIDGE_STATUS_LED_H
#define BRIDGE_STATUS_LED_H
#include "controller_usb.h"
// Core 0 only. No waits, DMA, timers, or IRQ handlers in the LED driver.
void status_led_init(void);
void status_led_event(const uc_event *event);
void status_led_task(uint32_t now, bool profile_ready, bool output_ready, uint32_t profile_rgb);
void status_led_saved(bool success);
void status_led_profile(unsigned number);
bool status_led_profile_busy(void);
bool status_led_command(const uint8_t arg[56], uint8_t reply[56]);
#endif
