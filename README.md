# ControllerBridge

ControllerBridge turns USB controllers into Switch-compatible gamepad and mouse inputs using an unmodified **Waveshare RP2350-USB-A** board. Connect the input controller to USB-A and USB-C to a Windows PC for configuration, or to the Switch 2 for play.

**Current development release: firmware 0.18.1 and Windows configurator 0.18.1.** The application uses Japanese on Japanese Windows and English otherwise. Repository documentation and screenshots are English; profile names remain as entered.

![ControllerBridge profile list, English interface](docs/gui-main-en.png)

## Quick start

1. Connect the board's USB-C port to Windows and run `ControllerBridge.exe`. It includes its Windows x64 runtime and matching firmware; no separate .NET installation is needed.
2. Follow the automatic firmware guide if it appears, or choose **Firmware update**. If prompted, reconnect the TYPE-C port while holding BOOT and confirm overwriting the board. The tool transfers the bundled firmware and checks its version after restart.
3. Connect the input controller directly to USB-A.
4. Select an unused slot and choose **Add controller**. Confirm the device, map its inputs, configure any axes, and save.
5. Reconnect USB-C to the Switch 2. The board selects the matching saved profile without a PC.

Only one configurator instance can run. Close other bridge configuration tools while using it. **Firmware update** compares only whether the running version equals the bundled version. An equal version finishes without writing; a different version requires confirmation. Firmware without the version-query command follows the BOOT connection and overwrite-confirmation route. See the [firmware update guide](docs/firmware-update.md).

At startup the tool checks firmware before reading settings. A different version or an unavailable version opens guidance automatically. If no supported interface is found, the initial guide covers USB-C connection, factory firmware, and other software; these cannot always be distinguished from an unplugged board. Declining keeps the status visible without reopening the same guide during background retries. Those retries preserve drafts and do not repeatedly disable or repaint the editor.

This release keeps the storage generation introduced in 0.16.0. Settings from versions earlier than 0.16.0 are ignored, and those boards start with all 16 slots unused. Register controllers again; subsequent saves remain persistent across restarts.

## Profiles and output

The board stores **16 slots**, including multiple settings for one controller. Each profile stores matching information, a name and stable ID, mappings, rapid fire, input interval, mouse settings, output mode, combined-mode button behavior, and LED color.

- **Duplicate profile** copies into the closest unused slot below the selection and appends an available `[2]`, `[3]`, etc. to its stored name. Save the new slot to persist it.
- **Delete profile** clears the selected slot when you save it.
- **LED RGB settings**, below Delete profile, selects the default dim blue (0, 0, 1) or exact R/G/B values from 0 to 255. Save the slot to apply it. Black (0, 0, 0) turns the ready LED off; status notifications still take priority. A six-digit RGB code field below the spinners accepts web colors with or without a leading # and stays synchronized with the channels. The preview uses a fixed display-only gamma of 4.0; saved RGB values remain exact.
- Drag a profile, or use `Alt+Up` / `Alt+Down`, to reorder saved slots. Completed moves save immediately and carry IDs, shortcuts, and selections with them.
- **Back up all slots** and **Restore all slots** handle the whole bank in one JSON file. Current backups use `ControllerBridge.Slots`, JSON version 2. Earlier backup/export formats are rejected.
- **Profile switching...** configures a roughly one-second BOOT hold or a controller shortcut. The selected profile is retained across reconnections.

Selecting an output tab edits the profile. Choose **Save slot to device** to persist it. Browsing the list does not change the active device profile; **Use this profile** activates another matching saved profile.

| Output tab | Behavior |
|---|---|
| Gamepad | Buttons, directions, and stick axes as a Switch-compatible gamepad |
| USB mouse | Relative movement and mouse buttons |
| Mouse + Gamepad | Separate mouse and gamepad HID interfaces |

Only the combined tab offers **Button behavior: Mouse / Controller**. Mouse behavior sends left/right clicks and gamepad Minus/Plus/HOME. Controller behavior sends all mapped gamepad controls while the mouse supplies movement. A USB layout change reconnects USB-C while the USB-A input host continues running.

