#include <stdbool.h>

#include "app_mode.h"
#include "board_config.h"
#include "buttons.h"
#include "hardware/watchdog.h"
#include "pico/bootrom.h"
#include "pico/rand.h"
#include "pico/stdlib.h"
#include "protocol.h"
#include "status_led.h"
#include "storage.h"
#include "typer.h"
#include "usb_device.h"

_Static_assert(PICO_FLASH_SIZE_BYTES == COPYCOP_FLASH_SIZE_BYTES,
               "Pico SDK board flash size does not match board_config.h");

static const copycop_rgb_t TARGET_BOOT_COLOR = {0u, 48u, 0u};
static const copycop_rgb_t LOAD_BOOT_COLOR = {0u, 0u, 48u};
static const copycop_rgb_t AFK_BOOT_COLOR = {32u, 0u, 40u};
static const copycop_rgb_t TARGET_IDLE_COLOR = {0u, 5u, 0u};
static const copycop_rgb_t LOAD_IDLE_COLOR = {0u, 0u, 5u};
static const copycop_rgb_t AFK_IDLE_COLOR = {4u, 0u, 6u};
static const copycop_rgb_t TYPING_COLOR = {0u, 18u, 20u};
static const copycop_rgb_t TRANSFER_COLOR = {20u, 10u, 0u};
static const copycop_rgb_t ERROR_COLOR = {32u, 0u, 0u};
static const copycop_rgb_t SUCCESS_COLOR = {0u, 32u, 0u};
static const copycop_rgb_t UPDATE_BOOT_COLOR = {24u, 24u, 24u};

static const uint16_t SPEED_LEVELS_MS[COPYCOP_SPEED_LEVEL_COUNT] = {
    5u, 25u, 50u, 100u, 250u, 500u, 750u, 1000u,
};

static const copycop_rgb_t SPEED_COLORS[COPYCOP_SPEED_LEVEL_COUNT] = {
    {0u, 24u, 24u}, {0u, 28u, 14u}, {0u, 32u, 0u}, {12u, 28u, 0u},
    {24u, 20u, 0u}, {30u, 12u, 0u}, {32u, 5u, 0u}, {32u, 0u, 0u},
};

static uint16_t random_speed_delay_ms(void) {
    return SPEED_LEVELS_MS[get_rand_32() % COPYCOP_SPEED_LEVEL_COUNT];
}

static bool start_afk_text(void) {
    const uint8_t *text;
    size_t text_length;
    return storage_get_text(&text, &text_length)
        && typer_start_random(text, text_length, SPEED_LEVELS_MS,
                              COPYCOP_SPEED_LEVEL_COUNT);
}

static bool colors_equal(copycop_rgb_t left, copycop_rgb_t right) {
    return left.red == right.red
        && left.green == right.green
        && left.blue == right.blue;
}

static copycop_rgb_t select_display_color(copycop_app_mode_t mode,
                                          bool override_active,
                                          copycop_rgb_t override_color) {
    if (override_active) return override_color;
    if (typer_is_active()) return TYPING_COLOR;
    if (mode == COPYCOP_MODE_LOAD && protocol_transfer_active()) return TRANSFER_COLOR;
    if (typer_had_error()) return ERROR_COLOR;
    if (mode == COPYCOP_MODE_LOAD) return LOAD_IDLE_COLOR;
    return mode == COPYCOP_MODE_AFK ? AFK_IDLE_COLOR : TARGET_IDLE_COLOR;
}

