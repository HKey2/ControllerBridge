#ifndef CONTROLLER_DIRECTION_H
#define CONTROLLER_DIRECTION_H
#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#include <string.h>

// Device-wide cursor parameters, independent of per-profile pointer gain.
// Cursor gains remain internal; threshold, hold and wait have a bounded settings UI.
typedef struct {
    uint32_t threshold;
    uint16_t trackball_gain10,paddle_gain10;
    uint32_t hold_ms; // Minimum pressed duration, stored outside profiles.
    uint32_t wait_ms; // Minimum released interval before the next generated click.
    uint8_t targets[4]; // Trackball U/D/L/R: 0=original direction, 1..18=output ID + 1.
    uint8_t paddle_targets[4]; // Paddle U/D/L/R, selected after its profile axis/inversion.
    uint16_t rolling_gain10; // Tenths of a degree per raw paddle count, independent of pointer gain.
    uint8_t paddle_mode,rolling_flags; // 0 clicks / 1 Virtual Rolling; bit 0 invert, bit 1 left stick.
    uint8_t trackball_mode,trackball_hold_threshold; // 0 legacy clicks / 1 Hold; raw points 1..127.
    uint8_t rolling_step_deg; // Legacy migration default; effective value comes from the profile.
    uint8_t host_mode; // 0=S (Switch-compatible HID), 1=X (wired Xbox 360 XInput), 2=Auto (X then S once), 3=Switch Pro.
    uint16_t inertia_threshold,inertia_interval_ms;
    uint8_t inertia_decay_percent,inertia_enabled; // Enable is supplied only by the active profile.
} udk_settings;
#define UDK_DEFAULT_HOLD_MS 30u
#define UDK_MAX_HOLD_MS 1000u
#define UDK_DEFAULT_WAIT_MS 20u
#define UDK_MAX_WAIT_MS 1000u
#define UDK_DEFAULT_ROLLING_STEP_DEG 30u
#define UDK_DEFAULT_SETTINGS {64u,60u,15u,UDK_DEFAULT_HOLD_MS,UDK_DEFAULT_WAIT_MS,{0,0,0,0},{0,0,0,0},10u,0,0,0,1,UDK_DEFAULT_ROLLING_STEP_DEG,0,8,100,90,0}
_Static_assert(sizeof(udk_settings)==40,"cursor settings layout");
_Static_assert(offsetof(udk_settings,host_mode)==31,"host mode uses existing padding");
#define UDK_TARGET_COUNT 18u
#define UDK_ROLLING_MODE 1u
#define UDK_HOLD_MODE 1u
#define UDK_ROLLING_INVERT 1u
#define UDK_ROLLING_LEFT 2u
static inline uint32_t udk_rolling_pack(const udk_settings *s) {
    return s->rolling_gain10|((uint32_t)s->paddle_mode<<16)|((uint32_t)s->rolling_flags<<24);
}
static inline void udk_rolling_unpack(udk_settings *s,uint32_t value) {
    s->rolling_gain10=(uint16_t)value;s->paddle_mode=(uint8_t)(value>>16);s->rolling_flags=(uint8_t)(value>>24);
}
// UDS7 packs both modes and rotation step into one u32 to retain the 56-byte guarded transaction.
// Byte 0: rolling gain; byte 1: paddle mode, reverse, left, trackball Hold bits;
// byte 2: Hold threshold; byte 3: rotation step degrees; byte-1 upper nibble: zero.
static inline uint32_t udk_modes_pack(const udk_settings *s) {
    return s->rolling_gain10|((uint32_t)s->paddle_mode<<8)|((uint32_t)s->rolling_flags<<9)|
        ((uint32_t)s->trackball_mode<<11)|((uint32_t)s->trackball_hold_threshold<<16)|((uint32_t)s->rolling_step_deg<<24);
}
static inline bool udk_modes_unpack(udk_settings *s,uint32_t value) {
    if(value&0x0000f000u)return false;
    s->rolling_gain10=(uint8_t)value;s->paddle_mode=(uint8_t)((value>>8)&1);
    s->rolling_flags=(uint8_t)((value>>9)&3);s->trackball_mode=(uint8_t)((value>>11)&1);
    s->trackball_hold_threshold=(uint8_t)(value>>16);s->rolling_step_deg=(uint8_t)(value>>24);return true;
}
// Dedicated Flash record: the earlier VRS1 paddle record keeps its original bytes.
static inline uint32_t udk_hold_pack(const udk_settings *s) {
    return s->trackball_hold_threshold|((uint32_t)s->trackball_mode<<8);
}
static inline bool udk_hold_unpack(udk_settings *s,uint32_t value) {
    if(value&0xffff0000u)return false;
    s->trackball_hold_threshold=(uint8_t)value;s->trackball_mode=(uint8_t)(value>>8);return true;
}
enum { UDK_SOURCE_TRACKBALL,UDK_SOURCE_PADDLE };
static inline const uint8_t *udk_source_targets(const udk_settings *s,uint8_t source) {
    return source==UDK_SOURCE_PADDLE?s->paddle_targets:s->targets;
}
static inline bool udk_targets_valid(const uint8_t targets[4]) {
    return targets && targets[0]<=UDK_TARGET_COUNT && targets[1]<=UDK_TARGET_COUNT &&
        targets[2]<=UDK_TARGET_COUNT && targets[3]<=UDK_TARGET_COUNT;
}
// The fallback order is U/D/L/R; profile output IDs order the hat U/R/D/L.
static inline uint8_t udk_target(uint8_t direction,const uint8_t targets[4]) {
    static const uint8_t defaults[4]={15,17,18,16};
    if(direction>=4)return 0;
    return targets && targets[direction]?targets[direction]:defaults[direction];
}
static inline bool udk_settings_valid(const udk_settings *s) {
    return s && s->host_mode<=3 && s->threshold && s->threshold<=65535u &&
        s->inertia_threshold>=1 && s->inertia_interval_ms>=1 && s->inertia_interval_ms<=1000 &&
        s->inertia_decay_percent<=99 && s->inertia_enabled<=1 &&
        s->trackball_gain10>=10 && s->trackball_gain10<=2560 &&
        s->paddle_gain10>=10 && s->paddle_gain10<=100 &&
        s->hold_ms>=1 && s->hold_ms<=UDK_MAX_HOLD_MS &&
        s->wait_ms>=1 && s->wait_ms<=UDK_MAX_WAIT_MS && udk_targets_valid(s->targets) && udk_targets_valid(s->paddle_targets) &&
        s->rolling_gain10>=1 && s->rolling_gain10<=100 && s->paddle_mode<=UDK_ROLLING_MODE && s->rolling_flags<=3 && s->trackball_mode<=UDK_HOLD_MODE &&
        s->trackball_hold_threshold>=1 && s->trackball_hold_threshold<=127 && s->rolling_step_deg>=5 && s->rolling_step_deg<=90 && s->rolling_step_deg%5u==0 && 360u%s->rolling_step_deg==0;
}
// Editing bounds are narrower than the legacy storage bounds. Reading an older
// valid record must not silently replace its values during firmware upgrade.
#define UDK_EDIT_MAX_THRESHOLD 256u
#define UDK_EDIT_MAX_HOLD_MS 512u
#define UDK_EDIT_MAX_WAIT_MS 512u
static inline bool udk_settings_edit(udk_settings *s,uint32_t threshold,uint32_t hold_ms,uint32_t wait_ms) {
    if(!udk_settings_valid(s) || threshold<1 || threshold>UDK_EDIT_MAX_THRESHOLD ||
       hold_ms<1 || hold_ms>UDK_EDIT_MAX_HOLD_MS ||
       wait_ms<1 || wait_ms>UDK_EDIT_MAX_WAIT_MS)return false;
    s->threshold=threshold;s->hold_ms=hold_ms;s->wait_ms=wait_ms;return true;
}
// Shared-settings transaction codec. Parse into a candidate without changing
// runtime state; the caller applies it only after a successful Flash write.
#define UDK_SETTINGS_REQUEST_BYTES 56u
static inline uint32_t udk_get32(const uint8_t *p) {
    return (uint32_t)p[0]|((uint32_t)p[1]<<8)|((uint32_t)p[2]<<16)|((uint32_t)p[3]<<24);
}
// UDSA (version 10) guards six old and six desired words, including the host mode.
// Profile routing and all existing Flash records retain their previous layouts.
static inline uint32_t udk_tuning_pack(const udk_settings *s) {
    return s->rolling_gain10|((uint32_t)s->trackball_hold_threshold<<8)|((uint32_t)s->host_mode<<16);
}
static inline uint32_t udk_inertia_pack(const udk_settings *s) {
    return s->inertia_threshold|((uint32_t)s->inertia_decay_percent<<16);
}
static inline bool udk_inertia_unpack(udk_settings *s,uint32_t value,uint32_t interval) {
    if((value&0xff000000u) || !(value&65535u) || ((value>>16)&255u)>99 || !interval || interval>1000)return false;
    s->inertia_threshold=(uint16_t)value;s->inertia_decay_percent=(uint8_t)(value>>16);s->inertia_interval_ms=(uint16_t)interval;return true;
}
static inline bool udk_settings_equal(const udk_settings *a,const udk_settings *b) {
    return a->threshold==b->threshold && a->hold_ms==b->hold_ms && a->wait_ms==b->wait_ms &&
        udk_tuning_pack(a)==udk_tuning_pack(b) && udk_inertia_pack(a)==udk_inertia_pack(b) && a->inertia_interval_ms==b->inertia_interval_ms;
}
static inline uint8_t udk_settings_request(const udk_settings *current,const uint8_t *request,
                                         size_t length,udk_settings *candidate) {
    if(!udk_settings_valid(current) || !request || !candidate || length!=UDK_SETTINGS_REQUEST_BYTES ||
       memcmp(request,"UDSA",4) || udk_get32(request+4)!=10)return 1;
    uint32_t options=udk_get32(request+44);if(options&0xfffc0000u)return 1;
    udk_settings next=*current;
    next.rolling_gain10=(uint8_t)options;next.trackball_hold_threshold=(uint8_t)(options>>8);next.host_mode=(uint8_t)(options>>16);
    if(!udk_inertia_unpack(&next,udk_get32(request+48),udk_get32(request+52)) ||
       !udk_settings_edit(&next,udk_get32(request+32),udk_get32(request+36),udk_get32(request+40)))return 1;
    if(!udk_settings_equal(&next,current) &&
        (udk_get32(request+8)!=current->threshold || udk_get32(request+12)!=current->hold_ms ||
         udk_get32(request+16)!=current->wait_ms || udk_get32(request+20)!=udk_tuning_pack(current) ||
         udk_get32(request+24)!=udk_inertia_pack(current) || udk_get32(request+28)!=current->inertia_interval_ms))return 4;
    *candidate=next;return 0;
}
#endif
