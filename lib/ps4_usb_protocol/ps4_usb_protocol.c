/* SPDX-License-Identifier: BSD-2-Clause
 * Copyright (c) 2026 HKey2
 */
#include "ps4_usb_protocol.h"
#include <string.h>

/* CRC-32/ISO-HDLC, reflected polynomial; the same checksum used by the
 * project's existing persistent records. This routine operates on wire bytes. */
uint32_t p4_crc32(const uint8_t *bytes,size_t length) {
    uint32_t state=UINT32_MAX;
    while(length--) {
        state^=*bytes++;
        for(unsigned bit=0;bit<8;++bit)
            state=(state>>1)^(0xedb88320u & (0u-(state&1u)));
    }
    return state^UINT32_MAX;
}
static void put32(uint8_t *p,uint32_t value) {
    for(unsigned i=0;i<4;++i)p[i]=(uint8_t)(value>>(i*8));
}
static bool checksum(const uint8_t *p) {
    uint8_t expected[4];put32(expected,p4_crc32(p,60));
    return memcmp(p+60,expected,sizeof(expected))==0;
}
static void fail(p4_session *s,p4_error reason) {
    s->phase=P4_FAILED;s->error=reason;s->issued=false;
}
void p4_begin(p4_session *s,uint8_t token,const uint8_t challenge[P4_CHALLENGE_BYTES],uint32_t now) {
    /* A complete private copy keeps the active exchange independent of the
     * owner receiving another request while this one is still being serviced. */
    memset(s,0,sizeof(*s));memcpy(s->challenge,challenge,P4_CHALLENGE_BYTES);
    s->token=token;s->deadline=now+3000000u;s->next_request=now;
}
bool p4_next(p4_session *s,uint32_t now,p4_transfer *r) {
    if(!p4_running(s) || s->issued)return false;
    if((int32_t)(now-s->deadline)>=0) { fail(s,P4_ERROR_EXPIRED);return false; }
    if((int32_t)(now-s->next_request)<0)return false;
    static const uint8_t ids[]={3,0xf3,0xf0,0xf2,0xf1};
    static const uint8_t lengths[]={48,8,64,16,64};
    memset(r,0,sizeof(*r));r->report=ids[s->phase];r->length=lengths[s->phase];
    r->write=s->phase==P4_CHALLENGE;
    if(r->write) {
        const unsigned offset=s->page*56u;
        const unsigned available=P4_CHALLENGE_BYTES-offset;
        const unsigned count=available<56?available:56;
        r->data[0]=r->report;r->data[1]=s->token;r->data[2]=s->page;
        memcpy(r->data+4,s->challenge+offset,count);
        put32(r->data+60,p4_crc32(r->data,60));
    }
    s->issued=true;return true;
}
void p4_accept(p4_session *s,uint32_t now,bool transferred,const uint8_t *r,size_t n) {
    if(!p4_running(s) || !s->issued)return;
    s->issued=false;
    if(!transferred) { fail(s,P4_ERROR_TRANSFER);return; }
    if((int32_t)(now-s->deadline)>=0) { fail(s,P4_ERROR_EXPIRED);return; }
    bool valid=false;
    switch(s->phase) {
    case P4_DEFINITION:
        valid=r && n==P4_DEFINITION_BYTES && r[0]==3 && r[2]==0x27;
        if(valid)s->phase=P4_CHANNEL;
        break;
    case P4_CHANNEL:
        valid=r && n==8 && r[0]==0xf3 && r[2]==56 && r[3]==56;
        if(valid)s->phase=P4_CHALLENGE;
        break;
    case P4_CHALLENGE:
        valid=true;
        if(++s->page==P4_CHALLENGE_PAGES) { s->phase=P4_WAIT_RESPONSE;s->page=0; }
        break;
    case P4_WAIT_RESPONSE:
        valid=r && n==16 && r[0]==0xf2 && r[1]==s->token && (r[2]==0 || r[2]==0x10);
        if(valid) {
            if(r[2]==0)s->phase=P4_RESPONSE;
            else s->next_request=now+10000u;
        }
        break;
    case P4_RESPONSE:
        valid=r && n==64 && r[0]==0xf1 && r[1]==s->token && r[2]==s->page && checksum(r);
        if(valid) {
            if(++s->page==P4_RESPONSE_PAGES)s->phase=P4_FINISHED;
        }
        break;
    default:break;
    }
    if(!valid)fail(s,P4_ERROR_FORMAT);
}
