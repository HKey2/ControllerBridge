#include "controller_usb.h"
#include "controller_memory.h"
#include <string.h>

typedef struct {
    uint32_t page, size, count, report_id;
    int32_t logical_min, logical_max;
} hid_globals;
static int32_t signed_item(uint32_t value, unsigned bytes) {
    if (bytes == 1) return (int8_t)value;
    if (bytes == 2) return (int16_t)value;
    return (int32_t)value;
}
bool uc_hid_parse(const uint8_t *desc, size_t length, uc_hid_layout *layout) {
    memset(layout, 0, sizeof(*layout));
    hid_globals g = {0}, stack[4];
    unsigned depth = 0, usage_count = 0;
    uint32_t usages[32], usage_min = 0, usage_max = 0;
    uint16_t offsets[256] = {0};
    bool range = false;
    for (size_t pos = 0; pos < length;) {
        uint8_t prefix = desc[pos++];
        if (prefix == 0xfe) return false; // unsupported long item, never guess
        unsigned n = prefix & 3;
        if (n == 3) n = 4;
        if (pos + n > length) return false;
        uint32_t value = 0;
        for (unsigned j = 0; j < n; ++j) value |= (uint32_t)desc[pos++] << (8*j);
        unsigned type = (prefix >> 2) & 3, tag = prefix >> 4;
        if (type == 1) {
            switch (tag) {
            case 0: if(value>65535)return false; g.page = value; break;
            case 1: g.logical_min = signed_item(value,n); break;
            case 2: g.logical_max = g.logical_min < 0 ? signed_item(value,n) : (int32_t)value; break;
            case 7: g.size = value; break;
            case 8:
                if (!value || value > 255) return false;
                g.report_id = value; layout->has_report_ids = true; break;
            case 9: g.count = value; break;
            case 10: if (depth == 4) return false; stack[depth++] = g; break;
            case 11: if (!depth) return false; g = stack[--depth]; break;
            default: break;
            }
        } else if (type == 2) {
            if (tag == 0) { if (usage_count == 32) return false; usages[usage_count++] = value; }
            else if (tag == 1) { usage_min = value; range = true; }
            else if (tag == 2) usage_max = value;
        } else if (type == 0) {
            if (tag == 8) {
                if (!(value & 1) && (!(value & 2) || (value & 4))) layout->unsupported_inputs = true;
                if (g.size > 32 || g.count > 4096 || g.size * g.count + offsets[g.report_id] > 65535) return false;
                for (uint32_t j = 0; j < g.count; ++j) {
                    uint32_t usage = range ? usage_min + j : usage_count ? usages[j < usage_count ? j : usage_count-1] : 0;
                    if (range && usage > usage_max) usage = usage_max;
                    if (!(value & 1) && (value & 2) && g.size && usage) {
                        if (layout->count == UC_HID_MAX_FIELDS) return false;
                        uc_hid_field *f = &layout->fields[layout->count++];
                        f->usage_page = (uint16_t)(usage > 65535 ? usage >> 16 : g.page);
                        f->usage = (uint16_t)(usage & 65535);
                        f->bit_offset = offsets[g.report_id];
                        f->bits = (uint8_t)g.size;
                        f->report_id = (uint8_t)g.report_id;
                        f->logical_min = g.logical_min;
                        f->logical_max = g.logical_max;
                        f->flags = (uint16_t)value;
                    }
                    offsets[g.report_id] = (uint16_t)(offsets[g.report_id]+g.size);
                }
            }
            usage_count = 0; range = false; usage_min = usage_max = 0;
        }
    }
    return depth == 0 && layout->count != 0;
}
const uc_hid_field *uc_hid_find(const uc_hid_layout *layout,uint16_t page,
                               uint16_t usage,uint8_t report_id) {
    for (unsigned i = 0; i < layout->count; ++i) {
        const uc_hid_field *f = &layout->fields[i];
        if (f->usage_page == page && f->usage == usage && f->report_id == report_id) return f;
    }
    return NULL;
}
bool UC_RAM_FUNC(uc_hid_field_value)(const uc_hid_field *f,bool has_ids,const uint8_t *data,size_t length,int32_t *out) {
    if(!f || !f->bits || f->bits>32)return false;
    if(has_ids) { if(!length || data[0]!=f->report_id)return false; data++; length--; }
    if((size_t)f->bit_offset+f->bits>length*8)return false;
    unsigned shift=f->bit_offset%8,bytes=(shift+f->bits+7)/8;
    const uint8_t *p=data+f->bit_offset/8;
    uint32_t value=p[0]>>shift;
    for(unsigned i=1;i<bytes;++i)value|=(uint32_t)p[i]<<(8*i-shift);
    if(f->bits<32) {
        uint32_t mask=(1u<<f->bits)-1;
        value&=mask;
        if(f->logical_min<0 && (value&(1u<<(f->bits-1))))value|=~mask;
    }
    *out=(int32_t)value; return true;
}
bool uc_hid_value(const uc_hid_layout *layout, uint16_t page, uint16_t usage,
                  const uint8_t *data, size_t length, int32_t *out) {
    if(layout->has_report_ids && !length)return false;
    const uc_hid_field *f=uc_hid_find(layout,page,usage,layout->has_report_ids?data[0]:0);
    return uc_hid_field_value(f,layout->has_report_ids,data,length,out);
}
