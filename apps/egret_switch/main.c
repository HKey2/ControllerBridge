#include "pico/stdlib.h"
#include "pico/multicore.h"
#include "pico/bootrom.h"
#include "hardware/clocks.h"
#include "hardware/sync.h"
#include "hardware/watchdog.h"
#include "controller_bridge.h"
#include "egret_switch_profile.h"
#include "profile_runtime.h"
#include "profile_button.h"
#include "controller_mouse.h"
#include "status_led.h"
#include "firmware_version.h"
#ifdef BRIDGE_USB_TRACE
#include "usb_control_trace.h"
#endif
#include <stdatomic.h>
#include <string.h>

#define EVENT_COUNT 128
enum { CMD_INFO=1,CMD_GET_CONFIG,CMD_SET_CONFIG,CMD_SAVE_CONFIG,CMD_START,CMD_STOP,
       CMD_SELECT_SNAPSHOT,CMD_READ_SNAPSHOT,CMD_BOOTSEL,CMD_ARM_BOOT,CMD_REBOOT=14,CMD_LED=31,CMD_BUTTON=34,CMD_MOUSE=36,CMD_MOUSE_WIRE=38,CMD_MOUSE_PROBE=39,CMD_APPLY_OUTPUT=46,CMD_FIRMWARE=47 };
#ifdef BRIDGE_MOUSE_FIXED_PROBE
enum { MOUSE_FIXED_PROBE=1, MOUSE_FIXED_AMPLITUDE=BRIDGE_MOUSE_FIXED_AMPLITUDE };
#else
enum { MOUSE_FIXED_PROBE=0, MOUSE_FIXED_AMPLITUDE=0 };
#endif
_Static_assert(UPR_CMD_PROFILE_ID>CMD_LED,"Profile identity command must not overlap application commands");
enum { MEASURE_IDLE,MEASURE_STARTING,MEASURE_RUNNING,MEASURE_STOPPING };
static uc_event events[EVENT_COUNT];
static atomic_uint event_head,event_tail,event_dropped;
static ub_configuration config;
static uint8_t active_output_interval;
static uint8_t active_output_options;
static um_queue mouse_queue;
static um_flight mouse_flight;
static uint8_t last_mouse_output[UM_REPORT_BYTES];
static bool mouse_flight_valid,mouse_was_ready;
static uint32_t mouse_drops_begin,mouse_button_drops_begin;
static uint32_t mouse_flushes_begin,mouse_input_x_begin,mouse_input_y_begin,mouse_acked_x_begin,mouse_acked_y_begin;
static uint32_t last_mouse_ack_sequence,last_mouse_armed_sequence,last_mouse_poll_ack;
static uint8_t output_configuration[59];
static bool input_ready;
static uc_statistics host_stats;
static ub_queue outgoing;
static ub_frame flight;
static bool flight_valid;
static uint32_t input_sequence,raw_reports,last_ack_sequence,last_armed_sequence;
static uint8_t last_raw[4];
static atomic_bool ack_pending,command_pending;
static atomic_bool mouse_ack_pending;
static uint32_t ack_cookie,ack_time;
static uint32_t mouse_ack_cookie,mouse_ack_time;
static bool ack_success;
static bool mouse_ack_success;
static uint8_t command[64],response[64]={'U','C','2','R',1};
static ub_snapshot measurement,selected_snapshot;
static bool measurement_valid,selected_valid,saved_valid,save_pending;
static uint32_t phase,duration_us,drops_begin,last_poll_ack,boot_capture_ms,ready_since;
static uint32_t start_not_before;
static bool start_request_pending;
static bool auto_save;
static uint32_t reboot_at;
static bool reboot_to_bootsel;
static bool output_change_requested,output_disconnected;
static uint32_t output_change_at;
static profile_button boot_button;
static uint32_t profile_switches,selection_save_at,selection_save_failures,selection_save_max_us;
static bool measuring(void) { return phase==MEASURE_RUNNING || phase==MEASURE_STOPPING; }
static bool mouse_mode(void) { return (active_output_options&UM_MOUSE)!=0; }
static bool combined_mode(void) { return (active_output_options&UM_COMBINED)!=0; }
static bool mouse_output_ready(void) { return combined_mode()?ud_secondary_ready():ud_ready(); }
static bool cancel_mouse_repeat(void) { return combined_mode()?ud_secondary_cancel_repeat():ud_cancel_repeat(); }
static bool load_snapshot(ub_snapshot *s) {
    memset(s,0,sizeof(*s));
    if(!ub_store_load(1,s,sizeof(*s)))return false;
    return s->magic==0x4d324355 && s->version==UB_VERSION && s->bytes==sizeof(*s);
}

