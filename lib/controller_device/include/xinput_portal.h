#ifndef XINPUT_PORTAL_H
#define XINPUT_PORTAL_H
#include <stdbool.h>
#include <stdint.h>

// Optional, unadvertised string requests carry configuration through the
// Windows hub driver while the device keeps its ordinary XUSB identity.
// Nonces and checksums prevent accidental/stale transactions, not hostile access.
typedef struct {
    uint8_t command[64];
    uint8_t device_id[8];
    uint32_t nonce,expires,response_revision;
    uint16_t nonce_low;
    bool opening,open,staging,waiting;
} xw_portal;
int xw_portal_read(xw_portal *portal,const uint8_t setup[8],uint8_t *data,
    uint16_t capacity,uint32_t now,bool busy,const uint8_t response[64],uint32_t response_revision,
    void (*submit)(const uint8_t *,uint16_t,void *),void *context);
#endif
