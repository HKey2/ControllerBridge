#ifndef USB_CONTROL_TRACE_H
#define USB_CONTROL_TRACE_H
#include <stdbool.h>
#include <stdint.h>
void uct_setup(const uint8_t setup[8],void *context);
void uct_event(uint32_t event,uint32_t detail,void *context);
bool uct_command(uint8_t cmd,const uint8_t arg[56],uint8_t reply[56],uint8_t *status);
#endif
