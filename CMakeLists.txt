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
include(cmake/pio_input_timestamp.cmake)

add_library(controller_usb STATIC
    lib/controller_usb/feedback.c
    lib/controller_usb/feedback_calibration.c
    lib/controller_usb/switch_rumble.c
    lib/controller_usb/host.c
    lib/controller_usb/bootsel.c
    lib/controller_usb/transport_pio.c
    lib/controller_usb/input_protocol.c
    lib/controller_usb/switch2.c
    lib/controller_usb/xbox_one.c
    lib/controller_usb/hid_layout.c)
target_include_directories(controller_usb PUBLIC lib/controller_usb/include PRIVATE lib/controller_usb)
target_link_libraries(controller_usb PUBLIC pico_stdlib PRIVATE pico_pio_usb)
target_compile_options(controller_usb PRIVATE -Wall -Wextra -Werror)

add_library(controller_device STATIC
    lib/controller_device/native_rp2350.c lib/controller_device/switch_wired.c lib/controller_device/switch_protocols.c
    lib/controller_device/usb_mouse.c lib/controller_device/xinput_wired.c lib/controller_device/xinput_portal.c)
target_include_directories(controller_device PUBLIC lib/controller_device/include)
target_link_libraries(controller_device PUBLIC pico_stdlib PRIVATE hardware_irq hardware_resets hardware_sync)
target_compile_options(controller_device PRIVATE -Wall -Wextra -Werror)

add_library(controller_bridge STATIC lib/controller_bridge/queue.c lib/controller_bridge/store.c
    lib/controller_bridge/gyro.c lib/controller_bridge/profile.c lib/controller_bridge/profile_store.c lib/controller_bridge/profile_runtime.c
    lib/controller_bridge/profile_button.c lib/controller_bridge/mouse.c)
target_include_directories(controller_bridge PUBLIC lib/controller_bridge/include)
target_link_libraries(controller_bridge PUBLIC controller_usb controller_device PRIVATE hardware_flash)
target_compile_options(controller_bridge PRIVATE -Wall -Wextra -Werror)

add_executable(ControllerBridge apps/egret_switch/main.c apps/egret_switch/egret_switch_profile.c
    apps/egret_switch/status_led.c apps/egret_switch/switch_output.c apps/egret_switch/xinput_feedback.c)
target_include_directories(ControllerBridge PRIVATE "${CMAKE_CURRENT_BINARY_DIR}/generated")
pico_generate_pio_header(ControllerBridge ${CMAKE_CURRENT_LIST_DIR}/apps/egret_switch/status_led.pio)
target_link_libraries(ControllerBridge PRIVATE controller_bridge pico_multicore pico_unique_id hardware_watchdog hardware_pio)
target_compile_options(ControllerBridge PRIVATE -Wall -Wextra -Werror)
target_compile_definitions(ControllerBridge PRIVATE PICO_STACK_SIZE=4096 PICO_CORE1_STACK_SIZE=4096)
pico_enable_stdio_usb(ControllerBridge 0)
pico_enable_stdio_uart(ControllerBridge 0)
pico_set_program_name(ControllerBridge "ControllerBridge")
pico_set_program_version(ControllerBridge "${CONTROLLER_FIRMWARE_VERSION}")
pico_add_extra_outputs(ControllerBridge)
