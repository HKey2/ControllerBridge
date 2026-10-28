#include "profile_button.h"
#include "controller_memory.h"
#include <string.h>

bool UC_RAM_FUNC(pb_allow_sampling)(profile_button *b,uint32_t now,bool eligible) {
    if(!eligible) { memset(b,0,sizeof(*b));b->need_release=true;return false; }
    if(!b->idle_tracking) { b->idle_tracking=true;b->idle_since=now;b->need_release=true; }
    return (uint32_t)(now-b->idle_since)>=PB_IDLE_US;
}
bool UC_RAM_FUNC(pb_sample)(profile_button *b,uint32_t now,bool pressed) {
    if(!b->have_sample || (uint32_t)(now-b->last_sample)>PB_SAMPLE_GAP_US) {
        b->candidate=pressed;b->candidate_since=now;b->consecutive=0;
        b->stable=false;b->need_release=true;
    }
    b->have_sample=true;b->last_sample=now;
    if(pressed!=b->candidate) { b->candidate=pressed;b->candidate_since=now;b->consecutive=1;return false; }
    if(b->consecutive<2)b->consecutive++;
    if(b->consecutive<2)return false;
    if(!pressed && !b->stable) { b->need_release=false;return false; }
    if(pressed!=b->stable) {
        b->stable=pressed;
        if(pressed)b->press_since=b->candidate_since;
        else b->need_release=false;
    }
    if(pressed && !b->need_release && (uint32_t)(now-b->press_since)>=PB_HOLD_US) {
        b->need_release=true;return true;
    }
    return false;
}
bool UC_RAM_FUNC(ph_update)(profile_hotkey *h,uint32_t now,bool enabled,uint32_t keys,uint32_t physical) {
    uint32_t down=physical&keys;
    if(!h->initialized || !enabled || !keys || (physical&~keys)) {
        h->previous=down;h->rising=0;h->initialized=true;return false;
    }
    uint32_t fresh=down&~h->previous;h->previous=down;
    h->rising&=down;
    // Retire an idle attempt before the 32-bit clock can wrap. No per-key
    // timestamp scan on unchanged reports or ordinary non-chord activity.
    if(h->rising && (uint32_t)(now-h->last_edge)>PH_WINDOW_US)h->rising=0;
    if(!fresh)return false;
    uint32_t pending=h->rising|fresh;
    for(unsigned i=0;pending;++i,pending>>=1) {
        uint32_t bit=1u<<i;
        if(!(pending&1))continue;
        if(fresh&bit)h->rise_at[i]=now;
        else if((uint32_t)(now-h->rise_at[i])>PH_WINDOW_US)h->rising&=~bit;
    }
    h->rising|=fresh;h->last_edge=now;
    if(down==keys && (h->rising&keys)==keys) { h->rising=0;return true; }
    return false;
}
