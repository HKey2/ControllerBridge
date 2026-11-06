# Configuration and storage design

This describes the current ControllerBridge configurator 0.22.0 - GUI r2 and firmware 0.22.0. It is an implementation reference, not a release history. See the [user guide](configurator-guide.md) for normal operations.

The four profile actions share one compact, equal-width column on the right: Duplicate profile, Delete profile, LED RGB settings and Conversion mapping. The existing default client size retains the full combined-output editor without scrolling. Delete confirmation states the existing all-drafts-saved requirement for dragging; its save and reorder logic is unchanged. Japanese board references use 「デバイス」 throughout the interface. The shared pointing-row factory uses the existing RaisedTableLayoutPanel for separate Trackball and Paddle groups, with compact padding and a gap between their borders. Existing controls, event handlers and enabled-state behavior are retained.

## Architecture and capabilities

Core 1 runs the PIO input host. Core 0 handles mapping, output, configuration, and the status LED. Input uses the pinned Pico-PIO-USB transport; the native RP2350 USB device path is implemented by this project.

The GUI is a self-contained Windows x64 WinForms application built with the existing .NET 9 SDK. It edits the board's 16 slots directly. It requires 848-byte profiles, 16 slots, and capability bits 12, 24 and 25 for the current settings format; saving enabled shift settings additionally requires bit 14, and saving a Y-axis paddle setting requires bit 15. Gain above 64 requires bit 16; either movement-conversion preference requires bit 17. These checks run before profile/bank uploads. The explicit profile-size field is authoritative. Single-slot writes are read back with their identity before command 46 applies the effective output.

The GUI uses per-monitor V2 DPI awareness. Every form holds layout suspended from `Ui.Form` until its constructor has populated the entire 96-DPI control tree. Disposing the returned layout scope enables the initial scaling pass. Consuming this baseline before adding the controls can leave their dimensions unscaled, then shrink them again when a high-system-DPI process opens on a lower-DPI monitor. Mapping editors use the same baseline and stay parented while hidden; the registration wizard also retains all three pages so they receive font and DPI changes. Single-column layouts explicitly follow the available width, while mapping-cell dimensions retain their logical size. Owner-drawn secondary text derives its size from the scaled control font.

On show and after a DPI change, windows fit within the current monitor's working area. Content retains its logical minimum size with scrollbars when necessary. The main window starts at 1290×1100 logical pixels and permits a 1290×700 content minimum, with the mapping area scrolling independently.

The input path supports one directly connected Full Speed device, up to four HID interfaces, eight IN endpoints, 64-byte input packets, and 256 observed fields. Variable HID inputs, report IDs, absolute axes, and four/eight-direction hats are supported. Relative movement uses the dedicated mouse path. See [compatibility](compatibility.md) for tested and untested devices.

## Paddle axis semantics

Mouse flags at profile offset 480 use bit 5 for the paddle destination: zero means X and one means Y. Bits 6–7 enable trackball and paddle conversion respectively. `MouseProfileSettings.PaddleAxis` is the X/Y enum, serialized as 0/1 in JSON; an omitted property defaults to X. Profiles are v8/848 bytes, banks v8/13,908 bytes, and JSON exports v5. The mouse-options prefix retains the same offsets.

The mixer applies paddle gain and its own reversal to the wheel delta, then adds it to the selected axis before per-axis fractional rounding. Trackball reversal is applied only to trackball movement. The runtime resets fractional state when it reloads a profile. No additional per-profile storage or runtime buffers are allocated. Command 37 reports the same eight-byte settings block; capability bit 15 announces support. GUI single-profile and bank writes, and CLI profile edits, reject a Y setting on older firmware before sending write commands. Older firmware/GUI cannot load a Y profile; save all paddle settings as X before downgrading.

## Profile wire format

The canonical profile is version 8, 848 bytes, little endian. The C definitions and C# codec validate fixed sizes and field ranges. The original 488-byte prefix retains its fields; version-5 profiles migrate by appending disabled shift and conversion settings; version-6 profiles retain their first three shift keys and replace the fourth key area with conversion settings. Versions 1–4 remain unsupported. Normal USB profile transfers use only the current 848-byte format; legacy conversion applies to stored banks and JSON imports.

| Offset | Bytes | Field |
|---:|---:|---|
| 0 | 2 | Profile version |
| 2 | 2 | Flags: enabled=1, descriptor match=2, revision match=4, custom LED RGB=8 |
| 4 / 6 / 8 | 2 each | VID / PID / bcdDevice |
| 10 | 1 | Input interval in ms; zero uses the descriptor |
| 11 | 1 | Four 2-bit axis modes: disabled=0, analog=1, digital pair=2 |
| 12 | 4 | Input descriptor fingerprint |
| 16 | 48 | NUL-terminated UTF-8 name, at most 47 data bytes |
| 64 | 288 | 18 digital bindings, 16 bytes each |
| 352 | 128 | Four axis bindings, 32 bytes each |
| 480 | 1 | Trackball enabled=1, paddle enabled=2, invert X=4, invert Y=8, invert paddle=16, paddle Y=32 (clear means X), trackball D-pad=64, paddle direction conversion=128 |
| 481 | 1 | Low byte of trackball gain multiplied by 10 |
| 482 | 1 | Paddle gain multiplied by 10, range 10-100 |
| 483 | 1 | High byte of trackball gain; combined range 10-2560 |
| 484 | 1 | Bits 0-1: gamepad=0, mouse=1, combined=2; bit 2: controller-button behavior |
| 485 / 486 / 487 | 1 each | LED red / green / blue (RGB888), each 0-255 |
| 488 | 4 | Shift override mask, bits 0–17 in digital output order |
| 492 | 1 | Modifier count: 0 disables shift; 1–3 require all keys held |
| 493 | 3 | Reserved, zero |
| 496 | 48 | Three physical modifier bindings, 16 bytes each; unused entries disabled |
| 544 | 4 | Trackball targets U/D/L/R, each explicit output ID + 1 (1–18) |
| 548 | 2 | Paddle negative/positive targets, each 1–18 |
| 550 | 1 | Trackball mode in bits 0–1, Paddle mode in bits 2–3; each 0 or 1, upper bits zero |
| 551 | 1 | Virtual Rolling reverse bit 0, Left stick bit 1; other bits zero |
| 552 | 1 | Rotation step: 5/10/15/20/30/40/45/60/90 degrees |
| 553 | 1 | Pseudo-inertia enable bit 0; other bits zero |
| 554 | 6 | Reserved, zero |
| 560 | 288 | 18 shifted digital bindings, 16 bytes each |

