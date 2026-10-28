#include "controller_profile.h"
#include "controller_memory.h"
#include <limits.h>
#include <string.h>

void up_profile_clear(up_profile *p) {
    memset(p,0,sizeof(*p));p->version=5;
    p->mouse=(up_mouse_options){.flags=UP_MOUSE_TRACKBALL|UP_MOUSE_PADDLE,.trackball_gain10=160,.paddle_gain10=10};
}
void up_bank_clear(up_bank *b) {
    memset(b,0,sizeof(*b));b->magic=UP_BANK_MAGIC;b->version=5;b->bytes=sizeof(*b);
    for(unsigned i=0;i<UP_PROFILE_COUNT;++i)up_profile_clear(&b->profiles[i]);
}
uint32_t up_crc32(const void *data,unsigned length) {
    const uint8_t *p=data;uint32_t crc=~0u;
    for(unsigned i=0;i<length;++i) {
        crc^=p[i];
        for(unsigned j=0;j<8;++j)crc=(crc>>1)^(0xedb88320u&(0u-(crc&1)));
    }
    return ~crc;
}
static bool source_valid(uint8_t id,uint16_t offset,uint8_t bits,uint8_t flags) {
    return bits && bits<=32 && (unsigned)offset+bits<=((flags&UP_REPORT_ID)?504u:512u) &&
        ((flags&UP_REPORT_ID)?id!=0:id==0);
}
static bool number_fits(int32_t value,uint8_t bits,uint8_t flags) {
    if(flags&UP_SIGNED) {
        if(bits==32)return true;
        int64_t limit=(int64_t)1<<(bits-1);
        return value>=-limit && value<limit;
    }
    return value>=0 && (bits==32 || (uint32_t)value<((uint32_t)1<<bits));
}
static bool digital_valid(const up_digital *d,bool allow_rapid) {
    if(d->rapid_hz && (!allow_rapid || d->mode==UP_DISABLED ||
       d->rapid_hz<UP_RAPID_MIN_HZ || d->rapid_hz>UP_RAPID_MAX_HZ))return false;
    if(d->mode==UP_DISABLED)return true;
    if(d->mode>UP_HAT_LEFT || (d->flags&~3u) ||
       !source_valid(d->report_id,d->bit_offset,d->bits,d->flags) || d->low>d->high ||
       !number_fits(d->low,d->bits,d->flags) || !number_fits(d->high,d->bits,d->flags))return false;
    int64_t range=(int64_t)d->high-d->low;
    return d->mode==UP_RANGE || range==7 || range==3;
}
bool up_profile_valid(const up_profile *p) {
    if(p->version!=5 || (p->flags&~15u) || !memchr(p->name,0,sizeof(p->name)))return false;
    const up_mouse_options *m=&p->mouse;
    unsigned gain=up_mouse_trackball_gain10(m);
    if((m->flags&~31u) || gain<10 || gain>640u || m->paddle_gain10<10 || m->paddle_gain10>100)return false;
    if((m->output_mode&~7u)!=0 || (m->output_mode&3u)==3u)return false;
    if(!(p->flags&UP_LED_CUSTOM))
        for(unsigned i=0;i<sizeof(m->led_rgb);++i)if(m->led_rgb[i])return false;
    if((p->flags&UP_ENABLED) && (!p->vid || !p->name[0]))return false;
    for(unsigned i=0;i<UP_DIGITAL_COUNT;++i)if(!digital_valid(&p->digital[i],true))return false;
    for(unsigned i=0;i<UP_AXIS_COUNT;++i) {
        unsigned mode=(p->axis_modes>>(2*i))&3;
        if(mode==UP_AXIS_DISABLED)continue;
        if(mode==UP_AXIS_BUTTON_PAIR) {
            if(!digital_valid(&p->axes[i].pair[0],false) || !digital_valid(&p->axes[i].pair[1],false))return false;
        } else if(mode==UP_AXIS_ANALOG) {
            const up_analog *a=&p->axes[i].analog;
            if((a->flags&~7u) || a->reserved || !source_valid(a->report_id,a->bit_offset,a->bits,a->flags) ||
               a->minimum>=a->center || a->center>=a->maximum || a->deadzone_percent>50 ||
               !number_fits(a->minimum,a->bits,a->flags) || !number_fits(a->maximum,a->bits,a->flags))return false;
        } else return false;
    }
    return true;
}
uint32_t up_profile_led_rgb(const up_profile *p) {
    if(!p || !(p->flags&UP_ENABLED) || !(p->flags&UP_LED_CUSTOM) || p->version!=5)return 1;
    const uint8_t *rgb=p->mouse.led_rgb;
    return ((uint32_t)rgb[0]<<16)|((uint32_t)rgb[1]<<8)|rgb[2];
}
uint8_t up_profile_output_options(const up_profile *p) {
    if(!p || !(p->flags&UP_ENABLED) || p->version!=5)return 0;
    unsigned mode=p->mouse.output_mode&3u;
    // These bits retain the established MSE1 diagnostic wire representation.
    return mode==UP_OUTPUT_MOUSE?1u:mode==UP_OUTPUT_COMBINED?
        (uint8_t)(65u|((p->mouse.output_mode&UP_OUTPUT_CONTROLLER_BUTTONS)?128u:0u)):0u;
}
bool up_switch_keys_valid(uint32_t keys) {
    if(!keys)return true;
    return !(keys&~0x3ffffu) && (keys&(keys-1)) && (keys&0x14000u)!=0x14000u && (keys&0x28000u)!=0x28000u;
}
bool up_bank_valid(const up_bank *b) {
    if(b->magic!=UP_BANK_MAGIC || b->version!=5 || b->bytes!=sizeof(*b) || (b->preferred_slots&~65535u))return false;
    for(unsigned i=0;i<UP_PROFILE_COUNT;++i)if(!up_profile_valid(&b->profiles[i]))return false;
    for(unsigned i=0;i<UP_PROFILE_COUNT;++i) {
        if(!up_switch_keys_valid(b->switch_keys[i]) || (!(b->profiles[i].flags&UP_ENABLED) && b->switch_keys[i]))return false;
        for(unsigned j=0;j<i;++j)if(up_same_controller(&b->profiles[i],&b->profiles[j]) && b->switch_keys[i]!=b->switch_keys[j])return false;
    }
    for(unsigned i=0;i<UP_PROFILE_COUNT;++i)if(b->preferred_slots&(1u<<i)) {
        if(!(b->profiles[i].flags&UP_ENABLED) || (b->preferred_slots&up_group_mask(b,i))!=(1u<<i))return false;
    }
    return true;
}
bool up_same_controller(const up_profile *a,const up_profile *b) {
    return (a->flags&UP_ENABLED) && (b->flags&UP_ENABLED) && a->vid==b->vid && a->pid==b->pid &&
        (a->flags&6)==(b->flags&6) && (!(a->flags&UP_MATCH_DESCRIPTOR) || a->descriptor_hash==b->descriptor_hash) &&
        (!(a->flags&UP_MATCH_REVISION) || a->bcd_device==b->bcd_device);
}
unsigned up_group_mask(const up_bank *b,unsigned slot) {
    unsigned mask=0;if(slot>=UP_PROFILE_COUNT)return 0;
    for(unsigned i=0;i<UP_PROFILE_COUNT;++i)if(up_same_controller(&b->profiles[slot],&b->profiles[i]))mask|=1u<<i;
    return mask;
}
unsigned up_profile_number(const up_bank *b,unsigned slot) {
    unsigned mask=up_group_mask(b,slot),number=0;
    for(unsigned i=0;i<=slot && i<UP_PROFILE_COUNT;++i)if(mask&(1u<<i))number++;
    return number;
}
void up_prefer_slot(up_bank *b,unsigned slot) {
    if(slot>=UP_PROFILE_COUNT)return;
    b->preferred_slots&=~((1u<<slot)|up_group_mask(b,slot));
    if(b->profiles[slot].flags&UP_ENABLED)b->preferred_slots|=1u<<slot;
}
int up_find_profile(const up_bank *b,uint16_t vid,uint16_t pid,uint16_t revision,uint32_t hash) {
    int found=-1,best=-1;
    for(unsigned i=0;i<UP_PROFILE_COUNT;++i) {
        const up_profile *p=&b->profiles[i];
        if(!(p->flags&UP_ENABLED) || p->vid!=vid || p->pid!=pid ||
           ((p->flags&UP_MATCH_DESCRIPTOR) && p->descriptor_hash!=hash) ||
           ((p->flags&UP_MATCH_REVISION) && p->bcd_device!=revision))continue;
        int score=((p->flags&UP_MATCH_DESCRIPTOR)?2:0)+((p->flags&UP_MATCH_REVISION)?1:0);
        if(score>best || (score==best && (b->preferred_slots&(1u<<i)))) { found=(int)i;best=score; }
    }
    return found;
}
void up_mapper_attach(up_mapper *m,const up_profile *p) {
    memset(m,0,sizeof(*m));m->profile=p && up_profile_valid(p)?p:NULL;memset(m->axes,128,sizeof(m->axes));
    if(m->profile && (p->flags&UP_ENABLED))
        for(unsigned i=0;i<UP_DIGITAL_COUNT;++i)if(p->digital[i].rapid_hz)m->rapid_configured|=1u<<i;
}
static bool UC_RAM_FUNC(read_source)(uint8_t wanted_interface,uint8_t id,uint16_t offset,uint8_t bits,uint8_t flags,
                        uint8_t interface_number,const uint8_t *raw,unsigned n,int32_t *value) {
    if(wanted_interface!=interface_number)return false;
    uc_hid_field field={.bit_offset=offset,.bits=bits,.report_id=id,.logical_min=(flags&UP_SIGNED)?-1:0};
    return uc_hid_field_value(&field,(flags&UP_REPORT_ID)!=0,raw,n,value);
}
static inline bool digital_pressed(const up_digital *d,int32_t value) {
    if(d->mode==UP_DISABLED || value<d->low || value>d->high)return false;
    if(d->mode==UP_RANGE)return true;
    unsigned position=(unsigned)(value-d->low),direction=d->mode-UP_HAT_UP;
    if(d->high-d->low==3)return position==direction;
    unsigned middle=2*direction;
    return position==middle || position==((middle+1)&7) || position==((middle+7)&7);
}
bool up_digital_pressed_value(const up_digital *d,int32_t value) { return digital_pressed(d,value); }
static bool UC_RAM_FUNC(digital_value)(const up_digital *d,uint8_t iface,const uint8_t *raw,unsigned n,bool *pressed) {
    int32_t value;
    if(d->mode==UP_DISABLED || !read_source(d->interface_number,d->report_id,d->bit_offset,d->bits,d->flags,iface,raw,n,&value))return false;
    *pressed=digital_pressed(d,value);
    return true;
}
void up_release_guard_begin(up_release_guard *g,const up_profile *p,uint32_t keys) {
    g->keys=g->held=keys;
    for(unsigned i=0;i<UP_DIGITAL_COUNT;++i)if(keys&(1u<<i))g->bindings[i]=p->digital[i];
}
void up_release_guard_apply(up_release_guard *g,uint8_t iface,const uint8_t *raw,unsigned n) {
    for(unsigned i=0;i<UP_DIGITAL_COUNT;++i)if(g->keys&(1u<<i)) {
        bool pressed;
        if(digital_value(&g->bindings[i],iface,raw,n,&pressed)) {
            if(pressed)g->held|=1u<<i;else g->held&=~(1u<<i);
        }
    }
    if(!g->held)g->keys=0;
}
static uint8_t UC_RAM_FUNC(analog_value)(const up_analog *a,int32_t value) {
    if(value<a->minimum)value=a->minimum;
    if(value>a->maximum)value=a->maximum;
    bool positive=value>=a->center;
    int64_t span=positive?(int64_t)a->maximum-a->center:(int64_t)a->center-a->minimum;
    int64_t distance=positive?(int64_t)value-a->center:(int64_t)a->center-value;
    int64_t dead=span*a->deadzone_percent/100;
    if(distance<=dead)return 128;
    if(a->flags&UP_INVERT)positive=!positive;
    unsigned amount=(unsigned)((distance-dead)*(positive?127:128)/(span-dead));
    return (uint8_t)(positive?128+amount:128-amount);
}
static uint32_t physical_buttons(const up_mapper *m) {
    uint32_t bits=0;
    for(unsigned i=0;i<UP_DIGITAL_COUNT;++i)if(m->digital[i])bits|=1u<<i;
    return bits;
}
static void UC_RAM_FUNC(report_buttons)(const up_mapper *m,uint32_t buttons,sw_report *r) {
    sw_neutral(r);
    r->buttons0=(uint8_t)buttons;r->buttons1=(uint8_t)((buttons>>8)&0x3f);
    int x=(int)((buttons>>15)&1)-(int)((buttons>>17)&1),y=(int)((buttons>>16)&1)-(int)((buttons>>14)&1);
    static const uint8_t hats[3][3]={{7,0,1},{6,15,2},{5,4,3}};
    r->hat=hats[y+1][x+1];r->lx=m->axes[0];r->ly=m->axes[1];r->rx=m->axes[2];r->ry=m->axes[3];
}
void UC_RAM_FUNC(up_mapper_report)(const up_mapper *m,sw_report *r) { report_buttons(m,physical_buttons(m),r); }
bool UC_RAM_FUNC(up_mapper_tick)(up_mapper *m,uint32_t now,bool ready,sw_report *out) {
    uint32_t physical=physical_buttons(m);
    m->physical=physical;
    for(unsigned i=0;i<UP_DIGITAL_COUNT;++i) {
        uint32_t bit=1u<<i;if(!(m->rapid_configured&bit))continue;
        unsigned hz=m->profile->digital[i].rapid_hz;
        if(!ready || !(physical&bit)) {
            m->rapid_held&=~bit;m->rapid_on&=~bit;
        } else if(!(m->rapid_held&bit)) {
            // First press is immediate. Release/repress restarts the cycle.
            m->rapid_held|=bit;m->rapid_on|=bit;
            m->rapid_time[i]=now;m->rapid_phase[i]=0;
            m->rapid_next[i]=now+(500000u+hz-1)/hz;
        } else if((int32_t)(now-m->rapid_next[i])>=0) {
            // Phase is (elapsed microseconds * Hz) modulo one million. Keeping
            // the fraction avoids drift at 3/7/30 Hz. Expensive arithmetic runs
            // only at edges, not on every iteration of the main loop.
            uint32_t elapsed=now-m->rapid_time[i];
            uint32_t phase=(uint32_t)(((uint64_t)m->rapid_phase[i]+(uint64_t)elapsed*hz)%1000000u);
            m->rapid_time[i]=now;m->rapid_phase[i]=phase;
            if(phase<500000u)m->rapid_on|=bit;else m->rapid_on&=~bit;
            uint32_t remaining=(phase<500000u?500000u:1000000u)-phase;
            m->rapid_next[i]=now+(remaining+hz-1)/hz;
        }
    }
    report_buttons(m,(physical&~m->rapid_configured)|(physical&m->rapid_on),out);
    bool changed=!m->have_output || memcmp(&m->last_output,out,sizeof(*out));
    m->last_output=*out;m->have_output=true;return changed;
}
bool UC_RAM_FUNC(up_mapper_apply)(up_mapper *m,uint8_t iface,const uint8_t *raw,unsigned n,sw_report *out) {
    if(!m->profile || !(m->profile->flags&UP_ENABLED))return false;
    const up_profile *p=m->profile;bool touched=false;
    for(unsigned i=0;i<UP_DIGITAL_COUNT;++i)
        if(digital_value(&p->digital[i],iface,raw,n,&m->digital[i]))touched=true;
    for(unsigned i=0;i<UP_AXIS_COUNT;++i) {
        unsigned mode=(p->axis_modes>>(2*i))&3;
        if(mode==UP_AXIS_BUTTON_PAIR) {
            for(unsigned j=0;j<2;++j)
                if(digital_value(&p->axes[i].pair[j],iface,raw,n,&m->pairs[i][j]))touched=true;
            m->axes[i]=m->pairs[i][0]==m->pairs[i][1]?128:m->pairs[i][0]?0:255;
        } else if(mode==UP_AXIS_ANALOG) {
            const up_analog *a=&p->axes[i].analog;int32_t value;
            if(read_source(a->interface_number,a->report_id,a->bit_offset,a->bits,a->flags,iface,raw,n,&value)) {
                m->axes[i]=analog_value(a,value);touched=true;
            }
        }
    }
    up_mapper_report(m,out);return touched;
}
