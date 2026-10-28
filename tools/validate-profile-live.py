"""Observe real input and USB output against one saved digital-only profile.

Reads only: no configuration writes or synthesized input. Keep the GUI closed.
Separate before/after snapshots exclude samples in which the source changed
during the three USB reads. Hold each control briefly for stable observations.
"""
import argparse
import ctypes as c
import datetime
import json
from pathlib import Path
import struct
import time
from controllerctl import Bridge, usb, save_snapshot


def expected(profile, raw):
    pressed = []
    for i in range(18):
        iface, rid, offset, bits, mode, flags, _, low, high = struct.unpack_from('<BBHBBBBii', profile, 64 + 16 * i)
        if not mode:
            pressed.append(False)
            continue
        if iface or rid or flags & 2 or mode != 1 or offset + bits > len(raw) * 8:
            raise ValueError('This live check supports single-interface, unnumbered range bindings only')
        value = (int.from_bytes(raw, 'little') >> offset) & ((1 << bits) - 1)
        if flags & 1 and value & (1 << (bits - 1)):
            value -= 1 << bits
        pressed.append(low <= value <= high)
    x = int(pressed[15]) - int(pressed[17])
    y = int(pressed[16]) - int(pressed[14])
    hat = {(0, 0): 15, (0, -1): 0, (1, -1): 1, (1, 0): 2, (1, 1): 3,
           (0, 1): 4, (-1, 1): 5, (-1, 0): 6, (-1, -1): 7}[(x, y)]
    buttons = sum(1 << i for i in range(14) if pressed[i])
    return struct.pack('<HB5B', buttons, hat, 128, 128, 128, 128, 0)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--profile', type=Path, required=True)
    ap.add_argument('--seconds', type=int, default=60)
    ap.add_argument('--out', type=Path, required=True)
    ap.add_argument('--statistics', action='store_true', help='Capture volatile firmware statistics; no Flash writes')
    args = ap.parse_args()
    profile = args.profile.read_bytes()
    if len(profile) not in (480, 488) or profile[11]:
        raise ValueError('Expected a supported profile with all analog outputs disabled')
    if any(profile[64 + i * 16 + 7] for i in range(18)):
        raise ValueError('This stable-input check requires rapid-fire OFF; generated transitions need a timed check')
    usb.bind(usb.h, 'HidD_GetInputReport', [usb.P, usb.P, usb.U32], c.c_ubyte)
    bridge = Bridge()
    samples = 0
    errors, mismatches, states = [], [], {}
    start = time.monotonic()
    previous_raw = None
    try:
        caps = bridge.command(20)
        if caps[9] >= caps[8] or caps[12:16] != profile[4:8] or struct.unpack_from('<H', caps, 6)[0] != len(profile):
            raise ValueError('Active input does not match the profile')
        if args.statistics:
            bridge.command(5, struct.pack('<IB', args.seconds * 1000, 0))
            time.sleep(.1)
            start = time.monotonic()
        print('READY: operate all mapped buttons and all eight directions, then release.', flush=True)
        while time.monotonic() - start < args.seconds:
            try:
                stage = 'feature-info-before'
                before = bridge.info()
                stage = 'input-report'
                report = c.create_string_buffer(9)
                if not usb.h.HidD_GetInputReport(bridge.handle, report, 9):
                    raise c.WinError(c.get_last_error())
                stage = 'feature-info-after'
                after = bridge.info()
                raw = bytes.fromhex(after['last_raw'])
                if before['last_raw'] == after['last_raw'] and previous_raw == raw:
                    want, actual = expected(profile, raw), report.raw[1:]
                    samples += 1
                    key = raw.hex() + '/' + actual.hex()
                    if key not in states:
                        states[key] = dict(time=round(time.monotonic() - start, 3), raw=raw.hex(), output=actual.hex(), expected=want.hex())
                    if want != actual:
                        mismatches.append(states[key])
                previous_raw = raw
            except OSError as ex:
                errors.append(dict(time=round(time.monotonic() - start, 3), stage=stage, error=str(ex)))
                previous_raw = None
            time.sleep(.02)
        seen_buttons = 0
        hats = set()
        for state in states.values():
            value = bytes.fromhex(state['output'])
            seen_buttons |= int.from_bytes(value[:2], 'little')
            hats.add(value[2])
        required = sum(1 << i for i in range(14) if profile[64 + i * 16 + 5])
        result = dict(timestamp=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                      seconds=args.seconds, stable_samples=samples, states=list(states.values()),
                      errors=errors, mismatches=mismatches, required_buttons=required,
                      observed_buttons=seen_buttons, observed_hats=sorted(hats),
                      all_mapped_buttons_observed=(seen_buttons & required) == required,
                      all_eight_directions_observed=set(range(8)).issubset(hats),
                      neutral_observed=any(s['output'] == '00000f8080808000' for s in states.values()))
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(result, indent=2), encoding='utf-8')
        print(json.dumps({k:v for k,v in result.items() if k not in ('states','errors','mismatches')}, indent=2))
        print(f'USB errors={len(errors)}, mismatches={len(mismatches)}', flush=True)
        if args.statistics:
            time.sleep(.2)
            save_snapshot(bridge.snapshot(), args.out.with_suffix('.stats.json'))
    finally:
        bridge.close()


if __name__ == '__main__':
    main()
