#include "pico/stdlib.h"
#include "pico/multicore.h"
#include "pico/stdio_usb.h"
#include "hardware/clocks.h"
#include "controller_usb.h"
#include "egret_profile.h"
#include <stdatomic.h>
#include <stdio.h>
#include <string.h>

#define QUEUE_COUNT 128
static uc_event queue[QUEUE_COUNT];
static atomic_uint head, tail, dropped;
static uint8_t report_descriptor[UC_REPORT_DESCRIPTOR_CAPACITY];
static uint16_t report_descriptor_received;
static uc_hid_layout layout;
static bool layout_valid;
static uint8_t previous[UC_EVENT_BYTES];
static uint16_t previous_length;
static bool have_previous;
static uint32_t changes;

static void queue_event(const uc_event *event, void *context) {
    (void)context;
    unsigned write = atomic_load_explicit(&head,memory_order_relaxed);
    unsigned next = (write+1) % QUEUE_COUNT;
    if (next == atomic_load_explicit(&tail,memory_order_acquire)) {
        atomic_fetch_add_explicit(&dropped,1,memory_order_relaxed);
        return;
    }
    queue[write] = *event;
    atomic_store_explicit(&head,next,memory_order_release);
}
static bool dequeue(uc_event *event) {
    unsigned read = atomic_load_explicit(&tail,memory_order_relaxed);
    if (read == atomic_load_explicit(&head,memory_order_acquire)) return false;
    *event = queue[read];
    atomic_store_explicit(&tail,(read+1)%QUEUE_COUNT,memory_order_release);
    return true;
}
static void host_core(void) {
    uc_host_options opts = {.dp_pin=12,.accept=egret_accept,.event=queue_event};
    uc_host_run(&opts);
}
static void hex(const uint8_t *data, unsigned length) {
    for (unsigned i = 0; i < length; ++i) printf("%02x",data[i]);
}
static const char *status_name(unsigned code) {
    static const char *const names[] = {"start","probing","probe_failed","device_found","rejected",
        "configured","report_descriptor_failed","input_error","disconnected","poll_changed","enum_failed"};
    return code < sizeof(names)/sizeof(names[0]) ? names[code] : "unknown";
}
static void handle_event(const uc_event *e, bool console) {
    if (e->type == UC_DESCRIPTOR) {
        if (e->code == 0x22 && e->interface_number == 0) {
            if (!e->offset) { report_descriptor_received = 0; layout_valid = false; }
            if (e->offset == report_descriptor_received && e->offset+e->length <= sizeof(report_descriptor)) {
                memcpy(report_descriptor+e->offset,e->data,e->length);
                report_descriptor_received += e->length;
                if (report_descriptor_received == e->total)
                    layout_valid = uc_hid_parse(report_descriptor,e->total,&layout);
            }
        }
        if (console) {
            printf("DESC type=%02x iface=%u offset=%u total=%u hex=",e->code,e->interface_number,e->offset,e->total);
            hex(e->data,e->length);
            printf("\n");
            if (e->code == 0x22 && e->offset+e->length == e->total)
                printf("LAYOUT valid=%u fields=%u report_ids=%u\n",layout_valid,layout.count,layout.has_report_ids);
        }
    } else if (e->type == UC_INPUT) {
        bool changed = !have_previous || previous_length != e->length || memcmp(previous,e->data,e->length);
        if (changed) {
            changes++;
            memcpy(previous,e->data,e->length);
            previous_length=e->length; have_previous=true;
            if (console) {
                printf("REPORT us=%lu seq=%lu ep=%02x len=%u hex=",(unsigned long)e->time_us,(unsigned long)e->sequence,e->endpoint,e->length);
                hex(e->data,e->length);
                if (layout_valid) egret_describe(&layout,e->data,e->length);
                printf("\n");
            }
        }
    } else if (e->type == UC_STATUS) {
        if (e->code == UC_CONFIGURED || e->code == UC_DISCONNECTED) have_previous=false;
        if (console) printf("STATE us=%lu name=%s detail=%lu\n",(unsigned long)e->time_us,status_name(e->code),(unsigned long)e->sequence);
    } else if (e->type == UC_STATISTICS && console) {
        uc_statistics s;
        memcpy(&s,e->data,sizeof(s));
        printf("STATS us=%lu reports=%lu changes=%lu errors=%lu stalls=%lu attempts=%lu generation=%lu interval_ms=%lu queue_drop=%u late_frames=%lu frame_us_max=%lu\n",
            (unsigned long)e->time_us,(unsigned long)s.reports,(unsigned long)changes,
            (unsigned long)s.transfer_errors,(unsigned long)s.stalls,(unsigned long)s.attempts,
            (unsigned long)s.generation,(unsigned long)s.poll_interval_ms,atomic_load(&dropped),
            (unsigned long)s.frame_late,(unsigned long)s.frame_us_max);
    }
}
int main(void) {
    // Known PIO-USB clock, within RP2350's rated clock range. Native USB remains
    // on its independent 48 MHz PLL. No overclock or voltage increase.
    set_sys_clock_khz(120000,true);
    stdio_init_all();
    multicore_launch_core1(host_core);
    bool was_connected=false;
    for (;;) {
        bool console=stdio_usb_connected();
        if (console && !was_connected) {
            printf("BOOT app=egret_diagnostic version=0.1.0 clock_hz=%lu target=0ae4:0703 host=pio_fs dp=12 dm=13 r13=retained\n",(unsigned long)clock_get_hz(clk_sys));
            printf("COMMANDS d=dump_descriptors 0=native_interval 1=experimental_1ms\n");
            have_previous=false;
            uc_request_dump();
        }
        was_connected=console;
        int ch=getchar_timeout_us(0);
        if (ch=='d') { have_previous=false; uc_request_dump(); }
        if (ch=='0') uc_request_poll_interval(0);
        if (ch=='1') uc_request_poll_interval(1);
        uc_event event;
        while (dequeue(&event)) handle_event(&event,console);
        tight_loop_contents();
    }
}