Flag bit 3 (`UP_LED_CUSTOM`) enables the three LED bytes. With the bit clear all three bytes must be zero, and ready output is the existing R=0/G=0/B=1. With the bit set, bytes are output exactly; all-zero custom RGB means off. Matching still uses only flag bits 1 and 2 (values 2 and 4), so color does not change controller grouping, profile selection, or shortcuts. The three RGB bytes retain their existing offsets. The profile flag field still has 12 unused bits. The appended shift block adds 360 bytes per profile; bank-copy spare space is now 2,460 bytes.

Migration preserves version-5 LED values, including the zero default in 0.16.x/0.17.x records. Missing LED properties in old JSON decode as zero. The normal GUI checks for exact matching firmware before editing the bank; older configurators cannot edit the version-8 bank.

Valid output bytes are 0, 1, 2, 4, 5, and 6. The controller-button preference is retained even while another tab is selected. Every accepted profile includes this field. Whole-bank operations preserve original profile bytes rather than rewriting unrelated slots.

Digital binding fields are interface (1), report ID (1), bit offset (2), bit width (1), mode (1), flags (1), rapid rate (1), lower bound (4), and upper bound (4). Flags are signed=1 and report-ID-present=2. Mode 0 is disabled, 1 compares a range, and 2-5 represent hat Up/Right/Down/Left. Rapid fire is zero (Off) or 1–60 cycles per second; unassigned bindings and analog digital pairs require zero. The GUI starting rate for an Off output is 20, without rewriting stored rates or enabling outputs automatically. Firmware capabilities advertise bounds 1 and 60 in bytes 48–49. This range expansion changes no wire offsets or profile/bank versions; older firmware still rejects rates outside 2–30.

Digital output order is Y, B, A, X, L, R, ZL, ZR, Minus, Plus, left-stick press, right-stick press, HOME, Capture, Up, Right, Down, Left.

An analog binding contains interface (1), report ID (1), bit offset (2), bit width (1), flags (1), reserved (2), minimum (4), center (4), maximum (4), dead-zone percentage (2), and reserved (10). Flag bit 2 enables inversion. A digital-pair axis instead contains negative and positive 16-byte bindings; both held gives center. Axis order is LX, LY, RX, RY. Values received on separate interfaces/report IDs are retained and combined. Bit offsets exclude the report-ID byte.

## Profile bank and Flash

The version-8 bank is 13,908 bytes: 16-byte bank header, 16 x 848-byte profiles, 256 bytes of IDs, 4 bytes of preferred selections, and 64 bytes of shortcuts. IDs use the byte order of .NET `Guid.ToByteArray()` / `new Guid(bytes)`. The 16-byte outer CRC header makes a stored copy 13,924 bytes, leaving 2,460 bytes in each 16 KiB bank area.

| Storage slot | Offset with 2 MiB Flash | Size | Purpose |
|---:|---:|---:|---|
| 4 | `0x1F4000` | 16,384 | Bank A; first v5-to-v8 commit |
| 5 | `0x1F8000` | 16,384 | Bank B; overlaps both old 8 KiB bank copies |
| Unused | `0x1FC000` | 8,192 | Reserved; no reads or writes |
| 0 | `0x1FE000` | 4,096 | Common settings, 228 bytes used |
| 1 | `0x1FF000` | 4,096 | Saved measurement, unchanged |

The Flash header retains magic `CBST` (`0x54534243`), storage version 1, payload length and CRC-32. A valid v8 bank takes priority over any remaining v6 bank; among v8 copies the newest generation wins. Without v8, both 16 KiB areas are checked for v6, the newest valid generation is converted, and the opposite area is written and read back before adoption. Without v6, both old v5 copies at `0x1F8000`/`0x1FA000` are checked and the newest valid one is expanded and committed to Bank A. Conversion preserves generation, IDs, selection and shortcuts. The first migration commit does not increment generation.

Startup migration occurs before initializing missing common records, preserving old shared conversion defaults until the bank is secured. Flash failures leave the converted RAM bank unpersisted and retain the legacy source for retry. Subsequent saves use the inactive area. If no supported bank exists, startup creates 16 unused slots, generation zero, with persisted=false. Pre-0.16.0 storage identities remain rejected; no preset is created.

Normal updates write and verify the inactive copy before adopting it. The total reserved tail is 48 KiB, and writes check for overlap with code. The unused 8 KiB remains reserved. The application UF2 validator rejects payload addresses at or above `0x101F4000` on the standard 2 MiB board.

Whole-slot JSON uses `ControllerBridge.Slots`, version 4, UTC creation time, Base64 bank bytes and SHA-256. Individual JSON uses `ControllerBridge.Profile`, version 4, without bank metadata. JSON v2 with binary v5 and JSON v3 with binary v6 are validated before conversion. Mixed current/legacy bank layouts are rejected. Old v6 fourth shift keys are validated then discarded; the first three and shifted mappings are retained. v5 gains disabled shift. Old zero targets are resolved to explicit directions. Paddle targets select left/right for X or up/down for Y.

