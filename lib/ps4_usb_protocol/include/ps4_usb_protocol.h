/* SPDX-License-Identifier: BSD-2-Clause
 * Copyright (c) 2026 HKey2
 */
#ifndef PS4_USB_PROTOCOL_H
#define PS4_USB_PROTOCOL_H
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

/* Wire contract: docs/ps4-usb-protocol.md. No USB stack or board dependency. */
enum {
    P4_DEFINITION_BYTES=48,
    P4_CHALLENGE_BYTES=256, P4_CHALLENGE_PAGES=5,
    P4_RESPONSE_PAGES=19
};
typedef enum {
    P4_DEFINITION, P4_CHANNEL, P4_CHALLENGE, P4_WAIT_RESPONSE,
    P4_RESPONSE, P4_FINISHED, P4_FAILED
} p4_phase;
typedef enum { P4_ERROR_NONE, P4_ERROR_TRANSFER, P4_ERROR_FORMAT, P4_ERROR_EXPIRED } p4_error;
typedef struct {
    uint8_t report, length;
    bool write;
    uint8_t data[64];
} p4_transfer;
typedef struct {
    uint8_t challenge[P4_CHALLENGE_BYTES];
    uint32_t deadline, next_request;
    uint8_t token, page;
    p4_phase phase;
    p4_error error;
    bool issued;
} p4_session;

uint32_t p4_crc32(const uint8_t *bytes,size_t length);
void p4_begin(p4_session *s,uint8_t token,const uint8_t challenge[P4_CHALLENGE_BYTES],uint32_t now);
bool p4_next(p4_session *s,uint32_t now,p4_transfer *request);
void p4_accept(p4_session *s,uint32_t now,bool transferred,const uint8_t *reply,size_t length);
static inline bool p4_running(const p4_session *s) { return s->phase<P4_FINISHED; }

#endif
