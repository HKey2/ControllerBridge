# S/X host output and Switch 2 Pro input

ControllerBridge 0.23.0 adds a device-wide host selector at the bottom of **Shared settings**. Install the matching configurator and firmware.

| Choice | Behavior |
|---|---|
| **S** | Default. Retains the existing Switch-compatible gamepad output. |
| **Auto** | Disabled and grey; no automatic host identification is implemented. |
| **X** | Uses the wired Xbox 360 XInput gamepad protocol for Gamepad and Mouse + Gamepad. |

Choose **Save to device**. The configurator verifies the saved value, asks the board to reconnect USB, and reconnects through the configuration interface. All profile mappings and unsaved drafts retain their physical positions. The selected mode persists after restart. USB mouse-only output remains a mouse while retaining the S/X preference for the gamepad modes.

![Shared settings with X selected](gui-mouse-udlr-settings-en.png)

## Button layout

The internal profile IDs retain their established physical positions. In X, the screen shows **Y at the top, X at the left, B at the right and A at the bottom**. Saving a host-mode change does not rewrite any profile bytes.

| Physical position or function | S label | X label/function | XUSB encoding |
|---|---|---|---|
| Bottom face button | B | A | 0x1000 |
| Right face button | A | B | 0x2000 |
| Left face button | Y | X | 0x4000 |
| Top face button | X | Y | 0x8000 |
| Left / right shoulder | L / R | LB / RB | 0x0100 / 0x0200 |
| Left / right trigger | ZL / ZR | LT / RT | Separate bytes, released 0 / pressed 255 |
| Minus / plus | − / + | Back / Start | 0x0020 / 0x0010 |
| Left / right stick click | L / R stick | L / R stick | 0x0040 / 0x0080 |
| HOME | HOME | Guide | 0x0400 |
| Capture | CAPTURE | No Xbox 360 counterpart | No bit sent; primary mapping button disabled |
| D-pad up / down / left / right | Same directions | Same directions | 0x0001 / 0x0002 / 0x0004 / 0x0008 |

Guide is sent using the established Xbox 360 wire bit. Windows' ordinary documented `XInputGetState` button list omits Guide; the operating system and Guide-aware consumers handle it separately. The physical test observed HOME through the system's extended XInput state export. Opening a particular overlay depends on that program and the Windows/Game Bar settings.

## USB and report format

The gamepad interface uses class/subclass/protocol **FF/5D/01**, the published 17-byte Xbox gamepad descriptor, interrupt IN **0x81**, interrupt OUT **0x02**, and 32-byte endpoint reservations. Input polling is 1 ms. Each input packet is exactly 20 bytes:

| Offset | Bytes | Value |
|---:|---:|---|
| 0 / 1 | 1 each | Report identifier 0x00 / length 0x14 |
| 2 | 2 | Button bits, little endian |
| 4 / 5 | 1 each | LT / RT |
| 6 / 8 / 10 / 12 | 2 each | Signed LX / LY / RX / RY, little endian |
| 14 | 6 | Zero |

Stick center is 0, with endpoints −32768 and +32767; positive Y is up. The existing mapper produces 8-bit axis positions, which this encoder scales into the signed XInput range. This release retains that conversion resolution. Trigger outputs are digital because the established profile targets ZL/ZR are digital bindings. Bit 0x0800 stays zero.

The board retains its own composite identity and a separate HID Feature configuration interface. It advertises the Microsoft OS 1.0 compatible ID **XUSB10** for interface 0, allowing the installed Windows Xbox driver to bind to that function without hiding configuration access. The real test required no driver installation.

| Output layout | Device identity | Interfaces / endpoints |
|---|---|---|
| X gamepad | CAFE:4005 | XUSB IF0 IN81/OUT02; configuration IF1 IN83 |
| X gamepad + mouse | CAFE:4006 | XUSB IF0 IN81/OUT02; mouse IF1 IN82; configuration IF2 IN83 |
| Mouse only | CAFE:4004 | Existing mouse layout; host preference retained |

The PC lists the XUSB function as **XBOX 360 Controller For Windows**. The project does not advertise the retail controller's headset/security interfaces. Rumble and player-LED output packets can be received, but forwarding them to the input controller is not implemented. Xbox-console authentication is outside this Windows XInput output implementation.

## Switch 2 Pro input

The connected controller identified itself as Nintendo **057E:2069**, revision **0201**. Its protocol differs from the original 057E:2009 Switch Pro protocol. The supported path uses the HID input endpoint on interface 0 and the bulk command/reply endpoints OUT02/IN82 on interface 1. Audio interfaces remain unused.

Initialization follows the ten published SDL commands, retaining open bulk endpoints and USB data toggles throughout the sequence. The controller then sends report **0x05**. The internal schema preserves its raw bit offsets: face buttons at bits 40–43, the remaining button/D-pad fields at their published positions, and four packed 12-bit stick axes at bits 88/100/112/124. The schema exposes 25 fields, including C/GL/GR as optional assignable inputs. A distinct SWP2 fingerprint separates these profiles from original Switch Pro profiles.

For a new controller, choose **Add controller**, assign its physical buttons, and use the normal analog-axis calibration. Unit-specific neutral/range values should be measured from that controller. For the physical trial, the first unused slot was registered as **Switch 2 Pro**, with its axes calibrated from the user's movement. This is a saved configuration on the tested board; firmware does not silently create profiles on other boards.

![Physical Switch 2 Pro connected in X mode](gui-xinput-en.png)

## Saved data and verification

Host mode adds an independent 16-byte HSM1 record at common-sector offset 228, leaving all preceding settings records intact. Missing/invalid HSM1 defaults to S. Shared settings use UDSA/schema 10 and guard the old host value together with the other editable values before saving. Profile v8 remains 848 bytes; bank v8 remains 13,908 bytes; runtime common settings remain 40 bytes because host mode uses existing padding. Common Flash usage is 244/4,096 bytes.

The normal firmware was verified on the physical board. Windows recognition, configuration access, real ABXY/D-pad/shoulder/trigger/Start/Back/stick-click/Guide input, all four stick directions, combined output enumeration, S/X return and warm restart passed. Existing slots and common numeric settings were preserved. See the dated [verification record and current memory budget](verification.md).

## Protocol references and attribution

- [Microsoft XINPUT_GAMEPAD](https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_gamepad): documented digital masks, trigger range and signed stick conventions.
- [GP2040-CE wired XInput descriptors](https://github.com/OpenStickCommunity/GP2040-CE/blob/main/headers/drivers/xinput/XInputDescriptors.h): original gamepad descriptor, 20-byte report and Guide bit.
- [Linux xpad driver](https://github.com/torvalds/linux/blob/master/drivers/input/joystick/xpad.c): independent decoding of wired Xbox 360 report fields; no GPL implementation code was copied.
- [Microsoft OS 1.0 descriptors](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/microsoft-defined-usb-descriptors): compatible-ID mechanism. The actual installed Windows `xusb22.inf` matches MS_COMP_XUSB10.
- [espp USB XInput function](https://github.com/esp-cpp/espp/blob/main/components/usb_device/src/usb_device.cpp): existing composite XUSB10 implementation reference.
- [SDL Switch 2 driver, pinned revision f3be7b901e5c5ed5b4860bd89c99fc386be28b9d](https://github.com/libsdl-org/SDL/blob/f3be7b901e5c5ed5b4860bd89c99fc386be28b9d/src/joystick/hidapi/SDL_hidapi_switch2.c): Switch 2 initialization and input layout. The command sequence was adapted, with the [SDL zlib license](../licenses/SDL-zlib.txt) retained; the local host implementation is marked as altered.
