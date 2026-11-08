# ControllerBridge documentation

These documents describe the current Windows configurator v0.22.0 - GUI r3 and normal firmware 0.22.0. Documentation and screenshots are English. The application itself retains Japanese and English interfaces.

The current configurator is **v0.22.0 - GUI r3**; its bundled firmware remains **0.22.0**. The unsuffixed base is the first edition, and the first GUI-only revision starts at **r2**, following the committed v0.19.0 and v0.20.16 numbering. Uncommitted edits and rebuilds keep the same GUI revision; later committed GUI releases increment it; a new main version starts without a suffix.

| Document | Contents |
|---|---|
| [Sample slot configuration](../samples/README.md) | Included full-bank sample and restore instructions |
| [User guide](configurator-guide.md) | Setup, profiles, mapping, calibration, output modes, Mouse UDLR settings, backup and restore |
| [Button shift](button-shift.md) | Hold-to-shift keys, inherited and overridden buttons, rapid fire, shared axes and migration |
| [Firmware update](firmware-update.md) | Bundled-version comparison, confirmations, automatic programming, first installation and recovery |
| [Configuration and storage design](configurator-design.md) | Current profile/bank formats, Feature commands, scheduling, and GUI behavior |
| [Reference-board case](case.md) | Clear PETG case remix, sample photos, STL downloads and original-design credits |
| [GPIO assignment and board porting](gpio-assignment.md) | Standard pins and exact source locations to change for another board |
| [Status LED](status-led.md) | Per-profile RGB settings, operating colors, profile number indication, and LED diagnostics |
| [Compatibility](compatibility.md) | Tested controllers, unsupported cases, and unresolved limitations |
| [Verification](verification.md) | Current build checks, current-format validation, empty startup, screenshots, and memory budget |

The 0.22.0 conversion and shared-settings screenshots use synthetic profiles. Conversion mapping distinguishes Trackball **Virtual Cursor** from Paddle **Virtual Rolling** and includes a per-profile inertia checkbox; the eight-field shared dialog groups controls by mode and shows default inertia tuning 8 / 0.90 / 100 ms. The automatic mismatch example shows the short notice for confirmed published older saved data. The BOOT confirmation omits it because saved data cannot be inspected on that route. The main-window image now shows the right-hand conversion action and separate raised Trackball/Paddle groups in 0.22.0 - GUI r2. Other older shift, DPI and BOOT-wait images retain their capture versions.

The application retains Japanese and English interfaces. Current profile editing uses **Save profile to device**.
