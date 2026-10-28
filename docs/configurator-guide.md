# ControllerBridge user guide

Windows x64 configurator 0.18.1 and RP2350-USB-A firmware 0.18.1. The application follows the Windows display language: Japanese on Japanese Windows, English otherwise. This guide and all repository screenshots use English. Saved profile names are not translated.

## Connect and start

1. Connect the input controller directly to the board's USB-A port.
2. Connect USB-C to a Windows PC.
3. Run `ControllerBridge.exe`.
4. Follow the automatic firmware guide if the tool detects a different version or cannot obtain one. You can also open **Firmware update** manually. Follow its BOOT connection and overwrite-confirmation prompts if the existing firmware is not recognized. See [firmware update](firmware-update.md).
5. Check the connection status and the **Profile list**. Installing this release over firmware older than 0.16.0 starts with all 16 slots unused; choose **Add controller** to register each controller again. New registrations persist across restarts.

The executable includes its runtime; no separate .NET installation is required. Only one instance can run. Close other bridge configuration tools while using the GUI. When the board is disconnected, the application retries about every two seconds. **Connect / Reconnect** starts an attempt immediately. An empty USB-A port shows a stable waiting message during enumeration retries.

The initial guide also covers factory firmware, other software, and no detected board; the PC cannot always tell these apart. Declining leaves a stable status message and does not repeat the same popup during retries. Use **Firmware update** when ready. Background retries keep the editor enabled and preserve unsaved edits. A firmware mismatch is shown as an update requirement, rather than an unexplained disconnected state.

![Current English profile list](gui-main-en.png)

## Profiles and slots

The board stores 16 profiles in total, including multiple profiles for one controller. Each stores its name, device matching information, output mode, button behavior, mappings, rapid fire, input interval, mouse settings, and LED color. The active profile has an indicator in the list.

Selecting a registered slot opens its settings without changing the active profile on the board. **Use this profile** activates a saved profile matching the connected controller. Unsaved edits have a `*`; choose **Save slot to device** to apply them. An unused slot shows **Unused**, hides the settings, and offers **Add controller**.

### Add, duplicate, and delete

**Add controller** opens a registration wizard. Confirm the detected device, assign inputs, configure any analog axes, and save to the board. Registration requires valid input from the connected controller.

**Duplicate profile** copies the selection into the nearest unused slot below it. It creates a new ID and appends an available `[2]`, `[3]`, and so on to the stored name. Copying a numbered copy does not nest suffixes. The original name stays unchanged. Reordering or deleting other profiles does not renumber names. Names are shortened at complete Unicode character boundaries when necessary to fit the 47-byte UTF-8 limit. Save the new slot to persist it. Duplication does not wrap to unused slots above the source.

**Delete profile** marks the selected slot unused after confirmation. Save that slot to apply the deletion. Before saving, **Reload device** can discard the draft.

### LED color

Select a profile, then choose **LED RGB settings** directly below **Delete profile**. **Default dim blue** retains the original ready color and brightness: R=0, G=0, B=1. Choose **Custom RGB values** to enter each channel from 0 to 255. The channel values are sent directly, without brightness scaling or RGB565 quantization. R=0, G=0, B=0 turns off the ready LED.

**OK** changes the selected draft; **Cancel** leaves it unchanged. Choose **Save slot to device** to persist it. The color is shown when that profile is active, input is received, and the output host is ready. Browsing another slot does not change the hardware color. Error, save, waiting, and profile-number indications retain their existing colors and priority. After an indication ends, the active profile color returns.

![LED RGB settings](gui-led-rgb-en.png)

The `#` field below the R/G/B spinners accepts six hexadecimal digits with or without a leading `#`; the prefix is removed automatically. For example, paste `000004` or `#000004` to set R=0, G=0, B=4. Uppercase and lowercase letters are accepted. Editing either the code or a spinner updates the other controls and the preview. Incomplete, invalid or overlong codes cannot be confirmed; they are not silently shortened into another color. **Cancel** remains available while editing an incomplete code.

The single preview uses gamma 4.0 internally, following the user's comparison with the actual LED. There is no gamma control, explanatory curve text or uncorrected swatch. Earlier PC preview preferences are ignored. The entered code and channel bytes remain exact; this display correction does not alter the profile or firmware output. The bundled firmware remains 0.18.1, so replacing the configurator EXE is sufficient.

New profiles and existing 0.16.x/0.17.x profiles default to dim blue. Duplicating a profile copies its color; reordering, individual export/import, and whole-bank backup/restore retain it. Selecting **Default dim blue** removes the custom setting. The color is not assigned automatically or required to be unique; use a different color for each profile if desired.

