// Optional RAM-only trace, armed explicitly. Freeze at the next host bus reset.
// No output identity/protocol changes and no Flash writes.
#include "usb_control_trace.h"
#include "pico/stdlib.h"
#include "hardware/structs/usb.h"
#include "hardware/sync.h"
#include <string.h>
#define TRACE_COUNT 512u
typedef struct { uint32_t time,event,a,b,in,out,sie,buffers; } trace_entry;
static trace_entry entries[TRACE_COUNT];
static uint32_t head;
static bool armed;
static void record(uint32_t event,uint32_t a,uint32_t b) {
    if(!armed)return;
    entries[head%TRACE_COUNT]=(trace_entry){time_us_32(),event,a,b,
        usb_dpram->ep_buf_ctrl[0].in,usb_dpram->ep_buf_ctrl[0].out,usb_hw->sie_status,usb_hw->buf_status};
    head++;
}
void uct_setup(const uint8_t setup[8],void *context) {
    (void)context;uint32_t a,b;memcpy(&a,setup,4);memcpy(&b,setup+4,4);record(0,a,b);
}
void uct_event(uint32_t event,uint32_t detail,void *context) {
    (void)context;
    if(event==1) {
        if(!(detail&(USB_INTS_BUS_RESET_BITS|USB_INTS_SETUP_REQ_BITS)) && !(usb_hw->buf_status&3))return;
    } else if(event==2 && !(detail&3))return;
    record(event,detail,0);
    if(event==1 && (detail&USB_INTS_BUS_RESET_BITS))armed=false;
}
bool uct_command(uint8_t cmd,const uint8_t arg[56],uint8_t reply[56],uint8_t *status) {
    if(cmd!=27 && cmd!=28)return false;
    uint32_t irq=save_and_disable_interrupts();
    if(cmd==27) {
        if(arg[0]==1) { head=0;armed=true; }
        if(arg[0]==2)armed=false;
        uint32_t header[]={0x31435254u,head,head<TRACE_COUNT?head:TRACE_COUNT,armed,sizeof(trace_entry)};
        memcpy(reply,header,sizeof(header));
    } else {
        uint16_t index;memcpy(&index,arg,2);uint32_t count=head<TRACE_COUNT?head:TRACE_COUNT;
        if(index>=count)*status=1;
        else memcpy(reply,&entries[(head-count+index)%TRACE_COUNT],sizeof(trace_entry));
    }
    restore_interrupts(irq);return true;
}
