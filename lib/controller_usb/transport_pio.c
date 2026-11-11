#include "transport.h"
#include "pico/stdlib.h"
#include "hardware/clocks.h"
#include "hardware/sync.h"
#include "pio_usb.h"
#include "pio_usb_ll.h"
#include <string.h>

static ut_result completed[PIO_USB_EP_POOL_CNT];
static uint16_t lengths[PIO_USB_EP_POOL_CNT];
static uint32_t next_frame;
static ut_metrics metrics;
static uint32_t received_us[PIO_USB_EP_POOL_CNT];

// Called after RX/ACK finishes. The receive timestamp feeds normal input timing;
// bus-response counters also provide the device's operational status.
void __not_in_flash_func(uc_pio_input_complete)(uint8_t address, uint8_t endpoint,
                                       uint8_t pid, int length, uint8_t expected) {
    if (!(endpoint & 127) || address != 1) return;
    uint32_t now = time_us_32();
    metrics.in_tokens++;
    if (pid == USB_PID_NAK && length < 0) metrics.in_naks++;
    else if (length >= 0 && pid != expected) metrics.in_duplicates++;
    else if (length < 0) metrics.in_packet_errors++;
    if (length >= 0 && pid == expected) {
        for (unsigned i=0;i<PIO_USB_EP_POOL_CNT;++i) {
            endpoint_t *ep=PIO_USB_ENDPOINT(i);
            if (ep->size && ep->dev_addr==address && ep->ep_num==endpoint)
                received_us[i]=now;
        }
    }
}

static int find_ep(uint8_t address, uint8_t endpoint) {
    for (unsigned i = 0; i < PIO_USB_EP_POOL_CNT; ++i) {
        endpoint_t *ep = PIO_USB_ENDPOINT(i);
        if (ep->size && ep->dev_addr == address && ep->root_idx == 0 &&
            (ep->ep_num == endpoint || (!(endpoint & 127) && !(ep->ep_num & 127))))
            return (int)i;
    }
    return -1;
}

// Override the optional legacy upper stack: no TinyUSB host, no automatic
// descriptor interpretation, and no application callbacks in the packet path.
void __not_in_flash_func(pio_usb_host_irq_handler)(uint8_t root_index) {
    root_port_t *root = PIO_USB_ROOT_PORT(root_index);
    uint32_t success = root->ep_complete, error = root->ep_error, stall = root->ep_stalled;
    for (unsigned i = 0; i < PIO_USB_EP_POOL_CNT; ++i) {
        uint32_t mask = 1u << i;
        if ((success | error | stall) & mask) {
            lengths[i] = PIO_USB_ENDPOINT(i)->actual_len;
            completed[i] = (stall & mask) ? UT_STALL : (error & mask) ? UT_ERROR : UT_OK;
        }
    }
    root->ep_complete = root->ep_error = root->ep_stalled = root->ep_continue = 0;
    root->ints = 0;
}

void ut_init(uint8_t dp_pin) {
    // This core owns the PIO engine. Core 0 owns native USB/CDC interrupts.
    save_and_disable_interrupts();
    pio_usb_configuration_t cfg = PIO_USB_DEFAULT_CONFIG;
    cfg.pin_dp = dp_pin;
    cfg.pio_tx_num = 1;
    cfg.pio_rx_num = 1;
    cfg.skip_alarm_pool = true;
    pio_usb_host_init(&cfg);
    root_port_t *root = PIO_USB_ROOT_PORT(0);
    // R13 masks electrical attachment. Probing always starts as full speed;
    // successful EP0 traffic, not a high D+ line, proves a device is present.
    root->connected = true;
    root->is_fullspeed = true;
    root->suspended = true;
    next_frame = time_us_32() + 1000;
    ut_wait_for_device(750);
}

void __not_in_flash_func(ut_pump)(void) {
    uint32_t now = time_us_32();
    if ((int32_t)(now - next_frame) < 0) return;
    uint32_t delay = now - next_frame;
    if (delay > 50) metrics.late_frames++;
    next_frame += 1000;
    // Never burst several frames to catch up after enumeration/log work.
    if (delay >= 1000) next_frame = now + 1000;
    pio_usb_host_frame();
    uint32_t elapsed = time_us_32() - now;
    if (elapsed > metrics.max_frame_us) metrics.max_frame_us = elapsed;
}

