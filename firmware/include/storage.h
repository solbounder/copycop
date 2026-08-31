#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

void storage_init(void);

bool storage_get_text(const uint8_t **utf8, size_t *length);
uint32_t storage_text_crc(void);
uint32_t storage_generation(void);
size_t storage_max_text_bytes(void);

bool storage_commit_text(const uint8_t *utf8, size_t length, uint32_t expected_crc);
bool storage_clear_text(void);

uint8_t storage_load_speed_index(void);
bool storage_save_speed_index(uint8_t speed_index);

