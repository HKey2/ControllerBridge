# Current verification

Applies to ControllerBridge Windows configurator and normal firmware 0.20.1. This update expands rapid fire to 1–60 cycles per second with a new starting rate of 20, while retaining saved rates. The button-only shifted page, AND modifiers, inherited mappings and version-6 profile/bank format remain supported. JSON exports use version 3 and retain version-2 import support. Dated entries below describe their own builds and checks; older hashes and measurements are historical. Current allocation figures are in [Current normal-build memory](#current-normal-build-memory).

## 2026-11-01: profile save wording and shared settings reminder

The profile action is now **Save profile to device** (Japanese: **プロファイルをデバイスに保存**). Unsaved-edit, duplicate, import and mouse-mapping guidance uses the same action name. The rapid-fire dialog title and heading are now **Rapid fire settings**. Its Japanese range and unavailable-button guidance follows the requested wording, and the former held-button explanatory line has been removed from that dialog.

Rapid fire, LED RGB, button shift and axis settings all use one shared localized reminder: changes are not written until **Save profile to device** is chosen on the profile screen. Immediate-write dialogs such as profile switching and firmware updates retain guidance matching their own save behavior. The axis window reserves the measured height of the new reminder when arranging its input list and calibration controls.

The final self-contained EXE passed **361 checks per language, 722 total**: rapid fire 23, shift 55, LED 162, axis 82, profile menu 23 and output tabs 16. Japanese and English captures were visually inspected. Results are in `build/save-wording/checks-final/summary.json`. The first axis run exposed a one-pixel vertical overflow after adding the reminder; the layout now reserves its wrapped height and both final axis suites pass. The original diagnostic remains in `build/save-wording/checks/ja/axis`.

The existing DPI runner also passed **13,157 checks per language, 26,314 total**, including simulated transitions 96 → 120 → 144 → 192 → 96 → 192 → 96 and bounded-window checks. Results are in `build/save-wording/dpi/summary.json`. Windows display settings were not changed; this is a controlled layout check, not a physical monitor-move test.

This is a GUI wording/layout update using the existing .NET 9 SDK. No physical device was opened or programmed. Firmware 0.20.1 and its UF2 remain byte-identical (`3E7BCE463636A2F4EA0F622420D6F98917669CB6440D8BB13F3077434FD1985C`). Board allocation is unchanged: Flash code/initial values 96,100 bytes plus 49,152 reserved, leaving 1,951,900; static SRAM 151,588 plus 8,192 for stacks, leaving 372,700. Profile and bank payload sizes remain 848 and 13,908 bytes.

## 2026-11-01: rapid-fire range 1–60 and new default 20

GUI controls, profile validation and firmware capabilities now agree on **1–60 complete press/release cycles per second**, in steps of one. Zero still means Off. The GUI shows 20 when enabling a currently Off output; saved nonzero values such as 2, 7, 10 and 30 remain unchanged. Opening or applying the dialog without enabling an Off output preserves zero. The same rules apply to main and shifted button pages. Wire fields, version-6 profiles, 16-slot banks and JSON version 3 are unchanged. The version is 0.20.1 so the bundled updater can distinguish firmware that accepts the wider range.

- Normal RP2350 firmware builds with the existing pinned toolchain. UF2: 192,512 bytes; SHA-256 `3E7BCE463636A2F4EA0F622420D6F98917669CB6440D8BB13F3077434FD1985C`.
- **45,930 C checks passed**, including **44,164 rapid-fire checks**. Every integer rate from 1 through 60 is checked immediately before and at each mathematical half-cycle boundary over three seconds. Existing independent-button, release/repress, output-pause, timer-wrap, axis and C# wire-fixture cases remain. Runtime capabilities advertise minimum 1 and maximum 60.
- **17 CLI checks passed** against the unchanged profile and snapshot formats.
- The self-contained Windows GUI passes **744 checks per language, 1,488 total**: general self-test 138, rapid fire 23, shift 55, LED 162, updater 124, connection 46, profile menu 23, arrival 42, axis 82, input log 33 and output tabs 16. The rapid dialog checks verify range/default, preservation of stored values, Off/Cancel/Apply behavior, disabled unmapped outputs, main/shifted binary round trips, profile JSON and whole-bank backup at 1, 20 and 60. Japanese and English rapid-setting captures were visually inspected.

The first English self-test stopped at the existing `File.Replace` operation while replacing a test JSON file (`build/rapid-range/checks/en/self/error.txt`). The same EXE passed in a fresh output directory; no production file-save behavior was changed. The underlying intermittent file-operation cause remains undetermined. Final reports retain the Japanese run and English retry in `build/rapid-range/verification.json`.

These are deterministic software checks with simulated inputs/storage. No board was opened or programmed, and this run does not establish physical USB delivery or game acceptance at 60 cycles per second. Flash code/initial values decrease by 48 bytes from 0.20.0; SRAM and persistent allocation sizes are unchanged.

## 2026-11-01: shift key numbers and simultaneous registration

The shift key list, learning feedback and registration review now use the same `InputField.Label` button numbers as the input log and live status. Saved USB source details remain visible to distinguish identical button numbers across interfaces or report IDs. Labels are resolved only with this profile's matching controller descriptor; offline or unrelated descriptors do not supply guessed button numbers.

Choosing **Learn shift keys** captures a gesture from its first new press until all captured inputs are released. Multiple fields, interfaces and report IDs can join the gesture. Existing held-input protection remains active. Already registered keys do not consume capacity twice, and an addition exceeding four total keys is rejected as a whole. Selecting an output, removing a modifier, closing, or losing the connection cancels the pending gesture. Applying the draft while captured keys remain held asks for their release first. See [the updated workflow](button-shift.md).

The final self-contained .NET 9 EXE passed **310 checks per language, 620 total**: shift 55, general self-test 134, input log 33, controller arrival 42 and connection UI 46. The expanded shift cases cover actual modal input delivery from both the main window and registration wizard, multiple changes in one poll and across polls, four-key capacity, duplicate sources, held inputs, single-key additions, cancellation of delayed preparation, hat/trigger ranges, button-number resolution and the registration review. Japanese and English captures of learning, registered keys and the review were visually inspected. Results are in `build/shift-learning/checks/summary.json`.

Two initial runs stopped because new synthetic profiles omitted required VID/name values (`build/shift-learning/initial/ja/error.txt` and `build/shift-learning/gesture-checks/ja/error.txt`). The test fixtures were corrected; production profile validation was not relaxed. No physical USB device was opened or programmed. Real controller timing remains unverified by this GUI test run.

This was a GUI change with no profile/bank format or firmware change. The 0.20.0 UF2 remained byte-identical (`3588776F42A03FCD91E895E3A07C98205E89F8F0ACD439554B485E1B4DC7E30D`). At that point board allocation was Flash 96,148 bytes plus 49,152 reserved (1,951,852 remaining), and static SRAM 151,588 bytes plus 8,192 for stacks (372,700 remaining). The current table below reflects the subsequent firmware build.

## 2026-11-01: DPI layout correction

The reported half-width controls and clipped LED dialog were reproduced with a synthetic 192-DPI startup baseline followed by display on a 96-DPI window. The form was consuming its DPI baseline before its control tree was complete. Forms now defer the initial scaling pass until construction ends. Hidden mapping/wizard pages retain their parents, single-column tables follow the window width during repeated scaling, and owner-drawn secondary fonts use the scaled control font. Windows fit the monitor's working area and provide scrolling when their content minimum is larger than the screen. The English square Capture button displays **CAP**, retaining its full tooltip and accessible name.

The final .NET 9 build passed these checks:

| Verification | Result |
|---|---|
| Japanese/English × initial DPI 96, 120, 144, 192 | All 8 cases passed |
| Native DPI notifications, each language | 96 → 120 → 144 → 192 → 96 → 192 → 96 passed |
| Main output tabs and combined-mode radio changes | Button text fit, mapping-cell dimensions and reachable controls passed at every step |
| LED, shift, rapid fire, axis, profile switching, registration, input log and simulated updater | Visible buttons fit and remain reachable; registration pages keep their mapping size when opened |
| Working areas 1920×1040 and 1280×680 | Window bounds fit; selecting the shift dialog's Apply button scrolls it fully into view |
| DPI assertions across the above cases | 43,912 passed; includes repeated text/bounds checks through control ancestors |
| Published EXE functional suites | 692 per language, 1,384 total passed: self 134, shift 30, LED 162, updater 124, connection 46, profile 23, arrival 42, axis 82, input log 33 and output tabs 16 |

The first English self-test encountered an `IOException` in `File.Replace` while replacing a test JSON file. Its log remains at `build/dpi/function-checks/en/self/error.txt`. The same published EXE passed the self-test and remaining English suites in a fresh output directory without a production-code change. The underlying cause of that file-operation failure has not been established.

The DPI runner uses the .NET 9 startup-DPI cache and native DPI-change notifications for controlled layout checks; it does not change Windows display settings. Both connected monitors were actually at 96 DPI during verification. Synthetic startup screenshots do not reproduce all native font-rasterization behavior at another system DPI. These checks are not a physical 4K-at-200% / FHD-at-100% monitor-move test. Native 96-DPI captures and captures after simulated DPI transitions were also visually inspected. The images below come from the published EXE at native 96 DPI using synthetic profiles.

![DPI-corrected combined output controls](gui-dpi-main-en.png)

![DPI-corrected LED settings with complete instructions and actions](gui-dpi-led-en.png)

Run the regression matrix after preparing the normal firmware package and restoring the existing GUI dependencies:

```powershell
.\tools\check-gui-dpi.ps1 -OutputDirectory build/dpi-new-run
```

Use a fresh output directory. The runner opens only preview forms and a fake updater device. It records results and captures per case, and leaves OS settings, physical USB devices and saved profiles untouched. The project and runner continue to use the installed .NET 9 SDK. The 0.20.0 firmware UF2 used for that DPI correction remained byte-identical (`3588776F42A03FCD91E895E3A07C98205E89F8F0ACD439554B485E1B4DC7E30D`), so that GUI-only correction did not change board allocation.

## 2026-11-01: button shift and format migration

The RP2350 Release firmware built successfully with the pinned Pico toolchain. The self-contained Windows x64 configurator was published with the existing .NET 9 SDK. The UF2 is 192,512 bytes, SHA-256 `3588776F42A03FCD91E895E3A07C98205E89F8F0ACD439554B485E1B4DC7E30D`.

- C tests: **13,051 checks passed**, including 78 dedicated shift checks using the real mapper, runtime, and Flash store implementation with simulated hardware. Coverage includes AND detection, inherited/remapped/muted outputs, modifier consumption and full-release guard, held inputs, split report IDs, common axes, rapid phases, shortcut conflicts, detach/seed, and no Flash write during page changes. Version-5 migration preserves all 16 slots and metadata; simulated first/second commit interruptions retain a valid bank and preserve common settings/measurements. C also executes the C#-generated version-6 shift fixture.
- CLI: **17 checks passed**, including the 848-byte/version-6 encoding. Existing stateless live-validation scripts accept current profiles only when shift is disabled and explicitly reject shifted profiles; they are not used as a shift oracle.
- GUI: Japanese and English each pass shift 30, general self-test 134, firmware update 124, connection 46, profile menu 23, controller arrival 42, axes 82, input log 33, and LED 162: **676 per language, 1,352 total**. Shift checks open the actual modal dialog from the main window, deliver input through the normal path, apply only to an unsaved draft, and cover cancellation of delayed learning after selecting No output or Inherit from main. The full suites passed before the final learning-cancellation/text polish; affected shift/self/axis/input-log suites were rerun on the final EXE.
- Japanese and English shift captures were inspected for complete controls, readable instructions, override marks, and absence of independent axis controls. The new English [shift screenshot](gui-shift-en.png) uses synthetic settings, not a read from a user's board.

An earlier English connection-suite run failed an existing state assertion. The assertion was extended to record the actual state; a diagnostic rerun and the complete final bilingual run passed. The original failure log is retained under `build/shift/checks/en/connection/error.txt`; its cause remains undetermined. No production connection behavior was changed to make it pass.

No physical USB device was opened or programmed by the shift tests. Real controller/gameplay response, hardware Flash interruption, and timing under hardware load remain unverified. Host tests establish state transitions and simulated recovery, not physical power-loss performance. Direction correction is deferred and unchanged.


## 2026-10-31 cancellable BOOT connection wait

First installation now starts monitoring for a valid BOOT connection as soon as the guide appears. The waiting dialog shows numbered reconnection steps and only **Cancel**, with no preliminary Yes/No question and no manual-wait timeout. Valid BOOT detection advances to the final overwrite confirmation; only Yes starts writing. A readable version mismatch keeps the existing automatic-update flow.

The published executable passed 124 firmware workflow/dialog checks, 46 connection checks, and 134 GUI self-tests in each of Japanese and English. The added deterministic cases cover BOOT detection before final confirmation, an already connected BOOT device, waiting cancellation, cancellation at the detection boundary, closing the wait with the window close button, declining the final confirmation, and BOOT failure preventing confirmation and writing. Cancelling via the close button is completed after the closing event, preventing the dialog from remaining open. Both languages' waiting and final-confirmation captures were visually inspected at 96 DPI.

The attached factory-state board also passed 16 read-only checks per language. Startup and background discovery after a simulated prior absence each opened the real wait with only Cancel. Cancelling both waits left its factory firmware intact, with no additional guides or editor changes through three retries after each cancellation. These results total 320 checks per language, 640 across both languages. The real board was not physically reconnected in BOOT mode or programmed; the detection-to-confirmation transition was exercised with a simulated device. The firmware UF2, supplied icons, and board memory allocations below are unchanged.

## 2026-10-31 factory-state board discovery

The supplied factory-state Waveshare RP2350-USB-A was connected to USB-C during verification. Windows reported product `Pico`, USB parent `2E8A:0009`, a CDC serial interface, and a reset interface. It did not expose the ControllerBridge HID transport or a firmware version. The corrected discovery classifies it as `UnknownFirmware` and shows **Device unsupported**, rather than `NotDetected` / **Device not connected**. Metadata enumeration does not open the serial port or issue reset commands.

Before the BOOT-wait revision above, the executable passed 96 firmware workflow/discovery checks, 46 connection UI checks, and 134 GUI self-tests in each of Japanese and English. The added deterministic cases cover factory/BOOT candidates, unrelated peripherals, composite child rejection, RP2040 BOOT exclusion, stable keys, multiple-candidate rejection, same-port BOOT matching, distinct unsupported status, repeated-prompt suppression, and rearming after an observed disconnect.

Read-only checks against the attached factory board passed 16 checks in each language. The real startup discovery opened the real installation guide, which the check declined. A second scenario simulated only the prior disconnected state, then used the normal background timer and actual USB discovery to find the same attached board and open guidance again. This verifies the connection-time code path; it is not a physical unplug/replug test. After each decline, three discovery retries over 6.8 seconds produced zero extra guides, enabled/text changes, invalidations, layouts, or editor rebindings. Both language captures of the guide and unsupported main-window status were visually inspected.

The factory board remained connected with its original USB identity. No reboot, serial reset, firmware programming, or profile write was performed. Actual first programming and the BOOT-to-application transition remain unverified by this check. The updater now binds a detected factory target to its USB port and still requires RP2350 USB/INFO_UF2 validation and the overwrite confirmation. Shared Pico SDK IDs identify candidates, not a carrier-board model.

Only the PC configurator and documentation changed. The supplied PNG/ICO and the embedded/distributed firmware are unchanged; the UF2 SHA-256 remains `E2004081E0562335C3F91A4B25345690967FD213D2BB65835FE34C109414DEF7`. Board memory allocations and remaining capacity in the table below remain current.

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

## 2026-10-30 disconnected startup (before factory-state detection)


With no recognized board, disconnected startup remains a normal waiting state without a startup error or firmware confirmation. The results in this subsection predate the factory-state detection added above: factory/BOOT USB identities now count as detected unsupported boards and open guidance automatically.

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
| Firmware workflow, discovery and dialogs | 124 checks passed in Japanese and English, using simulated identities/devices, including cancellable BOOT waiting and final confirmation after detection |
| Connection UI and automatic guidance | 46 checks passed in Japanese and English, including silent disconnected startup, unsupported status and guidance after an observed disconnect |
| Factory-state board with real USB discovery | 16 read-only checks passed in each language; `UnknownFirmware`, startup BOOT wait and background arrival after simulated absence, with only Cancel and stable retries after each cancellation |
| Previous disconnected startup with real USB discovery | 3 read-only checks passed in each language; `NotDetected`, zero guides, and three background retries over 6.8 seconds |
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

The updater checks exercise exact same-version exit, lower/higher/prerelease mismatches, immediate manual BOOT waiting, final confirmation only after detection, cancellation during waiting or at the detection boundary, declining confirmation, failure at probe/BOOT/write/verification, and post-write version mismatch. They validate the actual embedded package checksum, UF2 family/address/block limits, identity decoding, RP2350 disk markers, and ambiguity rejection. Dialog checks cover text fit, Cancel-only waiting, closing the wait, default No at final confirmation, closing confirmations, blocking closure while writing, and final results. Japanese and English screenshots were visually inspected.

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
.\dist\ControllerBridge.exe --check-button-shift captures/shift --language ja
```

No SDK/runtime is installed or updated by these scripts. The target framework remains `net9.0-windows`. The board has not been flashed by this change. Use the new **Firmware update** workflow to install the embedded firmware. Settings written by 0.16.0 remain supported; older Flash records are ignored.

## Current normal-build memory

| Resource | Used or reserved | Remaining |
|---|---:|---:|
| Flash, 2,097,152 bytes | Code/initial values 96,100; persistent storage 49,152 | 1,951,900 |
| SRAM, 532,480 bytes | Static data 151,588; two stacks 8,192 | 372,700 |
| One bank copy, 16,384 bytes | Payload 13,908 + header 16 | 2,460 |
| Saved measurement, 4,096 bytes | Payload 3,784 + header 16 | 296 |

Static SRAM consists of vector table 272, initialized data 34,016, and BSS 117,300 bytes. The linker minimum heap of 2,048 bytes is part of the remaining SRAM, not an additional subtraction. These are allocation figures, not measured peak stack or free-heap values.

These figures were measured from the 0.20.1 ELF/linker map. Compared with 0.20.0, code/initial values decrease by 48 bytes; static SRAM and reserved Flash are unchanged. Compared with 0.19.0, code/initial values grow by 9,216 bytes, static SRAM by 28,140 bytes, and reserved Flash by 16,384 bytes. The profile is 848 bytes (488-byte main prefix plus a 360-byte shift block); the bank is 13,908 bytes. The UF2 is 192,512 bytes. Optional diagnostic builds have separate memory requirements.

Button-page transitions use runtime state only; they do not write Flash. Both pages share all four axis settings. The GUI editor and test image buffers allocate PC memory, outside the board figures above.
