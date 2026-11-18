#include "analog_response.h"
#include "controller_profile.h"
#include "analog_curves.h"

// floor(n * UAR_UNIT / d), for 0 <= n <= d. Usual 8/16-bit reports use the
// direct product; wider HID fields take the bounded 30-step integer path.
uint32_t uar_fraction(uint64_t n,uint64_t d) {
    if(n>=d)return (uint32_t)UAR_UNIT;
    if(n<=UINT64_MAX/(uint64_t)UAR_UNIT)return (uint32_t)(n*(uint64_t)UAR_UNIT/d);
    uint64_t remainder=0;uint32_t result=0;
    for(int bit=29;bit>=0;--bit) {
        result*=2;remainder*=2;
        if(((uint32_t)UAR_UNIT>>bit)&1u)remainder+=n;
        if(remainder>=d) { result+=(uint32_t)(remainder/d);remainder%=d; }
    }
    return result;
}
// Integer square root for the small initial-rise segment. Its argument
// is at most (UAR_UNIT/128)^2 < 2^46, so at most 23 root bits are needed.
static uint32_t uar_initial_root(uint64_t value) {
    uint64_t root=0,bit=UINT64_C(1)<<44;
    while(bit>value)bit>>=2;
    while(bit) {
        if(value>=root+bit) { value-=root+bit;root=(root>>1)+bit; }
        else root>>=1;
        bit>>=2;
    }
    return (uint32_t)root;
}
uint32_t uar_curve(uint32_t x,uint8_t code,bool finite_low_gain) {
    code=up_analog_curve_code(code);
    if(!code || !x || x>=(uint32_t)UAR_UNIT)return x;

    if(code==UP_ANALOG_LOW_SPEED_CURVE) {
        // Start with a local square-root rise, then immediately change
        // curvature to join the existing line without a value/slope jump.
        // With a=1/128, f=3*sqrt(a*x)/4 through a. For d=x-a,
        // f=3a/4+3d/8+d^2/(64a) through 5a. The joining slopes are
        // 3/8 and 1/2. The curve remains monotone; only x<5/128 changes.
        // Keep constant gain afterward through the lower half, then increase
        // the slope continuously. At x=1/2 the value is 1/4 and slope is 1/2;
        // at x=1 the value is 1 and slope is 67/20, as before.
        // For t=2x-1, the upper segment is (10+10t+3t^2+17t^3)/40.
        // All terms are nonnegative and monotone, including after rounding.
        if(finite_low_gain) {
            const uint32_t initial=(uint32_t)UAR_UNIT/128u;
            if(x<=initial)return 3u*uar_initial_root((uint64_t)initial*x)/4u;
            if(x<5u*initial) {
                uint64_t d=x-initial;
                // Positive terms keep the rounded integer curve monotone.
                return (uint32_t)(3u*initial/4u+3u*d/8u+d*d/(64u*initial));
            }
            if(x<=UAR_UNIT/2u) {
                uint32_t value=x/2u;
                return value?value:1u;
            }
            uint64_t t=2u*(uint64_t)x-UAR_UNIT;
            uint64_t square=t*t/UAR_UNIT;
            uint64_t cube=square*t/UAR_UNIT;
            return (uint32_t)((10u*(uint64_t)UAR_UNIT+10u*t+3u*square+17u*cube)/40u);
        }
        // Preserve the existing reciprocal continuation above unity.
        // Original x^2 through 1/4, then x^2-(12/5)(x-1/4)^2(1-x).
        // Divide between products to keep all intermediates within uint64_t.
        uint64_t square=(uint64_t)x*x/UAR_UNIT;
        if(x>UAR_UNIT/4u) {
            uint64_t d=x-UAR_UNIT/4u;
            uint64_t reduced=d*d/UAR_UNIT;
            square-=reduced*(UAR_UNIT-x)*12u/((uint64_t)UAR_UNIT*5u);
        }
        return (uint32_t)(square?square:1u);
    }
    uint32_t original=x;unsigned shifts=0;
    // Use the upper half of the curve table, where its relative resolution is
    // sufficient, then apply the exact power-of-two scale in integer units.
    // This also supplies a stable reciprocal curve for input above unity.
    while(x<(uint32_t)UAR_UNIT/2u) { x*=2u;++shifts; }
    uint64_t answer;
    if(code==20)answer=(uint64_t)x*x/(uint64_t)UAR_UNIT;
    else if(code==30) {
        uint64_t square=(uint64_t)x*x/(uint64_t)UAR_UNIT;
        answer=square*x/(uint64_t)UAR_UNIT;
    } else {
        unsigned row;
        switch(code) { case 4:row=0;break;case 6:row=1;break;case 8:row=2;break;
            case 12:row=3;break;case 14:row=4;break;case 16:row=5;break;default:return x; }
        uint64_t at=(uint64_t)x*256;unsigned index=(unsigned)(at/(uint64_t)UAR_UNIT);
        uint64_t part=at%(uint64_t)UAR_UNIT;
        answer=((uint64_t)analog_curves[row][index]*(uint64_t)UAR_UNIT+
            (analog_curves[row][index+1]-analog_curves[row][index])*part)/32768u;
    }
    static const uint32_t power_two_tenths[10]={1000000000u,1071773463u,1148698355u,1231144413u,
        1319507911u,1414213562u,1515716567u,1624504793u,1741101127u,1866065983u};
    unsigned exponent=shifts*code;
    answer=answer*(uint64_t)UAR_UNIT/power_two_tenths[exponent%10u];
    answer=exponent/10u>=32u?0:answer>>(exponent/10u);
    if(finite_low_gain && answer>(uint64_t)original*4u)answer=(uint64_t)original*4u;
    // Retain a nonzero integral contribution even below one response unit.
    return (uint32_t)(answer?answer:1);
}
uint32_t uar_response(uint64_t numerator,uint64_t denominator,uint16_t gain100,uint8_t curve) {
    if(!numerator || !denominator || !gain100)return 0;
    uint64_t response;
    if(!up_analog_curve_code(curve))response=uar_fraction(numerator*gain100,denominator*100u);
    else if(numerator<=denominator) {
        uint32_t linear=uar_fraction(numerator,denominator);if(!linear)linear=1;
        response=(uint64_t)uar_curve(linear,curve,true)*gain100/100u;
    } else {
        // Reciprocal continuation preserves the value and slope at unity,
        // including for the low-speed curve, without early input saturation.
        uint32_t inverse=uar_fraction(denominator,numerator);if(!inverse)inverse=1;
        uint32_t curved=uar_curve(inverse,curve,false);
        response=uar_fraction((uint64_t)UAR_UNIT*gain100,(uint64_t)curved*100u);
    }
    if(!response)response=1;
    return (uint32_t)(response>UAR_UNIT?UAR_UNIT:response);
}
