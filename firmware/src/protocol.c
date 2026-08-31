#include "protocol.h"

#include <stddef.h>
#include <stdint.h>
#include <string.h>

#include "board_config.h"
#include "crc32.h"
#include "storage.h"
#include "usb_device.h"

typedef enum protocol_type {
    TYPE_HELLO = 0x01,
    TYPE_GET_INFO = 0x02,
    TYPE_BEGIN_TRANSFER = 0x10,
    TYPE_DATA = 0x11,
    TYPE_END_TRANSFER = 0x12,
    TYPE_GET_STATUS = 0x20,
    TYPE_CLEAR = 0x21,
    TYPE_COPY_EVENT = 0x40,
    TYPE_RESPONSE_MASK = 0x80,
} protocol_type_t;

typedef enum protocol_status {
    STATUS_OK = 0,
    STATUS_BAD_FRAME = 1,
    STATUS_BAD_VERSION = 2,
    STATUS_BAD_STATE = 3,
    STATUS_BAD_LENGTH = 4,
    STATUS_BAD_CRC = 5,
    STATUS_BAD_OFFSET = 6,
    STATUS_STORAGE_ERROR = 7,
    STATUS_BUSY = 8,
} protocol_status_t;

typedef struct __attribute__((packed)) protocol_frame {
    uint8_t magic;
    uint8_t version;
    uint8_t type;
    uint8_t status;
    uint32_t sequence;
    uint32_t argument0;
    uint32_t argument1;
    uint16_t payload_length;
    uint16_t reserved;
    uint8_t payload[COPYCOP_PROTOCOL_PAYLOAD_BYTES];
    uint32_t frame_crc;
} protocol_frame_t;

_Static_assert(sizeof(protocol_frame_t) == COPYCOP_USB_ENDPOINT_SIZE,
               "protocol frame must match HID report size");

static uint8_t transfer_buffer[COPYCOP_SLOT_MAX_PAYLOAD_BYTES];
static bool transfer_started;
static uint32_t expected_length;
static uint32_t expected_crc;
static uint32_t received_length;
static bool commit_pending;
static protocol_frame_t commit_request;
static protocol_frame_t transmit_frame;
static bool transmit_pending;
static uint32_t event_sequence;
static copycop_protocol_result_t last_result;

static void finalize_frame(protocol_frame_t *frame) {
    frame->magic = COPYCOP_PROTOCOL_MAGIC;
    frame->version = COPYCOP_PROTOCOL_VERSION;
    frame->frame_crc = copycop_crc32(frame, offsetof(protocol_frame_t, frame_crc));
}

static void queue_response(const protocol_frame_t *request, protocol_status_t status,
                           uint32_t argument0, uint32_t argument1) {
    memset(&transmit_frame, 0, sizeof(transmit_frame));
    transmit_frame.type = request->type | TYPE_RESPONSE_MASK;
    transmit_frame.status = (uint8_t)status;
    transmit_frame.sequence = request->sequence;
    transmit_frame.argument0 = argument0;
    transmit_frame.argument1 = argument1;
    finalize_frame(&transmit_frame);
    transmit_pending = true;
}

static bool frame_valid(const protocol_frame_t *frame) {
    return frame->magic == COPYCOP_PROTOCOL_MAGIC
        && frame->version == COPYCOP_PROTOCOL_VERSION
        && frame->payload_length <= COPYCOP_PROTOCOL_PAYLOAD_BYTES
        && frame->frame_crc == copycop_crc32(frame, offsetof(protocol_frame_t, frame_crc));
}

void protocol_init(void) {
    transfer_started = false;
    commit_pending = false;
    transmit_pending = false;
    event_sequence = 1u;
    last_result = COPYCOP_PROTOCOL_RESULT_NONE;
}

