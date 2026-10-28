#include "status_led.h"
#include "controller_profile.h"
#include "status_led.pio.h"
#include "pico/stdlib.h"
#include "hardware/clocks.h"
#include "hardware/pio.h"
#include <string.h>

enum { BLUE, CYAN, YELLOW, GREEN, RED, PURPLE, PROFILE, OFF=255 };
enum { WAITING, INITIALIZING, RECEIVING, FAULT };
// Ratios soften yellow and purple. Default is the lowest nonzero level.
#define DEFAULT_LEVEL 1
static const uint8_t palette[6][3]={{0,0,8},{0,8,8},{8,6,0},{0,8,0},{8,0,0},{6,0,8}};
static int led_sm=-1;
static uint8_t input_state,base_color=OFF,wanted_color=OFF,shown_color=OFF;
static uint8_t wanted_level=DEFAULT_LEVEL,shown_level,consecutive_errors;
static bool probe_found,pulse_active,manual_override,blink_on;
static uint32_t identity,next_update,pulse_until,last_write,writes,last_on,last_off;
static uint32_t blink_period,next_toggle;
static uint32_t profile_rgb=1,shown_rgb;
static uint32_t profile_next,profile_pulses,profile_finished;
static uint8_t profile_number,profile_remaining;
static bool profile_active,profile_on;

static uint32_t channel(uint8_t value,uint8_t level) {
    uint32_t scaled=value*level/8;
    // Keep both components of yellow/purple visible even at level one.
    return value && !scaled?1:scaled;
}

