#pragma once

#include <stdint.h>

typedef struct copycop_rgb {
    uint8_t red;
    uint8_t green;
    uint8_t blue;
} copycop_rgb_t;

void status_led_init(void);
void status_led_show_solid(copycop_rgb_t color);
void status_led_off(void);

