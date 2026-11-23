#include "status_oled.h"
#include "oled_text.h"
#include "oled_output.h"
#include "controller_bridge.h"
#include "profile_runtime.h"
#include "pico/stdlib.h"
#include "hardware/i2c.h"
#include <string.h>
#include <limits.h>

enum { IDLE, INITIALIZING, ADDRESSING, DRAWING, TURNING_ON, TURNING_OFF };
static bool healthy,initialized,view_valid,display_on;
static uint8_t state,mode_seen,type;
static unsigned slot_seen;
static uint32_t generation_seen,started,period_seen,ids_seen,in_period_seen,bytes_seen;
static const uint8_t *sending;
static unsigned send_size,send_offset,last_remaining;
static char shown[4][9];
static uint8_t frame[257]={0x40},page[65]={0x40},dirty_rows,sending_row;
static const uint8_t initialize[]={0x00,0xae,0xd5,0x80,0xa8,31,0xd3,0,
    0x40,0x8d,0x14,0x20,0x00,0xa1,0xc8,0xda,0x12,0x81,127,
    0xd9,0xf1,0xdb,0x40,0xa4,0xa6};
static uint8_t address[]={0x00,0x21,32,95,0x22,0,0};
static const uint8_t turn_on[]={0x00,0xaf},turn_off[]={0x00,0xae};