For the EGRET II mini Paddle Controller, trackball and paddle can be enabled independently. Trackball gain is 1.0-64.0, default 16.0; paddle gain is 1.0-10.0, default 1.0, both in 0.1 steps. Input interval `0` uses the controller descriptor; `1` requests input every millisecond. NAK responses mean request rate is not the same as fresh data rate.

## Documentation and compatibility

Start with the [user guide](docs/configurator-guide.md). The [documentation index](docs/README.md) also links the current storage/protocol reference, GPIO porting instructions, LED behavior, diagnostics, compatibility limits, and verification results.

The standard input path supports one directly connected Full Speed device, four HID interfaces, eight IN endpoints, 64-byte input packets, and 256 observed fields. Hubs, Bluetooth, Low Speed/High Speed, HID array inputs, Xbox One, and arbitrary vendor initialization are outside the supported scope.

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
| `apps/egret_diagnostic` | Separate input-only USB CDC diagnostic target |
| `tools` | Build scripts and local diagnostic tools |

Core 1 handles input; core 0 handles mapping and output. The normal bridge uses vendored Pico-PIO-USB for input and this project's native USB implementation for output. TinyUSB is used only by the separate CDC diagnostic target.

## Build and verify

Use the existing pinned Pico SDK 2.1.1, Arm GCC 14.2.1, and .NET 9 SDK. The GUI targets `net9.0-windows`. Build scripts check dependencies and do not install, upgrade, or change system toolchains.

```powershell
.\tools\build-diagnostic.ps1 -Target ControllerBridge
.\tools\build-configurator.ps1 -Restore
.\dist\ControllerBridge.exe --self-test captures/gui-check --language en
.\dist\ControllerBridge.exe --check-profile-menu captures/profile-check --language en
.\dist\ControllerBridge.exe --check-axis-dialog captures/axis-check --language en
.\dist\ControllerBridge.exe --check-led-rgb captures/led-check --language en
.\dist\ControllerBridge.exe --check-firmware-update captures/firmware-check --language en
```

The configurator build also builds the normal firmware and embeds that exact UF2 with its SHA-256 manifest. Outputs are `build/ControllerBridge.uf2`, `dist/ControllerBridge.exe`, and a separate `dist/ControllerBridge.uf2` for manual recovery. Omit `-Restore` when GUI dependencies are already restored. Firmware tools default to `%USERPROFILE%\.pico-sdk`; use `-PicoSdkPath` and `-PicoToolsRoot` for another existing installation. Developer previews accept `--preview --language en` or `--preview --language ja`.

Current checks pass: 72 LED setting checks, 134 GUI self-tests, 44 connection checks, and 82 firmware workflow/dialog checks in each language. Host-side C checks include exact RGB-to-PIO output and profile color persistence. The updater checks use simulated devices. A read-only probe of the connected board verified USB identification and the unsupported-version response; physical programming and the BOOT-to-application transition still require hardware validation. See [verification and its limits](docs/verification.md).

## Memory budget

Normal firmware allocation figures, calculated from its binary and linker sections:

| Resource | Used or reserved | Remaining |
|---|---:|---:|
| Flash, 2,097,152 bytes | Code/initial values 86,460; persistent storage 32,768 | 1,977,924 |
| SRAM, 532,480 bytes | Static data 123,444; two stacks 8,192 | 400,844 |
| One bank copy, 8,192 bytes | Payload 8,148 + header 16 | 28 |
| Saved measurement, 4,096 bytes | Payload 3,784 + header 16 | 296 |

Static SRAM is vector table 272 + initialized data 31,920 + BSS 91,252 bytes. The minimum heap reservation is included in remaining SRAM. These are not peak stack/heap measurements. The profile remains 488 bytes, the bank 8,148 bytes, and the UF2 file 173,056 bytes.

## Repository contents and license

Diagnostic output under `captures/`, build outputs, release binaries, and caches are ignored. Pico-PIO-USB is vendored without its Git history and verified against recorded hashes.

Project code is provided under the [BSD 2-Clause License](LICENSE), copyright 2026 HKey2. Third-party code retains its own licenses under `third_party/` and `licenses/`.
