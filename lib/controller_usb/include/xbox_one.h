#ifndef UC_XBOX_ONE_H
#define UC_XBOX_ONE_H
#include "controller_usb.h"

typedef enum {
    UC_XONE_STANDARD, UC_XONE_ELITE, UC_XONE_ELITE2_OLD,
    UC_XONE_ELITE2_EARLY, UC_XONE_ELITE2_SPLIT
} uc_xbox_one_model;
typedef struct {
    uint16_t vid, pid;
    uc_xbox_one_model model;
} uc_xbox_one;

void uc_xbox_one_identify(uc_xbox_one *pad,uint16_t vid,uint16_t pid,uint16_t revision);
// Synthetic HID schemas describe the original GIP bytes, including command IDs.
// Separate guide/paddle packets leave other controls intact in the existing mapper.
uint16_t uc_xbox_one_layout(const uc_xbox_one *pad,uint8_t out[UC_REPORT_DESCRIPTOR_CAPACITY]);
bool uc_xbox_one_input(const uc_xbox_one *pad,const uint8_t *data,size_t length);
bool uc_xbox_one_hello(const uc_xbox_one *pad,const uint8_t *data,size_t length);
bool uc_xbox_one_guide_ack(const uint8_t *data,size_t length);
// Indexed startup stages. A zero length skips a model-specific stage.
#define UC_XBOX_ONE_INIT_STAGES 8
uint8_t uc_xbox_one_init(const uc_xbox_one *pad,unsigned stage,uint8_t sequence,uint8_t out[13]);
void uc_xbox_one_ack(uint8_t sequence,uint8_t out[13]);
// MS-GIPUSB variable-length header. A USB transfer may contain several messages.
typedef struct {
    const uint8_t *payload;
    uint32_t position;
    uint16_t size, consumed;
    uint8_t command, flags, sequence;
} uc_gip_message;
typedef struct {
    uint32_t total, received;
    uint8_t sequence;
    bool active, complete;
} uc_gip_metadata;
bool uc_gip_parse(const uint8_t *data,size_t length,uc_gip_message *message);
// Metadata is consumed as a stream: this driver uses the compiled gamepad
// schema, so storing the entire descriptive blob would serve no input purpose.
bool uc_gip_fragment_receive(uc_gip_metadata *state,const uc_gip_message *message);
bool uc_gip_metadata_receive(uc_gip_metadata *state,const uc_gip_message *message);
void uc_gip_ack(const uc_gip_message *message,uint32_t received,uint16_t remaining,uint8_t out[13]);
#endif
