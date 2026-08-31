#include "crc32.h"

#include <stdint.h>

uint32_t copycop_crc32_update(uint32_t crc, const void *data, size_t length) {
    const uint8_t *bytes = (const uint8_t *)data;
    for (size_t index = 0; index < length; ++index) {
        crc ^= bytes[index];
        for (unsigned int bit = 0; bit < 8u; ++bit) {
            const uint32_t mask = (uint32_t)-(int32_t)(crc & 1u);
            crc = (crc >> 1u) ^ (UINT32_C(0xEDB88320) & mask);
        }
    }
    return crc;
}

uint32_t copycop_crc32(const void *data, size_t length) {
    return copycop_crc32_update(UINT32_C(0xFFFFFFFF), data, length)
        ^ UINT32_C(0xFFFFFFFF);
}

