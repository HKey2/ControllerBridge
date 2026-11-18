#include "controller_paddle_point.h"
#include "analog_response.h"
#include <string.h>

#define PP_UNIT ((int64_t)UAR_UNIT)
#define PP_RAW_LIMIT (INT64_C(1)<<55)
#define PP_ERROR_LIMIT (INT64_C(1)<<61)

static int64_t pp_bound(int64_t v,int64_t n) { return v>n?n:v<-n?-n:v; }
static uint64_t pp_abs(int64_t v) { return (uint64_t)(v<0?-v:v); }
static uint32_t pp_elapsed(const ump_state *s,uint32_t now) {
    uint32_t n=s->have_ack?now-s->last_ack_us:0;return n<0x80000000u?n:0;
}
// Whole-rate integration with an exact remainder, without multiplying a long
// interval by a wide (up to signed-32-bit HID) source rate in one operation.
static void pp_spend(int64_t *whole,int64_t *remainder,int64_t rate,uint32_t dt) {
    int64_t n=rate*(dt%1000000u)+*remainder;
    *whole-=rate*(dt/1000000u)+n/1000000;
    *remainder=n%1000000;
}
static int64_t pp_error(const ump_state *s,uint32_t dt) {
    int64_t whole=s->pending,rem=s->raw_remainder;
    pp_spend(&whole,&rem,s->applied_raw_rate,dt);
    return pp_bound(whole,PP_ERROR_LIMIT/1000000)*1000000-rem;
}
static void pp_rebase(ump_state *s,int64_t error,uint32_t dt) {
    s->pending=error/1000000;s->raw_remainder=-(error%1000000);
    pp_spend(&s->pending,&s->raw_remainder,-s->applied_raw_rate,dt);
}
static uint32_t pp_range(const ump_state *s,int direction) {
    return direction<0?s->negative_range:s->positive_range;
}
static int64_t pp_full_rate(const ump_state *s,int direction) {
    return (int64_t)pp_range(s,direction)*1000000/s->native_period_us;
}
static uint32_t pp_until_ack(const ump_state *s,uint32_t now,uint32_t interval) {
    if(!s->have_ack)return interval;
    uint32_t dt=now-s->last_ack_us;
    return dt<0x80000000u?interval-dt%interval:interval;
}
static int32_t pp_response(const ump_state *s,int64_t raw_rate,const up_paddle_analog *c) {
    if(!raw_rate)return 0;
    int sign=raw_rate<0?-1:1;
    uint64_t numerator=pp_abs(raw_rate),denominator=s->reference_counts;
    if(!denominator) {
        // Uncharacterized relative wheels retain their native HID reference.
        numerator*=s->native_period_us;denominator=(uint64_t)pp_range(s,sign)*1000000u;
    }
    return (int32_t)uar_response(numerator,denominator,c->speed100,c->curve)*sign;
}
static void pp_quantize(int64_t response,int direction,const up_paddle_analog *c,ump_plan *p) {
    unsigned maximum=c->maximum_percent?c->maximum_percent:100u;
    unsigned span=maximum-c->minimum_percent;
    uint32_t width=direction<0?128u:127u;
    unsigned ceiling_axis=(maximum*width+99u)/100u;
    int64_t ceiling_n=(int64_t)(ceiling_axis*100u-c->minimum_percent*width)*PP_UNIT;
    int64_t ceiling=(ceiling_n+span*width-1)/(span*width);
    // The nearest representable pair may straddle the configured maximum by
    // one axis quantum. Carry the remainder between that pair, rather than
    // accumulating an impossible full-scale debt below the upper quantum.
    uint64_t value=pp_abs(pp_bound(response,ceiling));
    uint64_t physical=(uint64_t)c->minimum_percent*(uint64_t)PP_UNIT+span*value;
    unsigned axis=(unsigned)(physical*width/(100u*(uint64_t)PP_UNIT));
    int effective=(int)(axis*100u)-(int)(c->minimum_percent*width);
    p->axis=128;p->applied_response=0;
    if(!value || effective<=0)return;
    // The level floor itself is not part of Speed. Measure the represented
    // effective response above that floor for temporal output quantization.
    int64_t actual=(int64_t)effective*PP_UNIT/(span*width);
    p->axis=(uint8_t)(128+direction*(int)axis);
    p->applied_response=(int32_t)actual*direction;
}
static void pp_stop(ump_state *s,uint32_t now,const up_paddle_analog *c) {
    if(!s->moving)return;
    uint32_t interval=s->observed_us?s->observed_us:s->planned_interval_us;
    if(interval<1000u)interval=1000u;
    uint32_t delay=pp_until_ack(s,now,interval),dt=pp_elapsed(s,now);
    int64_t left=pp_error(s,dt+delay);
    if(left*s->direction<=0)left=0;
    unsigned tracking=(unsigned)c->smoothing_ms*1000u;
    if(!tracking)tracking=1000u;
    int64_t full=pp_full_rate(s,s->direction),speed=(int64_t)(pp_abs(left)/tracking);
    if(speed>full)speed=full;
    uint32_t maximum=(uint32_t)c->inertia10*10000u;maximum=maximum>delay?maximum-delay:0;
    uint64_t natural=speed?pp_abs(left)*2u/(uint64_t)speed:0;
    uint32_t duration=natural>maximum?maximum:(uint32_t)natural;
    int64_t rate=duration?left*2/duration:0;rate=pp_bound(rate,full);
    if(natural>maximum && pp_abs(left)>(uint64_t)full*tracking)
        pp_rebase(s,rate*duration/2,dt+delay);
    s->release_raw_rate=rate;s->release_at_us=now+delay;s->release_duration_us=duration;
    s->moving=false;s->decaying=duration!=0;s->cache_valid=false;
}
void ump_input(ump_state *s,int32_t raw,bool invert,int32_t minimum,int32_t maximum,
               uint32_t period,uint32_t reference_counts,uint32_t now,const up_paddle_analog *c) {
    if(s->have_report && (uint32_t)(now-s->last_report_us)>=0x80000000u)return;
    if(!s->native_period_us) {
        uint32_t positive=maximum>0?(uint32_t)maximum:1u;
        uint32_t negative=minimum<0?(uint32_t)(-(int64_t)minimum):positive;
        s->positive_range=invert?negative:positive;s->negative_range=invert?positive:negative;
        s->native_period_us=period<1000u?1000u:period>255000u?255000u:period;
        s->reference_counts=reference_counts;
    }
    s->last_report_us=now;s->have_report=true;
    if(!raw) { pp_stop(s,now,c);return; }
    int64_t delta=raw;if(invert)delta=-delta;
    int sign=delta<0?-1:1;
    if((s->moving || s->decaying) && s->direction && sign!=s->direction) {
        if(pp_abs(delta)<3u) { pp_stop(s,now,c);return; }
        pp_rebase(s,0,pp_elapsed(s,now));
        s->response_phase=0;s->requested_response=s->applied_response;
    }
    s->pending=pp_bound(s->pending+delta,PP_RAW_LIMIT);
    s->direction=(int8_t)sign;s->last_motion_us=now;
    s->moving=true;s->decaying=false;s->cache_valid=false;
}
void ump_prepare(ump_state *s,uint32_t now,uint32_t idle,uint32_t interval,
                 const up_paddle_analog *c,ump_plan *plan) {
    if(!s->native_period_us) { *plan=(ump_plan){.axis=128};return; }
    if(s->moving && (uint32_t)(now-s->last_motion_us)>=idle)pp_stop(s,now,c);
    if(interval<1000u)interval=1000u;
    if(s->observed_us)interval=s->observed_us;
    s->planned_interval_us=interval;
    if(!s->cache_valid || (uint32_t)(now-s->cache_us)>=1000u) {
        uint32_t dt=pp_elapsed(s,now),delay=pp_until_ack(s,now,interval),remaining=0;
        if(s->decaying) {
            int32_t age=(int32_t)(now+delay-s->release_at_us);if(age<0)age=0;
            if((uint32_t)age<s->release_duration_us)remaining=s->release_duration_us-(uint32_t)age;
            else s->decaying=false;
        }
        int64_t available=pp_error(s,dt+delay),rate=0;
        if(available*s->direction>0 && (s->moving || s->decaying)) {
            unsigned tracking=(unsigned)c->smoothing_ms*1000u;if(!tracking)tracking=1000u;
            rate=s->moving?available/tracking:s->release_raw_rate*remaining/s->release_duration_us;
            rate=pp_bound(rate,pp_full_rate(s,s->direction));
            int64_t limit=available/interval;
            if(pp_abs(rate)>pp_abs(limit))rate=limit;
        }
        ump_plan next={.raw_rate=rate,.requested_response=pp_response(s,rate,c),.axis=128};
        int64_t phase=s->response_phase+((int64_t)s->requested_response-s->applied_response)*dt;
        // Fractional host output survives an idle gap, but is emitted only
        // while this source is tracking/releasing, never as a late stray move.
        if(s->moving || s->decaying) {
            int64_t response=next.requested_response+phase/interval;
            if(response*s->direction<0)response=0;
            pp_quantize(response,s->direction,c,&next);
        }
        s->cached=next;s->cache_us=now;s->cache_valid=true;
    }
    *plan=s->cached;
}
void ump_ack(ump_state *s,const ump_plan *plan,uint32_t now) {
    if(s->have_ack) {
        uint32_t dt=now-s->last_ack_us;if(dt>=0x80000000u)return;
        pp_spend(&s->pending,&s->raw_remainder,s->applied_raw_rate,dt);
        s->response_phase+=((int64_t)s->requested_response-s->applied_response)*dt;
        if(dt>=1000u && dt<=100000u)s->observed_us=dt;
    }
    s->applied_raw_rate=plan->raw_rate;
    s->requested_response=plan->requested_response;s->applied_response=plan->applied_response;
    s->last_ack_us=now;s->have_ack=true;s->cache_valid=false;
}
