#ifndef EGRET_SWITCH_PROFILE_H
#define EGRET_SWITCH_PROFILE_H
#include "controller_bridge.h"
void egret_default_configuration(ub_configuration *configuration);
typedef struct { const uc_hid_field *x,*y,*buttons[12]; bool has_ids; } egret_fields;
bool egret_prepare_fields(const uc_hid_layout *layout,egret_fields *fields);
bool egret_switch_report(const egret_fields *fields,const uint8_t *raw,unsigned length,
                         const ub_configuration *configuration,sw_report *report);
#endif
