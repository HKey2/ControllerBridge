# Device compatibility and limits

The standard hardware is an unmodified Waveshare RP2350-USB-A. Connect one input device directly to USB-A. USB-C provides the configuration/output connection. Alternate RP2350 boards require the changes in the [GPIO guide](gpio-assignment.md) and their own hardware checks.

## Input devices

| Device or input type | Current scope |
|---|---|
| EGRET II mini arcade panel (`0AE4:0703`) | Input mapping and Switch 2 controls verified |
| X68000 Z JOYCARD (`33DD:0013`) | Input mapping, automatic selection, and Switch 2 controls verified |
| 8BitDo M30 2.4G, wired Switch-compatible mode | Input initialization and mapped controls verified; receiver operation is outside these checks |
| EGRET II mini Paddle Controller (`0AE4:0701`) | Trackball, paddle, mouse buttons, and combined navigation output implemented and tested |
| XE1AJ-USB / Cyber Stick (`17A7:0005`) | Full Speed input, buttons, stick X/Y, and throttle Rz observed; a reference profile was saved, but game-specific analog play remains unverified |
| Generic variable HID input | Buttons, report IDs, absolute axes, and four/eight-direction hats supported within resource limits; this does not guarantee every product |
| Wired Xbox 360-format input | Backend implemented; physical input remains unverified |

Unsupported scope includes USB hubs, Bluetooth, Low Speed/High Speed devices, HID array input interpretation, Xbox One, and arbitrary vendor-specific initialization.

Resource limits are four HID interfaces, eight IN endpoints, 64-byte input packets, 256 decoded fields, 1,024-byte configuration descriptors, and 2,048-byte HID report descriptors. Inputs exceeding descriptor bounds are rejected instead of silently truncated.

## Controller-specific behavior

M30 wired input can expose different USB identities. The Switch-compatible path recognizes `057E:2009`, performs its USB handshake and report-mode initialization, and decodes report `0x30`. A controller power reconnection may be needed after resetting only the bridge. The observed identity transition does not establish the controller's internal mode-selection rule.

The Xbox 360-format backend accepts the corresponding interface class/subclass/protocol and packet format. Its decoded fields come from a fixed internal schema, not an actual HID report descriptor. The Switch-compatible backend likewise supplies an internal schema. Profile matching includes the relevant descriptor fingerprint so different input modes can have different profiles.

The observed Cyber Stick report has 13 declared buttons, a hat, and four unsigned 8-bit axes. In the checked analog operation, X/Y were the stick and Rz was the throttle; Z and some declared controls remained unchanged. Calibrate the actual unit rather than copying another unit's neutral values. DIGITAL mode and game-specific behavior need separate validation.

The mouse path selects relative X/Y and relative wheel input. For the EGRET controller, wheel movement is interpreted as paddle movement on the profile-selected X (horizontal) or Y (vertical) axis. The default is X; Y selection requires firmware 0.20.2 or later. Absolute axes are not automatically used as relative mouse input. An enabled matching profile is required. Firmware 0.20.10 supports independent trackball D-pad conversion and paddle X=left/right or Y=up/down conversion. In combined output, conversion adds D-pad clicks while normal pointer movement continues for both enabled sources. Physical D-pad inputs and direction assignments are not required. The device-wide persistent threshold defaults to 64 after separate hidden cursor gain (initially 6.0× ball / 1.5× paddle), with signed accumulation and one click followed by complete accumulator clear. Physical trackball/paddle operation on 0.20.6 has confirmed simultaneous pointer and D-pad transfers to a Windows PC with all four direction assignments empty: 770 complete clicks and 3,415 mouse movements, with no recorded drops. D-pad response on Switch HOME was still reported absent with 0.20.6. The user also reproduced missed very short taps on an ordinary controller. Version 0.20.7 added minimum 30 ms pressed and released phases; PC measurements showed 31.000–31.001 ms for both, and the user subsequently confirmed direction operation. Version 0.20.8 introduced a saved shared duration initialized to 16 ms. The user subsequently confirmed operation at 16 ms and accepted it as the fix. Version 0.20.9 keeps that default and adds Mouse UDLR settings for threshold 1–256 pt and hold time 1–512 ms. Version 0.20.10 separates the released interval into Wait time (default 16 ms, range 1–512 ms) and retains Hold time for the pressed phase. Missing wait settings inherit the old hold value. Physical save/readback and reboot checks confirm independent persistence; the final storage comparison changes only the new 16-byte wait record. No new motion-timing capture was made for this settings change. Fast movement can exceed the four-click waiting queue and is counted as dropped direction events; pointer movement continues independently.

## Output and game behavior

Gamepad output retains the project's verified Switch-compatible descriptor path. Mouse-only output uses development identity `CAFE:4004`; this is not a production USB identity allocation. Combined mode exposes separate mouse and gamepad HID interfaces.

Mouse Report Protocol supports signed 16-bit movement; Boot Protocol supports signed 8-bit movement. Larger movement is split while retaining residual displacement. Movement and click queues are independent, but overload is not guaranteed lossless.

Mouse movement with Minus, Plus, and HOME has been verified on Switch 2. Combined full-gamepad behavior also depends on how a game assigns player 1/player 2 and accepts devices. USB delivery alone does not prove game adoption.

**Combat School movement speed remains unresolved.** Input at 1 ms and trackball gain 64 improved the result but did not reach the tested gaming mouse's full-power response. Paddle input was also insufficient, and small back-and-forth trackball movements behaved differently from sustained movement. Current software does not claim to resolve that game-specific behavior.

The configurator's live axis list has been checked using deterministic input notifications and rendered layouts. Physical calibration and in-game response still need checking for each actual analog controller. See [current verification](verification.md).
