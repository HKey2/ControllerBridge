#ifndef UC_INPUT_PROTOCOL_H
#define UC_INPUT_PROTOCOL_H
#include "controller_usb.h"

enum { UC_PROTOCOL_HID=1, UC_PROTOCOL_XBOX360=2, UC_PROTOCOL_SWITCH_PRO=4 };
typedef struct {
    uint8_t number,protocol;
    uint16_t report_length;
    uint8_t output_descriptor[7]; // Initialization endpoint for supported protocols.
} uc_input_interface;
typedef struct {
    uint8_t descriptor[7],interface_number,protocol;
} uc_input_endpoint;
typedef struct {
    uc_input_interface interfaces[UC_MAX_INTERFACES];
    uc_input_endpoint endpoints[UC_MAX_ENDPOINTS];
    unsigned interface_count,endpoint_count;
    uint8_t protocols;
} uc_input_configuration;

// Full-speed, alternate setting zero, HID or wired Xbox 360 gamepad only.
bool uc_input_config_parse(const uint8_t *data,size_t length,uc_input_configuration *out);
bool uc_input_config_identify(uc_input_configuration *out,uint16_t vid,uint16_t pid);
// Internal bit-field schema, not a descriptor obtained from the Xbox device.
const uint8_t *uc_xbox360_layout(uint16_t *length);
const uint8_t *uc_switch_pro_layout(uint16_t *length);
bool uc_input_packet_valid(uint8_t protocol,const uint8_t *data,size_t length);
#endif