### Reorder

Drag a registered profile to the blue insertion line. The upper half of a row inserts before it; the lower half inserts after it. The list scrolls near its edges. `Alt+Up` and `Alt+Down` also move the selection.

Save pending edits before reordering. A completed move is saved immediately and verified by reading back the bank. Profile data, ID, shortcut, and saved selection move together. BOOT and controller shortcuts cycle matching profiles in this order.

## Backup and restore

- **Back up all slots** saves all 16 persisted slots, their order, IDs, shortcuts, and selections to one JSON file.
- **Restore all slots** validates the file, asks for confirmation, replaces the bank, and reads it back for verification.

Per-profile output modes, button behavior, and LED colors are included. Common diagnostic settings and saved measurements are separate. Save or discard drafts before either operation.

New backups use `ControllerBridge.Slots`, JSON version 2, with a SHA-256 checksum. Older backup files, including JSON version 1, are rejected. There is no migration from earlier saved settings. The board validates the full payload, generation, CRC, and profile structure before writing the inactive Flash copy. An interrupted transfer before commit does not replace the active bank.

### Individual profile files

For occasional individual transfers, right-click a registered row and choose **Export**, or right-click an unused row and choose **Import**. The keyboard menu key and `Shift+F10` also open the selected row's menu. No additional buttons appear in the normal interface.

Export includes current edits. Import creates an unsaved draft; **Save slot to device** persists it. Occupied slots cannot be overwritten this way. Duplicate names receive a number; missing IDs or IDs conflicting with another slot receive a new ID. Unique names and IDs remain unchanged. Canceling or rejecting a file preserves existing drafts.

Individual files use `ControllerBridge.Profile`, JSON version 2. Earlier exports are rejected. They exclude slot order, shortcuts, and active selection, and cannot replace whole-slot backups.

## Map inputs and calibrate axes

Select an output control, release the physical input, and perform the action to assign. Already-held inputs must return to neutral before learning. A connection change or event overrun cancels learning.

Analog axes support measured minimum, center, maximum, a 0-50% dead zone, and inversion. Alternatively, use two digital inputs for negative and positive movement; pressing both produces center.

The analog dialog displays all candidate axes with live centered meters and raw values. Move the physical control to identify its axis, then select the matching row. Unselected axes update too. Large sets use multiple columns when needed.

![Current English analog input dialog](gui-axis-en.png)

The meter uses the descriptor range before calibration, inversion, or dead zone. A range crossing zero uses zero as its reference; other ranges use their midpoint. Missing input shows a waiting state; out-of-range values appear gray. Select the source, measure its travel, record the released center, and apply the result.

**Rapid fire...** sets assigned buttons to Off or 2-30 press/release cycles per second while held. Apply the dialog, then save the slot. Rapid fire is unavailable for unassigned buttons or digital pairs used as analog axes.

**Input interval (ms)** belongs to the profile. `0` uses the controller descriptor's interval; `1` requests a transfer every millisecond. The controller can answer NAK when no data is ready. Request rate does not establish its internal scan rate or the game's sampling rate.

## Output modes

| Tab | Output |
|---|---|
| Gamepad | Mapped buttons, directions, and stick axes as a Switch-compatible gamepad |
| USB mouse | Relative movement and buttons as a standard USB mouse |
| Mouse + Gamepad | Separate mouse and gamepad HID interfaces on one USB device |

Selecting a tab edits the profile. Save the slot to persist it. Only the combined tab shows **Button behavior**:

- **Mouse** sends left/right clicks while the gamepad supplies Minus, Plus, and HOME. Left/right click share the B/A mappings.
- **Controller** sends all mapped gamepad buttons, directions, and axes. The mouse supplies movement with its buttons released.

A USB layout change reconnects USB-C; the USB-A input host remains running. Changing only combined-mode button behavior needs no USB reconnection.

For the EGRET II mini Paddle Controller, trackball and paddle can be enabled independently. Trackball gain is 1.0-64.0, default 16.0; paddle gain is 1.0-10.0, default 1.0, both in 0.1 steps. The paddle produces horizontal movement. With both enabled, horizontal trackball and paddle movement are added.

## Switch profiles without a PC

Use **Profile switching...** to select a roughly one-second BOOT hold or a controller shortcut for all profiles matching that controller. A shortcut needs at least two nonconflicting buttons or directions pressed within 100 ms. Release them before repeating it. Shortcut inputs also reach the game.

The shortcut method disables BOOT monitoring. Purple LED flashes indicate the matching profile's number. Selection is saved and survives reconnection. See [LED behavior](status-led.md) and [device compatibility](compatibility.md).
