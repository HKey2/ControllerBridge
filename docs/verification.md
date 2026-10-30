# Current verification

Applies to ControllerBridge Windows configurator and normal firmware 0.19.0. This release adds first-empty-slot selection for an input without a profile and guards registration against connection changes and occupied destinations. It retains the RGB wire order introduced in 0.18.1. Custom, status, and diagnostic colors share the same board-order conversion. Default dim blue and the low-brightness notification levels are preserved. Custom colors use the three previously reserved profile bytes and flag bit 3 without increasing the stored sizes. It preserves the Flash storage identity introduced in 0.16.0, binary profile/bank layout version 5, and JSON backup/export version 2.

## 2026-10-30 input-log repainting

The flicker came from unconditional whole-list invalidation on every 100 ms refresh, a native ListView without double buffering, and a two-step status-label assignment when a notice was retained. The latter temporarily removed the second line and resized the grid twice per update. Updating `VirtualListSize` also requested a whole native count refresh; append notifications now preserve the viewport and limit invalidation to affected rows.

Native-window probes measured the original and corrected code using 12 deterministic updates per scenario at 96 DPI:

| Scenario | Original | Corrected |
|---|---|---|
| Visible append: full-client paint regions | 12 | 0 |
| Offscreen append, following off: list paints | 12 | 0 |
| Retained notice: grid size changes | 24 | 0 |
| Retained notice: status text assignments | 24 | 12 |
| Full 2,000-row ring: full-client paint regions | 12 | 0 |
| Native ListView double-buffer style | Disabled | Enabled |

The repaint suite passes 44 checks in both Japanese and English, covering visible/offscreen/following appends, a retained notice, ring rotation, idle ticks, same-count snapshot replacement after clear, scroll position, newest row contents, and actual native buffering. The existing 33 input-log checks also pass in both languages. These are native paint/layout measurements with synthetic input, not USB timing or frame-by-frame compositor measurements. Necessary changed-row painting and background handling still occur inside the buffered native control. The 100 ms display cadence and input event retention are unchanged. This PC-only change leaves the firmware and board memory figures below unchanged.

## 2026-10-30 registration input log and stable analog readout

The input-log suite passed 33 checks in Japanese and English using synthetic parsed fields and the normal GUI input-delivery path. It covers duplicate button Usage names with distinct indexes/report locations, snapshots and missing values, press/release events, signed hexadecimal values, device-timestamp wrap, baseline draining, mapping while logging or paused, resume-gap notices, clear/reopen, bounded retention, export contents, and overrun handling. Actual modal `ShowDialog` checks verify that both the wizard and sibling log remain enabled while the main editor is disabled, and that cancelling retains the finished log. The checks invoke no profile saves and perform no hardware writes.

The analog-dialog suite passed 82 checks in each language. Nine values from zero through signed 32-bit extremes verify that the live label, **Record center**, and **Use descriptor range** retain identical bounds and that the full numeric text fits. Existing calibration, source detection, waiting/offline states, and six-/32-axis layouts also pass. Japanese and English log/wizard captures and short-/long-value analog captures were visually inspected at 96 DPI. These checks do not establish physical controller sampling timing or lossless recording under sustained event-ring overload.

This is a PC GUI change using the existing firmware 0.19.0 event stream. The firmware, supplied icon, saved profile formats, and board memory figures below are unchanged. The log's 2,000-row limit applies to PC memory, not board storage.

The final self-contained executable passed those 33 input-log and 82 analog-dialog checks in both languages. It also passed the existing 134 GUI self-tests, 42 controller-arrival checks, and 44 connection UI checks in Japanese. It was published with the installed .NET 9 SDK; the embedded UF2 remains the 0.19.0 image with the SHA-256 listed below.

## 2026-10-30 connected-profile appearance

The connected controller's active profile uses bold text, a blue outline, and a Connected label in the list. Selection highlighting remains independent, and the editor's fonts are unchanged. The displayed-row cache follows the connected active slot, so a disconnect invalidates the former highlighted row. Japanese and English previews were inspected with the connected row both selected and unselected; the existing 44 connection UI checks passed in each language. The firmware, supplied icon, and board memory allocation are unchanged.

## 2026-10-30 unused-slot name appearance

The profile-name text box is disabled and uses the gray surface background when the selected slot is unused. A registered slot restores its normal editable input appearance. Japanese and English captures of both states were visually inspected. The updated executable passed the existing 42 input-arrival and 44 connection UI checks in each language. This is a GUI-only change: the supplied PNG/ICO and embedded firmware 0.19.0 are unchanged, so the board memory figures below remain current.

## 2026-10-30 input arrival and guarded registration