void ut_delay_ms(uint32_t ms) {
    uint32_t start = time_us_32();
    while ((uint32_t)(time_us_32() - start) < ms * 1000) ut_pump();
}
uint32_t __not_in_flash_func(ut_idle_budget_us)(void) {
    int32_t remaining=(int32_t)(next_frame-time_us_32());
    return remaining>0?(uint32_t)remaining:0;
}

void ut_close_device(uint8_t address) { pio_usb_host_close_device(0, address); }
void ut_wait_for_device(uint32_t ms) {
    ut_close_device(0);ut_close_device(1);
    // Leave the bus at idle without SOFs or probes so a newly attached
    // controller can complete startup. Do not hold it in USB reset here.
    PIO_USB_ROOT_PORT(0)->suspended = true;
    ut_delay_ms(ms);
}
void ut_reset(void) {
    ut_close_device(0);
    ut_close_device(1);
    memset(completed, 0, sizeof(completed));
    root_port_t *root = PIO_USB_ROOT_PORT(0);
    root->connected = true;
    root->is_fullspeed = true;
    pio_usb_host_port_reset_start(0);
    ut_delay_ms(30);
    pio_usb_host_port_reset_end(0);
    ut_delay_ms(10);
}

// Only used during failed Elite 2 enumeration, never during active input.
// USB 2.0 7.1.7.7: >=20 ms K, low-speed EOP, 10 ms recovery with SOF only.
void ut_resume_fullspeed(void) {
    ut_close_device(0);ut_close_device(1);
    root_port_t *root=PIO_USB_ROOT_PORT(0);
    pio_port_t *pp=PIO_USB_PIO_PORT(0);
    root->suspended=true;
    ut_delay_ms(10);
    gpio_set_outover(root->pin_dp,GPIO_OVERRIDE_LOW);
    gpio_set_outover(root->pin_dm,GPIO_OVERRIDE_HIGH);
    gpio_set_oeover(root->pin_dp,GPIO_OVERRIDE_HIGH);
    gpio_set_oeover(root->pin_dm,GPIO_OVERRIDE_HIGH);
    ut_delay_ms(20);
    // Borrow the idle TX state machine for an exact 8+4 cycles at 6 MHz.
    // Restore its four instructions before SOF or any normal transaction.
    PIO pio=pp->pio_usb_tx;uint sm=pp->sm_tx,offset=pp->offset_tx;
    uint16_t saved[4];uint32_t divider=pio->sm[sm].clkdiv;
    uint j=root->pin_dp<root->pin_dm?1:2,k=j^3;
    pio_sm_set_enabled(pio,sm,false);
    for(unsigned i=0;i<4;++i)saved[i]=(uint16_t)pio->instr_mem[offset+i];
    pio->instr_mem[offset]=pio_encode_set(pio_pindirs,3)|pio_encode_sideset(2,0)|pio_encode_delay(7);
    pio->instr_mem[offset+1]=pio_encode_nop()|pio_encode_sideset(2,j)|pio_encode_delay(3);
    pio->instr_mem[offset+2]=pio_encode_set(pio_pindirs,0)|pio_encode_sideset(2,j);
    pio->instr_mem[offset+3]=pio_encode_jmp(offset+3)|pio_encode_sideset(2,j);
    SM_SET_CLKDIV(pio,sm,pp->clk_div_ls_tx);
    pio_sm_clkdiv_restart(pio,sm);
    pio_sm_exec(pio,sm,pio_encode_set(pio_pindirs,3)|pio_encode_sideset(2,k));
    pio_sm_exec(pio,sm,pio_encode_jmp(offset)|pio_encode_sideset(2,k));
    gpio_set_outover(root->pin_dp,GPIO_OVERRIDE_NORMAL);
    gpio_set_outover(root->pin_dm,GPIO_OVERRIDE_NORMAL);
    gpio_set_oeover(root->pin_dp,GPIO_OVERRIDE_NORMAL);
    gpio_set_oeover(root->pin_dm,GPIO_OVERRIDE_NORMAL);
    pio_sm_set_enabled(pio,sm,true);
    busy_wait_us(3);
    pio_sm_set_enabled(pio,sm,false);
    for(unsigned i=0;i<4;++i)pio->instr_mem[offset+i]=saved[i];
    pio->sm[sm].clkdiv=divider;
    pio_sm_exec(pio,sm,pp->tx_reset_instr);
    pio_sm_set_enabled(pio,sm,true);
    root->connected=true;root->is_fullspeed=true;root->suspended=false;
    next_frame=time_us_32();ut_delay_ms(10);
}

