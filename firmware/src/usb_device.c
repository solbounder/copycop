#include "usb_device.h"

#include <string.h>

#include "board_config.h"
#include "tusb.h"

static copycop_app_mode_t active_mode;
static bool mounted;

void copycop_usb_descriptors_set_mode(copycop_app_mode_t mode);

void copycop_usb_init(copycop_app_mode_t mode) {
    active_mode = mode;
    mounted = false;
    copycop_usb_descriptors_set_mode(mode);

    tusb_rhport_init_t init = {
        .role = TUSB_ROLE_DEVICE,
        .speed = TUSB_SPEED_AUTO,
    };
    tusb_init(0, &init);
}

void copycop_usb_task(void) {
    tud_task();
}

bool copycop_usb_mounted(void) {
    return mounted;
}

copycop_app_mode_t copycop_usb_mode(void) {
    return active_mode;
}

bool copycop_usb_keyboard_press(uint8_t modifiers, uint8_t keycode) {
    if (active_mode == COPYCOP_MODE_LOAD || !tud_hid_ready()) return false;
    uint8_t keys[6] = {keycode, 0u, 0u, 0u, 0u, 0u};
    return tud_hid_keyboard_report(0u, modifiers, keys);
}

bool copycop_usb_keyboard_release_all(void) {
    if (active_mode == COPYCOP_MODE_LOAD || !tud_hid_ready()) return false;
    return tud_hid_keyboard_report(0u, 0u, NULL);
}

bool copycop_usb_load_send(const uint8_t *report, size_t length) {
    if (active_mode != COPYCOP_MODE_LOAD || report == NULL
        || length != COPYCOP_USB_ENDPOINT_SIZE || !tud_hid_ready()) {
        return false;
    }
    return tud_hid_report(0u, report, (uint16_t)length);
}

__attribute__((weak)) void copycop_usb_load_report_received(const uint8_t *report,
                                                            size_t length) {
    (void)report;
    (void)length;
}

void tud_mount_cb(void) {
    mounted = true;
}

void tud_umount_cb(void) {
    mounted = false;
}

void tud_suspend_cb(bool remote_wakeup_enabled) {
    (void)remote_wakeup_enabled;
}

void tud_resume_cb(void) {
}

uint16_t tud_hid_get_report_cb(uint8_t instance, uint8_t report_id,
                               hid_report_type_t report_type, uint8_t *buffer,
                               uint16_t requested_length) {
    (void)instance;
    (void)report_id;
    (void)report_type;
    (void)buffer;
    (void)requested_length;
    return 0u;
}

void tud_hid_set_report_cb(uint8_t instance, uint8_t report_id,
                           hid_report_type_t report_type,
                           uint8_t const *buffer, uint16_t buffer_size) {
    (void)instance;
    (void)report_id;
    (void)report_type;
    if (active_mode == COPYCOP_MODE_LOAD && buffer != NULL
        && buffer_size == COPYCOP_USB_ENDPOINT_SIZE) {
        copycop_usb_load_report_received(buffer, buffer_size);
    }
}
