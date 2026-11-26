#include "gamecube_output.h"
#include <stdatomic.h>
#include "hardware/sync.h"
static atomic_uint epoch,pending;
static uint32_t current_epoch,flight_epoch;
static uint8_t reply[GC_REPORT_BYTES];
static unsigned reply_length;
static bool polling;
void gco_init(void) {
    current_epoch=atomic_fetch_add_explicit(&epoch,1,memory_order_acq_rel)+1;
    atomic_store_explicit(&pending,0,memory_order_release);reply_length=0;polling=false;
}
bool gco_receive(const uint8_t *data,uint16_t length,void *context) {
    (void)context;
    if(!length || data[0]<0x11 || data[0]>0x15)return true;
    if(data[0]==0x11)return true; // This mode does not expose vibration.
    if(atomic_load_explicit(&pending,memory_order_acquire))return false;
    atomic_store_explicit(&pending,data[0],memory_order_release);return true;
}
void gco_bus_event(unsigned event,void *context) {
    (void)context;
    // Suspend retains the host's polling selection; reset and disconnect do not.
    if(event==UD_BUS_SUSPEND)return;
    atomic_store_explicit(&pending,0,memory_order_release);
    atomic_fetch_add_explicit(&epoch,1,memory_order_release);
}
void gco_service(void) {
    uint32_t irq=save_and_disable_interrupts();
    uint32_t now=atomic_load_explicit(&epoch,memory_order_acquire);
    if(now!=current_epoch) { current_epoch=now;reply_length=0;polling=false; }
    if(reply_length) { restore_interrupts(irq);return; }
    unsigned command=atomic_exchange_explicit(&pending,0,memory_order_acq_rel);
    if(command)reply_length=gc_command((uint8_t)command,&polling,reply);
    restore_interrupts(irq);
}
bool gco_polling(void) { return polling && current_epoch==atomic_load_explicit(&epoch,memory_order_acquire); }
const uint8_t *gco_reply(unsigned *length) {
    *length=reply_length;
    return reply_length && current_epoch==atomic_load_explicit(&epoch,memory_order_acquire)?reply:NULL;
}
void gco_reply_started(void) { flight_epoch=current_epoch; }
void gco_reply_complete(bool success) {
    if(success && flight_epoch==current_epoch)reply_length=0;
}