bool ut_open(uint8_t address, const uint8_t descriptor[7]) {
    bool ok = pio_usb_host_endpoint_open(0, address, descriptor, false);
    int i = find_ep(address, descriptor[2]);
    if (i >= 0) completed[i] = UT_PENDING;
    return ok;
}
void ut_ep0(uint8_t address, uint8_t size) {
    pio_usb_host_endpoint_close(0, address, 0);
    const uint8_t desc[7] = {7, 5, 0, 0, size, 0, 0};
    ut_open(address, desc);
}
bool ut_submit(uint8_t address, uint8_t endpoint, uint8_t *data, uint16_t length) {
    int i = find_ep(address, endpoint);
    if (i < 0) return false;
    completed[i] = UT_PENDING;
    return pio_usb_host_endpoint_transfer(0, address, endpoint, data, length);
}
ut_result ut_result_for(uint8_t address, uint8_t endpoint, uint16_t *actual) {
    int i = find_ep(address, endpoint);
    if (i < 0) return UT_ERROR;
    if (actual) *actual = lengths[i];
    return completed[i];
}
static ut_result wait_transfer(uint8_t address, uint8_t endpoint, uint16_t *actual) {
    uint32_t start = time_us_32();
    ut_result result;
    while ((result = ut_result_for(address, endpoint, actual)) == UT_PENDING) {
        ut_pump();
        if ((uint32_t)(time_us_32() - start) > 500000) {
            pio_usb_host_endpoint_abort_transfer(0, address, endpoint);
            return UT_TIMEOUT;
        }
    }
    return result;
}
ut_result ut_control(uint8_t address, const uint8_t setup[8], uint8_t *data, uint16_t *actual) {
    int i = find_ep(address, 0);
    if (i < 0) return UT_ERROR;
    completed[i] = UT_PENDING;
    if (actual) *actual = 0;
    if (!pio_usb_host_send_setup(0, address, setup)) return UT_ERROR;
    ut_result result = wait_transfer(address, 0, NULL);
    if (result != UT_OK) return result;
    uint16_t length = setup[6] | ((uint16_t)setup[7] << 8);
    uint8_t direction = setup[0] & 0x80;
    if (length) {
        if (!ut_submit(address, direction, data, length)) return UT_ERROR;
        result = wait_transfer(address, direction, actual);
        if (result != UT_OK) return result;
    }
    uint8_t status_direction = length && direction ? 0 : 0x80;
    if (!ut_submit(address, status_direction, NULL, 0)) return UT_ERROR;
    return wait_transfer(address, status_direction, NULL);
}
ut_result ut_interrupt_out(uint8_t address,const uint8_t descriptor[7],uint8_t *data,uint16_t length) {
    if(!ut_open(address,descriptor))return UT_ERROR;
    ut_result result=ut_submit(address,descriptor[2],data,length)?wait_transfer(address,descriptor[2],NULL):UT_ERROR;
    pio_usb_host_endpoint_close(0,address,descriptor[2]);
    return result;
}
ut_result ut_transfer_wait(uint8_t address,uint8_t endpoint) { return wait_transfer(address,endpoint,NULL); }
void ut_close_endpoint(uint8_t address,uint8_t endpoint) { pio_usb_host_endpoint_close(0,address,endpoint); }
void ut_set_interval(uint8_t address, uint8_t endpoint, uint8_t interval) {
    int i = find_ep(address, endpoint);
    if (i >= 0) {
        PIO_USB_ENDPOINT(i)->interval = interval;
        PIO_USB_ENDPOINT(i)->interval_counter = 0;
    }
}
void ut_metrics_get(ut_metrics *out) { *out = metrics; }
uint32_t ut_received_us(uint8_t address, uint8_t endpoint) {
    int i=find_ep(address,endpoint);
    return i<0 ? time_us_32() : received_us[i];
}
