#ifndef HOST_OUTPUT_H
#define HOST_OUTPUT_H
#include <stdbool.h>
#include <stdint.h>

enum { UH_SWITCH, UH_XINPUT, UH_AUTO };
enum { UH_FIXED, UH_WAITING, UH_PROBING, UH_CONFIRMED, UH_FALLBACK };
#define UH_PROBE_US 5000000u
typedef struct {
    uint32_t deadline;
    uint8_t selected,active,state;
    bool initialized;
} uh_output;

// Reapplying a profile, USB reset and suspend do not create another attempt.
// Only boot or an explicit change of the selected mode starts a new choice.
static inline void uh_select(uh_output *s,uint8_t selected) {
    if(s->initialized && s->selected==selected)return;
    *s=(uh_output){.selected=selected,.active=selected==UH_AUTO?UH_XINPUT:selected,
        .state=selected==UH_AUTO?UH_WAITING:UH_FIXED,.initialized=true};
}
static inline bool uh_pending(const uh_output *s) {
    return s->state==UH_WAITING || s->state==UH_PROBING;
}
// The deadline starts at the first observed host activity, not before the
// host is connected. A completed X-specific exchange or gamepad IN ACK is
// evidence of acceptance; generic enumeration/configuration is not.
static inline bool uh_poll(uh_output *s,uint32_t now,bool activity,bool configured,bool accepted) {
    if(!uh_pending(s))return false;
    if(configured && accepted) { s->state=UH_CONFIRMED;return false; }
    if(s->state==UH_WAITING) {
        if(!activity)return false;
        s->deadline=now+UH_PROBE_US;s->state=UH_PROBING;
    }
    if((int32_t)(now-s->deadline)<0)return false;
    s->active=UH_SWITCH;s->state=UH_FALLBACK;return true;
}
static inline bool uh_xinput_control(const uint8_t s[8]) {
    return s[0]==0xc1 && s[1]==1 && s[2]==0 && (s[3]==0 || s[3]==1) &&
        s[4]==0 && s[5]==0 && (s[6]!=0 || s[7]!=0);
}
static inline bool uh_xinput_output(const uint8_t *data,uint16_t length) {
    return (length==3 && data[0]==1 && data[1]==3 && data[2]<=13) ||
        (length==8 && data[0]==0 && data[1]==8);
}
#endif
