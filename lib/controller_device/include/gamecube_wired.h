#ifndef GAMECUBE_WIRED_H
#define GAMECUBE_WIRED_H
#include "switch_wired.h"
#include "controller_device.h"
#define GC_REPORT_BYTES 37u
void gc_descriptors(ud_options *options);
void gc_encode(const sw_report *input,uint8_t report[GC_REPORT_BYTES]);
unsigned gc_command(uint8_t command,bool *polling,uint8_t reply[GC_REPORT_BYTES]);
#endif
