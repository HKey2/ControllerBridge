#ifndef CONTROLLER_DISPLAY_H
#define CONTROLLER_DISPLAY_H
#include <stdbool.h>
#include <stdint.h>
// Persistent IDs, never UI positions. Add new IDs without renumbering old ones.
enum { UDISPLAY_OFF=0, UDISPLAY_NAME_PERIOD_SLOT=1, UDISPLAY_NAME=2, UDISPLAY_DETAIL_A=3, UDISPLAY_DETAIL_B=4, UDISPLAY_DETAIL_C=5 };
#define UDISPLAY_SUPPORTED_MASK ((1u<<UDISPLAY_OFF)|(1u<<UDISPLAY_NAME_PERIOD_SLOT)|(1u<<UDISPLAY_NAME)|(1u<<UDISPLAY_DETAIL_A)|(1u<<UDISPLAY_DETAIL_B)|(1u<<UDISPLAY_DETAIL_C))
#define UDISPLAY_DEFAULT UDISPLAY_OFF
static inline bool udisplay_valid(uint32_t type) {
    switch(type) {
    case UDISPLAY_OFF:case UDISPLAY_NAME_PERIOD_SLOT:case UDISPLAY_NAME:case UDISPLAY_DETAIL_A:case UDISPLAY_DETAIL_B:case UDISPLAY_DETAIL_C:return true;
    default:return false;
    }
}
#endif
