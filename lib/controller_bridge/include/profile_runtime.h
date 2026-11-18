#ifndef PROFILE_RUNTIME_H
#define PROFILE_RUNTIME_H
#include "controller_profile.h"
#include "controller_bridge.h"
#include "controller_mouse.h"
#include "controller_paddle_point.h"
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
#define UPR_CMD_TOGGLE_STATE 57
// Capability flags in command 20's status/capability word (the feature word is full).
#define UPR_CAP_BUTTON_TOGGLE 32u
#define UPR_CAP_EXTENDED_CURVES 64u
#define UPR_CAP_VIRTUAL_POINT 128u
#define UPR_CAP_PADDLE_ANALOG 256u
#define UPR_CAP_SHARED_ANALOG_STICK 512u
#define UPR_CAP_INTEGER_VIRTUAL_POINT 1024u
#define UPR_CAP_NORMALIZED_PADDLE 2048u
#define UPR_CAP_LOW_SPEED_CURVE 4096u
#define UPR_FEATURE_PSEUDO_INERTIA 33554432u
#define UPR_FEATURE_ANALOG_PAIR 67108864u
#define UPR_FEATURE_GYRO_STEERING (1u<<27)
#define UPR_FEATURE_GYRO_DRIFT_PROTECTION (1u<<28)
#define UPR_FEATURE_INPUT_DIRECTIONS (1u<<29)
#define UPR_FEATURE_RUMBLE (1u<<30)
#define UPR_FEATURE_ANALOG_ANGLE (1u<<31)
#define UPR_FEATURE_PROFILE_CONVERSION (1u<<24)
#define UPR_FEATURE_BANK_TRANSFER 2048u
#define UPR_FEATURE_PROFILE_OUTPUT 4096u
#define UPR_FEATURE_GUARDED_REGISTRATION 8192u
#define UPR_FEATURE_BUTTON_SHIFT 16384u
#define UPR_FEATURE_PADDLE_AXIS 32768u
#define UPR_FEATURE_GAIN256 65536u
#define UPR_FEATURE_DIRECTION_KEYS 131072u
#define UPR_FEATURE_DIRECTION_SETTINGS 262144u
#define UPR_FEATURE_DIRECTION_TARGETS 524288u
#define UPR_FEATURE_DIRECTION_SOURCES 1048576u
#define UPR_FEATURE_VIRTUAL_ROLLING 2097152u
#define UPR_FEATURE_TRACKBALL_HOLD 4194304u
#define UPR_FEATURE_ROLLING_STEP 8388608u
void upr_init(const udk_settings *common);
const udk_settings *upr_direction_settings(void);
// True means the outgoing state must be reset to neutral (new device/profile).
bool upr_event(const uc_event *event);
uint8_t upr_gyro_axes(void);
bool upr_rumble_enabled(void);
bool upr_apply(const uc_event *event,uint32_t now_us,bool output_ready,sw_report *report);
bool upr_tick(uint32_t now_us,bool output_ready,sw_report *report);
bool upr_ready(void);
bool upr_mouse_ready(void);
void upr_mouse_reset(void);
void upr_release_toggles(void);
// Apply after ordinary mouse/gamepad routing, including repeats and stop timeout.
bool upr_rolling_overlay(uint32_t now_us,sw_report *report);
// Separate acknowledged plans prevent one source from spending another's movement.
typedef struct { um_analog_plan ball;ump_plan paddle;uint8_t sources; } upr_analog_plan;
// A repeated identical USB report can still need a new source/response plan.
// Compare its defined fields, not structure padding or output bytes alone.
static inline bool upr_analog_plan_equal(const upr_analog_plan *a,const upr_analog_plan *b) {
    if(a->sources!=b->sources)return false;
    if(a->sources&1u)for(unsigned i=0;i<2;++i)
        if(a->ball.raw_rate[i]!=b->ball.raw_rate[i] ||
           a->ball.requested_response[i]!=b->ball.requested_response[i] ||
           a->ball.applied_response[i]!=b->ball.applied_response[i] ||
           a->ball.axis[i]!=b->ball.axis[i])return false;
    if(a->sources&2u)
        if(a->paddle.raw_rate!=b->paddle.raw_rate ||
           a->paddle.requested_response!=b->paddle.requested_response ||
           a->paddle.applied_response!=b->paddle.applied_response ||
           a->paddle.axis!=b->paddle.axis)return false;
    return true;
}
uint32_t upr_analog_prepare(uint32_t now_us,uint32_t horizon_us,sw_report *report,upr_analog_plan *plan);
void upr_analog_ack(uint32_t epoch,const upr_analog_plan *plan,uint32_t time_us);
uint8_t upr_hold_directions(uint32_t now_us);
// Mouse-only relative decode; never touches direction/hold/rolling state.
// The caller resets output state at mode/profile/readiness boundaries.
// Decode repeated identical deltas too; never use GUI events.
bool upr_mouse_delta(const uc_event *event,int32_t *x,int32_t *y);
bool upr_pointing_input(const uc_event *event,bool keys_ready,int32_t *x,int32_t *y,um_key_events *keys);
bool upr_direction_enabled(void);
void upr_direction_accumulators(int32_t tenths[3]);
unsigned upr_direction_threshold(void);
// Apply the separately stored hidden cursor settings at initialization.
bool upr_set_direction_settings(const udk_settings *settings);
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
