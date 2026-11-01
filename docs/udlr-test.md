# UDLR keyboard test — 0.20.10-udlr

This optional build emits U/D/L/R letters through the board's USB-C keyboard interface. Connect the EGRET controller to USB-A and the board to the PC by USB-C. Movement conversion runs on the board and shares the normal firmware's conversion code. This dedicated diagnostic still exposes a keyboard-only output. Use normal 0.20.10 in Mouse + Gamepad mode to test simultaneous pointer movement and D-pad clicks; the UDLR test does not expose a mouse interface.

## Build and launch

```powershell
.\tools\build-udlr-test.ps1
```

The script enables CMake `BRIDGE_UDLR_TEST` and MSBuild `UdlrTest`, uses separate intermediates, and writes `test/udlr/dist/ControllerBridgeUdlrTest.exe` and `ControllerBridgeUdlrTest.uf2`. It uses the existing pinned Pico tools and .NET 9 SDK. The EXE embeds that exact test firmware. Firmware alone can be built with `tools/build-diagnostic.ps1 -UdlrTest -BuildDirectory test/udlr/build`. Normal builds explicitly use the default OFF flag and do not contain this test's dialog, button, keyboard descriptor, gate or command 50. Test source is retained at `apps/egret_switch/udlr_test.h` and `gui/ControllerBridge/UdlrTestDialog.cs`.

Run the dedicated EXE, whose title contains **UDLR TEST**. Enable conversion on the active profile and save it. Open **UDLR test…** at the top right. The read-only **Movement per letter (device setting)** shows the current saved threshold (default 64). To edit it, close the test dialog and open **Mouse UDLR settings** on the main window. Press **Start**, turn IME and Caps Lock off, and operate the controller with the text box focused. **Stop** releases output and writes a JSON trial log beside the EXE under `logs/`. Closing the dialog stops output; loss of dialog communication stops it after approximately two seconds.

## Conversion

- Trackball and paddle conversion use hidden device-wide gains, initially **6.0×** and **1.5×**. These are stored separately from pointer sensitivity and are not displayed or editable in the GUI. Saved profile mouse sensitivity is retained but does not affect the test or normal D-pad conversion. Inversion remains effective.
- Trackball X is L/R, Y is U/D. Paddle **X axis** is L/R and **Y axis** is U/D; **Reverse rotation** reverses the selected pair. The profile UI uses arrows: ↑↓←→, ←→, ↑↓.
- Three independent signed sums retain tenths. Reaching the saved threshold (initially ±64) sends one press/release, clears that sum, and discards excess. Simultaneous crossings are sent ball X, ball Y, paddle. With the initial threshold and cursor gains, a raw +11 ball count or a paddle magnitude of 43 crosses from zero; each produces only one letter even if movement is much larger.
- The shared threshold is stored in the device's common configuration sector and survives reboot/profile switching. **Mouse UDLR settings** edits it from 1–256 pt, independently of the test's Start/Stop controls. Starting or stopping a trial does not write Flash. The test sends no physical-button letters.

Pressed **Hold time** and released **Wait time** are separate persistent device-wide parameters, each initialized to 16 ms and editable from 1–512 ms in **Mouse UDLR settings**. Each phase is timed from its corresponding successful USB completion, with four waiting clicks. Missing wait settings inherit the previous shared hold duration on upgrade; valid independent values are preserved. Held keyboard reports retain the same letter until release; overflow is counted and discarded. Command 51 / `controllerctl.py direction-timing` reports actual ACK-to-ACK timing separately from the GUI letter log. The text count is PC reception, while generated/enqueued/ACK press/release counts describe firmware output. Keyboard letters go to whichever PC field has focus. Logs include the common threshold, actual hidden conversion gains read from the device, paddle axis, retained profile settings, letters and sampled counters. Stored profile gain therefore can differ from `conversionGains` intentionally. The preview below is the unchanged 0.20.7 UI with synthetic input, not measured controller motion.

![Read-only common threshold; cursor gains remain hidden](gui-udlr-test-en.png)

## Test-only protocol and returning to normal

Command 50 requires `UDL2`, enable byte at offset 4, and zero at offset 8 (no threshold parameter). It returns `UTS2`, schema 2, enable status and threshold. Old `UDL1` requests and nonzero threshold requests are rejected. Command 49 keeps `DKY1` with byte 6=2 for this variant and byte 7=running; normal firmware leaves both zero. Successful status polling renews the two-second lease. No converted letters are sent until Start.

Use the normal `dist/ControllerBridge.exe` updater to return to normal 0.20.10. Normal firmware emits gamepad D-pad clicks and has no keyboard-test entry point. Profiles, hidden cursor gains, common threshold, hold and wait durations are shared between the two variants. The historical `test/udlr/source/` copy is 0.20.3 evidence; current builds use the repository source tree and the script above.

## Validation and memory

Japanese and English settings/LED/firmware-package checks total **960** for the latest GUI; the unchanged firmware passes **46,713** shared C checks. The GUI-only update removes the extra hold/wait explanation and replaces the LED picker text with a square eyedropper icon. No board was accessed for that GUI update. The attached board was updated to **normal 0.20.10** after a verified full-Flash backup. **146 physical settings checks and 11 preservation checks** cover invalid values, old-schema rejection, stale writes, independent 1/512 and 512/1 ms durations, wait-only and hold-only changes, reboot persistence and restoration to 64 pt / 16 ms hold / 16 ms wait. All 16 profiles and prior common settings remain unchanged; only the new 16-byte wait record differs in the final 64 KiB storage comparison. The 0.20.10-udlr binary was built and checked but was not installed during this update. The user confirmed 16 ms operation before the UI changes. See [verification](verification.md).

| Resource | Used or reserved | Remaining |
|---|---:|---:|
| Flash, 2,097,152 bytes | Code/initial values 100,888 + storage 49,152 | 1,947,112 |
| SRAM, 532,480 bytes | Static 153,240 + stacks 8,192 | 371,048 |
| Common settings sector, 4,096 bytes | Prefix 56 + alignment 8 + direction records 64 | 3,968 |
| One bank copy, 16,384 bytes | Payload 13,908 + header 16 | 2,460 |

Static SRAM is vectors 272 + initialized data 35,560 + BSS 117,408 bytes. Remaining SRAM includes the minimum heap reservation and is not a measurement of peak use. Profile size remains 848 bytes; saved measurement size remains 3,784 bytes.
