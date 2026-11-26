#include "controller_usb.h"
#include "transport.h"
#include "controller_feedback.h"
#include "input_protocol.h"
#include "ps4_usb_protocol.h"
#include "xbox_one.h"
#include "switch2.h"
#include "pico/stdlib.h"
#include <stdatomic.h>
#include <string.h>

typedef struct {
    uint8_t number, protocol;
    uint16_t report_length;
    uint8_t output_descriptor[7];
    uint8_t report[UC_REPORT_DESCRIPTOR_CAPACITY];
} hid_interface;
typedef struct {
    uint8_t descriptor[7], interface_number, native_interval, protocol;
    uint8_t buffer[64];
    unsigned errors,handle;
} input_endpoint;
static uc_host_options options;
static uc_statistics stats;
static uint8_t device_descriptor[18], configuration[UC_CONFIG_CAPACITY];
static uint16_t config_length;
static hid_interface interfaces[UC_MAX_INTERFACES];
static input_endpoint endpoints[UC_MAX_ENDPOINTS];
static unsigned interface_count, endpoint_count;
static bool ready,low_speed;
static uint32_t last_statistics;
static bool probe_missing;
// Only Xbox One keeps an OUT endpoint open after initialization. This state
// never changes the transport engine, endpoint intervals or legacy packet path.
static uc_xbox_one xbox_one;
static struct {
    uint32_t started_us;
    uint8_t endpoint,queue[16][13],head,count,buffer[13];
    bool active,busy;
} xbox_out;
typedef struct {
    uc_gip_metadata data;
    uint32_t last_data,last_ack;
    uint8_t command,flags,retries;
    bool used,reliable;
} xbox_rx_stream;
static xbox_rx_stream xbox_rx[8];
static uint32_t xbox_ack_tick;
static bool xbox_primary_metadata_complete;
static bool xbox_ack_retry(unsigned index,uint8_t ack[13],uint32_t now);
// Retain these across enumeration retries while the controller restarts.
// A timed-out Elite 2 startup can reopen the recovery allowance once. Only
// accepted input ends that failure episode; enumeration alone cannot rearm it.
static bool xbox_reset_attempted,xbox_resume_attempted;
static bool xbox_retry_pending,xbox_retry_used;
static bool xbox_bus_active;
static atomic_uint request_dump;
static atomic_uint request_interval; // bit 8 marks a pending request
static atomic_uint request_statistics;
static uint8_t interval_override;
enum { HOST_RUNNING, HOST_PAUSE_REQUESTED, HOST_FLASH_ACTIVE, HOST_FLASH_RELEASED };
static atomic_uint pause_state;

static void __attribute__((noinline)) __not_in_flash_func(pause_point)(void) {
    if(atomic_load_explicit(&pause_state,memory_order_acquire)!=HOST_PAUSE_REQUESTED)return;
    // Preparation requires XIP. Complete it before acknowledging.
    ut_flash_prepare();
    unsigned expected=HOST_PAUSE_REQUESTED;
    if(!atomic_compare_exchange_strong_explicit(&pause_state,&expected,HOST_FLASH_ACTIVE,
            memory_order_acq_rel,memory_order_acquire))return;
    // Only this loop and its SRAM-only callees may execute with XIP disabled.
    while(atomic_load_explicit(&pause_state,memory_order_acquire)==HOST_FLASH_ACTIVE)ut_flash_pump();
    // RELEASED means the writer has restored XIP and its native USB interrupts.
    ut_display_invalidate();
    ut_flash_resume();
    atomic_store_explicit(&pause_state,HOST_RUNNING,memory_order_release);
}
bool uc_host_pause(uint32_t timeout_ms) {
    if(!ucf_quiesce(timeout_ms*1000))return false;
    unsigned expected=HOST_RUNNING;
    if(!atomic_compare_exchange_strong_explicit(&pause_state,&expected,HOST_PAUSE_REQUESTED,
            memory_order_acq_rel,memory_order_acquire))return false;
    uint32_t start=time_us_32();
    while(atomic_load_explicit(&pause_state,memory_order_acquire)!=HOST_FLASH_ACTIVE) {
        if((uint32_t)(time_us_32()-start)>timeout_ms*1000) {
            expected=HOST_PAUSE_REQUESTED;
            // Cancellation and acknowledgement compete for the same state.
            // An acknowledgement that wins is safe, even at the deadline.
            return !atomic_compare_exchange_strong_explicit(&pause_state,&expected,HOST_RUNNING,
                       memory_order_acq_rel,memory_order_acquire) && expected==HOST_FLASH_ACTIVE;
        }
    }
    return true;
}
void uc_host_resume(void) {
    atomic_store_explicit(&pause_state,HOST_FLASH_RELEASED,memory_order_release);
    while(atomic_load_explicit(&pause_state,memory_order_acquire)!=HOST_RUNNING)tight_loop_contents();
}

static uint16_t le16(const uint8_t *b) { return b[0] | ((uint16_t)b[1] << 8); }
static void emit(uc_event *event) {
    event->time_us = time_us_32();
    options.event(event, options.context);
}
static void status(uc_status code, uint32_t detail) {
    uc_event event = {.type=UC_STATUS, .code=code, .sequence=detail};
    emit(&event);
}
static void descriptor_event(uint8_t type, uint8_t number, const uint8_t *bytes, uint16_t length) {
    for (uint16_t offset = 0; offset < length;) {
        uint16_t n = length - offset;
        if (n > UC_EVENT_BYTES) n = UC_EVENT_BYTES;
        uc_event event = {.type=UC_DESCRIPTOR, .code=type, .interface_number=number,
                          .offset=offset, .total=length, .length=n};
        memcpy(event.data, bytes+offset, n);
        emit(&event);
        offset += n;
        ut_pump();
    }
}
static void dump(void) {
    descriptor_event(1, 0, device_descriptor, sizeof(device_descriptor));
    descriptor_event(2, 0, configuration, config_length);
    for (unsigned i = 0; i < interface_count; ++i)
        descriptor_event(0x22, interfaces[i].number, interfaces[i].report, interfaces[i].report_length);
}
static ut_result control(uint8_t address, uint8_t kind, uint8_t request,
                         uint16_t value, uint16_t index, uint16_t length,
                         uint8_t *data, uint16_t *actual) {
    uint8_t setup[8] = {kind,request,value,value>>8,index,index>>8,length,length>>8};
    return ut_control(address,setup,data,actual);
}
static bool read_descriptor(uint8_t address, uint8_t kind, uint16_t value,
                            uint16_t index, uint8_t *data, uint16_t size) {
    uint16_t actual = 0;
    return control(address,kind,6,value,index,size,data,&actual) == UT_OK && actual == size;
}

