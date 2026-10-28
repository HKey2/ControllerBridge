#include "egret_profile.h"
#include <stdio.h>
bool egret_accept(uint16_t vid, uint16_t pid, void *context) {
    (void)context;
    return vid == 0x0ae4 && pid == 0x0703;
}
void egret_describe(const uc_hid_layout *layout, const uint8_t *report, size_t length) {
    int32_t x, y, pressed;
    uint32_t buttons = 0;
    if (!uc_hid_value(layout,1,0x30,report,length,&x) || !uc_hid_value(layout,1,0x31,report,length,&y)) return;
    for (unsigned i = 1; i <= 32; ++i)
        if (uc_hid_value(layout,9,i,report,length,&pressed) && pressed) buttons |= 1u << (i-1);
    printf(" x=%ld y=%ld buttons=%08lx",(long)x,(long)y,(unsigned long)buttons);
}
