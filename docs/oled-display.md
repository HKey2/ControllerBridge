# OLED status display

Firmware 0.32.0 supports a 64×32 OLED on the Waveshare RP2350-USB-A with SDA on GP6 and SCL on GP7. Connect the display to 3.3 V and GND. The display uses I²C1 at address 0x3C or 0x3D. The supported panel uses SSD1306-compatible commands, column offset 32, 32 display rows, and A1/C8 orientation; panels with different geometry need different firmware settings.

The firmware checks the two display addresses only at startup. If neither responds, no display updates are attempted until the next restart. Connecting a display after startup therefore requires restarting the board.

## Connection reference

![SSD1306 OLED connection pins on the RP2350-USB-A: GND, 3V3, GP6/SDA and GP7/SCL](images/oled/ssd1306-oled-connection-pins-rp2350-usb-a.png)

Reference wiring: ① GND, ② 3V3, ③ GP6 / SDA, ④ GP7 / SCL.

## Display type

Open **Shared settings → Display**, immediately below **Host mode**. Select a type and choose **Save to device**. The choice applies to every profile and is retained after restart. OFF is used when no valid display setting has been saved, including an upgrade from firmware without display settings. An existing saved display type is preserved during updates. Use the matching 0.32.0 configurator and its bundled firmware; an earlier build with the same number may require an update.

| Type | Display |
|---|---|
| OFF | Default. Turns off the OLED and stops display updates and interval sampling. The startup OLED presence check still runs. |
| Profile name + polling interval + index | The top three rows show the name; the final row shows a measured output polling interval, actual S/X mode, and slot 01–16. |
| Maximum profile name + output mode | Wraps the name across 30 cells: 8 + 8 + 8 + 6. The final two cells always contain a space and S or X. Long words wrap to the next row. |
| Detail A | From top to bottom: the connected controller's 4-digit VID followed by its 4-digit PID, IN interval, OUT interval, and input payload bytes per second. |
| Detail B | Controller VID/PID and the current gamepad output in a common XInput-format view; all four rows use hexadecimal. |
| Detail C | Signed X/Y mouse output counts per second; R: theoretical input capacity at the current polling interval; S: actual received input reports per second. |

The display has four rows of eight 8×8 character cells. Only printable ASCII characters are shown; non-ASCII characters are omitted without changing the saved profile name. With the interval/index type, whitespace separates the name into rows, each word is cut at eight characters, and words after the third row are omitted. With maximum-name display, whitespace becomes a single space, and all text wraps across the available cells.

S means Switch output, including Pro. X means XInput output. These indicate the actual output format, including Auto's X/S change, rather than identifying the connected console. With no active profile, the name area stays blank and the interval/index type shows `S--` or `X--`.

For `EGRET Paddle` in slot 1, with an observed 1 ms output interval:

```text
EGRET
Paddle

 1ms S01
```

Maximum-name display for `ABCDEFGHIJKLMNOPQRSTUVWXYZ123456789`:

```text
ABCDEFGH
IJKLMNOP
QRSTUVWX
YZ1234 S
```

Detail A example (values are illustrative):

```text
0413502B
 IN: 1ms
OUT: 1ms
16384B
```

IN is the observed request interval on the controller's primary input endpoint; OUT is the observed request interval on the main output endpoint toward the console or PC. Both include requests with no new data. Separate endpoints are not added together to shorten the interval. The fourth row shows the approximate successful input payload bytes per second, across input endpoints. It is a traffic indicator rather than total USB bus traffic: headers, control transfers and duplicate packets are excluded. Rates update about once per second; IN can take two to three seconds to settle after enabling Detail A or reconnecting.

The first row has no separator: `0413502B` means VID `0413` and PID `502B`. These belong to the controller connected to USB-A, not the converter. An adapter reports its own VID/PID; the IDs do not identify controllers behind it or individual units with a serial number. No controller is shown as `--------`. Detail A uses the entire first row for these IDs; output mode and profile index remain available in the configurator and the other display types.

The interval is the average time between the connected host's USB input requests, including requests that find no new report. It is sampled about once per second; it does not measure the controller's internal scan rate or total input latency. `--ms` means waiting for a usable sample or no requests. Values below 100 ms use integer milliseconds; longer intervals use tenths of a second. After startup, settings saves, or USB reconnection, allow about one to two seconds for a fresh value.

Names and mode update when the active profile, its saved name, the selected display type, or the actual output format changes. The interval/index type also updates when its displayed average changes. Only rows with changed text are sent to the OLED; unchanged text causes no redraw. Input traffic counting is enabled only while Detail A or C is active on a detected, working OLED. OFF and maximum-name display do not sample polling intervals. A missing OLED causes no runtime display work; a failed transfer stops OLED communication until restart.

