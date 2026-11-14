#include "switch_rumble.h"
#include <string.h>

const uint8_t sr_neutral[8]={0,1,0x40,0x40,0,1,0x40,0x40};
static int clamp(int x,int lo,int hi) { return x<lo?lo:x>hi?hi:x; }
static int amplitude(unsigned i) {
    return !i?-256:i<16?(int)i*8-248:i<32?(int)i*2-158:(int)i-127;
}
static unsigned bits(uint32_t word,unsigned at,unsigned width) { return (word>>at)&((1u<<width)-1); }
static void command(sr_motor *m,unsigned band,unsigned code) {
    if(!code) { m->amplitude[band]=-256;m->frequency[band]=0; }
    else if(code<=11)m->amplitude[band]=(int16_t)(-16*((int)code-1));
    else if(code<=16)m->frequency[band]=(int16_t)(6*((int)code-14));
    else {
        static const int8_t da[5]={4,1,0,-1,-4},df[3]={1,0,-1};
        unsigned index=code-17;
        m->amplitude[band]=(int16_t)clamp(m->amplitude[band]+da[index/3],-256,0);
        m->frequency[band]=(int16_t)clamp(m->frequency[band]+df[index%3],-64,64);
    }
}
static void pair(sr_motor *m,uint32_t w,unsigned high,unsigned low) {
    command(m,1,bits(w,high,5));command(m,0,bits(w,low,5));
}
static void capture(const sr_motor *m,sr_motor *samples,unsigned *count) {
    if(samples)samples[*count]=*m;
    ++*count;
}
static unsigned motor(sr_motor *m,uint32_t w,sr_motor *samples) {
    unsigned count=0;
    switch(w>>30) {
    case 0:return 0;
    case 1:
        if(!(w&0xfffffu))pair(m,w,20,25);
        else if(!(w&3)) {
            m->amplitude[0]=(int16_t)amplitude(bits(w,23,7));m->frequency[0]=(int16_t)((int)bits(w,16,7)-64);
            m->amplitude[1]=(int16_t)amplitude(bits(w,9,7));m->frequency[1]=(int16_t)((int)bits(w,2,7)-64);
        } else {
            unsigned band=w&1,value=bits(w,23,7);
            if(w&4)m->frequency[band]=(int16_t)((int)value-64);
            else m->amplitude[band]=(int16_t)amplitude(value);
            capture(m,samples,&count);pair(m,w,13,18);capture(m,samples,&count);pair(m,w,3,8);
        }
        break;
    case 2:
        if(!(w&1023))pair(m,w,20,25);
        else {
            unsigned band=w&1;
            m->amplitude[band]=(int16_t)amplitude(bits(w,23,7));m->frequency[band]=(int16_t)((int)bits(w,1,7)-64);
            command(m,band^1,bits(w,18,5));
        }
        capture(m,samples,&count);
        if(!(w&1023))pair(m,w,10,15);else pair(m,w,8,13);
        break;
    case 3:pair(m,w,20,25);capture(m,samples,&count);pair(m,w,10,15);capture(m,samples,&count);pair(m,w,0,5);break;
    }
    capture(m,samples,&count);return count;
}
void sr_reset(sr_state *s) {
    memset(s,0,sizeof(*s));
    for(unsigned i=0;i<2;++i)for(unsigned b=0;b<2;++b)s->motor[i].amplitude[b]=-256;
}
static void decode(sr_state *s,const uint8_t data[8],sr_samples *samples) {
    for(unsigned i=0;i<2;++i) {
        const uint8_t *d=data+4*i;
        uint32_t w=d[0]|((uint32_t)d[1]<<8)|((uint32_t)d[2]<<16)|((uint32_t)d[3]<<24);
        unsigned n=motor(&s->motor[i],w,samples?samples->motor[i]:NULL);
        if(samples)samples->count[i]=(uint8_t)n;
    }
}
void sr_decode(sr_state *s,const uint8_t data[8]) { decode(s,data,NULL); }
void sr_decode_samples(sr_state *s,const uint8_t data[8],sr_samples *samples) { decode(s,data,samples); }
static unsigned encode_amplitude(int value) {
    if(value<-254)return 0;
    unsigned low=1,high=127;
    while(low<high) { unsigned mid=(low+high)/2;if(amplitude(mid)<value)low=mid+1;else high=mid; }
    if(low>1 && value-amplitude(low-1)<amplitude(low)-value)--low;
    return low;
}
void sr_absolute(const sr_state *s,uint8_t data[8]) {
    for(unsigned i=0;i<2;++i) {
        const sr_motor *m=&s->motor[i];
        if(m->amplitude[0]<-254 && m->amplitude[1]<-254) { memcpy(data+4*i,sr_neutral,4);continue; }
        uint32_t w=(1u<<30)|(encode_amplitude(m->amplitude[0])<<23)|
            ((unsigned)clamp(m->frequency[0]+64,0,127)<<16)|
            (encode_amplitude(m->amplitude[1])<<9)|((unsigned)clamp(m->frequency[1]+64,0,127)<<2);
        // Avoid the ambiguous one-5-bit encoding when every low field is zero.
        if(!(w&0xfffffu))w|=4;
        for(unsigned b=0;b<4;++b)data[4*i+b]=(uint8_t)(w>>(8*b));
    }
}
bool sr_active(const sr_state *s) {
    for(unsigned i=0;i<2;++i)for(unsigned b=0;b<2;++b)if(s->motor[i].amplitude[b]>=-254)return true;
    return false;
}

