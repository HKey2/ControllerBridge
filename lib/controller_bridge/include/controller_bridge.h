#ifndef CONTROLLER_BRIDGE_H
#define CONTROLLER_BRIDGE_H
#include <stdbool.h>
#include <stdint.h>
#include "controller_direction.h"
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
    uint8_t direction_key; // ACK-counted edge: 1..4 press U/D/L/R, 0x81..0x84 release.
    uint8_t direction_level; // Held U/D/L/R for repeated keyboard reports; zero means released.
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
// Atomically reserve an ordered press/release pair; leave queued frames intact
// if full. Physical D-pad state takes priority over generated clicks.
bool ub_queue_direction_click(ub_queue *queue,const ub_frame *base,uint8_t direction,uint32_t *sequence);
// Generated directions have their own bounded queue. Normal button transitions
// and the mouse endpoint never wait for a generated direction's hold interval.
#define UB_DIRECTION_PENDING 4u
enum { UB_DIRECTION_IDLE,UB_DIRECTION_PRESS_WAIT,UB_DIRECTION_HELD,UB_DIRECTION_RELEASE_WAIT,UB_DIRECTION_GAP };
typedef struct {
    struct { uint32_t received_us; uint8_t direction; } pending[UB_DIRECTION_PENDING];
    uint32_t phase_at,source_us,cookie;
    uint32_t press_samples,press_min,press_max,gap_samples,gap_min,gap_max;
    uint8_t read,count,phase,level;
    bool awaiting,have_release;
} ub_direction_pulse;
void ub_direction_reset(ub_direction_pulse *pulse);
bool ub_direction_offer(ub_direction_pulse *pulse,uint8_t direction,uint32_t received_us);
unsigned ub_direction_pending_frames(const ub_direction_pulse *pulse);
void ub_direction_prepare(ub_direction_pulse *pulse,uint32_t now,uint32_t hold_us,uint32_t wait_us,ub_frame *frame);
void ub_direction_sent(ub_direction_pulse *pulse,uint8_t edge,uint32_t cookie);
// Return an edge only on its first successful completion. Failed transfers can
// be retried; stale completions after reset never start a hold timer.
uint8_t ub_direction_complete(ub_direction_pulse *pulse,uint8_t edge,uint32_t cookie,uint32_t now,bool success);
bool ub_store_load(unsigned slot, void *data, uint32_t bytes);
bool ub_store_load_legacy_bank(unsigned copy, void *data, uint32_t bytes);
bool ub_store_save(unsigned slot, const void *data, uint32_t bytes, bool host_running);
// Device-wide extension in the configuration sector; the legacy 40-byte prefix
// and its CRC remain readable by earlier firmware. No profile format change.
bool ub_store_load_direction_threshold(uint32_t *threshold);
bool ub_store_load_direction_settings(udk_settings *settings);
bool ub_store_save_configuration(const ub_configuration *config,const udk_settings *settings,bool host_running);
#endif
