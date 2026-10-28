#ifndef EGRET_PROFILE_H
#define EGRET_PROFILE_H
#include "controller_usb.h"
bool egret_accept(uint16_t vid, uint16_t pid, void *context);
void egret_describe(const uc_hid_layout *layout, const uint8_t *report, size_t length);
#endif
