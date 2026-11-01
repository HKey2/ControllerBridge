#ifndef CONTROLLER_DIRECTION_H
#define CONTROLLER_DIRECTION_H
#include <stdint.h>
#include <stdbool.h>

// Device-wide cursor parameters, independent of per-profile pointer gain.
// Cursor gains remain internal; threshold, hold and wait have a bounded settings UI.
typedef struct {
    uint32_t threshold;
    uint16_t trackball_gain10,paddle_gain10;
    uint32_t hold_ms; // Minimum pressed duration, stored outside profiles.
    uint32_t wait_ms; // Minimum released interval before the next generated click.
} udk_settings;
#define UDK_DEFAULT_HOLD_MS 30u
#define UDK_MAX_HOLD_MS 1000u
#define UDK_DEFAULT_WAIT_MS 20u
#define UDK_MAX_WAIT_MS 1000u
#define UDK_DEFAULT_SETTINGS {64u,60u,15u,UDK_DEFAULT_HOLD_MS,UDK_DEFAULT_WAIT_MS}
_Static_assert(sizeof(udk_settings)==16,"cursor settings layout");
static inline bool udk_settings_valid(const udk_settings *s) {
    return s && s->threshold && s->threshold<=65535u &&
        s->trackball_gain10>=10 && s->trackball_gain10<=2560 &&
        s->paddle_gain10>=10 && s->paddle_gain10<=100 &&
        s->hold_ms>=1 && s->hold_ms<=UDK_MAX_HOLD_MS &&
        s->wait_ms>=1 && s->wait_ms<=UDK_MAX_WAIT_MS;
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
#endif
