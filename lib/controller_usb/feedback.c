#include "controller_feedback.h"
#include "switch_rumble.h"
#include "transport.h"
#include "pico/stdlib.h"
#include <stdatomic.h>
#include <string.h>

#define FEEDBACK_COUNT 8u
typedef struct { uint32_t epoch;uint8_t data[8]; } feedback_frame;
static feedback_frame queue[FEEDBACK_COUNT];
static atomic_uint head,tail,request,published,acknowledged;
static uint32_t observed,started,last_send,flight_epoch,play_until;
static uint8_t descriptor[7],buffer[64],sequence,kind,failures,toggle,backend,sample_count;
static bool flight_active;
static bool attached,opened,busy,ack_wait,ack_seen,initialized,need_stop,failed;
static sr_state decoded;
enum { SEND_STOP=1,SEND_ENABLE,SEND_EFFECT,SEND_REFRESH };
static void publish(unsigned state) {
    atomic_store_explicit(&published,state|(attached?(256u|((unsigned)backend<<16)):0)|(observed&1?512u:0),memory_order_release);
    atomic_store_explicit(&acknowledged,observed,memory_order_release);
}
void ucf_select(bool enabled) {
    uint32_t next=atomic_load_explicit(&request,memory_order_relaxed)+2;
    atomic_store_explicit(&request,(next&~1u)|(enabled?1u:0),memory_order_release);
}
uint32_t ucf_epoch(void) { return atomic_load_explicit(&request,memory_order_acquire)&~1u; }
uint32_t ucf_status(void) { return atomic_load_explicit(&published,memory_order_acquire); }
bool ucf_quiesce(uint32_t timeout_us) {
    uint32_t wanted=atomic_load_explicit(&request,memory_order_acquire);
    if(!(wanted&1) && (ucf_status()&255)==UCF_OFF && atomic_load_explicit(&acknowledged,memory_order_acquire)==wanted)return true;
    ucf_select(false);wanted=atomic_load_explicit(&request,memory_order_acquire);
    uint32_t start=time_us_32();
    for(;;) {
        unsigned state=ucf_status()&255;
        if(atomic_load_explicit(&acknowledged,memory_order_acquire)==wanted && (state==UCF_OFF || state==UCF_FAULT))return true;
        if((uint32_t)(time_us_32()-start)>=timeout_us)return false;
        tight_loop_contents();
    }
}
bool ucf_submit_pair(const uint8_t first[8],const uint8_t data[8]) {
    uint32_t epoch=atomic_load_explicit(&request,memory_order_acquire);
    if(!(epoch&1))return true;
    uint32_t status=ucf_status();
    if(!(status&256) || (status&255)==UCF_FAULT)return true;
    unsigned write=atomic_load_explicit(&head,memory_order_relaxed),read=atomic_load_explicit(&tail,memory_order_acquire);
    unsigned count=first?2u:1u;
    if((read+FEEDBACK_COUNT-write-1)%FEEDBACK_COUNT<count)return false;
    if(first) { queue[write].epoch=epoch;memcpy(queue[write].data,first,8);write=(write+1)%FEEDBACK_COUNT; }
    unsigned next=(write+1)%FEEDBACK_COUNT;
    queue[write].epoch=epoch;memcpy(queue[write].data,data,8);
    atomic_store_explicit(&head,next,memory_order_release);return true;
}
bool ucf_submit(const uint8_t data[8]) { return ucf_submit_pair(NULL,data); }
static void discard(void) { atomic_store_explicit(&tail,atomic_load_explicit(&head,memory_order_acquire),memory_order_release); }
static void discard_stale(uint32_t epoch) {
    unsigned read=atomic_load_explicit(&tail,memory_order_relaxed),write=atomic_load_explicit(&head,memory_order_acquire);
    while(read!=write && queue[read].epoch!=epoch)read=(read+1)%FEEDBACK_COUNT;
    atomic_store_explicit(&tail,read,memory_order_release);
}
static void close_output(void) {
    if(opened) { toggle=ut_data_toggle(1,descriptor[2]);ut_idle_output(0);ut_close_endpoint(1,descriptor[2]);opened=false; }
}
static void attach(const uint8_t d[7],uint8_t next_sequence,uint8_t type,bool is_open) {
    memcpy(descriptor,d,7);attached=true;opened=is_open;backend=type;play_until=0;toggle=0;busy=ack_wait=ack_seen=initialized=failed=false;
    sequence=next_sequence;failures=0;observed=atomic_load_explicit(&request,memory_order_acquire);
    discard();sr_reset(&decoded);need_stop=(observed&1)!=0;
    if(need_stop && opened)ut_idle_output(descriptor[2]);else close_output();
    publish(need_stop?UCF_STOPPING:UCF_OFF);
}
void ucf_attach(const uint8_t d[7],uint8_t next_sequence) { attach(d,next_sequence,4,true); }
void ucf_attach_switch2(const uint8_t d[7]) { attach(d,0,5,false); }
void ucf_detach(void) {
    ut_idle_output(0);attached=opened=busy=ack_wait=initialized=need_stop=false;failed=false;
    observed=atomic_load_explicit(&request,memory_order_acquire);discard();sr_reset(&decoded);publish(UCF_OFF);
}
bool ucf_pending(void) {
    return observed!=atomic_load_explicit(&request,memory_order_acquire) || busy || ack_wait || need_stop || (attached && (observed&1) && !failed);
}
static bool open_output(void) {
    if(opened)return true;
    if(!ut_open(1,descriptor))return false;
    ut_set_data_toggle(1,descriptor[2],toggle);ut_idle_output(descriptor[2]);opened=true;return true;
}
static bool send(unsigned next_kind,const uint8_t hd[8]) {
    if(!open_output())return false;
    unsigned length;sr_state candidate=decoded;
    if(backend==5) {
        sr_samples samples={0};
        if(next_kind!=SEND_REFRESH)sr_decode_samples(&candidate,hd,&samples);
        sample_count=(uint8_t)sr_switch2(&candidate,&samples,sequence,buffer);length=64;
        flight_active=sr_active(&candidate);
        for(unsigned i=0;i<2;++i)for(unsigned j=0;j<samples.count[i];++j)
            for(unsigned b=0;b<2;++b)if(samples.motor[i][j].amplitude[b]>=-254)flight_active=true;
    } else {
        buffer[0]=next_kind==SEND_ENABLE?1:0x10;buffer[1]=sequence;memcpy(buffer+2,hd,8);
        buffer[10]=0x48;buffer[11]=1;length=next_kind==SEND_ENABLE?12:10;
    }
    if(!ut_submit(1,descriptor[2],buffer,(uint16_t)length))return false;
    if(backend==5)decoded=candidate;
    sequence=(uint8_t)((sequence+1)&15);started=time_us_32();flight_epoch=observed;kind=(uint8_t)next_kind;
    busy=true;ack_seen=false;return true;
}
static void fault(void) {
    failed=true;initialized=ack_wait=false;need_stop=false;discard();sr_reset(&decoded);close_output();publish(UCF_FAULT);
}
void ucf_input(const uint8_t *data,unsigned length) {
    if(((busy && kind==SEND_ENABLE) || ack_wait) && length>=15 && data[0]==0x21 && data[14]==0x48) {
        if(data[13]&0x80)ack_seen=true;else { failed=true;need_stop=true; }
    }
}
void ucf_service(void) {
    uint32_t next=atomic_load_explicit(&request,memory_order_acquire),now=time_us_32();
    if(next!=observed) {
        observed=next;discard_stale(next);sr_reset(&decoded);initialized=ack_wait=failed=false;failures=0;
        need_stop=attached;play_until=0;publish(attached?UCF_STOPPING:UCF_OFF);
    }
    if(!attached)return;
    if(busy) {
        ut_idle_output_service();
        ut_result result=ut_result_for(1,descriptor[2],NULL);
        if(result==UT_PENDING && (uint32_t)(now-started)<20000)return;
        if(result==UT_PENDING) { ut_abort(1,descriptor[2]);result=UT_TIMEOUT; }
        busy=false;
        if(result!=UT_OK) {
            if(++failures>=3) { fault();return; }
            failed=true;need_stop=true;ack_wait=initialized=false;
        } else {
            last_send=now;
            if(flight_epoch==observed) {
                if(kind==SEND_STOP) {
                    need_stop=false;sr_reset(&decoded);play_until=now;
                    if(backend==5)initialized=true;
                    if(failed) { fault();return; }
                    if(!(observed&1)) { close_output();publish(UCF_OFF);return; }
                } else if(kind==SEND_ENABLE)ack_wait=true;
                else {
                    if(backend==5)play_until=started+(sample_count==1?4000u:(uint32_t)sample_count*5000);
                    publish((backend==5?flight_active:sr_active(&decoded))?UCF_ACTIVE:UCF_READY);
                }
            }
        }
    }
    // HD2 expansion and USB packet preparation also consume time before the
    // asynchronous OUT transaction. Reserve the next input frame for both.
    if(backend==5 && ut_idle_budget_us()<250)return;
    if(need_stop) {
        publish(UCF_STOPPING);
        if(!send(SEND_STOP,sr_neutral) && ++failures>=3)fault();
        return;
    }
    if(failed || !(observed&1))return;
    if(ack_wait) {
        if(ack_seen) { ack_wait=false;initialized=true;publish(UCF_READY); }
        // A command reply is produced on the controller's report cadence, not
        // at the USB OUT ACK. Also allow slow user-selected input polling.
        // This is an asynchronous initialization deadline; input never waits.
        else if((uint32_t)(now-started)>=750000) { failed=true;need_stop=true; }
        return;
    }
    if(!initialized) {
        publish(UCF_WAITING);
        if(!send(SEND_ENABLE,sr_neutral) && ++failures>=3)fault();
        return;
    }
    // Preserve all HD2 samples before accepting the next effect. A lifecycle
    // stop above always bypasses this playback interval.
    if(backend==5 && (int32_t)(now-play_until)<0)return;
    if(backend==5 && !sr_active(&decoded) && (ucf_status()&255)==UCF_ACTIVE)publish(UCF_READY);
    unsigned read=atomic_load_explicit(&tail,memory_order_relaxed);
    if(read!=atomic_load_explicit(&head,memory_order_acquire)) {
        feedback_frame frame=queue[read];
        if(frame.epoch==observed) {
            if(!send(SEND_EFFECT,frame.data)) { failed=true;need_stop=true;return; }
            if(backend!=5)sr_decode(&decoded,frame.data);
        }
        atomic_store_explicit(&tail,(read+1)%FEEDBACK_COUNT,memory_order_release);
    } else if(sr_active(&decoded) && (uint32_t)(now-last_send)>=(backend==5?4000u:50000u)) {
        uint8_t current[8];sr_absolute(&decoded,current);
        if(!send(SEND_REFRESH,current)) { failed=true;need_stop=true; }
    }
}
