#ifndef CONTROLLER_FEEDBACK_H
#define CONTROLLER_FEEDBACK_H
#include <stdbool.h>
#include <stdint.h>

enum { UCF_OFF,UCF_WAITING,UCF_READY,UCF_ACTIVE,UCF_STOPPING,UCF_FAULT };
// Core 0: a new selection always invalidates old queued effects.
void ucf_select(bool enabled);
bool ucf_submit(const uint8_t hd[8]);
bool ucf_submit_pair(const uint8_t first[8],const uint8_t second[8]);
uint32_t ucf_status(void); // low byte state, bit8 supported, bit9 requested, bits16..23 backend (4=Pro,5=Pro2)
uint32_t ucf_epoch(void);
bool ucf_quiesce(uint32_t timeout_us);
// Core 1: the Pro OUT pipe is opened once by initialization, with its toggle intact.
void ucf_attach(const uint8_t descriptor[7],uint8_t next_sequence);
void ucf_attach_switch2(const uint8_t descriptor[7]);
void ucf_detach(void);
bool ucf_pending(void);
void ucf_service(void);
void ucf_input(const uint8_t *data,unsigned length);
#endif