static bool parse_config(void) {
    uc_input_configuration parsed;
    if(!uc_input_config_parse(configuration,config_length,&parsed))return false;
    if(low_speed) {
        // Low Speed supports a direct HID button device, with at most two
        // interrupt-IN pipes. No vendor startup or feedback protocol is used.
        if(parsed.protocols!=UC_PROTOCOL_HID || parsed.endpoint_count>2)return false;
        for(unsigned i=0;i<parsed.endpoint_count;++i)
            if(le16(parsed.endpoints[i].descriptor+4)>8)return false;
    } else if(!uc_input_config_identify(&parsed,le16(device_descriptor+8),le16(device_descriptor+10)))return false;
    interface_count=parsed.interface_count;endpoint_count=parsed.endpoint_count;
    for(unsigned i=0;i<interface_count;++i) {
        interfaces[i].number=parsed.interfaces[i].number;
        interfaces[i].protocol=parsed.interfaces[i].protocol;
        interfaces[i].report_length=parsed.interfaces[i].report_length;
        memcpy(interfaces[i].output_descriptor,parsed.interfaces[i].output_descriptor,7);
    }
    for(unsigned i=0;i<endpoint_count;++i) {
        input_endpoint *ep=&endpoints[i];
        memcpy(ep->descriptor,parsed.endpoints[i].descriptor,7);
        ep->interface_number=parsed.endpoints[i].interface_number;
        ep->protocol=parsed.endpoints[i].protocol;
        ep->native_interval=ep->descriptor[6];ep->errors=0;
    }
    status(UC_INPUT_PROTOCOL,parsed.protocols);
    return true;
}
static bool switch_reply(input_endpoint *ep,uint8_t report,uint8_t command,uint8_t *copy,uint16_t *size) {
    uint32_t start=time_us_32();
    while((uint32_t)(time_us_32()-start)<500000) {
        ut_pump();uint16_t actual=0;
        ut_result result=ut_result_for(1,ep->descriptor[2],&actual);
        if(result==UT_PENDING)continue;
        if(result!=UT_OK)return false;
        bool matched=report==0x81 ? actual>=2 && ep->buffer[0]==report && ep->buffer[1]==command :
            actual>=15 && ep->buffer[0]==report && (ep->buffer[13]&0x80) && ep->buffer[14]==command;
        if(report==0x21 && command==3 && uc_input_packet_valid(UC_PROTOCOL_SWITCH_PRO,ep->buffer,actual))matched=true;
        // Capture before rearming: the host controller owns ep->buffer afterwards.
        if(matched && copy) { memcpy(copy,ep->buffer,actual);*size=actual; }
        if(!ut_submit(1,ep->descriptor[2],ep->buffer,le16(ep->descriptor+4)))return false;
        if(matched)return true;
    }
    return false;
}
static void motion_calibration(uint8_t protocol,const uint8_t *data,unsigned size) {
    uc_event event={.type=UC_STATUS,.code=UC_NATIVE_MOTION_CALIBRATION,.sequence=protocol,.length=(uint16_t)size};
    memcpy(event.data,data,size);options.event(&event,options.context);
}
static bool switch_sensor_command(uint8_t out,input_endpoint *ep,uint8_t *sequence,uint8_t command,
                                  const uint8_t *data,unsigned size,uint8_t *reply,uint16_t *actual) {
    uint8_t request[32]={1,0,0,1,0x40,0x40,0,1,0x40,0x40};
    request[1]=(*sequence)++&15;request[10]=command;memcpy(request+11,data,size);
    return ut_submit(1,out,request,(uint16_t)(11+size)) && ut_transfer_wait(1,out)==UT_OK &&
        switch_reply(ep,0x21,command,reply,actual);
}
static void switch_sensors(uint8_t out,input_endpoint *ep,uint8_t *sequence) {
    uint8_t request[5],reply[64];uint16_t actual;
    // Factory calibration, followed by a valid user calibration if present.
    const uint32_t addresses[]={0x6020,0x8026};
    for(unsigned i=0;i<2;++i) {
        memcpy(request,&addresses[i],4);request[4]=i?26:24;
        if(!switch_sensor_command(out,ep,sequence,0x10,request,5,reply,&actual))continue;
        if(actual<20u+request[4] || memcmp(reply+15,request,5))continue;
        if(!i)motion_calibration(UC_PROTOCOL_SWITCH_PRO,reply+20,24);
        else if(reply[20]==0xb2 && reply[21]==0xa1)motion_calibration(UC_PROTOCOL_SWITCH_PRO,reply+22,24);
    }
    const uint8_t settings[]={3,0,1,1},enabled=1;
    switch_sensor_command(out,ep,sequence,0x41,settings,sizeof(settings),NULL,NULL);
    switch_sensor_command(out,ep,sequence,0x40,&enabled,1,NULL,NULL);
}
static bool switch_start(hid_interface *iface,input_endpoint *ep) {
    uint8_t out=iface->output_descriptor[2],step=0,sequence=1;
    ut_result result=UT_ERROR;
    if(!ut_open(1,iface->output_descriptor))return false;
    // Keep OUT open throughout initialization so USB DATA0/DATA1 toggles survive.
    const uint8_t commands[]={2,3,2,4};
    for(unsigned i=0;i<sizeof(commands);++i) {
        step=(uint8_t)(i+1);uint8_t request[]={0x80,commands[i]};
        result=ut_submit(1,out,request,sizeof(request))?ut_transfer_wait(1,out):UT_ERROR;
        if(result!=UT_OK)goto done;
        // 0x04 deliberately has no reply; it keeps the controller in USB mode.
        if(commands[i]!=4 && !switch_reply(ep,0x81,commands[i],NULL,NULL)) { result=UT_TIMEOUT;goto done; }
    }
    {
        step=5;
        // Report mode 0x30; both rumble channels carry their neutral encoding.
        uint8_t request[]={1,0,0,1,0x40,0x40,0,1,0x40,0x40,3,0x30};
        result=ut_submit(1,out,request,sizeof(request))?ut_transfer_wait(1,out):UT_ERROR;
        if(result==UT_OK && !switch_reply(ep,0x21,3,NULL,NULL))result=UT_TIMEOUT;
    }
done:
    if(result==UT_OK) { switch_sensors(out,ep,&sequence);ucf_attach(iface->output_descriptor,sequence); }
    else ut_close_endpoint(1,out);
    status(UC_INPUT_INIT,((uint32_t)step<<16)|(uint32_t)result);
    return result==UT_OK;
}
static void switch2_sensors(const uint8_t bulk[2][7]) {
    uint8_t request[16]={2,0x91,0,4,0,8,0,0,12,0x7e,0,0},reply[64],calibration[24]={0};
    // Partial reads fit one USB packet and never access pairing/private data.
    const uint32_t address[]={0x1310c,0x13044};
    bool complete=true;
    for(unsigned i=0;i<2;++i) {
        memcpy(request+12,&address[i],4);
        if(!ut_submit(1,bulk[0][2],request,sizeof(request)) || ut_transfer_wait(1,bulk[0][2])!=UT_OK ||
           !ut_submit(1,bulk[1][2],reply,sizeof(reply)) || ut_transfer_wait(1,bulk[1][2])!=UT_OK) { complete=false;break; }
        uint16_t actual=0;ut_result_for(1,bulk[1][2],&actual);
        if(actual<28 || reply[0]!=2 || reply[1]!=1 || reply[3]!=4 || reply[5]!=0x78 ||
           reply[8]!=12 || memcmp(reply+12,request+12,4)) { complete=false;continue; }
        memcpy(calibration+12*i,reply+16,12);
    }
    if(complete)motion_calibration(UC_PROTOCOL_SWITCH2,calibration,24);
}
static bool switch2_start(hid_interface *iface) {
    uint8_t bulk[2][7],request[28],reply[64];uint8_t step=0;
    if(!uc_switch2_config(configuration,config_length,bulk))return false;
    if(!ut_open(1,bulk[0]) || !ut_open(1,bulk[1]))return false;
    ut_result result=UT_ERROR;
    for(unsigned i=0;i<10;++i) {
        uint8_t length;const uint8_t *packet=uc_switch2_init(i,&length);step=(uint8_t)(i+1);
        memcpy(request,packet,length);
        result=ut_submit(1,bulk[0][2],request,length)?ut_transfer_wait(1,bulk[0][2]):UT_ERROR;
        if(result!=UT_OK)break;
        // Drain one command response, retaining DATA toggles across commands.
        result=ut_submit(1,bulk[1][2],reply,sizeof(reply))?ut_transfer_wait(1,bulk[1][2]):UT_ERROR;
        if(result!=UT_OK)break;
    }
    if(result==UT_OK)switch2_sensors(bulk);
    ut_close_endpoint(1,bulk[0][2]);ut_close_endpoint(1,bulk[1][2]);
    if(result==UT_OK)ucf_attach_switch2(iface->output_descriptor);
    status(UC_INPUT_INIT,((uint32_t)step<<16)|(uint32_t)result);
    return result==UT_OK;
}
static bool xbox_ack_queue(const uint8_t ack[13]) {
    if(xbox_out.busy && !memcmp(xbox_out.buffer,ack,13))return true;
    for(unsigned i=0;i<xbox_out.count;++i)
        if(!memcmp(xbox_out.queue[(xbox_out.head+i)%16],ack,13))return true;
    if(xbox_out.count==16)return false;
    memcpy(xbox_out.queue[(xbox_out.head+xbox_out.count++)%16],ack,13);
    return true;
}
static ut_result xbox_service(void) {
    if(xbox_out.busy) {
        ut_result result=ut_result_for(1,xbox_out.endpoint,NULL);
        if(result==UT_PENDING) {
            if((uint32_t)(time_us_32()-xbox_out.started_us)>=500000)return UT_TIMEOUT;
        } else {
            if(result!=UT_OK)return result;
            xbox_out.busy=false;
        }
    }
    uint32_t now=time_us_32();
    if((uint32_t)(now-xbox_ack_tick)>=8000) {
        xbox_ack_tick=now;
        for(unsigned i=0;i<8;++i) {
            uint8_t ack[13];
            if(xbox_ack_retry(i,ack,now) && !xbox_ack_queue(ack))return UT_ERROR;
        }
    }
    if(xbox_out.busy)return UT_OK;
    if(xbox_out.count) {
        memcpy(xbox_out.buffer,xbox_out.queue[xbox_out.head],13);
        if(!ut_submit(1,xbox_out.endpoint,xbox_out.buffer,sizeof(xbox_out.buffer)))return UT_ERROR;
        xbox_out.head=(uint8_t)((xbox_out.head+1)%16);xbox_out.count--;
        xbox_out.started_us=time_us_32();xbox_out.busy=true;
    }
    return UT_OK;
}
static ut_result xbox_send(const uint8_t *data,uint8_t length) {
    memcpy(xbox_out.buffer,data,length);
    return ut_submit(1,xbox_out.endpoint,xbox_out.buffer,length)?ut_transfer_wait(1,xbox_out.endpoint):UT_ERROR;
}
static xbox_rx_stream *xbox_stream(const uc_gip_message *m) {
    xbox_rx_stream *available=NULL;
    uint8_t flags=m->flags&0x27;
    uint32_t now=time_us_32();
    for(unsigned i=0;i<8;++i) {
        xbox_rx_stream *s=&xbox_rx[i];
        if(s->used && s->command==m->command && s->flags==flags) {
            if(s->data.sequence!=m->sequence) {
                if(!(m->flags&0x40))return NULL;
                memset(s,0,sizeof(*s));s->used=true;s->command=m->command;s->flags=flags;
            }
            return s;
        }
        if(!s->used || s->data.complete || (uint32_t)(now-s->last_data)>=1000000)available=s;
    }
    if(!available || !(m->flags&0x40))return NULL;
    memset(available,0,sizeof(*available));available->used=true;
    available->command=m->command;available->flags=flags;return available;
}
static bool xbox_metadata_complete(void) {
    return xbox_primary_metadata_complete;
}
static void xbox_stream_ack(const xbox_rx_stream *s,uint8_t ack[13]) {
    uc_gip_message m={.command=s->command,.flags=s->flags,.sequence=s->data.sequence};
    uc_gip_ack(&m,s->data.received,(uint16_t)(s->data.total-s->data.received),ack);
}
// Every ACME request is handled, during startup and during active input.
// Fragment tracking is shared by metadata, security and extended/vendor data.
static bool xbox_receive(const uc_gip_message *m,uint8_t ack[13],bool *send_ack) {
    *send_ack=(m->flags&0x10)!=0;
    if(!(m->flags&0x80)) {
        if(m->command==4 && (m->flags&0x27)==0x20 && m->size)xbox_primary_metadata_complete=true;
        if(*send_ack)uc_gip_ack(m,m->size,0,ack);
        return true;
    }
    xbox_rx_stream *s=xbox_stream(m);
    if(!s || !uc_gip_fragment_receive(&s->data,m))return false;
    if(s->command==4 && s->flags==0x20 && s->data.complete)xbox_primary_metadata_complete=true;
    uint32_t now=time_us_32();s->last_data=now;s->retries=0;
    if(*send_ack || ((m->flags&0x20) && (m->command==4 || m->command==6 || m->command==0x1e)))s->reliable=true;
    if(s->reliable && m->size && ((m->flags&0x40) || s->data.received==s->data.total ||
        (m->position>s->data.received && (uint32_t)(now-s->last_ack)>=100000))) *send_ack=true;
    if(*send_ack) { xbox_stream_ack(s,ack);s->last_ack=now; }
    return true;
}
static bool xbox_ack_retry(unsigned index,uint8_t ack[13],uint32_t now) {
    xbox_rx_stream *s=&xbox_rx[index];
    if(!s->used || !s->reliable || s->data.complete || s->retries>=8 ||
       (uint32_t)(now-s->last_data)>=1000000 || (uint32_t)(now-s->last_ack)<100000)return false;
    xbox_stream_ack(s,ack);s->last_ack=now;s->retries++;return true;
}
static ut_result xbox_handshake(input_endpoint *ep,uint8_t *sequence,bool *primary_seen) {
    // Hello -> Metadata -> final ACK -> completion -> Start. Metadata is
    // re-requested at 500 ms, at most four attempts (MS-GIPUSB 2.2.3).
    uint32_t start=time_us_32(),requested_at=0;unsigned requests=0;
    uint8_t request[4]={4,0x20,*sequence,0};bool hello=false;
    *primary_seen=false;xbox_bus_active=false;memset(xbox_rx,0,sizeof(xbox_rx));xbox_ack_tick=start;xbox_primary_metadata_complete=false;
    while((uint32_t)(time_us_32()-start)<4000000) {
        ut_pump();uint16_t actual=0;
        uint32_t now=time_us_32();
        if((uint32_t)(now-xbox_ack_tick)>=8000) {
            xbox_ack_tick=now;
            for(unsigned i=0;i<8;++i) {
                uint8_t ack[13];
                if(xbox_ack_retry(i,ack,now)) { ut_result sent=xbox_send(ack,sizeof(ack));if(sent!=UT_OK)return sent; }
            }
        }
        if(hello && requests && (uint32_t)(time_us_32()-requested_at)>=500000) {
            if(requests==4)return UT_TIMEOUT;
            ut_result sent=xbox_send(request,sizeof(request));if(sent!=UT_OK)return sent;
            requested_at=time_us_32();requests++;
        }
        ut_result result=ut_result_for(1,ep->descriptor[2],&actual);
        if(result==UT_PENDING) {
            if(!hello && (uint32_t)(time_us_32()-start)>=2000000)return UT_TIMEOUT;
            continue;
        }
        if(result!=UT_OK)return result;
        if(actual)xbox_bus_active=true;
        for(unsigned offset=0;offset<actual;) {
            uc_gip_message m;if(!uc_gip_parse(ep->buffer+offset,actual-offset,&m))return UT_ERROR;
            if(!hello && uc_xbox_one_hello(&xbox_one,ep->buffer+offset,m.consumed)) {
                hello=true;*primary_seen=true;(*sequence)++;
                result=xbox_send(request,sizeof(request));if(result!=UT_OK)return result;
                requested_at=time_us_32();requests=1;
            }
            uint8_t ack[13];bool send_ack;
            if(!xbox_receive(&m,ack,&send_ack))return UT_ERROR;
            if(send_ack) { result=xbox_send(ack,sizeof(ack));if(result!=UT_OK)return result; }
            offset+=m.consumed;
        }
        if(!ut_submit(1,ep->descriptor[2],ep->buffer,le16(ep->descriptor+4)))return UT_ERROR;
        if(hello && xbox_metadata_complete())return UT_OK;
    }
    return UT_TIMEOUT;
}
// This separate path leaves legacy HID/XUSB/Switch report handling unchanged.
static bool xbox_input(input_endpoint *ep,uint16_t actual) {
    bool received_input=false;
    for(unsigned offset=0;offset<actual;) {
        uc_gip_message m;if(!uc_gip_parse(ep->buffer+offset,actual-offset,&m))return false;
        uint8_t ack[13];bool send_ack;
        if(!xbox_receive(&m,ack,&send_ack) || (send_ack && !xbox_ack_queue(ack)))return false;
        // A complete report may have an extended length header. Normalize it
        // to the four-byte header used by the stable, synthetic HID schema.
        if(!(m.flags&0xcf) && m.size<=60) {
            uc_event event={.type=UC_INPUT,.interface_number=ep->interface_number,
                .endpoint=ep->descriptor[2],.length=(uint16_t)(m.size+4)};
            event.data[0]=m.command;event.data[1]=m.flags;event.data[2]=m.sequence;event.data[3]=(uint8_t)m.size;
            memcpy(event.data+4,m.payload,m.size);
            if(uc_xbox_one_input(&xbox_one,event.data,event.length)) {
                event.sequence=++stats.reports;event.time_us=ut_received_us(1,ep->descriptor[2]);
                options.event(&event,options.context);
                received_input=true;
            }
        }
        offset+=m.consumed;
    }
    // Status/ACK traffic and an idle, configured device do not replenish the
    // extra recovery. Validate the entire packet before accepting a recovery.
    if(received_input)xbox_retry_pending=xbox_retry_used=false;
    return true;
}
static bool xbox_elite2(void) { return xbox_one.vid==0x045e && xbox_one.pid==0x0b00; }
static ut_result xbox_initialize(input_endpoint *ep,uint8_t *sequence,uint8_t *step,bool *primary_seen) {
    *step=0;
    ut_result result=xbox_handshake(ep,sequence,primary_seen);
    if(result!=UT_OK)return result;
    for(unsigned i=1;i<UC_XBOX_ONE_INIT_STAGES;++i) {
        // Security and vendor messages each use their own sequence pool.
        uint8_t serial=i==3 || i==5?1:i>=6?(uint8_t)(i-5):*sequence;
        uint8_t n=uc_xbox_one_init(&xbox_one,i,serial,xbox_out.buffer);
        if(!n)continue;
        *step=(uint8_t)(i+1);if(i==1 || i==2 || i==4)(*sequence)++;
        result=ut_submit(1,xbox_out.endpoint,xbox_out.buffer,n)?ut_transfer_wait(1,xbox_out.endpoint):UT_ERROR;
        if(result!=UT_OK)return result;
    }
    return UT_OK;
}
static bool xbox_start(hid_interface *iface) {
    // GIP reserves sequence zero. Initialization sends at most eight packets.
    uint8_t sequence=1,step=0;
    ut_result result=UT_ERROR;
    xbox_out.endpoint=iface->output_descriptor[2];
    // Elite 2 may keep its GIP state across a USB reset. Synchronize both
    // sides' DATA toggles before a possible GIP Reset (USB CLEAR_FEATURE).
    if(xbox_elite2() && control(1,2,1,0,xbox_out.endpoint,0,NULL,NULL)!=UT_OK)return false;
    if(!ut_open(1,iface->output_descriptor))return false;
    // Some compatible pads require audio alternate setting zero explicitly.
    // Only send it if that GIP interface is actually present.
    for(unsigned offset=0;offset<config_length;offset+=configuration[offset]) {
        const uint8_t *d=configuration+offset;
        if(d[1]==4 && d[2]==1 && !d[3] && d[5]==0xff && d[6]==0x47 && d[7]==0xd0) {
            result=control(1,1,11,0,1,0,NULL,NULL);
            if(result!=UT_OK && result!=UT_STALL)goto done;
            break;
        }
    }
    // The failed enumeration has already passed through the host loop's
    // 750 ms retry delay. Never reopen these guards within the failing call.
    if(xbox_elite2() && xbox_retry_pending) {
        xbox_retry_pending=false;xbox_retry_used=true;
        xbox_reset_attempted=xbox_resume_attempted=false;
    }
    bool primary_seen=false;
    result=xbox_initialize(&endpoints[0],&sequence,&step,&primary_seen);
    if(result==UT_TIMEOUT && !primary_seen && xbox_elite2() && !xbox_bus_active && !xbox_resume_attempted) {
        // A long host outage (e.g. BOOTSEL) can leave the controller silent.
        // Resume USB, then enumerate again after the recovery interval.
        xbox_resume_attempted=true;ut_resume_fullspeed();goto done;
    }
    if(result==UT_TIMEOUT && (!primary_seen || xbox_retry_used) && xbox_elite2() && !xbox_reset_attempted) {
        xbox_reset_attempted=true;
        const uint8_t reset[]={5,0x20,sequence++,1,7};
        result=xbox_send(reset,sizeof(reset));
        if(result==UT_OK)result=xbox_initialize(&endpoints[0],&sequence,&step,&primary_seen);
        // The controller may detach while resetting. Normal enumeration then
        // resumes; neither that detach nor re-enumeration replenishes a retry.
    }
    // A resume above deliberately returns for re-enumeration and skips this
    // scheduling point. Complete the ordinary recovery before adding one more.
    if(result==UT_TIMEOUT && xbox_elite2() && !xbox_retry_used)xbox_retry_pending=true;
done:
    status(UC_INPUT_INIT,((uint32_t)step<<16)|(uint32_t)result);
    xbox_out.active=result==UT_OK;
    if(!xbox_out.active)ut_close_endpoint(1,xbox_out.endpoint);
    return xbox_out.active;
}
// Read only the per-unit calibration page, then apply its volatile gyro trim.
// Failed or unsupported motion setup leaves ordinary buttons/sticks available.
static void ds3_motion_initialize(uint8_t iface) {
    uint8_t identity[64],page[64],request[48]={0};
    uint16_t ni=0,np=0;uc_ds3_calibration cal={0};
    uc_event event={.type=UC_STATUS,.code=UC_MOTION_CALIBRATION,.interface_number=iface};
    bool valid=control(1,0xa1,1,0x0301,iface,sizeof(identity),identity,&ni)==UT_OK;
    if(valid) {
        request[4]=3;request[5]=1;request[6]=0xa0;
        valid=control(1,0x21,9,0x03ef,iface,sizeof(request),request,NULL)==UT_OK &&
            control(1,0xa1,1,0x03ef,iface,sizeof(page),page,&np)==UT_OK &&
            uc_ds3_calibration_parse(identity,ni,page,np,&cal);
    }
    if(valid) {
        unsigned size=uc_ds3_calibration_output(&cal,request);
        if(size)valid=control(1,0x21,9,0x0201,iface,(uint16_t)size,request,NULL)==UT_OK;
    }
    // Sony paths 1/2 have non-overlapping motor and gyro-trim fields.
    // Keep ordinary input available for other revisions without guessing their output format.
    if(valid && (cal.path==1 || cal.path==2))ucf_attach_ds3(iface,request);
    if(valid) {
        event.sequence=cal.path;event.length=sizeof(cal.values);
        memcpy(event.data,cal.values,sizeof(cal.values));
    }
    options.event(&event,options.context);
}

