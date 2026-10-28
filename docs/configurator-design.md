# Configuration and storage design

This describes the current ControllerBridge configurator 0.18.1 and firmware 0.18.1. It is an implementation reference, not a release history. See the [user guide](configurator-guide.md) for normal operations.

## Architecture and capabilities

Core 1 runs the PIO input host. Core 0 handles mapping, output, configuration, and the status LED. Input uses the pinned Pico-PIO-USB transport; the native RP2350 USB device path is implemented by this project. TinyUSB is used only by the separate CDC diagnostic target.

The GUI is a self-contained Windows x64 WinForms application built with the existing .NET 9 SDK. It edits the board's 16 slots directly. It requires 488-byte profiles, 16 slots, and capability bit 12 before writing per-profile output settings. Single-slot writes are read back with their identity before command 46 applies the effective output.

The input path supports one directly connected Full Speed device, up to four HID interfaces, eight IN endpoints, 64-byte input packets, and 256 observed fields. Variable HID inputs, report IDs, absolute axes, and four/eight-direction hats are supported. Relative movement uses the dedicated mouse path. See [compatibility](compatibility.md) for tested and untested devices.

## Profile wire format

The canonical profile is version 5, 488 bytes, little endian. The C definitions and C# codec validate fixed sizes and field ranges. Only version 5 is accepted. Earlier 480-byte and 488-byte versions are rejected without conversion.

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

Flag bit 3 (`UP_LED_CUSTOM`) enables the three LED bytes. With the bit clear all three bytes must be zero, and ready output is the existing R=0/G=0/B=1. With the bit set, bytes are output exactly; all-zero custom RGB means off. Matching still uses only flag bits 1 and 2 (values 2 and 4), so color does not change controller grouping, profile selection, or shortcuts. The 488-byte profile and 8,148-byte bank do not grow. The three former tail-reserved bytes are now fully allocated (48 bytes across 16 slots); the flags field still has 12 unused bits. Bank-copy spare space remains 28 bytes.

This extends the accepted version-5 values while retaining 0.16.x/0.17.x banks without migration: their LED flag/bytes are zero. JSON version 2 remains unchanged; missing LED properties decode as zero. Custom-color records require firmware/configurator 0.18.1 or later; older implementations reject the new flag/nonzero tail bytes. The normal GUI checks for exact matching firmware before editing the bank.

Valid output bytes are 0, 1, 2, 4, 5, and 6. The controller-button preference is retained even while another tab is selected. Every accepted profile includes this field. Whole-bank operations preserve original profile bytes rather than rewriting unrelated slots.

Digital binding fields are interface (1), report ID (1), bit offset (2), bit width (1), mode (1), flags (1), rapid rate (1), lower bound (4), and upper bound (4). Flags are signed=1 and report-ID-present=2. Mode 0 is disabled, 1 compares a range, and 2-5 represent hat Up/Right/Down/Left. Rapid fire is zero or 2-30 cycles per second; unassigned bindings and analog digital pairs require zero.

Digital output order is Y, B, A, X, L, R, ZL, ZR, Minus, Plus, left-stick press, right-stick press, HOME, Capture, Up, Right, Down, Left.

An analog binding contains interface (1), report ID (1), bit offset (2), bit width (1), flags (1), reserved (2), minimum (4), center (4), maximum (4), dead-zone percentage (2), and reserved (10). Flag bit 2 enables inversion. A digital-pair axis instead contains negative and positive 16-byte bindings; both held gives center. Axis order is LX, LY, RX, RY. Values received on separate interfaces/report IDs are retained and combined. Bit offsets exclude the report-ID byte.

## Profile bank and Flash

The version-5 bank is 8,148 bytes: 16-byte bank header, 16 x 488-byte profiles, 256 bytes of IDs, 4 bytes of preferred selections, and 64 bytes of shortcuts. IDs use the byte order of .NET `Guid.ToByteArray()` / `new Guid(bytes)`. The 16-byte outer CRC header makes a stored copy 8,164 bytes, leaving 28 bytes in each 8 KiB bank area.

| Storage slot | Offset with 2 MiB Flash | Size | Purpose |
|---:|---:|---:|---|
| 4 | `0x1F8000` | 8,192 | Bank A |
| 5 | `0x1FA000` | 8,192 | Bank B |
| Unused | `0x1FC000` | 8,192 | Reserved; no reads or writes |
| 0 | `0x1FE000` | 4,096 | Common settings |
| 1 | `0x1FF000` | 4,096 | Saved measurement |

The Flash header uses magic `CBST` (`0x54534243`), storage version 1, payload length, and CRC-32. This identity intentionally rejects all pre-0.16.0 Flash records, including profiles, common settings, and measurements. The newest valid current bank wins. If neither copy is valid, startup creates 16 unused slots in RAM with generation zero and `persisted=false`. It does not create an EGRET preset, inspect older bank areas, or write a migrated bank. The first normal save starts the new persistent bank; subsequent restarts read it normally.

