#include "xinput_feedback.h"
#include "controller_feedback.h"
#include "controller_device.h"
#include <stdatomic.h>

#define XFO_COUNT 8u
typedef struct { uint32_t epoch;uint8_t strong,weak; } motor_command;
static motor_command queue[XFO_COUNT];
static atomic_uint head,tail,bus_epoch;
static atomic_bool accepting;
static uint32_t current_epoch,feedback_epoch;
static bool selected;
static void discard_received(void) {
    atomic_store_explicit(&accepting,false,memory_order_release);
    atomic_store_explicit(&tail,atomic_load_explicit(&head,memory_order_acquire),memory_order_release);
    selected=false;
    ud_output_discard();
}
void xfo_stop(void) {
    discard_received();ucf_select(false);feedback_epoch=ucf_epoch();
}
void xfo_init(void) {
    atomic_store_explicit(&head,0,memory_order_relaxed);atomic_store_explicit(&tail,0,memory_order_relaxed);
    current_epoch=atomic_fetch_add_explicit(&bus_epoch,1,memory_order_acq_rel)+1;xfo_stop();
}
bool xfo_receive(const uint8_t *data,uint16_t n,void *context) {
    (void)context;
    if(n!=8 || data[0]!=0 || data[1]!=8 || data[2]!=0)return true;
    if(!atomic_load_explicit(&accepting,memory_order_acquire))return true;
    unsigned write=atomic_load_explicit(&head,memory_order_relaxed),next=(write+1)%XFO_COUNT;
    if(next==atomic_load_explicit(&tail,memory_order_acquire))return false;
    queue[write]=(motor_command){atomic_load_explicit(&bus_epoch,memory_order_relaxed),data[3],data[4]};
    atomic_store_explicit(&head,next,memory_order_release);return true;
}
void xfo_bus_event(unsigned event,void *context) {
    (void)event;(void)context;atomic_store_explicit(&accepting,false,memory_order_release);
    atomic_fetch_add_explicit(&bus_epoch,1,memory_order_release);
}
bool xfo_pending(void) {
    return selected || atomic_load_explicit(&head,memory_order_acquire)!=atomic_load_explicit(&tail,memory_order_relaxed) ||
        current_epoch!=atomic_load_explicit(&bus_epoch,memory_order_acquire);
}
void xfo_service(bool allowed) {
    uint32_t epoch=atomic_load_explicit(&bus_epoch,memory_order_acquire);
    if(epoch!=current_epoch) { current_epoch=epoch;xfo_stop(); }
    else if(feedback_epoch!=ucf_epoch()) { discard_received();feedback_epoch=ucf_epoch(); }
    uint32_t state=ucf_status();
    allowed=allowed && !ucf_inhibited() && (state&256) && ((state>>16)&255)==1 && (state&255)!=UCF_FAULT;
    if(!allowed) {
        if(selected || atomic_load_explicit(&accepting,memory_order_relaxed))xfo_stop();
        return;
    }
    atomic_store_explicit(&accepting,true,memory_order_release);
    unsigned read=atomic_load_explicit(&tail,memory_order_relaxed);
    if(read==atomic_load_explicit(&head,memory_order_acquire))return;
    motor_command next=queue[read];
    if(next.epoch==current_epoch) {
        if(!selected) { ucf_select(true);feedback_epoch=ucf_epoch();selected=true; }
        if(!ucf_submit_motors(next.strong,next.weak))return;
    }
    atomic_store_explicit(&tail,(read+1)%XFO_COUNT,memory_order_release);
}