void copycop_usb_load_report_received(const uint8_t *report, size_t length) {
    if (report == NULL || length != sizeof(protocol_frame_t)) return;
    protocol_frame_t request;
    memcpy(&request, report, sizeof(request));

    if (!frame_valid(&request)) {
        if (request.magic == COPYCOP_PROTOCOL_MAGIC) {
            queue_response(&request,
                           request.version == COPYCOP_PROTOCOL_VERSION
                               ? STATUS_BAD_FRAME : STATUS_BAD_VERSION,
                           received_length, 0u);
        }
        return;
    }

    switch (request.type) {
        case TYPE_HELLO:
            queue_response(&request, STATUS_OK, COPYCOP_PROTOCOL_VERSION,
                           COPYCOP_IMPLEMENTATION_PHASE);
            break;
        case TYPE_GET_INFO:
            queue_response(&request, STATUS_OK, (uint32_t)storage_max_text_bytes(),
                           (uint32_t)(storage_generation() != 0u));
            transmit_frame.payload_length = 12u;
            memcpy(&transmit_frame.payload[0], &(uint32_t){storage_generation()}, 4u);
            memcpy(&transmit_frame.payload[4], &(uint32_t){storage_text_crc()}, 4u);
            {
                const uint8_t *text;
                size_t text_length;
                const uint32_t stored_length = storage_get_text(&text, &text_length)
                    ? (uint32_t)text_length : 0u;
                memcpy(&transmit_frame.payload[8], &stored_length, 4u);
            }
            finalize_frame(&transmit_frame);
            break;
        case TYPE_BEGIN_TRANSFER:
            if (commit_pending) {
                queue_response(&request, STATUS_BUSY, received_length, 0u);
            } else if (request.argument0 > storage_max_text_bytes()) {
                queue_response(&request, STATUS_BAD_LENGTH, 0u,
                               (uint32_t)storage_max_text_bytes());
            } else {
                transfer_started = true;
                expected_length = request.argument0;
                expected_crc = request.argument1;
                received_length = 0u;
                queue_response(&request, STATUS_OK, 0u, expected_length);
            }
            break;
        case TYPE_DATA: {
            if (!transfer_started) {
                queue_response(&request, STATUS_BAD_STATE, received_length, 0u);
                break;
            }
            const uint32_t offset = request.argument0;
            const uint32_t end = offset + request.payload_length;
            if (end > expected_length || end < offset) {
                queue_response(&request, STATUS_BAD_LENGTH, received_length, expected_length);
            } else if (offset == received_length) {
                memcpy(transfer_buffer + offset, request.payload, request.payload_length);
                received_length = end;
                queue_response(&request, STATUS_OK, received_length, expected_length);
            } else if (end <= received_length
                       && memcmp(transfer_buffer + offset, request.payload,
                                 request.payload_length) == 0) {
                queue_response(&request, STATUS_OK, received_length, expected_length);
            } else {
                queue_response(&request, STATUS_BAD_OFFSET, received_length, expected_length);
            }
            break;
        }
        case TYPE_END_TRANSFER:
            if (!transfer_started || received_length != expected_length) {
                queue_response(&request, STATUS_BAD_STATE, received_length, expected_length);
            } else if (copycop_crc32(transfer_buffer, received_length) != expected_crc) {
                transfer_started = false;
                queue_response(&request, STATUS_BAD_CRC, received_length, expected_crc);
                last_result = COPYCOP_PROTOCOL_RESULT_ERROR;
            } else {
                commit_request = request;
                commit_pending = true;
                transfer_started = false;
            }
            break;
        case TYPE_GET_STATUS:
            queue_response(&request, commit_pending ? STATUS_BUSY : STATUS_OK,
                           received_length, expected_length);
            break;
        case TYPE_CLEAR:
            if (storage_clear_text()) {
                queue_response(&request, STATUS_OK, storage_generation(), 0u);
                last_result = COPYCOP_PROTOCOL_RESULT_CLEARED;
            } else {
                queue_response(&request, STATUS_STORAGE_ERROR, 0u, 0u);
                last_result = COPYCOP_PROTOCOL_RESULT_ERROR;
            }
            break;
        default:
            queue_response(&request, STATUS_BAD_FRAME, 0u, 0u);
            break;
    }
}

void protocol_task(void) {
    if (commit_pending && !transmit_pending) {
        const bool saved = storage_commit_text(transfer_buffer, expected_length, expected_crc);
        commit_pending = false;
        queue_response(&commit_request, saved ? STATUS_OK : STATUS_STORAGE_ERROR,
                       saved ? storage_generation() : 0u,
                       saved ? (uint32_t)expected_length : 0u);
        last_result = saved ? COPYCOP_PROTOCOL_RESULT_SAVED
                            : COPYCOP_PROTOCOL_RESULT_ERROR;
    }
    if (transmit_pending
        && copycop_usb_load_send((const uint8_t *)&transmit_frame,
                                 sizeof(transmit_frame))) {
        transmit_pending = false;
    }
}

void protocol_notify_copy_pressed(void) {
    if (transmit_pending || transfer_started || commit_pending) return;
    memset(&transmit_frame, 0, sizeof(transmit_frame));
    transmit_frame.type = TYPE_COPY_EVENT;
    transmit_frame.sequence = event_sequence++;
    finalize_frame(&transmit_frame);
    transmit_pending = true;
}

copycop_protocol_result_t protocol_take_result(void) {
    const copycop_protocol_result_t result = last_result;
    last_result = COPYCOP_PROTOCOL_RESULT_NONE;
    return result;
}

bool protocol_transfer_active(void) {
    return transfer_started || commit_pending;
}

