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
bool ub_queue_pop(ub_queue *q,ub_frame *f) {
    if(q->read==q->write)return false;
    *f=q->pending[q->read]; q->read=(q->read+1)%UB_TRANSITIONS; return true;
}