Old JSON lacks common settings. The importer reads retained migration defaults from the connected device when available, otherwise uses standard defaults, and notifies the user. It cannot reconstruct missing values from a different source device. New per-profile conversion settings are included in exports, clones, identity-preserving saves and whole-bank operations. JSON v1 and profile v1–v4 remain unsupported. Export uses temporary write/readback/replace; imports are capped at 256 KiB. No downgrade conversion is provided.

## Button shift runtime

Each profile has one main button page and one shifted page; axes, identity, output mode, input interval and mouse settings are common. Each override bit replaces one output binding, including its rapid rate. A disabled override explicitly mutes that output. Unset bits inherit the main page. Active overrides suppress inherited button outputs driven by the same overlapping physical input, preventing a moved button from generating both its old and new output. Overlap compares interface, report ID, bit range, signed/report-ID flags and activation values; it is not based on button labels.

The mapper retains both pages' pressed states, raw modifiers, digital axis pairs and analog values across report IDs/interfaces. Modifier AND detection precedes output for the current report. On any modifier release the main page returns; modifier bindings remain consumed until all are released. Inputs already held are recomputed for the newly active page. Profile activation seeds all cached field values; detach clears state. Conflict masks are calculated at attach rather than by an all-pairs scan per report. Page edges reset rapid phases without changing the selected profile or writing Flash.

Modifier consumption includes a shared physical source used by a common axis. Otherwise the four axes keep their main-page behavior. Partially held multi-key chords can produce their main mappings before the chord first completes; single-key modifiers are consumed immediately. No tap/hold or chord timing window is implemented. Dedicated unmapped physical modifiers avoid pre-chord normal outputs.

Permanent profile shortcuts read the main physical bindings and are suppressed during modifier consumption. The profile/bank validators reject overlapping modifier/shortcut inputs, including individual edits and shortcut changes. No shifted page alters the stored active profile ID. See [button shift](button-shift.md) for the user workflow and examples.

## Feature transport

Feature data is 64 bytes; the Windows HID API prepends report ID zero for a 65-byte buffer. Bytes 0-3 are `UC2C` / `UC2R`, byte 4 is transport version 1, byte 5 is command, byte 6 is sequence, byte 7 is response status, and bytes 8-63 are payload. Offsets below are relative to payload. Integers are little endian.

Status values are 0 success, 1 malformed request, 2 measurement busy, 3 save failure, 4 generation conflict, and 5 rejected legacy setting operation. Slots are 0-15; 255 means no selected slot.

| Command | Operation |
|---:|---|
| 9 | Request BOOTSEL after a 250 ms response window |
| 20 | Capabilities (`UPG1`): profile size/count, active slot, fields, device identity, connection/bank generations, event cursor, input interval, host/protocol state, feature bits, rapid range, profile number/count, shortcut |
| 21 | Read a decoded input field and its current value |
| 22 | Read up to four input change events from a cursor |
| 23 | Read up to 56 bytes of a slot from a supplied offset |
| 24 | Begin slot staging: slot, CRC, expected generation, optional profile ID |
| 25 | Append up to 52 bytes to slot staging at an offset |
| 26 | Validate and save the complete staged slot; return generation |
| 29 | Read cached device/configuration descriptor fragments |
| 30 | Read the latest raw input packet, sequence, and receive timestamp |
| 31 | LED diagnostics (`LED1`) |
| 32 | Read slot ID, generation, and profile CRC consistently |
| 33 | Select a saved matching profile with an expected generation |
| 34 | BOOT sampling/switching diagnostics (`BTN1`) |
| 35 | Read or save the shortcut for all profiles matching the selected controller |
| 36 | Mouse output and queue diagnostics (`MSE1`) |
| 37 | Effective per-profile mouse settings (`MPR1`) |
| 38 | Active mouse protocol and wire format (`MWR1`) |
| 39 | Build-time fixed-motion probe settings (`MFP1`) |
| 40 | Bank info: `UPB1`, byte count, generation, CRC |
| 41 | Read up to 56 bank bytes using offset and expected generation |
| 42 | Begin bank staging using expected generation, CRC, and full length |
| 43 | Append up to 52 bank bytes using offset and length |
| 44 | Commit a complete valid bank and return generation/CRC |
| 45 | Abort bank staging |
| 46 | Apply the active profile's output after checking expected generation and slot; return `OUT1` and current/pending/reconnect state |
| 47 | Normal firmware identity (`CBV1`), version, and target board; see [firmware update](firmware-update.md) for the byte layout |
| 48 | Begin registration into an empty slot, guarded by the captured input attachment and identity |
| 49 | Read direction generation/ACK/drop counters (`DKY1`) |
| 51 | Read actual generated-direction timing (`DPT1`, 0.20.7+) |
| 52 | Read shared tuning and retained migration defaults (`UDS9`, 0.22.0) |
| 53 | Validate, save and apply shared tuning and pseudo-inertia (`UDS9`, 0.22.0) |

The implementation is authoritative for byte-level command layouts: [`profile_runtime.c`](../lib/controller_bridge/profile_runtime.c), [`main.c`](../apps/egret_switch/main.c), [`HidBridge.cs`](../gui/ControllerBridge/HidBridge.cs).

Capability bits 0-12 represent rapid fire, IDs, on-board variants, shortcuts, immediate switching, mouse output, extended-profile/16-slot support (originally 488 bytes; read the explicit size field), combined output, combined full-gamepad buttons, extended trackball gain, 16-bit mouse output, whole-bank transfer, and per-profile output. Bit 13 (8192) adds guarded registration. Bit 14 (16384) adds button shift and the 848-byte profile. Bit 15 (32768) adds selectable paddle X/Y output. Bit 16 (65536) adds trackball gain through 256.0. Bit 17 (131072) adds movement-to-D-pad conversion. Bit 18 (262144) adds shared direction-settings commands 52/53. Bit 19 (524288) adds direction-output assignments and UDS3/schema 3. Bit 20 (1048576) adds independent trackball/paddle assignments and UDS4/schema 4. Bit 21 (2097152) adds Virtual Rolling and UDS5/schema 5. Bit 22 (4194304) adds Trackball Hold and UDS6/schema 6. Bit 23 (8388608) adds configurable rotation steps and UDS7/schema 7. Bit 24 (16777216) identifies profile conversion; bit 25 (33554432) adds v8 pseudo-inertia and UDS9/schema 9 shared tuning. Clients must check the relevant bits rather than infer support from names.