// Service already-open input pipes during startup feature transfers. This is
// never called by the steady-state input path, including ordinary profiles.
static bool ps4_startup_input(void) {
    ut_pump();
    for(unsigned i=0;i<endpoint_count;++i) {
        input_endpoint *ep=&endpoints[i];uint16_t actual=0;
        ut_result result=ut_result_for(1,ep->descriptor[2],&actual);
        if(result==UT_PENDING)continue;
        if(result==UT_OK && uc_input_packet_valid(ep->protocol,ep->buffer,actual)) {
            uc_event event={.type=UC_INPUT,.interface_number=ep->interface_number,
                .endpoint=ep->descriptor[2],.length=actual,.sequence=++stats.reports,
                .time_us=ut_received_us(1,ep->descriptor[2])};
            memcpy(event.data,ep->buffer,actual);options.event(&event,options.context);
        }
        if(result!=UT_OK || !ut_submit(1,ep->descriptor[2],ep->buffer,le16(ep->descriptor+4))) {
            stats.transfer_errors++;
            if(result==UT_STALL)stats.stalls++;
            ready=false;ut_close_device(1);status(UC_DISCONNECTED,result);return false;
        }
    }
    return true;
}
// Parse only at enumeration. Keep the temporary layout off the polling frame.
static bool __attribute__((noinline)) ps4_session_format(const hid_interface *iface) {
    uc_hid_layout layout;
    return uc_hid_parse(iface->report,iface->report_length,&layout) && layout.ps4_session;
}
// The protocol engine owns wire framing and validation; the host owns USB and
// input service. No session state or work is retained in the normal input loop.
static void __attribute__((noinline)) ps4_start(uint8_t iface) {
    uint8_t challenge[P4_CHALLENGE_BYTES];
    for(unsigned i=0;i<sizeof(challenge);++i)challenge[i]=(uint8_t)i;
    p4_session session;p4_transfer request;
    p4_begin(&session,1,challenge,time_us_32());
    ut_result last=UT_OK;
    while(p4_running(&session)) {
        pause_point();
        if(!ps4_startup_input())return;
        if(!p4_next(&session,time_us_32(),&request)) {
            if(p4_running(&session))ut_delay_ms(1);
            continue;
        }
        uint16_t actual=0;
        last=control(1,request.write?0x21:0xa1,request.write?9:1,
                     (uint16_t)(0x0300u|request.report),iface,request.length,request.data,&actual);
        p4_accept(&session,time_us_32(),last==UT_OK,request.data,actual);
    }
    if(session.error==P4_ERROR_EXPIRED)last=UT_TIMEOUT;
    else if(session.error!=P4_ERROR_NONE && last==UT_OK)last=UT_ERROR;
    status(UC_INPUT_INIT,0x40000u|((uint32_t)session.error<<8)|(uint32_t)last);
}

