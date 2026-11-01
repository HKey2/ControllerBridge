#include "controller_bridge.h"
#include <string.h>
#include <stddef.h>
_Static_assert(sizeof(ub_configuration)==40,"Configuration wire layout");
_Static_assert(sizeof(ub_histogram)==536,"Histogram wire layout");
_Static_assert(offsetof(ub_snapshot,dual)==3552,"Legacy snapshot prefix");
_Static_assert(sizeof(ub_dual_snapshot)==136,"Combined endpoint statistics layout");
_Static_assert(offsetof(ub_snapshot,mouse_buttons)==3688,"Combined snapshot prefix");
_Static_assert(sizeof(ub_mouse_button_snapshot)==96,"Mouse button statistics layout");
_Static_assert(sizeof(ub_snapshot)==3784,"Snapshot wire layout");
_Static_assert(sizeof(ub_snapshot)+16<=4096,"Snapshot fits its flash sector with store header");
void ub_hist_reset(ub_histogram *h,uint32_t step) {
    memset(h,0,sizeof(*h)); h->minimum=UINT32_MAX; h->step_us=step;
}
void ub_hist_add(ub_histogram *h,uint32_t value) {
    h->count++; h->sum+=value;
    if(value<h->minimum)h->minimum=value;
    if(value>h->maximum)h->maximum=value;
    uint32_t bin=value/h->step_us;
    if(bin>=UB_HIST_BINS)bin=UB_HIST_BINS-1;
    h->bins[bin]++;
}
void ub_queue_reset(ub_queue *q) { memset(q,0,sizeof(*q)); }
unsigned ub_queue_depth(const ub_queue *q) { return (q->write+UB_TRANSITIONS-q->read)%UB_TRANSITIONS; }
bool ub_queue_push(ub_queue *q,const ub_frame *f) {
    bool changed=!q->have_latest || memcmp(&q->latest.report,&f->report,sizeof(f->report));
    q->latest=*f; q->latest.transition=changed; q->have_latest=true;
    if(!changed)return true;
    unsigned next=(q->write+1)%UB_TRANSITIONS;
    if(next==q->read) {
        // Explicit fail-safe: discard backlog and preserve the current state,
        // including release, instead of leaving a button stuck after overflow.
        q->drops++; q->read=q->write;
    }
    q->pending[q->write]=*f; q->pending[q->write].transition=true; q->write=next;
    unsigned depth=ub_queue_depth(q); if(depth>q->max_depth)q->max_depth=depth;
    return true;
}
bool ub_queue_direction_click(ub_queue *q,const ub_frame *base,uint8_t direction,uint32_t *sequence) {
    if(direction>=4 || base->report.hat!=15 || ub_queue_depth(q)>UB_TRANSITIONS-3)return false;
    static const uint8_t hats[4]={0,4,6,2};
    ub_frame edge=*base;edge.report.hat=hats[direction];edge.direction_key=(uint8_t)(direction+1);edge.sequence=++*sequence;
    ub_queue_push(q,&edge);
    edge=*base;edge.direction_key=(uint8_t)(0x81+direction);edge.sequence=++*sequence;
    ub_queue_push(q,&edge);q->latest.direction_key=0;
    return true;
}
bool ub_queue_pop(ub_queue *q,ub_frame *f) {
    if(q->read==q->write)return false;
    *f=q->pending[q->read]; q->read=(q->read+1)%UB_TRANSITIONS; return true;
}

