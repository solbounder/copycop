#pragma once

#include <stdbool.h>

typedef enum copycop_button {
    COPYCOP_BUTTON_LEFT = 0,
    COPYCOP_BUTTON_MIDDLE = 1,
    COPYCOP_BUTTON_RIGHT = 2,
    COPYCOP_BUTTON_COUNT = 3,
} copycop_button_t;

void buttons_init(void);

/* Called before USB initialization; returns a bit per stably held button. */
unsigned int buttons_boot_pressed_mask(void);

/* Call at COPYCOP_MAIN_LOOP_PERIOD_MS. */
void buttons_poll(void);

bool buttons_is_pressed(copycop_button_t button);
unsigned int buttons_pressed_count(void);
