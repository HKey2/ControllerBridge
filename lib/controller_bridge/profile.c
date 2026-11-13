#include "controller_profile.h"
#include "controller_memory.h"
#include <limits.h>
#include <string.h>

void up_profile_clear(up_profile *p) {
    memset(p,0,sizeof(*p));p->version=UP_PROFILE_VERSION;
    p->shift.conversion=(up_conversion)UP_DEFAULT_CONVERSION;
    p->mouse=(up_mouse_options){.flags=UP_MOUSE_TRACKBALL|UP_MOUSE_PADDLE,.trackball_gain10=160,.paddle_gain10=10};
}
void up_bank_clear(up_bank *b) {
    memset(b,0,sizeof(*b));b->magic=UP_BANK_MAGIC;b->version=UP_PROFILE_VERSION;b->bytes=sizeof(*b);
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
        ((flags&UP_REPORT_ID)?id!=0:id==0) && (!(flags&UP_BIG_ENDIAN) || (bits==16 && !(offset&7))) &&
        (!(flags&UP_SOURCE_REVERSED) || (bits==8 && !(flags&(UP_SIGNED|UP_BIG_ENDIAN))));
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
    if(d->mode>UP_HAT_LEFT || (d->flags&~(unsigned)UP_SOURCE_FLAGS) ||
       !source_valid(d->report_id,d->bit_offset,d->bits,d->flags) || d->low>d->high ||
       !number_fits(d->low,d->bits,d->flags) || !number_fits(d->high,d->bits,d->flags))return false;
    int64_t range=(int64_t)d->high-d->low;
    return d->mode==UP_RANGE || range==7 || range==3;
}
static bool profile_valid(const up_profile *p,unsigned version) {
    bool legacy=version==6;
    if(p->version!=version || (p->flags&~15u) || !memchr(p->name,0,sizeof(p->name)))return false;
    const up_mouse_options *m=&p->mouse;
    unsigned gain=up_mouse_trackball_gain10(m);
    if(gain<10 || gain>2560u || m->paddle_gain10<10 || m->paddle_gain10>100)return false;
    if((m->output_mode&~7u)!=0 || (m->output_mode&3u)==3u)return false;
    // Conversion flags are retained preferences in mouse-only mode; its
    // dedicated input/output route does not execute gamepad conversion.
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
            if(a->flags&UP_GYRO) {
                if(legacy || p->vid!=0x054c || p->pid!=0x0268 || (i!=0 && i!=2) ||
                   (a->flags&~4u)!=(UP_REPORT_ID|UP_BIG_ENDIAN|UP_GYRO) || a->interface_number ||
                   a->report_id!=1 || a->bit_offset!=368 || a->bits!=16 || a->reserved>12800 ||
                   a->minimum>=-1000 || a->minimum< -1000000000 || a->maximum<=1000 || a->maximum>1000000000 ||
                   a->center<=0 || a->center>=1023*256 || a->deadzone_percent>50)return false;
                for(unsigned j=0;j<sizeof(a->padding);++j)if(a->padding[j])return false;
            } else if((a->flags&~(unsigned)(UP_SOURCE_FLAGS|UP_INVERT)) || a->reserved || !source_valid(a->report_id,a->bit_offset,a->bits,a->flags) ||
               a->minimum>=a->maximum || a->minimum>a->center || a->center>a->maximum || a->deadzone_percent>50 ||
               !number_fits(a->minimum,a->bits,a->flags) || !number_fits(a->maximum,a->bits,a->flags))return false;
        } else if(mode==UP_AXIS_ANALOG_PAIR) {
            if(legacy)return false;
            for(unsigned j=0;j<2;++j) {
                const up_analog_half *a=&p->axes[i].halves[j];
                if((a->flags&~(unsigned)UP_SOURCE_FLAGS) || !source_valid(a->report_id,a->bit_offset,a->bits,a->flags) ||
                   a->neutral==a->maximum || a->deadzone_percent>50 ||
                   !number_fits(a->neutral,a->bits,a->flags) || !number_fits(a->maximum,a->bits,a->flags))return false;
            }
        } else return false;
    }
    const up_shift *s=&p->shift;
    if(s->count>(legacy?4u:UP_SHIFT_KEYS) || (s->overrides&~0x3ffffu) || (up_profile_recenter_keys(p)&~0x1ffffu) || (legacy && up_profile_recenter_keys(p)))return false;
    const up_digital *keys=(const up_digital *)((const uint8_t *)p+496);
    for(unsigned i=0;i<(legacy?4u:UP_SHIFT_KEYS);++i) {
        if(!digital_valid(&keys[i],false) || (i<s->count && !keys[i].mode))return false;
        for(unsigned j=0;j<i && i<s->count;++j) {
            const up_digital *a=&keys[i],*b=&keys[j];
            if(a->interface_number==b->interface_number && a->report_id==b->report_id && a->bit_offset==b->bit_offset && a->bits==b->bits)return false;
        }
    }
    for(unsigned i=0;i<UP_DIGITAL_COUNT;++i)if(!digital_valid(&s->digital[i],true))return false;
    if(!legacy) {
        const up_conversion *c=&s->conversion;
        for(unsigned i=0;i<4;++i)if(!c->targets[i] || c->targets[i]>18)return false;
        for(unsigned i=0;i<2;++i)if(!c->paddle_targets[i] || c->paddle_targets[i]>18)return false;
        if((c->modes&~5u) || c->rolling_flags>3 || c->rolling_step_deg<5 || c->rolling_step_deg>90 ||
           c->rolling_step_deg%5u || 360u%c->rolling_step_deg)return false;
        if(c->inertia_flags>1u)return false;
        for(unsigned i=0;i<6;++i)if(c->reserved[i])return false;
    }
    return true;
}
uint32_t up_profile_recenter_keys(const up_profile *p) {
    const uint8_t *b=p->shift.gyro_recenter;
    return b[0]|((uint32_t)b[1]<<8)|((uint32_t)b[2]<<16);
}
bool up_profile_valid(const up_profile *p) { return profile_valid(p,UP_PROFILE_VERSION); }
void up_conversion_from_legacy(up_conversion *c,const up_mouse_options *mouse,const udk_settings *s) {
    *c=(up_conversion)UP_DEFAULT_CONVERSION;
    for(unsigned i=0;i<4;++i)c->targets[i]=udk_target((uint8_t)i,s->targets);
    unsigned negative=(mouse->flags&UP_MOUSE_PADDLE_Y)?0u:2u;
    for(unsigned i=0;i<2;++i)c->paddle_targets[i]=udk_target((uint8_t)(negative+i),s->paddle_targets);
    c->modes=s->trackball_mode|(s->paddle_mode<<2);
    c->rolling_flags=s->rolling_flags;c->rolling_step_deg=s->rolling_step_deg;
}
void up_conversion_apply(const up_conversion *c,udk_settings *s) {
    memcpy(s->targets,c->targets,4);
    // Both possible legacy axis labels resolve to the same negative/positive map.
    s->paddle_targets[0]=s->paddle_targets[2]=c->paddle_targets[0];
    s->paddle_targets[1]=s->paddle_targets[3]=c->paddle_targets[1];
    s->trackball_mode=c->modes&3u;s->paddle_mode=(c->modes>>2)&3u;
    s->rolling_flags=c->rolling_flags;s->rolling_step_deg=c->rolling_step_deg;s->inertia_enabled=c->inertia_flags&1u;
}
uint32_t up_profile_led_rgb(const up_profile *p) {
    if(!p || !(p->flags&UP_ENABLED) || !(p->flags&UP_LED_CUSTOM) || p->version!=UP_PROFILE_VERSION)return 1;
    const uint8_t *rgb=p->mouse.led_rgb;
    return ((uint32_t)rgb[0]<<16)|((uint32_t)rgb[1]<<8)|rgb[2];
}
uint8_t up_profile_output_options(const up_profile *p) {
    if(!p || !(p->flags&UP_ENABLED) || p->version!=UP_PROFILE_VERSION)return 0;
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
    if(b->magic!=UP_BANK_MAGIC || b->version!=UP_PROFILE_VERSION || b->bytes!=sizeof(*b) || (b->preferred_slots&~65535u))return false;
    for(unsigned i=0;i<UP_PROFILE_COUNT;++i)if(!up_profile_valid(&b->profiles[i]))return false;
    for(unsigned i=0;i<UP_PROFILE_COUNT;++i) {
        if(up_profile_shift_conflicts(&b->profiles[i],b->switch_keys[i]) || !up_switch_keys_valid(b->switch_keys[i]) || (!(b->profiles[i].flags&UP_ENABLED) && b->switch_keys[i]))return false;
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
static bool same_source(const up_digital *a,const up_digital *b) {
    return a->interface_number==b->interface_number && a->report_id==b->report_id &&
        a->bit_offset==b->bit_offset && a->bits==b->bits && (a->flags&UP_SOURCE_FLAGS)==(b->flags&UP_SOURCE_FLAGS);
}
static uint8_t modifier_mask(const up_profile *p,const up_digital *d) {
    uint8_t mask=0;
    for(unsigned k=0;k<p->shift.count;++k)if(up_digital_overlap(d,&p->shift.keys[k]))mask|=(uint8_t)(1u<<k);
    return mask;
}
void up_mapper_attach(up_mapper *m,const up_profile *p) {
    memset(m,0,sizeof(*m));m->profile=p && up_profile_valid(p)?p:NULL;
    memset(m->axes,128,sizeof(m->axes));memset(m->raw_axes,128,sizeof(m->raw_axes));
    int32_t bias=512*256;uint16_t noise=0;bool found=false;
    if(m->profile)for(unsigned i=0;i<UP_AXIS_COUNT;++i) {
        const up_analog *a=&p->axes[i].analog;
        if(((p->axis_modes>>(2*i))&3)!=UP_AXIS_ANALOG || !(a->flags&UP_GYRO))continue;
        if(!found) { bias=a->center;found=true; }
        if(a->reserved>noise)noise=a->reserved;
    }
    up_gyro_init(&m->gyro,bias,noise);
    if(!m->profile || !(p->flags&UP_ENABLED))return;
    for(unsigned i=0;i<UP_DIGITAL_COUNT;++i) {
        if(p->digital[i].rapid_hz)m->rapid_configured|=1u<<i;
        m->base_modifiers[i]=modifier_mask(p,&p->digital[i]);
        m->back_modifiers[i]=modifier_mask(p,&p->shift.digital[i]);
        for(unsigned j=0;j<UP_DIGITAL_COUNT;++j)
            if((p->shift.overrides&(1u<<j)) && up_digital_overlap(&p->digital[i],&p->shift.digital[j]))m->inherited_conflicts[i]|=1u<<j;
    }
    for(unsigned i=0;i<UP_AXIS_COUNT;++i) {
        unsigned mode=(p->axis_modes>>(2*i))&3;
        if(mode==UP_AXIS_BUTTON_PAIR)for(unsigned j=0;j<2;++j)m->pair_modifiers[i][j]=modifier_mask(p,&p->axes[i].pair[j]);
        if(mode==UP_AXIS_ANALOG) {
            const up_analog *a=&p->axes[i].analog;
            up_digital d={.interface_number=a->interface_number,.report_id=a->report_id,.bit_offset=a->bit_offset,.bits=a->bits,.flags=(uint8_t)(a->flags&UP_SOURCE_FLAGS)};
            for(unsigned k=0;k<p->shift.count;++k)if(same_source(&d,&p->shift.keys[k]))m->analog_modifiers[i]|=(uint8_t)(1u<<k);
        }
        if(mode==UP_AXIS_ANALOG_PAIR)for(unsigned j=0;j<2;++j) {
            const up_analog_half *a=&p->axes[i].halves[j];
            up_digital d={.interface_number=a->interface_number,.report_id=a->report_id,.bit_offset=a->bit_offset,.bits=a->bits,.flags=a->flags};
            for(unsigned k=0;k<p->shift.count;++k)if(same_source(&d,&p->shift.keys[k]))m->pair_modifiers[i][j]|=(uint8_t)(1u<<k);
        }
    }
}
static bool UC_RAM_FUNC(read_source)(uint8_t wanted_interface,uint8_t id,uint16_t offset,uint8_t bits,uint8_t flags,
                        uint8_t interface_number,const uint8_t *raw,unsigned n,int32_t *value) {
    if(wanted_interface!=interface_number)return false;
    uc_hid_field field={.bit_offset=offset,.bits=bits,.report_id=id,.logical_min=(flags&UP_SIGNED)?-1:0,.flags=(uint16_t)(((flags&UP_BIG_ENDIAN)?UC_FIELD_BIG_ENDIAN:0)|((flags&UP_SOURCE_REVERSED)?UC_FIELD_REVERSED:0))};
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
bool up_digital_overlap(const up_digital *a,const up_digital *b) {
    if(!a->mode || !b->mode)return false;
    // Legacy raw and normalized ranges may coexist in imported profiles.
    up_digital reflected;
    if(((a->flags^b->flags)&UP_SOURCE_REVERSED) && a->bits==8 && b->bits==8 && b->mode==UP_RANGE) {
        reflected=*b;reflected.flags^=UP_SOURCE_REVERSED;
        reflected.low=255-b->high;reflected.high=255-b->low;b=&reflected;
    }
    if(!same_source(a,b))return false;
    if(a->mode==UP_RANGE && b->mode==UP_RANGE)return a->low<=b->high && b->low<=a->high;
    const up_digital *hat=a->mode==UP_RANGE?b:a;
    for(int64_t v=hat->low;v<=hat->high;++v)if(digital_pressed(a,(int32_t)v) && digital_pressed(b,(int32_t)v))return true;
    return false;
}
bool up_profile_shift_conflicts(const up_profile *p,uint32_t keys) {
    for(unsigned i=0;i<UP_DIGITAL_COUNT;++i)if(keys&(1u<<i))
        for(unsigned k=0;k<p->shift.count;++k)if(up_digital_overlap(&p->digital[i],&p->shift.keys[k]))return true;
    return false;
}
bool up_bank_upgrade_v6(up_bank *b,const udk_settings *defaults) {
    if(b->magic!=UP_BANK_MAGIC || b->version!=6 || b->bytes!=sizeof(*b) || !udk_settings_valid(defaults))return false;
    // Validate the original fourth key before the explicitly permitted removal.
    for(unsigned i=0;i<UP_PROFILE_COUNT;++i)if(!profile_valid(&b->profiles[i],6))return false;
    for(unsigned i=0;i<UP_PROFILE_COUNT;++i) {
        up_profile *p=&b->profiles[i];
        if(p->shift.count>UP_SHIFT_KEYS)p->shift.count=UP_SHIFT_KEYS;
        up_conversion_from_legacy(&p->shift.conversion,&p->mouse,defaults);p->version=UP_PROFILE_VERSION;
    }
    b->version=UP_PROFILE_VERSION;
    return up_bank_valid(b);
}
bool up_bank_upgrade_v5(up_bank *b,const udk_settings *defaults) {
    if(b->magic!=UP_BANK_MAGIC || b->version!=5 || b->bytes!=UP_LEGACY_BANK_BYTES)return false;
    uint8_t *raw=(uint8_t *)b,tail[324];
    memcpy(tail,raw+7824,sizeof(tail));
    for(unsigned i=UP_PROFILE_COUNT;i--;) {
        uint16_t version;memcpy(&version,raw+16+i*UP_LEGACY_PROFILE_BYTES,2);
        if(version!=5)return false;
        memmove(&b->profiles[i],raw+16+i*UP_LEGACY_PROFILE_BYTES,UP_LEGACY_PROFILE_BYTES);
        memset(&b->profiles[i].shift,0,sizeof(up_shift));b->profiles[i].version=6;
    }
    memcpy(b->profile_ids,tail,sizeof(tail));b->version=6;b->bytes=sizeof(*b);
    return up_bank_upgrade_v6(b,defaults);
}
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
    // An endpoint neutral has a zero-width inactive side.
    if(!span || distance<=dead)return 128;
    if(a->flags&UP_INVERT)positive=!positive;
    unsigned amount=(unsigned)((distance-dead)*(positive?127:128)/(span-dead));
    return (uint8_t)(positive?128+amount:128-amount);
}
static uint16_t UC_RAM_FUNC(analog_half_value)(const up_analog_half *a,int32_t value) {
    int64_t span=(int64_t)a->maximum-a->neutral,distance=(int64_t)value-a->neutral;
    if(span<0) { span=-span;distance=-distance; }
    int64_t dead=span*a->deadzone_percent/100;
    if(distance<=dead)return 0;
    if(distance>=span)return 65535;
    return (uint16_t)((distance-dead)*65535/(span-dead));
}
static uint8_t analog_pair_value(const up_mapper *m,unsigned i,uint8_t blocked) {
    int32_t negative=(m->pair_modifiers[i][0]&blocked)?0:m->analog_halves[i][0];
    int32_t positive=(m->pair_modifiers[i][1]&blocked)?0:m->analog_halves[i][1];
    int32_t sum=positive-negative;
    // Sum on a symmetric scale first: equally pressed inputs cancel exactly.
    // Only the final conversion uses the asymmetric 128/127 output range.
    return (uint8_t)(sum>=0?128+sum*127/65535:128-(-sum)*128/65535);
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
    m->physical=0;
    for(unsigned i=0;i<UP_DIGITAL_COUNT;++i)if(m->base_digital[i])m->physical|=1u<<i;
    for(unsigned i=0;i<UP_DIGITAL_COUNT;++i) {
        uint32_t bit=1u<<i;if(!(m->rapid_configured&bit))continue;
        unsigned hz=(m->shift_active && (m->profile->shift.overrides&(1u<<i)))?m->profile->shift.digital[i].rapid_hz:m->profile->digital[i].rapid_hz;
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
static void UC_RAM_FUNC(resolve_shift)(up_mapper *m) {
    const up_profile *p=m->profile;
    bool active=p->shift.count && m->shift_held==((1u<<p->shift.count)-1u);
    if(active)m->shift_consumed=true;
    else if(!m->shift_held)m->shift_consumed=false;
    if(active!=m->shift_active) { m->rapid_held=m->rapid_on=0;m->shift_active=active; }
    uint8_t blocked=m->shift_consumed?m->shift_held:0;
    uint32_t overrides_held=0;
    if(active)for(unsigned i=0;i<UP_DIGITAL_COUNT;++i)if(m->shift_digital[i] && (p->shift.overrides&(1u<<i)))overrides_held|=1u<<i;
    m->rapid_configured=0;m->physical=0;
    for(unsigned i=0;i<UP_DIGITAL_COUNT;++i) {
        bool replace=active && (p->shift.overrides&(1u<<i));
        m->digital[i]=replace?m->shift_digital[i]:m->base_digital[i];
        if((replace?m->back_modifiers[i]:m->base_modifiers[i])&blocked)m->digital[i]=false;
        if(!replace && active && (m->inherited_conflicts[i]&overrides_held))m->digital[i]=false;
        if((replace?p->shift.digital[i].rapid_hz:p->digital[i].rapid_hz))m->rapid_configured|=1u<<i;
        if(m->base_digital[i])m->physical|=1u<<i;
    }
    for(unsigned i=0;i<UP_AXIS_COUNT;++i) {
        unsigned mode=(p->axis_modes>>(2*i))&3;
        m->axes[i]=m->raw_axes[i];
        if(mode==UP_AXIS_BUTTON_PAIR) {
            bool neg=m->pairs[i][0] && !(m->pair_modifiers[i][0]&blocked);
            bool pos=m->pairs[i][1] && !(m->pair_modifiers[i][1]&blocked);
            m->axes[i]=neg==pos?128:neg?0:255;
        } else if(mode==UP_AXIS_ANALOG && (m->analog_modifiers[i]&blocked))m->axes[i]=128;
        else if(mode==UP_AXIS_ANALOG_PAIR)m->axes[i]=analog_pair_value(m,i,blocked);
    }
}
void up_mapper_seed(up_mapper *m,uint8_t iface,uint8_t id,uint16_t offset,uint8_t bits,uint8_t flags,int32_t value) {
    const up_profile *p=m->profile;if(!p || !(p->flags&UP_ENABLED))return;
    up_digital field={.interface_number=iface,.report_id=id,.bit_offset=offset,.bits=bits,.flags=flags};
    for(unsigned i=0;i<UP_DIGITAL_COUNT;++i) {
        if(same_source(&p->digital[i],&field))m->base_digital[i]=digital_pressed(&p->digital[i],value);
        if(same_source(&p->shift.digital[i],&field))m->shift_digital[i]=digital_pressed(&p->shift.digital[i],value);
    }
    for(unsigned k=0;k<p->shift.count;++k)if(same_source(&p->shift.keys[k],&field)) {
        if(digital_pressed(&p->shift.keys[k],value))m->shift_held|=(uint8_t)(1u<<k);else m->shift_held&=(uint8_t)~(1u<<k);
    }
    for(unsigned i=0;i<UP_AXIS_COUNT;++i) {
        unsigned mode=(p->axis_modes>>(2*i))&3;
        if(mode==UP_AXIS_BUTTON_PAIR)for(unsigned j=0;j<2;++j) {
            if(same_source(&p->axes[i].pair[j],&field))m->pairs[i][j]=digital_pressed(&p->axes[i].pair[j],value);
        } else if(mode==UP_AXIS_ANALOG) {
            const up_analog *a=&p->axes[i].analog;
            if(!(a->flags&UP_GYRO) && a->interface_number==iface && a->report_id==id && a->bit_offset==offset && a->bits==bits && (a->flags&UP_SOURCE_FLAGS)==flags)m->raw_axes[i]=analog_value(a,value);
        } else if(mode==UP_AXIS_ANALOG_PAIR)for(unsigned j=0;j<2;++j) {
            const up_analog_half *a=&p->axes[i].halves[j];
            if(a->interface_number==iface && a->report_id==id && a->bit_offset==offset && a->bits==bits && a->flags==flags)
                m->analog_halves[i][j]=analog_half_value(a,value);
        }
    }
    resolve_shift(m);
}
bool UC_RAM_FUNC(up_mapper_apply)(up_mapper *m,uint8_t iface,const uint8_t *raw,unsigned n,sw_report *out) {
    if(!m->profile || !(m->profile->flags&UP_ENABLED))return false;
    const up_profile *p=m->profile;bool touched=false;
    for(unsigned i=0;i<UP_DIGITAL_COUNT;++i)
        if(digital_value(&p->digital[i],iface,raw,n,&m->base_digital[i]))touched=true;
    for(unsigned i=0;i<UP_DIGITAL_COUNT;++i)if(p->shift.overrides&(1u<<i))
        if(digital_value(&p->shift.digital[i],iface,raw,n,&m->shift_digital[i]))touched=true;
    for(unsigned k=0;k<p->shift.count;++k) {
        bool held;
        if(digital_value(&p->shift.keys[k],iface,raw,n,&held)) {
            if(held)m->shift_held|=(uint8_t)(1u<<k);else m->shift_held&=(uint8_t)~(1u<<k);
            touched=true;
        }
    }
    for(unsigned i=0;i<UP_AXIS_COUNT;++i) {
        unsigned mode=(p->axis_modes>>(2*i))&3;
        if(mode==UP_AXIS_BUTTON_PAIR) {
            for(unsigned j=0;j<2;++j)
                if(digital_value(&p->axes[i].pair[j],iface,raw,n,&m->pairs[i][j]))touched=true;
            m->raw_axes[i]=m->pairs[i][0]==m->pairs[i][1]?128:m->pairs[i][0]?0:255;
        } else if(mode==UP_AXIS_ANALOG) {
            const up_analog *a=&p->axes[i].analog;int32_t value;
            if(a->flags&UP_GYRO) { touched=true;continue; }
            if(read_source(a->interface_number,a->report_id,a->bit_offset,a->bits,a->flags,iface,raw,n,&value)) {
                m->raw_axes[i]=analog_value(a,value);touched=true;
            }
        } else if(mode==UP_AXIS_ANALOG_PAIR)for(unsigned j=0;j<2;++j) {
            const up_analog_half *a=&p->axes[i].halves[j];int32_t value;
            if(read_source(a->interface_number,a->report_id,a->bit_offset,a->bits,a->flags,iface,raw,n,&value)) {
                m->analog_halves[i][j]=analog_half_value(a,value);touched=true;
            }
        }
    }
    resolve_shift(m);up_mapper_report(m,out);return touched;
}

bool up_mapper_motion(up_mapper *m,uint8_t iface,const uint8_t *raw,unsigned n,uint32_t time) {
    const up_profile *p=m->profile;
    if(!p || !(p->flags&UP_ENABLED) || p->vid!=0x054c || p->pid!=0x0268 || iface || n<49 || raw[0]!=1)return false;
    uint32_t keys=up_profile_recenter_keys(p),pressed=raw[2]|((uint32_t)raw[3]<<8)|((uint32_t)(raw[4]&1)<<16);
    uint16_t accel[3];
    for(unsigned i=0;i<3;++i)accel[i]=(uint16_t)(((uint16_t)raw[41+2*i]<<8)|raw[42+2*i]);
    uint16_t value=(uint16_t)(((uint16_t)raw[47]<<8)|raw[48]);
    uint32_t maximum_gap=((uint32_t)p->input_interval_ms+20u)*1000u;
    if(maximum_gap<100000u)maximum_gap=100000u;
    up_gyro_step step=up_gyro_update(&m->gyro,value,accel,true,time,maximum_gap,keys && (pressed&keys)==keys);
    bool touched=false;
    for(unsigned i=0;i<UP_AXIS_COUNT;++i) {
        const up_analog *a=&p->axes[i].analog;
        if(((p->axis_modes>>(2*i))&3)!=UP_AXIS_ANALOG || !(a->flags&UP_GYRO))continue;
        m->gyro_position[i]+=step.delta;
        // Keep overshoot so returning from full lock preserves the reference.
        const int64_t limit=INT64_C(1000000000)*256000;
        if(m->gyro_position[i]>limit)m->gyro_position[i]=limit;
        if(m->gyro_position[i]<-limit)m->gyro_position[i]=-limit;
        if(step.recentered)m->gyro_position[i]=0;
        up_analog scale=*a;scale.center=0;
        if(scale.deadzone_percent<5)scale.deadzone_percent=5;
        m->raw_axes[i]=analog_value(&scale,(int32_t)(m->gyro_position[i]/256000));touched=true;
    }
    return touched;
}
