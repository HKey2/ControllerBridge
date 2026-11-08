#ifndef UC_SWITCH2_H
#define UC_SWITCH2_H
#include "input_protocol.h"
bool uc_switch2_config(const uint8_t *data,size_t length,uint8_t bulk[2][7]);
const uint8_t *uc_switch2_init(unsigned step,uint8_t *length);
const uint8_t *uc_switch2_layout(uint16_t *length);
#endif
