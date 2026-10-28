"""Show six low-brightness colors for 1 second each, with 1 second dark gaps.

Close ControllerBridge.exe first. Requires firmware 0.5.2-dev or later.
LED commands change RAM only; no profile/configuration/Flash writes.
Automatic state indication resumes when the test finishes.
"""
import argparse
import datetime
import json
from pathlib import Path
import struct
import time
from controllerctl import Bridge

COLORS = ('blue', 'cyan', 'yellow', 'green', 'red', 'purple')


def led(bridge, action=0, color=0, level=1):
    b = bridge.command(31, bytes((action, color, level)))
    if b[:4] != b'LED1':
        raise ValueError('LED diagnostics are not available')
    names = ('deadline_us', 'now_us', 'writes', 'last_on_us', 'last_off_us')
    return dict(state=b[4], shown=b[5], level=b[6], override=bool(b[7]),
                **dict(zip(names, struct.unpack_from('<5I', b, 8))), sm=b[28], gpio=b[29],
                flags=b[30], blink_period_us=struct.unpack_from('<I', b, 32)[0])


def raw(bridge):
    b = bridge.command(30)
    seq, timestamp = struct.unpack_from('<II', b, 4)
    return dict(sequence=seq, timestamp_us=timestamp)


def profiles(bridge):
    caps = bridge.profile_caps()
    return [b''.join(bridge.command(23, struct.pack('<BH', slot, offset))
                    for offset in range(0, caps['size'], 56))[:caps['size']] for slot in range(caps['count'])]


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--level', type=int, choices=range(1, 33), default=1,
                    help='Maximum RGB channel (default 1 out of 255)')
    ap.add_argument('--repeat', type=int, choices=range(1, 4), default=1)
    ap.add_argument('--out', type=Path, default=Path('captures/led-colors.json'))
    args = ap.parse_args()
    bridge = Bridge()
    result = dict(at=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                  level=args.level, colors=[], errors=[],
                  limit='Telemetry confirms scheduled PIO writes; visible colors/brightness require human observation.')
    try:
        saved = profiles(bridge)
        result['before'] = bridge.info()
        result['raw_before'] = raw(bridge)
        led(bridge, 2)
        print('Starting in 3 seconds: BLUE -> CYAN -> YELLOW -> GREEN -> RED -> PURPLE.', flush=True)
        time.sleep(3)
        for cycle in range(args.repeat):
            for color, name in enumerate(COLORS):
                print(f'{cycle+1}: {name.upper()} (1 s, level {args.level}/255)', flush=True)
                started = led(bridge, 1, color, args.level)
                time.sleep(.05)
                lit = led(bridge)
                time.sleep(1.02)
                off = led(bridge)
                duration = ((off['last_off_us']-lit['last_on_us']) & 0xffffffff)/1000
                result['colors'].append(dict(name=name, start=started, lit=lit, off=off, on_ms=duration))
                if lit['shown'] != color or lit['level'] != args.level or off['shown'] != 255 or not 980 <= duration <= 1020:
                    result['errors'].append(f'{name}: unexpected color/level/timing')
                # Total start-to-start interval is at least two seconds.
                time.sleep(.93)
        result['after'] = bridge.info()
        result['raw_after'] = raw(bridge)
        result['profiles_unchanged'] = profiles(bridge) == saved
        delta = result['after']['raw_reports'] - result['before']['raw_reports']
        error_delta = result['after']['input_errors'] - result['before']['input_errors']
        result['received_reports'] = delta
        result['input_error_delta'] = error_delta
        a, b = result['raw_before'], result['raw_after']
        seq_delta = (b['sequence']-a['sequence']) & 0xffffffff
        result['mean_input_interval_us'] = ((b['timestamp_us']-a['timestamp_us']) & 0xffffffff)/seq_delta if seq_delta else None
        if not result['profiles_unchanged'] or error_delta:
            result['errors'].append('Profile changed or USB input error occurred')
    finally:
        try:
            result['final_led'] = led(bridge, 3)
        finally:
            bridge.close()
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in result.items() if k not in ('colors', 'before', 'after')}, indent=2), flush=True)
    if result['errors']:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
