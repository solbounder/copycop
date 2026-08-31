#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

void typer_init(void);
bool typer_start(const uint8_t *utf8, size_t length, uint16_t inter_key_delay_ms);
bool typer_start_random(const uint8_t *utf8, size_t length,
                        const uint16_t *delay_levels_ms, size_t delay_level_count);
void typer_set_delay_ms(uint16_t inter_key_delay_ms);
void typer_task(void);
void typer_cancel(void);
bool typer_is_active(void);
bool typer_had_error(void);
