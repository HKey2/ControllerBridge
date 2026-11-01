#include "controller_bridge.h"
#include "pico/stdlib.h"
#include "hardware/flash.h"
#include "hardware/sync.h"
#include <string.h>

// A new storage identity intentionally starts with no earlier saved data.
#define STORE_MAGIC 0x54534243u // CBST
#define STORE_VERSION 1u
#define STORE_BASE (PICO_FLASH_SIZE_BYTES-12*FLASH_SECTOR_SIZE)
// Slots 0/1 retain configuration/measurement. New 16 KiB banks use slots 4/5.
// First save uses slot 4, before the old v5 banks, so migration is power-loss safe.
// Slots 2/3 are unused and must never be read or written.
static bool store_slot_valid(unsigned slot) { return slot<2 || slot==4 || slot==5; }
static uint32_t store_offset(unsigned slot) {
    return slot<2 ? PICO_FLASH_SIZE_BYTES-(2-slot)*FLASH_SECTOR_SIZE :
        STORE_BASE+(slot-4)*4*FLASH_SECTOR_SIZE;
}
static uint32_t store_size(unsigned slot) { return (slot<4?1u:4u)*FLASH_SECTOR_SIZE; }
typedef struct { uint32_t magic, version, length, crc; } store_header;
static uint8_t write_buffer[4*FLASH_SECTOR_SIZE];
#define DIRECTION_OFFSET 64u
#define DIRECTION_MAGIC 0x31434b44u // DKC1
typedef struct { uint32_t magic,version,threshold,crc; } direction_setting;
#define DIRECTION_GAIN_MAGIC 0x31474b44u // DKG1, follows the unchanged DKC1 record.
typedef struct {
    uint32_t magic,version;
    uint16_t trackball_gain10,paddle_gain10;
    uint32_t crc;
} direction_gain_setting;
#define DIRECTION_HOLD_MAGIC 0x31544844u // DHT1, appended at sector offset 96.
typedef struct { uint32_t magic,version,hold_ms,crc; } direction_hold_setting;
#define DIRECTION_WAIT_MAGIC 0x31545744u // DWT1, appended at sector offset 112.
typedef struct { uint32_t magic,version,wait_ms,crc; } direction_wait_setting;
#define DIRECTION_MAP_MAGIC 0x31504d44u // DMP1, appended at sector offset 128.
typedef struct { uint32_t magic,version;uint8_t targets[4];uint32_t crc; } direction_map_setting;
#define DIRECTION_PADDLE_MAP_MAGIC 0x314d5044u // DPM1, appended at sector offset 144.
#define VIRTUAL_ROLLING_MAGIC 0x31535256u // VRS1, appended at sector offset 160.
#define TRACKBALL_HOLD_MAGIC 0x31484254u // TBH1, appended at sector offset 176.
#define ROLLING_STEP_MAGIC 0x31445352u // RSD1, appended at sector offset 192.
typedef struct { uint32_t magic,version,value,crc; } rolling_setting;
typedef struct { direction_setting threshold;direction_gain_setting gains;direction_hold_setting hold;direction_wait_setting wait;direction_map_setting map,paddle_map;rolling_setting rolling,trackball_hold,rolling_step; } direction_records;
_Static_assert(sizeof(direction_records)==144,"cursor Flash records layout");
_Static_assert(DIRECTION_OFFSET+sizeof(direction_records)<=FLASH_SECTOR_SIZE,"cursor records fit configuration sector");
_Static_assert(sizeof(store_header)+sizeof(ub_configuration)<=DIRECTION_OFFSET,"configuration extension overlap");
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
bool ub_store_load_legacy_bank(unsigned copy,void *data,uint32_t bytes) {
    if(copy>1 || bytes!=8148)return false;
    const store_header *h=(const store_header *)(XIP_BASE+PICO_FLASH_SIZE_BYTES-(8-2*copy)*FLASH_SECTOR_SIZE);
    if(h->magic!=STORE_MAGIC || h->version!=STORE_VERSION || h->length!=bytes || h->crc!=crc32((const uint8_t *)(h+1),bytes))return false;
    memcpy(data,h+1,bytes);return true;
}
static bool save(unsigned slot,const void *data,uint32_t bytes,bool host_running,const direction_records *direction) {
    if(!store_slot_valid(slot) || bytes>store_size(slot)-sizeof(store_header) ||
       (uintptr_t)&__flash_binary_end>XIP_BASE+STORE_BASE)return false;
    memset(write_buffer,0xff,sizeof(write_buffer));
    store_header h={STORE_MAGIC,STORE_VERSION,bytes,crc32(data,bytes)};
    memcpy(write_buffer,&h,sizeof(h)); memcpy(write_buffer+sizeof(h),data,bytes);
    if(direction)memcpy(write_buffer+DIRECTION_OFFSET,direction,sizeof(*direction));
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

bool ub_store_save(unsigned slot,const void *data,uint32_t bytes,bool host_running) {
    return save(slot,data,bytes,host_running,NULL);
}
bool ub_store_load_direction_threshold(uint32_t *threshold) {
    ub_configuration config;
    if(!ub_store_load(0,&config,sizeof(config)))return false;
    const direction_setting *s=(const direction_setting *)(XIP_BASE+store_offset(0)+DIRECTION_OFFSET);
    if(s->magic!=DIRECTION_MAGIC || s->version!=1 || !s->threshold || s->threshold>65535u ||
       s->crc!=crc32((const uint8_t *)s,12))return false;
    *threshold=s->threshold;return true;
}
bool ub_store_load_direction_settings(udk_settings *settings) {
    *settings=(udk_settings)UDK_DEFAULT_SETTINGS;
    ub_configuration config;
    if(!ub_store_load(0,&config,sizeof(config)))return false;
    bool threshold_valid=ub_store_load_direction_threshold(&settings->threshold);
    const direction_gain_setting *g=(const direction_gain_setting *)(XIP_BASE+store_offset(0)+DIRECTION_OFFSET+sizeof(direction_setting));
    udk_settings candidate=*settings;
    candidate.trackball_gain10=g->trackball_gain10;candidate.paddle_gain10=g->paddle_gain10;
    bool gains_valid=g->magic==DIRECTION_GAIN_MAGIC && g->version==1 &&
        g->crc==crc32((const uint8_t *)g,12) && udk_settings_valid(&candidate);
    if(gains_valid)*settings=candidate;
    const direction_hold_setting *h=(const direction_hold_setting *)(g+1);
    candidate=*settings;candidate.hold_ms=h->hold_ms;
    bool hold_valid=h->magic==DIRECTION_HOLD_MAGIC && h->version==1 &&
        h->crc==crc32((const uint8_t *)h,12) && udk_settings_valid(&candidate);
    if(hold_valid)*settings=candidate;
    // Before DWT1 existed, the saved hold time controlled both phases.
    // Inherit only a valid saved hold time when wait is missing/invalid.
    // Without a saved hold record, retain the independent factory wait default.
    if(hold_valid)settings->wait_ms=settings->hold_ms;
    const direction_wait_setting *w=(const direction_wait_setting *)(h+1);
    candidate=*settings;candidate.wait_ms=w->wait_ms;
    bool wait_valid=w->magic==DIRECTION_WAIT_MAGIC && w->version==1 &&
        w->crc==crc32((const uint8_t *)w,12) && udk_settings_valid(&candidate);
    if(wait_valid)*settings=candidate;
    const direction_map_setting *m=(const direction_map_setting *)(w+1);
    bool map_valid=m->magic==DIRECTION_MAP_MAGIC && m->version==1 &&
        m->crc==crc32((const uint8_t *)m,12) && udk_targets_valid(m->targets);
    if(map_valid)memcpy(settings->targets,m->targets,4);
    // Older firmware used the trackball map for both sources. Preserve that
    // behavior until the new paddle record is explicitly saved; never write on read.
    memcpy(settings->paddle_targets,settings->targets,4);
    const direction_map_setting *p=(const direction_map_setting *)(m+1);
    bool paddle_valid=p->magic==DIRECTION_PADDLE_MAP_MAGIC && p->version==1 &&
        p->crc==crc32((const uint8_t *)p,12) && udk_targets_valid(p->targets);
    if(paddle_valid)memcpy(settings->paddle_targets,p->targets,4);
    const rolling_setting *v=(const rolling_setting *)(p+1);candidate=*settings;udk_rolling_unpack(&candidate,v->value);
    bool rolling_valid=v->magic==VIRTUAL_ROLLING_MAGIC && v->version==1 &&
        v->crc==crc32((const uint8_t *)v,12) && udk_settings_valid(&candidate);
    if(rolling_valid)*settings=candidate;
    const rolling_setting *t=v+1;candidate=*settings;
    bool trackball_valid=t->magic==TRACKBALL_HOLD_MAGIC && t->version==1 &&
        t->crc==crc32((const uint8_t *)t,12) && udk_hold_unpack(&candidate,t->value) && udk_settings_valid(&candidate);
    if(trackball_valid)*settings=candidate;
    const rolling_setting *r=t+1;candidate=*settings;
    candidate.rolling_step_deg=(uint8_t)r->value;
    bool step_valid=r->magic==ROLLING_STEP_MAGIC && r->version==1 && r->value>=5 && r->value<=90 &&
        r->crc==crc32((const uint8_t *)r,12) && udk_settings_valid(&candidate);
    if(step_valid)*settings=candidate;
    return threshold_valid && gains_valid && hold_valid && wait_valid && map_valid && paddle_valid && rolling_valid && trackball_valid && step_valid;
}
bool ub_store_save_configuration(const ub_configuration *config,const udk_settings *settings,bool host_running) {
    if(!udk_settings_valid(settings))return false;
    direction_records records={
        .threshold={DIRECTION_MAGIC,1,settings->threshold,0},
        .gains={DIRECTION_GAIN_MAGIC,1,settings->trackball_gain10,settings->paddle_gain10,0},
        .hold={DIRECTION_HOLD_MAGIC,1,settings->hold_ms,0},
        .wait={DIRECTION_WAIT_MAGIC,1,settings->wait_ms,0},
        .map={DIRECTION_MAP_MAGIC,1,{0,0,0,0},0},
        .paddle_map={DIRECTION_PADDLE_MAP_MAGIC,1,{0,0,0,0},0},
        .rolling={VIRTUAL_ROLLING_MAGIC,1,udk_rolling_pack(settings),0},
        .trackball_hold={TRACKBALL_HOLD_MAGIC,1,udk_hold_pack(settings),0},
        .rolling_step={ROLLING_STEP_MAGIC,1,settings->rolling_step_deg,0}};
    memcpy(records.map.targets,settings->targets,4);memcpy(records.paddle_map.targets,settings->paddle_targets,4);
    records.threshold.crc=crc32((const uint8_t *)&records.threshold,12);
    records.gains.crc=crc32((const uint8_t *)&records.gains,12);
    records.hold.crc=crc32((const uint8_t *)&records.hold,12);
    records.wait.crc=crc32((const uint8_t *)&records.wait,12);
    records.map.crc=crc32((const uint8_t *)&records.map,12);
    records.paddle_map.crc=crc32((const uint8_t *)&records.paddle_map,12);
    records.rolling.crc=crc32((const uint8_t *)&records.rolling,12);
    records.trackball_hold.crc=crc32((const uint8_t *)&records.trackball_hold,12);
    records.rolling_step.crc=crc32((const uint8_t *)&records.rolling_step,12);
    return save(0,config,sizeof(*config),host_running,&records);
}
