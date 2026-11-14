#ifndef CONTROLLER_DEVICE_H
#define CONTROLLER_DEVICE_H
#include <stdbool.h>
#include <stdint.h>

// Full-speed input interface 0, EP0 plus interrupt IN1 and optional OUT1/OUT2.
// Optional interface 1 is vendor Feature HID, auxiliary bulk, or a second HID input.
// Call from core 0. Completion/feature callbacks run in its USB IRQ and must not block.
enum { UD_BUS_RESET=1,UD_BUS_SUSPEND,UD_BUS_DISCONNECT };

typedef struct {
    const uint8_t *device, *configuration, *report;
    uint16_t device_length, configuration_length, report_length;
    const char *manufacturer, *product, *serial;
    uint8_t input_length;
    uint32_t short_string_stall_mask; // Reference quirks: selected strings STALL instead of a terminating ZLP.
    bool relative_mouse; // GET_REPORT returns buttons with zero relative movement.
    const uint8_t *configuration_report; // Optional Feature-only HID interface 1, IN2 stays NAK.
    uint16_t configuration_report_length; // Mutually exclusive with auxiliary_bulk.
    uint8_t configuration_interface; // Zero selects the legacy default IF1.
    uint8_t configuration_endpoint; // Zero selects the legacy default IN2; XInput uses IN5/IN6.
    const uint8_t *secondary_report; // HID interface 1 / IN2; excludes the two options above.
    uint8_t secondary_interface; // Zero selects IF1; XInput appends mouse at IF4.
    uint8_t secondary_endpoint; // Zero selects IN2; XInput appends mouse at IN5.
    uint16_t secondary_report_length;
    uint8_t secondary_input_length;
    bool secondary_relative_mouse;
    const uint8_t *secondary_initial_input;
    void (*secondary_complete)(uint32_t cookie, uint32_t time_us, bool acked, void *context);
    uint8_t output_endpoint; // 0=absent, 1=OUT1, 2=OUT2 (excludes auxiliary_bulk).
    bool auxiliary_bulk; // optional vendor interface 1, bulk IN2/OUT2
    const uint8_t *initial_input;
    uint16_t (*get_feature)(uint8_t *data, uint16_t capacity, void *context);
    void (*set_feature)(const uint8_t *data, uint16_t length, void *context);
    void (*output)(const uint8_t *data, uint16_t length, void *context);
    // Optional lossless output sink: false holds OUT/EP0 status until service.
    bool (*try_output)(const uint8_t *data,uint16_t length,void *context);
    void (*bus_event)(unsigned event,void *context);
    void (*auxiliary_output)(const uint8_t *data, uint16_t length, void *context);
    // Optional read-only control request. -1 uses standard handling; -2 explicitly stalls.
    int (*control_read)(const uint8_t setup[8], uint8_t *data, uint16_t capacity, void *context);
    void (*observe_setup)(const uint8_t setup[8], void *context);
    void (*observe_event)(uint32_t event, uint32_t detail, void *context);
    void (*complete)(uint32_t cookie, uint32_t time_us, bool acked, void *context);
    void *context;
} ud_options;
typedef struct {
    uint32_t resets, setups, sof, acks, naks, control_stalls, bus_errors, abort_timeouts;
    uint32_t last_sof_us, last_ack_us, configured, suspended;
} ud_statistics;

void ud_init(const ud_options *options);
bool ud_ready(void);
void ud_output_service(void);
void ud_output_discard(void);
bool ud_busy(void);
bool ud_send(const uint8_t *data, uint16_t length, uint32_t cookie, uint32_t *armed_us);
// Safely abort a still-pending repeat. False includes the packet having just
// completed; its completion callback must be consumed before sending again.
bool ud_cancel_repeat(void);
bool ud_secondary_ready(void);
bool ud_secondary_busy(void);
bool ud_secondary_send(const uint8_t *data, uint16_t length, uint32_t cookie, uint32_t *armed_us);
bool ud_secondary_cancel_repeat(void);
// Atomically reject a mouse packet prepared for a superseded SET_PROTOCOL.
uint8_t ud_mouse_protocol(bool secondary);
bool ud_mouse_send(bool secondary,bool report_protocol,const uint8_t *data,uint16_t length,uint32_t cookie,uint32_t *armed_us);
void ud_statistics_get(ud_statistics *out);
// Disable USB callbacks and remove the pull-up. A later ud_init reconnects.
void ud_disconnect(void);
bool ud_auxiliary_send(const uint8_t *data, uint16_t length);
#endif
