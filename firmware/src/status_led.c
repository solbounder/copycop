#include "status_led.h"

#include <stdint.h>

#include "board_config.h"
#include "hardware/clocks.h"
#include "hardware/pio.h"
#include "pico/stdlib.h"
#include "ws2812.pio.h"

static PIO led_pio;
static uint led_state_machine;

static void ws2812_program_init(PIO pio, uint state_machine, uint offset,
                                uint pin, float frequency_hz) {
    pio_sm_config config = ws2812_program_get_default_config(offset);

    sm_config_set_sideset_pins(&config, pin);
    sm_config_set_out_shift(&config, false, true, 24u);
    sm_config_set_fifo_join(&config, PIO_FIFO_JOIN_TX);

    const float cycles_per_bit = (float)(ws2812_T1 + ws2812_T2 + ws2812_T3);
    const float divider = (float)clock_get_hz(clk_sys) / (frequency_hz * cycles_per_bit);
    sm_config_set_clkdiv(&config, divider);

    pio_gpio_init(pio, pin);
    pio_sm_set_consecutive_pindirs(pio, state_machine, pin, 1u, true);
    pio_sm_init(pio, state_machine, offset, &config);
    pio_sm_set_enabled(pio, state_machine, true);
}

static uint32_t pack_wire_rgb(copycop_rgb_t color) {
    const uint32_t rgb = ((uint32_t)color.red << 16u)
                       | ((uint32_t)color.green << 8u)
                       | (uint32_t)color.blue;
    return rgb << 8u;
}

void status_led_init(void) {
    led_pio = pio0;
    led_state_machine = (uint)pio_claim_unused_sm(led_pio, true);
    const uint offset = pio_add_program(led_pio, &ws2812_program);

    ws2812_program_init(led_pio, led_state_machine, offset,
                        COPYCOP_RGB_GPIO, (float)COPYCOP_RGB_FREQUENCY_HZ);
    status_led_off();
}

void status_led_show_solid(copycop_rgb_t color) {
    const uint32_t packed = pack_wire_rgb(color);
    for (unsigned int index = 0; index < COPYCOP_RGB_LED_COUNT; ++index) {
        pio_sm_put_blocking(led_pio, led_state_machine, packed);
    }

    while (!pio_sm_is_tx_fifo_empty(led_pio, led_state_machine)) {
        tight_loop_contents();
    }
    sleep_us(COPYCOP_RGB_RESET_TIME_US);
}

void status_led_off(void) {
    status_led_show_solid((copycop_rgb_t){0u, 0u, 0u});
}
