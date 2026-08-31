#include "buttons.h"

#include <stddef.h>
#include <stdint.h>

#include "board_config.h"
#include "hardware/gpio.h"
#include "pico/time.h"

static const uint BUTTON_GPIOS[COPYCOP_BUTTON_COUNT] = {
    [COPYCOP_BUTTON_LEFT] = COPYCOP_BUTTON_LEFT_GPIO,
    [COPYCOP_BUTTON_MIDDLE] = COPYCOP_BUTTON_MIDDLE_GPIO,
    [COPYCOP_BUTTON_RIGHT] = COPYCOP_BUTTON_RIGHT_GPIO,
};

static uint8_t debounce_integrator[COPYCOP_BUTTON_COUNT];
static bool stable_pressed[COPYCOP_BUTTON_COUNT];

static bool read_pressed(copycop_button_t button) {
    const bool level = gpio_get(BUTTON_GPIOS[button]);
    return level == (COPYCOP_BUTTON_ACTIVE_LEVEL != 0u);
}

void buttons_init(void) {
    for (size_t index = 0; index < COPYCOP_BUTTON_COUNT; ++index) {
        const uint gpio = BUTTON_GPIOS[index];
        gpio_init(gpio);
        gpio_set_dir(gpio, GPIO_IN);
        gpio_pull_up(gpio);

        const bool pressed = read_pressed((copycop_button_t)index);
        stable_pressed[index] = pressed;
        debounce_integrator[index] = pressed ? COPYCOP_BUTTON_DEBOUNCE_SAMPLES : 0u;
    }
}

unsigned int buttons_boot_pressed_mask(void) {
    unsigned int pressed_samples[COPYCOP_BUTTON_COUNT] = {0u, 0u, 0u};

    for (unsigned int sample = 0; sample < COPYCOP_BOOT_SAMPLE_COUNT; ++sample) {
        for (size_t index = 0; index < COPYCOP_BUTTON_COUNT; ++index) {
            if (read_pressed((copycop_button_t)index)) {
                ++pressed_samples[index];
            }
        }
        sleep_ms(COPYCOP_BOOT_SAMPLE_INTERVAL_MS);
    }

    unsigned int mask = 0u;
    for (size_t index = 0; index < COPYCOP_BUTTON_COUNT; ++index) {
        if (pressed_samples[index] >= COPYCOP_BOOT_PRESSED_THRESHOLD) {
            mask |= 1u << index;
        }
    }
    return mask;
}

void buttons_poll(void) {
    for (size_t index = 0; index < COPYCOP_BUTTON_COUNT; ++index) {
        const bool pressed = read_pressed((copycop_button_t)index);
        uint8_t value = debounce_integrator[index];

        if (pressed) {
            if (value < COPYCOP_BUTTON_DEBOUNCE_SAMPLES) {
                ++value;
            }
        } else if (value > 0u) {
            --value;
        }

        debounce_integrator[index] = value;
        if (value == COPYCOP_BUTTON_DEBOUNCE_SAMPLES) {
            stable_pressed[index] = true;
        } else if (value == 0u) {
            stable_pressed[index] = false;
        }
    }
}

bool buttons_is_pressed(copycop_button_t button) {
    if ((unsigned int)button >= COPYCOP_BUTTON_COUNT) {
        return false;
    }
    return stable_pressed[button];
}

unsigned int buttons_pressed_count(void) {
    unsigned int count = 0u;
    for (size_t index = 0; index < COPYCOP_BUTTON_COUNT; ++index) {
        if (stable_pressed[index]) {
            ++count;
        }
    }
    return count;
}
