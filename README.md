# ControllerBridge

[English](README.md) | [日本語](README.ja.md)

- Support for a wide range of controllers, including paddles, trackballs, and flight sticks.
- Fully customizable controls and settings.
- Combined mouse and gamepad output.
- Shift-button switching between primary and alternate button mappings.
- Per-button rapid fire, adjustable in one-press-per-second increments.
- Map mouse, trackball, and paddle movement to any button.
- Virtual rolling switch mode.
- Internal input processing within 1 ms.
- Configurable input polling interval (1–255 ms).
- Up to 16 onboard profiles, with automatic controller matching and shortcut switching.
- Customizable RGB LED colors for each profile.
- Standalone operation after setup—no PC required during play.
- Profile backup and restore.

## Overview

ControllerBridge turns USB controllers into Switch-compatible HID, Xbox 360 XInput and mouse inputs using an unmodified **Waveshare RP2350-USB-A** board. Connect the input controller to USB-A and USB-C to a Windows PC for configuration, or to a Switch 2 (S mode) / Windows PC (X mode) for play.

[![ControllerBridge in its clear PETG case, with the LED illuminating the lid in a dark setting](docs/images/case/led-dark.jpg)](docs/case.md)

The reference-board case uses a clear PETG lid with a light-diffusing fin. See the [case introduction, sample photos and STL downloads](docs/case.md). It is a remix of [barrenechea's Waveshare RP2350-USB-A Case](https://www.printables.com/model/1261378-waveshare-rp2350-usb-a-case), licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).

## Current release

**Current development release: v0.25.1 (firmware 0.25.1).** The application uses Japanese on Japanese Windows and English otherwise. This README is available in English and Japanese. Detailed documentation and reference screenshots use English; profile names remain as entered.

**0.25.1:** X gamepad output uses the reference Xbox 360 USB identity and four-interface layout without a configuration HID. The Windows configurator connects and updates firmware while this layout remains active, with no BOOT-button operation or driver replacement. Install the matching configurator and bundled firmware. See [X output and configuration access](docs/xinput-output.md).

**0.25.0:** Adds an analog stick setup wizard that learns the input source, center and range for all four stick axes from actual movement, with one-sided pedal and inversion support. Source colors and retained reference lines show progress; Setup wizard sits to the left of the individual axis buttons. Input reception runs independently of UI work, reducing profile-switch delays and flicker. This version also includes the X output interval setting and descriptor-processing improvements. Use the bundled 0.25.0 firmware for one-sided pedal mappings. See [analog stick setup](docs/configurator-guide.md#analog-stick-setup-wizard).

**0.24.1:** The profile Input interval also controls X gamepad output: 0 retains reference-compatible timing; 1–255 requests that interval in milliseconds without the 11 ms producer wait. Applying a changed X interval reconnects USB. S and mouse output timing are retained. Native endpoint intervals are parsed at attachment instead of per pointing report. See [latency and polling policy](docs/latency-scheduling.md).

**0.24.0:** Auto is now selectable alongside fixed S and X. Auto tries X first and switches to S once if X is not accepted within five seconds. It never cycles back or retries the other mode during that selection. The configurator shows the active result as Auto (X) or Auto (S); profiles and numeric shared settings are retained.

**0.23.2:** Fixed wired 8BitDo Arcade Stick initialization. X output now follows the connected reference’s USB packet sizes, endpoint numbers, interface descriptors, strings, short capability replies and report timing. Settings remain accessible with the existing PIDs; a dedicated build can override the PIDs if needed. Saved profile and shared-settings formats are unchanged. See [XInput compatibility and remaining differences](docs/xinput-output.md).

**0.23.1:** X output now uses VID `045E`, retaining PIDs `4005` (gamepad) and `4006` (gamepad + mouse). The firmware answers wired Xbox 360 input/motor capability requests used during initialization. Configuration access supports both the old and new identities. Japanese Shared settings now labels the selector **出力モード** and X as **X : Xbox360互換**. See [XInput compatibility and supported scope](docs/xinput-output.md).

**0.23.0:** Shared settings now ends with **S / Auto / X**. S retains the previous output and is the default; Auto is disabled; X uses wired Xbox 360 XInput. X changes the displayed ABXY layout and sends HOME as Guide. The host mode is shared by all profiles and persists after restart. USB reconnects after a verified mode save, and settings remain accessible in X mode. Wired Switch 2 Pro input is also supported. See [XInput output and Switch 2 Pro setup](docs/xinput-output.md).

**0.22.0 GUI r4:** The four header actions—Firmware update, Connect / Reconnect, Profile switching and Shared settings—now use equal widths, sized for their labels and the display scale.

**0.22.0 GUI r3:** Fixed flicker when switching output tabs and repeated scaling that could push shared settings out of view on high-DPI displays. Added a [sample slot configuration](samples/README.md), also included in the release ZIP.

**0.22.0 GUI r2:** Trackball's click-conversion mode is now named **Virtual Cursor** in Conversion mapping and Shared settings. Paddle retains **Virtual Rolling**. This is a display-name change; behavior and saved settings are unchanged.

**0.22.0 settings UI:** Shared settings groups click conversion, Trackball Hold and Paddle Virtual Rolling, with pseudo-inertia inside Hold. Conversion mapping omits the redundant profile-only notice. Save to device and Apply to draft are disabled until settings differ from their opening values; reverting all edits disables the action again. Conversion mapping is aligned with the right-hand profile actions. The deletion confirmation explains that dragging remains disabled until all pending edits are saved to the device. Japanese board references consistently use 「デバイス」. The main profile editor now groups Trackball and Paddle in separate raised panels, enclosing each source's enable switch, gain, inversion and conversion controls.

**Firmware 0.20.18:** USB mouse uses a dedicated movement path, bypassing gamepad conversion state while preserving pointer gain, inversion, paddle axis, clicks and rapid fire. Gamepad and combined output retain their conversion behavior. See [implementation verification and memory](docs/verification.md).

**0.20.21:** Elite Series 2 gets one additional automatic recovery cycle if GIP startup times out. Successful initialization returns the LED to cyan while waiting for the first input; leaving the controller untouched does not trigger a reconnect. Existing profiles and settings are retained. See [MS-GIPUSB startup and LED states](docs/xbox-one-input.md).

**0.20.20:** MS-GIPUSB input for wired Xbox One-family controllers is available, including the separate Xbox button and Elite paddle fields. Elite Series 2 input and reconnection after bridge restart/firmware update were verified. Connect by USB and choose **Add controller** to create its mappings. Existing mouse-mode conversion preferences remain preserved. Other models and physical Switch/game output still need verification. See [MS-GIPUSB input](docs/xbox-one-input.md).

**0.20.19:** Selecting USB mouse retains both movement-to-gamepad conversion flags and shows their ON/OFF values in grey. Conversion is inactive in this mode. Returning to Gamepad or Mouse + Gamepad restores the retained settings. Saving and reloading also preserves them; install the matching 0.22.0 firmware. Pointer settings remain editable.

**Earlier GUI correction:** Fixed overlapping text when switching modes in Mouse UDLR settings. Clarified the Paddle Virtual Rolling label to **Output analog stick** and its description to **Converts the paddle's virtual angle into analog-axis tilt.**

**0.22.0: conversion settings now belong to each profile.** Open **Conversion mapping** to choose four Trackball targets, two Paddle targets (negative/positive), both conversion modes, and Virtual Rolling stick, reversal and rotation step. Choose **Apply to draft**, then **Save profile to device**. Paddle targets remain the same when changing the pointer's X/Y output axis. Rotation steps are 5/10/15/20/30/40/45/60/90°, default 30°.

**Shared settings** provides device-wide click threshold (default 64 pt), pressed time (30 ms), released interval (20 ms), Hold threshold (1 pt), **inertia threshold 8 pt/20 ms, decay factor 0.90 and decay interval 100 ms**, and rotation sensitivity (1.0°/pt). Enable pseudo-inertia separately in each profile under **Conversion mapping → Trackball → Hold → Enable pseudo-inertia**. It defaults to disabled, including migrated profiles. Saving shared settings preserves unsaved profile drafts.

Each axis starts inertia when its measured displacement over the last 20 ms exceeds the threshold. Small input does not replenish it. Existing inertia retains 90% each interval and ends at or below 8; there is no maximum-duration setting. Initial amounts 14–15 last 600 ms, 16 lasts 700 ms and 36 lasts about 1.5 seconds. A qualifying reversal cancels that axis immediately. Hold is a binary button output: the decaying internal amount determines duration, while actual game speed also depends on the game's key repeat.

Older v5/v6 device banks and JSON v2/v3 files can be converted. Device migration copies the former shared maps, modes and rotation step into every profile; Paddle takes left/right for an old X profile or up/down for an old Y profile. Shift now supports three keys: only the old fourth key is removed, preserving the first three, shifted mappings and rapid-fire settings. Old JSON did not contain shared conversion settings, so imports use the connected device's retained migration defaults or standard defaults while disconnected and display a notice. Check the converted mappings before saving. New profiles/banks use v8 and JSON exports v5. Keep a pre-upgrade backup for any downgrade.

Pointer gain remains Trackball 1.0–256.0× (default 16.0) and Paddle 1.0–10.0× (default 1.0). Pointer gain, inversion and Paddle output axis remain per-profile. Combined output runs conversion alongside pointer movement; USB mouse-only retains preferences without running conversion. Rapid fire remains 1–60 cycles per second, initially 20 when enabled. See the [user guide](docs/configurator-guide.md#conversion-mapping-and-shared-settings).

![ControllerBridge profile list, English interface](docs/gui-main-en.png)

## Quick start

1. Connect the board's USB-C port to Windows and run `ControllerBridge.exe`. It includes its Windows x64 runtime and matching firmware; no separate .NET installation is needed.
2. Follow the automatic firmware guide if it appears, or choose **Firmware update**. For first installation, the dialog immediately waits for reconnection with BOOT held and shows only **Cancel**. Reconnect USB-C while holding BOOT; detection opens the final overwrite confirmation. Choose **Yes** to transfer the bundled firmware and check its version after restart.
3. Connect the input controller directly to USB-A.
4. For a controller without a matching profile, the first available slot is selected automatically. Choose **Add controller**, confirm the device, map its inputs, configure any axes, and save.
5. Reconnect USB-C to the Switch 2. The board selects the matching saved profile without a PC.

Only one configurator instance can run. Close other bridge configuration tools while using it. **Firmware update** compares only whether the running version equals the bundled version. An equal version finishes without writing; a different version requires confirmation. Firmware without the version-query command follows the BOOT connection and overwrite-confirmation route. See the [firmware update guide](docs/firmware-update.md).

At startup and during background discovery, the tool checks firmware before reading settings. A factory-state RP2350-USB-A exposing Pico SDK USB (`2E8A:0009`), a board in RP2350 BOOT mode (`2E8A:000F`), or a detected bridge with no readable version shows **Device unsupported** and automatically opens the firmware installation guide. A readable different version opens the update guide. With no recognized board, the status is **Device not connected** and no guide opens. Cancelling the wait or declining the final confirmation keeps the unsupported status visible without repeating the guide during that connection; an observed disconnection rearms guidance for the next attachment. Background retries preserve drafts and do not repeatedly disable or repaint the editor. **Firmware update** remains available for manual installation or USB identities outside these recognized cases.

New input detection never opens a registration confirmation or writes settings. Selection happens once per attachment, so later manual browsing is preserved. Unsaved edits and modal operations defer the move. A full bank shows a nonmodal no-empty-slot message only when no matching profile exists. Slots with unsaved profiles or pending deletions are protected. Registration since 0.19.0 verifies the original input connection and empty destination at both transfer start and commit; install the matching bundled firmware to use this protection.

This release adds hold-to-shift button mappings. Existing 0.16.0–0.19.0 settings migrate to the new format with shift disabled, preserving the main mappings, axes, IDs, shortcuts, output settings and LED colors. Back up all slots before updating if you may need to return to older firmware: old firmware cannot read the new format. Pre-0.16.0 records remain unsupported and start with 16 unused slots. See [button shift](docs/button-shift.md).

## Profiles and output

The board stores **16 slots**, including multiple settings for one controller. Each profile stores matching information, a name and stable ID, mappings, button shift, rapid fire, input interval, mouse settings, output mode, combined-mode button behavior, and LED color.

- **Duplicate profile** copies into the closest unused slot below the selection and appends an available `[2]`, `[3]`, etc. to its stored name. Save the new slot to persist it.
- **Delete profile** clears the selected slot when you save it.
- **LED RGB settings**, below Delete profile, selects the default dim blue (0, 0, 1) or exact R/G/B values from 0 to 255. Save the slot to apply it. Black (0, 0, 0) turns the ready LED off; status notifications still take priority. A six-digit RGB code field below the spinners accepts web colors with or without a leading # and stays synchronized with the channels. The preview uses a fixed display-only gamma of 4.0; saved RGB values remain exact.
- Drag a profile, or use `Alt+Up` / `Alt+Down`, to reorder saved slots. Completed moves save immediately and carry IDs, shortcuts, and selections with them.
- **Back up all slots** and **Restore all slots** handle the whole bank in one JSON file. Current backups use `ControllerBridge.Slots`, JSON version 5. Version-2/3 backups and individual exports are converted; version 2 has no shifted mappings. Version 1 remains unsupported.
- **Button shift…**, in the mapping editor, learns one to three physical keys. Holding all keys activates a button-only shifted page; unchanged outputs inherit the main mapping. Choose **Apply to draft**, then save the slot. All four axes stay common. See the [shift guide](docs/button-shift.md) for consumption, remapping and shortcut rules.
- **Shared settings** saves common thresholds, timings, sensitivity, inertia tuning and S/X host mode. **Conversion mapping** edits targets, modes, rotation steps and inertia enable in the selected profile draft. See [settings](docs/configurator-guide.md#conversion-mapping-and-shared-settings).
- **Profile switching...** configures a roughly one-second BOOT hold or a controller shortcut. The selected profile is retained across reconnections.

Selecting an output tab edits the profile. Choose **Save profile to device** to persist it. Browsing the list does not change the active device profile; **Use this profile** activates another matching saved profile.

| Output tab | Behavior |
|---|---|
| Gamepad | Buttons, directions, and stick axes as a Switch-compatible gamepad |
| USB mouse | Relative movement and mouse buttons |
| Mouse + Gamepad | Separate mouse and gamepad HID interfaces |

Only the combined tab offers **Button behavior: Mouse / Controller**. Mouse behavior sends left/right clicks and gamepad Minus/Plus/HOME. Controller behavior sends all mapped gamepad controls while the mouse supplies movement. A USB layout change reconnects USB-C while the USB-A input host continues running.

For the EGRET II mini Paddle Controller, trackball and paddle can be enabled independently. Trackball gain is 1.0-256.0, default 16.0; paddle gain is 1.0-10.0, default 1.0, both in 0.1 steps. Input interval `0` uses the controller descriptor; `1`–`255` sets the requested interval in milliseconds. For example, `1` requests input every millisecond. NAK responses mean request rate is not the same as fresh data rate.

Internal input processing refers to the bridge's work between receiving input and preparing output. It excludes controller scanning, USB polling waits, game processing, and display latency.

## Documentation and compatibility

Start with the [user guide](docs/configurator-guide.md). The [documentation index](docs/README.md) also links the current storage/protocol reference, GPIO porting instructions, LED behavior, compatibility limits, and verification results.

The standard input path supports one directly connected Full Speed device, four HID interfaces, eight IN endpoints, 64-byte input packets, and 256 observed fields. Hubs, Bluetooth, Low Speed/High Speed, HID array inputs, and arbitrary vendor initialization are outside the supported scope.

EGRET controls, X68000 Z JOYCARD controls, and wired M30 Switch-compatible input have been tested. Cyber Stick input has been observed, but game-specific analog play remains unverified. The Xbox 360-format backend is implemented without physical-input verification. Combined output can depend on the game's player assignment. **Combat School movement speed remains unresolved.** See [compatibility and limits](docs/compatibility.md).

## Source layout

| Path | Responsibility |
|---|---|
| `lib/controller_usb` | PIO USB host, enumeration, input protocols, HID decoding |
| `lib/controller_device` | Native RP2350 USB device, gamepad/mouse output and Feature transport |
| `lib/controller_bridge` | Mapping, queues, profiles, storage, switching, measurements |
| `apps/egret_switch` | Normal bridge firmware and status LED |
| `gui/ControllerBridge` | ControllerBridge WinForms application |
| `boards` | Board definitions; see the [GPIO guide](docs/gpio-assignment.md) for application pin constants |
| `hardware/case` | Printable case STL files and their CC BY 4.0 attribution |
| `tools` | Product build scripts |

Core 1 handles input; core 0 handles mapping and output. The normal bridge uses vendored Pico-PIO-USB for input and this project's native USB implementation for output.

## Build

Use the existing pinned Pico SDK 2.1.1, Arm GCC 14.2.1, and .NET 9 SDK. The GUI targets `net9.0-windows`. Build scripts check dependencies and do not install, upgrade, or change system toolchains.

```powershell
.\tools\build-firmware.ps1
.\tools\build-configurator.ps1 -Restore
```

The configurator build also builds the normal firmware and embeds that exact UF2 with its SHA-256 manifest. Outputs are `build/ControllerBridge.uf2`, `dist/ControllerBridge.exe`, and a separate `dist/ControllerBridge.uf2` for manual recovery. Omit `-Restore` when GUI dependencies are already restored. Firmware tools default to `%USERPROFILE%\.pico-sdk`; use `-PicoSdkPath` and `-PicoToolsRoot` for another existing installation.

## Memory budget

Normal firmware allocation figures, calculated from its binary and linker sections:

| Resource | Used or reserved | Remaining |
|---|---:|---:|
| Flash, 2,097,152 bytes | Code/initial values 128,528; persistent storage 49,152 | 1,919,472 |
| SRAM, 532,480 bytes | Static data 158,464; two stacks 8,192 | 365,824 |
| One bank copy, 16,384 bytes | Payload 13,908 + header 16 | 2,460 |
| Saved measurement, 4,096 bytes | Payload 3,784 + header 16 | 296 |
| Common settings, 4,096 bytes | Legacy header/payload 56 + alignment 8 + direction/mode records 180 | 3,852 |

Static SRAM consists of vector table 272, initialized data 39,416, and BSS 118,776 bytes. The minimum heap reservation of 2,048 bytes is included in remaining SRAM. These are allocation figures, not measured peak stack/heap values. Profile size is 848 bytes, bank payload 13,908 bytes, and normal UF2 257,536 bytes.

## Repository contents and license

Diagnostic output under `captures/`, build outputs, release binaries, and caches are ignored. Pico-PIO-USB is vendored without its Git history and verified against recorded hashes.

Project code is provided under the [BSD 2-Clause License](LICENSE), copyright 2026 HKey2. Third-party code retains its own licenses under `third_party/` and `licenses/`. The remixed case STL files are separately licensed under [CC BY 4.0](hardware/case/LICENSE.md), with attribution to the original designer, barrenechea.

Switch 2 initialization commands are adapted from SDL under its [zlib license](licenses/SDL-zlib.txt); see the pinned source and protocol references in [XInput output](docs/xinput-output.md).