## Detail B: game-facing values

Detail B shows the output after profile assignments, shifts, turbo/toggle and direction/stick conversion, in the following fixed layout. It is intended for comparing the converter's output with a game's response or dead zone. It does not show the original controller's raw resolution, pressure readings or the game's internal processing.

| Row | Eight hexadecimal digits |
|---|---|
| 1 | VID (4) + PID (4), for the controller connected to USB-A |
| 2 | `wButtons` (4) + `bLeftTrigger` / LT (2) + `bRightTrigger` / RT (2) |
| 3 | `sThumbLX` (4) + `sThumbLY` (4) |
| 4 | `sThumbRX` (4) + `sThumbRY` (4) |

These are the fields of [Microsoft's XINPUT_GAMEPAD structure](https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_gamepad). Values are zero-padded and printed as numbers, not byte-order dumps. Each stick uses the 16-bit two's-complement representation: `0000` is center, `7FFF` is +32767, `8000` is -32768, and `FFFF` is -1. Positive X is right and positive Y is up. Only the publicly defined button bits are shown; Guide and Capture do not have public XINPUT_GAMEPAD fields.

Example: one XInput A button, full LT, released RT, left stick fully right/down, and centered right stick:

```text
0413502B
1000FF00
7FFF8000
00000000
```

The same view is used in S, X and Pro output. S/Pro values are expressed in XInput units for comparison; their actual USB packets retain their selected format. The configurator's fixed button names and the actual output mapping do not change. The current converter uses 8-bit internal stick values and digital ZL/ZR, so the 16-bit stick display does not imply native 16-bit resolution and LT/RT currently read `00` or `FF`.

The display samples the current report prepared for USB transmission up to 20 times per second and redraws only changed rows. A held value is useful for comparing game response; very short presses can occur between display samples. No input history or event queue is added. This view does not confirm that a game has accepted or acted on a report. With no controller, all rows show `--------`; when gamepad output is unavailable, including mouse-only profiles or USB suspension, only the controller ID remains and the other rows show `--------`.

## Detail C: pointing-device movement and input report capacity

Detail C updates about once per second and uses its four rows for X, Y, R and S. It does not show VID/PID. X/Y show signed decimal mouse output counts per second, after the configured sensitivity and inversion, using successfully delivered mouse reports. X is positive right and Y positive down. Values are net movement, so opposite motion cancels within the interval. They are not screen pixels or the game's internal cursor coordinates. The actual elapsed interval is used for the per-second calculation.

```text
X+001234
Y-000056
R 1000/s
S  125/s
```

**R is the theoretical input report capacity at the current polling interval:** 1000 divided by the interval in milliseconds, rounded to the nearest whole report per second. The current input-interval override is used when set; otherwise the primary endpoint's bInterval is used. Examples: 1 ms = 1000/s, 2 ms = 500/s, 8 ms = 125/s. It is not calculated from observed polls or successful reports, and is not a claim about the controller's sensor scan rate or maximum capability under a different setting.

**S is the number of reports the controller actually returned per second.** It counts nonempty successful reports received on the same primary USB-A input endpoint, even when their contents are unchanged. Requests that receive no data, errors and duplicate retries are excluded. Multiple input endpoints are not added together. S is independent of how many reports the converter sends to the console or PC. The example means that the current 1 ms input interval permits 1000 reports/s while the controller returned about 125 reports/s.

R and S describe the input side; X/Y describe the converted mouse output. On a device with separate input interfaces, the R/S pair describes the same primary endpoint as Detail A's IN interval, not every interface. Some devices stop reporting while idle; compare during sustained movement under the same input-interval setting. A zero S reading while idle is valid and does not establish the device's maximum reporting capability. Per-second rounding and the sample boundary can cause small fluctuations; S is not clamped to R to hide them.

X/Y are available for USB mouse and mouse-plus-gamepad profiles with a supported pointing input. Gamepad-only profiles, unavailable mouse output and the first interval show `X-------` / `Y-------`. R/S can still be shown independently. `R ----/s` means the configured input interval is unavailable; `S ----/s` means waiting for a usable measurement interval. No controller shows four rows of hyphens. Magnitudes above 999999 show a sign and six asterisks instead of wrapping; rates above 9999 show `R>9999/s` or `S>9999/s`.

Switching profiles, changing USB state or saving settings discards affected averaging intervals. Existing output totals are read once per update; no additional input history, motion queue or per-output-packet display operation is added. Counting and display work stop when OFF or a different non-metric display type is selected.

## Installation order

**We recommend removing R13 before installing the OLED.** Once the OLED is mounted, physically accessing and removing R13 becomes more difficult.
