# ControllerBridge documentation

These documents describe the current Windows configurator v0.22.0 and normal firmware 0.22.0. Documentation and screenshots are English. The application itself retains Japanese and English interfaces.

The configurator and bundled firmware are released together as **v0.22.0**, without a GUI revision suffix.

| Document | Contents |
|---|---|
| [User guide](configurator-guide.md) | Setup, profiles, mapping, calibration, output modes, Mouse UDLR settings, backup and restore |
| [Button shift](button-shift.md) | Hold-to-shift keys, inherited and overridden buttons, rapid fire, shared axes and migration |
| [Firmware update](firmware-update.md) | Bundled-version comparison, confirmations, automatic programming, first installation and recovery |
| [Configuration and storage design](configurator-design.md) | Current profile/bank formats, Feature commands, scheduling, and GUI behavior |
| [Reference-board case](case.md) | Clear PETG case remix, sample photos, STL downloads and original-design credits |
| [GPIO assignment and board porting](gpio-assignment.md) | Standard pins and exact source locations to change for another board |
| [Status LED](status-led.md) | Per-profile RGB settings, operating colors, profile number indication, and LED diagnostics |
| [Compatibility](compatibility.md) | Tested controllers, unsupported cases, and unresolved limitations |
| [Verification](verification.md) | Current build checks, current-format validation, empty startup, screenshots, and memory budget |

The 0.22.0 conversion and shared-settings screenshots use synthetic profiles. Conversion mapping includes a per-profile inertia checkbox; the eight-field shared dialog groups controls by mode and shows default inertia tuning 8 / 0.90 / 100 ms. The automatic mismatch example shows the short notice for confirmed published older saved data. The BOOT confirmation omits it because saved data cannot be inspected on that route. Older main-window, shift and BOOT-wait images retain their capture versions.

The application retains Japanese and English interfaces. Current profile editing uses **Save profile to device**.
