/*
 * Physical flash is 2 MiB, but only the lower 1 MiB is linkable firmware.
 * The storage service owns offsets 0x100000..0x1FFFFF.
 */
FLASH(rx) : ORIGIN = 0x10000000, LENGTH = 0x00100000

