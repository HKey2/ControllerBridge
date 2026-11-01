# ControllerBridge

ControllerBridge turns USB controllers into Switch-compatible gamepad and mouse inputs using an unmodified **Waveshare RP2350-USB-A** board. Connect the input controller to USB-A and USB-C to a Windows PC for configuration, or to the Switch 2 for play.

**Current development release: firmware 0.20.11 and Windows configurator 0.20.11.** The application uses Japanese on Japanese Windows and English otherwise. Repository documentation and screenshots are English; profile names remain as entered.

Trackball pointer gain remains **1.0–256.0× (default 16.0)** and paddle pointer gain **1.0–10.0× (default 1.0)**, in 0.1 steps. Saved values are unchanged. The **Pointer gain (×)** fields remain editable while conversion is enabled. In **Mouse + Gamepad**, conversion adds D-pad clicks while both pointer sources continue moving the mouse. No physical D-pad input or assignment is required. Direction conversion uses separate, hidden device-wide parameters, initially **6.0× for the trackball and 1.5× for the paddle**, stored independently of profile pointer gain. Enable **Convert to ↑↓←→** for the trackball, or **Convert to ←→ / ↑↓** for the paddle. The paddle follows its X/Y output axis. Each signed source sum emits one press/release pair at the configured threshold (initially ±64), then clears and discards excess. **Mouse UDLR settings**, immediately right of **Profile switching...**, edits the device-wide **Movement threshold** (default **64 pt**, range **1–256**), **Hold time** (default **30 ms**, range **1–512**) and **Wait time** (default **20 ms**, range **1–512**). Save writes all three settings to the device and verifies them by reading back; all profiles share them. Hold time controls the pressed phase, and the independent wait time controls the released interval before the next click. Each phase starts at its corresponding successful USB completion. The dialog shows units beside the spinners without range labels. Cursor gains remain hidden. These defaults apply to newly initialized settings. Valid saved durations, including 16/16 ms, are retained. See [movement conversion](docs/configurator-guide.md#movement-to-d-pad).

Paddle rotation can be assigned per profile to **X (horizontal)** or **Y (vertical)** using the **Output axis** list in the mouse settings. Existing profiles remain X. Gain and rotation reversal apply to the selected axis; trackball movement retains its own X/Y settings. Install the matching 0.20.11 firmware, choose the axis, then **Save profile to device**. Profile and bank sizes are unchanged. See the [paddle settings](docs/configurator-guide.md#paddle-output-axis).

Rapid fire supports **1–60 press/release cycles per second**, with **20** as the starting value when enabling an output that was Off. Existing saved rates remain unchanged, and Off remains Off until enabled. The bundled firmware also supports this range. Profile and backup formats are unchanged.

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
- **Back up all slots** and **Restore all slots** handle the whole bank in one JSON file. Current backups use `ControllerBridge.Slots`, JSON version 3. Version-2 backups and individual exports are imported with shift disabled; version 1 remains unsupported.
- **Button shift…**, in the mapping editor, learns one to four physical keys. Holding all keys activates a button-only shifted page; unchanged outputs inherit the main mapping. Choose **Apply to draft**, then save the slot. All four axes stay common. See the [shift guide](docs/button-shift.md) for consumption, remapping and shortcut rules.
- **Mouse UDLR settings** opens the shared movement-threshold/hold-time/wait-time dialog. Choose **Save to device** to apply and persist it independently of any unsaved profile edits. See [settings](docs/configurator-guide.md#mouse-udlr-settings).
- **Profile switching...** configures a roughly one-second BOOT hold or a controller shortcut. The selected profile is retained across reconnections.

Selecting an output tab edits the profile. Choose **Save profile to device** to persist it. Browsing the list does not change the active device profile; **Use this profile** activates another matching saved profile.

| Output tab | Behavior |
|---|---|
| Gamepad | Buttons, directions, and stick axes as a Switch-compatible gamepad |
| USB mouse | Relative movement and mouse buttons |
| Mouse + Gamepad | Separate mouse and gamepad HID interfaces |

Only the combined tab offers **Button behavior: Mouse / Controller**. Mouse behavior sends left/right clicks and gamepad Minus/Plus/HOME. Controller behavior sends all mapped gamepad controls while the mouse supplies movement. A USB layout change reconnects USB-C while the USB-A input host continues running.

For the EGRET II mini Paddle Controller, trackball and paddle can be enabled independently. Trackball gain is 1.0-256.0, default 16.0; paddle gain is 1.0-10.0, default 1.0, both in 0.1 steps. Input interval `0` uses the controller descriptor; `1` requests input every millisecond. NAK responses mean request rate is not the same as fresh data rate.

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
.\dist\ControllerBridge.exe --check-button-shift captures/shift-check --language en
.\dist\ControllerBridge.exe --check-led-rgb captures/led-check --language en
.\dist\ControllerBridge.exe --check-firmware-update captures/firmware-check --language en
.\dist\ControllerBridge.exe --check-mouse-udlr-settings captures/mouse-udlr-check --language en
```

The configurator build also builds the normal firmware and embeds that exact UF2 with its SHA-256 manifest. Outputs are `build/ControllerBridge.uf2`, `dist/ControllerBridge.exe`, and a separate `dist/ControllerBridge.uf2` for manual recovery. Omit `-Restore` when GUI dependencies are already restored. Firmware tools default to `%USERPROFILE%\.pico-sdk`; use `-PicoSdkPath` and `-PicoToolsRoot` for another existing installation. Developer previews accept `--preview --language en` or `--preview --language ja`.

The 0.20.11 default-setting change passes **46,727 deterministic C checks**, **614 normal GUI checks**, and **630 test GUI checks**. The settings and bundled-firmware checks run in Japanese and English, with dialog layouts at 100%, 150% and 200% scaling. They verify 30 ms hold / 20 ms wait for new settings, retained saved values and the legacy shared-duration migration. No physical board was accessed for this default-only change. Earlier 0.20.10 hardware evidence remains in [verification and its limits](docs/verification.md).

The U/D/L/R USB keyboard test is kept in the source and compiled only by `tools/build-udlr-test.ps1`. Normal builds omit its dialog, button, keyboard descriptor and command 50. See [test build and usage](docs/udlr-test.md).

## Memory budget

Normal firmware allocation figures, calculated from its binary and linker sections:

| Resource | Used or reserved | Remaining |
|---|---:|---:|
| Flash, 2,097,152 bytes | Code/initial values 101,352; persistent storage 49,152 | 1,946,648 |
| SRAM, 532,480 bytes | Static data 153,140; two stacks 8,192 | 371,148 |
| One bank copy, 16,384 bytes | Payload 13,908 + header 16 | 2,460 |
| Saved measurement, 4,096 bytes | Payload 3,784 + header 16 | 296 |
| Common settings, 4,096 bytes | Legacy header/payload 56 + alignment 8 + direction records 64 | 3,968 |

Static SRAM is vector table 272 + initialized data 35,456 + BSS 117,412 bytes. The minimum heap reservation is included in remaining SRAM. These are not peak stack/heap measurements. A profile is 848 bytes, the bank is 13,908 bytes, and the UF2 file is 202,752 bytes.

## Repository contents and license

Diagnostic output under `captures/`, build outputs, release binaries, and caches are ignored. Pico-PIO-USB is vendored without its Git history and verified against recorded hashes.

Project code is provided under the [BSD 2-Clause License](LICENSE), copyright 2026 HKey2. Third-party code retains its own licenses under `third_party/` and `licenses/`.
