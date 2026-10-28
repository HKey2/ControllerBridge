#pragma once

// Keep the per-report conversion path independent of XIP cache placement.
// Host-side deterministic tests use the same code without Pico attributes.
#if defined(PICO_ON_DEVICE)
#include "pico.h"
#define UC_RAM_FUNC(name) __not_in_flash_func(name)
#else
#define UC_RAM_FUNC(name) name
#endif
