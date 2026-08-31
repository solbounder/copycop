#include "typer.h"

#include "board_config.h"
#include "keyboard_layout.h"
#include "pico/rand.h"
#include "pico/time.h"
#include "usb_device.h"

typedef enum typer_phase {
    TYPER_IDLE,
    TYPER_PRESS,
    TYPER_RELEASE,
    TYPER_GAP,
    TYPER_FINAL_RELEASE,
} typer_phase_t;

static const uint8_t *text_bytes;
static size_t text_length;
static size_t text_offset;
static uint16_t configured_delay_ms;
static const uint16_t *configured_random_delays_ms;
static size_t configured_random_delay_count;
static bool random_timing;
static absolute_time_t next_action;
static copycop_key_sequence_t current_sequence;
static uint8_t sequence_index;
static typer_phase_t phase;
static bool error_seen;

static uint16_t next_delay_ms(void) {
    if (!random_timing) return configured_delay_ms;
    return configured_random_delays_ms[
        get_rand_32() % configured_random_delay_count];
}

static void request_finish(bool error) {
    phase = TYPER_FINAL_RELEASE;
    error_seen = error;
    next_action = get_absolute_time();
}

void typer_init(void) {
    phase = TYPER_IDLE;
    error_seen = false;
}

static bool start_common(const uint8_t *utf8, size_t length) {
    text_bytes = utf8;
    text_length = length;
    text_offset = 0u;
    sequence_index = 0u;
    current_sequence.count = 0u;
    error_seen = false;
    phase = TYPER_PRESS;
    next_action = get_absolute_time();
    return true;
}

bool typer_start(const uint8_t *utf8, size_t length, uint16_t inter_key_delay_ms) {
    if (phase != TYPER_IDLE || utf8 == NULL || length == 0u) return false;
    random_timing = false;
    configured_delay_ms = inter_key_delay_ms;
    return start_common(utf8, length);
}

bool typer_start_random(const uint8_t *utf8, size_t length,
                        const uint16_t *delay_levels_ms, size_t delay_level_count) {
    if (phase != TYPER_IDLE || utf8 == NULL || length == 0u
        || delay_levels_ms == NULL || delay_level_count == 0u) {
        return false;
    }
    random_timing = true;
    configured_random_delays_ms = delay_levels_ms;
    configured_random_delay_count = delay_level_count;
    return start_common(utf8, length);
}

void typer_task(void) {
    if (phase == TYPER_IDLE || absolute_time_diff_us(get_absolute_time(), next_action) > 0) {
        return;
    }

    if (phase == TYPER_PRESS) {
        if (sequence_index >= current_sequence.count) {
            if (text_offset >= text_length) {
                request_finish(false);
                return;
            }
            uint32_t codepoint;
            if (!utf8_decode_next(text_bytes, text_length, &text_offset, &codepoint)
                || !keyboard_layout_de_map(codepoint, &current_sequence)) {
                request_finish(true);
                return;
            }
            sequence_index = 0u;
        }

        const copycop_key_stroke_t stroke = current_sequence.strokes[sequence_index];
        if (!copycop_usb_keyboard_press(stroke.modifiers, stroke.keycode)) return;
        phase = TYPER_RELEASE;
        next_action = make_timeout_time_ms(COPYCOP_KEY_HOLD_MS);
        return;
    }

    if (phase == TYPER_RELEASE) {
        if (!copycop_usb_keyboard_release_all()) return;
        ++sequence_index;
        phase = TYPER_GAP;
        next_action = make_timeout_time_ms(next_delay_ms());
        return;
    }

    if (phase == TYPER_GAP) {
        phase = TYPER_PRESS;
        next_action = get_absolute_time();
        return;
    }

    if (copycop_usb_keyboard_release_all()) {
        phase = TYPER_IDLE;
    }
}

void typer_cancel(void) {
    if (phase != TYPER_IDLE) {
        request_finish(false);
    }
}

bool typer_is_active(void) {
    return phase != TYPER_IDLE;
}

bool typer_had_error(void) {
    return error_seen;
}
