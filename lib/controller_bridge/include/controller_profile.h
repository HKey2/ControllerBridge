#ifndef CONTROLLER_PROFILE_H
#define CONTROLLER_PROFILE_H
#include "controller_usb.h"
#include "switch_wired.h"
#include "controller_direction.h"
#include "controller_gyro.h"
#include <stddef.h>

#define UP_PROFILE_COUNT 16
#define UP_DIGITAL_COUNT 18
#define UP_AXIS_COUNT 4
#define UP_PROFILE_BYTES 848
#define UP_SHIFT_KEYS 3
#define UP_PROFILE_VERSION 9u
#define UP_LEGACY_PROFILE_BYTES 488
#define UP_LEGACY_BANK_BYTES 8148
#define UP_BANK_MAGIC 0x31425055u
#define UP_ID_BYTES 16
#define UP_RAPID_MIN_HZ 1
#define UP_RAPID_MAX_HZ 60
enum { UP_ENABLED=1, UP_MATCH_DESCRIPTOR=2, UP_MATCH_REVISION=4, UP_LED_CUSTOM=8, UP_RUMBLE=16 };
enum { UP_SIGNED=1, UP_REPORT_ID=2, UP_INVERT=4, UP_GYRO=8, UP_BIG_ENDIAN=16, UP_SOURCE_REVERSED=32 };
#define UP_SOURCE_FLAGS (UP_SIGNED|UP_REPORT_ID|UP_BIG_ENDIAN|UP_SOURCE_REVERSED)
enum { UP_DISABLED, UP_RANGE, UP_HAT_UP, UP_HAT_RIGHT, UP_HAT_DOWN, UP_HAT_LEFT };
enum { UP_AXIS_DISABLED, UP_AXIS_ANALOG, UP_AXIS_BUTTON_PAIR, UP_AXIS_ANALOG_PAIR };
enum { UP_MOUSE_TRACKBALL=1, UP_MOUSE_PADDLE=2, UP_MOUSE_INVERT_X=4, UP_MOUSE_INVERT_Y=8, UP_MOUSE_INVERT_PADDLE=16, UP_MOUSE_PADDLE_Y=32, UP_MOUSE_TRACKBALL_KEYS=64, UP_MOUSE_PADDLE_KEYS=128 };
// Byte 484: low two bits choose the USB layout; bit 2 remembers how
// buttons behave in combined mode. Keep the preference when another tab is used.
enum { UP_OUTPUT_GAMEPAD=0, UP_OUTPUT_MOUSE=1, UP_OUTPUT_COMBINED=2, UP_OUTPUT_CONTROLLER_BUTTONS=4 };
typedef struct {
    // Pointer-only gains use low/high bytes at fixed wire offsets.
    // Cursor gains live separately in udk_settings, never in this profile.
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
    // Rapid fire: 0=off, 1..60 cycles/second. Existing saved rates are retained.
    // Axis button-pair bindings always keep this byte zero.
    uint8_t bits,mode,flags,rapid_hz;
    int32_t low,high;
} up_digital;
typedef struct {
    uint8_t interface_number,report_id;
    uint16_t bit_offset;
    uint8_t bits,flags;
    // Gyro: noise threshold in raw-count Q8. Otherwise zero.
    uint16_t reserved;
    // Gyro: negative/positive count-ms limits, and resting raw-count Q8 bias.
    // Ordinary analog bindings retain their raw minimum/center/maximum.
    int32_t minimum,center,maximum;
    uint16_t deadzone_percent;
    uint8_t padding[10];
} up_analog;
typedef struct {
    uint8_t interface_number,report_id;
    uint16_t bit_offset;
    uint8_t bits,flags;
    uint16_t deadzone_percent;
    int32_t neutral,maximum; // Raw released and fully pressed values, in either order.
} up_analog_half;
typedef union { up_analog analog; up_digital pair[2]; up_analog_half halves[2]; } up_axis;
typedef struct {
    uint8_t targets[4]; // Explicit output IDs, U/D/L/R.
    uint8_t paddle_targets[2]; // Negative/positive after input reversal; independent of pointer axis.
    uint8_t modes; // bits 0..1 trackball, 2..3 paddle; 0 clicks, 1 Hold/Rolling.
    uint8_t rolling_flags; // bit 0 reverse, bit 1 left stick.
    uint8_t rolling_step_deg;
    uint8_t inertia_flags; // bit 0 enables pseudo-inertia for Trackball Hold.
    uint8_t reserved[6];
} up_conversion;
#define UP_DEFAULT_CONVERSION {{15,17,18,16},{18,16},0,0,30,0,{0}}
typedef struct {
    uint32_t overrides; // One bit per output: 0 inherits, 1 replaces (disabled = mute).
    uint8_t count,gyro_recenter[3]; // Shared DS3 physical chord, little-endian 17-bit mask.
    // All configured physical shift modifiers must be held.
    up_digital keys[UP_SHIFT_KEYS];
    up_conversion conversion; // Independent of shift activation; reuses the old fourth key.
    up_digital digital[UP_DIGITAL_COUNT];
} up_shift;
typedef struct {
    uint16_t version,flags,vid,pid,bcd_device;
    uint8_t input_interval_ms,axis_modes;
    uint32_t descriptor_hash;
    char name[48];
    up_digital digital[UP_DIGITAL_COUNT];
    up_axis axes[UP_AXIS_COUNT];
    up_mouse_options mouse;
    up_shift shift;
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
    bool base_digital[UP_DIGITAL_COUNT],shift_digital[UP_DIGITAL_COUNT];
    bool shift_active,shift_consumed;
    uint8_t shift_held,axes[UP_AXIS_COUNT],raw_axes[UP_AXIS_COUNT];
    uint16_t analog_halves[UP_AXIS_COUNT][2]; // Equal 0..65535 scales before signed summation.
    uint32_t inherited_conflicts[UP_DIGITAL_COUNT];
    uint8_t base_modifiers[UP_DIGITAL_COUNT],back_modifiers[UP_DIGITAL_COUNT];
    uint8_t pair_modifiers[UP_AXIS_COUNT][2],analog_modifiers[UP_AXIS_COUNT];
    uint32_t rapid_configured,rapid_held,rapid_on,physical;
    uint32_t rapid_time[UP_DIGITAL_COUNT],rapid_phase[UP_DIGITAL_COUNT],rapid_next[UP_DIGITAL_COUNT];
    int64_t gyro_position[UP_AXIS_COUNT]; // Q8 count-us, retained outside output saturation.
    up_gyro gyro;
    sw_report last_output;
    bool have_output;
    uint8_t gyro_axes;
} up_mapper;

