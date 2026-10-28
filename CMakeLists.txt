cmake_minimum_required(VERSION 3.13)
set(PICO_BOARD waveshare_rp2350_usb_a_diag CACHE STRING "Target board")
list(APPEND PICO_BOARD_HEADER_DIRS ${CMAKE_CURRENT_LIST_DIR}/boards)
include($ENV{PICO_SDK_PATH}/external/pico_sdk_import.cmake)
project(ControllerBridge C CXX ASM)
file(STRINGS "${CMAKE_CURRENT_LIST_DIR}/firmware-version.txt" CONTROLLER_FIRMWARE_VERSION LIMIT_COUNT 1)
configure_file(cmake/firmware_version.h.in generated/firmware_version.h @ONLY)
set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 17)
pico_sdk_init()
add_subdirectory(third_party/Pico-PIO-USB)
include(cmake/pio_trace.cmake)

add_library(controller_usb STATIC
    lib/controller_usb/host.c
    lib/controller_usb/bootsel.c
    lib/controller_usb/transport_pio.c
    lib/controller_usb/input_protocol.c
    lib/controller_usb/hid_layout.c)
target_include_directories(controller_usb PUBLIC lib/controller_usb/include PRIVATE lib/controller_usb)
target_link_libraries(controller_usb PUBLIC pico_stdlib PRIVATE pico_pio_usb)
target_compile_options(controller_usb PRIVATE -Wall -Wextra -Werror)

add_executable(egret_diagnostic apps/egret_diagnostic/main.c apps/egret_diagnostic/egret_profile.c)
target_link_libraries(egret_diagnostic PRIVATE controller_usb pico_multicore pico_stdio_usb)
target_compile_options(egret_diagnostic PRIVATE -Wall -Wextra -Werror)
target_compile_definitions(egret_diagnostic PRIVATE
    PICO_STDIO_USB_PRODUCT_STRING="EGRET controller USB diagnostics"
    PICO_STDIO_USB_STDOUT_TIMEOUT_US=2000
    PICO_STDIO_USB_CONNECT_WAIT_TIMEOUT_MS=0
    PICO_STACK_SIZE=4096 PICO_CORE1_STACK_SIZE=4096)
pico_enable_stdio_usb(egret_diagnostic 1)
pico_enable_stdio_uart(egret_diagnostic 0)
pico_set_program_name(egret_diagnostic "egret_diagnostic")
pico_set_program_version(egret_diagnostic "0.1.0")
pico_add_extra_outputs(egret_diagnostic)

add_library(controller_device STATIC
    lib/controller_device/native_rp2350.c lib/controller_device/switch_wired.c lib/controller_device/switch_protocols.c
    lib/controller_device/usb_mouse.c)
target_include_directories(controller_device PUBLIC lib/controller_device/include)
target_link_libraries(controller_device PUBLIC pico_stdlib PRIVATE hardware_irq hardware_resets hardware_sync)
target_compile_options(controller_device PRIVATE -Wall -Wextra -Werror)

add_library(controller_bridge STATIC lib/controller_bridge/queue.c lib/controller_bridge/store.c
    lib/controller_bridge/profile.c lib/controller_bridge/profile_store.c lib/controller_bridge/profile_runtime.c
    lib/controller_bridge/profile_button.c lib/controller_bridge/mouse.c)
target_include_directories(controller_bridge PUBLIC lib/controller_bridge/include)
target_link_libraries(controller_bridge PUBLIC controller_usb controller_device PRIVATE hardware_flash)
target_compile_options(controller_bridge PRIVATE -Wall -Wextra -Werror)
option(BRIDGE_MOUSE_FIXED_PROBE "Diagnostic: raw ball threshold 2, fixed vectors, no paddle" OFF)
set(BRIDGE_MOUSE_FIXED_AMPLITUDE "10000" CACHE STRING "Diagnostic fixed mouse vector amplitude (1..32767)")
set(BRIDGE_MOUSE_PACKET_BYTES "8" CACHE STRING "Mouse interrupt endpoint maximum packet size (8 or 16)")
if(NOT BRIDGE_MOUSE_PACKET_BYTES MATCHES "^(8|16)$")
    message(FATAL_ERROR "BRIDGE_MOUSE_PACKET_BYTES must be 8 or 16")
