#include "storage.h"

#include <stddef.h>
#include <stdint.h>
#include <string.h>

#include "board_config.h"
#include "crc32.h"
#include "hardware/flash.h"
#include "hardware/watchdog.h"
#include "pico/flash.h"
#include "pico/platform.h"
#include "pico/stdlib.h"

#define STORAGE_HEADER_MAGIC UINT32_C(0x48435043)
#define STORAGE_COMMIT_MAGIC UINT32_C(0x4D435043)
#define STORAGE_SETTINGS_MAGIC UINT32_C(0x53435043)
#define STORAGE_RECORD_SCHEMA_VERSION UINT16_C(1)
#define SETTINGS_SCHEMA_VERSION UINT16_C(2)
#define SETTINGS_PAGE_COUNT (COPYCOP_SETTINGS_BYTES / COPYCOP_FLASH_PROGRAM_BYTES)

typedef struct __attribute__((packed)) storage_header {
    uint32_t magic;
    uint16_t version;
    uint16_t header_size;
    uint32_t slot;
    uint32_t generation;
    uint32_t length;
    uint32_t payload_crc;
    uint32_t header_crc;
    uint8_t reserved[228];
} storage_header_t;

typedef struct __attribute__((packed)) storage_commit {
    uint32_t magic;
    uint16_t version;
    uint16_t reserved0;
    uint32_t generation;
    uint32_t header_crc;
    uint32_t payload_crc;
    uint32_t length;
    uint8_t reserved[232];
} storage_commit_t;

typedef struct __attribute__((packed)) settings_record {
    uint32_t magic;
    uint16_t version;
    uint8_t speed_index;
    uint8_t reserved0;
    uint32_t generation;
    uint32_t crc;
    uint8_t reserved[240];
} settings_record_t;

_Static_assert(sizeof(storage_header_t) == COPYCOP_FLASH_PROGRAM_BYTES,
               "storage header must occupy one flash page");
_Static_assert(sizeof(storage_commit_t) == COPYCOP_FLASH_PROGRAM_BYTES,
               "storage commit must occupy one flash page");
_Static_assert(sizeof(settings_record_t) == COPYCOP_FLASH_PROGRAM_BYTES,
               "settings record must occupy one flash page");
_Static_assert(COPYCOP_SLOT_1_OFFSET + 4u * COPYCOP_SLOT_BYTES == COPYCOP_FLASH_SIZE_BYTES,
               "four slots must end at physical flash end");

typedef enum flash_operation_kind {
    FLASH_OPERATION_ERASE,
    FLASH_OPERATION_PROGRAM,
} flash_operation_kind_t;

typedef struct flash_operation {
    flash_operation_kind_t kind;
    uint32_t offset;
    uint8_t *data;
    size_t length;
} flash_operation_t;

static bool active_valid;
static unsigned int active_bank;
static storage_header_t active_header;
static uint8_t settings_speed_index;
static uint32_t settings_generation;

static const uint8_t *flash_pointer(uint32_t offset) {
    return (const uint8_t *)(XIP_BASE + offset);
}

static uint32_t bank_offset(unsigned int bank) {
    return COPYCOP_SLOT_1_OFFSET + bank * COPYCOP_SLOT_BANK_BYTES;
}

static bool generation_newer(uint32_t candidate, uint32_t reference) {
    return (int32_t)(candidate - reference) > 0;
}

static void __not_in_flash_func(run_flash_operation)(void *parameter) {
    flash_operation_t *operation = (flash_operation_t *)parameter;
    if (operation->kind == FLASH_OPERATION_ERASE) {
        flash_range_erase(operation->offset, operation->length);
    } else {
        flash_range_program(operation->offset, operation->data, operation->length);
    }
}

static bool execute_flash(flash_operation_kind_t kind, uint32_t offset,
                          const void *data, size_t length) {
    flash_operation_t operation = {
        .kind = kind,
        .offset = offset,
        .data = (uint8_t *)data,
        .length = length,
    };
    watchdog_update();
    const int result = flash_safe_execute(run_flash_operation, &operation, 1000u);
    watchdog_update();
    return result == PICO_OK;
}

