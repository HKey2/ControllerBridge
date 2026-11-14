#include "switch_output.h"
#include "switch_protocols.h"
#include "controller_feedback.h"
#include "switch_rumble.h"
#include "pico/stdlib.h"
#include <stdatomic.h>
#include <string.h>

#define RX_COUNT 8u
#define REPLY_COUNT 4u
typedef struct { uint32_t epoch,serial;uint8_t length,data[64]; } received;
static received rx[RX_COUNT];
static atomic_uint rx_head,rx_tail,bus_epoch,bus_events,rx_serial,stop_serial;
static uint8_t replies[REPLY_COUNT][64];
static unsigned reply_read,reply_write;
static uint32_t current_epoch,flight_epoch,seen_stop,feedback_epoch;
static bool selected,resync;
static sp_state protocol;
static sr_state source;
static void effect_stop(void) { ucf_select(false);selected=false;resync=true;feedback_epoch=ucf_epoch(); }
void spo_stop(void) { effect_stop(); }
void spo_init(void) {
    memset(&protocol,0,sizeof(protocol));protocol.mode=0x30;
    sr_reset(&source);reply_read=reply_write=0;
    atomic_store_explicit(&rx_head,0,memory_order_relaxed);atomic_store_explicit(&rx_tail,0,memory_order_relaxed);
    current_epoch=atomic_fetch_add_explicit(&bus_epoch,1,memory_order_acq_rel)+1;
    atomic_store_explicit(&bus_events,0,memory_order_relaxed);
    seen_stop=atomic_load_explicit(&stop_serial,memory_order_relaxed);effect_stop();
}
bool spo_receive(const uint8_t *data,uint16_t length,void *context) {
    (void)context;
    if(!length || length>64)return true;
    // Observe OFF even while its command waits for reply/queue capacity.
    // Retries retain the next serial until the packet is accepted.
    if(length>=12 && data[0]==1 && data[10]==0x48 && data[11]==0)
        atomic_store_explicit(&stop_serial,atomic_load_explicit(&rx_serial,memory_order_relaxed)+1,memory_order_release);
    unsigned write=atomic_load_explicit(&rx_head,memory_order_relaxed),next=(write+1)%RX_COUNT;
    if(next==atomic_load_explicit(&rx_tail,memory_order_acquire))return false;
    uint32_t serial=atomic_fetch_add_explicit(&rx_serial,1,memory_order_relaxed)+1;
    rx[write].epoch=atomic_load_explicit(&bus_epoch,memory_order_relaxed);rx[write].serial=serial;
    rx[write].length=(uint8_t)length;memcpy(rx[write].data,data,length);
    atomic_store_explicit(&rx_head,next,memory_order_release);return true;
}
void spo_bus_event(unsigned event,void *context) {
    (void)context;
    atomic_fetch_or_explicit(&bus_events,1u<<event,memory_order_release);
    atomic_fetch_add_explicit(&bus_epoch,1,memory_order_release);
}
void spo_service(const sw_report *input,bool allowed) {
    allowed=allowed && !ucf_inhibited();
    if(feedback_epoch!=ucf_epoch()) {
        // Flash writes quiesce the physical output. Resume only on a fresh host
        // command; packets held before that stop must not restart an old effect.
        selected=false;resync=true;feedback_epoch=ucf_epoch();
        ud_output_discard();
        atomic_store_explicit(&rx_tail,atomic_load_explicit(&rx_head,memory_order_acquire),memory_order_release);
    }
    uint32_t epoch=atomic_load_explicit(&bus_epoch,memory_order_acquire);
    if(epoch!=current_epoch) {
        current_epoch=epoch;unsigned events=atomic_exchange_explicit(&bus_events,0,memory_order_acq_rel);
        effect_stop();reply_read=reply_write=0;
        if(events&((1u<<UD_BUS_RESET)|(1u<<UD_BUS_DISCONNECT))) {
            memset(&protocol,0,sizeof(protocol));protocol.mode=0x30;sr_reset(&source);
        }
    }
    uint32_t stop=atomic_load_explicit(&stop_serial,memory_order_acquire);
    if(stop!=seen_stop) { seen_stop=stop;protocol.rumble_enabled=false;effect_stop(); }
    if(!allowed && selected)effect_stop();
    unsigned read=atomic_load_explicit(&rx_tail,memory_order_relaxed);
    if(read==atomic_load_explicit(&rx_head,memory_order_acquire))return;
    const received *r=&rx[read];
    if(r->epoch!=current_epoch) { atomic_store_explicit(&rx_tail,(read+1)%RX_COUNT,memory_order_release);return; }
    const uint8_t *c=r->data;unsigned n=r->length;
    bool reply=(n>=2 && c[0]==0x80 && c[1]!=4) || (n>=11 && c[0]==1);
    unsigned next_reply=(reply_write+1)%REPLY_COUNT;
    if(reply && next_reply==reply_read)return;
    bool older_stop=(int32_t)(r->serial-seen_stop)<0;
    bool off=n>=12 && c[0]==1 && c[10]==0x48 && c[11]==0;
    bool hd=(c[0]==0x10 && n>=10) || (c[0]==1 && n>=11);
    bool on=n>=12 && c[0]==1 && c[10]==0x48 && c[11]==1;
    if(hd && allowed && (protocol.rumble_enabled || on) && !off && !older_stop) {
        if(!selected) { ucf_select(true);feedback_epoch=ucf_epoch();selected=true;resync=true; }
        uint8_t baseline[8];sr_absolute(&source,baseline);
        // A resumed session gets one absolute baseline before a dependent frame.
        // Absolute neutral never needs an old nonzero baseline.
        const uint8_t *first=resync && memcmp(c+2,sr_neutral,8)?baseline:NULL;
        if(!ucf_submit_pair(first,c+2))return;
        sr_decode(&source,c+2);resync=false;
    } else if(hd && allowed && !older_stop)sr_decode(&source,c+2);
    if(sp_command(&protocol,c,n,input,replies[reply_write],time_us_32()))reply_write=next_reply;
    if(older_stop)protocol.rumble_enabled=false;
    if(off)effect_stop();
    atomic_store_explicit(&rx_tail,(read+1)%RX_COUNT,memory_order_release);
}
const uint8_t *spo_reply(void) {
    return current_epoch!=atomic_load_explicit(&bus_epoch,memory_order_acquire) || reply_read==reply_write?NULL:replies[reply_read];
}
void spo_reply_started(void) { flight_epoch=current_epoch; }
void spo_reply_complete(bool success) {
    if(success && flight_epoch==current_epoch && reply_read!=reply_write)reply_read=(reply_read+1)%REPLY_COUNT;
}
void spo_status(uint8_t data[56]) {
    uint32_t status=ucf_status();
    memset(data,0,56);memcpy(data,"RMB1",4);data[4]=1;data[5]=(uint8_t)status;
    data[6]=(uint8_t)(status>>16);data[7]=((status&512)?1:0)|((status&256)?2:0)|
        ((status&1024) || (status&255)==UCF_ACTIVE?4:0)|((status&2048)?8:0)|(ucf_inhibited()?16:0);
    uint32_t epoch=ucf_epoch(),pad=ucf_pad_generation(),token=ucf_calibration_token();
    uint32_t features=(1u<<0)|(1u<<3)|(1u<<4)|(1u<<8)|(1u<<9)|(1u<<10);
    memcpy(data+8,&epoch,4);memcpy(data+12,&pad,4);memcpy(data+20,&token,4);memcpy(data+40,&features,4);
}
