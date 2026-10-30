# GPIO assignment and board porting

The standard target is **Waveshare RP2350-USB-A**, using RP2350A and 2 MiB Flash. This describes firmware 0.19.0. Numbers below are RP2350 GPIO numbers, not connector positions or physical IC pins.

Consult the [manufacturer schematic](https://files.waveshare.com/wiki/RP2350-USB-A/RP2350-USB-A.pdf) and [board wiki](https://www.waveshare.com/wiki/RP2350-USB-A) for wiring. Software restrictions below come from this repository.

## Standard assignment

| Function | Connection | Implementation |
|---|---|---|
| USB-A input host D+ | GPIO12 | `uc_host_options.dp_pin` in each application's `host_core()` |
| USB-A input host D- | GPIO13 | Derived as D+ + 1 by default PIO-USB configuration |
| WS2812B status LED DIN | GPIO16 | `CONTROLLER_STATUS_LED_PIN` and `status_led.c` |
| USB-C output D+ / D- | Dedicated `USB_DP` / `USB_DM` | Native USB PHY; not remappable to ordinary GPIO in this implementation |
| BOOT button | Flash `QSPI_SS` / CS | Dedicated QSPI register access in `bootsel.c` |
| RESET button | `RUN` | Hardware reset; no application GPIO assignment |
| UART0 defaults | TX GPIO0 / RX GPIO1 | SDK defaults only; all three applications disable UART stdio |
| USB-A VBUS power | Board power circuit | No GPIO-controlled VBUS switch is implemented |

The ordinary GPIOs used by the standard bridge are **12, 13, and 16**. Dedicated Flash, native USB, and RUN connections are separate.

## Files to change

| File | Responsibility |
|---|---|
| [`boards/waveshare_rp2350_usb_a_diag.h`](../boards/waveshare_rp2350_usb_a_diag.h) | Board constants, RP2350A selection, Flash size and boot settings, USB/LED declarations |
| [`CMakeLists.txt`](../CMakeLists.txt) | `PICO_BOARD`, default `waveshare_rp2350_usb_a_diag` |
| [`apps/egret_switch/main.c`](../apps/egret_switch/main.c) | Main bridge: `.dp_pin=12` in `host_core()` |
| [`apps/egret_diagnostic/main.c`](../apps/egret_diagnostic/main.c) | Input diagnostic: `.dp_pin=12`; startup log also hardcodes `dp=12 dm=13` |
| [`apps/egret_poll_probe/main.c`](../apps/egret_poll_probe/main.c) | Polling probe: `.dp_pin=12` |
| [`lib/controller_usb/transport_pio.c`](../lib/controller_usb/transport_pio.c) | `ut_init()`: PIO selection, pin order, Full Speed startup |
| [`apps/egret_switch/status_led.c`](../apps/egret_switch/status_led.c), [`status_led.pio`](../apps/egret_switch/status_led.pio) | LED data format and timing |
| [`lib/controller_usb/bootsel.c`](../lib/controller_usb/bootsel.c) | BOOT sampling and synchronization around Flash access |
| [`lib/controller_device/native_rp2350.c`](../lib/controller_device/native_rp2350.c) | Native USB device and PHY initialization |

**The board macros `CONTROLLER_USB_DP_PIN` and `CONTROLLER_USB_DM_PIN` are currently not read by USB host initialization. Changing them alone does not move the input USB port.** The LED macro is used directly.

## Port to another RP2350A GPIO layout

1. Check input USB D+/D-, LED, BOOT, and VBUS power on the target schematic. Assign native USB to the PC/console output and PIO USB to the input controller.
2. Create a distinct board header and header guard under `boards/`. Match Flash size, boot configuration, clock, and GPIO wiring to the actual board.
3. Select the header filename without its extension as `PICO_BOARD`. Use a fresh build directory or explicitly update the CMake cache: changing a default does not override a cached board choice. The current build script has no `-Board` argument.
4. Change `.dp_pin=12` in each application you will build. Using `.dp_pin=CONTROLLER_USB_DP_PIN` centralizes subsequent changes in the board header. Update the diagnostic's printed pin numbers too.
5. Set `CONTROLLER_STATUS_LED_PIN` to the WS2812B DIN pin. Set `CONTROLLER_STATUS_LED_GRB` to 0 for RGB wire order (the tested board), or 1 for an LED confirmed to require GRB. Verify primary colors on the actual board; the part name alone is not sufficient. A board without this LED needs LED initialization disabled or another implementation; there is no "not connected" sentinel. A simple LED or another RGB protocol needs more than a pin change.
6. Build `ControllerBridge` and verify USB-C enumeration, USB-A input, LED, the selected switching method, and settings persistence on the target. Alternate boards have not been tested by this project.

For example, D+=GPIO4, D-=GPIO5, WS2812B=GPIO16 requires D+ set to 4 in the application, matching 4/5 declarations, and LED pin 16. This illustrates the edits; it is not a verified alternate board.

## Pin ordering and resources

`ut_init()` starts from `PIO_USB_DEFAULT_CONFIG` and assigns `cfg.pin_dp`. The default `PIO_USB_PINOUT_DPDM` means **D- = D+ + 1**. The DM board macro cannot override this relationship.

For reverse-adjacent wiring, **D- = D+ - 1**, also set `cfg.pinout = PIO_USB_PINOUT_DMDP;` in `ut_init()`. Nonadjacent D+/D- pins are unsupported by this configuration. See [`pio_usb_configuration.h`](../third_party/Pico-PIO-USB/src/pio_usb_configuration.h) and [`pio_usb.c`](../third_party/Pico-PIO-USB/src/pio_usb.c).

| Resource | Standard use |
|---|---|
| Input USB | PIO1, SM0 TX, SM1 RX, SM2 EOP, TX DMA channel 0 |
| Status LED | One available PIO0 state machine, 800 kHz WS2812, RGB order on the tested board |
| Output USB | Native USB controller and PHY |
| System clock | 120 MHz, set by each application's `main()` |

Avoid resource conflicts when adding peripherals. RP2350B GPIO32 and above, or an RP2040 port, require package, GPIO-base, and register checks beyond this procedure.

## BOOT, power, and storage differences

BOOT is not a normal GPIO input. `uc_bootsel_poll()` briefly releases QSPI CS and samples it using SRAM code and coordination between the two cores. A button on ordinary GPIO needs a different input implementation. Controller-shortcut switching disables BOOT monitoring.

USB-A 5 V power comes from the board circuit. A GPIO-controlled power switch needs explicit initialization. Native USB VBUS detection is overridden; there is no VBUS-detect GPIO setting. Follow the target board's electrical design.

The standard board retains R13. The firmware attempts Full Speed initialization and checks EP0 communication instead of relying solely on the electrical attachment level. This does not imply Low Speed support or require the manufacturer's generic host modification procedure.

The final 32 KiB of Flash hold settings, measurements, and profiles. [`store.c`](../lib/controller_bridge/store.c) calculates addresses from `PICO_FLASH_SIZE_BYTES`. Changing capacity moves those addresses; use a GUI backup to transfer profiles to the new board. See the current [memory budget](../README.md#memory-budget).
