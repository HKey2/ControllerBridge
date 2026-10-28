# Current verification

Applies to ControllerBridge Windows configurator and normal firmware 0.18.1. This release fixes the red/green reversal reported on the actual board by changing the final LED wire order to RGB. Custom, status, and diagnostic colors share the same board-order conversion. Default dim blue and the low-brightness notification levels are preserved. Custom colors use the three previously reserved profile bytes and flag bit 3 without increasing the stored sizes. It preserves the Flash storage identity introduced in 0.16.0, binary profile/bank layout version 5, and JSON backup/export version 2.

## Build and software checks

The 2026-10-28 configurator update fixes the display-only LED preview gamma at 4.0 and adds synchronized six-digit RGB code entry. A follow-up accepts a leading `#` on paste and embeds the supplied application icon. Firmware and the embedded/distributed UF2 remain the same 0.18.1 image (SHA-256 `703D81A2E0116582B5F30ACEFE0E4468D509F97C7209C40C81FA0E2073A18AE3`). No physical device was written for this update.

| Check | Result |
|---|---|
| Normal firmware Release build | Passed with the existing Pico SDK 2.1.1 and Arm GCC 14.2.1 |
| Windows self-contained publish | Passed with the existing .NET 9 SDK; output `ControllerBridge.exe` |
| LED setting UI and persistence | 162 checks passed in Japanese and English; fixed preview curve, native hex paste with an optional # prefix, bidirectional synchronization, invalid-code rejection, exact RGB, default/off, draft/save/cancel, duplicate, reorder, JSON, and layout |
| LED output driver | 45 checks passed using the real driver with simulated PIO/time |
| Previous 0.18.0 GUI self-test | 134 checks passed in Japanese and English |
| Firmware workflow and dialogs | 82 checks passed in Japanese and English, using simulated devices |
| Previous 0.18.0 connection UI and automatic guidance | 44 checks passed in Japanese and English, using simulated connection states |
| Previous 0.17.1 attached older bridge startup | 7 read-only checks passed; `UnknownFirmware` detected, one guide, three retries over 6.8 seconds after declining |
| Previous 0.17.1 output tabs and profile menu | 16 and 23 checks respectively passed in each language |
| Previous 0.17.1 read-only device inspection | Connected HID bridge and its USB location identified; old firmware did not support command 47 |
| Previous 0.16.0 axis-dialog verification | 64 English checks passed; implementation unchanged |
| Current C storage/runtime/LED verification | 12,919 deterministic checks passed, including 74 profile-output/color checks and 45 LED-driver checks |
| Previous 0.16.0 diagnostic CLI verification | 16 checks passed; implementation unchanged |

LED checks verify exact RGB888 wire offsets and unchanged profile/bank sizes, default versus explicit black, full 255 values, canonical default reset, draft isolation, save, duplication, reorder, individual JSON and whole-bank backups. The real C driver is run against simulated PIO writes and time: checks cover the RGB wire order inferred from the user's physical observation, MSB-first 24-bit PIO setup, separate red/green/blue at both full and low levels, unequal channel values, diagnostic primary colors and logical RGB telemetry, unchanged-color suppression, changing one custom color to another, full FIFO retry, default/white/off, save success/failure, profile-number flashes, error recovery, host waiting, disconnect, and diagnostic override. Profile runtime tests exercise persisted colors through selection, BOOT/shortcut cycling, reload, reorder and default reset. These checks establish software values and state transitions; the user reported the physical red/green reversal in 0.18.0. The corrected 0.18.1 emission and Flash writes have not been physically verified by the agent. See the [wire-order evidence](status-led.md#wire-order-and-physical-observation).

The preview update checks known gamma-4.0 reference points, monotonicity over the 8-bit domain, exact black, native paste of `000004`, `#000004` and mixed-case codes, prefix removal with caret preservation, spinner/code synchronization, malformed/incomplete/duplicate-prefix/embedded-prefix/overlong strings, recovery by editing a spinner, default mode, OK/cancel/reopen behavior, and unchanged profile bytes and dirty state. No per-PC preview preference is accessed. These checks validate the software mapping, not physical LED-to-monitor brightness agreement.

Japanese and English LED dialogs, including the low-brightness code example and invalid-input state, were rendered from the current executable and visually inspected. Their layout checks include the main window at normal/minimum sizes. The firmware workflow results are retained from the original 0.18.1 build; the same firmware is embedded in this GUI update. The unchanged connection UI and general GUI results are retained from 0.18.0. Read-only hardware observations below are retained from 0.17.1 and were not repeated for this release.

The icon check decodes all nine ICO sizes, compares every pixel of its 64-pixel frame to the supplied PNG, and compares the icon extracted from the published EXE to its corresponding ICO frame. All comparisons passed. The main window and LED dialog captures show the icon in their title bars. Taskbar and Explorer cache refresh were not separately exercised.