static bool validate_bank(unsigned int bank, storage_header_t *header_out) {
    storage_header_t header;
    storage_commit_t commit;
    const uint32_t start = bank_offset(bank);
    memcpy(&header, flash_pointer(start), sizeof(header));
    memcpy(&commit, flash_pointer(start + COPYCOP_SLOT_COMMIT_OFFSET), sizeof(commit));

    if (header.magic != STORAGE_HEADER_MAGIC
        || header.version != STORAGE_RECORD_SCHEMA_VERSION
        || header.header_size != sizeof(storage_header_t)
        || header.slot != 0u
        || header.length > COPYCOP_SLOT_MAX_PAYLOAD_BYTES
        || header.header_crc != copycop_crc32(&header, offsetof(storage_header_t, header_crc))) {
        return false;
    }
    if (commit.magic != STORAGE_COMMIT_MAGIC
        || commit.version != STORAGE_RECORD_SCHEMA_VERSION
        || commit.generation != header.generation
        || commit.header_crc != header.header_crc
        || commit.payload_crc != header.payload_crc
        || commit.length != header.length) {
        return false;
    }

    const uint8_t *payload = flash_pointer(start + COPYCOP_SLOT_PAYLOAD_OFFSET);
    if (copycop_crc32(payload, header.length) != header.payload_crc) return false;
    *header_out = header;
    return true;
}

static bool settings_valid(const settings_record_t *record) {
    return record->magic == STORAGE_SETTINGS_MAGIC
        && record->version == SETTINGS_SCHEMA_VERSION
        && record->speed_index < COPYCOP_SPEED_LEVEL_COUNT
        && record->crc == copycop_crc32(record, offsetof(settings_record_t, crc));
}

void storage_init(void) {
    storage_header_t headers[2];
    const bool valid_a = validate_bank(0u, &headers[0]);
    const bool valid_b = validate_bank(1u, &headers[1]);

    active_valid = valid_a || valid_b;
    if (valid_a && valid_b) {
        active_bank = generation_newer(headers[1].generation, headers[0].generation) ? 1u : 0u;
    } else {
        active_bank = valid_b ? 1u : 0u;
    }
    if (active_valid) active_header = headers[active_bank];

    settings_speed_index = COPYCOP_DEFAULT_SPEED_INDEX;
    settings_generation = 0u;
    for (uint32_t page = 0u; page < SETTINGS_PAGE_COUNT; ++page) {
        settings_record_t record;
        const uint32_t offset = COPYCOP_SETTINGS_OFFSET
            + page * COPYCOP_FLASH_PROGRAM_BYTES;
        memcpy(&record, flash_pointer(offset), sizeof(record));
        if (settings_valid(&record)
            && (settings_generation == 0u
                || generation_newer(record.generation, settings_generation))) {
            settings_generation = record.generation;
            settings_speed_index = record.speed_index;
        }
    }
}

bool storage_get_text(const uint8_t **utf8, size_t *length) {
    if (utf8 == NULL || length == NULL || !active_valid || active_header.length == 0u) {
        return false;
    }
    *utf8 = flash_pointer(bank_offset(active_bank) + COPYCOP_SLOT_PAYLOAD_OFFSET);
    *length = active_header.length;
    return true;
}

uint32_t storage_text_crc(void) {
    return active_valid ? active_header.payload_crc : 0u;
}

uint32_t storage_generation(void) {
    return active_valid ? active_header.generation : 0u;
}

size_t storage_max_text_bytes(void) {
    return COPYCOP_SLOT_MAX_PAYLOAD_BYTES;
}

bool storage_commit_text(const uint8_t *utf8, size_t length, uint32_t expected_crc) {
    if ((utf8 == NULL && length != 0u) || length > COPYCOP_SLOT_MAX_PAYLOAD_BYTES
        || copycop_crc32(utf8, length) != expected_crc) {
        return false;
    }

    const unsigned int target_bank = active_valid ? 1u - active_bank : 0u;
    const uint32_t start = bank_offset(target_bank);
    const uint32_t generation = active_valid ? active_header.generation + 1u : 1u;

    storage_header_t header;
    memset(&header, 0xFF, sizeof(header));
    header.magic = STORAGE_HEADER_MAGIC;
    header.version = STORAGE_RECORD_SCHEMA_VERSION;
    header.header_size = sizeof(header);
    header.slot = 0u;
    header.generation = generation;
    header.length = (uint32_t)length;
    header.payload_crc = expected_crc;
    header.header_crc = copycop_crc32(&header, offsetof(storage_header_t, header_crc));

    storage_commit_t commit;
    memset(&commit, 0xFF, sizeof(commit));
    commit.magic = STORAGE_COMMIT_MAGIC;
    commit.version = STORAGE_RECORD_SCHEMA_VERSION;
    commit.generation = generation;
    commit.header_crc = header.header_crc;
    commit.payload_crc = expected_crc;
    commit.length = (uint32_t)length;

    if (!execute_flash(FLASH_OPERATION_ERASE, start, NULL, COPYCOP_SLOT_BANK_BYTES)
        || !execute_flash(FLASH_OPERATION_PROGRAM, start, &header, sizeof(header))) {
        return false;
    }

    uint8_t page_buffer[COPYCOP_FLASH_PROGRAM_BYTES];
    size_t written = 0u;
    while (written < length) {
        const size_t remaining = length - written;
        const size_t chunk = remaining < sizeof(page_buffer) ? remaining : sizeof(page_buffer);
        memset(page_buffer, 0xFF, sizeof(page_buffer));
        memcpy(page_buffer, utf8 + written, chunk);
        if (!execute_flash(FLASH_OPERATION_PROGRAM,
                           start + COPYCOP_SLOT_PAYLOAD_OFFSET + (uint32_t)written,
                           page_buffer, sizeof(page_buffer))) {
            return false;
        }
        written += chunk;
    }

    if (copycop_crc32(flash_pointer(start + COPYCOP_SLOT_PAYLOAD_OFFSET), length)
            != expected_crc
        || !execute_flash(FLASH_OPERATION_PROGRAM,
                          start + COPYCOP_SLOT_COMMIT_OFFSET,
                          &commit, sizeof(commit))) {
        return false;
    }

    storage_header_t verified;
    if (!validate_bank(target_bank, &verified)) return false;
    active_bank = target_bank;
    active_header = verified;
    active_valid = true;
    return true;
}