static void pulse(uint8_t color,uint8_t level,bool override,uint32_t now) {
    wanted_color=color;wanted_level=level;manual_override=override;pulse_active=true;
    pulse_until=now+1000000;
}
static void flush(uint32_t now) {
    if(led_sm<0)return;
    uint32_t rgb=0;
    if(wanted_color==PROFILE) {
        // Profile values are LED channel bytes, without palette scaling.
        rgb=profile_rgb;
    } else if(wanted_color!=OFF) {
        const uint8_t *color=palette[wanted_color];
        rgb=(channel(color[0],wanted_level)<<16)|(channel(color[1],wanted_level)<<8)|
            channel(color[2],wanted_level);
    }
    uint8_t level=(wanted_color==OFF || wanted_color==PROFILE)?0:wanted_level;
    if(shown_color==wanted_color && shown_level==level && shown_rgb==rgb)return;
    // 24 bits take 30 us. A 1 ms gap guarantees the reset/latch interval.
    if((uint32_t)(now-last_write)<1000 || pio_sm_is_tx_fifo_full(pio0,(uint)led_sm))return;
    // PIO shifts the upper 24 bits MSB first. Apply board order once here,
    // identically for custom RGB, status colors, and diagnostic colors.
#if CONTROLLER_STATUS_LED_GRB
    uint32_t word=((rgb&0x00ff00u)<<16)|(rgb&0xff0000u)|((rgb&0xffu)<<8);
#else
    uint32_t word=rgb<<8;
#endif
    pio_sm_put(pio0,(uint)led_sm,word);
    shown_color=wanted_color;shown_level=level;shown_rgb=rgb;
    last_write=now;writes++;
    if(!rgb)last_off=now;else last_on=now;
}
void status_led_init(void) {
    if(!pio_can_add_program(pio0,&bridge_status_led_program))return;
    led_sm=pio_claim_unused_sm(pio0,false);
    if(led_sm<0)return;
    uint offset=pio_add_program(pio0,&bridge_status_led_program);
    pio_gpio_init(pio0,CONTROLLER_STATUS_LED_PIN);
    pio_sm_set_consecutive_pindirs(pio0,(uint)led_sm,CONTROLLER_STATUS_LED_PIN,1,true);
    pio_sm_config c=bridge_status_led_program_get_default_config(offset);
    sm_config_set_sideset_pins(&c,CONTROLLER_STATUS_LED_PIN);
    sm_config_set_out_shift(&c,false,true,24);
    sm_config_set_fifo_join(&c,PIO_FIFO_JOIN_TX);
    sm_config_set_clkdiv(&c,(float)clock_get_hz(clk_sys)/8000000.0f);
    pio_sm_init(pio0,(uint)led_sm,offset,&c);
    pio_sm_set_enabled(pio0,(uint)led_sm,true);
    // Explicit black clears a color retained by the LED across MCU resets.
    pio_sm_put(pio0,(uint)led_sm,0);last_write=time_us_32();writes=1;
}
void status_led_event(const uc_event *e) {
    if(e->type==UC_INPUT) { input_state=RECEIVING;consecutive_errors=0;return; }
    if(e->type!=UC_STATUS)return;
    switch(e->code) {
    case UC_PROBING: probe_found=false;break;
    case UC_DEVICE_FOUND:
        probe_found=true;
        // Retries of the same failed device retain the red error indication.
        if(input_state!=FAULT || identity!=e->sequence)input_state=INITIALIZING;
        identity=e->sequence;break;
    case UC_PROBE_FAILED: input_state=WAITING;identity=0;break;
    case UC_ENUM_FAILED: input_state=probe_found?FAULT:WAITING;break;
    case UC_REJECTED: case UC_REPORT_DESCRIPTOR_FAILED: input_state=FAULT;break;
    case UC_DISCONNECTED: input_state=WAITING;identity=0;consecutive_errors=0;break;
    case UC_INPUT_ERROR:
        if(consecutive_errors<3)consecutive_errors++;
        if(consecutive_errors>=3)input_state=FAULT;
        break;
    default:break;
    }
}
void status_led_task(uint32_t now,bool profile_ready,bool output_ready,uint32_t rgb) {
    if((int32_t)(now-next_update)<0)return;
    next_update=now+10000;
    profile_rgb=rgb&0xffffffu;
    bool waiting=input_state==WAITING || (input_state==RECEIVING && profile_ready && !output_ready);
    uint8_t color=input_state==FAULT?RED:waiting?BLUE:
        input_state==INITIALIZING?CYAN:!profile_ready?YELLOW:profile_rgb==1?BLUE:PROFILE;
    uint32_t period=waiting?2000000:
        (input_state==INITIALIZING || input_state==FAULT)?500000:0;
    if(color!=base_color || period!=blink_period) {
        base_color=color;blink_period=period;blink_on=true;next_toggle=now+period/2;
    } else if(period && (int32_t)(now-next_toggle)>=0) {
        blink_on=!blink_on;next_toggle=now+period/2;
    }
    if(pulse_active && (int32_t)(now-pulse_until)>=0)pulse_active=false;
    if(!pulse_active) {
        wanted_color=manual_override?OFF:(!period || blink_on)?base_color:OFF;
        wanted_level=DEFAULT_LEVEL;
    }
    if(profile_active) {
        if((int32_t)(now-profile_next)>=0) {
            if(profile_on) {
                profile_on=false;profile_remaining--;
                profile_next=now+(profile_remaining?200000:350000);
            } else if(profile_remaining) {
                profile_on=true;profile_pulses++;profile_next=now+150000;
            } else { profile_active=false;profile_finished++; }
        }
        if(profile_active) { wanted_color=profile_on?PURPLE:OFF;wanted_level=DEFAULT_LEVEL; }
    }
    flush(now);
}
void status_led_saved(bool success) {
    profile_active=false;pulse(success?PURPLE:RED,DEFAULT_LEVEL,false,time_us_32());
}
void status_led_profile(unsigned number) {
    if(!number || number>UP_PROFILE_COUNT)return;
    profile_number=profile_remaining=(uint8_t)number;profile_on=false;profile_active=true;
    manual_override=false;pulse_active=false;profile_next=time_us_32()+250000;
    wanted_color=OFF;wanted_level=DEFAULT_LEVEL;
}
bool status_led_profile_busy(void) { return profile_active; }
bool status_led_command(const uint8_t arg[56],uint8_t reply[56]) {
    if(arg[0]>3 || (arg[0]==1 && (arg[1]>=6 || !arg[2] || arg[2]>32)))return false;
    uint32_t now=time_us_32();
    if(arg[0]==1)pulse(arg[1],arg[2],true,now);
    if(arg[0]==2) { wanted_color=OFF;manual_override=true;pulse_active=false;pulse_until=now; }
    if(arg[0]==3) {
        manual_override=false;pulse_active=false;pulse_until=now;
        wanted_color=(!blink_period || blink_on)?base_color:OFF;wanted_level=DEFAULT_LEVEL;
    }
    flush(now);
    memcpy(reply,"LED1",4);
    reply[4]=base_color;reply[5]=shown_color;reply[6]=shown_level;reply[7]=manual_override || pulse_active;
    memcpy(reply+8,&pulse_until,4);memcpy(reply+12,&now,4);memcpy(reply+16,&writes,4);
    memcpy(reply+20,&last_on,4);memcpy(reply+24,&last_off,4);
    reply[28]=led_sm<0?255:(uint8_t)led_sm;reply[29]=CONTROLLER_STATUS_LED_PIN;
    reply[30]=(manual_override?1:0)|(pulse_active?2:0)|(blink_on?4:0);
    memcpy(reply+32,&blink_period,4);
    reply[36]=profile_number;reply[37]=profile_remaining;reply[38]=profile_on;reply[39]=profile_active;
    memcpy(reply+40,&profile_pulses,4);memcpy(reply+44,&profile_finished,4);
    // Actual transmitted R/G/B bytes (also available for default/status colors).
    reply[48]=(uint8_t)(shown_rgb>>16);reply[49]=(uint8_t)(shown_rgb>>8);reply[50]=(uint8_t)shown_rgb;
    return led_sm>=0;
}