int main(void) {
    buttons_init();

    const unsigned int boot_buttons = buttons_boot_pressed_mask();
    const unsigned int all_buttons = (1u << COPYCOP_BUTTON_COUNT) - 1u;
    const unsigned int left_button = 1u << COPYCOP_BUTTON_LEFT;

    if (boot_buttons == all_buttons) {
        status_led_init();
        status_led_show_solid(UPDATE_BOOT_COLOR);
        sleep_ms(COPYCOP_UPDATE_INDICATION_MS);
        reset_usb_boot(0u, 0u);
        while (true) {
            tight_loop_contents();
        }
    }

    copycop_app_mode_t mode = COPYCOP_MODE_TARGET;
    if (boot_buttons == left_button) {
        mode = COPYCOP_MODE_AFK;
    } else if ((boot_buttons & (1u << COPYCOP_BUTTON_MIDDLE)) != 0u) {
        mode = COPYCOP_MODE_LOAD;
    }

    status_led_init();
    copycop_rgb_t boot_color = TARGET_BOOT_COLOR;
    if (mode == COPYCOP_MODE_LOAD) {
        boot_color = LOAD_BOOT_COLOR;
    } else if (mode == COPYCOP_MODE_AFK) {
        boot_color = AFK_BOOT_COLOR;
    }

    status_led_show_solid(boot_color);
    sleep_ms(COPYCOP_BOOT_INDICATION_MS);
    status_led_off();
    sleep_ms(COPYCOP_BOOT_GAP_MS);

    storage_init();
    copycop_usb_init(mode);
    typer_init();
    protocol_init();

    unsigned int speed_index = storage_load_speed_index();
    bool previous_buttons[COPYCOP_BUTTON_COUNT];
    for (unsigned int index = 0; index < COPYCOP_BUTTON_COUNT; ++index) {
        previous_buttons[index] = buttons_is_pressed((copycop_button_t)index);
    }

    bool color_override_active = false;
    copycop_rgb_t color_override = {0u, 0u, 0u};
    absolute_time_t color_override_until = nil_time;

    bool afk_repeat_enabled = false;
    bool afk_one_shot_pending = false;
    bool afk_repeat_waiting = false;
    bool speed_save_pending = false;
    absolute_time_t afk_next_repeat = nil_time;

    copycop_rgb_t displayed = select_display_color(mode, false, color_override);
    status_led_show_solid(displayed);

    watchdog_enable(COPYCOP_WATCHDOG_TIMEOUT_MS, true);

    while (true) {
        copycop_usb_task();
        buttons_poll();

        bool pressed_edges[COPYCOP_BUTTON_COUNT];
        for (unsigned int index = 0; index < COPYCOP_BUTTON_COUNT; ++index) {
            const bool pressed = buttons_is_pressed((copycop_button_t)index);
            pressed_edges[index] = pressed && !previous_buttons[index];
            previous_buttons[index] = pressed;
        }

        if (mode == COPYCOP_MODE_TARGET) {
            if (pressed_edges[COPYCOP_BUTTON_RIGHT]) {
                if (typer_is_active()) {
                    typer_cancel();
                } else {
                    const uint8_t *text;
                    size_t text_length;
                    if (!storage_get_text(&text, &text_length)
                        || !typer_start(text, text_length,
                                        SPEED_LEVELS_MS[speed_index])) {
                        color_override = ERROR_COLOR;
                        color_override_until = make_timeout_time_ms(600u);
                        color_override_active = true;
                    }
                }
            }
            if (pressed_edges[COPYCOP_BUTTON_LEFT]) {
                const unsigned int next_index = speed_index + 1u
                    < COPYCOP_SPEED_LEVEL_COUNT ? speed_index + 1u : speed_index;
                bool saved = true;
                if (next_index != speed_index) {
                    if (typer_is_active()) {
                        speed_index = next_index;
                        typer_set_delay_ms(SPEED_LEVELS_MS[speed_index]);
                        speed_save_pending = true;
                    } else {
                        saved = storage_save_speed_index((uint8_t)next_index);
                        if (saved) speed_index = next_index;
                    }
                }
                color_override = saved ? SPEED_COLORS[speed_index] : ERROR_COLOR;
                color_override_until = make_timeout_time_ms(350u);
                color_override_active = true;
            }
            if (pressed_edges[COPYCOP_BUTTON_MIDDLE]) {
                const unsigned int next_index = speed_index > 0u
                    ? speed_index - 1u : speed_index;
                bool saved = true;
                if (next_index != speed_index) {
                    if (typer_is_active()) {
                        speed_index = next_index;
                        typer_set_delay_ms(SPEED_LEVELS_MS[speed_index]);
                        speed_save_pending = true;
                    } else {
                        saved = storage_save_speed_index((uint8_t)next_index);
                        if (saved) speed_index = next_index;
                    }
                }
                color_override = saved ? SPEED_COLORS[speed_index] : ERROR_COLOR;
                color_override_until = make_timeout_time_ms(350u);
                color_override_active = true;
            }
            typer_task();
            if (speed_save_pending && !typer_is_active()) {
                speed_save_pending = false;
                if (!storage_save_speed_index((uint8_t)speed_index)) {
                    color_override = ERROR_COLOR;
                    color_override_until = make_timeout_time_ms(600u);
                    color_override_active = true;
                }
            }
        } else if (mode == COPYCOP_MODE_LOAD) {
            if (pressed_edges[COPYCOP_BUTTON_MIDDLE]) {
                protocol_notify_copy_pressed();
            }
            protocol_task();
            const copycop_protocol_result_t result = protocol_take_result();
            if (result != COPYCOP_PROTOCOL_RESULT_NONE) {
                color_override = result == COPYCOP_PROTOCOL_RESULT_ERROR
                    ? ERROR_COLOR : SUCCESS_COLOR;
                color_override_until = make_timeout_time_ms(700u);
                color_override_active = true;
            }
        } else {
            if (pressed_edges[COPYCOP_BUTTON_MIDDLE]) {
                afk_repeat_waiting = false;
                if (buttons_is_pressed(COPYCOP_BUTTON_LEFT)) {
                    afk_repeat_enabled = false;
                    if (typer_is_active()) {
                        afk_one_shot_pending = true;
                        typer_cancel();
                    } else {
                        afk_one_shot_pending = false;
                        if (!start_afk_text()) {
                            color_override = ERROR_COLOR;
                            color_override_until = make_timeout_time_ms(600u);
                            color_override_active = true;
                        }
                    }
                } else {
                    afk_one_shot_pending = false;
                    afk_repeat_enabled = true;
                    if (!typer_is_active() && !start_afk_text()) {
                        afk_repeat_enabled = false;
                        color_override = ERROR_COLOR;
                        color_override_until = make_timeout_time_ms(600u);
                        color_override_active = true;
                    }
                }
            }

            if (pressed_edges[COPYCOP_BUTTON_RIGHT]) {
                afk_repeat_enabled = false;
                afk_one_shot_pending = false;
                afk_repeat_waiting = false;
                typer_cancel();
            }

            const bool typer_was_active = typer_is_active();
            typer_task();
            const bool typer_finished = typer_was_active && !typer_is_active();

            if (typer_finished && typer_had_error()) {
                afk_repeat_enabled = false;
                afk_one_shot_pending = false;
                afk_repeat_waiting = false;
            } else if (typer_finished && afk_one_shot_pending) {
                afk_one_shot_pending = false;
                if (!start_afk_text()) {
                    color_override = ERROR_COLOR;
                    color_override_until = make_timeout_time_ms(600u);
                    color_override_active = true;
                }
            } else if (typer_finished && afk_repeat_enabled) {
                afk_repeat_waiting = true;
                afk_next_repeat = make_timeout_time_ms(random_speed_delay_ms());
            }

            if (afk_repeat_enabled && afk_repeat_waiting && !typer_is_active()
                && absolute_time_diff_us(get_absolute_time(), afk_next_repeat) <= 0) {
                afk_repeat_waiting = false;
                if (!start_afk_text()) {
                    afk_repeat_enabled = false;
                    color_override = ERROR_COLOR;
                    color_override_until = make_timeout_time_ms(600u);
                    color_override_active = true;
                }
            }
        }

        if (color_override_active
            && absolute_time_diff_us(get_absolute_time(), color_override_until) <= 0) {
            color_override_active = false;
        }

        const copycop_rgb_t desired = select_display_color(
            mode, color_override_active, color_override);
        if (!colors_equal(desired, displayed)) {
            status_led_show_solid(desired);
            displayed = desired;
        }

        watchdog_update();
        sleep_ms(COPYCOP_MAIN_LOOP_PERIOD_MS);
    }
}