bool storage_clear_text(void) {
    return storage_commit_text(NULL, 0u, copycop_crc32(NULL, 0u));
}

uint8_t storage_load_speed_index(void) {
    return settings_speed_index;
}

static bool page_erased(uint32_t offset) {
    const uint32_t *words = (const uint32_t *)flash_pointer(offset);
    for (size_t index = 0u; index < COPYCOP_FLASH_PROGRAM_BYTES / sizeof(uint32_t); ++index) {
        if (words[index] != UINT32_MAX) return false;
    }
    return true;
}

bool storage_save_speed_index(uint8_t speed_index) {
    if (speed_index >= COPYCOP_SPEED_LEVEL_COUNT) return false;

    uint32_t write_offset = UINT32_MAX;
    for (uint32_t page = 0u; page < SETTINGS_PAGE_COUNT; ++page) {
        const uint32_t offset = COPYCOP_SETTINGS_OFFSET
            + page * COPYCOP_FLASH_PROGRAM_BYTES;
        if (page_erased(offset)) {
            write_offset = offset;
            break;
        }
    }

    if (write_offset == UINT32_MAX) {
        uint32_t oldest_sector = 0u;
        uint32_t oldest_generation = UINT32_MAX;
        const uint32_t pages_per_sector = COPYCOP_FLASH_ERASE_BYTES / COPYCOP_FLASH_PROGRAM_BYTES;
        const uint32_t sector_count = COPYCOP_SETTINGS_BYTES / COPYCOP_FLASH_ERASE_BYTES;
        for (uint32_t sector = 0u; sector < sector_count; ++sector) {
            uint32_t newest_in_sector = 0u;
            for (uint32_t page = 0u; page < pages_per_sector; ++page) {
                settings_record_t record;
                const uint32_t offset = COPYCOP_SETTINGS_OFFSET
                    + sector * COPYCOP_FLASH_ERASE_BYTES
                    + page * COPYCOP_FLASH_PROGRAM_BYTES;
                memcpy(&record, flash_pointer(offset), sizeof(record));
                if (settings_valid(&record)
                    && generation_newer(record.generation, newest_in_sector)) {
                    newest_in_sector = record.generation;
                }
            }
            if (newest_in_sector < oldest_generation) {
                oldest_generation = newest_in_sector;
                oldest_sector = sector;
            }
        }
        write_offset = COPYCOP_SETTINGS_OFFSET + oldest_sector * COPYCOP_FLASH_ERASE_BYTES;
        if (!execute_flash(FLASH_OPERATION_ERASE, write_offset, NULL,
                           COPYCOP_FLASH_ERASE_BYTES)) {
            return false;
        }
    }

    settings_record_t record;
    memset(&record, 0xFF, sizeof(record));
    record.magic = STORAGE_SETTINGS_MAGIC;
    record.version = SETTINGS_SCHEMA_VERSION;
    record.speed_index = speed_index;
    record.generation = settings_generation + 1u;
    record.crc = copycop_crc32(&record, offsetof(settings_record_t, crc));
    if (!execute_flash(FLASH_OPERATION_PROGRAM, write_offset, &record, sizeof(record))) {
        return false;
    }

    settings_speed_index = speed_index;
    settings_generation = record.generation;
    return true;
}
