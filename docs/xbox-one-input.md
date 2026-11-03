# MS-GIPUSB controller input

ControllerBridge 0.20.20 adds MS-GIPUSB input for wired Xbox One-family controllers. Connect the controller directly to the board's USB-A port with a data-capable cable. The configurator displays **MS-GIPUSB** after initialization. Choose **Add controller**, learn the desired buttons and analog axes, and save the new profile. Existing profiles are preserved. On Elite Series 2, initialization may take several seconds after restarting or updating the bridge; wait for **MS-GIPUSB** before registering. If the LED remains cyan after connection, operate a button or stick to confirm input reception.

The implemented fields are ABXY, Menu/View, D-pad, LB/RB, L3/R3, two 10-bit triggers, four signed 16-bit stick axes, the separately reported Xbox/Guide button and supported Elite paddle layouts. Main input, Guide and paddle packets update independent fields so receiving one does not clear the others. Elite firmware or on-controller remapping may affect whether a paddle produces a distinct field; use the input log to inspect the actual controller.

The physical reference device is a genuine Microsoft Elite Series 2 (`045E:0B00`, USB revision `050F`). Its normal input and recovery after bridge restart/firmware update were checked. Other Xbox One/S/Series/Elite revisions and compatible third-party pads have parser or initialization support but have not been physically verified. Share is not exposed as a separate field. Headset audio, controller firmware updates, runtime vibration, Xbox-console authentication, Bluetooth and wireless adapters are outside this implementation. Xbox compatibility here means the **input controller**; it does not add Xbox-console output support.

ACK responses continue for every requested GIP message during operation. Fragmented reliable transfers use cumulative acknowledgements and bounded retransmission. There is no requirement to send an arbitrary heartbeat when the controller has requested no ACK. The recovery paths for Elite Series 2 are limited to initial connection and do not add reset or resume operations to active input.

The user has reported successful Switch operation with this controller; an instrumented Switch/game output capture has not been added. No Xbox profile was automatically added to the saved bank. Mapping and independent report state passed host-side tests. See [verification](verification.md) for the exact input coverage, recovery evidence and current memory figures.

## Startup recovery and LED states in 0.20.21

Elite Series 2 (`045E:0B00`) gets one extra recovery cycle after a GIP initialization timeout. After the existing 750 ms retry delay, the firmware can retry USB resume for a silent controller and GIP Reset for incomplete startup. Each operation is bounded within that extra cycle. If it fails again, ordinary detection continues, but the extra recovery is not repeated indefinitely. A valid input report ends the failure episode and makes one extra cycle available for a later failure. A successful handshake without input does not renew that allowance.

| Display | Meaning during startup |
|---|---|
| Red blink | Initialization failed; bounded automatic recovery and normal detection may still be in progress. |
| Cyan blink | Initializing, or connected and waiting for the first accepted input. This wait has no idle timeout. |
| Steady profile color, initially dim blue | Input has been received, the matching profile is applied and output is ready. |

The extra cycle runs only for initialization timeouts. It does not restart a configured controller because it is untouched, and does not change the saved profiles or settings. Recovery behavior is covered by deterministic tests of the production host loop. The user also confirmed the physical fix in 0.20.21: with the controller connected, a GUI firmware update that previously left the board blinking red now progresses from a brief red blink to cyan, then steady dim blue after controller input, without replugging. The user accepted this update-time startup issue as resolved. See [verification](verification.md) for the record.