static uint32_t gamepad_next;
static uint32_t next_sample,sample_at,sample_acks,sample_naks,sample_bus,sample_store,sample_errors,period_us;
static bool sample_valid,input_sample_valid,input_waiting,input_value_valid;
static uc_display_metrics input_sample;
static uint32_t input_store,input_started,in_period_us,bytes_per_second,reports_per_second,input_limit_per_second;
static oled_mouse_totals pointer_sample;
static uint32_t pointer_at,pointer_bus,pointer_store;
static int32_t pointer_x,pointer_y;
static bool pointer_sample_valid,pointer_value_valid;
static void sample_input(uint32_t now) {
    uc_display_metrics current;
    if(!uc_display_get(&current) || (input_waiting && (int32_t)(current.time_us-input_started)<0) || (int32_t)(now-current.time_us)>2500000) {
        input_sample_valid=input_value_valid=false;in_period_us=bytes_per_second=reports_per_second=input_limit_per_second=0;return;
    }
    input_waiting=false;
    // Capacity comes from the configured interval, never from observed polls.
    input_limit_per_second=current.connected && current.interval_ms && current.interval_ms<=255?
        (1000+current.interval_ms/2)/current.interval_ms:0;
    if(input_sample_valid && current.time_us==input_sample.time_us)return;
    uint32_t store=ub_store_write_generation(),elapsed=current.time_us-input_sample.time_us;
    uint32_t polls=current.polls-input_sample.polls,bytes=current.bytes-input_sample.bytes;
    bool consistent=current.connected && input_sample_valid && current.generation==input_sample.generation &&
        store==input_store && current.interval_ms==input_sample.interval_ms && elapsed>=800000 && elapsed<=2500000;
    in_period_us=consistent && polls && polls<=25000?(elapsed+polls/2)/polls:0;
    bytes_per_second=consistent?(uint32_t)(((uint64_t)bytes*1000000+elapsed/2)/elapsed):0;
    uint32_t reports=current.reports-input_sample.reports;
    reports_per_second=consistent?(uint32_t)(((uint64_t)reports*1000000+elapsed/2)/elapsed):0;
    input_value_valid=consistent;
    input_sample=current;input_store=store;input_sample_valid=current.connected!=0;
}
static void reset_sample(void) {
    sample_valid=input_sample_valid=input_value_valid=pointer_sample_valid=pointer_value_valid=false;
    period_us=in_period_us=bytes_per_second=reports_per_second=input_limit_per_second=0;pointer_x=pointer_y=0;
    next_sample=gamepad_next=0;input_started=time_us_32();input_waiting=true;
}
static int32_t pointer_rate(uint32_t delta,uint32_t elapsed) {
    // Interpret the modulo counter difference without overflowing signed math.
    int64_t signed_delta=(delta&0x80000000u)?(int64_t)delta-0x100000000ll:delta;
    int64_t value=signed_delta*1000000;
    value=value<0?-((-value+elapsed/2)/elapsed):(value+elapsed/2)/elapsed;
    return value>INT32_MAX?INT32_MAX:value<INT32_MIN?INT32_MIN:(int32_t)value;
}
static void sample_pointer(uint32_t now) {
    sample_input(now);
    oled_mouse_totals current;
    uint32_t bus=ud_bus_generation(),store=ub_store_write_generation(),elapsed=now-pointer_at;
    bool ready=status_oled_mouse_totals(&current);
    pointer_value_valid=ready && pointer_sample_valid && current.epoch==pointer_sample.epoch &&
        bus==pointer_bus && store==pointer_store && elapsed>=800000 && elapsed<=1500000;
    pointer_x=pointer_value_valid?pointer_rate(current.x-pointer_sample.x,elapsed):0;
    pointer_y=pointer_value_valid?pointer_rate(current.y-pointer_sample.y,elapsed):0;
    if(ready)pointer_sample=current;
    pointer_sample_valid=ready;pointer_at=now;pointer_bus=bus;pointer_store=store;
    next_sample=now+1000000;
}
static void sample_period(uint32_t now) {
    if(next_sample && (int32_t)(now-next_sample)<0)return;
    if(type==UDISPLAY_DETAIL_A)sample_input(now);
    if(!ud_ready()) { sample_valid=false;period_us=0;next_sample=now+1000000;return; }
    uint32_t bus=ud_bus_generation();ud_statistics st;ud_statistics_get(&st);
    uint32_t stamp=time_us_32(),store=ub_store_write_generation();
    uint32_t elapsed=stamp-sample_at,count=(st.acks-sample_acks)+(st.naks-sample_naks);
    // Count host IN requests, including NAKs, rather than only delivered reports.
    // Bus transitions and every Flash write invalidate the whole averaging window.
    bool consistent=bus==ud_bus_generation() && st.configured && !st.suspended;
    period_us=sample_valid && consistent && bus==sample_bus && store==sample_store && st.bus_errors==sample_errors &&
        elapsed>=800000 && elapsed<=1500000 && count && count<=10000 ? (elapsed+count/2)/count : 0;
    sample_valid=consistent;sample_at=stamp;sample_acks=st.acks;sample_naks=st.naks;
    sample_bus=bus;sample_store=store;sample_errors=st.bus_errors;next_sample=stamp+1000000;
}
static void stop(void) {
    healthy=false;state=IDLE;reset_sample();uc_display_enable(false);
    // Release even a clock-stretched transfer. No retry until reboot.
    i2c_deinit(i2c1);
    gpio_init(CONTROLLER_OLED_SDA_PIN);gpio_init(CONTROLLER_OLED_SCL_PIN);
    gpio_disable_pulls(CONTROLLER_OLED_SDA_PIN);gpio_disable_pulls(CONTROLLER_OLED_SCL_PIN);
}
static void begin(uint8_t phase,const uint8_t *bytes,unsigned size,uint32_t now) {
    i2c_hw_t *hw=i2c_get_hw(i2c1);
    (void)hw->clr_stop_det;(void)hw->clr_tx_abrt;
    sending=bytes;send_size=size;send_offset=0;last_remaining=size;started=now;state=phase;
}
static void begin_row(uint32_t now) {
    for(sending_row=0;sending_row<4 && !(dirty_rows&(1u<<sending_row));++sending_row) {}
    address[5]=address[6]=sending_row;
    memcpy(page+1,frame+1+64*sending_row,64);
    begin(ADDRESSING,address,sizeof(address),now);
}
bool status_oled_set_type(uint8_t display_type) {
    if(!healthy || !udisplay_valid(display_type))return false;
    uc_display_enable(display_type==UDISPLAY_DETAIL_A || display_type==UDISPLAY_DETAIL_C);
    if(type!=display_type) { type=display_type;view_valid=false;reset_sample(); }
    return type!=UDISPLAY_OFF || state!=IDLE || display_on;
}
bool status_oled_init(uint8_t display_type) {
    healthy=initialized=view_valid=display_on=false;state=IDLE;dirty_rows=0;reset_sample();uc_display_enable(false);
    type=udisplay_valid(display_type)?display_type:UDISPLAY_DEFAULT;
    i2c_init(i2c1,400000);
    gpio_set_function(CONTROLLER_OLED_SDA_PIN,GPIO_FUNC_I2C);
    gpio_set_function(CONTROLLER_OLED_SCL_PIN,GPIO_FUNC_I2C);
    gpio_pull_up(CONTROLLER_OLED_SDA_PIN);gpio_pull_up(CONTROLLER_OLED_SCL_PIN);
    // Probe with display-off; OFF never initializes graphics or starts sampling.
    for(uint8_t a=0x3c;a<=0x3d;++a) {
        if(i2c_write_timeout_us(i2c1,a,turn_off,sizeof(turn_off),false,1000)==sizeof(turn_off)) {
            healthy=true;uc_display_enable(type==UDISPLAY_DETAIL_A || type==UDISPLAY_DETAIL_C);return true;
        }
        i2c_deinit(i2c1);i2c_init(i2c1,400000);
    }
    stop();return false;
}
bool status_oled_task(uint32_t now,char mode,unsigned slot,uint32_t generation,const char *name) {
    if(!healthy)return false;
    i2c_hw_t *hw=i2c_get_hw(i2c1);
    if(state!=IDLE) {
        if(hw->tx_abrt_source) { stop();return false; }
        unsigned remaining=send_size-send_offset+hw->txflr;
        // A Flash save may pause dispatch while I2C makes progress independently.
        if(remaining<last_remaining)started=now;
        last_remaining=remaining;
        unsigned room=16-hw->txflr,previous_offset=send_offset;
        while(room-- && send_offset<send_size) {
            uint32_t word=sending[send_offset++];
            if(send_offset==send_size)word|=I2C_IC_DATA_CMD_STOP_BITS;
            hw->data_cmd=word;
        }
        if(send_offset!=previous_offset)started=now;
        if(send_offset<send_size || !(hw->raw_intr_stat&I2C_IC_RAW_INTR_STAT_STOP_DET_BITS)) {
            if((uint32_t)(now-started)>=20000)stop();
            return healthy;
        }
        (void)hw->clr_stop_det;
        uint8_t completed=state;state=IDLE;
        if(completed==INITIALIZING)initialized=true;
        if(completed==TURNING_ON)display_on=true;
        if(completed==TURNING_OFF)display_on=false;
        if(type!=UDISPLAY_OFF) {
            if(completed==ADDRESSING) { begin(DRAWING,page,sizeof(page),now);return true; }
            if(completed==DRAWING) {
                dirty_rows&=(uint8_t)~(1u<<sending_row);
                if(dirty_rows && view_valid) { begin_row(now);return true; }
            }
            if(completed==DRAWING && !display_on && view_valid) { begin(TURNING_ON,turn_on,sizeof(turn_on),now);return true; }
        }
    }
    if(type==UDISPLAY_OFF) {
        if(display_on) { begin(TURNING_OFF,turn_off,sizeof(turn_off),now);return true; }
        return false;
    }
    if(!initialized) { begin(INITIALIZING,initialize,sizeof(initialize),now);return true; }
    if(type==UDISPLAY_NAME_PERIOD_SLOT || type==UDISPLAY_DETAIL_A)sample_period(now);
    uint32_t ids=(type==UDISPLAY_DETAIL_A || type==UDISPLAY_DETAIL_B || type==UDISPLAY_DETAIL_C)?upr_device_ids():0;
    char rows[4][9];
    if(type==UDISPLAY_DETAIL_C) {
        // Read existing output totals at 1 Hz, independently of the input report
        // rate. Neither zero-motion output repeats nor faster host polling can
        // inflate the input count. No per-mouse-packet display hook is needed.
        if(view_valid && (ids_seen!=ids || generation_seen!=generation || slot_seen!=slot || mode_seen!=(uint8_t)mode))reset_sample();
        if(view_valid && next_sample && (int32_t)(now-next_sample)<0)return true;
        sample_pointer(now);
        oled_text_detail_c(rows,ids!=0,pointer_x,pointer_y,pointer_value_valid,input_limit_per_second,reports_per_second,input_value_valid);
    } else if(type==UDISPLAY_DETAIL_B) {
        // Sample the already-existing transmit cache at 20 Hz. No per-packet
        // hook, extra input queue, or display work in the USB interrupt path.
        if(view_valid && ids_seen==ids && generation_seen==generation && mode_seen==(uint8_t)mode &&
           gamepad_next && (int32_t)(now-gamepad_next)<0)return true;
        gamepad_next=now+50000;
        uint8_t report[14],gamepad[12];
        unsigned length=ids?ud_input_snapshot(report,sizeof(report)):0;
        bool valid=length && oled_output_decode(report,length,gamepad);
        oled_text_detail_b(rows,ids,valid?gamepad:NULL);
    } else {
        if(view_valid && mode_seen==(uint8_t)mode && slot_seen==slot && generation_seen==generation &&
           period_seen==period_us && ids_seen==ids && in_period_seen==in_period_us && bytes_seen==bytes_per_second)return true;
        if(type==UDISPLAY_DETAIL_A)oled_text_detail_a(rows,ids?in_period_us:0,period_us,ids?bytes_per_second:0,ids);
        else oled_text_format(rows,type,mode,slot,name,period_us);
    }
    dirty_rows=0;
    for(unsigned row=0;row<4;++row)if(!view_valid || memcmp(shown[row],rows[row],8))dirty_rows|=(uint8_t)(1u<<row);
    mode_seen=(uint8_t)mode;slot_seen=slot;generation_seen=generation;period_seen=period_us;
    ids_seen=ids;in_period_seen=in_period_us;bytes_seen=bytes_per_second;view_valid=true;
    if(!dirty_rows)return true;
    memcpy(shown,rows,sizeof(shown));oled_text_render(frame+1,rows);
    begin_row(now);return true;
}
