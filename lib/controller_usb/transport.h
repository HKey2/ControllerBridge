#ifndef UC_TRANSPORT_H
#define UC_TRANSPORT_H
#include <stdbool.h>
#include <stdint.h>
typedef enum { UT_PENDING, UT_OK, UT_ERROR, UT_STALL, UT_TIMEOUT, UT_DISCONNECTED } ut_result;
typedef struct {
    uint32_t late_frames, max_frame_us;
    uint32_t in_tokens, in_naks, in_duplicates, in_packet_errors;
} ut_metrics;
// Core 1 only. Select/reset the primary endpoint and invalidate pause windows.
void ut_display_select(uint8_t endpoint);
void ut_display_invalidate(void);
void ut_display_publish(bool connected,uint32_t interval_ms);
void ut_init(uint8_t dp_pin,bool sense_speed);
void ut_pump(void);
// Connection-time selection, followed by a dedicated direct-root input path.
bool ut_low_speed(void);
bool ut_lowspeed_begin(void);
void ut_lowspeed_pump(void);
int ut_input_handle(uint8_t endpoint);
ut_result ut_input_result(unsigned handle,uint16_t *actual);
bool ut_input_submit(unsigned handle,uint8_t *data,uint16_t length);
uint32_t ut_input_received_us(unsigned handle);
uint32_t ut_idle_budget_us(void);
void ut_idle_output(uint8_t endpoint);
void ut_idle_output_service(void);
// DS3 Output SET_REPORT, progressed one packet at a time between input frames.
bool ut_idle_control_begin(uint8_t iface,uint8_t data[48]);
ut_result ut_idle_control_service(void);
void ut_idle_control_abort(void);
void ut_abort(uint8_t address,uint8_t endpoint);
uint8_t ut_data_toggle(uint8_t address,uint8_t endpoint);
void ut_set_data_toggle(uint8_t address,uint8_t endpoint,uint8_t toggle);
void ut_delay_ms(uint32_t ms);
// Leave the bus idle without traffic; the next reset resumes discovery.
void ut_wait_for_device(uint32_t ms);
bool ut_reset(void);
void ut_resume_fullspeed(void);
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
