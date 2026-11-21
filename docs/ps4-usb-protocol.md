# PS4-compatible USB gamepad protocol

This is ControllerBridge's interoperability contract for wired PS4-compatible controllers, especially arcade sticks that can be reused on Switch. It describes an existing wire format; it does not define a new controller protocol or claim Sony certification. Device identity, vendor, model name and the presence of motion sensors are not prerequisites for the basic format.

The packet descriptions below are based on USB HID rules, device-supplied report descriptors and USB traffic. The implementation in `lib/ps4_usb_protocol` is project code under the repository's BSD 2-Clause license. This document is not a copy of another project's source or documentation. Protocol compatibility does not imply that every product sold as a PS4 controller implements every extension.

## Scope

- USB Full Speed, one attached controller, HID interrupt input.
- Buttons, hat, stick axes and analog trigger values advertised by the controller. Arcade devices may report unused axes at their neutral values.
- Optional Feature-report session exchange, only when the HID descriptor declares the corresponding reports.
- PS4 console output is not implemented. The authentication limits below explain why saving a completed USB exchange is insufficient for standalone output with a different input controller.
- Bluetooth, audio, touch coordinates, motion sensors and controller feedback are outside this contract. A touchpad's physical click may be exposed as a button without touch-coordinate support.

## Interface discovery

Read the actual configuration and HID report descriptors. Use the interface number and endpoints from those descriptors; neither interface 0 nor interface 3 is assumed. An interface is a candidate for the common PS4 USB input format when report ID 1 is 64 bytes including its ID and its control prefix has the following layout. The output report 5 / 32-byte declaration or the 48-byte Feature report 3 provides an additional format marker.

The parser continues to the end of a valid descriptor even after the converter's mapping capacity is reached. ControllerBridge exposes at most 64 scalar input fields per interface and 256 in total. Skipped fields still occupy their declared bits; their bytes are not removed from reports or descriptor identity.

This identification is a structural match, not a claim that a device is authentic or licensed. A different layout can still be used through ordinary HID mapping, but does not receive this protocol's automatic direction correction or session exchange.

## Input report 1

Byte offsets include the report ID. Multibyte values are little endian unless stated otherwise.

| Bytes / bits | Meaning |
|---|---|
| 0 | Report ID `01` |
| 1, 2 | Left X, left Y; unsigned 8-bit values |
| 3, 4 | Right X, right Y; unsigned 8-bit values |
| 5 bits 0–3 | Hat: 0 up, 1 up-right, 2 right, 3 down-right, 4 down, 5 down-left, 6 left, 7 up-left, 8 neutral |
| 5 bits 4–7 | Four face buttons in square, cross, circle, triangle order |
| 6 bits 0–7 | L1, R1, L2 button, R2 button, Share, Options, L3, R3 |
| 7 bits 0–1 | PS/Home, touchpad click |
| 7 bits 2–7 | Report counter; not an assignable control |
| 8, 9 | Analog L2, R2; unsigned 8-bit values |
| 10–63 | Device-specific status, sensors, touch or padding; not inferred to be usable controls |

The descriptor's standard usages for the first four axes are X, Y, Z and Rz. The following two trigger usages are Rx and Ry. Axis ranges are 0–255; the stick center is learned from the device rather than fixed to one integer. The hat uses logical range 0–7 with a neutral/null value.

The wire axes are right/down positive. ControllerBridge's user interface is right/up positive: only left Y and right Y are reflected as `255 - raw`. The report bytes and descriptor identity remain unchanged. New assignments carry the normalized-source flag. Existing saved assignments retain their previous physical output, including an intentional reversal; rerun and save the stick wizard to replace an earlier assignment.

Vendor-page counters and status values remain available as raw data but are not candidates for automatic button or axis learning. Sensor or touch support must be implemented explicitly rather than inferred from vendor bytes.

## Feature transport

All requests use the discovered HID interface as `wIndex`. The high byte of `wValue` is 3 (Feature), and its low byte is the report ID. Report lengths below include the ID byte.

| Direction | bmRequestType | bRequest | Operation |
|---|---:|---:|---|
| Device to host | `A1` | `01` | GET_REPORT |
| Host to device | `21` | `09` | SET_REPORT |

The interrupt IN endpoint remains serviced while an optional exchange is in progress. Class requests must not become the normal input-polling mechanism. Failure of an optional exchange does not invalidate otherwise valid input reports.

## Optional session reports

The session extension requires matching Feature declarations for IDs F0, F1, F2 and F3, with sizes 64, 64, 16 and 8 bytes, together with definition report 3. A controller exposing only the common input format does not need this exchange.

