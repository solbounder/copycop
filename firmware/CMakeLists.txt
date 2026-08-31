cmake_minimum_required(VERSION 3.20)

set(PICO_BOARD pico CACHE STRING "Pico SDK board baseline for RP2040 + 2 MiB flash")

if(DEFINED PICO_SDK_PATH)
    set(_copycop_pico_sdk_path "${PICO_SDK_PATH}")
elseif(DEFINED ENV{PICO_SDK_PATH})
    set(_copycop_pico_sdk_path "$ENV{PICO_SDK_PATH}")
else()
    message(FATAL_ERROR
        "PICO_SDK_PATH is not set. Point it at a Raspberry Pi Pico SDK checkout.")
endif()

file(TO_CMAKE_PATH "${_copycop_pico_sdk_path}" _copycop_pico_sdk_path)
set(PICO_SDK_PATH "${_copycop_pico_sdk_path}" CACHE PATH "Pico SDK path" FORCE)
include("${PICO_SDK_PATH}/external/pico_sdk_import.cmake")

project(copycop_firmware VERSION 0.3.0 LANGUAGES C CXX ASM)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

pico_sdk_init()

add_executable(copycop_firmware
    src/main.c
    src/buttons.c
    src/crc32.c
    src/keyboard_layout_de.c
    src/protocol.c
    src/status_led.c
    src/storage.c
    src/typer.c
    src/usb_descriptors.c
    src/usb_device.c
)

pico_generate_pio_header(copycop_firmware
    ${CMAKE_CURRENT_LIST_DIR}/src/ws2812.pio
)

target_include_directories(copycop_firmware PRIVATE
    ${CMAKE_CURRENT_LIST_DIR}/include
)

target_link_libraries(copycop_firmware PRIVATE
    pico_stdlib
    hardware_clocks
    hardware_flash
    hardware_pio
    hardware_watchdog
    pico_flash
    pico_rand
    pico_unique_id
    tinyusb_device
)

target_compile_definitions(copycop_firmware PRIVATE
    COPYCOP_FIRMWARE_VERSION_MAJOR=${PROJECT_VERSION_MAJOR}
    COPYCOP_FIRMWARE_VERSION_MINOR=${PROJECT_VERSION_MINOR}
    COPYCOP_FIRMWARE_VERSION_PATCH=${PROJECT_VERSION_PATCH}
    COPYCOP_IMPLEMENTATION_PHASE=7
)

set_source_files_properties(
    src/main.c
    src/buttons.c
    src/crc32.c
    src/keyboard_layout_de.c
    src/protocol.c
    src/status_led.c
    src/storage.c
    src/typer.c
    src/usb_descriptors.c
    src/usb_device.c
    PROPERTIES COMPILE_OPTIONS "-Wall;-Wextra"
)

# Console stdio must never add CDC to either USB personality.
pico_enable_stdio_usb(copycop_firmware 0)
pico_enable_stdio_uart(copycop_firmware 0)

# The upper half of the physical 2 MiB flash is never linkable firmware.
pico_add_linker_script_override_path(copycop_firmware
    ${CMAKE_CURRENT_LIST_DIR}/linker
    FILES pico_flash_region.ld
)

pico_add_extra_outputs(copycop_firmware)