Normal updates write and verify the inactive copy before adopting it. The total reserved tail is 32 KiB, and writes check for overlap with code. The unused 8 KiB remains reserved. The reset is a change in accepted storage identity, not a chip-wide secure erase.

Whole-slot JSON uses `ControllerBridge.Slots`, JSON version 2, UTC creation time, Base64 bank bytes, and SHA-256. Individual JSON uses `ControllerBridge.Profile`, JSON version 2, and excludes bank metadata. Both require the current product identifier and JSON version; previous exports are rejected. The binary profile/bank layouts remain version 5. Exports write a temporary file, read it back for verification, then replace the destination. Individual imports are limited to 256 KiB and validated before any draft is changed.

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

The implementation is authoritative for byte-level command layouts: [`profile_runtime.c`](../lib/controller_bridge/profile_runtime.c), [`main.c`](../apps/egret_switch/main.c), [`HidBridge.cs`](../gui/ControllerBridge/HidBridge.cs), and [`controllerctl.py`](../tools/controllerctl.py).

Capability bits 0-12 represent rapid fire, IDs, on-board variants, shortcuts, immediate switching, mouse output, 488-byte/16-slot profiles, combined output, combined full-gamepad buttons, extended trackball gain, 16-bit mouse output, whole-bank transfer, and per-profile output. The normal firmware advertises 8191. Clients must check the relevant bits rather than infer support from names.

The 256-entry event ring stores field index, flags, value, and timestamp. GUI polling runs every 25 ms. Learning drains old events before taking its baseline and aborts on a connection generation change or an overrun. Captured input events do not trigger Flash writes.

## Output scheduling and measurements

Output state values are 0 Gamepad, 1 Mouse, 65 combined mouse-button behavior, and 193 combined controller-button behavior. These differ from profile byte 484. Common configuration byte 7 retains its diagnostic protocol position; current output-mode changes must use profiles.

A layout change waits 250 ms after the response, disconnects USB-C, then reinitializes after another 250 ms. USB-A reception continues. Only the current button state initializes the new output; relative movement accumulated during disconnection is not replayed. Measurement starts after any output transition completes.

Mouse movement and click transitions use independent 64-entry queues. Report Protocol supports signed 16-bit movement and combines up to eight pending input movements; Boot Protocol uses signed 8-bit movement and combines up to two. Only acknowledged movement is consumed. Click transitions do not wait for queued movement to drain. Capacity overflow is counted rather than claimed to be lossless.

The current saved measurement payload is 3,784 bytes. Its `DUO1` extension separates mouse and gamepad counters, and `MBT1` records click transitions and the formats actually acknowledged. With the 16-byte header it occupies 3,800 of 4,096 bytes, leaving 296 bytes. Input-receive-to-output-ready/ACK statistics exclude the input controller's internal scan and the game's response. See [diagnostics](diagnostic-guide.md).

## GUI behavior

`ConnectionRead` reads command 47 before settings and owns the HID handle until the complete bank/input/output snapshot is ready. Unknown or different firmware returns a classified status without attempting to decode incompatible profiles. Background connection attempts have a separate `connecting` guard, so they do not toggle the main form's `working`/enabled state. Manual operations supersede pending attempts; late results after closure are disposed. A modal editor defers background discovery.

`FirmwareNoticeTracker` records each device/version notice for the session and only offers an unidentified-device guide on the initial discovery. The updater is opened after releasing the connection guard and performs its own fresh version check. Its return reconnects with automatic guidance suppressed, preventing cancellation loops. Version matching is exact equality, with no upgrade/downgrade ordering.

Status labels change only when the complete displayed text changes. Input events all reach the learning logic, while the live label receives only the final value in each polling batch. Empty USB-A host retry counters do not rebind the editor. Layout containers, the main form, firmware dialog, and custom buttons use buffered painting. Repeated offline discovery retains status, selection, and drafts without repeated enable/disable changes, layout, invalidation, or rebinding.

Names are persisted with duplicate suffixes; the list does not derive changing numbers from slot order. The standard client area is 1290 x 1000 and minimum window size is 1210 x 800 at 96 DPI. The profile pane is 400 pixels wide, with ellipsis for longer names. Analog sources use a live list with centered meters and multiple columns when needed. Documentation images are current English previews; the application retains both UI languages.

## Application icon

The Windows executable and application windows use `Resources/ControllerBridge_icon.png`. `tools/build-app-icon.ps1` produces `Resources/ControllerBridge_icon.ico` with 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixel frames. The 64-pixel source is preserved exactly; enlarged frames retain its pixel edges. `tools/build-configurator.ps1` regenerates the ICO before publishing. The project embeds it as both the Windows executable icon and a managed resource used by the common form styling helper. This does not affect firmware resources or board memory.
