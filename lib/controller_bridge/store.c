#include "controller_bridge.h"
#include "pico/stdlib.h"
#include "hardware/flash.h"
#include "hardware/sync.h"
#include <string.h>

// A new storage identity intentionally starts with no earlier saved data.
#define STORE_MAGIC 0x54534243u // CBST
#define STORE_VERSION 1u
#define STORE_BASE (PICO_FLASH_SIZE_BYTES-8*FLASH_SECTOR_SIZE)
// Slots 0/1 hold current configuration/measurement; 4/5 hold the bank copies.
// Slots 2/3 are unused and must never be read or written.
static bool store_slot_valid(unsigned slot) { return slot<2 || slot==4 || slot==5; }
static uint32_t store_offset(unsigned slot) {
    return slot<2 ? PICO_FLASH_SIZE_BYTES-(2-slot)*FLASH_SECTOR_SIZE :
        STORE_BASE+(slot-4)*2*FLASH_SECTOR_SIZE;
}
static uint32_t store_size(unsigned slot) { return (slot<4?1u:2u)*FLASH_SECTOR_SIZE; }
typedef struct { uint32_t magic, version, length, crc; } store_header;
static uint8_t write_buffer[2*FLASH_SECTOR_SIZE];
extern char __flash_binary_end;
static uint32_t crc32(const uint8_t *data,uint32_t length) {
    uint32_t crc=~0u;
    for(uint32_t i=0;i<length;++i) {
        crc^=data[i];
        for(unsigned b=0;b<8;++b)crc=(crc>>1)^(0xedb88320u&(0u-(crc&1u)));
    }
    return ~crc;
}
bool ub_store_load(unsigned slot,void *data,uint32_t bytes) {
    if(!store_slot_valid(slot) || bytes>store_size(slot)-sizeof(store_header))return false;
    const store_header *h=(const store_header *)(XIP_BASE+store_offset(slot));
    const uint8_t *payload=(const uint8_t *)(h+1);
    if(h->magic!=STORE_MAGIC || h->version!=STORE_VERSION || h->length!=bytes || h->crc!=crc32(payload,bytes))return false;
    memcpy(data,payload,bytes); return true;
}
bool ub_store_save(unsigned slot,const void *data,uint32_t bytes,bool host_running) {
    if(!store_slot_valid(slot) || bytes>store_size(slot)-sizeof(store_header) ||
       (uintptr_t)&__flash_binary_end>XIP_BASE+STORE_BASE)return false;
    memset(write_buffer,0xff,sizeof(write_buffer));
    store_header h={STORE_MAGIC,STORE_VERSION,bytes,crc32(data,bytes)};
    memcpy(write_buffer,&h,sizeof(h)); memcpy(write_buffer+sizeof(h),data,bytes);
    if(host_running && !uc_host_pause(1000))return false;
    uint32_t irq=save_and_disable_interrupts();
    uint32_t offset=store_offset(slot);
    uint32_t size=store_size(slot);
    flash_range_erase(offset,size);
    flash_range_program(offset,write_buffer,size);
    restore_interrupts(irq);
    if(host_running)uc_host_resume();
    return memcmp((const void *)(XIP_BASE+offset),write_buffer,store_size(slot))==0;
}
