#ifndef CONTROLLER_PROFILE_H
#define CONTROLLER_PROFILE_H
#include "controller_usb.h"
#include "switch_wired.h"

#define UP_PROFILE_COUNT 16
#define UP_DIGITAL_COUNT 18
#define UP_AXIS_COUNT 4
#define UP_PROFILE_BYTES 488
#define UP_BANK_MAGIC 0x31425055u
#define UP_ID_BYTES 16
#define UP_RAPID_MIN_HZ 2
#define UP_RAPID_MAX_HZ 30
enum { UP_ENABLED=1, UP_MATCH_DESCRIPTOR=2, UP_MATCH_REVISION=4, UP_LED_CUSTOM=8 };
enum { UP_SIGNED=1, UP_REPORT_ID=2, UP_INVERT=4 };
enum { UP_DISABLED, UP_RANGE, UP_HAT_UP, UP_HAT_RIGHT, UP_HAT_DOWN, UP_HAT_LEFT };
enum { UP_AXIS_DISABLED, UP_AXIS_ANALOG, UP_AXIS_BUTTON_PAIR };
enum { UP_MOUSE_TRACKBALL=1, UP_MOUSE_PADDLE=2, UP_MOUSE_INVERT_X=4, UP_MOUSE_INVERT_Y=8, UP_MOUSE_INVERT_PADDLE=16 };
// Byte 484: low two bits choose the USB layout; bit 2 remembers how
// buttons behave in combined mode. Keep the preference when another tab is used.
enum { UP_OUTPUT_GAMEPAD=0, UP_OUTPUT_MOUSE=1, UP_OUTPUT_COMBINED=2, UP_OUTPUT_CONTROLLER_BUTTONS=4 };
typedef struct {
    // Gain uses low and high bytes at fixed wire offsets.
    // Bytes 485..487 store exact RGB888 when UP_LED_CUSTOM is set.
    uint8_t flags,trackball_gain10,paddle_gain10,trackball_gain10_hi,output_mode,led_rgb[3];
} up_mouse_options;
static inline uint16_t up_mouse_trackball_gain10(const up_mouse_options *o) {
    return (uint16_t)(o->trackball_gain10|((uint16_t)o->trackball_gain10_hi<<8));
}
static inline void up_mouse_set_trackball_gain10(up_mouse_options *o,uint16_t gain) {
    o->trackball_gain10=(uint8_t)gain;o->trackball_gain10_hi=(uint8_t)(gain>>8);
}

typedef struct {
    uint8_t interface_number,report_id;
    uint16_t bit_offset;
    // Rapid fire: 0=off, 2..30 cycles/second.
    // Axis button-pair bindings always keep this byte zero.
    uint8_t bits,mode,flags,rapid_hz;
    int32_t low,high;
} up_digital;
typedef struct {
    uint8_t interface_number,report_id;
    uint16_t bit_offset;
    uint8_t bits,flags;
    uint16_t reserved;
    int32_t minimum,center,maximum;
    uint16_t deadzone_percent;
    uint8_t padding[10];
} up_analog;
typedef union { up_analog analog; up_digital pair[2]; } up_axis;
typedef struct {
    uint16_t version,flags,vid,pid,bcd_device;
    uint8_t input_interval_ms,axis_modes;
    uint32_t descriptor_hash;
    char name[48];
    up_digital digital[UP_DIGITAL_COUNT];
    up_axis axes[UP_AXIS_COUNT];
    up_mouse_options mouse;
} up_profile;
typedef struct {
    uint32_t magic,version,generation,bytes;
    up_profile profiles[UP_PROFILE_COUNT];
    uint8_t profile_ids[UP_PROFILE_COUNT][UP_ID_BYTES];
    uint32_t preferred_slots; // One selected bit per controller match group.
    uint32_t switch_keys[UP_PROFILE_COUNT]; // 0: BOOT; otherwise mapped physical chord, shared by group.
} up_bank;
typedef struct {
    const up_profile *profile;
    bool digital[UP_DIGITAL_COUNT],pairs[UP_AXIS_COUNT][2];
    uint8_t axes[UP_AXIS_COUNT];
    uint32_t rapid_configured,rapid_held,rapid_on,physical;
    uint32_t rapid_time[UP_DIGITAL_COUNT],rapid_phase[UP_DIGITAL_COUNT],rapid_next[UP_DIGITAL_COUNT];
    sw_report last_output;
    bool have_output;
} up_mapper;

// Keep the triggering physical bindings until all are released, even if the
// next profile maps those buttons differently. Only active during a switch.
typedef struct {
    up_digital bindings[UP_DIGITAL_COUNT];
    uint32_t keys,held;
} up_release_guard;

_Static_assert(sizeof(up_digital)==16,"digital wire ABI");
_Static_assert(sizeof(up_analog)==32,"analog wire ABI");
_Static_assert(sizeof(up_profile)==UP_PROFILE_BYTES,"profile wire ABI");
_Static_assert(sizeof(up_mouse_options)==8,"mouse profile wire ABI");
_Static_assert(sizeof(up_bank)==8148,"16 profiles fit two flash sectors with store header");

void up_profile_clear(up_profile *profile);
void up_bank_clear(up_bank *bank);
bool up_profile_valid(const up_profile *profile);
uint8_t up_profile_output_options(const up_profile *profile);
uint32_t up_profile_led_rgb(const up_profile *profile); // 0xRRGGBB; default 0x000001.
bool up_bank_valid(const up_bank *bank);
bool up_switch_keys_valid(uint32_t keys);
bool up_digital_pressed_value(const up_digital *binding,int32_t value);
int up_find_profile(const up_bank *bank,uint16_t vid,uint16_t pid,uint16_t revision,uint32_t descriptor_hash);
bool up_same_controller(const up_profile *a,const up_profile *b);
unsigned up_group_mask(const up_bank *bank,unsigned slot);
unsigned up_profile_number(const up_bank *bank,unsigned slot);
void up_prefer_slot(up_bank *bank,unsigned slot);
uint32_t up_crc32(const void *data,unsigned length);
void up_mapper_attach(up_mapper *mapper,const up_profile *profile);
bool up_mapper_apply(up_mapper *mapper,uint8_t interface_number,const uint8_t *report,unsigned length,sw_report *output);
void up_mapper_report(const up_mapper *mapper,sw_report *output);
// Apply hold-to-repeat after mapping, independently of USB input report timing.
// Returns true only when the filtered output changed. No waits or catch-up burst.
bool up_mapper_tick(up_mapper *mapper,uint32_t now_us,bool output_ready,sw_report *output);
void up_release_guard_begin(up_release_guard *guard,const up_profile *profile,uint32_t keys);
void up_release_guard_apply(up_release_guard *guard,uint8_t interface_number,const uint8_t *report,unsigned length);
// Load the current bank from two-sector slots 4/5; otherwise return an empty bank.
bool up_registry_load(up_bank *bank);
bool up_registry_save(up_bank *bank,unsigned slot,const up_profile *profile);
// NULL preserves the existing ID; disabled profiles always clear it.
bool up_registry_save_identified(up_bank *bank,unsigned slot,const up_profile *profile,const uint8_t *id);
bool up_registry_select(up_bank *bank,unsigned slot);
bool up_registry_switch_keys(up_bank *bank,unsigned slot,uint32_t keys);
// Commit all slots together to the inactive Flash copy. The live bank and its
// generation change only after a verified write succeeds.
bool up_registry_replace(up_bank *bank,const up_bank *replacement);
#endif