static void host_event(const uc_event *e,void *context) {
    (void)context;
    unsigned write=atomic_load_explicit(&event_head,memory_order_relaxed),next=(write+1)%EVENT_COUNT;
    if(next==atomic_load_explicit(&event_tail,memory_order_acquire)) {
        atomic_fetch_add_explicit(&event_dropped,1,memory_order_relaxed); return;
    }
    events[write]=*e; atomic_store_explicit(&event_head,next,memory_order_release);
}
static bool next_event(uc_event *e) {
    unsigned read=atomic_load_explicit(&event_tail,memory_order_relaxed);
    if(read==atomic_load_explicit(&event_head,memory_order_acquire))return false;
    *e=events[read]; atomic_store_explicit(&event_tail,(read+1)%EVENT_COUNT,memory_order_release); return true;
}
static void host_core(void) {
    uc_host_options options={.dp_pin=12,.event=host_event}; uc_host_run(&options);
}
static uint16_t get_feature(uint8_t *data,uint16_t capacity,void *context) {
    (void)context; unsigned n=capacity<64?capacity:64; memcpy(data,response,n); return n;
}
static void set_feature(const uint8_t *data,uint16_t length,void *context) {
    (void)context;
    if(length!=64 || memcmp(data,"UC2C",4) || data[4]!=1 || atomic_load_explicit(&command_pending,memory_order_relaxed))return;
    memcpy(command,data,64); atomic_store_explicit(&command_pending,true,memory_order_release);
}
static void input_complete(uint32_t cookie,uint32_t time_us,bool acked,void *context) {
    (void)context;
    // Only one hardware packet can be outstanding. Main consumes this event
    // before arming another, so the mailbox cannot overwrite an earlier ACK.
    ack_cookie=cookie; ack_time=time_us; ack_success=acked;
    atomic_store_explicit(&ack_pending,true,memory_order_release);
}
static void mouse_complete(uint32_t cookie,uint32_t time_us,bool acked,void *context) {
    (void)context;
    // A separate mailbox prevents a mouse completion from overwriting a pad ACK.
    mouse_ack_cookie=cookie;mouse_ack_time=time_us;mouse_ack_success=acked;
    atomic_store_explicit(&mouse_ack_pending,true,memory_order_release);
}
static bool in_window(uint32_t timestamp) {
    return measuring() && (int32_t)(timestamp-measurement.start_us)>=0;
}
static void summary_add(ub_summary *s,uint32_t us) {
    if(!s->count || us<s->minimum)s->minimum=us;
    if(us>s->maximum)s->maximum=us;
    s->count++;s->sum+=us;
}
static void record_mouse_buttons(uint8_t old,uint8_t buttons,bool acked,bool moving) {
    if(!measuring() || old==buttons)return;
    ub_mouse_button_snapshot *s=&measurement.mouse_buttons;
    if(acked)s->acked_edges++;
    else { s->input_edges++;if(moving)s->moving_edges++; }
    for(unsigned i=0;i<2;++i)if((old^buttons)&(1u<<i)) {
        if(buttons&(1u<<i)) { if(acked)s->acked_down[i]++;else s->input_down[i]++; }
        else { if(acked)s->acked_up[i]++;else s->input_up[i]++; }
    }
}
static void process_mouse_ack(uint32_t cookie,uint32_t time,bool success) {
        if(!mouse_flight_valid || mouse_flight.sequence!=cookie)return;
        if(success) {
            if(measuring())measurement.mouse_buttons.wire_formats|=mouse_flight.wide?2u:1u;
            record_mouse_buttons(mouse_queue.acked_buttons,mouse_flight.report.buttons,true,false);
            if(mouse_flight.button_queued && in_window(mouse_flight.button_time))
                summary_add(&measurement.mouse_buttons.click_ack,time-mouse_flight.button_time);
            um_ack(&mouse_queue,&mouse_flight);
            if(measuring()) {
                if(combined_mode()) {
                    measurement.dual.mouse_acks++;
                    measurement.dual.mouse_buttons_acked|=mouse_flight.report.buttons;
                    if(last_mouse_poll_ack)summary_add(&measurement.dual.mouse_gap,time-last_mouse_poll_ack);
                    if(mouse_flight.queued)measurement.dual.mouse_transitions++;
                    if(mouse_flight.queued && mouse_flight.sequence!=last_mouse_ack_sequence && in_window(mouse_flight.time))
                        summary_add(&measurement.dual.mouse_ack,time-mouse_flight.time);
                } else {
                    if(last_mouse_poll_ack)ub_hist_add(&measurement.poll_gap,time-last_mouse_poll_ack);
                    if(mouse_flight.queued)measurement.counts.transitions_acked++;
                    if(mouse_flight.queued && mouse_flight.sequence!=last_mouse_ack_sequence && in_window(mouse_flight.time)) {
                        measurement.counts.fresh_acked++;ub_hist_add(&measurement.ack,time-mouse_flight.time);
                        ub_hist_add(&measurement.transition_ack,time-mouse_flight.time);
                    } else measurement.counts.repeat_acked++;
                }
                last_mouse_poll_ack=time;
            }
            last_mouse_ack_sequence=mouse_flight.sequence;
        } else { um_flush(&mouse_queue,mouse_queue.buttons);upr_mouse_reset(); }
        mouse_flight_valid=false;
}
static void process_ack(void) {
    if(atomic_exchange_explicit(&mouse_ack_pending,false,memory_order_acquire))
        process_mouse_ack(mouse_ack_cookie,mouse_ack_time,mouse_ack_success);
    if(!atomic_exchange_explicit(&ack_pending,false,memory_order_acquire))return;
    if(mouse_mode()&&!combined_mode()) {
        process_mouse_ack(ack_cookie,ack_time,ack_success);return;
    }
    if(!flight_valid || flight.sequence!=ack_cookie)return;
    if(ack_success) {
        if(measuring()) {
            if(combined_mode())measurement.dual.pad_acked|=flight.report.buttons0|((uint32_t)flight.report.buttons1<<8);
            if(last_poll_ack)ub_hist_add(&measurement.poll_gap,ack_time-last_poll_ack);
            last_poll_ack=ack_time;
            if(flight.transition)measurement.counts.transitions_acked++;
            if(flight.sequence && flight.sequence!=last_ack_sequence && in_window(flight.received_us)) {
                measurement.counts.fresh_acked++; ub_hist_add(&measurement.ack,ack_time-flight.received_us);
                if(flight.transition)ub_hist_add(&measurement.transition_ack,ack_time-flight.received_us);
            } else measurement.counts.repeat_acked++;
        }
        last_ack_sequence=flight.sequence;
    }
    flight_valid=false;
}
static void schedule_mouse(void) {
    bool ready=mouse_output_ready();
    if(!ready) {
        if(mouse_was_ready) {
            // A suspended/reset host must not receive accumulated old motion.
            if(mouse_flight_valid && cancel_mouse_repeat())mouse_flight_valid=false;
            um_flush(&mouse_queue,mouse_queue.buttons);upr_mouse_reset();
        }
        mouse_was_ready=false;return;
    }
    mouse_was_ready=true;
    if(atomic_load_explicit(combined_mode()?&mouse_ack_pending:&ack_pending,memory_order_acquire))return;
    if(mouse_flight_valid) {
        if(!mouse_flight.queued && um_pending(&mouse_queue)) {
            if(!cancel_mouse_repeat())return;
            mouse_flight_valid=false;
        } else return;
    }
    bool wide=ud_mouse_protocol(combined_mode())==1;
    if(MOUSE_FIXED_PROBE)um_next_single(&mouse_queue,&mouse_flight,wide);
    else um_next_wide(&mouse_queue,&mouse_flight,wide);
    uint32_t armed;
    uint8_t wire[UM_REPORT_BYTES];um_encode(&mouse_flight.report,wide,wire);
    if(ud_mouse_send(combined_mode(),wide,wire,sizeof(wire),mouse_flight.sequence,&armed)) {
        mouse_flight_valid=true;memcpy(last_mouse_output,wire,sizeof(wire));
        if(mouse_flight.queued && mouse_flight.sequence!=last_mouse_armed_sequence && in_window(mouse_flight.time)) {
            if(combined_mode())summary_add(&measurement.dual.mouse_ready,armed-mouse_flight.time);
            else {
                ub_hist_add(&measurement.ready,armed-mouse_flight.time);
                ub_hist_add(&measurement.transition_ready,armed-mouse_flight.time);
            }
        }
        last_mouse_armed_sequence=mouse_flight.sequence;
    }
}
static void schedule_output(void) {
    if(mouse_mode()) { schedule_mouse();if(!combined_mode())return; }
    if(!ud_ready() || atomic_load_explicit(&ack_pending,memory_order_acquire))return;
    if(flight_valid) {
        // An unchanged input report does not need to revoke an identical
        // hardware packet just to refresh its timestamp. Continuous 1 kHz
        // sources otherwise exercise EP_ABORT on every input, including while
        // EP0 serves GUI requests. Actual changes still replace pending repeats
        // immediately, and queued press/release transitions retain their order.
        if(!flight.transition && ub_queue_depth(&outgoing)) {
            if(!ud_cancel_repeat())return;
            if(measuring())measurement.counts.repeats_replaced++;
            flight_valid=false;
        } else return;
    }
    bool from_queue=ub_queue_depth(&outgoing)!=0;
    if(from_queue)flight=outgoing.pending[outgoing.read];
    else {
        if(outgoing.have_latest)flight=outgoing.latest;
        else { memset(&flight,0,sizeof(flight)); sw_neutral(&flight.report); }
        flight.transition=false;
    }
    uint32_t armed;
    if(ud_send((const uint8_t *)&flight.report,sizeof(flight.report),flight.sequence,&armed)) {
        if(from_queue) { ub_frame consumed; ub_queue_pop(&outgoing,&consumed); }
        flight_valid=true;
        if(flight.sequence && flight.sequence!=last_armed_sequence && in_window(flight.received_us)) {
            ub_hist_add(&measurement.ready,armed-flight.received_us);
            if(flight.transition)ub_hist_add(&measurement.transition_ready,armed-flight.received_us);
        }
        last_armed_sequence=flight.sequence;
    }
}
static void begin_measurement(uint32_t time_us) {
    memset(&measurement,0,sizeof(measurement));
    measurement.magic=0x4d324355; measurement.version=UB_VERSION; measurement.bytes=sizeof(measurement);
    measurement.start_us=time_us; measurement.configuration=config; measurement.host_begin=host_stats;
    measurement.configuration.output_options=active_output_options;
    measurement.counts.reserved[0]=upr_active_slot()==255?0:upr_active_slot()+1;
    measurement.counts.reserved[1]=upr_profile_crc();measurement.counts.reserved[2]=upr_bank_generation();
    ud_statistics_get(&measurement.device_begin);
    ub_hist_reset(&measurement.mapping,2); ub_hist_reset(&measurement.ready,8);
    ub_hist_reset(&measurement.ack,64); ub_hist_reset(&measurement.poll_gap,64);
    ub_hist_reset(&measurement.transition_ready,64); ub_hist_reset(&measurement.transition_ack,64);
    drops_begin=atomic_load(&event_dropped); outgoing.drops=0; outgoing.max_depth=ub_queue_depth(&outgoing);
    mouse_drops_begin=mouse_queue.drops;mouse_queue.max_depth=mouse_queue.pending_sources;
    mouse_button_drops_begin=mouse_queue.button_drops;mouse_queue.button_max_depth=mouse_queue.button_count;
    mouse_flushes_begin=mouse_queue.flushes;mouse_input_x_begin=mouse_queue.input_x;mouse_input_y_begin=mouse_queue.input_y;
    mouse_acked_x_begin=mouse_queue.acked_x;mouse_acked_y_begin=mouse_queue.acked_y;
    if(combined_mode())measurement.dual.magic=0x314f5544; // DUO1
    measurement.dual.reserved=MOUSE_FIXED_AMPLITUDE;
    if(mouse_mode()) {
        measurement.mouse_buttons.magic=0x3154424d; // MBT1
        measurement.mouse_buttons.input_initial=mouse_queue.buttons;
        measurement.mouse_buttons.acked_initial=mouse_queue.acked_buttons;
    }
    last_poll_ack=last_mouse_poll_ack=0; measurement_valid=false; phase=MEASURE_RUNNING;
}
static void end_measurement(uint32_t time_us) {
    measurement.end_us=time_us; measurement.host_end=host_stats;
    ud_statistics_get(&measurement.device_end);
    measurement.counts.raw_queue_drop=atomic_load(&event_dropped)-drops_begin;
    measurement.counts.transition_queue_drop=outgoing.drops;
    measurement.counts.max_depth=outgoing.max_depth;
    memcpy(measurement.last_raw,last_raw,4);
    if(outgoing.have_latest)memcpy(measurement.last_output,&outgoing.latest.report,8);
    measurement.flags=5; measurement_valid=true; phase=MEASURE_IDLE;
    if(combined_mode()) {
        measurement.flags|=16;
        measurement.dual.mouse_drops=mouse_queue.drops-mouse_drops_begin;
        measurement.dual.mouse_flushes=mouse_queue.flushes-mouse_flushes_begin;
        measurement.dual.mouse_max_depth=mouse_queue.max_depth;
        measurement.dual.input_x=mouse_queue.input_x-mouse_input_x_begin;measurement.dual.input_y=mouse_queue.input_y-mouse_input_y_begin;
        measurement.dual.acked_x=mouse_queue.acked_x-mouse_acked_x_begin;measurement.dual.acked_y=mouse_queue.acked_y-mouse_acked_y_begin;
    } else if(mouse_mode()) {
        measurement.flags|=8;
        measurement.counts.transition_queue_drop=mouse_queue.drops-mouse_drops_begin;
        measurement.counts.max_depth=mouse_queue.max_depth;
        memset(measurement.last_output,0,8);memcpy(measurement.last_output,last_mouse_output,sizeof(last_mouse_output));
    }
    if(mouse_mode()) {
        measurement.mouse_buttons.dropped_edges=mouse_queue.button_drops-mouse_button_drops_begin;
        measurement.mouse_buttons.max_depth=mouse_queue.button_max_depth;
        measurement.mouse_buttons.pending=mouse_queue.button_count;
    }
    save_pending=auto_save;
}
static void reset_output(uint32_t timestamp) {
    upr_mouse_reset();
    if(mouse_mode()) {
        if(mouse_flight_valid && cancel_mouse_repeat())mouse_flight_valid=false;
        um_flush(&mouse_queue,0);if(!combined_mode())return;
    }
    if(flight_valid && ud_cancel_repeat())flight_valid=false;
    ub_queue_reset(&outgoing);
    ub_frame neutral={.received_us=timestamp,.sequence=++input_sequence};sw_neutral(&neutral.report);
    ub_queue_push(&outgoing,&neutral);
}
static bool request_profile_output(uint32_t now) {
    uint8_t desired=upr_output_options();
    if(phase==MEASURE_IDLE && !output_disconnected && (desired&(UM_MOUSE|UM_COMBINED))==(active_output_options&(UM_MOUSE|UM_COMBINED))) {
        // Button routing does not change USB descriptors. Release the old
        // interpretation before accepting reports under the new one.
        if(desired!=active_output_options) { active_output_options=desired;reset_output(now); }
        output_change_requested=false;return false;
    }
    output_change_requested=true;output_change_at=now+250000;
    return true;
}
static bool boot_controls_idle;
static uint32_t switch_inputs,last_switch_keys;
static profile_hotkey hotkey;
static uint32_t __not_in_flash_func(switch_keys)(void) {
    uint32_t keys=upr_switch_keys();
    if(keys!=last_switch_keys) { memset(&hotkey,0,sizeof(hotkey));memset(&boot_button,0,sizeof(boot_button));last_switch_keys=keys; }
    return keys;
}
static bool __not_in_flash_func(switch_available)(void) {
    return input_ready && ud_ready() && phase==MEASURE_IDLE && !output_change_requested && upr_group_count()>1;
}
static void cycle_profile(uint32_t now) {
    if(!upr_cycle_profile())return;
    config.input_interval_ms=upr_input_interval();profile_switches++;
    // Preserve the chord's already queued press before releasing the old mapping.
    ub_frame neutral={.received_us=now,.sequence=++input_sequence};sw_neutral(&neutral.report);
    if(mouse_mode())reset_output(now);else ub_queue_push(&outgoing,&neutral);
    switch_inputs=upr_switch_inputs();boot_controls_idle=switch_inputs==0 && upr_switch_released();
    // A different mapping may expose buttons that were already held. Seed the
    // edge history from observed source values; remapping is not a physical edge.
    hotkey=(profile_hotkey){.previous=switch_inputs&upr_switch_keys(),.initialized=true};
    selection_save_at=now+100000;status_led_profile(upr_profile_number());
    request_profile_output(now);
}
static void __not_in_flash_func(hotkey_task)(uint32_t now) {
    uint32_t keys=switch_keys();
    if(keys && ph_update(&hotkey,now,switch_available(),keys,switch_inputs))cycle_profile(now);
}
static void __not_in_flash_func(boot_button_task)(uint32_t now) {
    uint32_t keys=switch_keys();
    bool available=switch_available() && !status_led_profile_busy();
    bool eligible=available && !keys && boot_controls_idle;
    bool enabled=pb_allow_sampling(&boot_button,now,eligible),pressed;uint32_t sampled;
    if(uc_bootsel_poll(enabled,&pressed,&sampled) && pb_sample(&boot_button,sampled,pressed))cycle_profile(now);
    hotkey_task(now);
    // Switch immediately, but only write Flash after 100 ms without controller
    // activity. The original chord bindings stay guarded across mapping changes.
    if(!boot_controls_idle || !input_ready)selection_save_at=now+100000;
    if(upr_selection_pending() && phase==MEASURE_IDLE && (int32_t)(now-selection_save_at)>=0) {
        uint32_t start=time_us_32();bool success=upr_save_selection();uint32_t elapsed=time_us_32()-start;
        if(elapsed>selection_save_max_us)selection_save_max_us=elapsed;
        if(!success) { selection_save_failures++;status_led_saved(false); }
    }
}
static void handle_host_event(const uc_event *e) {
    if(e->type!=UC_INPUT && upr_event(e)) { config.input_interval_ms=upr_input_interval();reset_output(e->time_us); }
    input_ready=upr_ready();
    if(e->type==UC_STATUS && e->code==UC_CONFIGURED && input_ready)request_profile_output(time_us_32());
    if(e->type==UC_INPUT) {
        ub_frame frame={.received_us=e->time_us,.sequence=++input_sequence};
        raw_reports++;
        if(measuring())measurement.counts.raw_reports++;
        if(!e->length || e->length>64) {
            if(input_ready && measuring())measurement.counts.bad_reports++;
            return;
        }
        bool mapped=upr_apply(e,time_us_32(),ud_ready(),&frame.report);
        if(mouse_mode()) {
            int32_t x=0,y=0;bool relative=upr_mouse_delta(e,&x,&y);
            if(!input_ready || (!mapped && !relative))return;
            // A different report may carry buttons only. Decode failure must
            // never reuse the preceding report's relative movement.
            if(!relative)x=y=0;
            uint8_t buttons=um_route_buttons(active_output_options,&frame.report);
            memset(last_raw,0,sizeof(last_raw));memcpy(last_raw,e->data,e->length<4?e->length:4);
            if(mouse_output_ready()) {
                if(measuring()) {
                    if(combined_mode()) {
                        measurement.dual.mouse_buttons_seen|=buttons;
                        if(x || y || buttons!=mouse_queue.buttons)measurement.dual.mouse_changes++;
                    } else if(x || y || buttons!=mouse_queue.buttons)measurement.counts.input_changes++;
                }
                record_mouse_buttons(mouse_queue.buttons,buttons,false,x||y);
                um_push(&mouse_queue,x,y,buttons,e->time_us,frame.sequence,true);
                if(!combined_mode()&&in_window(e->time_us))ub_hist_add(&measurement.mapping,time_us_32()-e->time_us);
            } else { um_flush(&mouse_queue,buttons);upr_mouse_reset(); }
            if(!combined_mode())return;
        }
        if(!mapped) {
            if(input_ready && measuring())measurement.counts.bad_reports++;
            return;
        }
        memset(last_raw,0,sizeof(last_raw));memcpy(last_raw,e->data,e->length<4?e->length:4);
        if(in_window(frame.received_us))ub_hist_add(&measurement.mapping,time_us_32()-frame.received_us);
        bool changed=!outgoing.have_latest || memcmp(&outgoing.latest.report,&frame.report,8);
        if(combined_mode()&&measuring())measurement.dual.pad_seen|=frame.report.buttons0|((uint32_t)frame.report.buttons1<<8);
        if(changed && measuring())measurement.counts.input_changes++;
        ub_queue_push(&outgoing,&frame);
    } else if(e->type==UC_STATUS) {
        if(e->code==UC_DISCONNECTED) {
            if(measuring())measurement.counts.input_disconnects++;
        }
    } else if(e->type==UC_STATISTICS) {
        memcpy(&host_stats,e->data,sizeof(host_stats));
        if(phase==MEASURE_STARTING && !start_request_pending && input_ready && ud_ready())begin_measurement(e->time_us);
        else if(phase==MEASURE_STOPPING)end_measurement(e->time_us);
    }
}
static void put32(uint8_t *data,uint32_t value) { memcpy(data,&value,4); }
static uint32_t get32(const uint8_t *data) { uint32_t value; memcpy(&value,data,4); return value; }
static bool configuration_valid(const ub_configuration *c) {
    if(c->version!=UB_VERSION || !c->output_interval_ms || c->axis_mode>2 || (c->output_options&~UM_OPTIONS_MASK) ||
       ((c->output_options&UM_COMBINED)&&!(c->output_options&UM_MOUSE)) ||
       ((c->output_options&UM_GAMEPAD_BUTTONS)&&((c->output_options&(UM_MOUSE|UM_COMBINED))!=(UM_MOUSE|UM_COMBINED))) ||
       c->boot_capture_ms>300000 || c->warmup_ms>30000)return false;
    for(unsigned i=0;i<12;++i)if(c->button_map[i]&0xc000)return false;
    return true;
}
static void publish_response(const uint8_t *reply) {
    uint32_t irq=save_and_disable_interrupts(); memcpy(response,reply,64); restore_interrupts(irq);
}
static void handle_command(void) {
    if(!atomic_load_explicit(&command_pending,memory_order_acquire))return;
    uint8_t local[64],reply[64]={'U','C','2','R',1};
    memcpy(local,command,64); atomic_store_explicit(&command_pending,false,memory_order_release);
    reply[5]=local[5]; reply[6]=local[6];
    uint8_t *payload=reply+8; const uint8_t *arg=local+8;
    if(measuring())measurement.counts.diagnostic_commands++;
    switch(local[5]) {
    case CMD_FIRMWARE:
#if !defined(BRIDGE_USB_TRACE) && !defined(BRIDGE_MOUSE_FIXED_PROBE) && BRIDGE_MOUSE_PACKET_BYTES == 8
        _Static_assert(sizeof(CONTROLLER_FIRMWARE_VERSION)<=24,"Firmware version must fit the USB response");
        memcpy(payload,"CBV1",4); payload[4]=1; payload[5]=1; // schema 1, RP2350-USB-A
        memcpy(payload+8,CONTROLLER_FIRMWARE_VERSION,sizeof(CONTROLLER_FIRMWARE_VERSION));
        memcpy(payload+32,"ControllerBridge",17);
#else
        reply[7]=1; // Diagnostic builds are not the bundled production firmware.
#endif
        break;
    case CMD_INFO: {
        ud_statistics ds; ud_statistics_get(&ds);
        uint32_t flags=(ds.configured?1:0)|(input_ready?2:0)|(phase==MEASURE_RUNNING?4:0)|
            (saved_valid?8:0)|(phase==MEASURE_STARTING?16:0)|(config.boot_capture_ms?32:0)|(measurement_valid?64:0)|
            (phase==MEASURE_STOPPING?128:0);
        put32(payload,UB_VERSION); put32(payload+4,time_us_32()); put32(payload+8,flags);
        put32(payload+12,raw_reports); put32(payload+16,ds.acks); put32(payload+20,host_stats.in_naks);
        put32(payload+24,host_stats.transfer_errors); memcpy(payload+28,last_raw,4);
        if(outgoing.have_latest)memcpy(payload+32,&outgoing.latest.report,8);
        if(mouse_mode()&&!combined_mode()) { memset(payload+32,0,8);memcpy(payload+32,last_mouse_output,sizeof(last_mouse_output)); }
        payload[40]=config.input_interval_ms; payload[41]=config.output_interval_ms; payload[42]=config.axis_mode;
        payload[43]=active_output_options;
        put32(payload+44,upr_field_count()); put32(payload+48,ub_queue_depth(&outgoing));
        put32(payload+52,active_output_interval); break;
    }
    case CMD_GET_CONFIG: memcpy(payload,&config,sizeof(config)); break;
    case CMD_SET_CONFIG: {
        ub_configuration c; memcpy(&c,arg,sizeof(c));
        if(phase!=MEASURE_IDLE) { reply[7]=2; break; }
        if(!configuration_valid(&c)) { reply[7]=1; break; }
        // Button assignments and output mode belong to the profile registry. Avoid
        // silently accepting obsolete CLI edits that would have no effect.
        if(c.axis_mode!=config.axis_mode || c.output_options!=config.output_options || memcmp(c.button_map,config.button_map,sizeof(c.button_map))) { reply[7]=5;break; }
        config=c; uc_request_poll_interval(config.input_interval_ms); break;
    }
    case CMD_SAVE_CONFIG:
        if(phase!=MEASURE_IDLE)reply[7]=2;
        else {
            if(!ub_store_save(0,&config,sizeof(config),true))reply[7]=3;
            status_led_saved(reply[7]==0);
        }
        break;
    case CMD_START: {
        uint32_t duration=get32(arg);
        if(phase!=MEASURE_IDLE || output_change_requested) { reply[7]=2; break; }
        if(duration<1000 || duration>300000) { reply[7]=1; break; }
        duration_us=duration*1000; auto_save=arg[4]!=0; phase=MEASURE_STARTING;
        start_not_before=time_us_32()+20000; start_request_pending=true; break;
    }
    case CMD_STOP:
        if(phase==MEASURE_RUNNING) { phase=MEASURE_STOPPING; uc_request_statistics(); }
        else if(phase==MEASURE_STARTING)phase=MEASURE_IDLE;
        break;
    case CMD_SELECT_SNAPSHOT:
        if(arg[0])selected_valid=load_snapshot(&selected_snapshot);
        else if(measurement_valid) { selected_snapshot=measurement; selected_valid=true; }
        else selected_valid=false;
        if(!selected_valid)reply[7]=3;
        else { put32(payload,selected_snapshot.bytes); put32(payload+4,selected_snapshot.version); }
        break;
    case CMD_READ_SNAPSHOT: {
        uint32_t offset=get32(arg);
        if(!selected_valid || offset>=selected_snapshot.bytes) { reply[7]=1; break; }
        uint32_t n=selected_snapshot.bytes-offset; if(n>56)n=56;
        memcpy(payload,(const uint8_t *)&selected_snapshot+offset,n); break;
    }
    case CMD_ARM_BOOT: {
        uint32_t duration=get32(arg);
        if(phase!=MEASURE_IDLE) { reply[7]=2; break; }
        if(duration<1000 || duration>300000) { reply[7]=1; break; }
        config.boot_capture_ms=duration;
        if(!ub_store_save(0,&config,sizeof(config),true))reply[7]=3;
        break;
    }
    case CMD_BOOTSEL: case CMD_REBOOT:
        reboot_at=time_us_32()+250000; reboot_to_bootsel=local[5]==CMD_BOOTSEL; break;
    case CMD_LED:
        if(phase!=MEASURE_IDLE && arg[0])reply[7]=2;
        else if(!status_led_command(arg,payload))reply[7]=1;
        break;
    case CMD_BUTTON: {
        uc_bootsel_statistics bs;uc_bootsel_statistics_get(&bs);
        memcpy(payload,"BTN1",4);memcpy(payload+4,&bs,sizeof(bs));
        put32(payload+32,profile_switches);put32(payload+36,selection_save_failures);put32(payload+40,selection_save_max_us);
        put32(payload+44,(boot_button.idle_tracking?1u:0)|(boot_button.stable?2u:0)|(boot_button.need_release?4u:0));
        put32(payload+48,PB_HOLD_US/1000);put32(payload+52,50);break;
    }
    case CMD_MOUSE:
        memcpy(payload,"MSE1",4);payload[4]=active_output_options;payload[5]=upr_output_options();
        payload[6]=upr_mouse_ready();payload[7]=mouse_queue.buttons;
        put32(payload+8,mouse_queue.inputs);put32(payload+12,mouse_queue.movement_inputs);
        put32(payload+16,mouse_queue.movement_acks);put32(payload+20,mouse_queue.button_acks);
        put32(payload+24,mouse_queue.drops);put32(payload+28,mouse_queue.max_depth);
        put32(payload+32,mouse_queue.input_x);put32(payload+36,mouse_queue.input_y);
        put32(payload+40,mouse_queue.acked_x);put32(payload+44,mouse_queue.acked_y);
        put32(payload+48,mouse_queue.pending_sources);put32(payload+52,mouse_queue.flushes);break;
    case CMD_MOUSE_WIRE:
        memcpy(payload,"MWR1",4);payload[4]=ud_mouse_protocol(combined_mode());payload[5]=UM_REPORT_BYTES;
        payload[6]=mouse_mode();payload[7]=combined_mode();
        put32(payload+8,payload[4]?32767:127);break;
    case CMD_MOUSE_PROBE:
        memcpy(payload,"MFP1",4);put32(payload+4,MOUSE_FIXED_PROBE);
        put32(payload+8,MOUSE_FIXED_PROBE?2:0);put32(payload+12,MOUSE_FIXED_AMPLITUDE);
        put32(payload+16,MOUSE_FIXED_PROBE?1:0);
        put32(payload+20,BRIDGE_MOUSE_PACKET_BYTES);break;
    case CMD_APPLY_OUTPUT:
        if(phase!=MEASURE_IDLE) { reply[7]=2;break; }
        if(get32(arg)!=upr_bank_generation() || arg[4]!=upr_active_slot()) { reply[7]=4;break; }
        // The GUI first reads back the committed profile/bank, then applies
        // its USB layout. A layout change cannot interrupt that verification.
        payload[6]=request_profile_output(time_us_32());
        memcpy(payload,"OUT1",4);payload[4]=active_output_options;payload[5]=upr_output_options();
        break;
    default: {
        bool changed=false;
#ifdef BRIDGE_USB_TRACE
        if(uct_command(local[5],arg,payload,&reply[7]))break;
#endif
        if(!upr_command(local[5],arg,payload,&reply[7],phase!=MEASURE_IDLE,&changed))reply[7]=1;
        if(local[5]==20 && !reply[7])put32(payload+44,get32(payload+44)|32u|128u|256u|1024u);
        if(changed) {
            config.input_interval_ms=upr_input_interval();input_ready=upr_ready();reset_output(time_us_32());
            switch_inputs=upr_switch_inputs();boot_controls_idle=switch_inputs==0 && upr_switch_released();
            hotkey=(profile_hotkey){.previous=switch_inputs&upr_switch_keys(),.initialized=true};
        }
        if(changed && (local[5]==UPR_CMD_SELECT_PROFILE || (local[5]==26 && upr_group_count()>1)))status_led_profile(upr_profile_number());
        else if(local[5]==26 && (reply[7]==0 || reply[7]==3))status_led_saved(reply[7]==0);
        break;
    }
    }
    publish_response(reply);
}
static void start_usb_output(uint8_t options) {
    active_output_options=options;
    flight_valid=mouse_flight_valid=mouse_was_ready=false;
    atomic_store_explicit(&ack_pending,false,memory_order_release);
    atomic_store_explicit(&mouse_ack_pending,false,memory_order_release);
    last_armed_sequence=last_mouse_armed_sequence=last_poll_ack=last_mouse_poll_ack=0;
    ub_queue_reset(&outgoing);
    um_init(&mouse_queue);upr_mouse_reset();
    sw_report initial;upr_output_report(time_us_32(),&initial);
    uint8_t initial_mouse[UM_REPORT_BYTES]={0};
    if(mouse_mode())initial_mouse[0]=um_route_buttons(active_output_options,&initial);
    if(combined_mode() && !(active_output_options&UM_GAMEPAD_BUTTONS))sw_navigation(&initial,&initial);
    memcpy(last_mouse_output,initial_mouse,sizeof(initial_mouse));
    um_flush(&mouse_queue,initial_mouse[0]);
    ub_frame current={.report=initial,.received_us=time_us_32(),.sequence=++input_sequence};
    ub_queue_push(&outgoing,&current);
    memcpy(output_configuration,combined_mode()?um_combined_configuration_descriptor:mouse_mode()?um_configuration_descriptor:sw_configuration_descriptor,
        mouse_mode()?sizeof(um_configuration_descriptor):sizeof(sw_configuration_descriptor));
    // Match the interrupt endpoint reservation to the actual 8-byte input.
    // Switch 2 polled this at 1 ms; the legacy 64-byte reservation gave 8 ms.
    // EP0 remains 64 bytes for descriptors and diagnostic Feature reports.
    output_configuration[31]=mouse_mode() && !combined_mode()?BRIDGE_MOUSE_PACKET_BYTES:8;
    output_configuration[33]=active_output_interval=config.output_interval_ms;
    ud_options output={.device=sw_device_descriptor,.device_length=18,
        .configuration=output_configuration,.configuration_length=34,.report=sw_report_descriptor,.report_length=102,
        .manufacturer="BDA",.product="NSW wired controller",.input_length=8,.initial_input=(const uint8_t *)&initial,
        .get_feature=get_feature,.set_feature=set_feature,.complete=input_complete};
    if(combined_mode()) {
        output.configuration_length=sizeof(um_combined_configuration_descriptor);
        output.secondary_report=um_report_descriptor;output.secondary_report_length=sizeof(um_report_descriptor);
        output.secondary_input_length=sizeof(initial_mouse);output.secondary_initial_input=initial_mouse;
        output.secondary_relative_mouse=true;output.secondary_complete=mouse_complete;
        output_configuration[56]=BRIDGE_MOUSE_PACKET_BYTES;
        output_configuration[58]=active_output_interval;
    } else if(mouse_mode()) {
        output.device=um_device_descriptor;output.report=um_report_descriptor;output.report_length=sizeof(um_report_descriptor);
        output.configuration_length=sizeof(um_configuration_descriptor);
        output.configuration_report=um_feature_descriptor;output.configuration_report_length=sizeof(um_feature_descriptor);
        output.manufacturer="ControllerBridge";output.product="ControllerBridge USB Mouse";
        output.input_length=sizeof(initial_mouse);output.initial_input=initial_mouse;output.relative_mouse=true;
    }
#ifdef BRIDGE_USB_TRACE
    output.observe_setup=uct_setup;output.observe_event=uct_event;
#endif
    ud_init(&output);
}
static void output_change_task(uint32_t now) {
    if(!output_change_requested || phase!=MEASURE_IDLE || (int32_t)(now-output_change_at)<0)return;
    if(!output_disconnected) {
        if((upr_output_options()&(UM_MOUSE|UM_COMBINED))==(active_output_options&(UM_MOUSE|UM_COMBINED))) {
            request_profile_output(now);return;
        }
        ud_disconnect();output_disconnected=true;output_change_at=now+250000;
        flight_valid=mouse_flight_valid=false;
        atomic_store_explicit(&ack_pending,false,memory_order_release);
        atomic_store_explicit(&mouse_ack_pending,false,memory_order_release);
    } else {
        // USB-A enumeration and the shortcut release guard keep running.
        // Build from the latest selected profile; never replay detached input.
        start_usb_output(upr_output_options());
        output_disconnected=output_change_requested=false;
    }
}
// Run dispatch from SRAM so UI/configuration code growth cannot evict this loop from XIP cache.
int __not_in_flash_func(main)(void) {
    set_sys_clock_khz(120000,true);
    status_led_init();
    if(!ub_store_load(0,&config,sizeof(config)) || !configuration_valid(&config))egret_default_configuration(&config);
    boot_capture_ms=config.boot_capture_ms;
    if(boot_capture_ms) {
        // Consume before exposing USB. Returning to the PC must not overwrite
        // the console measurement by automatically starting it a second time.
        config.boot_capture_ms=0;
        if(!ub_store_save(0,&config,sizeof(config),false))boot_capture_ms=0;
    }
    saved_valid=load_snapshot(&selected_snapshot);
    upr_init();
    start_usb_output(0);
    uc_request_poll_interval(config.input_interval_ms);
    multicore_launch_core1(host_core);
    for(;;) {
        process_ack();
        uc_event e;
        while(next_event(&e)) {
            handle_host_event(&e); process_ack(); schedule_output();
            // Learning/GUI observation is not on the input-to-output-ready path.
            // Retain every source state, but only after the mapped packet is armed.
            if(e.type==UC_INPUT) {
                upr_event(&e);
                // Cache only after output is armed; never scan controls in the idle loop.
                switch_inputs=upr_switch_inputs();boot_controls_idle=switch_inputs==0 && upr_switch_released();
                // Use source reception time, so a backlog cannot compress
                // physically separated edges into the 100 ms chord window.
                hotkey_task(e.time_us);
            } else if(!input_ready)boot_controls_idle=false;
            status_led_event(&e);
        }
        // Repeat edges are generated even by HID devices that only report
        // physical changes. They use the same ordered transition queue as input.
        uint32_t rapid_now=time_us_32();sw_report rapid_report;
        if(upr_tick(rapid_now,ud_ready(),&rapid_report)) {
            ub_frame frame={.report=rapid_report,.received_us=rapid_now,.sequence=++input_sequence};
            if(measuring()&&!combined_mode())measurement.counts.input_changes++;
            if(mouse_mode()) {
                if(input_ready && mouse_output_ready()) {
                    uint8_t buttons=um_route_buttons(active_output_options,&frame.report);
                    if(combined_mode()&&measuring()&&buttons!=mouse_queue.buttons)measurement.dual.mouse_changes++;
                    record_mouse_buttons(mouse_queue.buttons,buttons,false,false);
                    um_push(&mouse_queue,0,0,buttons,rapid_now,frame.sequence,false);
                }
            }
            if(!mouse_mode()||combined_mode()) {
                if(combined_mode()) {
                    if(!(active_output_options&UM_GAMEPAD_BUTTONS))sw_navigation(&frame.report,&frame.report);
                    if(measuring()) {
                        measurement.dual.pad_seen|=frame.report.buttons0|((uint32_t)frame.report.buttons1<<8);
                        if(!outgoing.have_latest||memcmp(&outgoing.latest.report,&frame.report,8))measurement.counts.input_changes++;
                    }
                }
                ub_queue_push(&outgoing,&frame);
            }
        }
        schedule_output(); handle_command();
        uint32_t now=time_us_32();
        boot_button_task(now);
        now=time_us_32();
        if(!reboot_at)output_change_task(now);
        status_led_task(now,input_ready,ud_ready(),upr_led_rgb());
        if(boot_capture_ms && input_ready && ud_ready() && !output_change_requested) {
            if(!ready_since)ready_since=now;
            if((uint32_t)(now-ready_since)>=config.warmup_ms*1000) {
                duration_us=boot_capture_ms*1000; boot_capture_ms=0; auto_save=true;
                phase=MEASURE_STARTING; start_not_before=now+20000; start_request_pending=true;
            }
        } else if(!input_ready || !ud_ready() || output_change_requested)ready_since=0;
        if(phase==MEASURE_STARTING && start_request_pending && input_ready && ud_ready() &&
           (int32_t)(now-start_not_before)>=0) {
            start_request_pending=false; uc_request_statistics();
        }
        if(phase==MEASURE_RUNNING && (uint32_t)(now-measurement.start_us)>=duration_us) {
            phase=MEASURE_STOPPING; uc_request_statistics();
        }
        if(save_pending) {
            save_pending=false;
            saved_valid=ub_store_save(1,&measurement,sizeof(measurement),true);
            if(!saved_valid)measurement.counts.save_failures++;
        }
        if(reboot_at && (int32_t)(now-reboot_at)>=0) {
            ud_disconnect();
            if(reboot_to_bootsel)reset_usb_boot(0,0); else watchdog_reboot(0,0,10);
            for(;;)tight_loop_contents();
        }
        tight_loop_contents();
    }
}
