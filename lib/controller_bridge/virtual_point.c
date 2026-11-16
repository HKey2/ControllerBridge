#include "controller_mouse.h"
#include <math.h>
#include <string.h>
#include "analog_curves.h"

#define VP_Q 65536
#define VP_SPEED (240 * VP_Q)
// Arithmetic overflow guard. The finite-stop budget is enforced at release.
#define VP_LIMIT (INT64_C(1)<<55)
// Require a strong raw report within 30 degrees of the opposite source
// direction, independently of Speed. Changing axes or taking a corner must
// retain the target. Small opposing reports do not restart the release tail.
#define VP_REVERSE_COUNTS 3u
static bool virtual_options_encoded(const up_conversion *c) {
    return (c->analog_curve==20 || c->analog_curve==30) && c->analog_full_speed>=60001 && c->analog_full_speed<=65000;
}
static unsigned duration_us(const up_conversion *c) {
    return virtual_options_encoded(c) && c->analog_curve==20 ? (unsigned)c->analog_return10*10000u : 250000u;
}
static int64_t bound(int64_t v,int64_t limit) { return v>limit?limit:v<-limit?-limit:v; }
static int64_t dose(int32_t rate,uint32_t us,int32_t *remainder) {
    int64_t n=(int64_t)rate*us+*remainder;
    *remainder=(int32_t)(n%1000000);return n/1000000;
}
static int32_t axis_rate(uint8_t axis) {
    int32_t n=(int)axis-128;
    return (int32_t)((int64_t)n*VP_SPEED/(n<0?128:127));
}
static void predict(const um_analog_state *s,uint32_t now,int64_t pending[2],int64_t phase[2]) {
    uint32_t dt=s->have_ack?now-s->last_ack_us:0;
    if(dt>=0x80000000u)dt=0;
    for(unsigned i=0;i<2;++i) {
        int32_t r=s->integral_remainder[i],q=s->phase_remainder[i];
        pending[i]=s->pending[i]-dose(s->applied[i],dt,&r);
        phase[i]=s->phase[i]+dose(s->requested[i]-s->applied[i],dt,&q);
    }
}
static void clamp_vector(float rate[2]) {
    float norm=sqrtf(rate[0]*rate[0]+rate[1]*rate[1]);
    if(norm>VP_SPEED) { float scale=VP_SPEED/norm;rate[0]*=scale;rate[1]*=scale; }
}
static uint32_t level_curve(uint32_t strength,uint8_t code) {
    code=up_analog_curve_code(code);
    if(!code || strength>=32768u)return strength;
    unsigned row;
    switch(code) {
    case 4:row=0;break;case 6:row=1;break;case 8:row=2;break;
    case 12:row=3;break;case 14:row=4;break;case 16:row=5;break;
    case 20:row=6;break;case 30:row=7;break;default:return strength;
    }
    unsigned index=strength>>7,part=strength&127u;
    const uint16_t *values=analog_curves[row];
    uint32_t curved=values[index]+((values[index+1]-values[index])*part+64u)/128u;
    return curved>strength*4u?strength*4u:curved;
}
static void apply_levels(const up_analog_levels *levels,uint8_t logical[2],uint8_t output[2]) {
    memcpy(output,logical,2);
    // Neutral level settings preserve the logical output directly and avoid
    // unnecessary curve and magnitude calculations.
    if(!levels || (!levels->minimum_percent && up_analog_maximum(levels)==100u &&
       !up_analog_curve_code(levels->curve)))return;
    float v[2];
    for(unsigned i=0;i<2;++i) { int n=(int)logical[i]-128;v[i]=n/(n<0?128.0f:127.0f); }
    float magnitude=sqrtf(v[0]*v[0]+v[1]*v[1]);
    if(magnitude==0)return; // A level floor must never move a neutral stick.
    uint32_t strength=magnitude>=1?32768u:(uint32_t)(magnitude*32768.0f);
    float level=(levels->minimum_percent+(up_analog_maximum(levels)-levels->minimum_percent)*
        (level_curve(strength,levels->curve)/32768.0f))/100.0f;
    for(unsigned i=0;i<2;++i) {
        float axis=v[i]*level/magnitude;
        int n=(int)roundf(axis*(axis<0?128.0f:127.0f));
        if(n>127)n=127;
        if(n<-128)n=-128;
        output[i]=(uint8_t)(128+n);
        // A suppressed physical component must not consume an unsent logical
        // displacement. Its fractional backlog remains available to new input.
        if(!n)logical[i]=128;
    }
}
static uint32_t until_ack(const um_analog_state *s,uint32_t now,uint32_t interval) {
    if(!s->have_ack)return interval;
    uint32_t dt=now-s->last_ack_us;
    if(dt>=0x80000000u)return interval;
    return interval-dt%interval;
}
static unsigned tracking_ms(const up_conversion *c) {
    unsigned ms=virtual_options_encoded(c)?c->analog_min_speed10:10u;return ms?ms:10u;
}
static void stop(um_analog_state *s,uint32_t now,const up_conversion *c) {
    if(!s->moving)return;
    int64_t pending[2],phase[2];predict(s,now,pending,phase);
    unsigned interval=s->observed_us?s->observed_us:s->planned_interval_us;
    if(interval<1000u)interval=1000u;
    unsigned delay=until_ack(s,now,interval);
    float left[2];
    for(unsigned i=0;i<2;++i) {
        int32_t r=0;int64_t available=pending[i]-dose(s->applied[i],delay,&r);
        left[i]=available*s->direction[i]>0?(float)available:0;
    }
    float distance=sqrtf(left[0]*left[0]+left[1]*left[1]);
    float speed=distance*1000.0f/tracking_ms(c);if(speed>VP_SPEED)speed=VP_SPEED;
    unsigned maximum=duration_us(c);maximum=maximum>delay?maximum-delay:0;
    float duration=speed>0?2.0f*distance/speed*1000000.0f:0;
    if(duration>maximum) {
        // Preserve the already held output. Only future backlog exceeding the
        // linear release budget is compressed, never a weak-input threshold.
        float scale=maximum/duration;
        for(unsigned i=0;i<2;++i)s->pending[i]+=(int64_t)(left[i]*scale)-(int64_t)left[i];
        duration=(float)maximum;
    }
    for(unsigned i=0;i<2;++i)s->release_rate[i]=distance>0?left[i]/distance*speed:0;
    s->release_at_us=now+delay;s->release_duration_us=duration;
    s->moving=false;s->decaying=duration>0;s->cache_valid=false;
}
static void discard_axis_backlog(um_analog_state *s,unsigned i,uint32_t now) {
    // pending is referenced to the last successful ACK. Retain the displacement
    // already emitted since that ACK so predict(now) sees exactly zero here.
    // The old held output between now and the reversal ACK is still accounted.
    uint32_t dt=s->have_ack?now-s->last_ack_us:0;
    if(dt>=0x80000000u)dt=0;
    int32_t remainder=s->integral_remainder[i];
    s->pending[i]=dose(s->applied[i],dt,&remainder);
    s->gain_remainder[i]=0;s->phase[i]=0;s->phase_remainder[i]=0;
    s->requested[i]=s->applied[i];
}
static bool reversal_input(um_analog_state *s,int64_t raw[2],uint32_t now) {
    float x=(float)raw[0],y=(float)raw[1];
    bool tracking=(s->moving || s->decaying) && (s->heading[0]!=0 || s->heading[1]!=0);
    float dot=x*s->heading[0]+y*s->heading[1];
    float cross=x*s->heading[1]-y*s->heading[0];
    bool opposite=tracking && dot<0 && fabsf(cross)<=-dot*0.577350269f;
    if(opposite) {
        if(fabsf(x)<VP_REVERSE_COUNTS && fabsf(y)<VP_REVERSE_COUNTS)return false;
        for(unsigned i=0;i<2;++i)discard_axis_backlog(s,i,now);
    }
    if(!tracking || opposite) { s->heading[0]=x;s->heading[1]=y; }
    else {
        // Weight by elapsed source time. An isolated orthogonal count does not
        // replace a sustained heading; an ordinary corner follows new motion.
        uint32_t dt=now-s->last_motion_us;
        float weight=(float)dt/(20000.0f+dt);
        s->heading[0]+=(x-s->heading[0])*weight;s->heading[1]+=(y-s->heading[1])*weight;
    }
    return true;
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
    unsigned gain=virtual_options_encoded(c)?c->analog_full_speed-60000u:100u;
    for(unsigned i=0;i<2;++i) {
        int64_t n=raw[i]*gain*VP_Q+s->gain_remainder[i];
        s->pending[i]=bound(s->pending[i]+n/100,VP_LIMIT);
        s->gain_remainder[i]=(int32_t)(n%100);
    }
    int64_t pending[2],phase[2];predict(s,now,pending,phase);
    for(unsigned i=0;i<2;++i) {
        int sign=pending[i]>0?1:pending[i]<0?-1:0;
        if(sign!=s->direction[i])s->phase[i]=s->phase_remainder[i]=0;
        s->direction[i]=(int8_t)sign;
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
        int64_t pending[2],phase[2];predict(s,now,pending,phase);
        float base[2]={0},rate[2]={0};
        unsigned delay=until_ack(s,now,horizon);
        float factor=0;
        if(s->decaying) {
            float elapsed=(float)(int32_t)(now+delay-s->release_at_us);
            if(elapsed<0)elapsed=0;
            if(elapsed<s->release_duration_us)factor=1.0f-elapsed/s->release_duration_us;
            else s->decaying=false;
        }
        for(unsigned i=0;i<2;++i) {
            int sign=s->direction[i];
            // Predict only until the next ACK, not another full interval on
            // every preview. Repeated previews must not amplify reversals. This
            // does not commit displacement: only successful ACK does that.
            int32_t r=0;
            int64_t available=pending[i]-dose(s->applied[i],delay,&r);
            if(sign && available*sign>0 && (s->moving || s->decaying)) {
                base[i]=s->moving?(float)available*1000.0f/tracking_ms(c):s->release_rate[i]*factor;
                float limit=(float)available*1000000.0f/horizon;
                if(base[i]*sign>limit*sign)base[i]=limit;
                int64_t phase_limit=(int64_t)VP_SPEED*horizon/127/1000000;
                rate[i]=base[i]+(float)bound(phase[i],phase_limit)*1000000.0f/horizon;
                if(rate[i]*sign<0)rate[i]=0;
                if(rate[i]*sign>limit*sign)rate[i]=limit;
            }
        }
        clamp_vector(base);clamp_vector(rate);
        for(unsigned i=0;i<2;++i) {
            int n=(int)(rate[i]*(rate[i]<0?128.0f:127.0f)/VP_SPEED);
            if(n>127)n=127;
            if(n<-128)n=-128;
            s->cached.requested[i]=(int32_t)base[i];s->cached.axis[i]=(uint8_t)(128+n);
        }
        apply_levels(levels,s->cached.axis,s->cached.output_axis);
        s->cache_us=now;s->cache_valid=true;
    }
    memcpy(plan->requested,s->cached.requested,sizeof(plan->requested));
    memcpy(plan->axis,s->cached.axis,sizeof(plan->axis));
    if(c->analog_flags&UP_ANALOG_RIGHT) { report->rx=s->cached.output_axis[0];report->ry=s->cached.output_axis[1]; }
    else { report->lx=s->cached.output_axis[0];report->ly=s->cached.output_axis[1]; }
}
void um_analog_ack(um_analog_state *s,const um_analog_plan *plan,uint32_t now) {
    if(s->have_ack) {
        uint32_t dt=now-s->last_ack_us;
        if(dt>=0x80000000u)return;
        // Failed/cancelled transfers do not call here. The next successful ACK
        // integrates the entire interval for which the host held the old value.
        for(unsigned i=0;i<2;++i) {
            s->pending[i]-=dose(s->applied[i],dt,&s->integral_remainder[i]);
            s->phase[i]+=dose(s->requested[i]-s->applied[i],dt,&s->phase_remainder[i]);
            int64_t limit=(int64_t)VP_SPEED*(dt>100000u?100000u:dt)/127/1000000;
            s->phase[i]=bound(s->phase[i],limit);
        }
        if(dt>=1000u && dt<=100000u)s->observed_us=dt;
    }
    for(unsigned i=0;i<2;++i) { s->applied[i]=axis_rate(plan->axis[i]);s->requested[i]=plan->requested[i]; }
    s->last_ack_us=now;s->have_ack=true;s->cache_valid=false;
}
