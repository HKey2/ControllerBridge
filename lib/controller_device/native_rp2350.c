/* RP2350 native USB device transport.
 * Hardware setup follows Raspberry Pi's BSD-3-Clause dev_lowlevel example;
 * control state machine, report scheduling and telemetry are project code.
 * See licenses/pico-examples-BSD-3-Clause.txt.
 */
#include "controller_device.h"
#include "pico/stdlib.h"
#include "hardware/irq.h"
#include "hardware/resets.h"
#include "hardware/sync.h"
#include "hardware/structs/usb.h"
#include <string.h>

#define HW_SET ((usb_hw_t *)hw_set_alias_untyped(usb_hw))
#define HW_CLR ((usb_hw_t *)hw_clear_alias_untyped(usb_hw))
#define EP1_BIT (1u << 2)
#define OUT1_BIT (1u << 3)
#define IN2_BIT (1u << 4)
#define OUT2_BIT (1u << 5)
#define ERROR_IRQS (USB_INTS_ERROR_CRC_BITS|USB_INTS_ERROR_BIT_STUFF_BITS|USB_INTS_ERROR_RX_OVERFLOW_BITS|USB_INTS_ERROR_RX_TIMEOUT_BITS|USB_INTS_ERROR_DATA_SEQ_BITS)
typedef struct __attribute__((packed)) { uint8_t kind, request; uint16_t value, index, length; } setup_packet;
typedef enum { C_IDLE, C_IN, C_OUT, C_STATUS_IN, C_STATUS_OUT, C_STALL } control_state;
static ud_options opt;
static ud_statistics stat;
static control_state control;
static uint8_t control_data[512], last_input[64];
static uint16_t control_length, control_offset, control_expected;
static bool control_zlp, feature_out;
static uint8_t ep0_pid, ep1_pid, out1_pid, idle_rate;
static volatile uint8_t protocol=1;
static bool remote_wakeup;
static int pending_address=-1, pending_config=-1;
static volatile bool in_busy;
static bool halted,configuration_halted;
static uint32_t in_cookie;
static uint8_t aux_data[128],aux_in_pid,aux_out_pid;
static uint16_t aux_length,aux_offset;
static volatile bool aux_busy;
static bool aux_zlp;
static uint8_t secondary_last[64],secondary_pid,secondary_idle;
static volatile uint8_t secondary_protocol=1;
static volatile bool secondary_busy;
static bool secondary_halted;
static uint32_t secondary_cookie;
static bool change_mouse_protocol(bool secondary,uint8_t value);