The Japanese and English executable each passed 42 input-arrival checks. They exercise first-slot ordering, saved and unsaved profile matches, dirty/deleted-slot protection, initialization/unsupported input, stale bank/input snapshots, deferred modal/foreground operations, one-time selection, manual browsing, reconnection, full-bank text, recovery when a slot becomes available, and the serialized registration identity. The arrival path only changes the PC list selection. Both languages' empty-slot and full-bank captures were visually inspected.

Forty deterministic checks run the real C runtime against simulated storage and parsed input descriptors. They cover successful registration, duplicate commit rejection, occupied slots, stale bank/attachment data, same-model reconnect, different input, loss during transfer and after its final chunk, every guarded identity component, mismatched staged profile/mapping data, storage failure/retry, and ordinary offline profile editing. These checks verify the firmware transaction logic, not physical USB/Flash timing. No physical board was programmed for this update.

The supplied PNG/ICO are preserved. The published EXE icon was compared against the supplied ICO. The embedded and distributed 0.19.0 UF2 has SHA-256 `E2004081E0562335C3F91A4B25345690967FD213D2BB65835FE34C109414DEF7`. Existing profile/bank version 5, JSON version 2, and current Flash records remain compatible. The new GUI requires its bundled 0.19.0 firmware for guarded registration.

## Disconnected startup


No detected bridge is now a normal disconnected waiting state on the first discovery as well as later attempts. It does not set a startup error or open a firmware update confirmation. A detected bridge with unknown or different firmware still opens its guide once; manual firmware update remains available for first installation and BOOT mode.

The published EXE passed 44 connection UI checks and 82 firmware workflow/dialog checks in each language. Real USB discovery returned `NotDetected` in Japanese and English, with zero guides at startup and through three retries over 6.8 seconds. Those retries caused no enabled/text changes, invalidations, layouts, or editor rebindings. The supplied PNG and ICO were preserved byte for byte without regeneration; the icon extracted from the published EXE matches the supplied ICO pixel for pixel. No hardware was written; current 0.19.0 memory figures are listed below.

## 2026-10-29 window and icon update

The supplied PNG icon was updated and the ICO/EXE rebuilt. Its 64-pixel frame matches the source pixel for pixel; the icon extracted from the published EXE matches the generated ICO. Both language captures show the new icon.

The initial client height increased from 1000 to 1100 (width remains 1290), and the minimum window size now matches the initial size. At 96 DPI, the complete window is 1306 × 1139. Before the change, the combined controller editor had 408 pixels available for 459 pixels of content and required vertical scrolling. It now has 508 pixels, leaving 49 pixels below the complete content. Japanese and English renders cover gamepad, USB mouse, both combined button modes, an unused slot and button learning; all have no horizontal or vertical editor scrollbar. The minimum-size captures also have no editor scrollbar. The English Clear mapping button's column now sizes to its label to avoid clipping its second line.

The LED/settings suite was rerun on the final executable: 162 checks passed in each language. The screenshots and measurements use the current 96-DPI desktop; they do not establish fit on every smaller working area or display scaling. No hardware was written; current 0.19.0 memory figures are listed below.

## Build and software checks

The current 0.19.0 executable and firmware were rebuilt using the installed toolchains. The LED preview remains fixed at gamma 4.0, with synchronized six-digit RGB entry accepting an optional leading `#`.

| Check | Result |
|---|---|
| Normal firmware Release build | Passed with the existing Pico SDK 2.1.1 and Arm GCC 14.2.1 |
| Windows self-contained publish | Passed with the existing .NET 9 SDK; output `ControllerBridge.exe` |
| LED setting UI and persistence | 162 checks passed in Japanese and English; fixed preview curve, native hex paste with an optional # prefix, bidirectional synchronization, invalid-code rejection, exact RGB, default/off, draft/save/cancel, duplicate, reorder, JSON, and layout |
| LED output driver | 45 checks passed using the real driver with simulated PIO/time |
| GUI self-test | 134 checks passed in Japanese and English |
| Firmware workflow and dialogs | 82 checks passed in Japanese and English, using simulated devices |
| Connection UI and automatic guidance | 44 checks passed in Japanese and English, including silent disconnected startup, manual reconnect, and guidance after a later detection |
| Disconnected startup with real USB discovery | 3 read-only checks passed in each language; `NotDetected`, zero guides, and three background retries over 6.8 seconds |
| Previous 0.17.1 attached older bridge startup | 7 read-only checks passed; `UnknownFirmware` detected, one guide, three retries over 6.8 seconds after declining |
| Previous 0.17.1 output tabs and profile menu | 16 and 23 checks respectively passed in each language |
| Previous 0.17.1 read-only device inspection | Connected HID bridge and its USB location identified; old firmware did not support command 47 |
| Previous 0.16.0 axis-dialog verification | 64 English checks passed; implementation unchanged |
| Controller arrival UI | 42 checks passed in each language |
| Guarded registration runtime | 40 deterministic C checks passed |
| Current C storage/runtime/LED verification | 12,959 deterministic checks passed, including the new registration checks, 74 profile-output/color checks and 45 LED-driver checks |
| Previous 0.16.0 diagnostic CLI verification | 16 checks passed; implementation unchanged |

