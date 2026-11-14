#ifndef SWITCH_OUTPUT_H
#define SWITCH_OUTPUT_H
#include "switch_wired.h"
#include <stdbool.h>
#include <stdint.h>
void spo_init(void);
void spo_stop(void);
bool spo_receive(const uint8_t *data,uint16_t length,void *context);
void spo_bus_event(unsigned event,void *context);
void spo_service(const sw_report *input,bool allowed);
const uint8_t *spo_reply(void);
void spo_reply_started(void);
void spo_reply_complete(bool success);
void spo_status(uint8_t data[56]);
#endif