static bool enumerate(void) {
    ready = false;probe_missing=false;
    uint8_t ps4_interfaces[UC_MAX_INTERFACES];unsigned ps4_count=0;
    memset(&xbox_out,0,sizeof(xbox_out));memset(xbox_rx,0,sizeof(xbox_rx));
    status(UC_PROBING, ++stats.attempts);
    if(!ut_reset()) { probe_missing=true;status(UC_PROBE_FAILED,0);return false; }
    low_speed=ut_low_speed();status(UC_INPUT_SPEED,low_speed?1u:2u);
    ut_ep0(0,8);
    if (!read_descriptor(0,0x80,0x0100,0,device_descriptor,8)) {
        probe_missing=true;status(UC_PROBE_FAILED,0); return false;
    }
    uint8_t packet = device_descriptor[7];
    if (device_descriptor[0] != 18 || device_descriptor[1] != 1 ||
        (packet != 8 && packet != 16 && packet != 32 && packet != 64)) return false;
    ut_ep0(0,packet);
    if (!read_descriptor(0,0x80,0x0100,0,device_descriptor,18)) return false;
    uint16_t vid = le16(device_descriptor+8), pid = le16(device_descriptor+10);
    status(UC_DEVICE_FOUND, ((uint32_t)vid << 16) | pid);
    // Preserve identification even when the input protocol is unsupported.
    descriptor_event(1,0,device_descriptor,sizeof(device_descriptor));
    if (options.accept && !options.accept(vid,pid,options.context)) {
        status(UC_REJECTED,((uint32_t)vid << 16) | pid); return false;
    }
    if (control(0,0,5,1,0,0,NULL,NULL) != UT_OK) return false;
    ut_delay_ms(2);
    ut_close_device(0);
    ut_ep0(1,packet);
    if (!read_descriptor(1,0x80,0x0200,0,configuration,9)) return false;
    config_length = le16(configuration+2);
    if (configuration[0] != 9 || configuration[1] != 2 || config_length < 9 || config_length > UC_CONFIG_CAPACITY) return false;
    if (!read_descriptor(1,0x80,0x0200,0,configuration,config_length)) return false;
    descriptor_event(2,0,configuration,config_length);
    if (!parse_config()) return false;
    if (control(1,0,9,configuration[5],0,0,NULL,NULL) != UT_OK) return false;
    ut_delay_ms(10);
    for (unsigned i = 0; i < interface_count; ++i) {
        hid_interface *iface = &interfaces[i];
        if(iface->protocol==UC_PROTOCOL_DUALSHOCK3) {
            // Each USB enumeration needs this input-enabling Feature read.
            // The controller may then wait for its PS button; keep polling it.
            uint8_t feature[17];uint16_t actual=0;
            ut_result result=control(1,0xa1,1,0x03f2,iface->number,sizeof(feature),feature,&actual);
            if(result==UT_OK && actual!=sizeof(feature))result=UT_ERROR;
            status(UC_INPUT_INIT,0x10000u|(uint32_t)result);
            if(result!=UT_OK)return false;
            ds3_motion_initialize(iface->number);
            const uint8_t *schema=uc_dualshock3_layout(&iface->report_length);
            memcpy(iface->report,schema,iface->report_length);continue;
        }
        if(iface->protocol==UC_PROTOCOL_XBOX_ONE) {
            uc_xbox_one_identify(&xbox_one,vid,pid,le16(device_descriptor+12));
            iface->report_length=uc_xbox_one_layout(&xbox_one,iface->report);
            continue;
        }
        if(iface->protocol==UC_PROTOCOL_SWITCH2) {
            const uint8_t *schema=uc_switch2_layout(&iface->report_length);
            memcpy(iface->report,schema,iface->report_length);continue;
        }
        if(iface->protocol==UC_PROTOCOL_SWITCH_PRO) {
            const uint8_t *schema=uc_switch_pro_layout(&iface->report_length);
            memcpy(iface->report,schema,iface->report_length);
            continue;
        }
        if(iface->protocol==UC_PROTOCOL_XBOX360) {
            // Input-only personalities cannot receive wired Xbox startup.
            // Avoid the optional vendor identification request as well: it can
            // select Xbox mode on a multi-mode device before HID discovery.
            if(!iface->output_descriptor[0]) {
                status(UC_INPUT_INIT,0x40000u|UT_PENDING);
                const uint8_t *schema=uc_xbox360_layout(&iface->report_length);
                memcpy(iface->report,schema,iface->report_length);continue;
            }
            // Standard Xbox 360 vendor/interface read used during input start.
            // Some compatible pads use it to finish automatic mode detection.
            // A stall is optional on other pads; input itself remains authoritative.
            uint8_t identity[20];uint16_t actual;
            ut_result identification=control(1,0xc1,1,0x0100,iface->number,sizeof(identity),identity,&actual);
            // Complete the wired startup: LED, controller start, LED, steady LED.
            // A single blinking-LED packet leaves 8BitDo Arcade Stick waiting.
            // One open pipe preserves DATA0/DATA1 across the four OUT packets.
            static const uint8_t startup[][3]={{1,3,2},{2,8,3},{1,3,2},{1,3,6}};
            ut_result led=UT_PENDING;
            if(iface->output_descriptor[0] && ut_open(1,iface->output_descriptor)) {
                for(unsigned step=0;step<sizeof(startup)/sizeof(startup[0]);++step) {
                    uint8_t command[3];memcpy(command,startup[step],sizeof(command));
                    led=ut_submit(1,iface->output_descriptor[2],command,sizeof(command))?
                        ut_transfer_wait(1,iface->output_descriptor[2]):UT_ERROR;
                    if(led!=UT_OK)break;
                }
                ut_close_endpoint(1,iface->output_descriptor[2]);
            }
            status(UC_INPUT_INIT,(uint32_t)identification|((uint32_t)led<<8)|((uint32_t)actual<<16));
            const uint8_t *schema=uc_xbox360_layout(&iface->report_length);
            memcpy(iface->report,schema,iface->report_length);
            continue;
        }
        // The interface recipient (0x81), unavailable via Windows' hub query,
        // is essential here. Preserve the exact returned bytes.
        if (!read_descriptor(1,0x81,0x2200,iface->number,iface->report,iface->report_length)) {
            status(UC_REPORT_DESCRIPTOR_FAILED,iface->number); return false;
        }
        if(!low_speed && ps4_session_format(iface))ps4_interfaces[ps4_count++]=iface->number;
    }
    for (unsigned i = 0; i < interface_count; ++i)
        descriptor_event(0x22,interfaces[i].number,interfaces[i].report,interfaces[i].report_length);
    for (unsigned i = 0; i < endpoint_count; ++i) {
        input_endpoint *ep = &endpoints[i];
        uint8_t desc[7];
        memcpy(desc,ep->descriptor,7);
        if (interval_override) desc[6] = interval_override;
        if(ep->protocol==UC_PROTOCOL_XBOX_ONE && xbox_elite2() &&
           control(1,2,1,0,desc[2],0,NULL,NULL)!=UT_OK)return false;
        if (!ut_open(1,desc) || !ut_submit(1,desc[2],ep->buffer,le16(desc+4))) return false;
        int handle=ut_input_handle(desc[2]);if(handle<0)return false;ep->handle=(unsigned)handle;
    }
    for(unsigned i=0;i<interface_count;++i)if(interfaces[i].protocol==UC_PROTOCOL_SWITCH_PRO) {
        if(!switch_start(&interfaces[i],&endpoints[0]))return false;
    }
    for(unsigned i=0;i<interface_count;++i)if(interfaces[i].protocol==UC_PROTOCOL_XBOX_ONE) {
        if(!xbox_start(&interfaces[i]))return false;
    }
    for(unsigned i=0;i<interface_count;++i)if(interfaces[i].protocol==UC_PROTOCOL_SWITCH2) {
        if(!switch2_start(&interfaces[i]))return false;
    }
    if(low_speed && !ut_lowspeed_begin())return false;
    stats.generation++;
    stats.poll_interval_ms = interval_override ? interval_override : endpoints[0].native_interval;
    ready = true;
    ut_display_select(endpoints[0].descriptor[2]);
    status(UC_CONFIGURED,stats.poll_interval_ms);
    for(unsigned i=0;ready && i<ps4_count;++i)ps4_start(ps4_interfaces[i]);
    return ready;
}
void uc_request_dump(void) { atomic_store_explicit(&request_dump,1,memory_order_release); }
void uc_request_statistics(void) { atomic_store_explicit(&request_statistics,1,memory_order_release); }
void uc_request_poll_interval(uint8_t interval) {
    atomic_store_explicit(&request_interval,256u | interval,memory_order_release);
}
static void commands(void) {
    unsigned poll = atomic_exchange_explicit(&request_interval,0,memory_order_acquire);
    if (poll) {
        interval_override = (uint8_t)poll;
        ut_display_invalidate();
        if (ready) for (unsigned i = 0; i < endpoint_count; ++i)
            ut_set_interval(1,endpoints[i].descriptor[2],interval_override ? interval_override : endpoints[i].native_interval);
        stats.poll_interval_ms = interval_override ? interval_override : (ready ? endpoints[0].native_interval : 0);
        status(UC_POLL_CHANGED,stats.poll_interval_ms);
    }
    if (atomic_exchange_explicit(&request_dump,0,memory_order_acquire) && ready) dump();
}
static void __not_in_flash_func(publish_statistics)(void) {
        uint32_t now = time_us_32();
        if ((uint32_t)(now-last_statistics) >= 1000000 || atomic_exchange_explicit(&request_statistics,0,memory_order_acquire)) {
            last_statistics = now;
            ut_display_publish(ready,stats.poll_interval_ms);
            ut_metrics metrics;
            ut_metrics_get(&metrics);
            stats.frame_late = metrics.late_frames;
            stats.frame_us_max = metrics.max_frame_us;
            stats.in_tokens = metrics.in_tokens;
            stats.in_naks = metrics.in_naks;
            stats.in_duplicates = metrics.in_duplicates;
            stats.in_packet_errors = metrics.in_packet_errors;
            uc_event event = {.type=UC_STATISTICS,.length=sizeof(stats)};
            memcpy(event.data,&stats,sizeof(stats));
            emit(&event);
        }
}
static void __not_in_flash_func(run_full_speed)(void) {
    while(ready) {
        pause_point();uc_bootsel_host_service();commands();
            ut_pump();
            // Bounded asynchronous ACK service; never wait for controller OUT
            // during active input. Legacy devices never enter this function.
            if(xbox_out.active) {
                ut_result result=xbox_service();
                if(result!=UT_OK) {
                    stats.transfer_errors++;
                    if(result==UT_STALL)stats.stalls++;
                    ready=false;ut_close_device(1);status(UC_DISCONNECTED,result);
                }
            }
            for (unsigned i = 0; i < endpoint_count && ready; ++i) {
                input_endpoint *ep = &endpoints[i];
                uint16_t actual = 0;
                ut_result result = ut_result_for(1,ep->descriptor[2],&actual);
                if (result == UT_PENDING) continue;
                if (result == UT_OK) {
                    ep->errors = 0;
                    // Initialization/status replies are not controller input reports.
                    bool valid=uc_input_packet_valid(ep->protocol,ep->buffer,actual);
                    if(ep->protocol==UC_PROTOCOL_SWITCH_PRO)ucf_input(ep->buffer,actual);
                    if(ep->protocol==UC_PROTOCOL_XBOX_ONE && !xbox_input(ep,actual)) {
                        stats.transfer_errors++;ready=false;ut_close_device(1);
                        status(UC_DISCONNECTED,UT_ERROR);break;
                    }
                    if(valid) {
                    uc_event event = {.type=UC_INPUT,.interface_number=ep->interface_number,
                                      .endpoint=ep->descriptor[2],.length=actual,.sequence=++stats.reports};
                    if (actual > sizeof(event.data)) actual = event.length = sizeof(event.data);
                    memcpy(event.data,ep->buffer,actual);
                    event.time_us=ut_received_us(1,ep->descriptor[2]);
                    options.event(&event,options.context);
                    }
                } else {
                    stats.transfer_errors++;
                    if (result == UT_STALL) stats.stalls++;
                    status(UC_INPUT_ERROR,result);
                    if (++ep->errors >= 3 || result == UT_STALL || result == UT_DISCONNECTED) {
                        ready = false;
                        ut_close_device(1);
                        status(UC_DISCONNECTED,stats.transfer_errors);
                        break;
                    }
                }
                if (!ut_submit(1,ep->descriptor[2],ep->buffer,le16(ep->descriptor+4))) {
                    ready = false;
                    status(UC_DISCONNECTED,UT_ERROR);
                }
            }
        if(ready && ucf_pending())ucf_service();
        publish_statistics();
    }
}
static void __not_in_flash_func(run_low_speed)(void) {
    while(ready) {
        pause_point();uc_bootsel_host_service();commands();ut_lowspeed_pump();
        for(unsigned i=0;i<endpoint_count && ready;++i) {
            input_endpoint *ep=&endpoints[i];uint16_t actual=0;
            ut_result result=ut_input_result(ep->handle,&actual);
            if(result==UT_PENDING)continue;
            if(result==UT_OK) {
                ep->errors=0;
                if(actual && actual<=8) {
                    uc_event event={.type=UC_INPUT,.interface_number=ep->interface_number,
                        .endpoint=ep->descriptor[2],.length=actual,.sequence=++stats.reports};
                    memcpy(event.data,ep->buffer,actual);
                    event.time_us=ut_input_received_us(ep->handle);
                    options.event(&event,options.context);
                }
            } else {
                ++stats.transfer_errors;if(result==UT_STALL)++stats.stalls;
                status(UC_INPUT_ERROR,result);
                if(++ep->errors>=3 || result==UT_STALL || result==UT_DISCONNECTED) {
                    ready=false;ut_close_device(1);status(UC_DISCONNECTED,stats.transfer_errors);break;
                }
            }
            if(!ut_input_submit(ep->handle,ep->buffer,le16(ep->descriptor+4))) {
                ready=false;status(UC_DISCONNECTED,UT_ERROR);
            }
        }
        publish_statistics();
    }
}
void __not_in_flash_func(uc_host_run)(const uc_host_options *opts) {
    options=*opts;ut_init(options.dp_pin,options.sense_speed);
    status(UC_START,0);last_statistics=time_us_32();
    for(;;) {
        pause_point();uc_bootsel_host_service();commands();ucf_detach();
        if(!enumerate()) {
            status(UC_ENUM_FAILED,stats.attempts);
            if(probe_missing)ut_wait_for_device(750);else ut_delay_ms(750);
            publish_statistics();continue;
        }
        if(low_speed)run_low_speed();else run_full_speed();
    }
}
