#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#define COPYCOP_MAX_STROKES_PER_CODEPOINT 2u

typedef struct copycop_key_stroke {
    uint8_t modifiers;
    uint8_t keycode;
} copycop_key_stroke_t;

typedef struct copycop_key_sequence {
    copycop_key_stroke_t strokes[COPYCOP_MAX_STROKES_PER_CODEPOINT];
    uint8_t count;
} copycop_key_sequence_t;

bool keyboard_layout_de_map(uint32_t codepoint, copycop_key_sequence_t *sequence);

/* Strict UTF-8 decoder. Advances offset only for a valid scalar value. */
bool utf8_decode_next(const uint8_t *bytes, size_t length, size_t *offset,
                      uint32_t *codepoint);

