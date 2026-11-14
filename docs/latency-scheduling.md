# Latency and polling policy

Current normal firmware: 0.28.1. The input and X output scheduling changes described below were introduced in 0.24.1.

The target is the interval from a physical switch changing state to the host's input library accepting that state. It does not include the game's simulation or display. ControllerBridge can timestamp received USB reports and its own output completion, but cannot observe the controller's contacts or the host library. Consequently, the total-latency conclusions below are conditional deductions, not new end-to-end measurements.

## Changes in 0.24.1

The native interrupt-IN interval used by Trackball Hold and Paddle Virtual Rolling is decoded once when an input device finishes configuration. Runtime input handling indexes a 16-entry endpoint cache and checks the owning interface. A profile's explicit interval still takes precedence. Reconnection clears the cache, alternate settings do not overwrite the active default setting, and requesting a diagnostic descriptor replay does not erase a live cache. This replaces a descriptor-length-dependent walk with constant-time lookup. No microsecond saving is claimed without a new timing measurement.

The existing per-profile **Input interval (ms)** now also controls the **X gamepad output**, including Auto when it has selected X:

| Profile value | USB-A input requests | X gamepad IN interval requested from host | X software producer |
|---|---|---|---|
| 0 | Controller descriptor's native interval | 4 ms | Ordinary profiles: reference 11 ms cadence. Gyro/rumble profiles: immediate scheduling. |
| 1 | 1 ms | 1 ms | Arm changes immediately when the endpoint/ordered queue permits |
| 2–255 | Specified number of milliseconds | Same specified interval | Same immediate scheduling; no additional 11 ms gate |

Only gamepad IN81's `bInterval` changes in the X descriptor. The identity, accessory interfaces and OUT endpoint retain their existing behavior. Version 0.28.1 adds input and rumble capability reporting during connection without changing input scheduling. At value 0, the existing 4 ms reference descriptor is retained. Ordinary profiles also retain the 11 ms producer cadence. From 0.28.0, gyro/rumble profiles bypass that producer wait so continuous input changes do not accumulate behind it. Switching back to an ordinary profile restores the reference cadence. At nonzero values the polling/cadence intentionally differ from the reference; POLYMEGA acceptance has not been established.

S gamepad and USB mouse output retain their separate legacy output interval, normally 1 ms. The mouse in an X combined device remains independent. No new setting or saved-data field is added. Saving a profile is verified before applying the output change; changing the active X interval reconnects USB, using the existing delayed reconnect path. This also applies to an on-device profile switch. Auto's already chosen result is retained. Editing an inactive profile with the same active timing does not reconnect output.

An interval is a scheduling request. The host may choose a different effective period; a controller may NAK requests without new data. Neither a descriptor value nor a count of repeated packets establishes the contact scan rate.

## Switch 2 Pro profiles

For wired Switch 2 Pro, **Input interval = 1** requests input every millisecond; **0** uses its 4 ms descriptor default. This setting is independent of the profile's **Rumble** option. A precision-control profile can keep rumble off and use 1 ms input. A separate profile may enable rumble with either interval. The saved interval and rumble preference follow profile selection and reconnection.

