# Diagnostics and measurements

Normal configuration uses `ControllerBridge.exe`. Diagnostic tools are for input inspection and controlled measurements. Close the GUI before using another tool that opens the same HID configuration interface.

## Inspect the normal firmware

```powershell
python tools/controllerctl.py info
python tools/controllerctl.py --help
python tools/capture-input.py --help
```

`info` reports connection and output state. `capture-input.py` can read device descriptors and packets through the normal firmware's Feature interface. The exact options and available measurement commands are documented by each tool's help output. Windows HID discovery verifies the protocol signature as well as USB identity.

Use the GUI to change profile mappings, output mode, and mouse gain. Legacy common-output or mapping setters may be rejected by the current firmware because those settings now belong to profiles.

## Measure output

```powershell
python tools/controllerctl.py measure --seconds 10 --out captures/measurement.json
```

Use a new output filename for each measurement. The saved result distinguishes input reception, output preparation, host acknowledgments, queue depth, errors, and drops. A completed firmware-side measurement may be saved to Flash and retrieved later; inspect `measure`, `arm`, and `snapshot` help before using those operations.

Input receive timestamps are taken after the board processes a completed input transfer. They exclude the controller's internal scan, the earlier USB polling wait, display latency, and game response. An ACK establishes that the USB host accepted a report, not that a game acted on it.

Requests every 1 ms can return NAK without new data. Changing the polling interval does not change the controller's internal update rate. For input latency, compare actual state transitions rather than idle repeats. Check duration, transition counts, connection generation, and saved-measurement identity before comparing records; a retrieved older snapshot is not a new measurement.

Combined mode has separate mouse and gamepad counters. Mouse movement totals and click edge counts are separate; compare acknowledged totals with received totals while considering queue clears and modulo-32-bit counters. Queue overflow and deliberate clears are reported.

## Input-only CDC firmware

The separate `egret_diagnostic` target accepts the EGRET II mini arcade panel and exposes a USB-C CDC log. It is not the normal bridge firmware. Building it does not flash it:

```powershell
.\tools\build-diagnostic.ps1 -Target egret_diagnostic
```

The output is `build/egret_diagnostic.uf2`. The tool uses the pinned Pico SDK 2.1.1 and Arm GCC 14.2.1 already installed on the machine and validates dependency revisions and recorded local changes without modifying them.

On a board running this target, connect the panel to USB-A and open USB-C CDC at 115200 baud with DTR enabled.

| Character | Action |
|---|---|
| `d` | Print cached device, configuration, and HID report descriptors |
| `0` | Restore the descriptor's input interval |
| `1` | Request experimental 1 ms input polling |

`DESC` records descriptor bytes, `REPORT` records input bytes/decoded values, and `STATS` records counters. `REPORT us=` is the board's receive-processing timestamp. Empty input retries do not mean a successful attachment; valid enumeration and traffic establish readiness.

To capture a CDC session, substitute the actual serial port for the example:

```powershell
.\tools\capture-diagnostic.ps1 -Port COM4 -Seconds 30 -Command '1d' -OutputPath '.\captures\new-run.log'
```

The summarizer under `tools/` reads a fixed list of capture files; adjust that list to match your own captures before running it.

## Optional build diagnostics

The normal bridge build disables USB control tracing and fixed-motion probes. `-UsbTrace` adds a RAM-only USB control trace. `-MouseFixedProbe`, `-MouseProbeAmplitude`, and `-MousePacketBytes` are controlled diagnostic build options, not normal controller tuning. A later normal build explicitly disables the optional trace/probe flags.

Current memory figures describe the normal `ControllerBridge` target only. Diagnostic targets and optional flags have their own allocations. See [verification](verification.md) and the [protocol reference](configurator-design.md).