Command 48 keeps the first 26 bytes of command 24: slot at 0, profile CRC32 at 1, expected bank generation at 5, ID-present flag (must be 1) at 9, and 16-byte profile ID at 10. It adds expected input connection generation at 26, descriptor fingerprint at 30, VID/PID/revision at 34/36/38, and input protocol at 40. All multibyte integers are little endian. A dedicated command and capability prevent old firmware from silently ignoring the guard.

Registration requires a connected matching attachment and an empty saved destination. Commands 25 and 26 retain the guard until commit. Probing/disconnection invalidates the staged registration even when the same controller returns. Commit rechecks connection generation, destination, exact staged identity, descriptor matching, and that mapped sources exist before any Flash save. Conflicts return status 4; a Flash failure remains status 3. Ordinary command-24 profile editing still permits editing saved profiles while the input is absent. Guarded registration uses the current version-8 profile and validates modifier and overridden input sources as well as main mappings.

The 256-entry event ring stores field index, flags, value, and timestamp. GUI polling runs every 25 ms. Learning drains old events before taking its baseline and aborts on a connection generation change or an overrun. Captured input events do not trigger Flash writes.

The registration input log observes this same parsed-field stream before the learning-delivery gate, including baseline-drain events and the last received overrun event. It makes no additional USB requests. Initial snapshots have no device timestamp; event rows retain the original 32-bit microsecond timestamp without treating a wrap as negative elapsed time. The index is the zero-based firmware field index, independent of HID Usage/button numbering and output mapping indexes. Decimal values and width-masked hexadecimal values are recorded before GUI calibration or mapping. Identical USB reports from two physical controls cannot be distinguished, and unchanged fields are not a packet trace.

