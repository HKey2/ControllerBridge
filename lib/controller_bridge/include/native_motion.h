#ifndef NATIVE_MOTION_H
#define NATIVE_MOTION_H
#include <stdbool.h>
#include <stdint.h>
// Samples use the bridge's Pro calibration: 4096 units/g and 13371/936 units/(deg/s).
// Pro axes are towards the triggers, towards the left grip, and out of the face.
typedef struct { uint32_t time_us;int16_t value[6]; } unm_sample;
typedef struct {
    unm_sample samples[16];
    int16_t pro_zero[6];int32_t pro_span[6];
    uint16_t ds3_cal[8];float switch2_bias[6];
    uint32_t last_stamp,bias_start,bias_sum;unsigned bias_count;
    uint16_t bias_min[4],bias_max[4];float ds3_zero;
    uint8_t head,count,ds3_axis;bool stamp_valid,bias_ready; // DS3 destination: 0 Z, 1 X, 2 Y.
} unm_state;
void unm_init(unm_state *state);
void unm_restart(unm_state *state);
bool unm_calibrate(unm_state *state,uint8_t protocol,const uint8_t *data,unsigned length);
bool unm_input(unm_state *state,uint8_t protocol,const uint8_t *data,unsigned length,uint32_t now_us,bool quiet);
void unm_encode(const unm_state *state,uint8_t output[36],uint32_t now_us,uint8_t gyro_range,uint8_t accel_range);
#endif
