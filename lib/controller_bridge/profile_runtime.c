#include "profile_runtime.h"
#include "input_protocol.h"
#include "controller_mouse.h"
#include <string.h>
#define FIELD_COUNT (UC_MAX_INTERFACES*UC_HID_MAX_FIELDS)
#define CHANGE_COUNT 256u
typedef struct {
    uc_hid_field field;
    int32_t value;
    uint32_t time;
    uint8_t iface,flags,valid,pad;
} observed_field;
typedef struct { uint16_t index,flags; int32_t value; uint32_t time; } field_change;
_Static_assert(sizeof(field_change)==12,"event wire ABI");
static up_bank bank;
static up_mapper mapper;
static up_release_guard switch_guard;
static observed_field fields[FIELD_COUNT];
static field_change changes[CHANGE_COUNT];
static uint8_t descriptor[UC_REPORT_DESCRIPTOR_CAPACITY];
static uint8_t device_descriptor[18],configuration_descriptor[UC_CONFIG_CAPACITY];
static uint16_t device_descriptor_size,configuration_descriptor_size;
static uc_hid_layout layout;
static unsigned received,field_count;
static uint32_t connection_generation,head,descriptor_crc;
static uint16_t vid,pid,revision;
static int active=-1;
static int mouse_x=-1,mouse_y=-1,mouse_wheel=-1;
static um_fraction mouse_fraction;
static bool connected,partial,persisted,binding_error;
static uint8_t host_status,input_protocol;
static uc_event last_input;
static uint32_t input_initialization;
static up_profile staged;
static uint8_t seen[UP_PROFILE_BYTES/8];
static int stage_slot=-1;
static uint32_t stage_crc,stage_generation;
static uint8_t staged_id[UP_ID_BYTES];
static bool stage_has_id;
static bool stage_registration;
static uint32_t stage_connection_generation;
static up_bank staged_bank;
static unsigned bank_received;
static uint32_t bank_stage_crc,bank_stage_generation;
static bool bank_staging;
static int pending_selection=-1;
static bool selection_unsaved;
static unsigned group_count,profile_number;
static uint16_t get16(const uint8_t *p) { uint16_t v;memcpy(&v,p,2);return v; }
static uint32_t get32(const uint8_t *p) { uint32_t v;memcpy(&v,p,4);return v; }
static void put16(uint8_t *p,uint16_t v) { memcpy(p,&v,2); }
static void put32(uint8_t *p,uint32_t v) { memcpy(p,&v,4); }
static void crc_add(const uint8_t *p,unsigned n) {
    for(unsigned i=0;i<n;++i) {
        descriptor_crc^=p[i];
        for(unsigned j=0;j<8;++j)descriptor_crc=(descriptor_crc>>1)^(0xedb88320u&(0u-(descriptor_crc&1)));
    }
}
static bool source_exists(uint8_t iface,uint8_t id,uint16_t offset,uint8_t bits,uint8_t flags) {
    for(unsigned i=0;i<field_count;++i) {
        const observed_field *f=&fields[i];
        if(f->iface==iface && f->field.report_id==id && f->field.bit_offset==offset && f->field.bits==bits &&
           f->flags==(flags&3) && !(f->field.flags&4))return true;
    }
    return false;
}
static bool digital_exists(const up_digital *d) {
    return !d->mode || source_exists(d->interface_number,d->report_id,d->bit_offset,d->bits,d->flags);
}
static bool profile_exists(const up_profile *p) {
    for(unsigned i=0;i<UP_DIGITAL_COUNT;++i)if(!digital_exists(&p->digital[i]))return false;
    for(unsigned i=0;i<UP_AXIS_COUNT;++i) {
        unsigned mode=(p->axis_modes>>(2*i))&3;
        if(mode==UP_AXIS_ANALOG) {
            const up_analog *a=&p->axes[i].analog;
            if(!source_exists(a->interface_number,a->report_id,a->bit_offset,a->bits,a->flags))return false;
        } else if(mode==UP_AXIS_BUTTON_PAIR && (!digital_exists(&p->axes[i].pair[0]) || !digital_exists(&p->axes[i].pair[1])))return false;
    }
    return true;
}
static void refresh_group(void) {
    unsigned mask=up_group_mask(&bank,upr_active_slot());
    group_count=profile_number=0;
    for(unsigned i=0;i<UP_PROFILE_COUNT;++i)if(mask&(1u<<i)) {
        group_count++;if((int)i<=active)profile_number++;
    }
}
static void seed_buttons(void) {
    if(active<0)return;
    for(unsigned i=0;i<UP_DIGITAL_COUNT;++i) {
        const up_digital *d=&bank.profiles[active].digital[i];if(!d->mode)continue;
        for(unsigned j=0;j<field_count;++j) {
            const observed_field *f=&fields[j];
            if(f->valid && f->iface==d->interface_number && f->field.report_id==d->report_id &&
               f->field.bit_offset==d->bit_offset && f->field.bits==d->bits && f->flags==(d->flags&3)) {
                mapper.digital[i]=up_digital_pressed_value(d,f->value);
                if(mapper.digital[i])mapper.physical|=1u<<i;
                break;
            }
        }
    }
}
static void select_profile(void) {
    upr_mouse_reset();
    active=connected?up_find_profile(&bank,vid,pid,revision,~descriptor_crc):-1;
    binding_error=active>=0 && !profile_exists(&bank.profiles[active]);
    if(binding_error)active=-1;
    refresh_group();
    up_mapper_attach(&mapper,active>=0?&bank.profiles[active]:NULL);
    seed_buttons();
    uc_request_poll_interval(active>=0?bank.profiles[active].input_interval_ms:0);
}
static uint8_t mouse_sources(void) { return (mouse_x>=0 && mouse_y>=0?1:0)|(mouse_wheel>=0?2:0); }
bool upr_mouse_ready(void) {
    if(!upr_ready())return false;
    unsigned enabled=bank.profiles[active].mouse.flags&3;
    return !enabled || (enabled&mouse_sources())!=0;
}
void upr_mouse_reset(void) { mouse_fraction=(um_fraction){0}; }
static bool mouse_field(int index,const uc_event *e,int32_t *value) {
    if(index<0)return false;
    const observed_field *f=&fields[index];
    return e->interface_number==f->iface && uc_hid_field_value(&f->field,(f->flags&UP_REPORT_ID)!=0,e->data,e->length,value);
}
bool upr_mouse_delta(const uc_event *e,int32_t *x,int32_t *y) {
    *x=*y=0;
    if(e->type!=UC_INPUT || !upr_ready())return false;
    const up_mouse_options *options=&bank.profiles[active].mouse;
    int32_t dx=0,dy=0,wheel=0;bool touched=false;
    if((options->flags&UP_MOUSE_TRACKBALL) && mouse_field(mouse_x,e,&dx) && mouse_field(mouse_y,e,&dy))touched=true;
    else dx=dy=0;
    if((options->flags&UP_MOUSE_PADDLE) && mouse_field(mouse_wheel,e,&wheel))touched=true;
    else wheel=0;
    if(!touched)return false;
#ifdef BRIDGE_MOUSE_FIXED_PROBE
    um_fixed_delta(options,dx,dy,2,BRIDGE_MOUSE_FIXED_AMPLITUDE,x,y);
#else
    um_mix(&mouse_fraction,options,dx,dy,wheel,x,y);
#endif
    return true;
}
void upr_init(void) {
    bank_staging=false;bank_received=0;stage_slot=-1;stage_registration=false;
    mouse_x=mouse_y=mouse_wheel=-1;upr_mouse_reset();
    switch_guard.keys=switch_guard.held=0;
    pending_selection=-1;selection_unsaved=false;
    group_count=profile_number=0;
    persisted=up_registry_load(&bank);
    descriptor_crc=~0u;up_mapper_attach(&mapper,NULL);
}
bool upr_ready(void) { return connected && active>=0; }
unsigned upr_field_count(void) { return field_count; }
unsigned upr_active_slot(void) { return active<0?255:(unsigned)active; }
uint8_t upr_input_interval(void) { return active<0?0:bank.profiles[active].input_interval_ms; }
uint8_t upr_output_options(void) { return up_profile_output_options(active<0?NULL:&bank.profiles[active]); }
uint32_t upr_led_rgb(void) { return up_profile_led_rgb(active<0?NULL:&bank.profiles[active]); }
void upr_output_report(uint32_t now,sw_report *report) {
    if(active<0)sw_neutral(report);else up_mapper_tick(&mapper,now,false,report);
}
uint32_t upr_profile_crc(void) { return active<0?0:up_crc32(&bank.profiles[active],sizeof(up_profile)); }
uint32_t upr_bank_generation(void) { return bank.generation; }
unsigned upr_group_count(void) { return group_count; }
unsigned upr_profile_number(void) { return profile_number; }
bool upr_controls_idle(void) {
    if(!upr_ready() || !last_input.length)return false;
    return upr_switch_inputs()==0;
}
uint32_t upr_switch_inputs(void) {
    uint32_t physical=mapper.physical;
    for(unsigned i=0;i<UP_AXIS_COUNT;++i)
        if(mapper.pairs[i][0] || mapper.pairs[i][1] || mapper.axes[i]<112 || mapper.axes[i]>144)physical|=1u<<31;
    return physical;
}
uint32_t upr_switch_keys(void) { return active<0?0:bank.switch_keys[active]; }
bool upr_switch_released(void) { return !switch_guard.held; }
static void activate(unsigned slot) {
    upr_mouse_reset();
    active=(int)slot;binding_error=false;up_mapper_attach(&mapper,&bank.profiles[slot]);
    seed_buttons();
    refresh_group();
    uc_request_poll_interval(bank.profiles[slot].input_interval_ms);
}
bool upr_cycle_profile(void) {
    if(!upr_ready() || stage_slot>=0)return false;
    unsigned mask=up_group_mask(&bank,(unsigned)active);
    for(unsigned step=1;step<UP_PROFILE_COUNT;++step) {
        unsigned next=((unsigned)active+step)%UP_PROFILE_COUNT;
        if((mask&(1u<<next)) && profile_exists(&bank.profiles[next])) {
            up_release_guard_begin(&switch_guard,&bank.profiles[active],upr_switch_keys());
            activate(next);pending_selection=(int)next;selection_unsaved=true;return true;
        }
    }
    return false;
}
bool upr_selection_pending(void) { return pending_selection>=0; }
bool upr_save_selection(void) {
    if(pending_selection<0)return true;
    unsigned slot=(unsigned)pending_selection;pending_selection=-1;
    bool success=up_registry_select(&bank,slot);
    selection_unsaved=!success;if(success)persisted=true;
    return success;
}
bool upr_event(const uc_event *e) {
    if(e->type==UC_STATUS) {
        host_status=e->code;
        if(e->code==UC_PROBING || e->code==UC_DISCONNECTED) {
            // Registration belongs to this attachment, even if the same model returns.
            if(stage_registration)stage_slot=-1;
            switch_guard.keys=switch_guard.held=0;
            connected=false;partial=false;binding_error=false;active=-1;field_count=received=0;
            mouse_x=mouse_y=mouse_wheel=-1;upr_mouse_reset();
            group_count=profile_number=0;
            vid=pid=revision=0;descriptor_crc=~0u;connection_generation++;
            device_descriptor_size=configuration_descriptor_size=0;
            input_protocol=0;memset(&last_input,0,sizeof(last_input));
            input_initialization=0;
            memset(fields,0,sizeof(fields));up_mapper_attach(&mapper,NULL);return true;
        }
        if(e->code==UC_DEVICE_FOUND) { vid=(uint16_t)(e->sequence>>16);pid=(uint16_t)e->sequence; }
        if(e->code==UC_INPUT_INIT)input_initialization=e->sequence;
        if(e->code==UC_INPUT_PROTOCOL) {
            input_protocol=(uint8_t)e->sequence;
            // A synthetic schema alone cannot distinguish Xbox-compatible pads.
            // Include their actual USB identity/layout; use the HID descriptor hash for standard devices.
            if(input_protocol&(UC_PROTOCOL_XBOX360|UC_PROTOCOL_SWITCH_PRO)) {
                uint8_t tag[]={'X','3','6','0',1};
                if(input_protocol&UC_PROTOCOL_SWITCH_PRO)memcpy(tag,"SWP1",4);
                crc_add(tag,sizeof(tag));crc_add(device_descriptor,device_descriptor_size);
                crc_add(configuration_descriptor,configuration_descriptor_size);
            }
        }
        if(e->code==UC_CONFIGURED) { connected=true;select_profile();return true; }
    } else if(e->type==UC_DESCRIPTOR && e->code==1 && e->length>=14) {
        vid=get16(e->data+8);pid=get16(e->data+10);revision=get16(e->data+12);
        if(!e->offset && e->length==sizeof(device_descriptor)) {
            memcpy(device_descriptor,e->data,sizeof(device_descriptor));device_descriptor_size=sizeof(device_descriptor);
        }
    } else if(e->type==UC_DESCRIPTOR && e->code==2) {
        if(!e->offset)configuration_descriptor_size=0;
        if(e->offset==configuration_descriptor_size && e->offset+e->length<=sizeof(configuration_descriptor)) {
            memcpy(configuration_descriptor+e->offset,e->data,e->length);
            configuration_descriptor_size=(uint16_t)(e->offset+e->length);
        }
    } else if(e->type==UC_DESCRIPTOR && e->code==0x22) {
        if(!e->offset)received=0;
        if(e->offset!=received || e->offset+e->length>sizeof(descriptor)) { partial=true;return false; }
        memcpy(descriptor+e->offset,e->data,e->length);received+=e->length;
        if(received==e->total) {
            uint8_t prefix[]={e->interface_number,(uint8_t)received,(uint8_t)(received>>8)};
            crc_add(prefix,sizeof(prefix));crc_add(descriptor,received);
            if(!uc_hid_parse(descriptor,received,&layout) || field_count+layout.count>FIELD_COUNT) { partial=true;return false; }
            if(layout.unsupported_inputs)partial=true;
            for(unsigned i=0;i<layout.count;++i) {
                const uc_hid_field *f=&layout.fields[i];
                if((unsigned)f->bit_offset+f->bits>(layout.has_report_ids?504u:512u) ||
                   (layout.has_report_ids && !f->report_id) || f->logical_max<f->logical_min) { partial=true;continue; }
                observed_field *o=&fields[field_count++];o->field=*f;o->iface=e->interface_number;
                o->flags=(f->logical_min<0?UP_SIGNED:0)|(layout.has_report_ids?UP_REPORT_ID:0);
            }
            // Trackball X/Y share a report; a paddle Wheel may use its own report.
            if(mouse_x<0)for(unsigned x=0;x<field_count && mouse_x<0;++x) {
                const observed_field *fx=&fields[x];
                if(fx->field.usage_page!=1 || fx->field.usage!=0x30 || (fx->field.flags&7)!=6)continue;
                for(unsigned y=0;y<field_count;++y) {
                    const observed_field *fy=&fields[y];
                    if(fy->iface==fx->iface && fy->field.report_id==fx->field.report_id &&
                       fy->field.usage_page==1 && fy->field.usage==0x31 && (fy->field.flags&7)==6) {
                        mouse_x=(int)x;mouse_y=(int)y;break;
                    }
                }
            }
            if(mouse_wheel<0)for(unsigned i=0;i<field_count;++i) {
                const uc_hid_field *f=&fields[i].field;
                if(f->usage_page==1 && f->usage==0x38 && (f->flags&7)==6) { mouse_wheel=(int)i;break; }
            }
        }
    } else if(e->type==UC_INPUT) {
        last_input=*e;
        if(switch_guard.keys)up_release_guard_apply(&switch_guard,e->interface_number,e->data,e->length);
        for(unsigned i=0;i<field_count;++i) {
            observed_field *f=&fields[i];int32_t value;
            if(f->iface!=e->interface_number || !uc_hid_field_value(&f->field,(f->flags&UP_REPORT_ID)!=0,e->data,e->length,&value))continue;
            if(!f->valid || value!=f->value) {
                f->value=value;f->time=e->time_us;f->valid=1;
                changes[head%CHANGE_COUNT]=(field_change){(uint16_t)i,1,value,e->time_us};head++;
            }
        }
    }
    return false;
}
bool upr_apply(const uc_event *e,uint32_t now,bool output_ready,sw_report *out) {
    if(!up_mapper_apply(&mapper,e->interface_number,e->data,e->length,out))return false;
    up_mapper_tick(&mapper,now,output_ready,out);return true;
}
bool upr_tick(uint32_t now,bool output_ready,sw_report *out) {
    return upr_ready() && mapper.rapid_configured && up_mapper_tick(&mapper,now,output_ready,out);
}
bool upr_command(uint8_t cmd,const uint8_t arg[56],uint8_t reply[56],uint8_t *status,bool busy,bool *changed) {
    *changed=false;
    switch(cmd) {
    case 20:
        memcpy(reply,"UPG1",4);put16(reply+4,1);put16(reply+6,UP_PROFILE_BYTES);reply[8]=UP_PROFILE_COUNT;
        reply[9]=(uint8_t)upr_active_slot();put16(reply+10,(uint16_t)field_count);put16(reply+12,vid);
        put16(reply+14,pid);put16(reply+16,revision);
        put16(reply+18,(connected?1:0)|(partial?2:0)|(persisted?4:0)|(binding_error?8:0)|(selection_unsaved?16:0));
        put32(reply+20,connection_generation);put32(reply+24,~descriptor_crc);put32(reply+28,bank.generation);put32(reply+32,head);
        reply[36]=active<0?0:bank.profiles[active].input_interval_ms;reply[37]=host_status;reply[38]=input_protocol;
        put32(reply+40,input_initialization);
        put32(reply+44,95|512|UPR_FEATURE_BANK_TRANSFER|UPR_FEATURE_PROFILE_OUTPUT|UPR_FEATURE_GUARDED_REGISTRATION);reply[48]=UP_RAPID_MIN_HZ;reply[49]=UP_RAPID_MAX_HZ;
        reply[50]=(uint8_t)upr_profile_number();reply[51]=(uint8_t)upr_group_count();
        put32(reply+52,upr_switch_keys());
        break;
    case 21: {
        unsigned index=get16(arg);if(index>=field_count) { *status=1;break; }
        const observed_field *o=&fields[index];const uc_hid_field *f=&o->field;
        put32(reply,connection_generation);put16(reply+4,(uint16_t)field_count);put16(reply+6,(uint16_t)index);
        reply[8]=o->iface;reply[9]=f->report_id;reply[10]=f->bits;reply[11]=o->flags;
        put16(reply+12,f->bit_offset);put16(reply+14,f->usage_page);put16(reply+16,f->usage);put16(reply+18,f->flags);
        put32(reply+20,(uint32_t)f->logical_min);put32(reply+24,(uint32_t)f->logical_max);put32(reply+28,(uint32_t)o->value);
        put32(reply+32,o->time);reply[36]=o->valid;break;
    }
    case 22: {
        uint32_t cursor=get32(arg),available=head-cursor;bool lost=available>CHANGE_COUNT;
        if(lost)cursor=head-CHANGE_COUNT;
        put32(reply+4,connection_generation);
        for(unsigned i=0;i<4 && cursor!=head;++i,++cursor) {
            field_change c=changes[cursor%CHANGE_COUNT];if(!i && lost)c.flags|=2;
            memcpy(reply+8+12*i,&c,sizeof(c));
        }
        put32(reply,cursor);break;
    }
    case 23: {
        unsigned slot=arg[0],offset=get16(arg+1);
        if(slot>=UP_PROFILE_COUNT || offset>=UP_PROFILE_BYTES) { *status=1;break; }
        unsigned n=UP_PROFILE_BYTES-offset;if(n>56)n=56;
        memcpy(reply,(const uint8_t *)&bank.profiles[slot]+offset,n);break;
    }
    case 24:
    case UPR_CMD_REGISTER_BEGIN:
        if(busy || pending_selection>=0) { *status=2;break; }
        bank_staging=false;
        stage_slot=-1;stage_registration=false;
        if(arg[9]>1) { *status=1;break; }
        if(arg[0]>=UP_PROFILE_COUNT || get32(arg+5)!=bank.generation) { *status=4;break; }
        if(cmd==UPR_CMD_REGISTER_BEGIN) {
            if(arg[9]!=1 || !connected || (bank.profiles[arg[0]].flags&UP_ENABLED) ||
               get32(arg+26)!=connection_generation || get32(arg+30)!=~descriptor_crc ||
               get16(arg+34)!=vid || get16(arg+36)!=pid || get16(arg+38)!=revision || arg[40]!=input_protocol) {
                *status=4;break;
            }
            stage_registration=true;stage_connection_generation=connection_generation;
        }
        stage_slot=arg[0];stage_crc=get32(arg+1);stage_generation=get32(arg+5);
        stage_has_id=arg[9]!=0;memcpy(staged_id,arg+10,UP_ID_BYTES);
        memset(&staged,0,sizeof(staged));memset(seen,0,sizeof(seen));break;
    case 25: {
        unsigned offset=get16(arg),n=arg[2];
        if(stage_slot<0) { *status=stage_registration?4:1;break; }
        if(!n || n>52 || offset+n>UP_PROFILE_BYTES) { *status=1;break; }
        memcpy((uint8_t *)&staged+offset,arg+4,n);
        for(unsigned i=offset;i<offset+n;++i)seen[i/8]|=(uint8_t)(1u<<(i%8));
        break;
    }
    case 26:
        if(busy) { *status=2;break; }
        if(stage_slot<0 || stage_generation!=bank.generation) { *status=4;break; }
        for(unsigned i=0;i<sizeof(seen);++i)if(seen[i]!=255) { *status=1;return true; }
        if(up_crc32(&staged,sizeof(staged))!=stage_crc || !up_profile_valid(&staged)) { *status=1;break; }
        if(stage_registration && (!connected || stage_connection_generation!=connection_generation ||
           (bank.profiles[stage_slot].flags&UP_ENABLED) ||
           (staged.flags&(UP_ENABLED|UP_MATCH_DESCRIPTOR))!=(UP_ENABLED|UP_MATCH_DESCRIPTOR) ||
           staged.vid!=vid || staged.pid!=pid || staged.bcd_device!=revision ||
           staged.descriptor_hash!=~descriptor_crc || !profile_exists(&staged))) {
            stage_slot=-1;*status=4;break;
        }
        if(!up_registry_save_identified(&bank,(unsigned)stage_slot,&staged,stage_has_id?staged_id:NULL)) { *status=3;break; }
        stage_slot=-1;persisted=true;selection_unsaved=false;select_profile();*changed=true;put32(reply,bank.generation);break;
    case UPR_CMD_MOUSE_PROFILE:
        memcpy(reply,"MPR1",4);reply[4]=(uint8_t)upr_active_slot();reply[5]=mouse_sources();
        if(active>=0)memcpy(reply+8,&bank.profiles[active].mouse,sizeof(up_mouse_options));
        break;
    case UPR_CMD_SELECT_PROFILE: {
        unsigned slot=arg[0];
        if(busy || stage_slot>=0 || pending_selection>=0) { *status=2;break; }
        if(get32(arg+1)!=bank.generation) { *status=4;break; }
        if(!upr_ready() || !(up_group_mask(&bank,(unsigned)active)&(1u<<(slot&31))) || slot>=UP_PROFILE_COUNT ||
           !profile_exists(&bank.profiles[slot])) { *status=1;break; }
        if(!up_registry_select(&bank,slot)) { *status=3;break; }
        activate(slot);persisted=true;selection_unsaved=false;*changed=true;put32(reply,bank.generation);break;
    }
    case UPR_CMD_SWITCH_KEYS: {
        unsigned slot=arg[1];uint32_t keys=get32(arg+2);
        if(arg[0]>1 || slot>=UP_PROFILE_COUNT || !(bank.profiles[slot].flags&UP_ENABLED)) { *status=1;break; }
        if(arg[0]) {
            if(busy || stage_slot>=0 || pending_selection>=0) { *status=2;break; }
            if(get32(arg+6)!=bank.generation) { *status=4;break; }
            if(!up_switch_keys_valid(keys)) { *status=1;break; }
            unsigned group=up_group_mask(&bank,slot);
            for(unsigned i=0;i<UP_PROFILE_COUNT;++i)if(group&(1u<<i))
                for(unsigned j=0;j<UP_DIGITAL_COUNT;++j)if((keys&(1u<<j)) && !bank.profiles[i].digital[j].mode) { *status=1;return true; }
            if(!up_registry_switch_keys(&bank,slot,keys)) { *status=3;break; }
            persisted=true;
        }
        put32(reply,bank.switch_keys[slot]);put32(reply+4,bank.generation);break;
    }
    case UPR_CMD_PROFILE_ID:
        if(arg[0]>=UP_PROFILE_COUNT) { *status=1;break; }
        memcpy(reply,bank.profile_ids[arg[0]],UP_ID_BYTES);
        put32(reply+16,bank.generation);put32(reply+20,up_crc32(&bank.profiles[arg[0]],UP_PROFILE_BYTES));break;
    case UPR_CMD_BANK_INFO:
        memcpy(reply,"UPB1",4);put32(reply+4,sizeof(bank));put32(reply+8,bank.generation);
        put32(reply+12,up_crc32(&bank,sizeof(bank)));break;
    case UPR_CMD_BANK_READ: {
        unsigned offset=get16(arg);
        if(get32(arg+2)!=bank.generation) { *status=4;break; }
        if(offset>=sizeof(bank)) { *status=1;break; }
        unsigned n=sizeof(bank)-offset;if(n>56)n=56;
        memcpy(reply,(const uint8_t *)&bank+offset,n);break;
    }
    case UPR_CMD_BANK_BEGIN:
        if(busy || pending_selection>=0) { *status=2;break; }
        bank_staging=false;
        if(get32(arg)!=bank.generation) { *status=4;break; }
        if(get32(arg+8)!=sizeof(bank)) { *status=1;break; }
        stage_slot=-1;bank_stage_generation=get32(arg);bank_stage_crc=get32(arg+4);
        bank_received=0;bank_staging=true;break;
    case UPR_CMD_BANK_CHUNK: {
        unsigned offset=get16(arg),n=arg[2];
        // Sequential chunks make a missing or repeated write detectable without
        // another per-byte bitmap. BEGIN can safely restart an interrupted upload.
        if(!bank_staging || offset!=bank_received || !n || n>52 || offset+n>sizeof(staged_bank)) { *status=1;break; }
        memcpy((uint8_t *)&staged_bank+offset,arg+4,n);bank_received+=n;break;
    }
    case UPR_CMD_BANK_COMMIT:
        if(busy || pending_selection>=0) { *status=2;break; }
        if(!bank_staging || bank_stage_generation!=bank.generation) { *status=4;break; }
        if(bank_received!=sizeof(staged_bank) || up_crc32(&staged_bank,sizeof(staged_bank))!=bank_stage_crc ||
           !up_bank_valid(&staged_bank)) { *status=1;break; }
        if(!up_registry_replace(&bank,&staged_bank)) { *status=3;break; }
        bank_staging=false;persisted=true;selection_unsaved=false;
        switch_guard.keys=switch_guard.held=0;select_profile();*changed=true;
        put32(reply,bank.generation);put32(reply+4,up_crc32(&bank,sizeof(bank)));break;
    case UPR_CMD_BANK_ABORT:
        bank_staging=false;bank_received=0;break;
    case 29: {
        // Bounded read-only diagnostic. Generation prevents mixing devices.
        const uint8_t *data;unsigned size,offset=get16(arg+2);
        if(arg[0]==1) { data=device_descriptor;size=device_descriptor_size; }
        else if(arg[0]==2) { data=configuration_descriptor;size=configuration_descriptor_size; }
        else { *status=1;break; }
        if(offset>size) { *status=1;break; }
        unsigned n=size-offset;if(n>44)n=44;
        put16(reply,(uint16_t)size);put16(reply+2,(uint16_t)offset);put16(reply+4,(uint16_t)n);
        put32(reply+8,connection_generation);memcpy(reply+12,data+offset,n);break;
    }
    case 30: {
        unsigned offset=arg[0],size=last_input.length;
        if(offset>size || size>UC_EVENT_BYTES) { *status=1;break; }
        unsigned n=size-offset;if(n>44)n=44;
        reply[0]=(uint8_t)size;reply[1]=(uint8_t)n;reply[2]=last_input.interface_number;reply[3]=last_input.endpoint;
        put32(reply+4,last_input.sequence);put32(reply+8,last_input.time_us);
        memcpy(reply+12,last_input.data+offset,n);break;
    }
    default:return false;
    }
    return true;
}
