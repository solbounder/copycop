#pragma once

#include <stddef.h>
#include <stdint.h>

uint32_t copycop_crc32_update(uint32_t crc, const void *data, size_t length);
uint32_t copycop_crc32(const void *data, size_t length);