// Keep the triggering physical bindings until all are released, even if the
// next profile maps those buttons differently. Only active during a switch.
typedef struct {
    up_digital bindings[UP_DIGITAL_COUNT];
    uint32_t keys,held;
} up_release_guard;

_Static_assert(sizeof(up_digital)==16,"digital wire ABI");
_Static_assert(sizeof(up_analog)==32,"analog wire ABI");
_Static_assert(sizeof(up_analog_half)==16,"analog half wire ABI");
_Static_assert(sizeof(up_profile)==UP_PROFILE_BYTES,"profile wire ABI");
_Static_assert(sizeof(up_mouse_options)==8,"mouse profile wire ABI");
_Static_assert(sizeof(up_shift)==360,"button shift wire ABI");
_Static_assert(sizeof(up_conversion)==16,"conversion wire ABI");
_Static_assert(offsetof(up_profile,shift.conversion)==544,"conversion offset");
_Static_assert(offsetof(up_profile,shift.digital)==560,"shifted bindings offset");
_Static_assert(sizeof(up_bank)==13908,"16 profiles fit four flash sectors with store header");

void up_profile_clear(up_profile *profile);
void up_bank_clear(up_bank *bank);
bool up_profile_valid(const up_profile *profile);
bool up_digital_overlap(const up_digital *a,const up_digital *b);
bool up_profile_shift_conflicts(const up_profile *profile,uint32_t switch_keys);
// Expand a validated v5 payload already copied to the beginning of this bank.
bool up_bank_upgrade_v5(up_bank *bank,const udk_settings *defaults);
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
uint32_t up_profile_recenter_keys(const up_profile *profile);
// Integrate at USB receipt timestamps, before ordinary mapping resolves output.
bool up_mapper_motion(up_mapper *mapper,uint8_t iface,const uint8_t *raw,unsigned length,uint32_t time_us);
bool up_mapper_apply(up_mapper *mapper,uint8_t interface_number,const uint8_t *report,unsigned length,sw_report *output);
void up_mapper_report(const up_mapper *mapper,sw_report *output);
void up_mapper_seed(up_mapper *mapper,uint8_t iface,uint8_t id,uint16_t offset,uint8_t bits,uint8_t flags,int32_t value);
// Apply hold-to-repeat after mapping, independently of USB input report timing.
// Returns true only when the filtered output changed. No waits or catch-up burst.
bool up_mapper_tick(up_mapper *mapper,uint32_t now_us,bool output_ready,sw_report *output);
void up_release_guard_begin(up_release_guard *guard,const up_profile *profile,uint32_t keys);
void up_release_guard_apply(up_release_guard *guard,uint8_t interface_number,const uint8_t *report,unsigned length);
// Load four-sector slots 4/5, or migrate the read-only v5 banks in RAM.
bool up_registry_load(up_bank *bank,const udk_settings *defaults);
bool up_bank_upgrade_v8(up_bank *bank);
bool up_bank_upgrade_v6(up_bank *bank,const udk_settings *defaults);
void up_conversion_from_legacy(up_conversion *conversion,const up_mouse_options *mouse,const udk_settings *defaults);
void up_conversion_apply(const up_conversion *conversion,udk_settings *effective);
bool up_registry_save(up_bank *bank,unsigned slot,const up_profile *profile);
// NULL preserves the existing ID; disabled profiles always clear it.
bool up_registry_save_identified(up_bank *bank,unsigned slot,const up_profile *profile,const uint8_t *id);
bool up_registry_select(up_bank *bank,unsigned slot);
bool up_registry_switch_keys(up_bank *bank,unsigned slot,uint32_t keys);
// Commit all slots together to the inactive Flash copy. The live bank and its
// generation change only after a verified write succeeds.
bool up_registry_replace(up_bank *bank,const up_bank *replacement);
#endif
