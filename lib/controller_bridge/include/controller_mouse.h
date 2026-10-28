#ifndef CONTROLLER_MOUSE_H
#define CONTROLLER_MOUSE_H
#include <stdbool.h>
#include <stdint.h>
#include "usb_mouse.h"
#include "controller_profile.h"

// Legacy configuration byte 7 was zero/reserved. No size or Flash layout change.
#define UM_MOUSE 1u
#define UM_GAIN_MASK 14u
#define UM_INVERT_X 16u
#define UM_INVERT_Y 32u
#define UM_COMBINED 64u // Requires UM_MOUSE; pad plus mouse, separate endpoints.
#define UM_GAMEPAD_BUTTONS 128u // Requires UM_COMBINED: full mapped pad, no mouse clicks.
#define UM_OPTIONS_MASK 255u
#define UM_QUEUE_CAPACITY 64u
typedef struct { int32_t x,y; uint32_t time,sequence,sources; } um_frame;
typedef struct { uint32_t time,sequence; uint8_t buttons; } um_button_frame;
typedef struct {
    um_report report;
    uint32_t time,sequence,epoch;
    uint32_t complete_frames;
    int64_t partial_x,partial_y;
    uint32_t button_time,button_sequence,button_epoch;
    bool button_queued,merged_motion,wide;
    bool queued;
} um_flight;
typedef struct {
    um_frame frames[UM_QUEUE_CAPACITY];
    unsigned read,write,count,pending_sources;
    uint32_t epoch;
    um_button_frame button_frames[UM_QUEUE_CAPACITY];
    unsigned button_read,button_write,button_count;
    uint32_t button_epoch,button_inputs,button_drops,button_max_depth;
    uint8_t buttons,acked_buttons;
    uint32_t inputs,movement_inputs,movement_acks,button_acks,drops,max_depth,flushes;
    // Modulo-2^32 totals permit comparison without signed overflow.
    uint32_t input_x,input_y,acked_x,acked_y;
} um_queue;
void um_init(um_queue *queue);
// Return mouse click bits and route the mapped pad in place. Full-pad mode
// preserves every mapped control; legacy combined mode keeps only -/+ /HOME.
uint8_t um_route_buttons(uint8_t options,sw_report *pad);
void um_flush(um_queue *queue,uint8_t buttons);
// A click can be pending even when the motion queue is empty.
bool um_pending(const um_queue *queue);
int32_t um_scale(int32_t value,uint8_t options,bool y_axis);
typedef struct { int32_t x10,y10; } um_fraction;
// Mix independently enabled sources at tenths precision; retain signed fractions.
void um_mix(um_fraction *fraction,const up_mouse_options *options,int32_t raw_x,int32_t raw_y,int32_t wheel,int32_t *x,int32_t *y);
// Diagnostic constant-speed vector; ignores gain and paddle, keeps direction.
void um_fixed_delta(const up_mouse_options *options,int32_t raw_x,int32_t raw_y,unsigned threshold,unsigned amplitude,int32_t *x,int32_t *y);
void um_push(um_queue *queue,int32_t x,int32_t y,uint8_t buttons,uint32_t time,uint32_t sequence,bool source_input);
void um_next(const um_queue *queue,um_flight *flight);
// Report protocol can carry a whole high-gain delta without 8-bit splitting.
// The legacy wrapper above deliberately keeps its 127-count limit.
void um_next_wide(const um_queue *queue,um_flight *flight,bool wide);
// Diagnostic packets must expose one source vector without merging magnitudes.
void um_next_single(const um_queue *queue,um_flight *flight,bool wide);
// Commit only on host ACK. Failed transfers are retried by the caller, or flushed
// on disconnect/reset. An obsolete flight must never consume a new epoch's data.
void um_ack(um_queue *queue,const um_flight *flight);
#endif
