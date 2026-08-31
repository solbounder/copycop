#include "keyboard_layout.h"

#include <string.h>

#include "class/hid/hid.h"

#define MOD_SHIFT KEYBOARD_MODIFIER_LEFTSHIFT
#define MOD_ALTGR KEYBOARD_MODIFIER_RIGHTALT

_Static_assert(MOD_ALTGR == 0x40u, "AltGr must be HID right Alt");

static void one(copycop_key_sequence_t *sequence, uint8_t modifiers, uint8_t keycode) {
    sequence->strokes[0] = (copycop_key_stroke_t){modifiers, keycode};
    sequence->count = 1u;
}

static void dead_key(copycop_key_sequence_t *sequence, uint8_t modifiers, uint8_t keycode) {
    sequence->strokes[0] = (copycop_key_stroke_t){modifiers, keycode};
    sequence->strokes[1] = (copycop_key_stroke_t){0u, HID_KEY_SPACE};
    sequence->count = 2u;
}

bool keyboard_layout_de_map(uint32_t codepoint, copycop_key_sequence_t *sequence) {
    if (sequence == NULL) {
        return false;
    }
    memset(sequence, 0, sizeof(*sequence));

    if (codepoint >= 'a' && codepoint <= 'z') {
        uint8_t keycode = (uint8_t)(HID_KEY_A + (codepoint - 'a'));
        if (codepoint == 'y') keycode = HID_KEY_Z;
        if (codepoint == 'z') keycode = HID_KEY_Y;
        one(sequence, 0u, keycode);
        return true;
    }
    if (codepoint >= 'A' && codepoint <= 'Z') {
        uint8_t keycode = (uint8_t)(HID_KEY_A + (codepoint - 'A'));
        if (codepoint == 'Y') keycode = HID_KEY_Z;
        if (codepoint == 'Z') keycode = HID_KEY_Y;
        one(sequence, MOD_SHIFT, keycode);
        return true;
    }
    if (codepoint >= '1' && codepoint <= '9') {
        one(sequence, 0u, (uint8_t)(HID_KEY_1 + (codepoint - '1')));
        return true;
    }

    switch (codepoint) {
        case '0': one(sequence, 0u, HID_KEY_0); break;
        case ' ': one(sequence, 0u, HID_KEY_SPACE); break;
        case '\t': one(sequence, 0u, HID_KEY_TAB); break;
        case '\n': one(sequence, 0u, HID_KEY_ENTER); break;
        case '!': one(sequence, MOD_SHIFT, HID_KEY_1); break;
        case '"': one(sequence, MOD_SHIFT, HID_KEY_2); break;
        case 0x00A7u: one(sequence, MOD_SHIFT, HID_KEY_3); break; /* section sign */
        case '$': one(sequence, MOD_SHIFT, HID_KEY_4); break;
        case '%': one(sequence, MOD_SHIFT, HID_KEY_5); break;
        case '&': one(sequence, MOD_SHIFT, HID_KEY_6); break;
        case '/': one(sequence, MOD_SHIFT, HID_KEY_7); break;
        case '(': one(sequence, MOD_SHIFT, HID_KEY_8); break;
        case ')': one(sequence, MOD_SHIFT, HID_KEY_9); break;
        case '=': one(sequence, MOD_SHIFT, HID_KEY_0); break;
        case '?': one(sequence, MOD_SHIFT, HID_KEY_MINUS); break;
        case '`': dead_key(sequence, MOD_SHIFT, HID_KEY_EQUAL); break;
        case 0x00B4u: dead_key(sequence, 0u, HID_KEY_EQUAL); break; /* acute */
        case '+': one(sequence, 0u, HID_KEY_BRACKET_RIGHT); break;
        case '*': one(sequence, MOD_SHIFT, HID_KEY_BRACKET_RIGHT); break;
        case '~': dead_key(sequence, MOD_ALTGR, HID_KEY_BRACKET_RIGHT); break;
        case '#': one(sequence, 0u, HID_KEY_BACKSLASH); break;
        case '\'': one(sequence, MOD_SHIFT, HID_KEY_BACKSLASH); break;
        case '-': one(sequence, 0u, HID_KEY_SLASH); break;
        case '_': one(sequence, MOD_SHIFT, HID_KEY_SLASH); break;
        case '.': one(sequence, 0u, HID_KEY_PERIOD); break;
        case ',': one(sequence, 0u, HID_KEY_COMMA); break;
        case ':': one(sequence, MOD_SHIFT, HID_KEY_PERIOD); break;
        case ';': one(sequence, MOD_SHIFT, HID_KEY_COMMA); break;
        case '<': one(sequence, 0u, HID_KEY_EUROPE_2); break;
        case '>': one(sequence, MOD_SHIFT, HID_KEY_EUROPE_2); break;
        case '|': one(sequence, MOD_ALTGR, HID_KEY_EUROPE_2); break;
        case '@': one(sequence, MOD_ALTGR, HID_KEY_Q); break;
        case 0x20ACu: one(sequence, MOD_ALTGR, HID_KEY_E); break; /* euro */
        case '[': one(sequence, MOD_ALTGR, HID_KEY_8); break;
        case ']': one(sequence, MOD_ALTGR, HID_KEY_9); break;
        case '{': one(sequence, MOD_ALTGR, HID_KEY_7); break;
        case '}': one(sequence, MOD_ALTGR, HID_KEY_0); break;
        case '\\': one(sequence, MOD_ALTGR, HID_KEY_MINUS); break;
        case 0x00E4u: one(sequence, 0u, HID_KEY_APOSTROPHE); break;
        case 0x00F6u: one(sequence, 0u, HID_KEY_SEMICOLON); break;
        case 0x00FCu: one(sequence, 0u, HID_KEY_BRACKET_LEFT); break;
        case 0x00C4u: one(sequence, MOD_SHIFT, HID_KEY_APOSTROPHE); break;
        case 0x00D6u: one(sequence, MOD_SHIFT, HID_KEY_SEMICOLON); break;
        case 0x00DCu: one(sequence, MOD_SHIFT, HID_KEY_BRACKET_LEFT); break;
        case 0x00DFu: one(sequence, 0u, HID_KEY_MINUS); break;
        default: return false;
    }
    return true;
}

bool utf8_decode_next(const uint8_t *bytes, size_t length, size_t *offset,
                      uint32_t *codepoint) {
    if (bytes == NULL || offset == NULL || codepoint == NULL || *offset >= length) {
        return false;
    }

    const size_t start = *offset;
    const uint8_t first = bytes[start];
    uint32_t value;
    size_t count;
    uint32_t minimum;

    if (first < 0x80u) {
        value = first;
        count = 1u;
        minimum = 0u;
    } else if ((first & 0xE0u) == 0xC0u) {
        value = first & 0x1Fu;
        count = 2u;
        minimum = 0x80u;
    } else if ((first & 0xF0u) == 0xE0u) {
        value = first & 0x0Fu;
        count = 3u;
        minimum = 0x800u;
    } else if ((first & 0xF8u) == 0xF0u) {
        value = first & 0x07u;
        count = 4u;
        minimum = 0x10000u;
    } else {
        return false;
    }

    if (start + count > length) return false;
    for (size_t index = 1u; index < count; ++index) {
        const uint8_t continuation = bytes[start + index];
        if ((continuation & 0xC0u) != 0x80u) return false;
        value = (value << 6u) | (continuation & 0x3Fu);
    }
    if (value < minimum || value > 0x10FFFFu || (value >= 0xD800u && value <= 0xDFFFu)) {
        return false;
    }

    *offset = start + count;
    *codepoint = value;
    return true;
}
