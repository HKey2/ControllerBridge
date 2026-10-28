#ifndef UC_TRANSPORT_H
#define UC_TRANSPORT_H
#include <stdbool.h>
#include <stdint.h>
typedef enum { UT_PENDING, UT_OK, UT_ERROR, UT_STALL, UT_TIMEOUT } ut_result;
typedef struct {
    uint32_t late_frames, max_frame_us;
    uint32_t in_tokens, in_naks, in_duplicates, in_packet_errors;
} ut_metrics;
void ut_init(uint8_t dp_pin);
void ut_pump(void);
uint32_t ut_idle_budget_us(void);
void ut_delay_ms(uint32_t ms);
void ut_reset(void);
bool ut_open(uint8_t address, const uint8_t descriptor[7]);
void ut_close_device(uint8_t address);
void ut_ep0(uint8_t address, uint8_t packet_size);
ut_result ut_control(uint8_t address, const uint8_t setup[8], uint8_t *data,
                     uint16_t *actual);
bool ut_submit(uint8_t address, uint8_t endpoint, uint8_t *data, uint16_t length);
ut_result ut_result_for(uint8_t address, uint8_t endpoint, uint16_t *actual);
ut_result ut_interrupt_out(uint8_t address,const uint8_t descriptor[7],uint8_t *data,uint16_t length);
ut_result ut_transfer_wait(uint8_t address,uint8_t endpoint);
void ut_close_endpoint(uint8_t address,uint8_t endpoint);
void ut_set_interval(uint8_t address, uint8_t endpoint, uint8_t interval);
void ut_metrics_get(ut_metrics *metrics);
uint32_t ut_received_us(uint8_t address, uint8_t endpoint);
#endif
