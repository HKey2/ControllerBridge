#include "controller_gyro.h"
#include <string.h>

static int32_t maximum(int32_t a,int32_t b) { return a>b?a:b; }
static int32_t minimum(int32_t a,int32_t b) { return a<b?a:b; }
static int32_t magnitude(int32_t a) { return a<0?-a:a; }
static uint32_t advance(uint32_t a,uint32_t b,uint32_t limit) { return b>=limit-a?limit:a+b; }
static int32_t band(const up_gyro *g) { return maximum(8*256,minimum(50*256,g->noise_q8*2+2*256)); }
static void stop(up_gyro *g) { g->rate_q8=0;g->moving=false;g->motion_us=0; }
static void forget_window(up_gyro *g) { g->samples=0;g->quiet_sum=0;g->quiet_us=0; }
void up_gyro_init(up_gyro *g,int32_t bias_q8,uint16_t noise_q8) {
    memset(g,0,sizeof(*g));g->bias_q8=bias_q8;g->noise_q8=noise_q8;
}
void up_gyro_begin_calibration(up_gyro *g) {
    g->manual=true;g->seen=false;forget_window(g);stop(g);
}
void up_gyro_recenter(up_gyro *g) { g->pending_recenter=true; }
static void learn(up_gyro *g,bool noise) {
    g->bias_q8=(int32_t)((g->quiet_sum*256+g->samples/2)/g->samples);
    if(noise)g->noise_q8=minimum(50*256,maximum(2*256,
        maximum(g->bias_q8-g->low*256,g->high*256-g->bias_q8)+256));
    stop(g);
}
up_gyro_step up_gyro_update(up_gyro *g,uint16_t raw,const uint16_t accel[3],
                          bool accel_valid,uint32_t time,uint32_t maximum_gap,bool held,bool allow_learning) {
    up_gyro_step result={0};
    if(!allow_learning)forget_window(g);
    bool recenter=g->pending_recenter || (held && !g->held);
    if(held!=g->held) { g->held_us=0;g->hold_done=false; }
    g->held=held;g->pending_recenter=false;
    if(recenter) {
        if(g->quiet_us>=500000 && g->samples>=4 && !g->moving)learn(g,false);
        stop(g);result.recentered=true;
    }
    if(raw<32 || raw>991) {
        g->seen=false;g->reliable=false;forget_window(g);stop(g);return result;
    }
    uint32_t elapsed=g->seen?time-g->time:0;
    if(elapsed>maximum_gap) { elapsed=0;g->seen=false;forget_window(g);stop(g); }
    if(!g->seen) {
        for(unsigned i=0;i<5;++i)g->history[i]=raw;
        g->filtered_q8=raw*256;g->cursor=0;
    }
    g->seen=true;g->reliable=true;g->time=time;result.elapsed=elapsed;
    if(held)g->held_us=advance(g->held_us,elapsed,2000000);
    g->history[g->cursor]=raw;g->cursor=(uint8_t)((g->cursor+1)%5);
    unsigned count=elapsed<=20000?5:elapsed<=50000?3:1;
    uint16_t sorted[5];
    for(unsigned i=0;i<count;++i)sorted[i]=g->history[(g->cursor+4-i)%5];
    for(unsigned i=1;i<count;++i)for(unsigned j=i;j && sorted[j]<sorted[j-1];--j) {
        uint16_t swap=sorted[j];sorted[j]=sorted[j-1];sorted[j-1]=swap;
    }
    int32_t sample=sorted[count/2];
    if(elapsed)g->filtered_q8+=(int32_t)(((int64_t)sample*256-g->filtered_q8)*elapsed/(elapsed+15000));

    // Acceleration changes reject handling/tilt. A bounded rate gate below
    // prevents normal auto-zero from learning a large steady rotation.
    bool pending_hold=held && !g->hold_done;
    int32_t spread=g->manual || pending_hold?64:maximum(12,minimum(64,g->noise_q8*2/256+4));
    bool restart=g->samples==0 || maximum(g->high,sample)-minimum(g->low,sample)>spread;
    for(unsigned i=0;i<3;++i) {
        if(accel[i]>1023)accel_valid=false;
        if(g->samples && maximum(g->accel_high[i],accel[i])-minimum(g->accel_low[i],accel[i])>8)restart=true;
    }
    if(!accel_valid || !allow_learning)forget_window(g);
    else {
        if(restart) {
            forget_window(g);g->low=g->high=(uint16_t)sample;
            for(unsigned i=0;i<3;++i)g->accel_low[i]=g->accel_high[i]=accel[i];
        } else g->quiet_us=advance(g->quiet_us,elapsed,4000000);
        g->low=(uint16_t)minimum(g->low,sample);g->high=(uint16_t)maximum(g->high,sample);
        for(unsigned i=0;i<3;++i) {
            g->accel_low[i]=(uint16_t)minimum(g->accel_low[i],accel[i]);
            g->accel_high[i]=(uint16_t)maximum(g->accel_high[i],accel[i]);
        }
        g->quiet_sum+=sample;g->samples++;
        if(g->samples>=8) {
            bool manual_ready=g->manual && g->quiet_us>=3000000;
            bool hold_ready=pending_hold && g->held_us>=2000000 && g->quiet_us>=2000000;
            int32_t mean=(int32_t)((g->quiet_sum*256+g->samples/2)/g->samples);
            bool auto_ready=!g->manual && !pending_hold && g->quiet_us>=750000 &&
                magnitude(mean-g->bias_q8)<=minimum(64*256,maximum(24*256,band(g)*2));
            if(manual_ready || hold_ready || auto_ready) {
                learn(g,manual_ready || hold_ready);forget_window(g);
                if(manual_ready || hold_ready) {
                    g->manual=false;g->hold_done=held;result.calibrated=true;result.recentered=true;
                }
                return result;
            }
        }
        if(g->quiet_us>=4000000)forget_window(g);
    }
    if(g->manual || recenter || !elapsed) { stop(g);return result; }
    int32_t rate=g->filtered_q8-g->bias_q8,absolute=magnitude(rate),dead=band(g);
    if(absolute<=dead) { g->moving=false;g->motion_us=0; }
    else if(!g->moving) {
        if(absolute>=dead+maximum(2*256,dead/3))g->motion_us=advance(g->motion_us,elapsed,20000);
        else g->motion_us=0;
        g->moving=g->motion_us>=20000;
    }
    int32_t output=g->moving?(rate<0?-(absolute-dead):absolute-dead):0;
    result.delta=((int64_t)g->rate_q8+output)*elapsed/2;
    g->rate_q8=output;return result;
}
