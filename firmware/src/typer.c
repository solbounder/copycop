#include "typer.h"

#include "board_config.h"
#include "class/hid/hid.h"
#include "keyboard_layout.h"
#include "pico/rand.h"
#include "pico/time.h"
#include "usb_device.h"

typedef enum typer_phase {
    TYPER_IDLE,
    TYPER_PREPARE,
    TYPER_MODIFIER_PRESS,
    TYPER_KEY_PRESS,
    TYPER_KEY_RELEASE,
    TYPER_MODIFIER_RELEASE,
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
static bool pause_requested;
static bool paused;

static uint16_t next_delay_ms(void) {
    if (!random_timing) return configured_delay_ms;
    return configured_random_delays_ms[
        get_rand_32() % configured_random_delay_count];
}

static bool uses_staged_altgr(copycop_key_stroke_t stroke) {
    return (stroke.modifiers & KEYBOARD_MODIFIER_RIGHTALT) != 0u;
}

static void request_finish(bool error) {
    pause_requested = false;
    paused = false;
    phase = TYPER_FINAL_RELEASE;
    error_seen = error;
    next_action = get_absolute_time();
}

static void enter_paused(void) {
    pause_requested = false;
    paused = true;
    next_action = get_absolute_time();
}

static bool can_enter_pause_now(void) {
    if (phase == TYPER_PREPARE || phase == TYPER_MODIFIER_PRESS
        || phase == TYPER_GAP) {
        return true;
    }
    return phase == TYPER_KEY_PRESS
        && !uses_staged_altgr(current_sequence.strokes[sequence_index]);
}

void typer_init(void) {
    phase = TYPER_IDLE;
    error_seen = false;
    pause_requested = false;
    paused = false;
}

static bool start_common(const uint8_t *utf8, size_t length) {
    text_bytes = utf8;
    text_length = length;
    text_offset = 0u;
    sequence_index = 0u;
    current_sequence.count = 0u;
    error_seen = false;
    pause_requested = false;
    paused = false;
    phase = TYPER_PREPARE;
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

void typer_set_delay_ms(uint16_t inter_key_delay_ms) {
    if (!random_timing) configured_delay_ms = inter_key_delay_ms;
}

void typer_pause(void) {
    if (phase != TYPER_IDLE && phase != TYPER_FINAL_RELEASE && !paused) {
        pause_requested = true;
    }
}

void typer_resume(void) {
    if (phase == TYPER_IDLE || (!paused && !pause_requested)) return;
    pause_requested = false;
    paused = false;
    next_action = get_absolute_time();
}

void typer_task(void) {
    if (phase == TYPER_IDLE || paused) {
        return;
    }

    if (pause_requested && can_enter_pause_now()) {
        enter_paused();
        return;
    }

    if (absolute_time_diff_us(get_absolute_time(), next_action) > 0) {
        return;
    }

    if (phase == TYPER_PREPARE) {
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
        phase = uses_staged_altgr(stroke)
            ? TYPER_MODIFIER_PRESS : TYPER_KEY_PRESS;
        next_action = get_absolute_time();
        return;
    }

    if (phase == TYPER_GAP) {
        phase = TYPER_PREPARE;
        next_action = get_absolute_time();
        return;
    }

    if (phase == TYPER_FINAL_RELEASE) {
        if (copycop_usb_keyboard_release_all()) {
            pause_requested = false;
            paused = false;
            phase = TYPER_IDLE;
        }
        return;
    }

    const copycop_key_stroke_t stroke = current_sequence.strokes[sequence_index];

    if (phase == TYPER_MODIFIER_PRESS) {
        if (!copycop_usb_keyboard_press(stroke.modifiers, 0u)) return;
        phase = TYPER_KEY_PRESS;
        next_action = make_timeout_time_ms(COPYCOP_ALTGR_SETTLE_MS);
        return;
    }

    if (phase == TYPER_KEY_PRESS) {
        if (!copycop_usb_keyboard_press(stroke.modifiers, stroke.keycode)) return;
        phase = TYPER_KEY_RELEASE;
        next_action = make_timeout_time_ms(
            uses_staged_altgr(stroke)
                ? COPYCOP_ALTGR_KEY_HOLD_MS : COPYCOP_KEY_HOLD_MS);
        return;
    }

    if (phase == TYPER_KEY_RELEASE) {
        if (uses_staged_altgr(stroke)) {
            if (!copycop_usb_keyboard_press(stroke.modifiers, 0u)) return;
            phase = TYPER_MODIFIER_RELEASE;
            next_action = make_timeout_time_ms(COPYCOP_ALTGR_SETTLE_MS);
            return;
        }
        if (!copycop_usb_keyboard_release_all()) return;
        ++sequence_index;
        phase = TYPER_GAP;
        if (pause_requested) {
            enter_paused();
        } else {
            next_action = make_timeout_time_ms(next_delay_ms());
        }
        return;
    }

    if (phase == TYPER_MODIFIER_RELEASE) {
        if (!copycop_usb_keyboard_release_all()) return;
        ++sequence_index;
        phase = TYPER_GAP;
        if (pause_requested) {
            enter_paused();
        } else {
            next_action = make_timeout_time_ms(next_delay_ms());
        }
        return;
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

bool typer_is_paused(void) {
    return phase != TYPER_IDLE && (paused || pause_requested);
}

bool typer_had_error(void) {
    return error_seen;
}
