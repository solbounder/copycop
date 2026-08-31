#pragma once

#include <stdbool.h>
#include <stdint.h>

typedef enum copycop_protocol_result {
    COPYCOP_PROTOCOL_RESULT_NONE = 0,
    COPYCOP_PROTOCOL_RESULT_SAVED,
    COPYCOP_PROTOCOL_RESULT_CLEARED,
    COPYCOP_PROTOCOL_RESULT_ERROR,
} copycop_protocol_result_t;

void protocol_init(void);
void protocol_task(void);
void protocol_notify_copy_pressed(void);
copycop_protocol_result_t protocol_take_result(void);
bool protocol_transfer_active(void);

