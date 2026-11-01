# Configuration and storage design

This describes the current ControllerBridge configurator 0.20.1 and firmware 0.20.1. It is an implementation reference, not a release history. See the [user guide](configurator-guide.md) for normal operations.

## Architecture and capabilities

Core 1 runs the PIO input host. Core 0 handles mapping, output, configuration, and the status LED. Input uses the pinned Pico-PIO-USB transport; the native RP2350 USB device path is implemented by this project. TinyUSB is used only by the separate CDC diagnostic target.

The GUI is a self-contained Windows x64 WinForms application built with the existing .NET 9 SDK. It edits the board's 16 slots directly. It requires 848-byte profiles, 16 slots, and capability bit 12 for the current settings format; saving enabled shift settings additionally requires bit 14. The explicit profile-size field is authoritative. Single-slot writes are read back with their identity before command 46 applies the effective output.

The GUI uses per-monitor V2 DPI awareness. Every form holds layout suspended from `Ui.Form` until its constructor has populated the entire 96-DPI control tree. Disposing the returned layout scope enables the initial scaling pass. Consuming this baseline before adding the controls can leave their dimensions unscaled, then shrink them again when a high-system-DPI process opens on a lower-DPI monitor. Mapping editors use the same baseline and stay parented while hidden; the registration wizard also retains all three pages so they receive font and DPI changes. Single-column layouts explicitly follow the available width, while mapping-cell dimensions retain their logical size. Owner-drawn secondary text derives its size from the scaled control font.

On show and after a DPI change, windows fit within the current monitor's working area. Content retains its logical minimum size with scrollbars when necessary. The main window starts at 1290×1100 logical pixels and permits a 1290×700 content minimum, with the mapping area scrolling independently. The DPI regression runner is `tools/check-gui-dpi.ps1`; it exercises synthetic startup DPI and native DPI-change notifications without changing Windows settings or opening a USB device. See [verification](verification.md) for the tested conditions and limits.

The input path supports one directly connected Full Speed device, up to four HID interfaces, eight IN endpoints, 64-byte input packets, and 256 observed fields. Variable HID inputs, report IDs, absolute axes, and four/eight-direction hats are supported. Relative movement uses the dedicated mouse path. See [compatibility](compatibility.md) for tested and untested devices.

## Profile wire format

The canonical profile is version 6, 848 bytes, little endian. The C definitions and C# codec validate fixed sizes and field ranges. The original 488-byte prefix retains its fields; version-5 profiles migrate by changing the version and appending disabled shift settings. Versions 1–4 remain unsupported. Normal USB profile transfers use only the current 848-byte format; legacy conversion applies to stored banks and JSON imports.

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
| 480 | 1 | Trackball enabled=1, paddle enabled=2, invert X=4, invert Y=8, invert paddle=16 |
| 481 | 1 | Low byte of trackball gain multiplied by 10 |
| 482 | 1 | Paddle gain multiplied by 10, range 10-100 |
| 483 | 1 | High byte of trackball gain; combined range 10-640 |
| 484 | 1 | Bits 0-1: gamepad=0, mouse=1, combined=2; bit 2: controller-button behavior |
| 485 / 486 / 487 | 1 each | LED red / green / blue (RGB888), each 0-255 |
| 488 | 4 | Shift override mask, bits 0–17 in digital output order |
| 492 | 1 | Modifier count: 0 disables shift; 1–4 require all keys held |
| 493 | 3 | Reserved, zero |
| 496 | 64 | Four physical modifier bindings, 16 bytes each; unused entries disabled |
| 560 | 288 | 18 shifted digital bindings, 16 bytes each |

Flag bit 3 (`UP_LED_CUSTOM`) enables the three LED bytes. With the bit clear all three bytes must be zero, and ready output is the existing R=0/G=0/B=1. With the bit set, bytes are output exactly; all-zero custom RGB means off. Matching still uses only flag bits 1 and 2 (values 2 and 4), so color does not change controller grouping, profile selection, or shortcuts. The three RGB bytes retain their existing offsets. The profile flag field still has 12 unused bits. The appended shift block adds 360 bytes per profile; bank-copy spare space is now 2,460 bytes.

Migration preserves version-5 LED values, including the zero default in 0.16.x/0.17.x records. Missing LED properties in old JSON decode as zero. The normal GUI checks for exact matching firmware before editing the bank; older configurators cannot edit the expanded version-6 bank.

Valid output bytes are 0, 1, 2, 4, 5, and 6. The controller-button preference is retained even while another tab is selected. Every accepted profile includes this field. Whole-bank operations preserve original profile bytes rather than rewriting unrelated slots.

