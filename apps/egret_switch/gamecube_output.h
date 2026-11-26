#ifndef GAMECUBE_OUTPUT_H
#define GAMECUBE_OUTPUT_H
#include "gamecube_wired.h"
void gco_init(void);
bool gco_receive(const uint8_t *data,uint16_t length,void *context);
void gco_bus_event(unsigned event,void *context);
void gco_service(void);
bool gco_polling(void);
const uint8_t *gco_reply(unsigned *length);
void gco_reply_started(void);
void gco_reply_complete(bool success);
#endif
