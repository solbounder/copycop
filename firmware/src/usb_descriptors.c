#include <stddef.h>
#include <string.h>

#include "app_mode.h"
#include "board_config.h"
#include "pico/unique_id.h"
#include "tusb.h"

static copycop_app_mode_t descriptor_mode = COPYCOP_MODE_TARGET;

void copycop_usb_descriptors_set_mode(copycop_app_mode_t mode) {
    descriptor_mode = mode;
}

static tusb_desc_device_t descriptor_device = {
    .bLength = sizeof(tusb_desc_device_t),
    .bDescriptorType = TUSB_DESC_DEVICE,
    .bcdUSB = 0x0200,
    .bDeviceClass = 0x00,
    .bDeviceSubClass = 0x00,
    .bDeviceProtocol = 0x00,
    .bMaxPacketSize0 = CFG_TUD_ENDPOINT0_SIZE,
    .idVendor = COPYCOP_USB_VID,
    .idProduct = COPYCOP_USB_PID_TARGET,
    .bcdDevice = COPYCOP_USB_BCD_DEVICE,
    .iManufacturer = 1,
    .iProduct = 2,
    .iSerialNumber = 3,
    .bNumConfigurations = 1,
};

uint8_t const *tud_descriptor_device_cb(void) {
    descriptor_device.idProduct = descriptor_mode == COPYCOP_MODE_LOAD
        ? COPYCOP_USB_PID_LOAD
        : COPYCOP_USB_PID_TARGET;
    return (uint8_t const *)&descriptor_device;
}

static uint8_t const target_report_descriptor[] = {
    TUD_HID_REPORT_DESC_KEYBOARD()
};

static uint8_t const load_report_descriptor[] = {
    TUD_HID_REPORT_DESC_GENERIC_INOUT(COPYCOP_USB_ENDPOINT_SIZE)
};

uint8_t const *tud_hid_descriptor_report_cb(uint8_t instance) {
    (void)instance;
    return descriptor_mode == COPYCOP_MODE_LOAD
        ? load_report_descriptor
        : target_report_descriptor;
}

enum {
    INTERFACE_HID = 0,
    INTERFACE_COUNT = 1,
};

#define TARGET_CONFIGURATION_LENGTH (TUD_CONFIG_DESC_LEN + TUD_HID_DESC_LEN)
#define LOAD_CONFIGURATION_LENGTH   (TUD_CONFIG_DESC_LEN + TUD_HID_INOUT_DESC_LEN)
#define ENDPOINT_HID                 1

static uint8_t const target_configuration[] = {
    TUD_CONFIG_DESCRIPTOR(1, INTERFACE_COUNT, 0, TARGET_CONFIGURATION_LENGTH,
                          TUSB_DESC_CONFIG_ATT_REMOTE_WAKEUP, 100),
    TUD_HID_DESCRIPTOR(INTERFACE_HID, 0, HID_ITF_PROTOCOL_KEYBOARD,
                       sizeof(target_report_descriptor),
                       0x80 | ENDPOINT_HID, COPYCOP_USB_ENDPOINT_SIZE,
                       COPYCOP_USB_POLL_INTERVAL_MS),
};

static uint8_t const load_configuration[] = {
    TUD_CONFIG_DESCRIPTOR(1, INTERFACE_COUNT, 0, LOAD_CONFIGURATION_LENGTH,
                          0x00, 100),
    TUD_HID_INOUT_DESCRIPTOR(INTERFACE_HID, 0, HID_ITF_PROTOCOL_NONE,
                             sizeof(load_report_descriptor), ENDPOINT_HID,
                             0x80 | ENDPOINT_HID, COPYCOP_USB_ENDPOINT_SIZE,
                             COPYCOP_USB_POLL_INTERVAL_MS),
};

uint8_t const *tud_descriptor_configuration_cb(uint8_t index) {
    (void)index;
    return descriptor_mode == COPYCOP_MODE_LOAD
        ? load_configuration
        : target_configuration;
}

static uint16_t string_descriptor[33];

static size_t copy_ascii_to_utf16(const char *text) {
    size_t count = strlen(text);
    if (count > 32u) count = 32u;
    for (size_t index = 0; index < count; ++index) {
        string_descriptor[index + 1u] = (uint8_t)text[index];
    }
    return count;
}

uint16_t const *tud_descriptor_string_cb(uint8_t index, uint16_t language_id) {
    (void)language_id;
    size_t count;

    if (index == 0u) {
        string_descriptor[1] = 0x0409;
        count = 1u;
    } else if (index == 1u) {
        count = copy_ascii_to_utf16("CopyCop");
    } else if (index == 2u) {
        count = copy_ascii_to_utf16(descriptor_mode == COPYCOP_MODE_LOAD
            ? "CopyCop Clipboard Loader"
            : "CopyCop Keyboard");
    } else if (index == 3u) {
        char serial[PICO_UNIQUE_BOARD_ID_SIZE_BYTES * 2u + 1u];
        pico_get_unique_board_id_string(serial, sizeof(serial));
        count = copy_ascii_to_utf16(serial);
    } else {
        return NULL;
    }

    string_descriptor[0] = (uint16_t)((TUSB_DESC_STRING << 8u) | (2u * count + 2u));
    return string_descriptor;
}