Digital binding fields are interface (1), report ID (1), bit offset (2), bit width (1), mode (1), flags (1), rapid rate (1), lower bound (4), and upper bound (4). Flags are signed=1 and report-ID-present=2. Mode 0 is disabled, 1 compares a range, and 2-5 represent hat Up/Right/Down/Left. Rapid fire is zero (Off) or 1–60 cycles per second; unassigned bindings and analog digital pairs require zero. The GUI starting rate for an Off output is 20, without rewriting stored rates or enabling outputs automatically. Firmware capabilities advertise bounds 1 and 60 in bytes 48–49. This range expansion changes no wire offsets or profile/bank versions; older firmware still rejects rates outside 2–30.

Digital output order is Y, B, A, X, L, R, ZL, ZR, Minus, Plus, left-stick press, right-stick press, HOME, Capture, Up, Right, Down, Left.

An analog binding contains interface (1), report ID (1), bit offset (2), bit width (1), flags (1), reserved (2), minimum (4), center (4), maximum (4), dead-zone percentage (2), and reserved (10). Flag bit 2 enables inversion. A digital-pair axis instead contains negative and positive 16-byte bindings; both held gives center. Axis order is LX, LY, RX, RY. Values received on separate interfaces/report IDs are retained and combined. Bit offsets exclude the report-ID byte.

## Profile bank and Flash

The version-6 bank is 13,908 bytes: 16-byte bank header, 16 x 848-byte profiles, 256 bytes of IDs, 4 bytes of preferred selections, and 64 bytes of shortcuts. IDs use the byte order of .NET `Guid.ToByteArray()` / `new Guid(bytes)`. The 16-byte outer CRC header makes a stored copy 13,924 bytes, leaving 2,460 bytes in each 16 KiB bank area.

| Storage slot | Offset with 2 MiB Flash | Size | Purpose |
|---:|---:|---:|---|
| 4 | `0x1F4000` | 16,384 | Bank A; first version-6 commit after migration |
| 5 | `0x1F8000` | 16,384 | Bank B; overlaps both old 8 KiB bank copies |
| Unused | `0x1FC000` | 8,192 | Reserved; no reads or writes |
| 0 | `0x1FE000` | 4,096 | Common settings, unchanged |
| 1 | `0x1FF000` | 4,096 | Saved measurement, unchanged |

The Flash header retains magic `CBST` (`0x54534243`), storage version 1, payload length, and CRC-32. The newest valid version-6 bank wins. If none exists, startup reads the version-5 copies at `0x1F8000` and `0x1FA000`, checks CRC and structure, selects the newest valid generation, and expands all profiles in RAM while preserving IDs, selections and shortcuts. Loading never writes Flash. The first migrated save writes/verifies Bank A before old banks can be overwritten; a later save can use Bank B only after a valid new copy exists. Tests simulate interrupted first and subsequent commits.

If neither a current bank nor a valid version-5 bank exists, startup creates 16 unused slots in RAM with generation zero and `persisted=false`. Pre-0.16.0 storage identities remain rejected. No EGRET preset is created. Common settings and saved measurements keep their existing addresses and identity.

Normal updates write and verify the inactive copy before adopting it. The total reserved tail is 48 KiB, and writes check for overlap with code. The unused 8 KiB remains reserved. The application UF2 validator rejects payload addresses at or above `0x101F4000` on the standard 2 MiB board.

Whole-slot JSON uses `ControllerBridge.Slots`, JSON version 3, UTC creation time, Base64 bank bytes, and SHA-256. Individual JSON uses `ControllerBridge.Profile`, JSON version 3, and excludes bank metadata. Version-2 exports with version-5 data are validated and migrated with shift disabled. JSON version 1 and binary profile versions 1–4 remain unsupported. Exports write a temporary file, read it back for verification, then replace the destination. Imports are limited to 256 KiB and validated before drafts change. Older firmware/configurators cannot read new records; retain a pre-upgrade backup for downgrade recovery.

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

The implementation is authoritative for byte-level command layouts: [`profile_runtime.c`](../lib/controller_bridge/profile_runtime.c), [`main.c`](../apps/egret_switch/main.c), [`HidBridge.cs`](../gui/ControllerBridge/HidBridge.cs), and [`controllerctl.py`](../tools/controllerctl.py).