// USB DPRAM is Device memory: libc memcpy may emit an unaligned STRH/LDRH
// for a three-byte tail (observed on RP2350 with a 203-byte HID descriptor).
// Only aligned 32-bit accesses touch DPRAM; the final word is zero padded.
static void dpram_write(volatile void *destination,const uint8_t *source,unsigned length) {
    volatile uint32_t *words=destination;
    for(unsigned offset=0;offset<length;offset+=4) {
        uint32_t word=0;
        for(unsigned b=0;b<4 && offset+b<length;++b)word|=(uint32_t)source[offset+b]<<(b*8);
        words[offset/4]=word;
    }
}
static void dpram_read(uint8_t *destination,const volatile void *source,unsigned length) {
    const volatile uint32_t *words=source;
    for(unsigned offset=0;offset<length;offset+=4) {
        uint32_t word=words[offset/4];
        for(unsigned b=0;b<4 && offset+b<length;++b)destination[offset+b]=word>>(b*8);
    }
}
static void arm_buffer(volatile uint32_t *reg, uint32_t value) {
    *reg=value & ~USB_BUF_CTRL_AVAIL;
    // DPRAM control/data must settle before the USB clock domain sees AVAIL.
    busy_wait_at_least_cycles(12);
    *reg=value | USB_BUF_CTRL_AVAIL;
}
static void ep0_in(const uint8_t *data, uint16_t length, uint8_t pid) {
    if (length) dpram_write(usb_dpram->ep0_buf_a,data,length);
    arm_buffer(&usb_dpram->ep_buf_ctrl[0].in,length|USB_BUF_CTRL_FULL|USB_BUF_CTRL_LAST|(pid?USB_BUF_CTRL_DATA1_PID:0));
}
static void ep0_out(uint16_t length, uint8_t pid) {
    arm_buffer(&usb_dpram->ep_buf_ctrl[0].out,length|USB_BUF_CTRL_LAST|(pid?USB_BUF_CTRL_DATA1_PID:0));
}
static void stall_control(void) {
    stat.control_stalls++;
    control=C_STALL;
    usb_hw->ep_stall_arm=USB_EP_STALL_ARM_EP0_IN_BITS|USB_EP_STALL_ARM_EP0_OUT_BITS;
    usb_dpram->ep_buf_ctrl[0].in=USB_BUF_CTRL_STALL;
    usb_dpram->ep_buf_ctrl[0].out=USB_BUF_CTRL_STALL;
}
static bool abort_mask(uint32_t mask) {
    HW_SET->abort=mask;
    uint32_t start=time_us_32();
    while ((usb_hw->abort_done&mask)!=mask) {
        if ((uint32_t)(time_us_32()-start)>500) {
            HW_CLR->abort=mask;
            stat.abort_timeouts++;
            return false;
        }
    }
    return true;
}
static void release_abort(uint32_t mask) {
    HW_CLR->abort_done=mask;
    HW_CLR->abort=mask;
}
static void notify_cancel(void) {
    if (in_busy) {
        in_busy=false;
        if (opt.complete) opt.complete(in_cookie,time_us_32(),false,opt.context);
    }
}
static void notify_secondary_cancel(void) {
    if(secondary_busy) {
        secondary_busy=false;
        if(opt.secondary_complete)opt.secondary_complete(secondary_cookie,time_us_32(),false,opt.context);
    }
}
static void reset_input(void) {
    notify_cancel();
    notify_secondary_cancel();
    usb_dpram->ep_ctrl[0].in=0;
    usb_dpram->ep_buf_ctrl[1].in=0;
    usb_dpram->ep_ctrl[0].out=0;
    usb_dpram->ep_buf_ctrl[1].out=0;
    usb_dpram->ep_ctrl[1].in=usb_dpram->ep_ctrl[1].out=0;
    usb_dpram->ep_buf_ctrl[2].in=usb_dpram->ep_buf_ctrl[2].out=0;
    aux_busy=false; aux_in_pid=aux_out_pid=0;
    HW_CLR->buf_status=EP1_BIT|OUT1_BIT|IN2_BIT|OUT2_BIT;
    ep1_pid=out1_pid=0; halted=configuration_halted=false;
    secondary_pid=0;secondary_halted=false;
}
static void arm_output(void) {
    arm_buffer(&usb_dpram->ep_buf_ctrl[1].out,64|USB_BUF_CTRL_LAST|(out1_pid?USB_BUF_CTRL_DATA1_PID:0));
    out1_pid^=1;
}
static void arm_aux_output(void) {
    arm_buffer(&usb_dpram->ep_buf_ctrl[2].out,64|USB_BUF_CTRL_LAST|(aux_out_pid?USB_BUF_CTRL_DATA1_PID:0));
    aux_out_pid^=1;
}
static void arm_aux_input(void) {
    unsigned n=aux_length-aux_offset; if(n>64)n=64;
    if(n) { dpram_write(usb_dpram->epx_data+128,aux_data+aux_offset,n); aux_offset+=n; }
    else aux_zlp=false;
    arm_buffer(&usb_dpram->ep_buf_ctrl[2].in,n|USB_BUF_CTRL_FULL|USB_BUF_CTRL_LAST|(aux_in_pid?USB_BUF_CTRL_DATA1_PID:0));
    aux_in_pid^=1;
}
static void enable_input(void) {
    reset_input();
    uint32_t offset=(uint32_t)(uintptr_t)usb_dpram->epx_data-(uint32_t)(uintptr_t)usb_dpram;
    usb_dpram->ep_ctrl[0].in=EP_CTRL_ENABLE_BITS|EP_CTRL_INTERRUPT_PER_BUFFER|
        EP_CTRL_INTERRUPT_ON_NAK|(3u<<EP_CTRL_BUFFER_TYPE_LSB)|offset;
    if(opt.output_endpoint) {
        usb_dpram->ep_ctrl[0].out=EP_CTRL_ENABLE_BITS|EP_CTRL_INTERRUPT_PER_BUFFER|
            (3u<<EP_CTRL_BUFFER_TYPE_LSB)|(offset+64);
        arm_output();
    }
    if(opt.auxiliary_bulk) {
        usb_dpram->ep_ctrl[1].in=EP_CTRL_ENABLE_BITS|EP_CTRL_INTERRUPT_PER_BUFFER|(2u<<EP_CTRL_BUFFER_TYPE_LSB)|(offset+128);
        usb_dpram->ep_ctrl[1].out=EP_CTRL_ENABLE_BITS|EP_CTRL_INTERRUPT_PER_BUFFER|(2u<<EP_CTRL_BUFFER_TYPE_LSB)|(offset+192);
        arm_aux_output();
    }
    if(opt.configuration_report) {
        usb_dpram->ep_ctrl[1].in=EP_CTRL_ENABLE_BITS|EP_CTRL_INTERRUPT_PER_BUFFER|
            (3u<<EP_CTRL_BUFFER_TYPE_LSB)|(offset+128);
        // No Input reports on this interface: the endpoint always responds NAK.
    }
    if(opt.secondary_report) {
        usb_dpram->ep_ctrl[1].in=EP_CTRL_ENABLE_BITS|EP_CTRL_INTERRUPT_PER_BUFFER|
            (3u<<EP_CTRL_BUFFER_TYPE_LSB)|(offset+128);
    }
}
static void control_next_in(void) {
    if(opt.observe_event)opt.observe_event(3,((uint32_t)control_length<<16)|control_offset,opt.context);
    uint16_t left=control_length-control_offset;
    if (left) {
        uint16_t n=left>64?64:left;
        ep0_in(control_data+control_offset,n,ep0_pid);
        ep0_pid^=1; control_offset+=n;
    } else if (control_zlp) {
        control_zlp=false; ep0_in(NULL,0,ep0_pid); ep0_pid^=1;
    } else {
        control=C_STATUS_OUT; ep0_out(0,1);
    }
}
static void control_send(const void *data, uint16_t available, uint16_t requested) {
    control_length=available<requested?available:requested;
    if (control_length>sizeof(control_data)) { stall_control(); return; }
    if (data!=control_data && control_length) memcpy(control_data,data,control_length);
    control_offset=0; ep0_pid=1; control=C_IN;
    control_zlp=available<requested && (control_length%64)==0;
    control_next_in();
}
static void control_status(void) { control=C_STATUS_IN; ep0_in(NULL,0,1); }
static void descriptor_request(const setup_packet *s) {
    uint8_t type=s->value>>8,index=s->value;
    if (type==1 && index==0 && s->kind==0x80) control_send(opt.device,opt.device_length,s->length);
    else if (type==2 && index==0 && s->kind==0x80) control_send(opt.configuration,opt.configuration_length,s->length);
    else if (type==0x22 && index==0 && s->kind==0x81 && s->index==0) control_send(opt.report,opt.report_length,s->length);
    else if (type==0x22 && index==0 && s->kind==0x81 && s->index==1 && opt.configuration_report)
        control_send(opt.configuration_report,opt.configuration_report_length,s->length);
    else if (type==0x22 && index==0 && s->kind==0x81 && s->index==1 && opt.secondary_report)
        control_send(opt.secondary_report,opt.secondary_report_length,s->length);
    else if (type==0x21 && index==0 && s->kind==0x81 && (s->index==0 || (s->index==1&&(opt.configuration_report||opt.secondary_report)))) {
        unsigned interface=0;
        for(unsigned offset=0;offset+2<=opt.configuration_length;) {
            unsigned n=opt.configuration[offset]; if(n<2||offset+n>opt.configuration_length)break;
            if(opt.configuration[offset+1]==4 && n>=9)interface=opt.configuration[offset+2];
            if(opt.configuration[offset+1]==0x21 && interface==s->index) { control_send(opt.configuration+offset,n,s->length); return; }
            offset+=n;
        }
        stall_control();
    } else if (type==3 && s->kind==0x80) {
        if (index==0) { const uint8_t lang[]={4,3,9,4}; control_send(lang,4,s->length); }
        else {
            const char *str=index==1?opt.manufacturer:index==2?opt.product:index==3?opt.serial:NULL;
            if (!str) { stall_control(); return; }
            size_t n=strlen(str); if(n>126)n=126;
            control_data[0]=2+2*n; control_data[1]=3;
            for(size_t i=0;i<n;++i) { control_data[2+2*i]=str[i]; control_data[3+2*i]=0; }
            control_send(control_data,2+2*n,s->length);
        }
    } else stall_control();
}
static void setup_received(void) {
    setup_packet s; dpram_read((uint8_t *)&s,usb_dpram->setup_packet,sizeof(s));
    if(opt.observe_setup)opt.observe_setup((const uint8_t *)&s,opt.context);
    stat.setups++;
    if (!abort_mask(3)) { stall_control(); return; }
    usb_dpram->ep_buf_ctrl[0].in=0;
    usb_dpram->ep_buf_ctrl[0].out=0;
    HW_CLR->buf_status=3;
    usb_hw->ep_stall_arm=0;
    release_abort(3);
    control=C_IDLE; ep0_pid=1; pending_address=pending_config=-1; feature_out=false;
    HW_CLR->sie_status=USB_SIE_STATUS_SETUP_REC_BITS;
    if((s.kind&0x80)&&opt.control_read) {
        int n=opt.control_read((const uint8_t *)&s,control_data,sizeof(control_data),opt.context);
        if(n>=0) { if(n>(int)sizeof(control_data))stall_control(); else control_send(control_data,n,s.length); return; }
    }
    if ((s.kind&0x60)==0) {
        if(s.kind==0 && (s.request==1||s.request==3) && s.value==1 && !s.index && !s.length) {
            remote_wakeup=s.request==3; control_status(); return;
        }
        if (s.request==6 && (s.kind&0x80)) { descriptor_request(&s); return; }
        if (s.kind==0 && s.request==5 && s.value<128 && !s.index && !s.length) {
            pending_address=s.value; control_status(); return;
        }
        if (s.kind==0 && s.request==9 && s.value<=1 && !s.index && !s.length) {
            pending_config=s.value; control_status(); return;
        }
        if (s.kind==0x80 && s.request==8 && !s.value && !s.index) {
            uint8_t value=stat.configured; control_send(&value,1,s.length); return;
        }
        if (s.request==0 && (s.kind==0x80 || (s.kind==0x81 && (s.index==0||(s.index==1&&(opt.auxiliary_bulk||opt.configuration_report||opt.secondary_report)))) ||
            (s.kind==0x82 && (s.index==0 || s.index==0x80 || s.index==0x81 || (s.index==1&&opt.output_endpoint) || ((s.index==2||s.index==0x82)&&opt.auxiliary_bulk) || (s.index==0x82&&(opt.configuration_report||opt.secondary_report)))))) {
            uint8_t value[2]={s.kind==0x80?((remote_wakeup?2:0)|((opt.configuration[7]&0x40)?1:0)):
                (s.kind==0x82 && ((s.index==0x81&&halted)||(s.index==0x82&&(configuration_halted||secondary_halted)))),0};
            control_send(value,2,s.length); return;
        }
        if (s.kind==0x81 && s.request==10 && (s.index==0||(s.index==1&&(opt.auxiliary_bulk||opt.configuration_report||opt.secondary_report)))) {
            uint8_t alt=0; control_send(&alt,1,s.length); return;
        }
        if (s.kind==1 && s.request==11 && !s.index && !s.value && !s.length) {
            if(opt.secondary_report) {
                // SET_INTERFACE only resets that interface's endpoint state.
                if(!abort_mask(EP1_BIT)) { stall_control();return; }
                notify_cancel();usb_dpram->ep_buf_ctrl[1].in=0;ep1_pid=0;halted=false;
                HW_CLR->buf_status=EP1_BIT;release_abort(EP1_BIT);control_status();return;
            }
            enable_input(); control_status(); return;
        }
        if(s.kind==1&&s.request==11&&s.index==1&&opt.secondary_report&&!s.value&&!s.length) {
            if(!abort_mask(IN2_BIT)) { stall_control();return; }
            notify_secondary_cancel();usb_dpram->ep_buf_ctrl[2].in=0;secondary_pid=0;secondary_halted=false;
            HW_CLR->buf_status=IN2_BIT;release_abort(IN2_BIT);control_status();return;
        }
        if(s.kind==1&&s.request==11&&s.index==1&&opt.auxiliary_bulk&&!s.value&&!s.length) { control_status(); return; }
        if(s.kind==1&&s.request==11&&s.index==1&&opt.configuration_report&&!s.value&&!s.length) {
            usb_dpram->ep_buf_ctrl[2].in=0;configuration_halted=false;control_status();return;
        }
        if(s.kind==2 && (s.request==1||s.request==3) && s.index==0x82 && opt.configuration_report && !s.value && !s.length) {
            configuration_halted=s.request==3;
            usb_dpram->ep_buf_ctrl[2].in=configuration_halted?USB_BUF_CTRL_STALL:0;
            control_status();return;
        }
        if(s.kind==2 && (s.request==1||s.request==3) && s.index==0x82 && opt.secondary_report && !s.value && !s.length) {
            if(!abort_mask(IN2_BIT)) { stall_control();return; }
            notify_secondary_cancel();secondary_halted=s.request==3;secondary_pid=0;
            usb_dpram->ep_buf_ctrl[2].in=secondary_halted?USB_BUF_CTRL_STALL:0;
            HW_CLR->buf_status=IN2_BIT;release_abort(IN2_BIT);control_status();return;
        }
        if (s.kind==2 && (s.request==1 || s.request==3) && s.index==0x81 && !s.value && !s.length) {
            if (!abort_mask(EP1_BIT)) { stall_control(); return; }
            notify_cancel();
            usb_dpram->ep_buf_ctrl[1].in=s.request==3?USB_BUF_CTRL_STALL:0;
            halted=s.request==3; ep1_pid=0;
            HW_CLR->buf_status=EP1_BIT; release_abort(EP1_BIT);
            control_status(); return;
        }
    } else if ((s.kind&0x7f)==0x21 && (s.index==0 || (s.index==1&&(opt.configuration_report||opt.secondary_report)))) {
        uint8_t report_type=s.value>>8,report_id=s.value;
        bool feature_interface=s.index==(opt.configuration_report?1:0);
        if(s.index==1&&opt.secondary_report) {
            // The boot mouse has no report IDs, output reports or Feature protocol.
            if(s.kind==0xa1&&s.request==1&&report_type==1&&!report_id) { control_send(secondary_last,opt.secondary_input_length,s.length);return; }
            if(s.kind==0x21&&s.request==10&&!report_id&&!s.length) { secondary_idle=s.value>>8;control_status();return; }
            if(s.kind==0xa1&&s.request==2&&!s.value) { control_send(&secondary_idle,1,s.length);return; }
            if(s.kind==0x21&&s.request==11&&s.value<=1&&!s.length) {
                if(opt.secondary_relative_mouse && !change_mouse_protocol(true,s.value)) { stall_control();return; }
                secondary_protocol=s.value;control_status();return;
            }
            if(s.kind==0xa1&&s.request==3&&!s.value) { uint8_t value=secondary_protocol;control_send(&value,1,s.length);return; }
            stall_control();return;
        }
        if (s.kind==0xa1 && s.request==1 && (report_id==0 || report_id==last_input[0])) {
            if (report_type==1 && s.index==0) { control_send(last_input,opt.input_length,s.length); return; }
            if (report_type==3 && feature_interface && opt.get_feature && report_id==0) {
                uint16_t n=opt.get_feature(control_data,64,opt.context);
                control_send(control_data,n,s.length); return;
            }
        }
        if (s.kind==0x21 && s.request==9 && ((report_type==2&&s.index==0)||(report_type==3&&feature_interface&&report_id==0)) && s.length<=64) {
            control=C_OUT; control_offset=0; control_expected=s.length; feature_out=report_type==3;
            if (s.length) ep0_out(s.length,1); else control_status();
            return;
        }
        if (s.kind==0x21 && s.request==10 && !s.length) { if(!s.index)idle_rate=s.value>>8;control_status();return; }
        if (s.kind==0xa1 && s.request==2 && !s.index) { control_send(&idle_rate,1,s.length); return; }
        if (s.kind==0x21 && s.request==11 && !s.index && s.value<=1 && !s.length) {
            if(opt.relative_mouse && !change_mouse_protocol(false,s.value)) { stall_control();return; }
            protocol=s.value;control_status();return;
        }
        if (s.kind==0xa1 && s.request==3 && !s.index) { uint8_t value=protocol;control_send(&value,1,s.length);return; }
    }
    stall_control();
}
static void completed_buffers(void) {
    uint32_t done=usb_hw->buf_status;
    if(opt.observe_event)opt.observe_event(2,done,opt.context);
    if(done&IN2_BIT) {
        HW_CLR->buf_status=IN2_BIT;
        if(opt.secondary_report) {
            if(secondary_busy) {
                secondary_busy=false;
                if(opt.secondary_complete)opt.secondary_complete(secondary_cookie,time_us_32(),true,opt.context);
            }
        } else if(opt.auxiliary_bulk) {
            if(aux_offset<aux_length||aux_zlp)arm_aux_input(); else aux_busy=false;
        }
    }
    if(done&OUT2_BIT) {
        uint16_t n=usb_dpram->ep_buf_ctrl[2].out&USB_BUF_CTRL_LEN_MASK;
        HW_CLR->buf_status=OUT2_BIT;
        if(n<=64&&opt.auxiliary_output) {
            uint8_t data[64]; dpram_read(data,usb_dpram->epx_data+192,n); opt.auxiliary_output(data,n,opt.context);
        }
        arm_aux_output();
    }
    if(done&OUT1_BIT) {
        uint16_t n=usb_dpram->ep_buf_ctrl[1].out&USB_BUF_CTRL_LEN_MASK;
        HW_CLR->buf_status=OUT1_BIT;
        if(n<=64 && opt.output) {
            uint8_t data[64]; dpram_read(data,usb_dpram->epx_data+64,n); opt.output(data,n,opt.context);
        }
        arm_output();
    }
    if (done&EP1_BIT) {
        uint32_t now=time_us_32(); HW_CLR->buf_status=EP1_BIT;
        if (in_busy) {
            in_busy=false; stat.acks++; stat.last_ack_us=now;
            if (opt.complete) opt.complete(in_cookie,now,true,opt.context);
        }
    }
    if (done&1) {
        HW_CLR->buf_status=1;
        if (control==C_IN) control_next_in();
        else if (control==C_STATUS_IN) {
            if (pending_address>=0) { usb_hw->dev_addr_ctrl=pending_address; pending_address=-1; }
            if (pending_config>=0) {
                stat.configured=pending_config; pending_config=-1;
                if (stat.configured) enable_input(); else reset_input();
            }
            control=C_IDLE;
        }
    }
    if (done&2) {
        uint16_t n=usb_dpram->ep_buf_ctrl[0].out&USB_BUF_CTRL_LEN_MASK;
        HW_CLR->buf_status=2;
        if (control==C_OUT) {
            if (n>control_expected-control_offset) { stall_control(); return; }
            dpram_read(control_data+control_offset,usb_dpram->ep0_buf_a,n);
            control_offset+=n;
            if (control_offset==control_expected) {
                if (feature_out && opt.set_feature) opt.set_feature(control_data,control_offset,opt.context);
                if (!feature_out && opt.output) opt.output(control_data,control_offset,opt.context);
                control_status();
            } else stall_control();
        } else if (control==C_STATUS_OUT) control=C_IDLE;
    }
}
static bool change_mouse_protocol(bool secondary,uint8_t value) {
    if((secondary?secondary_protocol:protocol)==value)return true;
    bool busy=secondary?secondary_busy:in_busy;
    uint32_t mask=secondary?IN2_BIT:EP1_BIT;
    if(busy) {
        // Retire or cancel the old-format packet before acknowledging the
        // host's protocol change. Never reinterpret an outstanding transfer.
        if(!abort_mask(mask))return false;
        if(usb_hw->buf_status&mask)completed_buffers();
        else if(secondary) {
            usb_dpram->ep_buf_ctrl[2].in=0;secondary_pid^=1;notify_secondary_cancel();
        } else {
            usb_dpram->ep_buf_ctrl[1].in=0;ep1_pid^=1;notify_cancel();
        }
        release_abort(mask);
    }
    return true;
}
static void __not_in_flash_func(usb_irq)(void) {
    uint32_t ints=usb_hw->ints;
    if(opt.observe_event)opt.observe_event(1,ints,opt.context);
    if (ints&USB_INTS_BUS_RESET_BITS) {
        HW_CLR->sie_status=USB_SIE_STATUS_BUS_RESET_BITS;
        stat.resets++; stat.configured=0; stat.suspended=0;
        remote_wakeup=false;
        idle_rate=secondary_idle=0;protocol=secondary_protocol=1;
        if(opt.secondary_relative_mouse)memset(secondary_last,0,sizeof(secondary_last));
        usb_hw->dev_addr_ctrl=0; control=C_IDLE; pending_address=pending_config=-1;
        usb_dpram->ep_buf_ctrl[0].in=usb_dpram->ep_buf_ctrl[0].out=0;
        reset_input(); HW_CLR->buf_status=0xffffffff; return;
    }
    if (ints&USB_INTS_BUFF_STATUS_BITS) completed_buffers();
    if (ints&USB_INTS_SETUP_REQ_BITS) setup_received();
    if (ints&USB_INTS_DEV_SOF_BITS) {
        (void)usb_hw->sof_rd; stat.sof++; stat.last_sof_us=time_us_32(); stat.suspended=0;
    }
    if (ints&USB_INTS_EP_STALL_NAK_BITS) {
        uint32_t pending=usb_hw->ep_nak_stall_status;
        if (pending&EP1_BIT) stat.naks++;
        HW_CLR->ep_nak_stall_status=pending;
    }
    if (ints&USB_INTS_DEV_SUSPEND_BITS) {
        stat.suspended=1; HW_CLR->sie_status=USB_SIE_STATUS_SUSPENDED_BITS;
    }
    if (ints&USB_INTS_DEV_RESUME_FROM_HOST_BITS) {
        stat.suspended=0; HW_CLR->sie_status=USB_SIE_STATUS_RESUME_BITS;
    }
    if (ints&ERROR_IRQS) {
        stat.bus_errors++;
        HW_CLR->sie_status=USB_SIE_STATUS_CRC_ERROR_BITS|USB_SIE_STATUS_BIT_STUFF_ERROR_BITS|
            USB_SIE_STATUS_RX_OVERFLOW_BITS|USB_SIE_STATUS_RX_TIMEOUT_BITS|USB_SIE_STATUS_DATA_SEQ_ERROR_BITS;
    }
}
void ud_init(const ud_options *options) {
    irq_set_enabled(USBCTRL_IRQ,false);
    in_busy=secondary_busy=aux_busy=false; control=C_IDLE; remote_wakeup=false; pending_address=pending_config=-1;
    halted=configuration_halted=secondary_halted=false;
    idle_rate=secondary_idle=0;protocol=secondary_protocol=1;
    opt=*options; memset(&stat,0,sizeof(stat)); memset(last_input,0,sizeof(last_input));
    memset(secondary_last,0,sizeof(secondary_last));
    if(opt.secondary_initial_input && opt.secondary_input_length<=64)memcpy(secondary_last,opt.secondary_initial_input,opt.secondary_input_length);
    if(opt.initial_input && opt.input_length<=64)memcpy(last_input,opt.initial_input,opt.input_length);
    reset_block(RESETS_RESET_USBCTRL_BITS); unreset_block_wait(RESETS_RESET_USBCTRL_BITS);
    memset(usb_dpram,0,sizeof(*usb_dpram));
    usb_hw->muxing=USB_USB_MUXING_TO_PHY_BITS|USB_USB_MUXING_SOFTCON_BITS;
    usb_hw->pwr=USB_USB_PWR_VBUS_DETECT_BITS|USB_USB_PWR_VBUS_DETECT_OVERRIDE_EN_BITS;
    usb_hw->main_ctrl=USB_MAIN_CTRL_CONTROLLER_EN_BITS;
    usb_hw->sie_ctrl=USB_SIE_CTRL_EP0_INT_1BUF_BITS;
    usb_hw->inte=USB_INTS_BUFF_STATUS_BITS|USB_INTS_BUS_RESET_BITS|USB_INTS_SETUP_REQ_BITS|
        USB_INTS_DEV_SOF_BITS|USB_INTS_EP_STALL_NAK_BITS|USB_INTS_DEV_SUSPEND_BITS|USB_INTS_DEV_RESUME_FROM_HOST_BITS|ERROR_IRQS;
    irq_set_exclusive_handler(USBCTRL_IRQ,usb_irq); irq_set_priority(USBCTRL_IRQ,0x40); irq_set_enabled(USBCTRL_IRQ,true);
    HW_SET->sie_ctrl=USB_SIE_CTRL_PULLUP_EN_BITS;
}
bool ud_ready(void) { return stat.configured && !stat.suspended && !halted; }
bool ud_busy(void) { return in_busy; }
bool ud_send(const uint8_t *data, uint16_t length, uint32_t cookie, uint32_t *armed_us) {
    uint32_t irq=save_and_disable_interrupts();
    if (!ud_ready() || in_busy || length>64 || length!=opt.input_length) { restore_interrupts(irq); return false; }
    memcpy(last_input,data,length); dpram_write(usb_dpram->epx_data,data,length);
    if(opt.relative_mouse && length>1)memset(last_input+1,0,length-1);
    in_cookie=cookie; in_busy=true;
    arm_buffer(&usb_dpram->ep_buf_ctrl[1].in,length|USB_BUF_CTRL_FULL|USB_BUF_CTRL_LAST|(ep1_pid?USB_BUF_CTRL_DATA1_PID:0));
    ep1_pid^=1;
    if (armed_us) *armed_us=time_us_32();
    restore_interrupts(irq); return true;
}
bool ud_cancel_repeat(void) {
    uint32_t irq=save_and_disable_interrupts();
    if (!in_busy || !abort_mask(EP1_BIT)) { restore_interrupts(irq); return false; }
    if (usb_hw->buf_status&EP1_BIT) {
        completed_buffers(); release_abort(EP1_BIT); restore_interrupts(irq); return false;
    }
    usb_dpram->ep_buf_ctrl[1].in=0;
    ep1_pid^=1; in_busy=false;
    release_abort(EP1_BIT); restore_interrupts(irq); return true;
}
bool ud_secondary_ready(void) { return opt.secondary_report && stat.configured && !stat.suspended && !secondary_halted; }
bool ud_secondary_busy(void) { return secondary_busy; }
bool ud_secondary_send(const uint8_t *data,uint16_t length,uint32_t cookie,uint32_t *armed_us) {
    uint32_t irq=save_and_disable_interrupts();
    if(!ud_secondary_ready()||secondary_busy||length>64||length!=opt.secondary_input_length) { restore_interrupts(irq);return false; }
    memcpy(secondary_last,data,length);dpram_write(usb_dpram->epx_data+128,data,length);
    if(opt.secondary_relative_mouse&&length>1)memset(secondary_last+1,0,length-1);
    secondary_cookie=cookie;secondary_busy=true;
    arm_buffer(&usb_dpram->ep_buf_ctrl[2].in,length|USB_BUF_CTRL_FULL|USB_BUF_CTRL_LAST|(secondary_pid?USB_BUF_CTRL_DATA1_PID:0));
    secondary_pid^=1;
    if(armed_us)*armed_us=time_us_32();
    restore_interrupts(irq);return true;
}
bool ud_secondary_cancel_repeat(void) {
    uint32_t irq=save_and_disable_interrupts();
    if(!secondary_busy||!abort_mask(IN2_BIT)) { restore_interrupts(irq);return false; }
    if(usb_hw->buf_status&IN2_BIT) {
        completed_buffers();release_abort(IN2_BIT);restore_interrupts(irq);return false;
    }
    usb_dpram->ep_buf_ctrl[2].in=0;secondary_pid^=1;secondary_busy=false;
    release_abort(IN2_BIT);restore_interrupts(irq);return true;
}
void ud_statistics_get(ud_statistics *out) {
    uint32_t irq=save_and_disable_interrupts(); *out=stat; restore_interrupts(irq);
}
uint8_t ud_mouse_protocol(bool secondary) { return secondary?secondary_protocol:protocol; }
bool ud_mouse_send(bool secondary,bool report_protocol,const uint8_t *data,uint16_t length,uint32_t cookie,uint32_t *armed_us) {
    uint32_t irq=save_and_disable_interrupts();
    bool sent=false;
    if(ud_mouse_protocol(secondary)==(report_protocol?1:0))
        sent=secondary?ud_secondary_send(data,length,cookie,armed_us):ud_send(data,length,cookie,armed_us);
    restore_interrupts(irq);return sent;
}
void ud_disconnect(void) {
    // Stop callbacks before callers replace descriptors or clear queued reports.
    // ud_init re-enables this IRQ when the new layout is ready.
    irq_set_enabled(USBCTRL_IRQ,false);
    HW_CLR->sie_ctrl=USB_SIE_CTRL_PULLUP_EN_BITS;
    stat.configured=0;stat.suspended=0;
}
bool ud_auxiliary_send(const uint8_t *data,uint16_t length) {
    uint32_t irq=save_and_disable_interrupts();
    if(!opt.auxiliary_bulk||!ud_ready()||aux_busy||length>sizeof(aux_data)) { restore_interrupts(irq); return false; }
    memcpy(aux_data,data,length); aux_length=length; aux_offset=0; aux_busy=true;
    aux_zlp=(length!=0&&length%64==0); arm_aux_input(); restore_interrupts(irq); return true;
}