void ub_direction_reset(ub_direction_pulse *p) {
    // Preserve lifetime timing statistics while abandoning stale movement.
    p->read=p->count=p->phase=p->level=0;
    p->awaiting=p->have_release=false;p->phase_at=p->source_us=p->cookie=0;
}
bool ub_direction_offer(ub_direction_pulse *p,uint8_t direction,uint32_t received_us) {
    if(direction>=4 || p->count==UB_DIRECTION_PENDING)return false;
    unsigned i=(p->read+p->count)%UB_DIRECTION_PENDING;
    p->pending[i].direction=direction;p->pending[i].received_us=received_us;p->count++;
    return true;
}
unsigned ub_direction_pending_frames(const ub_direction_pulse *p) {
    return 2u*p->count+(p->phase==UB_DIRECTION_PRESS_WAIT?2u:
        (p->phase==UB_DIRECTION_HELD || p->phase==UB_DIRECTION_RELEASE_WAIT)?1u:0u);
}
static uint8_t direction_edge(const ub_direction_pulse *p) {
    if(p->phase==UB_DIRECTION_PRESS_WAIT)return p->level;
    if(p->phase==UB_DIRECTION_RELEASE_WAIT)return (uint8_t)(p->level+0x80);
    return 0;
}
void ub_direction_prepare(ub_direction_pulse *p,uint32_t now,uint32_t hold_us,uint32_t wait_us,ub_frame *frame) {
    frame->direction_key=frame->direction_level=0;
    if(frame->report.hat!=15 || hold_us<1000u || hold_us>UDK_MAX_HOLD_MS*1000u ||
       wait_us<1000u || wait_us>UDK_MAX_WAIT_MS*1000u) { ub_direction_reset(p);return; }
    if(p->phase==UB_DIRECTION_HELD && (uint32_t)(now-p->phase_at)>=hold_us)
        p->phase=UB_DIRECTION_RELEASE_WAIT;
    if(p->phase==UB_DIRECTION_GAP && (uint32_t)(now-p->phase_at)>=wait_us) {
        p->phase=UB_DIRECTION_IDLE;
        // Gap measurements describe consecutive queued clicks, excluding long
        // idle periods that could span multiple wraps of the 32-bit clock.
        if(!p->count)p->have_release=false;
    }
    if(p->phase==UB_DIRECTION_IDLE && p->count) {
        p->level=(uint8_t)(p->pending[p->read].direction+1);p->source_us=p->pending[p->read].received_us;
        p->read=(uint8_t)((p->read+1)%UB_DIRECTION_PENDING);p->count--;
        p->phase=UB_DIRECTION_PRESS_WAIT;
    }
    if(p->phase==UB_DIRECTION_PRESS_WAIT || p->phase==UB_DIRECTION_HELD) {
        static const uint8_t hats[4]={0,4,6,2};
        frame->direction_level=p->level;frame->report.hat=hats[p->level-1];
    }
    frame->direction_key=direction_edge(p);
    if(frame->direction_key) { frame->transition=true;frame->received_us=p->source_us; }
}
void ub_direction_sent(ub_direction_pulse *p,uint8_t edge,uint32_t cookie) {
    if(edge && edge==direction_edge(p)) { p->cookie=cookie;p->awaiting=true; }
}
static void direction_duration(uint32_t duration,uint32_t *count,uint32_t *minimum,uint32_t *maximum) {
    if(!*count || duration<*minimum)*minimum=duration;
    if(duration>*maximum)*maximum=duration;
    (*count)++;
}
uint8_t ub_direction_complete(ub_direction_pulse *p,uint8_t edge,uint32_t cookie,uint32_t now,bool success) {
    if(!p->awaiting || p->cookie!=cookie || !edge || edge!=direction_edge(p))return 0;
    p->awaiting=false;
    if(!success)return 0;
    if(p->phase==UB_DIRECTION_PRESS_WAIT) {
        if(p->have_release)direction_duration(now-p->phase_at,&p->gap_samples,&p->gap_min,&p->gap_max);
        p->have_release=false;p->phase=UB_DIRECTION_HELD;
    } else {
        direction_duration(now-p->phase_at,&p->press_samples,&p->press_min,&p->press_max);
        p->have_release=true;p->phase=UB_DIRECTION_GAP;
    }
    p->phase_at=now;return edge;
}
