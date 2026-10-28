// pico_cmake_set PICO_PLATFORM=rp2350
// pico_cmake_set_default PICO_FLASH_SIZE_BYTES = (2 * 1024 * 1024)
#ifndef WAVESHARE_RP2350_USB_A_DIAG_H
#define WAVESHARE_RP2350_USB_A_DIAG_H
#define PICO_RP2350A 1
#define PICO_RP2350_A2_SUPPORTED 1
#define PICO_FLASH_SIZE_BYTES (2 * 1024 * 1024)
#define PICO_BOOT_STAGE2_CHOOSE_W25Q080 1
#define PICO_FLASH_SPI_CLKDIV 2
#define PICO_DEFAULT_UART 0
#define PICO_DEFAULT_UART_TX_PIN 0
#define PICO_DEFAULT_UART_RX_PIN 1
#define CONTROLLER_USB_DP_PIN 12
#define CONTROLLER_USB_DM_PIN 13
#define CONTROLLER_STATUS_LED_PIN 16
// The tested board needs RGB on the wire (GRB swapped red/green physically).
// Set to 1 only when porting to a board whose LED is confirmed to use GRB.
#define CONTROLLER_STATUS_LED_GRB 0
#endif
