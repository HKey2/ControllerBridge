# Native gyro controls in Pro output

Use **Pro** output to send motion sensors to games that provide their own gyro controls. Enable **Gyro (Pro)** for the intended gamepad profile and choose **Save settings to device**. The selection is saved separately for each profile and is included in profile duplication and backups.

1. Install the firmware bundled with this version, including when the displayed version is already 0.34.0.
2. Open **Shared settings**, select **Pro**, and save.
3. Select the controller's gamepad profile, enable **Gyro (Pro)**, and save it to the device.
4. For DS3, keep the controller still for about one second after connecting or activating the profile. Then enable the game's motion or tilt steering option. Use the game's own centering operation if provided.

| USB input controller | Native motion supplied |
|---|---|
| Original Switch Pro Controller | Three gyro axes and three accelerometer axes |
| Switch 2 Pro Controller | Three gyro axes and three accelerometer axes, converted to Pro coordinates |
| DUALSHOCK 3 with readable motion calibration | One gyro axis perpendicular to the controller face, suitable for turning it as a steering wheel; three accelerometer axes |

For DS3, use the **DS3: X / Y / Z** list beside the native gyro checkbox to select the Pro gyro axis that receives its single rotation sensor. The initial selection is **Z**, preserving existing profiles. Choose **X** or **Y** when the game uses a different axis, then save the profile. The selection survives reconnection, duplication and backup restoration. This changes only the destination gyro axis; acceleration retains its physical coordinates.

The two unselected gyro axes always report zero angular velocity. DS3 cannot reproduce three-axis gyro aiming, and its rotation sensitivity is approximate because the sensor does not provide the same calibration data as a Pro Controller. Acceleration is retained so games can observe gravity and tilt. The game's choice of sensors and steering behavior determines how it responds.

While native motion is active in Pro output, stored gyro-to-stick assignments are suspended to prevent one movement from driving both inputs. Their saved settings remain available in other output modes or when native motion is disabled. Ordinary physical stick and button assignments remain active. The stick-gyro range, sensitivity and Recenter settings do not control native motion.

Native motion is available for gamepad-only profiles with a supported Full Speed USB input. X, S, GC and mouse output do not send this Pro motion stream. GC analog L/R remain a separate, provisional option; GC analog triggers and native Pro gyro cannot be sent together in one output mode. The converter does not create game support for motion controls or analog pedals.

DS3 establishes its initial gyro neutral while still; it does not continually absorb a held turn into neutral. Rumble inhibits neutral learning. If the controller was moving during activation, reconnect it or switch away and back to the profile, then keep it still. A stopped or disconnected input does not continue reporting an old rotation.
