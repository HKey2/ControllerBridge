#ifndef CONTROLLER_PADDLE_POINT_H
#define CONTROLLER_PADDLE_POINT_H
#include "controller_profile.h"

// Raw cursor motion and the shaped host response have separate ACK ledgers.
// The first keeps whole source counts plus a /1e6 integration remainder;
// the second keeps effective output * microseconds, including sub-axis quanta.
typedef struct {
    int64_t raw_rate;
    int32_t requested_response,applied_response;
    uint8_t axis;
} ump_plan;
typedef struct {
    int64_t pending,raw_remainder,applied_raw_rate,release_raw_rate,response_phase;
    int32_t requested_response,applied_response;
    uint32_t positive_range,negative_range,native_period_us,reference_counts;
    uint32_t last_ack_us,last_motion_us,last_report_us,observed_us,planned_interval_us;
    uint32_t release_at_us,release_duration_us,cache_us;
    int8_t direction;
    bool have_ack,have_report,moving,decaying,cache_valid;
    ump_plan cached;
} ump_state;
void ump_input(ump_state *s,int32_t raw,bool invert,int32_t minimum,int32_t maximum,
               uint32_t native_period_us,uint32_t reference_counts,uint32_t now,const up_paddle_analog *settings);
void ump_prepare(ump_state *s,uint32_t now,uint32_t idle,uint32_t interval,
                 const up_paddle_analog *settings,ump_plan *plan);
void ump_ack(ump_state *s,const ump_plan *plan,uint32_t now);
#endif
