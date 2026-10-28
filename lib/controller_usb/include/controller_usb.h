#ifndef CONTROLLER_USB_H
#define CONTROLLER_USB_H
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

// Current backend: one directly attached full-speed device. Bounds are explicit;
// unsupported configurations are rejected, never truncated and presented as valid.
#define UC_CONFIG_CAPACITY 1024
#define UC_REPORT_DESCRIPTOR_CAPACITY 2048
#define UC_MAX_INTERFACES 4
#define UC_MAX_ENDPOINTS 8
#define UC_EVENT_BYTES 64

typedef enum { UC_STATUS, UC_DESCRIPTOR, UC_INPUT, UC_STATISTICS } uc_event_type;
typedef enum {
    UC_START, UC_PROBING, UC_PROBE_FAILED, UC_DEVICE_FOUND, UC_REJECTED,
    UC_CONFIGURED, UC_REPORT_DESCRIPTOR_FAILED, UC_INPUT_ERROR,
    UC_DISCONNECTED, UC_POLL_CHANGED, UC_ENUM_FAILED, UC_INPUT_PROTOCOL, UC_INPUT_INIT
} uc_status;
typedef struct {
    uint32_t reports, transfer_errors, stalls, attempts;
    uint32_t frame_late, frame_us_max, poll_interval_ms, generation;
    uint32_t in_tokens, in_naks, in_duplicates, in_packet_errors;
} uc_statistics;
typedef struct {
    uint32_t time_us, sequence;
    uint16_t offset, total, length;
    uint8_t type, code, interface_number, endpoint;
    uint8_t data[UC_EVENT_BYTES];
} uc_event;
typedef void (*uc_event_callback)(const uc_event *event, void *context);
typedef bool (*uc_accept_device)(uint16_t vid, uint16_t pid, void *context);
typedef struct {
    uint8_t dp_pin;
    uc_accept_device accept;
    uc_event_callback event;
    void *context;
} uc_host_options;

// Run on a dedicated core. Callbacks are synchronous on that core and must not
// block (especially not for USB logging). All transfer buffers stay on this core.
void uc_host_run(const uc_host_options *options);
typedef struct {
    uint32_t samples,irq_mask_max_us,host_pause_max_us,offers_expired,budget_overruns,last_sample_us,pressed;
} uc_bootsel_statistics;
bool uc_bootsel_poll(bool enabled,bool *pressed,uint32_t *sampled_us);
void uc_bootsel_statistics_get(uc_bootsel_statistics *statistics);
// Core 1 only, outside active PIO USB transfers, with interrupts already disabled.
void uc_bootsel_host_service(void);
// Thread-safe diagnostic requests, consumed between transfers by the host core.
void uc_request_dump(void);
void uc_request_poll_interval(uint8_t interval_ms); // 0 restores bInterval
void uc_request_statistics(void);
// Core 0 flash writer: park the host core in SRAM before disabling XIP.
// A timeout leaves the host running and the caller must not write flash.
bool uc_host_pause(uint32_t timeout_ms);
void uc_host_resume(void);

// A bounded HID layout parser, independent of the PIO backend and of any VID/PID.
// Scalar Variable Input fields are decoded; arrays remain available as raw data.
#define UC_HID_MAX_FIELDS 64
typedef struct {
    uint16_t usage_page, usage, bit_offset;
    uint8_t bits, report_id;
    int32_t logical_min, logical_max;
    uint16_t flags; // HID Input flags, including relative (bit 2)
} uc_hid_field;
typedef struct {
    uc_hid_field fields[UC_HID_MAX_FIELDS];
    uint16_t count;
    bool has_report_ids;
    bool unsupported_inputs; // array/relative inputs cannot be learned as absolute controls
} uc_hid_layout;
bool uc_hid_parse(const uint8_t *descriptor, size_t length, uc_hid_layout *layout);
// Cache the returned field after descriptor parsing for repeated low-latency reads.
const uc_hid_field *uc_hid_find(const uc_hid_layout *layout, uint16_t usage_page,
                              uint16_t usage, uint8_t report_id);
bool uc_hid_field_value(const uc_hid_field *field, bool has_report_ids,
                        const uint8_t *report, size_t length, int32_t *value);
bool uc_hid_value(const uc_hid_layout *layout, uint16_t usage_page, uint16_t usage,
                  const uint8_t *report, size_t length, int32_t *value);
#endif