`InputLogBuffer` bounds PC history at 2,000 immutable rows with an eviction count. `InputLogForm` uses a virtual list and batches repainting at 100 ms; the existing learning path still receives every delivered change. `InputLogListView` explicitly enables the native double-buffer style. For pure appends, it preserves WinForms' managed item count while forwarding `LVM_SETITEMCOUNT` with `LVSICF_NOINVALIDATEALL | LVSICF_NOSCROLL`; offscreen appends do not repaint visible rows. Clearing or rotating the ring redraws item content, tracked by the buffer reset generation and eviction count. There is no unconditional whole-control invalidation on an ordinary update. Following only requests a scroll if the last row is outside the vertical viewport. Status text, including a retained notice, is composed before a single assignment, avoiding a temporary one-line layout on each two-line update. See Microsoft's [item-count flags](https://learn.microsoft.com/en-us/windows/win32/controls/lvm-setitemcount) and [native double-buffer style](https://learn.microsoft.com/en-us/windows/win32/controls/extended-list-view-styles).

Pausing the log does not pause polling or learning; resuming records an omission notice and a new snapshot. The log is a modeless sibling of the modal registration wizard, owned by the main form. Wizard closure finishes the log without reparenting windows during closing, so diagnostics remain available for export. All storage is on the PC; firmware buffers, persistent layouts, and board memory requirements are unchanged.

## Output scheduling and measurements

Output state values are 0 Gamepad, 1 Mouse, 65 combined mouse-button behavior, and 193 combined controller-button behavior. These differ from profile byte 484. Common configuration byte 7 retains its diagnostic protocol position; current output-mode changes must use profiles.

A layout change waits 250 ms after the response, disconnects USB-C, then reinitializes after another 250 ms. USB-A reception continues. Only the current button state initializes the new output; relative movement accumulated during disconnection is not replayed. Measurement starts after any output transition completes.

Mouse movement and click transitions use independent 64-entry queues. Report Protocol supports signed 16-bit movement and combines up to eight pending input movements; Boot Protocol uses signed 8-bit movement and combines up to two. Only acknowledged movement is consumed. Click transitions do not wait for queued movement to drain. Capacity overflow is counted rather than claimed to be lossless.

The current saved measurement payload is 3,784 bytes. Its `DUO1` extension separates mouse and gamepad counters, and `MBT1` records click transitions and the formats actually acknowledged. With the 16-byte header it occupies 3,800 of 4,096 bytes, leaving 296 bytes. Input-receive-to-output-ready/ACK statistics exclude the input controller's internal scan and the game's response.

## GUI behavior

`ConnectionRead` reads command 47 before settings and owns the HID handle until the complete bank/input/output snapshot is ready. Unknown or different firmware returns a classified status without attempting to decode incompatible profiles. Background connection attempts have a separate `connecting` guard, so they do not toggle the main form's `working`/enabled state. Manual operations supersede pending attempts; late results after closure are disposed. A modal editor defers background discovery.

If no bridge HID is found, discovery enumerates present `GUID_DEVINTERFACE_USB_DEVICE` interfaces and resolves each USB parent and location path. Pico SDK CDC/reset (`2E8A:0009`) and RP2350 BOOT (`2E8A:000F`) parents become installation candidates. Composite child interfaces are not counted separately; unrelated USB devices and RP2040 BOOT (`2E8A:0003`) are excluded. Candidate ordering and case are normalized for stable notice keys. This is metadata enumeration only: it does not open the serial port or send reset commands. The factory board supplied for verification exposes product name `Pico` and `2E8A:0009`; this shared identity is not proof of a particular carrier board.

`FirmwareNoticeTracker` records each detected device/version notice until a disconnected state is observed. Factory/BOOT candidates and unreadable bridge firmware use `UnknownFirmware`, displayed as **Device unsupported**. No recognized board uses `NotDetected`, displayed as **Device not connected**, and never opens guidance. Cancelling the BOOT wait or declining confirmation suppresses repeat prompts during that connection; a later observed absence rearms guidance for reattachment. The updater is opened after releasing the connection guard and performs its own fresh version check. Its return reconnects with automatic guidance suppressed, preventing cancellation loops. Version matching is exact equality, with no upgrade/downgrade ordering.

The updater also records a single factory/BOOT candidate's USB location. Multiple candidates fail without selecting one. After the user follows the BOOT guide, the programming disk must be on the recorded port, just as for a normal versioned application. RP2350 USB/INFO_UF2 checks and the separate overwrite confirmation remain required. Manual recovery with no previously detectable target still allows a single validated RP2350 BOOT disk.

When the version is unavailable, `FirmwareWorkflow` enters `WaitingForBootsel` immediately, with no initial Yes/No prompt. `PrepareAsync(null, token)` polls for a validated BOOT disk every 250 ms until detection or cancellation; manual reconnection has no timeout. The dialog displays numbered physical steps and only **Cancel**, with Escape and window-close cancellation. Cancellation is checked after discovery as well as during polling so that cancelling at the detection boundary cannot open confirmation or start writing. After valid detection, `ConfirmOverwrite` opens the final Yes/No prompt, defaulting to No. Only Yes starts writing; `WriteAsync` revalidates the selected disk and USB identity after confirmation. A readable version mismatch retains its existing confirmation, automatic BOOT transition, and 30-second BOOT timeout.

`ControllerArrivalTracker` consumes one navigation per initialized input attachment. It checks saved and draft profiles with the normal matching rules. The destination is the first slot with no saved profile, enabled draft, or dirty flag. Navigation runs after the polling/connection guard is released and waits for clean drafts, an up-to-date bank/input snapshot, and no foreground/modal operation. It only selects a list row; no device command is sent. The no-space condition is persistent header text, not a modal prompt. Registration captures the original bridge object, bank generation, and input identity and rechecks them after waiting for polling; firmware command 48 supplies the commit guard.

Status labels change only when the complete displayed text changes. Input events all reach the learning logic, while the live label receives only the final value in each polling batch. Empty USB-A host retry counters do not rebind the editor. Layout containers, the main form, firmware dialog, and custom buttons use buffered painting. Repeated offline discovery retains status, selection, and drafts without repeated enable/disable changes, layout, invalidation, or rebinding.

Names are persisted with duplicate suffixes; the list does not derive changing numbers from slot order. The initial and minimum client area is 1290 x 1100 at 96 DPI (1306 x 1139 with the checked window frame). The profile pane is 400 pixels wide, with ellipsis for longer names. Analog sources use a live list with centered meters and multiple columns when needed. The live numeric label reserves the measured localized signed-32-bit width, so adjacent calibration buttons do not shift with its value. Documentation images use English; saved-profile images reflect the device bank described in the guide. The application retains both UI languages.

## Application icon

The Windows executable and application windows use `Resources/ControllerBridge_icon.png`. `tools/build-app-icon.ps1` produces `Resources/ControllerBridge_icon.ico` with 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixel frames. The 64-pixel source is preserved exactly; enlarged frames retain its pixel edges. `tools/build-configurator.ps1` regenerates the ICO before publishing. The project embeds it as both the Windows executable icon and a managed resource used by the common form styling helper. This does not affect firmware resources or board memory.

## Dedicated mouse-only input route (0.20.18)

`handle_host_event` first applies the normal button mapper, then chooses the pointing route from the active USB output flags. This uses the currently running descriptor mode, not the requested profile mode, so a pending 250 ms USB-mode transition cannot select a route ahead of its output. Pure mouse output returns through `handle_mouse_only_input` before the gamepad direction-event locals and hold query. Its short mouse queue/measurement delivery code intentionally duplicates the corresponding combined-mode sequence.

`upr_mouse_delta` has its own short relative-field decoding sequence and shares only `um_mix` for pointer gain, inversion, paddle-axis selection and fractional accumulation. It has no conversion-enable argument and neither reads conversion flags nor touches click sums, Hold or Rolling state. It initializes output deltas to zero even when decoding fails, preserving button-only report behavior. The gamepad/combined route retains `upr_pointing_input`, its direction events and overlays. Initial output and rapid-fire processing also skip direction-hold queries in pure mouse output.

Conversion state is reset at existing profile, output-mode, readiness and disconnect boundaries instead of on each mouse report. Mouse transport failures still flush the mouse queue and call `upr_mouse_reset`. No additional persistent setting, buffer or storage-format change is required. Since 0.20.19, GUI and firmware validation accept retained conversion preferences in pure mouse mode. No input-handler or runtime change is needed: the dedicated route already ignores those flags.

## Pointing-to-direction conversion (0.22.0)

Mouse flags use all eight bits. `Mouse.TrackballToDpad` and `Mouse.PaddleToDpad` default to false in JSON that lacks them. Profile v8/848 bytes, bank v8/13,908 bytes and JSON v5 retain their fixed binary sizes. Selecting USB mouse retains both conversion flags, displays their values in disabled grey controls and does not execute conversion. Pointer and source edits preserve both preferences. Returning to a gamepad mode uses the retained settings. GUI encoding and firmware profile/bank validation accept this combination since 0.20.19. Older firmware rejects it when the corresponding input source is enabled; compatibility requires matching firmware, not a new storage layout. The internal `upr_direction_enabled` query and command-49 preference indicator continue to describe configured source/conversion flags; actual execution is selected by the active output route.

`um_direction_keys` maintains three signed 32-bit sums in tenths with 64-bit multiplication/addition. Common click threshold, hidden gains and click durations combine with the active profile's explicit targets and modes. The 40-byte `udk_settings` structure retains migration maps/modes/step and adds inertia threshold/interval (u16), decay percent (u8) and effective per-profile enable (u8). `upr_effective_direction_settings` composes the effective settings on activation/settings changes, outside the per-report route. Paddle negative/positive targets populate the effective U/L and D/R entries so pointer axis changes do not alter the chosen outputs. `udk_settings` remains separate from the 16-byte per-profile conversion block, which includes six reserved bytes.

Storage accepts click threshold 1–65,535, hidden Trackball/Paddle gains 10–2,560/10–100 tenths, click durations 1–1,000 ms, Hold threshold 1–127, rolling gain 1–100 tenths inertia threshold 1–65,535, factor 0–99 percent and interval 1–1,000 ms. GUI edits accept click threshold 1–256 and durations 1–512, retaining valid older values until explicit save. Initial common values are threshold 64, hidden gains 60/15, hold/wait 30/20, Hold threshold 1, rolling gain 10 and inertia 8 / 90% / 100 ms (disabled per profile). Legacy zero targets resolve to original directions; new profile targets are explicitly 1–18 in output order.

One input decode yields independent pointer movement and conversion events. Crossing the signed threshold emits one click and resets that axis sum, discarding excess. Event order is Trackball X, Trackball Y, Paddle; each event keeps direction and source. Mouse-only output bypasses conversion state. Profile/settings transitions invalidate old generated queues and Hold masks before applying new mapping, while preserving ordinary physical inputs. The application tracks direction revisions to prevent queued old directions being reinterpreted using a new profile.

`ub_direction_pulse` remains a 76-byte state machine with four waiting clicks. Each pending event retains its direction and source, and the active state retains that source through press/release ACKs. The source bytes use existing padding, so the queue allocation does not grow. Output and physical-conflict checks use the selected source map, including when equal directions from different sources are queued consecutively. It overlays only the selected generated button bit or hat value onto normal output, preserving unrelated button/axis transitions and independent mouse scheduling. The held direction level still identifies the source U/D/L/R direction for ACK accounting and the keyboard diagnostic. States are idle, waiting for press ACK, held, waiting for release ACK, and released gap. The held and gap timers begin only at the matching successful completion callback, and use unsigned elapsed-time subtraction across clock wrap. The held phase uses saved `direction_settings.hold_ms * 1000`, while the gap uses saved `direction_settings.wait_ms * 1000`; their initial values are 30,000 and 20,000 us respectively. `ub_direction_prepare` receives them as independent arguments. Its guard requires both to be 1,000–1,000,000 us and clears pending output if either is invalid. An edge is counted once; failures remain retryable. Reset clears pending state and invalidates old completion cookies. A physical report holding the selected button, or any physical hat when the target is a direction, takes priority. New conflicting clicks are rejected; a conflict with the current/next generated target resets pending generated work and invalidates its ACK cookie. The unmodified physical report is retained, so no generated release can release a held physical button. Physical hat input can coexist with a generated face/menu button. Capacity overflow discards a whole new click and increments the direction drop count. Ordinary physical-state queue overflow remains independent. `ub_frame` stays 20 bytes: its former padding holds both the one-time ACK edge marker and the held direction level. The UDLR encoder uses the level on repeated reports, so normal 1 ms polling cannot release a held key early. The older pair helper remains available to its packet-format tests but is not used by the application scheduler.

Read-only Feature command **49** returns 56-byte `DKY1`: schema byte 4=1; byte 5=active conversion; bytes 6–7 reserved zero in normal firmware. Offset 8 is threshold (u32); 12/16/20 are ball-X, ball-Y and paddle signed sums in tenths (i32); 24 generated clicks, 28 enqueued clicks, 32 acknowledged presses, 36 acknowledged releases, 40 full-queue drops, 44 physical-target conflicts, 48 ordinary queued frames plus unfinished generated direction edges, 52 ordinary queue-overflow resets (all u32). Counters are modulo 2^32. The diagnostic remains read-only; commands 52/53 provide the separate settings interface.

Read-only command **51** returns `DPT1`, schema byte 4=1, phase byte 5 (0 idle, 1 press ACK pending, 2 held, 3 release ACK pending, 4 gap), last/current direction byte 6 (1 U, 2 D, 3 L, 4 R; phase determines whether it is held), queued-click byte 7. Remaining fields are u32: offset 8 minimum press us; 12 minimum release us; 16 waiting capacity; 20 measured press count; 24/28 minimum/maximum ACK-to-ACK pressed duration; 32 measured consecutive released-gap count; 36/40 minimum/maximum ACK-to-ACK gap duration; 44 unfinished direction edges; 48 last edge ACK time; 52 source receive time. Offsets 8 and 12 return the actual saved hold and wait durations respectively, converted to microseconds and initially 30,000 and 20,000 respectively; the DPT1 schema and offsets remain unchanged. Zero samples means no measured range. Idle gaps are excluded from consecutive-gap statistics. Timing statistics remain across output resets, while active/pending work is cleared. These statistics do not demonstrate application-level reception on another host.

The ordinary gamepad descriptor uses Generic Desktop Hat Switch (usage 0x01/0x39), with up/right/down/left values 0/2/4/6 and null value 15. This matches the existing physical-direction mapper. The separate keyboard test maps U/D/L/R to page-7 usages 0x18/0x07/0x0F/0x15 with Shift, not arrow key usages. See [USB-IF HID Usage Tables 1.7](https://www.usb.org/sites/default/files/hut1_7.pdf) for the usage definitions; the initial 30/20 ms intervals are an implementation choice, not a USB or Nintendo requirement.

## Trackball Hold runtime (0.22.0)

Hold requires a ready profile with relative Trackball X/Y, Trackball enabled, conversion enabled and profile trackball_mode=1. `um_hold_state` is **556 bytes**: 32 samples of X/Y/end/span (512 bytes), nine timing/magnitude/configuration words (36 bytes), direction/decay/ring fields and alignment (8 bytes). Ordinary raw-axis Hold applies profile inversion and the shared Hold movement threshold without gain. Zero/subthreshold input clears ordinary Hold; absent reports expire after max(20 ms, two effective input polling intervals).

Optional inertia estimates signed displacement in a **20 ms** window independently per axis. Each report's delta is distributed uniformly over its preceding interval; proportional overlap avoids a third full report being counted because of microsecond jitter. The first report uses a 20 ms interval. At Full Speed's 1 ms minimum polling interval, the 32-entry history covers the window. Entirely included samples avoid division; partial-boundary samples use signed 64-bit intermediates. Velocity clamps at 65,535 counts, represented in Q8. Pointer gain does not affect it.

A current movement meeting the ordinary Hold threshold, matching the window sign, and strictly exceeding the inertia threshold refreshes that axis from measured speed (no energy accumulation). Zero/small windows do not refresh the decay deadline. A qualifying reversal cancels that axis and removes its old history; sufficiently large reverse movement may then start a new impulse. Disabled profiles bypass the estimator.

Every real-time interval multiplies Q8 magnitude by decay percent / 100 with integer truncation. At or below the activation threshold, the axis releases. There is no maximum-duration parameter. The default 8 / 90% / 100 ms gives 600 ms for 14–15, 700 ms for 16, and 1,500 ms for 36. Decay handles delayed calls and 32-bit timestamp wrap, and ignores older queued timestamps rather than advancing the clock backwards. Even the largest representable impulse terminates after about 1,104 decay steps at 99%; this bounds catch-up work without imposing a time limit. Resets, readiness loss, output/profile/settings changes clear all generated inertia and history while preserving ordinary physical input.

`ub_frame` remains **20 bytes**: `hold_directions` uses its last padding byte. Queue change detection includes this mask, preserving rapid press/reverse/release transitions before USB ACK. Input, rapid-fire and measurement frames carry it. The scheduler uses the queued mask for queued frames, otherwise the current runtime state for idle expiry. `um_hold_overlay` runs after ordinary mapping and before generated paddle clicks; mapped buttons are ORed, opposing generated hat directions cancel, and any physical hat owns the hat. Axes are unchanged. A conflicting paddle click follows existing generated-click conflict handling. Paddle Virtual Rolling overlays its selected stick afterward. Repeated reports are cancelled when the complete report, Hold mask or pulse fields change, so a stale repeat cannot retain a released Hold.

The UDLR diagnostic encodes all active U/D/L/R bits as shifted letters and merges any active paddle click. It can therefore hold both letters of a diagonal. Commands 49/51 and the existing log counters still describe **clicks**, not Hold duration or a complete Hold history; PC keyboard autorepeat can add repeated characters while one key is held.

## Virtual Rolling runtime (0.20.16)

Virtual Rolling requires an active matching profile with a relative wheel input, Paddle enabled, its conversion bit set, and profile Paddle mode 1. `um_rolling_state` remains **12 bytes**: last-motion timestamp, angle in tenths of a degree, signed residual tenths, cached X/Y, moving state and a cached step in a former padding byte. Startup zero means up. A **38-byte, 19-entry uint16 Q15 quarter-sine table** covers 0° through 90° in 5° increments, with quadrant reflection for all reachable angles. Positive/negative stick extents use 127/128 with nearest-integer rounding; the default 30° vectors remain byte-identical. No floating-point or trigonometric work runs per input.

For each nonzero valid wheel report, signed movement is `-wheel * rolling_gain10`, optionally reversed by the dedicated flag, using 64-bit intermediates. Valid steps are the nine multiples of 5 from 5 through 90 that divide 360. If the cached step differs, first round the retained angle to the nearest new step measured from up (`((degrees + step/2) / step * step) % 360`, clockwise on ties), clear the old residual, and cache the new step. Add the residual to the movement, divide by `step * 10` with truncation toward zero, retain the signed remainder, and advance the angle by whole steps modulo 360°. Multiple steps and full turns require no loop per detent; the latest position is output. 90° therefore always yields the four cardinal vectors, including after a step change. Pointer gains, pointer inversion and paddle X/Y selection do not affect this calculation. Trackball clicks and both pointer sources remain independent; paddle clicks are suppressed in Virtual Rolling mode.

A real zero-wheel report clears moving state only. Reports for another interface/report ID, or invalid/truncated reports, do not synthesize a stop. Without a new paddle report, `um_rolling_overlay` stops motion after max(20 ms, twice the input polling interval). The interval uses the profile override when nonzero, otherwise the wheel interface's cached endpoint descriptor. Elapsed-time comparison handles the 32-bit clock wrap. Input/output resets and settings saves stop movement but preserve both angle and residual; only `upr_init` resets them unconditionally. A changed step performs the alignment/residual reset above on the next nonzero report. Other setting changes leave them intact. No game-state signal is read and no angle is persisted.

The normal send scheduler overlays the selected stick after ordinary mapping, combined-output routing and generated button pulses, on both new and repeated reports. Moving outputs the lookup vector; idle outputs center. The chosen stick belongs to this mode while enabled; other axes/buttons remain unchanged. The ordinary queue never stores the virtual vector. Changes to the full report, Hold mask or pulse identity cancel stale ordinary repeats so a stop can reach USB without waiting for another physical report. The optional keyboard-only UDLR build has no stick report: select Button clicks to test paddle letters.

## Shared tuning, migration defaults and transaction (0.22.0)

Common configuration slot 0 remains at `0x1FE000` (4 KiB). The original `CBST` 16-byte header and 40-byte payload remain intact. The first nine appended records are 16 bytes: magic, u32 schema=1, four data bytes and CRC32 of the first 12 bytes. INR1 is 20 bytes: magic, schema=1, packed threshold/decay, u32 interval, CRC32 of the first 16 bytes.

| Offset | Record | Data |
|---:|---|---|
| 64 | DKC1 | u32 click threshold |
| 80 | DKG1 | u16 hidden Trackball/Paddle gains |
| 96 / 112 | DHT1 / DWT1 | u32 click hold / wait ms |
| 128 / 144 | DMP1 / DPM1 | retained legacy U/D/L/R targets for migration |
| 160 | VRS1 | rolling gain bits 0–15; retained migration mode bits 16–23 and flags bits 24–31 |
| 176 | TBH1 | Hold threshold bits 0–7; retained migration Trackball mode bits 8–15; high bits zero |
| 192 | RSD1 | retained migration rotation step |
| 208 | INR1 (20 bytes) | u16 threshold + u8 decay percent + zero high byte; u32 interval ms |

The prefix plus alignment and ten records uses **228 bytes**, leaving **3,868**. Common tuning edits preserve retained legacy maps/modes/step, and do not overwrite per-profile conversion. Missing/invalid records fall back independently. Missing wait inherits a valid saved hold for old shared-duration compatibility, otherwise defaults to 20; hold defaults to 30. Missing/invalid INR1 defaults to 8 / 90% / 100 ms. Bank migration runs before a boot write initializes these records. Measurements and their 3,784-byte payload are unchanged. Older firmware may erase records it does not know on common save; v8 banks require restoring a pre-upgrade backup before any downgrade.

Command 2 retains its original 40-byte prefix and appended DKC1 settings view; schema byte 2 and persisted flag stay at 52/53. Commands 52/53 use **UDS9**, schema 9. Bit **24** describes profile conversion and bit **25** identifies v8 pseudo-inertia/shared tuning. The 56-byte response is: magic/schema 0/4, u32 click threshold/hold/wait 8/12/16, persisted 20, retained migration maps 24/28, migration packed mode options 32, reserved zero 36, packed inertia threshold/decay 40, interval 44, reserved zero 48/52. The legacy mode options retain rolling gain byte 0, modes/flags byte 1, Hold threshold byte 2 and rotation step byte 3.

Command 53's 56-byte request contains six expected words and six desired words, guarding every editable common value:

| Offset | Contents |
|---:|---|
| 0 / 4 | UDS9 / u32 schema 9 |
| 8 / 12 / 16 | Expected click threshold / hold / wait |
| 20 / 24 / 28 | Expected rolling gain + Hold threshold / inertia threshold + decay / interval |
| 32 / 36 / 40 | Desired click threshold / hold / wait |
| 44 / 48 / 52 | Desired rolling gain + Hold threshold / inertia threshold + decay / interval |

Tuning packs rolling gain in byte 0 and Hold threshold in byte 1; upper bytes are zero. Inertia packs threshold in low 16 bits and decay percent in byte 2; byte 3 is zero. Enable is exclusively profile offset 553, never shared storage or this transaction.

Old schemas, invalid ranges and nonzero reserved bytes return status 1. Stale expected tuning returns status 4 unless all desired tuning is already current, permitting lost-response retry. Busy measurement/reconnection returns 2; save failure returns 3 without applying the candidate. Hidden gains and retained migration defaults are preserved. After persistence, the firmware composes effective profile settings and clears generated state. The GUI checks the response and independent readback. Profile updates use the existing bank-generation guard.

The GUI separates **Conversion mapping** (per-profile draft, then profile save) from **Shared settings** (eight common numeric controls, immediate guarded save). The conversion dialog has four Trackball targets and two Paddle targets. Trackball mode 0 is displayed as **Virtual Cursor**, while Paddle mode 1 remains **Virtual Rolling**; this naming distinction does not change stored mode values or firmware behavior. Paddle mode greys out its inactive target or rolling controls in place. The common dialog groups click conversion, Trackball Hold (including a pseudo-inertia subsection), and Paddle Virtual Rolling. It has no mode tabs and retains all eight editable values regardless of selected profile mode. The inertia explanation belongs to its group; the separate status row appears only for clamped older values or a save error. Both action buttons compare current editable values with their opening values and disable themselves when unchanged, including after a full revert. Shared numeric text is inspected without committing pending keyboard edits; comparison uses the saved precision. Out-of-range legacy display clamping does not by itself count as a user edit. Save failure retains the changed values and permits retry. Handler guards also reject unchanged actions. Cancel writes nothing. Both use the existing layout/DPI framework.

### Bounded Elite Series 2 startup retry (0.20.21)

For `045E:0B00`, a GIP startup timeout after the existing recovery schedules one extra recovery cycle. The normal enumeration loop retains its 750 ms delay. On the next GIP startup, the pending flag is consumed, the extra-cycle flag is marked used, and the USB-resume and GIP-reset guards each reopen once. A resume still returns for re-enumeration without scheduling another cycle. The extra cycle may use GIP Reset after a primary Hello if metadata or a later startup command timed out; the complete metadata/Start/vendor sequence is replayed after that reset. Unsupported models, non-timeout failures, USB timing and active ACK scheduling retain their existing behavior.

Successful initialization alone does not replenish the extra cycle. Status messages, repeated enumeration, malformed packets and a configured controller with no input cannot replenish it either. Only a fully parsed USB packet containing an accepted gamepad, Guide or paddle input clears the extra-cycle flags. This enables one future recovery for a new failure episode without resetting a working controller. Ordinary enumeration continues after an exhausted cycle, but does not repeatedly re-enable the special recovery operations. No operation-wait timeout is added, and no settings or persistent-storage formats change.

The LED consumes `UC_CONFIGURED` to clear an earlier initialization fault and wait in cyan for the first input. If input has already been observed, this status does not overwrite the receiving state. The normal ready profile color is selected after accepted input.
