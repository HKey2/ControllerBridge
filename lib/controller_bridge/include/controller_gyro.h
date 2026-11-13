#ifndef CONTROLLER_GYRO_H
#define CONTROLLER_GYRO_H
#include <stdbool.h>
#include <stdint.h>

// One DS3 sensor is shared by all steering assignments. Q8 raw counts and
// receipt timestamps avoid depending on an undocumented angular-rate scale.
typedef struct {
    int32_t bias_q8,noise_q8,filtered_q8,rate_q8;
    int64_t quiet_sum;
    uint32_t time,quiet_us,held_us,motion_us,samples;
    uint16_t history[5],low,high,accel_low[3],accel_high[3];
    uint8_t cursor;
    bool seen,moving,held,hold_done,manual,pending_recenter,reliable;
} up_gyro;
typedef struct {
    int64_t delta;
    uint32_t elapsed;
    bool recentered,calibrated;
} up_gyro_step;

void up_gyro_init(up_gyro *g,int32_t bias_q8,uint16_t noise_q8);
void up_gyro_begin_calibration(up_gyro *g);
void up_gyro_recenter(up_gyro *g);
up_gyro_step up_gyro_update(up_gyro *g,uint16_t raw,const uint16_t accel[3],
                          bool accel_valid,uint32_t time,uint32_t maximum_gap,bool held);
#endif
