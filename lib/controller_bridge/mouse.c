#include "controller_mouse.h"
#include <limits.h>
#include <string.h>

uint8_t um_route_buttons(uint8_t options,sw_report *pad) {
    if(options&UM_GAMEPAD_BUTTONS)return 0;
    uint8_t clicks=((pad->buttons0&SW_B)?1:0)|((pad->buttons0&SW_A)?2:0);
    if(options&UM_COMBINED)sw_navigation(pad,pad);
    return clicks;
}
void um_init(um_queue *q) { memset(q,0,sizeof(*q)); }
void um_flush(um_queue *q,uint8_t buttons) {
    if(um_pending(q))q->flushes++;
    q->read=q->write=q->count=q->pending_sources=0;q->buttons=buttons&3;q->epoch++;
    q->button_drops+=q->button_count;
    q->button_read=q->button_write=q->button_count=0;q->button_epoch++;
}
bool um_pending(const um_queue *q) { return q->count || q->button_count; }
int32_t um_scale(int32_t value,uint8_t options,bool y_axis) {
    int64_t scaled=(int64_t)value*(1+((options&UM_GAIN_MASK)>>1));
    if(options&(y_axis?UM_INVERT_Y:UM_INVERT_X))scaled=-scaled;
    if(scaled>INT32_MAX)return INT32_MAX;
    if(scaled<-INT32_MAX)return -INT32_MAX;
    return (int32_t)scaled;
}
static int32_t tenths(int64_t value,int32_t *fraction) {
    value+=*fraction;
    // Normal HID movement takes only 32-bit division by the constant 10.
    if(value>=INT32_MIN && value<=INT32_MAX) {
        int32_t scaled=(int32_t)value,whole=scaled/10;
        *fraction=scaled-whole*10;return whole;
    }
    int64_t whole=value/10;
    if(whole>INT32_MAX) { *fraction=0;return INT32_MAX; }
    if(whole<-INT32_MAX) { *fraction=0;return -INT32_MAX; }
    *fraction=(int32_t)(value-whole*10);return (int32_t)whole;
}
void um_mix(um_fraction *fraction,const up_mouse_options *o,int32_t raw_x,int32_t raw_y,int32_t wheel,int32_t *x,int32_t *y) {
    int64_t sx=0,sy=0;
    if(o->flags&UP_MOUSE_TRACKBALL) {
        unsigned gain=up_mouse_trackball_gain10(o);
        sx=(int64_t)raw_x*gain;sy=(int64_t)raw_y*gain;
        if(o->flags&UP_MOUSE_INVERT_X)sx=-sx;
        if(o->flags&UP_MOUSE_INVERT_Y)sy=-sy;
    }
    if(o->flags&UP_MOUSE_PADDLE) {
        int64_t paddle=-(int64_t)wheel*o->paddle_gain10;
        if(o->flags&UP_MOUSE_INVERT_PADDLE)paddle=-paddle;
        sx+=paddle;
    }
    *x=tenths(sx,&fraction->x10);*y=tenths(sy,&fraction->y10);
}
void um_fixed_delta(const up_mouse_options *o,int32_t raw_x,int32_t raw_y,unsigned threshold,unsigned amplitude,int32_t *x,int32_t *y) {
    *x=*y=0;
    if(!(o->flags&UP_MOUSE_TRACKBALL) || !threshold || !amplitude || amplitude>32767)return;
    int64_t sx=raw_x,sy=raw_y;
    if(o->flags&UP_MOUSE_INVERT_X)sx=-sx;
    if(o->flags&UP_MOUSE_INVERT_Y)sy=-sy;
    int64_t ax=sx<0?-sx:sx,ay=sy<0?-sy:sy,size=ax>ay?ax:ay;
    if(size<threshold)return;
    *x=(int32_t)(sx*amplitude/size);*y=(int32_t)(sy*amplitude/size);
}
void um_push(um_queue *q,int32_t x,int32_t y,uint8_t buttons,uint32_t time,uint32_t sequence,bool source_input) {
    buttons&=3;
    if(source_input)q->inputs++;
    if(x || y)q->movement_inputs++;
    q->input_x+=(uint32_t)x;q->input_y+=(uint32_t)y;
    if(q->buttons!=buttons) {
        q->button_inputs++;
        if(q->button_count==UM_QUEUE_CAPACITY) {
            // Button overload is independent of motion overload. Keep the
            // newest physical state (especially release) and count the loss.
            q->button_drops+=q->button_count;
            q->button_read=q->button_write=q->button_count=0;q->button_epoch++;
        }
        q->button_frames[q->button_write]=(um_button_frame){time,sequence,buttons};
        q->button_write=(q->button_write+1)%UM_QUEUE_CAPACITY;q->button_count++;
        if(q->button_count>q->button_max_depth)q->button_max_depth=q->button_count;
    }
    q->buttons=buttons;
    if(!x && !y)return;
    if(q->pending_sources==UM_QUEUE_CAPACITY) {
        // Discard excessive old motion without discarding pending clicks.
        // The motion epoch invalidates an outstanding packet's motion commit,
        // while its separately tracked button ACK remains valid.
        q->drops+=q->pending_sources;q->flushes++;
        q->read=q->write=q->count=q->pending_sources=0;q->epoch++;
    }
    q->frames[q->write]=(um_frame){x,y,time,sequence,1};
    q->write=(q->write+1)%UM_QUEUE_CAPACITY;q->count++;
    q->pending_sources++;
    if(q->pending_sources>q->max_depth)q->max_depth=q->pending_sources;
}
void um_next(const um_queue *q,um_flight *f) {
    um_next_wide(q,f,false);
}
static void next_batch(const um_queue *q,um_flight *f,bool wide,unsigned batch) {
    *f=(um_flight){.report={q->buttons,0,0},.epoch=q->epoch,.button_epoch=q->button_epoch,.wide=wide};
    int32_t maximum=wide?32767:127;
    if(q->button_count) {
        const um_button_frame *button=&q->button_frames[q->button_read];
        f->report.buttons=button->buttons;
        f->button_time=f->time=button->time;f->button_sequence=f->sequence=button->sequence;
        f->button_queued=f->queued=true;
    }
    if(!q->count)return;
    const um_frame *front=&q->frames[q->read];
    f->time=front->time;f->sequence=front->sequence;f->queued=true;
    int64_t x=front->x,y=front->y;unsigned parts=1;
    const um_frame *last=front;
    while(parts<q->count && parts<batch) {
        const um_frame *next=&q->frames[(q->read+parts)%UM_QUEUE_CAPACITY];
        int64_t sx=x+next->x,sy=y+next->y;
        // An 8 ms host poll can cover eight 1 ms source samples. Combine only
        // already pending relative motion; never wait or merge click edges.
        // Retain the old two-entry bound for the 8-bit Boot protocol.
        if(sx<INT32_MIN || sx>INT32_MAX || sy<INT32_MIN || sy>INT32_MAX)break;
        x=sx;y=sy;last=next;parts++;
    }
    int64_t ax=x<0?-x:x,ay=y<0?-y:y,size=ax>ay?ax:ay;
    if(size>maximum) {
        int64_t sent_x=x*maximum/size,sent_y=y*maximum/size;
        f->complete_frames=parts-1;
        // On ACK, remove all but the last selected entry and leave sum-sent
        // there. Preparation is read-only, including the source accounting.
        f->partial_x=sent_x-(x-last->x);
        f->partial_y=sent_y-(y-last->y);
        f->merged_motion=parts>1 && (f->partial_x || f->partial_y);
        x=sent_x;y=sent_y;
    } else f->complete_frames=parts;
    f->report.x=(int16_t)x;f->report.y=(int16_t)y;
}
void um_next_wide(const um_queue *q,um_flight *f,bool wide) { next_batch(q,f,wide,wide?8u:2u); }
void um_next_single(const um_queue *q,um_flight *f,bool wide) { next_batch(q,f,wide,1); }
void um_ack(um_queue *q,const um_flight *f) {
    q->acked_x+=(uint32_t)(int32_t)f->report.x;q->acked_y+=(uint32_t)(int32_t)f->report.y;
    if(f->report.x || f->report.y)q->movement_acks++;
    if(q->acked_buttons!=f->report.buttons)q->button_acks++;
    q->acked_buttons=f->report.buttons;
    if(f->button_queued && f->button_epoch==q->button_epoch && q->button_count &&
       q->button_frames[q->button_read].sequence==f->button_sequence) {
        q->button_read=(q->button_read+1)%UM_QUEUE_CAPACITY;q->button_count--;
    }
    if(!f->queued || f->epoch!=q->epoch)return;
    if(f->complete_frames<=q->count) {
        uint32_t sources=0;
        for(unsigned i=0;i<f->complete_frames;++i)sources+=q->frames[(q->read+i)%UM_QUEUE_CAPACITY].sources;
        q->read=(q->read+f->complete_frames)%UM_QUEUE_CAPACITY;q->count-=f->complete_frames;
        if(f->merged_motion && q->count)q->frames[q->read].sources+=sources;
        else q->pending_sources-=sources;
        if(q->count && (f->partial_x || f->partial_y)) {
            q->frames[q->read].x=(int32_t)(q->frames[q->read].x-f->partial_x);
            q->frames[q->read].y=(int32_t)(q->frames[q->read].y-f->partial_y);
            if(f->merged_motion) {
                q->frames[q->read].time=f->time;q->frames[q->read].sequence=f->sequence;
            }
        }
    }
}
