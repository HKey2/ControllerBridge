#include "pico/stdlib.h"
#include "pico/multicore.h"
#include "pico/bootrom.h"
#include "hardware/clocks.h"
#include "hardware/sync.h"
#include "hardware/watchdog.h"
#include "hardware/structs/watchdog.h"
#include "hardware/exception.h"
#include "controller_bridge.h"
#include "switch_protocols.h"
#include "egret_switch_profile.h"
#include <stdatomic.h>
#include <string.h>

// Bounded, hypothesis-driven comparison. Each result is committed before the
// next USB identity is attached; a PC boot never automatically starts a run.
#define PROBE_MAGIC 0x31425250u
#define EVENT_COUNT 128
typedef struct { uint32_t count,min,max,step; uint64_t sum; uint32_t bins[16]; } histogram;
typedef struct {
    uint32_t profile,start_us,end_us,configured_begin,configured_end;
    uint32_t acks,naks,sof,control_stalls,bus_errors,abort_timeouts,resets,setups;
    uint32_t input_reports,input_changes,input_errors,input_disconnects,event_drops,transition_drops,max_depth,transitions_acked;
    uint32_t normal_acks,reply_acks,out_packets,out_drops,unknown_commands,handshake_mask,subcommand_mask,report_mode,reserved[3];
    histogram ack_gap,transition_ack;
    uint8_t trace[16][8];
} result;
typedef struct {
    uint32_t magic,version,bytes,state,token,duration_ms,completed,profiles,save_errors,trigger,reserved[2];
    result results[SP_COUNT];
} experiment;
_Static_assert(sizeof(result)==432,"Probe ABI");
_Static_assert(sizeof(experiment)==3504,"Probe ABI");
_Static_assert(sizeof(experiment)<=4080,"Flash sector capacity");
static experiment record;
static ub_configuration config;
static uc_event events[EVENT_COUNT];
static atomic_uint head,tail,dropped;
static uc_hid_layout layout; static egret_fields fields;
static uint8_t hid[UC_REPORT_DESCRIPTOR_CAPACITY]; static unsigned hid_length;
static bool input_ready,layout_valid;
static uc_statistics host_stats;
static ub_queue outgoing; static ub_frame flight;
static bool flight_valid,flight_reply;
static uint32_t sequence;
static atomic_bool ack_pending,command_pending;
static bool ack_ok; static uint32_t ack_time;
static uint8_t command[64],response[64]={'U','C','2','R',1};
typedef struct { uint8_t data[64]; uint16_t length; bool auxiliary; } packet;
static packet outputs[16]; static atomic_uint out_head,out_tail,out_drops;
static uint8_t replies[8][64]; static unsigned reply_read,reply_write;
static uint8_t auxiliary_reply[128]; static unsigned auxiliary_length;
static sp_state protocol;
static unsigned profile;
static uint32_t state,deadline,chord_since,reboot_at,last_ack,trace_count;
static uint32_t errors_begin,drops_begin;
static ud_statistics device_begin;
static uint32_t reset_trace[8];
static void __attribute__((used)) fault_record(uint32_t *frame) {
    watchdog_hw->scratch[0]|=0x80000000;
    watchdog_hw->scratch[1]=frame[6]; watchdog_hw->scratch[2]=frame[5];
    watchdog_hw->scratch[3]=*(volatile uint32_t *)0xe000ed28;
    for(;;)__asm volatile("nop");
}
static void __attribute__((naked)) fault_handler(void) { __asm volatile("mrs r0,msp\n b fault_record"); }
static result *active;
enum { IDLE,DETACHED,WARMUP,MEASURING };
static bool expired(uint32_t now,uint32_t target) { return (int32_t)(now-target)>=0; }
static void hist_reset(histogram *h) { memset(h,0,sizeof(*h)); h->min=~0u; h->step=1000; }
static void hist_add(histogram *h,uint32_t us) {
    h->count++; h->sum+=us; if(us<h->min)h->min=us; if(us>h->max)h->max=us;
    unsigned bin=us/h->step; if(bin>15)bin=15; h->bins[bin]++;
}
static void host_event(const uc_event *e,void *ctx) {
    (void)ctx; unsigned w=atomic_load_explicit(&head,memory_order_relaxed),next=(w+1)%EVENT_COUNT;
    if(next==atomic_load_explicit(&tail,memory_order_acquire)) { atomic_fetch_add(&dropped,1); return; }
    events[w]=*e; atomic_store_explicit(&head,next,memory_order_release);
}
static bool take_event(uc_event *e) {
    unsigned r=atomic_load_explicit(&tail,memory_order_relaxed);
    if(r==atomic_load_explicit(&head,memory_order_acquire))return false;
    *e=events[r]; atomic_store_explicit(&tail,(r+1)%EVENT_COUNT,memory_order_release); return true;
}
static bool accept(uint16_t vid,uint16_t pid,void *ctx) { (void)ctx; return vid==0xae4&&pid==0x703; }
static void host_core(void) { uc_host_options o={.dp_pin=12,.accept=accept,.event=host_event}; uc_host_run(&o); }
static uint16_t get_feature(uint8_t *data,uint16_t capacity,void *ctx) {
    (void)ctx; unsigned n=capacity<64?capacity:64; memcpy(data,response,n); return n;
}
static void set_feature(const uint8_t *data,uint16_t n,void *ctx) {
    (void)ctx; if(n!=64||memcmp(data,"UC2C",4)||data[4]!=1||atomic_load(&command_pending))return;
    memcpy(command,data,64); atomic_store_explicit(&command_pending,true,memory_order_release);
}
static void complete(uint32_t cookie,uint32_t when,bool ok,void *ctx) {
    (void)cookie; (void)ctx; ack_time=when; ack_ok=ok; atomic_store_explicit(&ack_pending,true,memory_order_release);
}
static void queue_output(const uint8_t *data,uint16_t length,bool auxiliary) {
    unsigned w=atomic_load(&out_head),next=(w+1)%16;
    if(next==atomic_load_explicit(&out_tail,memory_order_acquire)) { atomic_fetch_add(&out_drops,1); return; }
    outputs[w].length=length; outputs[w].auxiliary=auxiliary; memcpy(outputs[w].data,data,length);
    atomic_store_explicit(&out_head,next,memory_order_release);
}
static void receive_output(const uint8_t *data,uint16_t length,void *ctx) { (void)ctx;queue_output(data,length,false); }
static void receive_auxiliary(const uint8_t *data,uint16_t length,void *ctx) { (void)ctx;queue_output(data,length,true); }
static void observe_setup(const uint8_t setup[8],void *ctx) {
    (void)ctx; uint32_t a,b; memcpy(&a,setup,4); memcpy(&b,setup+4,4);
    watchdog_hw->scratch[1]=a; watchdog_hw->scratch[2]=b; watchdog_hw->scratch[3]++;
}
static void observe_event(uint32_t event,uint32_t detail,void *ctx) {
    (void)ctx; watchdog_hw->scratch[4]=event; watchdog_hw->scratch[5]=detail;
    if(event==1)watchdog_hw->scratch[6]=detail;
    if(event==2)watchdog_hw->scratch[7]=detail;
}
static void attach(unsigned p) {
    watchdog_hw->scratch[0]=100+p;
    profile=p; flight_valid=false; atomic_store(&ack_pending,false); ub_queue_reset(&outgoing);
    memset(&protocol,0,sizeof(protocol)); reply_read=reply_write=0; auxiliary_length=0;
    atomic_store(&out_head,0); atomic_store(&out_tail,0); atomic_store(&out_drops,0); trace_count=0;
    ud_options o={.get_feature=get_feature,.set_feature=set_feature,.complete=complete,.output=receive_output,
        .auxiliary_output=receive_auxiliary,.observe_setup=observe_setup,.observe_event=observe_event};
    sp_descriptors(p,&o); sw_report neutral; sw_neutral(&neutral); uint8_t initial[64];
    sp_encode(p,&neutral,initial,time_us_32()); o.initial_input=initial; ud_init(&o);
    watchdog_hw->scratch[0]=200+p;
}
static void process_ack(void) {
    if(!atomic_exchange_explicit(&ack_pending,false,memory_order_acquire))return;
    if(ack_ok && flight_valid && state==MEASURING) {
        if(last_ack)hist_add(&active->ack_gap,ack_time-last_ack);
        last_ack=ack_time;
        if(flight_reply)active->reply_acks++;
        else {
            active->normal_acks++;
            if(flight.transition) {
                active->transitions_acked++;
                if((int32_t)(flight.received_us-active->start_us)>=0)hist_add(&active->transition_ack,ack_time-flight.received_us);
            }
        }
    }
    flight_valid=false;
}
static void process_output(void) {
    if(auxiliary_length) {
        if(!ud_auxiliary_send(auxiliary_reply,auxiliary_length))return;
        auxiliary_length=0;
    }
    unsigned r=atomic_load(&out_tail);
    if(r==atomic_load_explicit(&out_head,memory_order_acquire))return;
    packet *p=outputs+r;
    if(active && trace_count<16) {
        memcpy(active->trace[trace_count],p->data,p->length<4?p->length:4);
        if(p->length>10)memcpy(active->trace[trace_count]+4,p->data+10,p->length<14?p->length-10:4);
        trace_count++;
    }
    if(profile>=SP_NSO4) {
        if(p->auxiliary)auxiliary_length=sp_nso_command(&protocol,p->data,p->length,auxiliary_reply);
        else protocol.output_count++;
    } else if(profile>=SP_PRO8) {
        unsigned next=(reply_write+1)%8;
        if(next==reply_read)atomic_fetch_add(&out_drops,1);
        else {
            sw_report neutral; sw_neutral(&neutral);
            if(sp_command(&protocol,p->data,p->length,outgoing.have_latest?&outgoing.latest.report:&neutral,replies[reply_write],time_us_32()))reply_write=next;
        }
    } else protocol.output_count++;
    atomic_store_explicit(&out_tail,(r+1)%16,memory_order_release);
}
static void schedule(void) {
    if(state==DETACHED||!ud_ready()||atomic_load_explicit(&ack_pending,memory_order_acquire))return;
    bool reply=reply_read!=reply_write;
    if(flight_valid) {
        bool newer=outgoing.have_latest&&outgoing.latest.sequence!=flight.sequence;
        if(!flight_reply&&!flight.transition&&(reply||ub_queue_depth(&outgoing)||newer)) {
            if(!ud_cancel_repeat())return;
            flight_valid=false;
        } else return;
    }
    uint8_t wire[64]; bool queued=false;
    if(reply) { memcpy(wire,replies[reply_read],64); memset(&flight,0,sizeof(flight)); }
    else {
        if(profile>=SP_NSO4) { if(!protocol.active)return; }
        else if(profile>=SP_PRO8 && protocol.mode!=0x30)return;
        queued=ub_queue_depth(&outgoing)!=0;
        if(queued)flight=outgoing.pending[outgoing.read];
        else if(outgoing.have_latest) { flight=outgoing.latest; flight.transition=false; }
        else { memset(&flight,0,sizeof(flight)); sw_neutral(&flight.report); }
        sp_encode(profile,&flight.report,wire,time_us_32());
    }
    if(ud_send(wire,profile>=SP_PRO8?64:8,flight.sequence,NULL)) {
        flight_valid=true; flight_reply=reply;
        if(reply)reply_read=(reply_read+1)%8;
        else if(queued) { ub_frame unused; ub_queue_pop(&outgoing,&unused); }
    }
}
static void process_event(const uc_event *e) {
    if(e->type==UC_DESCRIPTOR&&e->code==0x22&&e->interface_number==0) {
        if(!e->offset) { hid_length=0; layout_valid=false; }
        if(e->offset==hid_length && e->offset+e->length<=sizeof(hid)) {
            memcpy(hid+e->offset,e->data,e->length); hid_length+=e->length;
            if(hid_length==e->total)layout_valid=uc_hid_parse(hid,hid_length,&layout)&&egret_prepare_fields(&layout,&fields);
        }
    } else if(e->type==UC_INPUT) {
        if(state==MEASURING)active->input_reports++;
        ub_frame f={.received_us=e->time_us,.sequence=++sequence};
        if(layout_valid&&egret_switch_report(&fields,e->data,e->length,&config,&f.report)) {
            bool changed=!outgoing.have_latest||memcmp(&outgoing.latest.report,&f.report,8);
            if(changed&&state==MEASURING)active->input_changes++;
            if(state!=DETACHED)ub_queue_push(&outgoing,&f);
            uint16_t buttons=f.report.buttons0|((uint16_t)f.report.buttons1<<8);
            if(state==IDLE&&record.state==1&&(buttons&(SW_MINUS|SW_PLUS))==(SW_MINUS|SW_PLUS)) {
                if(!chord_since)chord_since=time_us_32();
            } else chord_since=0;
        }
    } else if(e->type==UC_STATUS) {
        if(e->code==UC_CONFIGURED)input_ready=layout_valid;
        if(e->code==UC_DISCONNECTED) { input_ready=false; if(state==MEASURING)active->input_disconnects++; }
    } else if(e->type==UC_STATISTICS)memcpy(&host_stats,e->data,sizeof(host_stats));
}
static void detach_for(unsigned next) {
    ud_disconnect(); state=DETACHED; profile=next; deadline=time_us_32()+1000000;
    flight_valid=false; active=next<SP_COUNT?record.results+next:NULL;
}
static void start_run(unsigned trigger) {
    memset(record.results,0,sizeof(record.results)); record.state=2; record.completed=0; record.trigger=trigger;
    if(!ub_store_save(1,&record,sizeof(record),true))record.save_errors++;
    detach_for(0);
}
static void start_measurement(uint32_t now) {
    // Keep initialization trace; interval counters begin only after warmup.
    active->profile=profile; active->start_us=now; ud_statistics_get(&device_begin);
    active->configured_begin=device_begin.configured; errors_begin=host_stats.transfer_errors;
    drops_begin=atomic_load(&dropped); outgoing.drops=0; outgoing.max_depth=ub_queue_depth(&outgoing);
    hist_reset(&active->ack_gap); hist_reset(&active->transition_ack); last_ack=0;
    state=MEASURING; deadline=now+record.duration_ms*1000;
    watchdog_hw->scratch[0]=300+profile;
}
static void finish_measurement(uint32_t now) {
    process_ack(); ud_statistics d; ud_statistics_get(&d); active->end_us=now; active->configured_end=d.configured;
    #define DELTA(field) active->field=d.field-device_begin.field
    DELTA(acks); DELTA(naks); DELTA(sof); DELTA(control_stalls); DELTA(bus_errors); DELTA(abort_timeouts); DELTA(resets); DELTA(setups);
    #undef DELTA
    active->input_errors=host_stats.transfer_errors-errors_begin;
    active->event_drops=atomic_load(&dropped)-drops_begin; active->transition_drops=outgoing.drops; active->max_depth=outgoing.max_depth;
    active->out_packets=protocol.output_count; active->out_drops=atomic_load(&out_drops);
    active->unknown_commands=protocol.unknown_count; active->handshake_mask=protocol.handshake_mask;
    active->subcommand_mask=protocol.subcommand_mask; active->report_mode=protocol.mode;
    record.completed=profile+1; if(record.completed==SP_COUNT)record.state=3;
    detach_for(profile+1);
    if(!ub_store_save(1,&record,sizeof(record),true))record.save_errors++;
}
static uint32_t u32(const uint8_t *p) { uint32_t v; memcpy(&v,p,4); return v; }
static void handle_command(void) {
    if(!atomic_exchange_explicit(&command_pending,false,memory_order_acquire))return;
    uint8_t reply[64]={'U','C','2','R',1}; reply[5]=command[5]; reply[6]=command[6]; uint8_t *p=reply+8;
    switch(command[5]) {
    case 15:
        if(state!=IDLE)reply[7]=2;
        else if(u32(command+8)<2000||u32(command+8)>30000)reply[7]=1;
        else {
            memset(&record,0,sizeof(record)); record.magic=PROBE_MAGIC; record.version=1; record.bytes=sizeof(record);
            record.state=1; record.token=u32(command+12); record.duration_ms=u32(command+8); record.profiles=SP_COUNT;
            if(!ub_store_save(1,&record,sizeof(record),true))reply[7]=3;
        }
        break;
    case 16:
        memcpy(p,&record,48); memcpy(p+48,&state,4); memcpy(p+52,&profile,4); break;
    case 17: {
        unsigned offset=u32(command+8);
        if(state!=IDLE)reply[7]=2;
        else if(offset>=sizeof(record))reply[7]=1;
        else { unsigned n=sizeof(record)-offset; if(n>56)n=56; memcpy(p,(const uint8_t *)&record+offset,n); }
        break;
    }
    case 18:
        if(state!=IDLE||record.state!=1)reply[7]=1;
        else { record.trigger=2; deadline=time_us_32()+500000; }
        break;
    case 19: memcpy(p,reset_trace,sizeof(reset_trace)); break;
    case 9: reboot_at=time_us_32()+250000; break;
    default: reply[7]=1; break;
    }
    uint32_t irq=save_and_disable_interrupts(); memcpy(response,reply,64); restore_interrupts(irq);
}
int main(void) {
    set_sys_clock_khz(120000,true);
    if(watchdog_caused_reboot())for(unsigned i=0;i<8;++i)reset_trace[i]=watchdog_hw->scratch[i];
    watchdog_enable(3000,true);
    exception_set_exclusive_handler(HARDFAULT_EXCEPTION,fault_handler);
    if(!ub_store_load(0,&config,sizeof(config))||config.version!=UB_VERSION)egret_default_configuration(&config);
    if(!ub_store_load(1,&record,sizeof(record))||record.magic!=PROBE_MAGIC) {
        memset(&record,0,sizeof(record)); record.magic=PROBE_MAGIC; record.version=1; record.bytes=sizeof(record); record.profiles=SP_COUNT;
    }
    // Preserve interrupted run data for inspection; only an explicitly armed
    // record and a fresh physical chord can start another console experiment.
    uc_request_poll_interval(1); multicore_launch_core1(host_core); attach(SP_POWERA);
    for(;;) {
        watchdog_update();
        process_ack(); uc_event e;
        while(take_event(&e)) { process_event(&e); process_ack(); schedule(); }
        process_output(); schedule(); handle_command(); uint32_t now=time_us_32();
        if(state==IDLE&&record.state==1&&input_ready&&ud_ready()) {
            if((chord_since&&(uint32_t)(now-chord_since)>=2000000)||
               (record.trigger==2&&expired(now,deadline)))start_run(record.trigger==2?2:1);
        }
        if(state==DETACHED&&expired(now,deadline)) {
            if(profile==SP_COUNT) { attach(SP_POWERA); state=IDLE; active=NULL; }
            else { attach(profile); state=WARMUP; deadline=now+4000000; }
        } else if(state==WARMUP&&expired(now,deadline))start_measurement(now);
        else if(state==MEASURING&&expired(now,deadline))finish_measurement(now);
        if(reboot_at&&expired(now,reboot_at)) { ud_disconnect(); reset_usb_boot(0,0); }
        tight_loop_contents();
    }
}
