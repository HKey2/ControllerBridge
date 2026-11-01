# ControllerBridge documentation

These documents describe the current Windows configurator 0.20.11 and normal firmware 0.20.11. Documentation and screenshots are English. The application itself retains Japanese and English interfaces.

| Document | Contents |
|---|---|
| [User guide](configurator-guide.md) | Setup, profiles, mapping, calibration, output modes, Mouse UDLR settings, backup and restore |
| [Button shift](button-shift.md) | Hold-to-shift keys, inherited and overridden buttons, rapid fire, shared axes and migration |
| [Firmware update](firmware-update.md) | Bundled-version comparison, confirmations, automatic programming, first installation and recovery |
| [Configuration and storage design](configurator-design.md) | Current profile/bank formats, Feature commands, scheduling, and GUI behavior |
| [GPIO assignment and board porting](gpio-assignment.md) | Standard pins and exact source locations to change for another board |
| [Status LED](status-led.md) | Per-profile RGB settings, operating colors, profile number indication, and LED diagnostics |
| [Diagnostics](diagnostic-guide.md) | Input inspection, measurements, CDC target, and optional probes |
| [Compatibility](compatibility.md) | Tested controllers, unsupported cases, and unresolved limitations |
| [Verification](verification.md) | Current build checks, current-format validation, empty startup, screenshots, and memory budget |

The Mouse UDLR settings button and dialog screenshots use 0.20.11 with synthetic profile data and default 64 pt / 30 ms hold / 20 ms wait values. The LED RGB image also uses 0.20.10 and shows the square eyedropper icon. The Mouse UDLR dialog omits the extra hold/wait explanation below its fields. The movement-conversion screenshot uses 0.20.7 with both conversion options enabled and pointer gain still editable. The cursor gains are separate hidden settings. Normal combined output keeps pointer movement active while adding direction clicks. The UDLR preview shows the read-only common threshold without displaying cursor gains. The paddle-axis screenshot uses the 0.20.2 GUI. Rapid-fire and button-shift screenshots use 0.20.1 with the shared save reminder. Main-window and firmware-update references retain 0.19.0 images; the axis reference is from 0.16.0. Their labels describe those captures. Current profile editing uses **Save profile to device**.

- [UDLR keyboard test build and usage](udlr-test.md) — source-retained, compile-time-only diagnostic.
