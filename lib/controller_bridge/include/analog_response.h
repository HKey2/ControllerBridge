#ifndef ANALOG_RESPONSE_H
#define ANALOG_RESPONSE_H
#include <stdbool.h>
#include <stdint.h>

// Effective response before the user's output minimum/maximum. The source
// reference and UI gain remain independent; no intermediate unity clipping.
#define UAR_UNIT UINT32_C(1000000000)
uint32_t uar_fraction(uint64_t numerator,uint64_t denominator);
uint32_t uar_curve(uint32_t value,uint8_t code,bool finite_low_gain);
uint32_t uar_response(uint64_t numerator,uint64_t denominator,uint16_t gain100,uint8_t curve);
#endif
