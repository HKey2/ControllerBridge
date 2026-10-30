# Status LED

The standard Waveshare RP2350-USB-A board has one WS2812B on GPIO16. The default maximum RGB component is 1/255. Normal ready operation uses the active profile color, initially steady dim blue (R=0, G=0, B=1); individual button presses do not change the color.

| Display | Meaning |
|---|---|
| Blue, 1 second on / 1 second off | Waiting for an input device, or for the output host when a registered input is ready |
| Cyan, 250 ms on / 250 ms off | Detected device is initializing |
| Steady yellow | Input received, but no matching profile is registered |
| Steady profile color (default dim blue) | Input received, profile applied, output ready |
| Red, 250 ms on / 250 ms off | Detected-device initialization failure or repeated input errors |
| Red for about 1 second | Flash save failed |
| Purple for about 1 second | Profile or settings save succeeded |
| Short purple flashes, 1-16 | Selected profile's ordinal among matching profiles |

The save indication takes priority for its one-second duration. Brief initialization states may finish between display updates. Repeated failures for the same device do not restart the red blink phase. Green remains available for diagnostics but is not a normal operating state.

The numbered sequence starts with 250 ms off, then repeats 150 ms on and 200 ms off, with 350 ms off after its last pulse. BOOT monitoring pauses during that sequence. A valid controller shortcut may select another profile and restart the indication for the new number.

## Per-profile RGB

**LED RGB settings** below **Delete profile** accepts R, G, and B independently from 0 through 255. **Default dim blue** keeps the original brightness. **Custom RGB values** go directly to the WS2812B channel bytes, without the palette brightness limiter or RGB565 quantization. Custom black is a valid off setting. The preview is a screen swatch, not an optical brightness measurement.

Editing affects a draft until **Save slot to device**. The active saved profile supplies the ready color. Duplicate, reorder, export/import, and backup/restore carry the color with that profile. Returning to the default clears the custom flag and color bytes. Waiting, initialization, unregistered-input, error, save, and profile-number indications remain at the original low brightness. The profile color returns after temporary indications finish.

## RGB code entry and preview

A text box prefixed by a separate `#` label sits below the three channel spinners. It accepts exactly six hexadecimal digits, supports native paste and either letter case, and automatically removes one optional leading `#`. Code edits update the R/G/B controls; spinner edits update the code. For example, `000004` or `#000004` means R=0, G=0, B=4. Incomplete, invalid and overlong strings disable OK and leave the last valid preview unchanged. They cannot be committed or silently truncated. Selecting the default color shows `000001` and disables custom entry.

The single preview uses a fixed internal gamma of **4.0**, selected after the user's comparison with the physical LED. Each input byte `v` maps to `round(255 * (v / 255)^(1 / 4.0))` for screen display. There is no adjustable curve, explanatory curve text or uncorrected swatch in the dialog. The previous `%LOCALAPPDATA%/ControllerBridge/led-preview.json` preference is no longer read or written; an existing file is left unused.

| LED channel value | Screen channel value |
|---:|---:|
| 0 | 0 |
| 1 | 64 |
| 4 | 90 |
| 8 | 107 |
| 32 | 152 |
| 128 | 215 |
| 255 | 255 |

This is an empirical relative appearance approximation, not optical calibration. Channel efficiencies, spectrum, absolute luminance, point-source glare, monitor settings and ambient lighting are not modeled. The RGB code, stored profile bytes and firmware output remain unchanged by the display curve. The display curve allocates no board memory. Current 0.19.0 allocation figures, including guarded registration, are in [verification](verification.md#current-normal-build-memory).

## Wire order and physical observation

The user observed that red and green were reversed with firmware 0.18.0. The GUI codec and stored profile bytes were already R/G/B; the driver then rearranged them into G/R/B, and the PIO transmitted that order MSB first. Version 0.18.1 configures this board for R/G/B on the wire. Custom, status, and diagnostic colors now share one final board-order conversion. The default blue and black values are unaffected, and stored RGB values need no migration.

The [Waveshare RGB sample](https://files.waveshare.com/wiki/RP2350-USB-A/RP2350-USB-A-RGB.zip) also assembles GRB bytes. That reference does not establish the color order of the tested physical board; the RGB setting is based on the user's actual observation. The schematic labels the part WS2812B-0807. Other boards/revisions need their own primary-color check. The board header declares `CONTROLLER_STATUS_LED_GRB=0`; a confirmed GRB variant can use 1 without changing profile data or GUI values.

The corrected build has been checked in software; its physical emission still requires confirmation on the user's board. The earlier software tests assumed GRB and therefore could not detect the incorrect hardware assumption.

## Implementation

Core 0 updates state and deadlines every 10 ms. Output is sent only when the desired color/diagnostic state changes. Comparing the actual 24-bit value also detects color changes between two custom profiles, including on/off changes. One 24-bit pixel takes 30 microseconds, and writes are at least 1 ms apart to allow the latch interval. The LED uses one PIO0 state machine and four PIO instructions, leaving the input USB engine on PIO1. It does not add a CPU busy wait, DMA channel, or dedicated timer interrupt. If PIO resources cannot be acquired, only LED output is disabled.

The brightness setting is an 8-bit PWM component value, not a measured ratio of perceived brightness. Two-component colors retain both components at the lowest level.

## Color diagnostic

Close the GUI and connect USB-C to the PC before running:

```powershell
python tools/led-test.py --level 1 --out captures/led-colors.json
```

The tool waits three seconds, then cycles blue, cyan, yellow, green, red, and purple, approximately one second on and one second off each, before restoring automatic state display. It does not save Flash or alter profiles. `--level` accepts 1-32 and `--repeat` accepts 1-3; diagnostic brightness does not change the normal default.

Feature command 31 accepts operation 0 to read, 1 to show a palette color for one second then remain in diagnostic off mode, 2 to enter diagnostic off mode, and 3 to restore automatic display. Operations 1/2 require operation 3 or a reboot to leave diagnostic override. Display changes are rejected during measurement.

Diagnostic color IDs 0-5 retain the palette meaning, 6 means raw profile RGB, and 255 means the explicit off state. The level byte is zero for raw RGB; response bytes 48, 49, and 50 report the actual last transmitted R, G, and B bytes for every color type.

The `LED1` response includes state/shown colors, brightness, override state, deadlines, write count, last on/off write times, SM and GPIO, blink timing, and profile-number progress. These are software/PIO timestamps, not optical measurements. See [`status_led.c`](../apps/egret_switch/status_led.c) and the [GPIO guide](gpio-assignment.md).