When rumble is off, Pro2 uses the ordinary input path without vibration conversion or periodic motor transfers. When rumble is enabled, both packet preparation and motor transmission wait for available time before the next input poll. Pro output still uses the first-generation Pro protocol toward the console; the console determines its own USB polling period. See [Pro output and rumble](configurator-guide.md#pro-output-and-rumble).

## Reference measurements and their boundaries

| Primary source | Published result | What it establishes here |
|---|---|---|
| GP2040-CE v0.7.12 | At 1 ms polling: XInput mean 0.76 ms, range 0.45–1.28; Switch mean 0.72 ms, range 0.41–1.22 | A useful target for direct GPIO-to-USB controller firmware, not a two-USB-bus bridge or every console's library |
| MiSTer Addons | Fast measured devices around 0.7 ms; 0.5 ms ideal mean polling wait at 1 ms | Supports the distinction between sampling wait and computation time |
| Brook Wingman FGC Tournament firmware, 2023-07-20 note | Manufacturer's 1.3 ms response-time claim | A converter reference point; insufficient methodology to establish a matched comparison or a theoretical minimum |

GP2040-CE's [published results](https://gp2040-ce.info/#performance) use an electrically stimulated input and a USB test host. Its [method description](https://github.com/OpenStickCommunity/Site/blob/main/latency_testing/README.md) connects a tester GPIO directly to the controller board and aggregates repeated runs. Its [FAQ](https://gp2040-ce.info/faq/faq-general/) explicitly distinguishes the requested 1 ms period from platforms that override it. A mode named Switch or PS5 in this test does not mean the final console library was the measurement endpoint.

[MiSTer Addons' method](https://misteraddons.com/pages/latency) measures electrically commanded button presses through to detection by its custom FPGA test core. The 0.5 ms value is an ideal **mean waiting time for a uniformly phased event at one 1 ms sampling stage**, not a minimum for every individual press or a universal total-latency floor.

The [Brook firmware note](https://www.brookaccessory.com/dl.php) does not specify enough about the 1.3 ms measurement's endpoints, controller, distribution or host. No independently documented Wingman dataset with matching stimulus and endpoints was established in this investigation. Comparing that figure directly against ControllerBridge's receive-to-ready time would mix different intervals. The Wingman result therefore cannot justify declaring further work unnecessary.

The [inputlag.science methodology](https://inputlag.science/controller/methodology) also separates descriptor/native polling from forced 1 ms polling. This is useful when considering whether a bridge can obtain fresher input than a direct connection.

## Model for total latency

```text
physical switch
  -> controller scan / debounce / report generation
  -> wait for bridge input poll + input USB transaction
  -> bridge processing + optional producer wait
  -> wait for host output poll + output USB transaction
  -> host driver / input library acceptance
```

Let `Pin` and `Pout` be the actual input and output polling periods, and `C` the receive-to-ready processing time. If events are uniformly phased and the two polling waits are independent, the bus/bridge contribution is approximately:

`Pin / 2 + C + Pout / 2 + wire-transfer time`

Controller scanning, internal report generation and host-library work are additional terms. If their clocks are coupled, their waits must not be blindly added as independent half-periods. X compatibility mode additionally has its 11 ms producer gate; explicit nonzero settings remove that gate. The output host still controls when the prepared packet is fetched.

For illustration, input/output periods of 1/1 ms and `C = 0.1 ms` give about **1.1 ms**, before the separately excluded terms. At 10/1 ms the same model gives about **5.6 ms**. These are explanatory model subtotals, not predictions of a particular EGRET controller's total latency.

The previous normal 0.24.0 measurements, recorded in [verification](verification.md), found S receive-to-ready means of **0.072 ms for the EGRET Arcade Stick** and **0.145 ms for pointer movement**. These already exclude the input polling wait, contact scanning and host-library work. Observed maxima of 0.111/0.274 ms are finite-sample maxima, not worst-case guarantees. These figures were measured before 0.24.1 and are not evidence of the cache's speed gain.

## Can the bridge be faster than a direct connection?

Yes, under conditions. If direct attachment is serviced slowly, while the bridge polls the controller more frequently and the controller can return fresh state on those extra opportunities, the saved input waiting time can exceed the added conversion and second USB hop.

For the simplified independent-phase model, changing input polling from direct 10 ms to bridged 1 ms saves 4.5 ms of average polling wait. A 1 ms output hop plus approximately 0.1 ms processing costs about 0.6 ms, leaving a conditional improvement around 3.9 ms. This is not a measured EGRET improvement: controller report-generation limits and host-library behavior can substantially change it.

If input polling, report production and host handling are otherwise equivalent, a normal asynchronous bridge adds processing and an output waiting stage. It cannot recover a button transition that the controller has not yet reported. Polling an 11 ms producer every millisecond does not turn it into a 1 ms producer.

Historical Switch 2 testing in this project also found that the host's effective period depended on endpoint packet reservation, despite the same advertised interval. The existing S path already uses the successful eight-byte reservation. USB ACK frequency is still not a measurement of library acceptance frequency.

## Scheduling assessment

The current S path and explicit-interval X path immediately prepare output after each input event. GUI observation/learning happens afterward. An unchanged in-flight repeat can be cancelled and replaced by a newly queued change; actual press/release transitions remain ordered. Relative mouse motion is accumulated without dropping its displacement. These properties avoid adding a complete firmware-loop or output-producer wait unnecessarily.

Delaying an already received digital change cannot improve its earliest possible delivery. A phase optimization would instead schedule **input acquisition** sufficiently before the actual host IN opportunity so that processing and transfer finish in time. For 1 ms output, perfect alignment could remove much of the approximately 0.5 ms average output-phase wait in the independent-clock model. This is a remaining sub-millisecond opportunity, not zero total latency. At 10 ms input, the much larger input sampling wait remains.

The existing PIO host generates a 1 ms frame and performs transfers together. The native USB side observes SOF and ACKs, but SOF is a frame boundary, not the gamepad's exact IN instant. Equal nominal rates do not lock two hosts' clock phases. Host scheduling changes, NAKs, USB reset/suspend and different endpoint workloads also matter. A safe input-phase controller would need to preserve downstream frame timing, account for actual IN phase and a sufficient processing margin, and fall back immediately when prediction expires. Shortening a frame arbitrarily or using the previous mean processing time as a deadline bound does not establish correctness.

The [current GP2040-CE scheduler source](https://github.com/OpenStickCommunity/GP2040-CE/blob/0cc9061d2352e4b9fe7b069d4e9698c10c8ff0a0/src/drivers/shared/USBReportScheduler.cpp) provides useful examples of guarded report staging and preserving digital edges. It schedules its own input processing/report submission; it does not demonstrate phase locking a separate USB input host to the console. This source snapshot is separate from the published v0.7.12 latency results. No implementation was copied.

For 0.24.1, the concrete changes remove repeated parsing and allow the substantial X producer restriction to be bypassed explicitly. No speculative input-phase delay was added. Further phase work remains technically possible; neither theoretical optimality nor equivalence to Wingman or GP2040-CE has been proved. A useful stopping criterion would be a matched electrical comparison against direct connection or another converter, including distributions and missed transitions, rather than comparing unrelated advertised means.

## Validation scope

Software checks cover independent endpoint caches, profile overrides, descriptor replay and reconnect invalidation; X descriptor/cadence behavior; producer wraparound; and the real output-switch functions with observable USB stubs. Normal firmware and the configurator are built with the existing toolchains, and the configurator's embedded firmware is checked against the standalone image. Display checks cover Japanese/English at 96/120/144/192 DPI.

No board firmware or saved setting was changed during this implementation task. No new contact-to-library, console acceptance, phase-alignment or post-optimization microsecond measurement is claimed. The previous physical measurements remain separate evidence.