endif()
if(BRIDGE_MOUSE_FIXED_PROBE)
    if(NOT BRIDGE_MOUSE_FIXED_AMPLITUDE MATCHES "^[1-9][0-9]*$" OR
       BRIDGE_MOUSE_FIXED_AMPLITUDE GREATER 32767)
        message(FATAL_ERROR "BRIDGE_MOUSE_FIXED_AMPLITUDE must be an integer from 1 to 32767")
    endif()
    target_compile_definitions(controller_bridge PRIVATE BRIDGE_MOUSE_FIXED_PROBE=1
        BRIDGE_MOUSE_FIXED_AMPLITUDE=${BRIDGE_MOUSE_FIXED_AMPLITUDE})
endif()

add_executable(ControllerBridge apps/egret_switch/main.c apps/egret_switch/egret_switch_profile.c
    apps/egret_switch/status_led.c)
target_include_directories(ControllerBridge PRIVATE "${CMAKE_CURRENT_BINARY_DIR}/generated")
pico_generate_pio_header(ControllerBridge ${CMAKE_CURRENT_LIST_DIR}/apps/egret_switch/status_led.pio)
option(BRIDGE_USB_TRACE "Build explicit RAM-only USB control trace" OFF)
if(BRIDGE_USB_TRACE)
    target_sources(ControllerBridge PRIVATE apps/egret_switch/usb_control_trace.c)
    target_compile_definitions(ControllerBridge PRIVATE BRIDGE_USB_TRACE=1)
endif()
target_link_libraries(ControllerBridge PRIVATE controller_bridge pico_multicore hardware_watchdog hardware_pio)
target_compile_options(ControllerBridge PRIVATE -Wall -Wextra -Werror)
target_compile_definitions(ControllerBridge PRIVATE PICO_STACK_SIZE=4096 PICO_CORE1_STACK_SIZE=4096)
target_compile_definitions(ControllerBridge PRIVATE BRIDGE_MOUSE_PACKET_BYTES=${BRIDGE_MOUSE_PACKET_BYTES})
pico_enable_stdio_usb(ControllerBridge 0)
pico_enable_stdio_uart(ControllerBridge 0)
pico_set_program_name(ControllerBridge "ControllerBridge")
if(BRIDGE_MOUSE_FIXED_PROBE)
    target_compile_definitions(ControllerBridge PRIVATE BRIDGE_MOUSE_FIXED_PROBE=1
        BRIDGE_MOUSE_FIXED_AMPLITUDE=${BRIDGE_MOUSE_FIXED_AMPLITUDE})
    set(mouse_probe_version "0.13.3-probe${BRIDGE_MOUSE_FIXED_AMPLITUDE}")
    if(NOT BRIDGE_MOUSE_PACKET_BYTES STREQUAL "8")
        string(APPEND mouse_probe_version "-ep${BRIDGE_MOUSE_PACKET_BYTES}")
    endif()
    pico_set_program_version(ControllerBridge "${mouse_probe_version}")
else()
    pico_set_program_version(ControllerBridge "${CONTROLLER_FIRMWARE_VERSION}")
endif()
pico_add_extra_outputs(ControllerBridge)

add_executable(egret_poll_probe apps/egret_poll_probe/main.c apps/egret_switch/egret_switch_profile.c)
target_include_directories(egret_poll_probe PRIVATE apps/egret_switch)
target_link_libraries(egret_poll_probe PRIVATE controller_bridge pico_multicore hardware_exception hardware_watchdog)
target_compile_options(egret_poll_probe PRIVATE -Wall -Wextra -Werror)
target_compile_definitions(egret_poll_probe PRIVATE PICO_STACK_SIZE=4096 PICO_CORE1_STACK_SIZE=4096)
pico_enable_stdio_usb(egret_poll_probe 0)
pico_enable_stdio_uart(egret_poll_probe 0)
pico_set_program_name(egret_poll_probe "egret_poll_probe")
pico_set_program_version(egret_poll_probe "0.3.0-probe")
pico_add_extra_outputs(egret_poll_probe)
