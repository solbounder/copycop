#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "app_mode.h"

void copycop_usb_init(copycop_app_mode_t mode);
void copycop_usb_task(void);
bool copycop_usb_mounted(void);
copycop_app_mode_t copycop_usb_mode(void);

bool copycop_usb_keyboard_press(uint8_t modifiers, uint8_t keycode);
bool copycop_usb_keyboard_release_all(void);

/* LOAD-mode hooks are implemented by the protocol module in Phase 3. */
void copycop_usb_load_report_received(const uint8_t *report, size_t length);
bool copycop_usb_load_send(const uint8_t *report, size_t length);

