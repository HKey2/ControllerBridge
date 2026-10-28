#include "controller_usb.h"
#include "transport.h"
#include "pico/stdlib.h"
#include "hardware/gpio.h"
#include "hardware/sync.h"
#include "hardware/structs/ioqspi.h"
#include "hardware/structs/sio.h"
#include <stdatomic.h>

// Core 1 offers an idle USB frame gap. Core 0 never waits for that offer.
// Only these RAM functions can own OFFER/CLAIM; both cores are then outside XIP.
enum { IDLE, REQUEST, OFFER, CLAIM, DONE };
static atomic_uint state;
static uint32_t next_sample;
static uc_bootsel_statistics stats;
static atomic_uint host_pause_max_us,offers_expired,budget_overruns;

void __not_in_flash_func(uc_bootsel_host_service)(void) {
    if(atomic_load_explicit(&state,memory_order_acquire)!=REQUEST)return;
    uint32_t budget_us=ut_idle_budget_us();if(budget_us<100)return;
    uint32_t start=time_us_32();unsigned expected=REQUEST;
    if(!atomic_compare_exchange_strong_explicit(&state,&expected,OFFER,memory_order_acq_rel,memory_order_acquire))return;
    for(;;) {
        unsigned current=atomic_load_explicit(&state,memory_order_acquire);
        if(current==DONE) { atomic_store_explicit(&state,IDLE,memory_order_release);break; }
        if(current!=OFFER && current!=CLAIM)break;
        if(current==OFFER && (uint32_t)(time_us_32()-start)>=20) {
            expected=OFFER;
            if(atomic_compare_exchange_strong_explicit(&state,&expected,REQUEST,memory_order_acq_rel,memory_order_acquire)) {
                atomic_fetch_add_explicit(&offers_expired,1,memory_order_relaxed);break;
            }
        }
        // Once CLAIM succeeds we must stay in RAM until CS is restored.
    }
    uint32_t elapsed=time_us_32()-start;
    if(elapsed>atomic_load_explicit(&host_pause_max_us,memory_order_relaxed))atomic_store_explicit(&host_pause_max_us,elapsed,memory_order_relaxed);
    if(elapsed>=budget_us)atomic_fetch_add_explicit(&budget_overruns,1,memory_order_relaxed);
}

bool __not_in_flash_func(uc_bootsel_poll)(bool enabled,bool *pressed,uint32_t *sampled_us) {
    unsigned current=atomic_load_explicit(&state,memory_order_acquire);
    if(!enabled || atomic_load_explicit(&budget_overruns,memory_order_relaxed)) {
        if(current==REQUEST || current==OFFER) {
            unsigned expected=current;
            atomic_compare_exchange_strong_explicit(&state,&expected,IDLE,memory_order_acq_rel,memory_order_acquire);
        }
        next_sample=0;return false;
    }
    uint32_t now=time_us_32();
    if(current==IDLE && (!next_sample || (int32_t)(now-next_sample)>=0)) {
        atomic_store_explicit(&state,REQUEST,memory_order_release);return false;
    }
    if(current!=OFFER)return false;
    uint32_t irq=save_and_disable_interrupts(),start=time_us_32();unsigned expected=OFFER;
    if(!atomic_compare_exchange_strong_explicit(&state,&expected,CLAIM,memory_order_acq_rel,memory_order_acquire)) {
        restore_interrupts(irq);return false;
    }
    __dsb();
    uint32_t control=ioqspi_hw->io[1].ctrl;
    hw_write_masked(&ioqspi_hw->io[1].ctrl,GPIO_OVERRIDE_LOW<<IO_QSPI_GPIO_QSPI_SS_CTRL_OEOVER_LSB,
                    IO_QSPI_GPIO_QSPI_SS_CTRL_OEOVER_BITS);
    // 1 kohm BOOT pulldown: allow at least 2 us to settle, using a RAM-safe timer.
    uint32_t floated=time_us_32();
    while((uint32_t)(time_us_32()-floated)<2)__asm volatile("nop");
    bool value=!(sio_hw->gpio_hi_in&SIO_GPIO_HI_IN_QSPI_CSN_BITS);
    ioqspi_hw->io[1].ctrl=control;__dsb();
    atomic_store_explicit(&state,DONE,memory_order_release);
    uint32_t elapsed=time_us_32()-start;
    restore_interrupts(irq);
    if(elapsed>stats.irq_mask_max_us)stats.irq_mask_max_us=elapsed;
    stats.samples++;stats.last_sample_us=now;stats.pressed=value;
    *pressed=value;*sampled_us=now;next_sample=now+50000;
    return true;
}
void uc_bootsel_statistics_get(uc_bootsel_statistics *result) {
    *result=stats;
    result->host_pause_max_us=atomic_load_explicit(&host_pause_max_us,memory_order_relaxed);
    result->offers_expired=atomic_load_explicit(&offers_expired,memory_order_relaxed);
    result->budget_overruns=atomic_load_explicit(&budget_overruns,memory_order_relaxed);
}