// HD2 wire format: report 02, independent 16-byte actuator blocks, 1..3
// five-byte samples. Frequency uses 96 steps/octave (110 Hz = code 333).
// Thus the HD1 logarithmic frequency changes map exactly to three HD2 steps.
// Amplitudes are linear and limited to 450 per band, not the full 10-bit range.
static const uint16_t switch2_amplitude[257]={
    0,0,2,2,2,2,2,2,2,2,2,2,2,2,2,2,
    2,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,
    4,4,4,4,4,4,4,4,4,4,4,4,5,5,5,5,
    5,5,5,5,5,6,6,6,6,6,6,6,6,7,7,7,
    7,7,7,8,8,8,8,8,8,9,9,9,9,9,10,10,
    10,10,10,11,11,11,11,12,12,12,12,13,13,13,13,14,
    14,14,15,15,15,16,16,16,17,17,17,18,18,19,19,19,
    20,20,21,21,22,22,23,23,24,24,25,25,26,26,27,28,
    28,29,29,30,31,31,32,33,33,34,35,36,36,37,38,39,
    40,41,42,42,43,44,45,46,47,48,49,50,52,53,54,55,
    56,57,59,60,61,63,64,65,67,68,70,71,73,75,76,78,
    80,81,83,85,87,89,91,93,95,97,99,101,103,105,108,110,
    112,115,117,120,123,125,128,131,134,137,140,143,146,149,152,156,
    159,163,166,170,173,177,181,185,189,193,198,202,206,211,215,220,
    225,230,235,240,245,251,256,262,268,273,279,286,292,298,305,311,
    318,325,332,340,347,355,362,370,378,387,395,404,413,422,431,440,
    450,
};
unsigned sr_switch2(const sr_state *s,const sr_samples *samples,uint8_t sequence,uint8_t report[64]) {
    unsigned count=1;
    if(samples)for(unsigned i=0;i<2;++i)if(samples->count[i]>count)count=samples->count[i];
    memset(report,0,64);report[0]=2;
    for(unsigned i=0;i<2;++i) {
        uint8_t *out=report+1+16*i;out[0]=(uint8_t)(0x40|(count<<4)|(sequence&15));
        for(unsigned j=0;j<count;++j) {
            const sr_motor *m=&s->motor[i];
            if(samples && j<samples->count[i])m=&samples->motor[i][j];
            unsigned high=(unsigned)(481+3*clamp(m->frequency[1],-64,64));
            unsigned low=(unsigned)(385+3*clamp(m->frequency[0],-64,64));
            unsigned ha=switch2_amplitude[clamp(m->amplitude[1],-256,0)+256];
            unsigned la=switch2_amplitude[clamp(m->amplitude[0],-256,0)+256];
            uint64_t word=high|((uint64_t)ha<<10)|((uint64_t)low<<20)|((uint64_t)la<<30);
            for(unsigned b=0;b<5;++b)out[1+5*j+b]=(uint8_t)(word>>(8*b));
        }
    }
    return count;
}
