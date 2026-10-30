#ifndef PROFILE_RUNTIME_H
#define PROFILE_RUNTIME_H
#include "controller_profile.h"
#include "controller_bridge.h"
// 31 is reserved by the application for status LED diagnostics.
#define UPR_CMD_PROFILE_ID 32
#define UPR_CMD_SELECT_PROFILE 33
#define UPR_CMD_SWITCH_KEYS 35
#define UPR_CMD_MOUSE_PROFILE 37
#define UPR_CMD_BANK_INFO 40
#define UPR_CMD_BANK_READ 41
#define UPR_CMD_BANK_BEGIN 42
#define UPR_CMD_BANK_CHUNK 43
#define UPR_CMD_BANK_COMMIT 44
#define UPR_CMD_BANK_ABORT 45
#define UPR_CMD_REGISTER_BEGIN 48
#define UPR_FEATURE_BANK_TRANSFER 2048u
#define UPR_FEATURE_PROFILE_OUTPUT 4096u
#define UPR_FEATURE_GUARDED_REGISTRATION 8192u
void upr_init(void);
// True means the outgoing state must be reset to neutral (new device/profile).
bool upr_event(const uc_event *event);
bool upr_apply(const uc_event *event,uint32_t now_us,bool output_ready,sw_report *report);
bool upr_tick(uint32_t now_us,bool output_ready,sw_report *report);
bool upr_ready(void);
bool upr_mouse_ready(void);
void upr_mouse_reset(void);
// Decode this packet, including repeated identical deltas; never use GUI events.
bool upr_mouse_delta(const uc_event *event,int32_t *x,int32_t *y);
unsigned upr_field_count(void);
unsigned upr_active_slot(void); // 0..15, 255 for none
uint8_t upr_input_interval(void);
uint8_t upr_output_options(void);
uint32_t upr_led_rgb(void);
// Current mapping without replaying events accumulated while USB reconnects.
void upr_output_report(uint32_t now_us,sw_report *report);
uint32_t upr_profile_crc(void);
uint32_t upr_bank_generation(void);
unsigned upr_group_count(void);
unsigned upr_profile_number(void);
bool upr_controls_idle(void);
uint32_t upr_switch_inputs(void);
uint32_t upr_switch_keys(void);
bool upr_switch_released(void);
bool upr_cycle_profile(void);
bool upr_selection_pending(void);
bool upr_save_selection(void);
bool upr_command(uint8_t cmd,const uint8_t arg[56],uint8_t reply[56],uint8_t *status,bool busy,bool *state_changed);
#endif
