#pragma once

#include <stdint.h>

/*
 * Waveshare RP2040-Keyboard-3 hardware configuration.
 *
 * Sources checked 2026-08-31:
 *   - Waveshare schematic dated 2024-02-29
 *   - Waveshare RP2040-Keyboard-3 Arduino demo
 *
 * Keep board-specific electrical, timing, and physical flash values here.
 */

#define COPYCOP_BUTTON_LEFT_GPIO          14u
#define COPYCOP_BUTTON_MIDDLE_GPIO        13u
#define COPYCOP_BUTTON_RIGHT_GPIO         12u
#define COPYCOP_BUTTON_ACTIVE_LEVEL       0u

#define COPYCOP_BUTTON_DEBOUNCE_SAMPLES   5u
#define COPYCOP_BOOT_SAMPLE_COUNT         8u
#define COPYCOP_BOOT_SAMPLE_INTERVAL_MS   2u
#define COPYCOP_BOOT_PRESSED_THRESHOLD    6u

#define COPYCOP_RGB_GPIO                  18u
#define COPYCOP_RGB_LED_COUNT             3u
#define COPYCOP_RGB_FREQUENCY_HZ          800000u
#define COPYCOP_RGB_RESET_TIME_US         80u

/* Verified on the production board: wire bytes are red, green, blue. */
#define COPYCOP_RGB_WIRE_ORDER_RGB         1u

/* Schematic chain: GP18 -> WS_L11 -> WS_L21 -> WS_L31. */
#define COPYCOP_RGB_CHAIN_WS_L11_INDEX    0u
#define COPYCOP_RGB_CHAIN_WS_L21_INDEX    1u
#define COPYCOP_RGB_CHAIN_WS_L31_INDEX    2u

/* Winbond W25Q16JVUXIQ on the official schematic. */
#define COPYCOP_FLASH_SIZE_BYTES          UINT32_C(0x00200000)
#define COPYCOP_FIRMWARE_REGION_BYTES     UINT32_C(0x00100000)
#define COPYCOP_STORAGE_REGION_BYTES      UINT32_C(0x00100000)
#define COPYCOP_FLASH_ERASE_BYTES         UINT32_C(4096)
#define COPYCOP_FLASH_PROGRAM_BYTES       UINT32_C(256)

#define COPYCOP_SETTINGS_OFFSET           UINT32_C(0x00100000)
#define COPYCOP_SETTINGS_BYTES            UINT32_C(0x00008000)
#define COPYCOP_SLOT_1_OFFSET             UINT32_C(0x00108000)
#define COPYCOP_SLOT_BYTES                UINT32_C(0x0003E000)
#define COPYCOP_SLOT_BANK_BYTES           UINT32_C(0x0001F000)
#define COPYCOP_SLOT_PAYLOAD_OFFSET        UINT32_C(0x00000100)
#define COPYCOP_SLOT_COMMIT_OFFSET         UINT32_C(0x0001EF00)
#define COPYCOP_SLOT_MAX_PAYLOAD_BYTES     UINT32_C(0x0001EE00)

#define COPYCOP_BOOT_INDICATION_MS        400u
#define COPYCOP_BOOT_GAP_MS               100u
#define COPYCOP_UPDATE_INDICATION_MS      300u
#define COPYCOP_MAIN_LOOP_PERIOD_MS       1u
#define COPYCOP_WATCHDOG_TIMEOUT_MS       8000u

/* Prototype-only TinyUSB VID and two deliberately distinct product IDs. */
#define COPYCOP_USB_VID                   UINT16_C(0xCAFE)
#define COPYCOP_USB_PID_TARGET            UINT16_C(0x4030)
#define COPYCOP_USB_PID_LOAD              UINT16_C(0x4031)
#define COPYCOP_USB_BCD_DEVICE            UINT16_C(0x0200)
#define COPYCOP_USB_ENDPOINT_SIZE         64u
#define COPYCOP_USB_POLL_INTERVAL_MS      1u
#define COPYCOP_PROTOCOL_VERSION          1u
#define COPYCOP_PROTOCOL_MAGIC            UINT8_C(0xC3)
#define COPYCOP_PROTOCOL_PAYLOAD_BYTES    40u

#define COPYCOP_SPEED_LEVEL_COUNT         8u
#define COPYCOP_DEFAULT_SPEED_INDEX       0u
#define COPYCOP_KEY_HOLD_MS               1u

#if COPYCOP_BOOT_PRESSED_THRESHOLD > COPYCOP_BOOT_SAMPLE_COUNT
#error "Boot pressed threshold cannot exceed the sample count"
#endif

#if (COPYCOP_FIRMWARE_REGION_BYTES + COPYCOP_STORAGE_REGION_BYTES) != COPYCOP_FLASH_SIZE_BYTES
#error "Firmware and storage regions must cover the physical flash exactly"
#endif
