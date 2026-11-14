#ifndef SWITCH_RUMBLE_H
#define SWITCH_RUMBLE_H
#include <stdbool.h>
#include <stdint.h>

// Per-motor logarithmic amplitude/frequency in units of 1/32.
// Wire facts: Switch HD one/two/three-sample command formats.
typedef struct { int16_t amplitude[2],frequency[2]; } sr_motor;
typedef struct { sr_motor motor[2]; } sr_state;
// Up to three chronological samples per motor; a hold has count zero.
typedef struct { sr_motor motor[2][3];uint8_t count[2]; } sr_samples;
void sr_decode_samples(sr_state *state,const uint8_t data[8],sr_samples *samples);
unsigned sr_switch2(const sr_state *state,const sr_samples *samples,uint8_t sequence,uint8_t report[64]);
void sr_reset(sr_state *state);
void sr_decode(sr_state *state,const uint8_t data[8]);
void sr_absolute(const sr_state *state,uint8_t data[8]);
bool sr_active(const sr_state *state);
extern const uint8_t sr_neutral[8];
#endif
