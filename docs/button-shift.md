# Button shift

ControllerBridge 0.20.0 adds one shifted button page inside each profile. Hold all configured shift keys to use it; release any one to return. The active profile, USB output mode, four axes, mouse movement settings and LED color stay common. No page transition saves Flash or reconnects USB.

This follows the hold action and inherited-mapping concept described in the [reWASD Shift mode guide](https://www.help.rewasd.com/basic-functions/shift-mode.html). ControllerBridge implements one button-only page, up to three AND modifiers and the rules below; it does not reproduce reWASD's complete layer/shortcut system.

## Configure

1. Select the profile, or open the mapping step while adding a controller.
2. Choose **Button shift…** beside **Rapid fire…**.
3. Release all inputs, choose **Learn shift keys**, then press the desired buttons together and release them all. The gesture is registered when its last input is released; it can include buttons arriving on different reports. You can also add one key at a time. Up to three different keys can be registered in total. Already registered keys are ignored; a gesture exceeding the remaining capacity is rejected as a whole. A key does not need a normal output mapping. All configured keys must be held together during play; there is no time limit between their presses.
4. Select a shifted output and perform the physical input to assign. Outputs you leave unchanged inherit the main page. **No output** explicitly mutes the selected output while shifted; **Inherit from main** removes that override. A `*` marks an actual registered shifted input. An unassigned/no-output override has no star and its tooltip says **no registered shifted mapping**. The no-output setting still prevents inheritance; clearing a main-page mapping does not delete a separately assigned shifted input. **Rapid fire…** can give a shifted output its own Off/1–60 Hz setting. Newly enabling an Off output starts at 20; existing rates are preserved.
5. Choose **Apply to draft**, then **Save profile to device**, or finish registration. Closing with Cancel discards the dialog's edits. Removing every shift key disables shift while retaining the stored shifted mappings for later use.

Shift key names use the same **Button N** numbering as the input log and live input status. The USB source details remain alongside the name to distinguish equal numbers on different interfaces or report IDs. These names come from the matching controller's descriptor. Without that descriptor, the dialog shows **Input name unavailable** and the saved source details instead of guessing a button number. The registration review uses the same names.

![Button shift settings with two modifiers and one overridden output](gui-shift-en.png)

This image uses synthetic test mappings. The dialog has no independent axis settings: LX, LY, RX and RY always use the common profile settings. D-pad directions remain normal digital outputs; no new four-way or diagonal correction is applied.

## Inheritance and moving a physical input

Mappings are edited by output button. With no override for an output, its current main-page mapping is inherited. A muted override disables only that output. A changed binding replaces that output's main binding while shifted.

If an override uses the physical input of an inherited main output, the inherited output is suppressed while that overridden binding is pressed. For example, if physical button P normally sends Y, assigning P to shifted A produces:

| Input | Output |
|---|---|
| P | Y |
| Shift + P | A |
| Release Shift while P stays held | Y |
| Release P | Neither Y nor A |

Thus the moved input does not also produce inherited Y. Other physical inputs and unchanged outputs continue to work. Explicitly assigning the same source to several shifted outputs is still allowed. Overlap is based on the physical field and active range, not display names.

In USB mouse or combined mouse-button behavior, B/A remain the shared left/right click mappings. Shifting those mappings therefore changes the clicks. The selected output mode still determines which mapped outputs are sent; shift cannot change a mouse profile into a gamepad profile. Mouse movement stays common.

## Modifier and release behavior

- One to three modifiers form an AND condition. A single modifier activates shift immediately. There is no toggle, tap action, hold delay, or multi-key timing window.
- Once the full chord activates, modifier inputs cannot produce normal button outputs on either page. Their use by a common axis is also suppressed while consumed; other axis inputs continue normally.
- Releasing any modifier immediately restores the main page. Modifier output suppression remains until every modifier has been released, so a remaining held modifier does not suddenly fire its main mapping. Holding all modifiers again can reactivate shift without a Flash write.
- For a multi-key chord, an individual modifier can produce its main mapping **before the chord first completes**. Output already sent cannot be withdrawn. Dedicated physical keys with no ordinary mappings avoid this. A single immediate modifier cannot also perform a normal press action.
- Buttons already held at a page change are reevaluated from cached input, releasing the old output and applying the new mapping without requiring another physical press. Release/disconnection clears the appropriate state. Rapid phases restart on page changes.
- Permanent profile shortcuts cannot overlap modifier inputs. The GUI and firmware reject such configurations. Profile hotkeys use main-page physical mappings. Profile cycling, including BOOT-triggered cycling, is rejected while shift modifiers are consumed; release them all before switching profiles.

## Upgrade and backup

Install matching 0.22.0 firmware and GUI. v5 settings gain disabled shift; v6 settings preserve the first three modifiers, shifted mappings and rapid rates, discarding only the fourth modifier if present. Main mappings, axes, IDs, slot order, selection, shortcuts, RGB and output settings are preserved. Pre-0.16.0 storage remains unsupported.

Exports use JSON v5; old JSON v2/v3 files are accepted. Old files lack common conversion settings, so the import notice explains whether device migration defaults or standard defaults were used. New v8 profiles keep 848 bytes by reusing the old fourth modifier's 16 bytes for 10 bytes of conversion settings plus 6 reserved bytes. Old firmware cannot read the new format; retain a pre-upgrade backup for downgrade recovery.

Migration writes and verifies the inactive bank before adopting it; v5 migration first uses Bank A, away from both old small copies. Later saves alternate two 16 KiB areas. See [storage layout](configurator-design.md#profile-bank-and-flash), [memory budget](../README.md#memory-budget), and [verification limits](verification.md).
