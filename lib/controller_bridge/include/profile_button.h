#ifndef PROFILE_BUTTON_H
#define PROFILE_BUTTON_H
#include <stdbool.h>
#include <stdint.h>

#define PB_IDLE_US 500000u
#define PB_HOLD_US 1000000u
#define PB_SAMPLE_GAP_US 150000u
typedef struct {
    uint32_t idle_since,candidate_since,press_since,last_sample;
    bool idle_tracking,candidate,stable,have_sample,need_release;
    uint8_t consecutive;
} profile_button;
// Disable sampling during gameplay; each new idle window needs a released button.
bool pb_allow_sampling(profile_button *button,uint32_t now,bool eligible);
// Two matching 50 ms samples debounce edges. Switch once when the hold reaches 1 s.
bool pb_sample(profile_button *button,uint32_t now,bool pressed);
#define PH_WINDOW_US 100000u
typedef struct {
    uint32_t previous,rising,last_edge,rise_at[32];
    bool initialized;
} profile_hotkey;
// Exact chord plus a fresh 0->1 edge on EVERY key within the last 100 ms.
// Successful switches consume all collected edges.
// A held subset cannot retrigger. Inputs are never suppressed or delayed.
bool ph_update(profile_hotkey *hotkey,uint32_t now,bool enabled,uint32_t keys,uint32_t physical);
#endif