The updater checks exercise exact same-version exit, lower/higher/prerelease mismatches, both manual confirmations, cancellation at each prompt, failure at probe/BOOT/write/verification, and post-write version mismatch. They validate the actual embedded package checksum, UF2 family/address/block limits, identity decoding, RP2350 disk markers, and ambiguity rejection. Dialog checks cover text fit, default No, closing confirmations, blocking closure while writing, and final results. Japanese and English screenshots were visually inspected.

Connection checks cover no supported interface, missing version support, lower/higher versions, and transport errors. Across three repeated attempts in each settled state, enabled changes, text changes, invalidations, layout events, and editor rebindings were all zero, with no repeated guide. They also exercise preserving edits made during discovery, overlapping attempt suppression, foreground-operation priority, modal-editor deferral, stable empty-input host retries, and one final live-label update per input batch. Known-version mismatch and factory/other-software states are simulated; they have not been physically flashed for this verification.

The attached physical HID bridge was independently identified as `UnknownFirmware`, rather than treating failure to discover any device as a hardware test. Its version-query response is unavailable. The real automatic BOOT guide was captured and declined; over the following 6.8 seconds, three background retries caused zero enabled/text changes, invalidations, layouts, editor rebindings, or additional guides. These counters establish that the application did not request periodic UI changes in this scenario; they are not a video/frame-timing measurement of the display compositor.

Read-only inspection successfully located the connected bridge's USB parent and location path and received an unsupported-command response for firmware version query. It did not reboot the board or change any firmware, profile or setting. BOOT drive mapping, actual UF2 transfer, physical disconnection timing, and the full automatic return to the HID application have not yet been exercised on hardware. Success in simulated tests is not presented as a hardware flash test.

Backup checks cover current-format round trips, rejection of previous JSON versions and unrelated product identifiers, checksums, exact bank bytes, identity, and atomic replacement. Binary and JSON profiles from versions 1-4 are rejected without conversion.

The C checks include 39 checks of the actual storage implementation with a simulated Flash array: old header rejection, current writes and reloads, CRC failures, unused slots, and host-pause failures. Runtime checks confirm 16 unused slots at startup, with no automatically registered controller. Profile and bank tests cover new saves, reloads, selection persistence, and failed writes. These are host-side checks; physical Flash persistence and registration after installation have not been exercised on a board.

Profile checks cover stored numbered names, copying a copy, ordering, UTF-8 truncation, name width, the row-specific context menu, JSON round trips, duplicate names and IDs, unsaved import, untouched other slots, and invalid/oversized files. These checks use preview data and do not write a physical device.

The firmware dialog screenshots are generated from the current executable and visually inspected. The analog-dialog image and its checks are retained from 0.16.0; axis input was synthetic through the normal notification path and does not establish physical stick calibration. The standard file dialogs were not manually exercised in this verification.

The GUI contains its own diagnostic entry points, so the following checks run from a fresh build. Each writes its results to the given output directory.

```powershell
.\tools\build-diagnostic.ps1 -Target ControllerBridge
.\tools\build-configurator.ps1 -Restore
.\dist\ControllerBridge.exe --self-test captures/gui-check --language en
.\dist\ControllerBridge.exe --check-led-rgb captures/led-check --language en
.\dist\ControllerBridge.exe --check-profile-menu captures/profile-check --language en
.\dist\ControllerBridge.exe --check-axis-dialog captures/axis-check --language en
.\dist\ControllerBridge.exe --check-firmware-update captures/firmware-check --language en
.\dist\ControllerBridge.exe --check-connection-ui captures/connection-check --language en
.\dist\ControllerBridge.exe --check-startup-readonly captures/startup-check --language ja
.\dist\ControllerBridge.exe --inspect-firmware captures/firmware-readonly.json
```

No SDK/runtime is installed or updated by these scripts. The target framework remains `net9.0-windows`. The board has not been flashed by this change. Use the new **Firmware update** workflow to install the embedded firmware. Settings written by 0.16.0 remain supported; older Flash records are ignored.

## Current normal-build memory

| Resource | Used or reserved | Remaining |
|---|---:|---:|
| Flash, 2,097,152 bytes | Code/initial values 86,460; persistent storage 32,768 | 1,977,924 |
| SRAM, 532,480 bytes | Static data 123,444; two stacks 8,192 | 400,844 |
| One bank copy, 8,192 bytes | Payload 8,148 + header 16 | 28 |
| Saved measurement, 4,096 bytes | Payload 3,784 + header 16 | 296 |

Static SRAM consists of vector table 272, initialized data 31,920, and BSS 91,252 bytes. The linker minimum heap of 2,048 bytes is part of the remaining SRAM, not an additional subtraction. These are allocation figures, not measured peak stack or free-heap values.

These figures were remeasured from the 0.18.1 ELF. Compared with 0.18.0, the shared RGB output conversion reduces Flash code/initial values by 32 bytes; static SRAM is unchanged. The profile remains 488 bytes, with all three tail bytes used for RGB888. The bank remains 8,148 bytes and the UF2 is 173,056 bytes. Optional diagnostic builds have separate memory requirements.

The fixed preview and RGB code entry update uses this identical firmware image. It adds no board Flash or SRAM allocation; all figures above remain current.
