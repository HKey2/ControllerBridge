# GameCube output and independent analog L/R

Version 0.34.0 adds **GC** in **Shared settings → Host mode**. It presents a GameCube USB controller adapter with one wired controller on port 1. The other three ports remain disconnected. Use this mode to supply two independent analog trigger values in addition to LX, LY, RX and RY.

## Set up L/R

1. Install the firmware bundled with this version of ControllerBridge. Existing profiles and shared settings are retained; new GC L/R assignments start unassigned.
2. Select the intended gamepad profile. Choose **GC L** or **GC R** on the same row as the **Analog inputs** heading. These buttons use the same width and input-selection layout as the stick-axis settings.
3. Choose the source for each trigger from the input buttons, or select **Move to detect an input axis**. For a DUALSHOCK 3, its **L2 pressure** and **R2 pressure** fields are available. These are separate from its binary button fields.
4. Release the chosen input and select **Measure range**. Move it to its full position, release it, and choose **Record released**. The numeric fields can also be edited. A decreasing source range is supported. Adjust **Dead zone %** if needed.
5. Choose **Apply**, then **Save settings to device** in the main window.
6. Open **Shared settings**, select **GC**, and choose **Save to device**. USB reconnects using the selected format. Connect the converter to the console to try it in the game.

Each trigger maps its own released-to-full range to 0–255. Simultaneous inputs retain their individual amounts. Pressing the selected input button again clears that trigger. GC L/R do not consume either stick or modify existing RY assignments, DS3 gyro steering, or digital-button assignments. They are stored per profile and included in duplication, reordering, profile export and full-slot backup.

## Buttons and limits

The GUI's chosen button names and physical layout remain unchanged. In terms of the fixed Switch button IDs:

| GUI ID | GameCube output |
|---|---|
| A / B / X / Y | A / B / X / Y |
| L / R | Digital L / R |
| ZR | Z |
| + | START |
| D-pad and both sticks | D-pad, control stick and C stick |
| ZL, −, stick clicks, HOME, CAPTURE | No equivalent GameCube output |

Analog L/R are separate from digital L/R. Pressing an analog source does not automatically set a digital button, and assigning a digital button does not set an analog amount. The profile keeps unsupported assignments for use in other output modes. Use another controller for console functions without a GC equivalent.

GC is selected explicitly; **Auto** continues to choose between X and S. Profiles that include USB mouse output use S while GC is selected. USB Low Speed inputs remain button/D-pad only, so GC analog L/R are inactive on those connections. GC mode does not forward rumble. DS3 gyro can still be converted into a stick axis, but the mode does not send native motion-sensor data.

On Windows, the standard HID driver does not expose this adapter format as a normal gamepad. ControllerBridge retains its separate configuration connection in GC mode. Use Auto or X for ordinary Windows gamepad use.

GC analog support is provisional. The game must accept GameCube controllers and read their analog triggers. This mode does not identify as a dedicated racing wheel, and analog pedal support in a particular game is not established by USB acceptance alone. A game may use these values for another purpose, such as camera movement, rather than accelerator and brake control. See [native Pro gyro controls](native-motion.md) for games with motion steering.

The OLED uses **G** for GC output. Detail B shows GC analog L/R in its LT/RT fields, from `00` to `FF`.

## Compatibility reference

The adapter wire format is informed by [loopj/usb-gamecube-controller-adapter](https://github.com/loopj/usb-gamecube-controller-adapter), under its [MIT license](../licenses/gamecube-adapter-MIT.txt). ControllerBridge implements its own profile mapping and USB command handling.
