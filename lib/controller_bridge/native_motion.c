#include "native_motion.h"
#include "input_protocol.h"
#include <math.h>
#include <string.h>
static int16_t le16(const uint8_t *p) { return (int16_t)(p[0]|((uint16_t)p[1]<<8)); }
static uint16_t be16(const uint8_t *p) { return ((uint16_t)p[0]<<8)|p[1]; }
static int16_t rounded(float value) {
    if(value>=32767)return 32767;
    if(value<=-32768)return -32768;
    return (int16_t)(value+(value<0?-.5f:.5f));
}
void unm_restart(unm_state *s) {
    s->head=s->count=0;s->stamp_valid=s->bias_ready=false;
    s->bias_count=0;s->bias_sum=0;s->bias_start=0;
    s->ds3_zero=s->ds3_cal[6];
}
void unm_init(unm_state *s) {
    memset(s,0,sizeof(*s));
    for(unsigned i=0;i<3;++i) {
        s->pro_span[i]=16384;s->pro_span[i+3]=13371;
        s->ds3_cal[i*2]=512;s->ds3_cal[i*2+1]=399;
    }
    s->ds3_cal[6]=512;unm_restart(s);
}
bool unm_calibrate(unm_state *s,uint8_t protocol,const uint8_t *p,unsigned n) {
    if(protocol==UC_PROTOCOL_DUALSHOCK3 && n==16) {
        uint16_t values[8];
        for(unsigned i=0;i<8;++i)values[i]=(uint16_t)le16(p+2*i);
        for(unsigned i=0;i<3;++i)if(values[2*i]>1023 || values[2*i+1]>1023 ||
            values[2*i]<=values[2*i+1] || values[2*i]-values[2*i+1]<32)return false;
        if(values[6]>1023)return false;
        memcpy(s->ds3_cal,values,sizeof(values));s->ds3_zero=values[6];return true;
    }
    if(protocol==UC_PROTOCOL_SWITCH_PRO && n==24) {
        int16_t zero[6];int32_t span[6];
        for(unsigned i=0;i<6;++i) {
            unsigned offset=i<3?i*2:12+(i-3)*2;
            zero[i]=le16(p+offset);span[i]=(int32_t)le16(p+offset+6)-zero[i];
            if(span[i]<1024 || span[i]>60000)return false;
        }
        memcpy(s->pro_zero,zero,sizeof(zero));memcpy(s->pro_span,span,sizeof(span));return true;
    }
    if(protocol==UC_PROTOCOL_SWITCH2 && n==24) {
        float bias[6];memcpy(bias,p,sizeof(bias));
        for(unsigned i=0;i<6;++i)if(!isfinite(bias[i]) || fabsf(bias[i])>(i<3?2.f:10.f))return false;
        memcpy(s->switch2_bias,bias,sizeof(bias));return true;
    }
    return false;
}
static void add(unm_state *s,const float values[6],uint32_t now) {
    if(s->count && (int32_t)(now-s->samples[(s->head+15)%16].time_us)<=0)return;
    unm_sample *sample=&s->samples[s->head];sample->time_us=now;
    for(unsigned i=0;i<6;++i)sample->value[i]=rounded(values[i]);
    s->head=(s->head+1)%16;if(s->count<16)s->count++;
}
static void ds3_bias(unm_state *s,const uint16_t v[4],uint32_t now,bool quiet) {
    if(s->bias_ready)return; // Never relearn a sustained steering movement as neutral.
    if(!quiet) { s->bias_count=0;return; }
    bool stable=s->bias_count!=0;
    for(unsigned i=0;i<4 && stable;++i)
        if(v[i]+(i==3?6:10)<s->bias_max[i] || v[i]>s->bias_min[i]+(i==3?6:10))stable=false;
    if(!stable) {
        s->bias_start=now;s->bias_sum=0;s->bias_count=0;
        for(unsigned i=0;i<4;++i)s->bias_min[i]=s->bias_max[i]=v[i];
    }
    for(unsigned i=0;i<4;++i) {
        if(v[i]<s->bias_min[i])s->bias_min[i]=v[i];
        if(v[i]>s->bias_max[i])s->bias_max[i]=v[i];
    }
    s->bias_sum+=v[3];s->bias_count++;
    if((uint32_t)(now-s->bias_start)>=500000 && s->bias_count>=32) {
        s->ds3_zero=(float)s->bias_sum/s->bias_count;s->bias_ready=true;
    }
}
bool unm_input(unm_state *s,uint8_t protocol,const uint8_t *p,unsigned n,uint32_t now,bool quiet) {
    if(protocol==UC_PROTOCOL_SWITCH_PRO && n>=49 && p[0]==0x30) {
        if(s->stamp_valid && s->last_stamp==p[1])return false;
        s->stamp_valid=true;s->last_stamp=p[1];
        for(unsigned sample=0;sample<3;++sample) {
            float v[6];
            for(unsigned i=0;i<6;++i)v[i]=((int32_t)le16(p+13+12*sample+2*i)-(i<3?0:s->pro_zero[i]))*
                (i<3?16384.f:13371.f)/s->pro_span[i];
            add(s,v,now-(2-sample)*5000u);
        }
        return true;
    }
    if(protocol==UC_PROTOCOL_SWITCH2 && n>=61 && p[0]==5) {
        uint32_t stamp;memcpy(&stamp,p+43,4);
        if(!stamp || (s->stamp_valid && stamp==s->last_stamp))return false;
        s->stamp_valid=true;s->last_stamp=stamp;
        float raw[6],v[6];
        for(unsigned i=0;i<6;++i)raw[i]=le16(p+49+2*i)*(i<3?32768.f/32767.f:(34.8f*180.f/3.141592654f)*(13371.f/936.f)/32767.f)-
            s->switch2_bias[i]*(i<3?4096.f:(180.f/3.141592654f)*(13371.f/936.f));
        for(unsigned i=0;i<6;i+=3) { v[i]=raw[i+1];v[i+1]=-raw[i];v[i+2]=raw[i+2]; }
        add(s,v,now);return true;
    }
    if(protocol==UC_PROTOCOL_DUALSHOCK3 && n>=49 && p[0]==1 && p[1]!=0xff) {
        uint16_t raw[4];float a[3],v[6];
        for(unsigned i=0;i<4;++i) { raw[i]=be16(p+41+2*i);if(raw[i]>1023)return false; }
        ds3_bias(s,raw,now,quiet);
        for(unsigned i=0;i<3;++i)a[i]=((int)raw[i]-s->ds3_cal[2*i])*4096.f/(s->ds3_cal[2*i]-s->ds3_cal[2*i+1]);
        v[0]=a[1];v[1]=-a[0];v[2]=-a[2];
        // DS3 has only the face-normal rotation sensor. No acceleration-derived
        // angular velocity is invented for the missing two gyro axes.
        v[3]=v[4]=v[5]=0;
        unsigned axis=s->ds3_axis==1?0:s->ds3_axis==2?1:2;
        v[3+axis]=s->bias_ready?((float)raw[3]-s->ds3_zero)*(13371.f/(936.f*1.4f)):0;
        add(s,v,now);return true;
    }
    return false;
}
void unm_encode(const unm_state *s,uint8_t out[36],uint32_t now,uint8_t gyro_range,uint8_t accel_range) {
    static const float accel_scale[]={1,2,4,.5f};
    bool fresh=s->count && (uint32_t)(now-s->samples[(s->head+15)%16].time_us)<100000;
    for(unsigned sample=0;sample<3;++sample) {
        const unm_sample *chosen=NULL;uint32_t target=now-(2-sample)*5000u;
        for(unsigned age=0;age<s->count;++age) {
            const unm_sample *candidate=&s->samples[(s->head+15-age)%16];
            chosen=candidate;if((int32_t)(target-candidate->time_us)>=0)break;
        }
        for(unsigned i=0;i<6;++i) {
            float value=chosen?(float)chosen->value[i]:(i==2?4096.f:0.f);
            if(i>=3 && !fresh)value=0;
            value*=i<3?accel_scale[accel_range&3]:(float)(1u<<(3-(gyro_range&3)));
            int16_t raw=rounded(value);out[sample*12+i*2]=(uint8_t)raw;out[sample*12+i*2+1]=(uint8_t)((uint16_t)raw>>8);
        }
    }
}
