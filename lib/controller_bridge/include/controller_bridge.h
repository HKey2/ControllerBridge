#ifndef CONTROLLER_BRIDGE_H
#define CONTROLLER_BRIDGE_H
#include <stdbool.h>
#include <stdint.h>
#include "controller_usb.h"
#include "controller_device.h"
#include "switch_wired.h"

#define UB_VERSION 0x00020000u
#define UB_HIST_BINS 128
#define UB_TRANSITIONS 64
typedef struct {
    uint32_t version;
    uint8_t input_interval_ms, output_interval_ms, axis_mode, output_options;
    uint16_t button_map[12];
    uint32_t boot_capture_ms, warmup_ms;
} ub_configuration;
typedef struct {
    uint32_t count, minimum, maximum, step_us;
    uint64_t sum;
    uint32_t bins[UB_HIST_BINS];
} ub_histogram;
typedef struct {
    uint32_t raw_reports, input_changes, bad_reports, raw_queue_drop;
    uint32_t transition_queue_drop, transitions_acked, fresh_acked, repeat_acked;
    uint32_t repeats_replaced, max_depth, input_disconnects, diagnostic_commands;
    uint32_t save_failures, reserved[3];
} ub_counts;
typedef struct {
    uint32_t count,minimum,maximum,reserved;
    uint64_t sum;
} ub_summary;
typedef struct {
    uint32_t magic,mouse_acks,mouse_changes,mouse_transitions,mouse_drops,mouse_flushes,mouse_max_depth;
    uint32_t pad_seen,pad_acked,mouse_buttons_seen,mouse_buttons_acked;
    uint32_t input_x,input_y,acked_x,acked_y,reserved;
    ub_summary mouse_ready,mouse_ack,mouse_gap;
} ub_dual_snapshot;
typedef struct {
    uint32_t magic,input_edges,moving_edges,acked_edges,dropped_edges,max_depth;
    uint32_t input_down[2],input_up[2],acked_down[2],acked_up[2];
    ub_summary click_ack;
    uint32_t pending,input_initial,acked_initial,wire_formats; // ACK formats: bit0=8-bit Boot, bit1=16-bit Report.
} ub_mouse_button_snapshot;
typedef struct {
    uint32_t magic, version, bytes, flags, start_us, end_us;
    ub_configuration configuration;
    uc_statistics host_begin, host_end;
    ud_statistics device_begin, device_end;
    ub_counts counts;
    uint8_t last_raw[4], last_output[8], reserved[4];
    ub_histogram mapping, ready, ack, poll_gap;
    // Separate physical-state transitions from replaceable steady-state samples.
    ub_histogram transition_ready, transition_ack;
    // Optional DUO1 extension. The original 3552-byte snapshot prefix is unchanged.
    ub_dual_snapshot dual;
    // MBT1: independent click edges, counts and input-to-ACK timing.
    ub_mouse_button_snapshot mouse_buttons;
} ub_snapshot;
typedef struct {
    sw_report report;
    uint32_t received_us, sequence;
    bool transition;
} ub_frame;
typedef struct {
    ub_frame pending[UB_TRANSITIONS], latest;
    unsigned read, write;
    bool have_latest;
    uint32_t drops, max_depth;
} ub_queue;
void ub_hist_reset(ub_histogram *histogram, uint32_t step_us);
void ub_hist_add(ub_histogram *histogram, uint32_t value_us);
void ub_queue_reset(ub_queue *queue);
// Queue only state transitions. Repeated input updates the latest snapshot.
bool ub_queue_push(ub_queue *queue, const ub_frame *frame);
bool ub_queue_pop(ub_queue *queue, ub_frame *frame);
unsigned ub_queue_depth(const ub_queue *queue);
bool ub_store_load(unsigned slot, void *data, uint32_t bytes);
bool ub_store_save(unsigned slot, const void *data, uint32_t bytes, bool host_running);
#endif