Capability bits 0-12 represent rapid fire, IDs, on-board variants, shortcuts, immediate switching, mouse output, extended-profile/16-slot support (originally 488 bytes; read the explicit size field), combined output, combined full-gamepad buttons, extended trackball gain, 16-bit mouse output, whole-bank transfer, and per-profile output. Bit 13 (8192) adds guarded registration. Bit 14 (16384) adds button shift and the 848-byte profile. The normal firmware advertises 32767. Clients must check the relevant bits rather than infer support from names.

Command 48 keeps the first 26 bytes of command 24: slot at 0, profile CRC32 at 1, expected bank generation at 5, ID-present flag (must be 1) at 9, and 16-byte profile ID at 10. It adds expected input connection generation at 26, descriptor fingerprint at 30, VID/PID/revision at 34/36/38, and input protocol at 40. All multibyte integers are little endian. A dedicated command and capability prevent old firmware from silently ignoring the guard.

Registration requires a connected matching attachment and an empty saved destination. Commands 25 and 26 retain the guard until commit. Probing/disconnection invalidates the staged registration even when the same controller returns. Commit rechecks connection generation, destination, exact staged identity, descriptor matching, and that mapped sources exist before any Flash save. Conflicts return status 4; a Flash failure remains status 3. Ordinary command-24 profile editing still permits editing saved profiles while the input is absent. Guarded registration uses the current version-6 profile and validates modifier and overridden input sources as well as main mappings.

The 256-entry event ring stores field index, flags, value, and timestamp. GUI polling runs every 25 ms. Learning drains old events before taking its baseline and aborts on a connection generation change or an overrun. Captured input events do not trigger Flash writes.

The registration input log observes this same parsed-field stream before the learning-delivery gate, including baseline-drain events and the last received overrun event. It makes no additional USB requests. Initial snapshots have no device timestamp; event rows retain the original 32-bit microsecond timestamp without treating a wrap as negative elapsed time. The index is the zero-based firmware field index, independent of HID Usage/button numbering and output mapping indexes. Decimal values and width-masked hexadecimal values are recorded before GUI calibration or mapping. Identical USB reports from two physical controls cannot be distinguished, and unchanged fields are not a packet trace.

`InputLogBuffer` bounds PC history at 2,000 immutable rows with an eviction count. `InputLogForm` uses a virtual list and batches repainting at 100 ms; the existing learning path still receives every delivered change. `InputLogListView` explicitly enables the native double-buffer style. For pure appends, it preserves WinForms' managed item count while forwarding `LVM_SETITEMCOUNT` with `LVSICF_NOINVALIDATEALL | LVSICF_NOSCROLL`; offscreen appends do not repaint visible rows. Clearing or rotating the ring redraws item content, tracked by the buffer reset generation and eviction count. There is no unconditional whole-control invalidation on an ordinary update. Following only requests a scroll if the last row is outside the vertical viewport. Status text, including a retained notice, is composed before a single assignment, avoiding a temporary one-line layout on each two-line update. See Microsoft's [item-count flags](https://learn.microsoft.com/en-us/windows/win32/controls/lvm-setitemcount) and [native double-buffer style](https://learn.microsoft.com/en-us/windows/win32/controls/extended-list-view-styles).

Pausing the log does not pause polling or learning; resuming records an omission notice and a new snapshot. The log is a modeless sibling of the modal registration wizard, owned by the main form. Wizard closure finishes the log without reparenting windows during closing, so diagnostics remain available for export. All storage is on the PC; firmware buffers, persistent layouts, and board memory requirements are unchanged.

## Output scheduling and measurements

Output state values are 0 Gamepad, 1 Mouse, 65 combined mouse-button behavior, and 193 combined controller-button behavior. These differ from profile byte 484. Common configuration byte 7 retains its diagnostic protocol position; current output-mode changes must use profiles.

A layout change waits 250 ms after the response, disconnects USB-C, then reinitializes after another 250 ms. USB-A reception continues. Only the current button state initializes the new output; relative movement accumulated during disconnection is not replayed. Measurement starts after any output transition completes.

Mouse movement and click transitions use independent 64-entry queues. Report Protocol supports signed 16-bit movement and combines up to eight pending input movements; Boot Protocol uses signed 8-bit movement and combines up to two. Only acknowledged movement is consumed. Click transitions do not wait for queued movement to drain. Capacity overflow is counted rather than claimed to be lossless.

The current saved measurement payload is 3,784 bytes. Its `DUO1` extension separates mouse and gamepad counters, and `MBT1` records click transitions and the formats actually acknowledged. With the 16-byte header it occupies 3,800 of 4,096 bytes, leaving 296 bytes. Input-receive-to-output-ready/ACK statistics exclude the input controller's internal scan and the game's response. See [diagnostics](diagnostic-guide.md).

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