| Report | Transfer | Length | Contract |
|---|---|---:|---|
| `03` | GET | 48 | Definition bytes; byte 0 is `03`, byte 2 is the format marker `27`. Other bytes are device capabilities, not promises that ControllerBridge implements them. |
| `F3` | GET | 8 | Open/reset the exchange. Byte 0 is `F3`; bytes 2 and 3 declare 56-byte payload units. Other bytes are retained as reserved. |
| `F0` | SET | 64 | One challenge page: ID, transaction token, page number, zero, 56-byte payload area, CRC-32. |
| `F2` | GET | 16 | Read readiness: ID, transaction token, state. State `10` means pending, `00` means response available; other states terminate the attempt. Reserved bytes may be zero and are not used to claim authenticity. |
| `F1` | GET | 64 | One response page: ID, transaction token, page number, reserved byte, 56-byte opaque payload, CRC-32. |

A challenge contains 256 bytes. F0 pages 0–3 each contain 56 bytes, and page 4 contains the remaining 32 bytes followed by 24 zero bytes. The token is unchanged for the entire transaction. Pages are sent in ascending order; an identical repeat can be accepted without advancing the transaction.

After all five challenge pages, poll F2 at intervals of at least 10 ms. If ready, retrieve F1 pages 0–18 in order. Check length, ID, token, page number and each response-page checksum. A complete response has 19 pages, containing 1,064 opaque payload bytes. Their cryptographic interpretation belongs to the console and the responding controller. Receiving pages is not proof that a console will accept them.

For F0 and F1, the final four bytes contain CRC-32/ISO-HDLC over bytes 0–59, stored little endian: reflected polynomial `EDB88320`, initial value `FFFFFFFF`, final XOR `FFFFFFFF`. F2 reserved trailing bytes are not treated as a mandatory checksum in this contract.

ControllerBridge gives each complete exchange a bounded three-second deadline. This deadline is a host policy, not a claimed device requirement. A failure or timeout ends that attempt while ordinary input remains available. No repeated challenge is sent during normal input polling. A new challenge is a separate transaction. The input initializer validates response pages as they arrive and discards them; it does not retain authentication data.

For standalone input initialization, a public nonsecret challenge may be used to select the interface's operating mode. Local initialization cannot substitute for answering a new console challenge.

## Multiple-mode devices

Some controllers expose different USB personalities during startup. Re-enumeration restarts discovery and discards all interface-specific session state. An Xbox-compatible interface with no interrupt OUT endpoint is read as an input-only interface; the converter does not attempt the wired Xbox OUT-start sequence or force a different personality through its identification request. Such an interface is not identified as PS4 merely because it lacks an OUT endpoint.

## PS4 console output and cached authentication

The current library enables compatible input devices; it does not add a PS4 output mode. Auto continues to select only the existing output modes.

A console issues a fresh challenge. A saved response, public certificate or device identifier cannot generate a signature for that new challenge. The USB exchanges described above do not export the controller's private signing key. Caching the response therefore cannot enable a different controller to operate independently on PS4. This limitation follows from the challenge/signature design, not the input report format.

Connecting a compatible authentication controller once does not create a reusable authorization for later challenges. This also applies when only the input controller is unplugged while the converter remains attached to the console. The [GP2040-CE console-compatibility FAQ](https://gp2040-ce.info/faq/faq-console-compatibility/#can-i-remove-the-ps4ps5-authentication-device-once-i-have-plugged-in-my-controller) describes repeated authorization requests, approximately every 30 seconds, and a timeout after approximately eight minutes when responses fail. These timings are a published implementation reference, not a timing guarantee or a ControllerBridge console measurement. Consequently, an initial-registration-and-swap workflow is not supported by response caching alone.

Standalone output would require suitable private signing credentials and a signing implementation. Relaying each fresh challenge to an attached authentication device is another architecture, but the standard board has one USB-A input port and no supported hub path. That architecture does not meet the intended use of connecting only an unrelated input controller. No private keys, key extraction, response replay or claimed console acceptance are included in this library.

## References and terminology

- [USB-IF HID specification and report-descriptor resources](https://www.usb.org/hid), particularly HID 1.11 sections 6.2.2 and 7.2.1–7.2.2, define the generic descriptor and class-request transport.
- [Linux PlayStation HID driver](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c) independently documents the 64-byte USB report 1, 32-byte output report 5 and common control-field order. It is a reference for wire-format facts; its implementation is not incorporated into ControllerBridge.

The conventional PS4 names above describe wire positions. ControllerBridge continues to use its fixed Switch-style button names throughout the configurator; selecting another output mode does not rename or reorder saved button IDs.
