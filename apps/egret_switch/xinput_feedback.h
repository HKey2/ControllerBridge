#ifndef XINPUT_FEEDBACK_H
#define XINPUT_FEEDBACK_H
#include <stdbool.h>
#include <stdint.h>
void xfo_init(void);
void xfo_stop(void);
bool xfo_receive(const uint8_t *data,uint16_t length,void *context);
void xfo_bus_event(unsigned event,void *context);
bool xfo_pending(void);
void xfo_service(bool allowed);
#endif
