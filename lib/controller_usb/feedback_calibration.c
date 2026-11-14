#include "controller_feedback.h"
#include "pico/stdlib.h"

// All fields here belong to core 0. The backend publishes pad/session changes
// atomically; a stale or expired GUI lease never controls a later session.
static uint32_t serial,lease_token,lease_until,lease_epoch,lease_pad,lease_bank;
static bool held;
bool ucf_inhibited(void) { return held || lease_token; }
bool ucf_calibration_pending(void) { return lease_token!=0; }
uint32_t ucf_calibration_token(void) { return lease_token; }
void ucf_calibration_cancel(void) {
    bool was=held || lease_token;held=false;lease_token=0;
    if(was)ucf_select(false);
}
void ucf_calibration_held(bool value) {
    if(value==held)return;
    held=value;
    if(!lease_token)ucf_select(false);
}
void ucf_calibration_tick(uint32_t now,uint32_t bank) {
    if(lease_token && ((int32_t)(now-lease_until)>=0 || bank!=lease_bank ||
       ucf_pad_generation()!=lease_pad || ucf_epoch()!=lease_epoch)) {
        lease_token=0;ucf_select(false);
    }
}
bool ucf_calibration_command(uint8_t action,uint32_t epoch,uint32_t pad,
                            uint32_t bank,uint32_t token,uint32_t current_bank) {
    uint32_t now=time_us_32();ucf_calibration_tick(now,current_bank);
    if(action>2 || epoch!=ucf_epoch() || pad!=ucf_pad_generation() || bank!=current_bank)return false;
    if(action==0) {
        if(token || lease_token)return false;
        ucf_select(false);lease_epoch=ucf_epoch();lease_pad=pad;lease_bank=bank;
        if(!++serial)++serial;
        lease_token=serial;
    } else {
        if(!token || token!=lease_token)return false;
        if(action==2) { lease_token=0;ucf_select(false);return true; }
    }
    lease_until=now+1000000;return true;
}
