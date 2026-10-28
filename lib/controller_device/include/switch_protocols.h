#ifndef SWITCH_PROTOCOLS_H
#define SWITCH_PROTOCOLS_H
#include "controller_device.h"
#include "switch_wired.h"
enum { SP_POWERA, SP_POWERA_USB2, SP_POWERA_PACKET8, SP_HORI, SP_PRO8, SP_PRO1, SP_NSO4, SP_NSO1, SP_COUNT };
typedef struct { uint32_t handshake_mask, subcommand_mask, output_count, unknown_count; uint8_t mode; bool active; } sp_state;
// Build descriptors in persistent internal buffers; call only while disconnected.
void sp_descriptors(unsigned profile, ud_options *options);
void sp_encode(unsigned profile,const sw_report *input,uint8_t output[64],uint32_t now_us);
// Interpret a Pro Controller host command. Returns true when a reply is needed.
bool sp_command(sp_state *state,const uint8_t *command,unsigned length,
                const sw_report *input,uint8_t reply[64],uint32_t now_us);
unsigned sp_nso_command(sp_state *state,const uint8_t *command,unsigned length,uint8_t reply[128]);
int sp_nso_control(const uint8_t setup[8],uint8_t *reply,uint16_t capacity,void *context);
#endif
