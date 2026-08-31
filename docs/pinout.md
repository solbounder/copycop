# Verified Waveshare RP2040-Keyboard-3 pinout

No pin in the firmware is guessed. The electrical mapping was checked against
Waveshare's official schematic dated 2024-02-29 and its official Arduino demo.

| Function | RP2040 GPIO | Electrical behavior | Evidence |
|---|---:|---|---|
| LEFT / SPEED | GP14 | switch to GND, internal pull-up, active-low | official demo maps GP14 to the left Ctrl key |
| MIDDLE / COPY + LOAD | GP13 | switch to GND, internal pull-up, active-low | official demo maps GP13 to the middle C key |
| RIGHT / PASTE | GP12 | switch to GND, internal pull-up, active-low | official demo maps GP12 to the right V key |
| RGB data | GP18 | WS2812B serial data, 800 kHz | official schematic and demo |

The three RGB devices do not use three GPIOs. They form one serial chain in the
schematic:

```text
GP18 -> WS_L11 -> WS_L21 -> WS_L31
```

All three LEDs are powered from 5 V and share ground. CopyCop deliberately sets
all three to the same status color, so no current behavior depends on their
left-to-right chain order.

The official Arduino demo declares a GRB device order. The first real-board
test showed red and green exchanged with that PIO byte order, while blue was
correct. `board_config.h` therefore records the tested production board's
R-G-B wire order explicitly instead of hiding that hardware observation.

The board flash in the schematic is a Winbond W25Q16JVUXIQ: 16 Mbit = 2 MiB.
The two USB-C receptacles feed one RP2040 USB device path through the board's
USB switching circuit; they are not independent host/device connections.

Sources:

- [Waveshare RP2040-Keyboard-3 wiki](https://www.waveshare.com/wiki/RP2040-Keyboard-3)
- [Official Waveshare schematic](https://files.waveshare.com/wiki/RP2040-Keyboard-3/RP2040-Keyboard-3-Schematic.pdf)
- [Official Waveshare demo archive](https://files.waveshare.com/wiki/RP2040-Keyboard-3/RP2040-Keyboard.zip)