LED checks verify exact RGB888 wire offsets and unchanged profile/bank sizes, default versus explicit black, full 255 values, canonical default reset, draft isolation, save, duplication, reorder, individual JSON and whole-bank backups. The real C driver is run against simulated PIO writes and time: checks cover the RGB wire order inferred from the user's physical observation, MSB-first 24-bit PIO setup, separate red/green/blue at both full and low levels, unequal channel values, diagnostic primary colors and logical RGB telemetry, unchanged-color suppression, changing one custom color to another, full FIFO retry, default/white/off, save success/failure, profile-number flashes, error recovery, host waiting, disconnect, and diagnostic override. Profile runtime tests exercise persisted colors through selection, BOOT/shortcut cycling, reload, reorder and default reset. These checks establish software values and state transitions; the user reported the physical red/green reversal in 0.18.0. The corrected 0.18.1 emission and Flash writes have not been physically verified by the agent. See the [wire-order evidence](status-led.md#wire-order-and-physical-observation).

The preview update checks known gamma-4.0 reference points, monotonicity over the 8-bit domain, exact black, native paste of `000004`, `#000004` and mixed-case codes, prefix removal with caret preservation, spinner/code synchronization, malformed/incomplete/duplicate-prefix/embedded-prefix/overlong strings, recovery by editing a spinner, default mode, OK/cancel/reopen behavior, and unchanged profile bytes and dirty state. No per-PC preview preference is accessed. These checks validate the software mapping, not physical LED-to-monitor brightness agreement.

The input-arrival, connection UI, firmware workflow, LED/settings and general GUI self-tests were run on the 0.19.0 executable in both languages. The attached-device observations below are retained from 0.17.1; the disconnected real USB discovery check was repeated with 0.19.0. The earlier LED dialog visual inspection remains applicable to its unchanged layout.

The earlier icon generation check decoded all nine generated ICO sizes and compared its 64-pixel frame to the PNG. For this release, the separately supplied PNG and ICO are preserved byte for byte, and the icon extracted from the EXE matches the supplied ICO pixel for pixel. The main window and LED dialog captures show the icon in their title bars. Taskbar and Explorer cache refresh were not separately exercised.

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
.\dist\ControllerBridge.exe --check-controller-arrival captures/arrival-check --language en
.\dist\ControllerBridge.exe --check-profile-menu captures/profile-check --language en
.\dist\ControllerBridge.exe --check-axis-dialog captures/axis-check --language en
.\dist\ControllerBridge.exe --check-firmware-update captures/firmware-check --language en
.\dist\ControllerBridge.exe --check-connection-ui captures/connection-check --language en
.\dist\ControllerBridge.exe --check-startup-readonly captures/startup-check --language ja
.\dist\ControllerBridge.exe --inspect-firmware captures/firmware-readonly.json
.\dist\ControllerBridge.exe --check-input-log captures/input-log --language ja
.\dist\ControllerBridge.exe --check-input-log-paint captures/input-log-paint --language ja
.\dist\ControllerBridge.exe --check-axis-dialog captures/axis-dialog --language ja
```

No SDK/runtime is installed or updated by these scripts. The target framework remains `net9.0-windows`. The board has not been flashed by this change. Use the new **Firmware update** workflow to install the embedded firmware. Settings written by 0.16.0 remain supported; older Flash records are ignored.

## Current normal-build memory

| Resource | Used or reserved | Remaining |
|---|---:|---:|
| Flash, 2,097,152 bytes | Code/initial values 86,884; persistent storage 32,768 | 1,977,500 |
| SRAM, 532,480 bytes | Static data 123,448; two stacks 8,192 | 400,840 |
| One bank copy, 8,192 bytes | Payload 8,148 + header 16 | 28 |
| Saved measurement, 4,096 bytes | Payload 3,784 + header 16 | 296 |

Static SRAM consists of vector table 272, initialized data 31,920, and BSS 91,256 bytes. The linker minimum heap of 2,048 bytes is part of the remaining SRAM, not an additional subtraction. These are allocation figures, not measured peak stack or free-heap values.

These figures were remeasured from the 0.19.0 ELF/linker map. Compared with 0.18.1, guarded registration adds 424 Flash bytes and 4 static SRAM bytes. The profile remains 488 bytes, with all three tail bytes used for RGB888. The bank remains 8,148 bytes and the UF2 is 174,080 bytes. Optional diagnostic builds have separate memory requirements.

The GUI arrival tracker runs on the PC. The firmware guard adds only transient state; it does not enlarge stored profiles, banks, or their reserved Flash areas.
