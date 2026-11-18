#include "controller_mouse.h"
#include "analog_response.h"
#include <string.h>

// The existing Trackball reference is 240 native counts per second. Curve and
// personal Speed act after pursuit in that reference, without clipping it.
#define VP_REFERENCE 240u
#define VP_LIMIT (INT64_C(1)<<55)
#define VP_ERROR_LIMIT (INT64_C(1)<<61)
// Covers simultaneous signed-32-bit X/Y reports every millisecond, while
// bounding rate * microseconds below INT64_MAX even for extreme backlogs.
#define VP_RAW_RATE_LIMIT INT64_C(4294967296000)
#define VP_REVERSE_COUNTS 3u

static int64_t bound(int64_t v,int64_t limit) { return v>limit?limit:v<-limit?-limit:v; }
static uint64_t magnitude(int64_t v) { return (uint64_t)(v<0?-v:v); }
static bool virtual_options_encoded(const up_conversion *c) {
    return (c->analog_curve==20 || c->analog_curve==30) && c->analog_full_speed>=60001 && c->analog_full_speed<=65000;
}
static unsigned tracking_us(const up_conversion *c) {
    unsigned ms=virtual_options_encoded(c)?c->analog_min_speed10:10u;return (ms?ms:10u)*1000u;
}
static unsigned duration_us(const up_conversion *c) {
    return virtual_options_encoded(c) && c->analog_curve==20 ? (unsigned)c->analog_return10*10000u : 250000u;
}
static uint16_t gain100(const up_conversion *c) {
    return (uint16_t)(virtual_options_encoded(c)?c->analog_full_speed-60000u:100u);
}
static uint32_t elapsed_ack(const um_analog_state *s,uint32_t now) {
    uint32_t dt=s->have_ack?now-s->last_ack_us:0;return dt<0x80000000u?dt:0;
}
static void spend(int64_t *whole,int64_t *remainder,int64_t rate,uint32_t dt) {
    int64_t n=rate*(dt%1000000u)+*remainder;
    *whole-=rate*(dt/1000000u)+n/1000000;*remainder=n%1000000;
}
static int64_t error_at(const um_analog_state *s,unsigned i,uint32_t dt) {
    int64_t whole=s->pending[i],rem=s->integral_remainder[i];
    spend(&whole,&rem,s->applied_raw_rate[i],dt);
    return bound(whole,VP_ERROR_LIMIT/1000000)*1000000-rem;
}
static void rebase_error(um_analog_state *s,unsigned i,int64_t error,uint32_t dt) {
    s->pending[i]=error/1000000;s->integral_remainder[i]=-(error%1000000);
    spend(&s->pending[i],&s->integral_remainder[i],-s->applied_raw_rate[i],dt);
}
static uint64_t isqrt(uint64_t value) {
    uint64_t root=0,bit=UINT64_C(1)<<62;
    while(bit>value)bit>>=2;
    while(bit) {
        if(value>=root+bit) { value-=root+bit;root=(root>>1)+bit; }
        else root>>=1;
        bit>>=2;
    }
    return root;
}
static uint64_t vector_length(const int64_t v[2]) {
    uint64_t x=magnitude(v[0]),y=magnitude(v[1]);
    if(!x)return y;
    if(!y)return x;
    unsigned shift=0;
    while(x>0x3fffffffu || y>0x3fffffffu) { x>>=1;y>>=1;++shift; }
    uint64_t squared=x*x+y*y,length=isqrt(squared);
    if(length*length<squared)++length;
    return length<<shift;
}
// Exact multiply/divide by a 32-bit fraction <= 1, without a wide product.
static int64_t fraction(int64_t n,uint32_t a,uint32_t b) {
    return n/(int64_t)b*a+(n%(int64_t)b)*a/(int64_t)b;
}
static void clamp_raw(int64_t rate[2]) {
    uint64_t length=vector_length(rate);
    if(length<=(uint64_t)VP_RAW_RATE_LIMIT)return;
    for(unsigned i=0;i<2;++i) {
        uint32_t ratio=uar_fraction(magnitude(rate[i]),length);
        rate[i]=(VP_RAW_RATE_LIMIT/UAR_UNIT*ratio+
            VP_RAW_RATE_LIMIT%UAR_UNIT*ratio/UAR_UNIT)*(rate[i]<0?-1:1);
    }
}
static uint32_t until_ack(const um_analog_state *s,uint32_t now,uint32_t interval) {
    if(!s->have_ack)return interval;
    uint32_t dt=now-s->last_ack_us;return dt<0x80000000u?interval-dt%interval:interval;
}
static void stop(um_analog_state *s,uint32_t now,const up_conversion *c) {
    if(!s->moving)return;
    unsigned interval=s->observed_us?s->observed_us:s->planned_interval_us;
    if(interval<1000u)interval=1000u;
    unsigned delay=until_ack(s,now,interval);uint32_t dt=elapsed_ack(s,now);
    int64_t left[2],rate[2];
    for(unsigned i=0;i<2;++i) {
        int64_t e=error_at(s,i,dt+delay);
        left[i]=e*s->direction[i]>0?e:0;
    }
    uint64_t distance=vector_length(left),speed=distance/tracking_us(c);
    if(speed>(uint64_t)VP_RAW_RATE_LIMIT)speed=VP_RAW_RATE_LIMIT;
    unsigned maximum=duration_us(c);maximum=maximum>delay?maximum-delay:0;
    uint64_t natural=speed?distance*2u/speed:0;
    unsigned duration=(unsigned)(natural>maximum?maximum:natural);
    for(unsigned i=0;i<2;++i)rate[i]=duration?left[i]*2/duration:0;
    clamp_raw(rate);
    if(natural>maximum && distance>(uint64_t)VP_RAW_RATE_LIMIT*tracking_us(c))
        for(unsigned i=0;i<2;++i) {
            int64_t kept=rate[i]/2*duration+(rate[i]%2)*duration/2;
            rebase_error(s,i,kept,dt+delay);
        }
    memcpy(s->release_raw_rate,rate,sizeof(rate));
    s->release_at_us=now+delay;s->release_duration_us=duration;
    s->moving=false;s->decaying=duration>0;s->cache_valid=false;
}
static void discard_axis_backlog(um_analog_state *s,unsigned i,uint32_t now) {
    rebase_error(s,i,0,elapsed_ack(s,now));
    s->response_phase[i]=0;s->requested_response[i]=s->applied_response[i];
}
static void direction_vector(const int64_t v[2],int64_t out[2]) {
    uint64_t largest=magnitude(v[0]);if(magnitude(v[1])>largest)largest=magnitude(v[1]);
    int64_t divisor=(int64_t)((largest+16382u)/16383u);if(!divisor)divisor=1;
    out[0]=v[0]/divisor;out[1]=v[1]/divisor;
}
static bool reversal_input(um_analog_state *s,const int64_t raw[2],uint32_t now) {
    bool tracking=(s->moving || s->decaying) && (s->heading[0] || s->heading[1]);
    int64_t x[2],h[2];direction_vector(raw,x);direction_vector(s->heading,h);
    int64_t dot=x[0]*h[0]+x[1]*h[1],cross=x[0]*h[1]-x[1]*h[0];
    bool opposite=tracking && dot<0 && 3*cross*cross<=dot*dot;
    if(opposite) {
        if(magnitude(raw[0])<VP_REVERSE_COUNTS && magnitude(raw[1])<VP_REVERSE_COUNTS)return false;
        for(unsigned i=0;i<2;++i)discard_axis_backlog(s,i,now);
    }
    if(!tracking || opposite)for(unsigned i=0;i<2;++i)s->heading[i]=raw[i]*20000;
    else {
        uint32_t dt=now-s->last_motion_us;
        for(unsigned i=0;i<2;++i)s->heading[i]+=fraction(raw[i]*20000-s->heading[i],dt,20000u+dt);
    }
    return true;
}
// Use enough integer precision for low-speed diagonals before taking length.
// For wide rates reduce both components together, retaining the scale in the
// normalization ratio. Direction and magnitude never depend on UI Speed.
static void response(const int64_t rate[2],uint16_t gain,uint8_t curve,int32_t out[2]) {
    int64_t v[2]={rate[0],rate[1]};unsigned up=0,down=0;
    uint64_t largest=magnitude(v[0]);if(magnitude(v[1])>largest)largest=magnitude(v[1]);
    if(!largest) { out[0]=out[1]=0;return; }
    while(largest>0x3fffffffu) { v[0]/=2;v[1]/=2;largest>>=1;++down; }
    while(largest<0x10000000u && up<20u) { v[0]*=2;v[1]*=2;largest*=2;++up; }
    uint64_t length=vector_length(v);
    uint32_t radial=uar_response(length<<down,(uint64_t)VP_REFERENCE<<up,gain,curve);
    for(unsigned i=0;i<2;++i)out[i]=(int32_t)(v[i]*radial/(int64_t)length);
}
static void quantize(const int64_t desired[2],const up_analog_levels *levels,um_analog_plan *plan) {
    uint64_t length=vector_length(desired);
    if(!length)return;
    unsigned minimum=levels?levels->minimum_percent:0;
    unsigned span=(levels?up_analog_maximum(levels):100u)-minimum;
    // One two-axis quantum may straddle the requested maximum. Preserve its
    // effective area in the ACK phase rather than accumulating unreachable debt.
    uint64_t ceiling=UAR_UNIT+((uint64_t)UAR_UNIT*142u+127u*span-1)/(127u*span);
    uint64_t radial=length>ceiling?ceiling:length;
    uint64_t physical=((uint64_t)minimum*UAR_UNIT+span*radial)/100u;
    int64_t represented[2];
    for(unsigned i=0;i<2;++i) {
        unsigned width=desired[i]<0?128u:127u;
        uint32_t ratio=uar_fraction(magnitude(desired[i]),length);
        uint64_t component=physical*ratio/UAR_UNIT;
        unsigned n=(unsigned)(component*width/UAR_UNIT);if(n>width)n=width;
        int sign=desired[i]<0?-1:1;
        plan->axis[i]=(uint8_t)(128+sign*(int)n);
        represented[i]=(int64_t)n*UAR_UNIT/width*sign;
    }
    uint64_t actual=vector_length(represented);
    if(actual*100u<=(uint64_t)minimum*UAR_UNIT) {
        plan->axis[0]=plan->axis[1]=128;return;
    }
    uint64_t effective=(actual*100u-(uint64_t)minimum*UAR_UNIT)/span;
    for(unsigned i=0;i<2;++i)
        plan->applied_response[i]=(int32_t)(represented[i]*(int64_t)effective/(int64_t)actual);
}
void um_analog_input(um_analog_state *s,const up_mouse_options *mouse,int32_t raw_x,int32_t raw_y,
                     uint32_t now,uint32_t period,uint32_t idle,const up_conversion *c) {
    (void)period;(void)idle;
    if(s->have_report && (uint32_t)(now-s->last_report_us)>=0x80000000u)return;
    s->last_report_us=now;s->have_report=true;
    if(!raw_x && !raw_y) { stop(s,now,c);return; }
    int64_t raw[2]={raw_x,raw_y};
    if(mouse->flags&UP_MOUSE_INVERT_X)raw[0]=-raw[0];
    if(mouse->flags&UP_MOUSE_INVERT_Y)raw[1]=-raw[1];
    if(!reversal_input(s,raw,now)) { stop(s,now,c);return; }
    uint32_t dt=elapsed_ack(s,now);
    for(unsigned i=0;i<2;++i) {
        s->pending[i]=bound(s->pending[i]+raw[i],VP_LIMIT);
        int64_t e=error_at(s,i,dt);int sign=e>0?1:e<0?-1:0;
        if(sign && s->direction[i] && sign!=s->direction[i])s->response_phase[i]=0;
        if(sign)s->direction[i]=(int8_t)sign;
    }
    s->last_motion_us=now;s->moving=true;s->decaying=false;s->cache_valid=false;
}
void um_analog_prepare(um_analog_state *s,uint32_t now,uint32_t idle,uint32_t horizon,
                       const up_conversion *c,const up_analog_levels *levels,sw_report *report,um_analog_plan *plan) {
    if(s->moving && (uint32_t)(now-s->last_motion_us)>=idle)stop(s,now,c);
    if(horizon<1000u)horizon=1000u;
    if(s->observed_us)horizon=s->observed_us;
    s->planned_interval_us=horizon;
    if(!s->cache_valid || (uint32_t)(now-s->cache_us)>=1000u) {
        um_analog_plan next={.axis={128,128}};
        unsigned delay=until_ack(s,now,horizon);uint32_t dt=elapsed_ack(s,now),remaining=0;
        if(s->decaying) {
            int32_t age=(int32_t)(now+delay-s->release_at_us);if(age<0)age=0;
            if((uint32_t)age<s->release_duration_us)remaining=s->release_duration_us-(uint32_t)age;
            else s->decaying=false;
        }
        for(unsigned i=0;i<2;++i) {
            int64_t available=error_at(s,i,dt+delay);
            if(available*s->direction[i]>0 && (s->moving || s->decaying)) {
                int64_t rate=s->moving?available/tracking_us(c):
                    fraction(s->release_raw_rate[i],remaining,s->release_duration_us);
                int64_t limit=available/horizon;
                next.raw_rate[i]=magnitude(rate)>magnitude(limit)?limit:rate;
            }
        }
        clamp_raw(next.raw_rate);
        response(next.raw_rate,gain100(c),levels?levels->curve:0,next.requested_response);
        if(s->moving || s->decaying) {
            int64_t desired[2]={0};
            for(unsigned i=0;i<2;++i)if(next.raw_rate[i]) {
                int64_t phase=s->response_phase[i]+((int64_t)s->requested_response[i]-s->applied_response[i])*dt;
                desired[i]=next.requested_response[i]+phase/horizon;
                if(desired[i]*s->direction[i]<0)desired[i]=0;
            }
            quantize(desired,levels,&next);
        }
        s->cached=next;s->cache_us=now;s->cache_valid=true;
    }
    *plan=s->cached;
    if(c->analog_flags&UP_ANALOG_RIGHT) { report->rx=plan->axis[0];report->ry=plan->axis[1]; }
    else { report->lx=plan->axis[0];report->ly=plan->axis[1]; }
}
void um_analog_ack(um_analog_state *s,const um_analog_plan *plan,uint32_t now) {
    if(s->have_ack) {
        uint32_t dt=now-s->last_ack_us;if(dt>=0x80000000u)return;
        for(unsigned i=0;i<2;++i) {
            spend(&s->pending[i],&s->integral_remainder[i],s->applied_raw_rate[i],dt);
            s->response_phase[i]+=((int64_t)s->requested_response[i]-s->applied_response[i])*dt;
        }
        if(dt>=1000u && dt<=100000u)s->observed_us=dt;
    }
    memcpy(s->applied_raw_rate,plan->raw_rate,sizeof(s->applied_raw_rate));
    memcpy(s->requested_response,plan->requested_response,sizeof(s->requested_response));
    memcpy(s->applied_response,plan->applied_response,sizeof(s->applied_response));
    s->last_ack_us=now;s->have_ack=true;s->cache_valid=false;
}
