"""Configure and measure this project's USB bridge using Windows HID features.

No dependencies beyond Python. A read-only UC2R identity check is mandatory
before sending any command, even when another device shares the VID/PID.
"""
import argparse
import ctypes as c
import datetime
import json
import math
from pathlib import Path
import struct
import time
import inspect_usb as usb
from decimal import Decimal, InvalidOperation
import zlib

VERSION = 0x00020000
BUTTONS = dict(Y=1, B=2, A=4, X=8, L=16, R=32, ZL=64, ZR=128,
               MINUS=256, PLUS=512, LSTICK=1024, RSTICK=2048, HOME=4096, CAPTURE=8192)
PHYSICAL = dict(top_left=9, top_middle=4, top_right=1, bottom_left=5,
                bottom_middle=3, bottom_right=2, credit=7, start=8, menu=10)
AXES = {'dpad': 0, 'left-stick': 1, 'both': 2}
HOST = 'reports transfer_errors stalls attempts frame_late frame_us_max poll_interval_ms generation in_tokens in_naks in_duplicates in_packet_errors'.split()
DEVICE = 'resets setups sof acks naks control_stalls bus_errors abort_timeouts last_sof_us last_ack_us configured suspended'.split()
COUNTS = 'raw_reports input_changes bad_reports raw_queue_drop transition_queue_drop transitions_acked fresh_acked repeat_acked repeats_replaced max_depth input_disconnects diagnostic_commands save_failures reserved0 reserved1 reserved2'.split()
for name in ('HidD_GetFeature', 'HidD_SetFeature'):
    usb.bind(usb.h, name, [usb.P, usb.P, usb.U32], c.c_ubyte)


class Bridge:
    def __init__(self):
        self.handle = None
        self.output_reconnect_pending = False
        matches = []
        for path in usb.interfaces('4d1e55b2-f16f-11cf-88cb-001111000030'):
            if not any(identity in path.lower() for identity in ('vid_20d6&pid_a713', 'vid_cafe&pid_4004')):
                continue
            handle = None
            try:
                handle = usb.open_device(path, 0xc0000000)
                response = self.get_feature(handle)
                if response[:5] == b'UC2R\x01':
                    matches.append((handle, path, response[6]))
                    handle = None
            except OSError:
                pass
            finally:
                if handle is not None:
                    usb.k.CloseHandle(handle)
        if len(matches) != 1:
            for handle, _, _ in matches:
                usb.k.CloseHandle(handle)
            raise RuntimeError(f'Expected exactly one UC2 bridge; found {len(matches)}')
        self.handle, self.path, self.sequence = matches[0]

    def close(self):
        if self.handle is not None:
            usb.k.CloseHandle(self.handle)
            self.handle = None

    @staticmethod
    def get_feature(handle):
        buffer = c.create_string_buffer(65)
        if not usb.h.HidD_GetFeature(handle, buffer, 65):
            raise c.WinError(c.get_last_error())
        return buffer.raw[1:]

    def command(self, command, payload=b'', timeout=5):
        if len(payload) > 56:
            raise ValueError('Command payload exceeds 56 bytes')
        self.sequence = (self.sequence + 1) & 255
        packet = b'\0UC2C' + bytes((1, command, self.sequence, 0)) + payload.ljust(56, b'\0')
        buffer = c.create_string_buffer(packet, 65)
        if not usb.h.HidD_SetFeature(self.handle, buffer, 65):
            raise c.WinError(c.get_last_error())
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            response = self.get_feature(self.handle)
            if response[:7] == b'UC2R' + bytes((1, command, self.sequence)):
                if response[7]:
                    status = {1: 'invalid argument', 2: 'measurement is busy', 3: 'not available or flash save failed',
                              4: 'profile generation conflict; reload settings',
                              5: 'mapping and output mode belong to profiles; use ControllerBridge.exe'}
                    raise RuntimeError(status.get(response[7], f'status {response[7]}'))
                return response[8:]
            time.sleep(0.002)
        raise TimeoutError(f'Command {command} timed out')

    def info(self):
        b = self.command(1)
        fields = 'version uptime_us flags raw_reports output_acks input_naks input_errors'.split()
        result = dict(zip(fields, struct.unpack_from('<7I', b)))
        result.update(last_raw=b[28:32].hex(), last_output=b[32:40].hex(),
                      input_interval_ms=b[40], desired_output_interval_ms=b[41], axis_mode=b[42],
                      active_output=decode_output_options(b[43]),
                      layout_fields=struct.unpack_from('<I', b, 44)[0],
                      queue_depth=struct.unpack_from('<I', b, 48)[0],
                      active_output_interval_ms=struct.unpack_from('<I', b, 52)[0])
        result['state'] = [name for bit, name in enumerate(('usb_configured', 'input_ready', 'measuring', 'saved_snapshot', 'starting', 'armed_next_boot', 'completed_snapshot', 'stopping')) if result['flags'] & (1 << bit)]
        return result

    def config(self):
        return bytearray(self.command(2)[:40])

    def snapshot(self, saved=False):
        n, version = struct.unpack_from('<II', self.command(7, bytes((saved,))))
        if n != 3784 or version != VERSION:
            raise RuntimeError(f'Unknown snapshot ABI: {n} bytes, {version:#x}')
        return b''.join(self.command(8, struct.pack('<I', offset)) for offset in range(0, n, 56))[:n]

    def profile_caps(self):
        b = self.command(20)
        size = struct.unpack_from('<H', b, 6)[0]
        if b[:4] != b'UPG1' or (size, b[8]) != (488, 16) or struct.unpack_from('<I', b, 44)[0] & 4672 != 4672:
            raise RuntimeError('Unsupported profile ABI')
        return dict(size=size, count=b[8], active=b[9], generation=struct.unpack_from('<I', b, 28)[0],
                    features=struct.unpack_from('<I', b, 44)[0])

    def read_profile(self, slot, caps=None):
        caps = caps or self.profile_caps()
        if not 0 <= slot < caps['count']: raise ValueError('Profile slot out of range')
        raw = b''.join(self.command(23, struct.pack('<BH', slot, off)) for off in range(0, caps['size'], 56))[:caps['size']]
        if struct.unpack_from('<H', raw)[0] != 5: raise RuntimeError('Unsupported profile version')
        identity = self.command(32, bytes((slot,)))
        if struct.unpack_from('<II', identity, 16) != (caps['generation'], zlib.crc32(raw)):
            raise RuntimeError('Profile changed while reading; retry')
        return bytearray(raw), identity[:16]

    def save_profile(self, slot, raw, generation, identity=None):
        caps = self.profile_caps()
        if len(raw) != caps['size'] or not 0 <= slot < caps['count']: raise ValueError('Invalid profile size or slot')
        version = struct.unpack_from('<H', raw)[0]
        if version != 5: raise ValueError('Only the current profile version is supported')
        begin = struct.pack('<BII', slot, zlib.crc32(raw), generation)
        if identity is not None:
            if len(identity) != 16: raise ValueError('Profile identity must be 16 bytes')
            begin += b'\x01' + identity
        self.command(24, begin)
        for off in range(0, len(raw), 52):
            chunk = raw[off:off+52]
            self.command(25, struct.pack('<HBB', off, len(chunk), 0) + chunk)
        saved_generation = struct.unpack_from('<I', self.command(26))[0]
        check_caps = self.profile_caps()
        check_raw, check_id = self.read_profile(slot, check_caps)
        if check_caps['generation'] != saved_generation or check_raw != raw or (identity is not None and check_id != identity):
            raise RuntimeError('Saved profile read-back differs')
        result = self.command(46, struct.pack('<IB', saved_generation, check_caps['active']))
        if result[:4] != b'OUT1': raise RuntimeError('Unexpected profile output reply')
        self.output_reconnect_pending = bool(result[6])
        return saved_generation


def decode_output_options(options):
    return dict(mode='combined' if options & 64 else 'mouse' if options & 1 else 'gamepad', gamepad_buttons=bool(options & 128))


def decode_mouse_profile(b):
    if b[:4] != b'MPR1': raise RuntimeError('Per-profile mouse settings require firmware 0.10.0 or later')
    flags = b[8]
    return dict(slot=b[4]+1 if b[4] < 16 else None, trackball_detected=bool(b[5]&1), paddle_detected=bool(b[5]&2),
                trackball_enabled=bool(flags&1), paddle_enabled=bool(flags&2), trackball_gain=(b[9] | b[11] << 8)/10,
                paddle_gain=b[10]/10, invert_x=bool(flags&4), invert_y=bool(flags&8), invert_paddle=bool(flags&16))


def gain_tenths(value, maximum=64):
    try:
        gain = Decimal(value)
        if not gain.is_finite() or not 1 <= gain <= maximum or gain*10 != (gain*10).to_integral_value(): raise ValueError()
        return int(gain*10)
    except (ValueError, InvalidOperation):
        raise argparse.ArgumentTypeError(f'Gain must be 1.0..{maximum}.0 in steps of 0.1')


def update_mouse_profile(bridge, slot=None, trackball=None, paddle=None, trackball_gain=None, paddle_gain=None,
                         invert_x=None, invert_y=None, invert_paddle=None, output_mode=None):
    caps = bridge.profile_caps()
    slot = caps['active'] if slot is None else slot
    raw, identity = bridge.read_profile(slot, caps)
    if not struct.unpack_from('<H', raw, 2)[0] & 1: raise ValueError('Choose a registered profile')
    if output_mode is not None:
        raw[484] = dict(gamepad=raw[484]&4, mouse=(raw[484]&4)|1, combined=2, **{'combined-gamepad': 6})[output_mode]
    for mask, value in ((1, trackball), (2, paddle), (4, invert_x), (8, invert_y), (16, invert_paddle)):
        if value is not None: raw[480] = (raw[480] | mask) if value else (raw[480] & ~mask)
    if trackball_gain is not None:
        if not 10 <= trackball_gain <= 640: raise ValueError('Trackball gain must be 1.0..64.0')
        raw[481] = trackball_gain & 255
        raw[483] = trackball_gain >> 8
    if paddle_gain is not None:
        if not 10 <= paddle_gain <= 100: raise ValueError('Paddle gain must be 1.0..10.0')
        raw[482] = paddle_gain
    generation = bridge.save_profile(slot, raw, caps['generation'], identity)
    return dict(slot=slot+1, generation=generation, flags=raw[480], trackball_gain=(raw[481] | raw[483] << 8)/10, paddle_gain=raw[482]/10,
                output_mode=raw[484]&3, controller_buttons=bool(raw[484]&4), usb_reconnect_pending=bridge.output_reconnect_pending)


def decode_mouse(b):
    if b[:4] != b'MSE1':
        raise RuntimeError('Mouse output requires firmware 0.9.0-dev or later')
    names = 'input_reports movement_inputs movement_acks button_changes_acked dropped_frames max_depth input_x input_y acked_x acked_y queue_depth flushed_backlogs'.split()
    result = dict(active=decode_output_options(b[4]), saved_or_pending=decode_output_options(b[5]),
                  relative_xy_ready=bool(b[6]), buttons=b[7], **dict(zip(names, struct.unpack_from('<12I', b, 8))))
    for key in ('input_x', 'input_y', 'acked_x', 'acked_y'):
        if result[key] >= 2**31: result[key] -= 2**32
    return result


def decode_mouse_wire(b):
    if b[:4] != b'MWR1':
        raise RuntimeError('Mouse wire status requires firmware 0.13.2 or later')
    return dict(protocol='report' if b[4] else 'boot', axis_bits=16 if b[4] else 8,
                report_bytes=b[5], mouse_enabled=bool(b[6]), combined=bool(b[7]),
                maximum_delta=struct.unpack_from('<I', b, 8)[0])


def decode_config(b):
    v, inp, out, axis, options, *rest = struct.unpack('<IBBBB12HII', b)
    return dict(version=v, input_interval_ms=inp, output_interval_ms=out,
                output=decode_output_options(options),
                axis_mode=next((name for name, value in AXES.items() if value == axis), axis),
                button_map={str(i + 1): [name for name, bit in BUTTONS.items() if rest[i] & bit] for i in range(12)},
                boot_capture_ms=rest[12], warmup_ms=rest[13])


def histogram(b, offset):
    count, minimum, maximum, step, total = struct.unpack_from('<IIIIQ', b, offset)
    bins = struct.unpack_from('<128I', b, offset + 24)
    if sum(bins) != count:
        raise ValueError('Histogram count mismatch')
    result = dict(count=count, min_us=minimum if count else None, max_us=maximum if count else None,
                  mean_us=total / count if count else None, bin_width_us=step,
                  overflow_threshold_us=127 * step, bins=list(bins))
    for label, fraction in [('p50', .5), ('p95', .95), ('p99', .99)]:
        acc = 0
        result[label + '_us_bounds'] = None
        if count:
            for i, n in enumerate(bins):
                acc += n
                if acc >= math.ceil(count * fraction):
                    result[label + '_us_bounds'] = [i * step, maximum if i == 127 else (i + 1) * step - 1]
                    break
    return result


def decode_snapshot(b):
    magic, version, size, flags, start, end = struct.unpack_from('<6I', b)
    if magic != 0x4d324355 or version != VERSION or size != 3784 or size != len(b):
        raise ValueError('Invalid snapshot header')
    elapsed = ((end - start) & 0xffffffff) / 1e6
    def counters(names, offset):
        return dict(zip(names, struct.unpack_from('<' + 'I' * len(names), b, offset)))
    hb, he = counters(HOST, 64), counters(HOST, 112)
    db, de = counters(DEVICE, 160), counters(DEVICE, 208)
    hd = {k: (he[k] - hb[k]) & 0xffffffff for k in HOST if k not in ('frame_us_max', 'poll_interval_ms', 'generation')}
    dd = {k: (de[k] - db[k]) & 0xffffffff for k in DEVICE[:8]}
    return dict(captured_at_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                version=version, flags=flags, start_us=start, end_us=end, elapsed_seconds=elapsed,
                output_mode='combined' if flags & 16 else 'mouse' if flags & 8 else 'gamepad',
                combined=decode_dual_snapshot(b[3552:3688]) if flags & 16 else None,
                mouse_buttons=decode_mouse_button_snapshot(b[3688:]) if flags & 24 else None,
                configuration=decode_config(b[24:64]),
                active_profile=(dict(slot=struct.unpack_from('<I', b, 308)[0],
                                     crc32=f'{struct.unpack_from("<I", b, 312)[0]:08X}',
                                     bank_generation=struct.unpack_from('<I', b, 316)[0]) if flags & 4 else None),
                host_begin=hb, host_end=he, host_delta=hd,
                device_begin=db, device_end=de, device_delta=dd, counts=counters(COUNTS, 256),
                last_raw=b[320:324].hex(), last_output=b[324:332].hex(),
                rates_hz={k: n / elapsed for k, n in dict(input_reports=hd['reports'], input_tokens=hd['in_tokens'], input_naks=hd['in_naks'], output_acks=dd['acks'], output_sof=dd['sof']).items()},
                latency={name: histogram(b, 336 + i * 536) for i, name in enumerate(('rx_to_mapping', 'rx_to_ready', 'rx_to_ack', 'output_ack_gap', 'transition_rx_to_ready', 'transition_rx_to_ack')[:(size - 336) // 536])},
                limits=['Timestamps are firmware observations after input transfer and in output completion IRQ.',
                        'RX-to-ACK includes waiting for the output host poll; excludes physical switch scan, input poll wait, console processing and display.',
                        'Overall ACK samples include replaceable steady-state reports. Use transition_rx_to_ack for latency of changed states when available.',
                        'No diagnostic commands or flash writes are issued inside a normal measurement interval.',
                        'Histogram percentiles are bounds. The final bin includes overflow; max and mean remain exact for recorded samples.'])


def decode_dual_snapshot(b):
    if len(b) != 136 or b[:4] != b'DUO1': raise ValueError('Invalid combined output statistics')
    names = 'mouse_acks mouse_changes mouse_transitions mouse_drops mouse_flushes mouse_max_depth pad_seen pad_acked mouse_buttons_seen mouse_buttons_acked input_x input_y acked_x acked_y reserved'.split()
    result = dict(zip(names, struct.unpack_from('<15I', b, 4)))
    result['fixed_probe_amplitude'] = result['reserved']
    for key in ('input_x', 'input_y', 'acked_x', 'acked_y'):
        if result[key] >= 0x80000000: result[key] -= 0x100000000
    for i, name in enumerate(('rx_to_mouse_ready', 'rx_to_mouse_ack', 'mouse_ack_gap')):
        count, low, high, _, total = struct.unpack_from('<IIIIQ', b, 64+i*24)
        result[name] = dict(count=count, min_us=low if count else None, max_us=high if count else None, mean_us=total/count if count else None)
    result['navigation_seen'] = [name for name in ('MINUS', 'PLUS', 'HOME') if result['pad_seen'] & BUTTONS[name]]
    result['navigation_acked'] = [name for name in ('MINUS', 'PLUS', 'HOME') if result['pad_acked'] & BUTTONS[name]]
    result['buttons_seen'] = [name for name, mask in BUTTONS.items() if result['pad_seen'] & mask]
    result['buttons_acked'] = [name for name, mask in BUTTONS.items() if result['pad_acked'] & mask]
    return result


def decode_mouse_button_snapshot(b):
    if len(b) != 96 or b[:4] != b'MBT1': raise ValueError('Invalid mouse button statistics')
    names = 'input_edges moving_edges acked_edges dropped_edges max_depth'.split()
    result = dict(zip(names, struct.unpack_from('<5I', b, 4)))
    for index, name in enumerate(('input_down', 'input_up', 'acked_down', 'acked_up')):
        result[name] = dict(zip(('left', 'right'), struct.unpack_from('<2I', b, 24+index*8)))
    count, low, high, _, total = struct.unpack_from('<IIIIQ', b, 56)
    result['rx_to_click_ack'] = dict(count=count, min_us=low if count else None,
                                    max_us=high if count else None, mean_us=total/count if count else None)
    result.update(zip(('pending', 'input_initial', 'acked_initial', 'wire_formats'), struct.unpack_from('<4I', b, 80)))
    result['wire_formats_seen'] = [name for mask, name in ((1, 'boot-8bit'), (2, 'report-16bit')) if result['wire_formats'] & mask]
    return result


def save_snapshot(raw, path):
    result = decode_snapshot(raw)
    if path:
        path = Path(path)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
        path.with_suffix('.bin').write_bytes(raw)
    compact = {k: result[k] for k in ('elapsed_seconds', 'configuration', 'rates_hz', 'host_delta', 'device_delta', 'counts')}
    compact['latency'] = {k: {f: v for f, v in h.items() if f != 'bins'} for k, h in result['latency'].items()}
    if result.get('combined') is not None: compact['combined'] = result['combined']
    if result.get('mouse_buttons') is not None: compact['mouse_buttons'] = result['mouse_buttons']
    print(json.dumps(compact, indent=2), flush=True)
    return result


def measure(bridge, seconds, path, save=False):
    status = bridge.info()
    if status['flags'] & 3 != 3:
        raise RuntimeError('Input and output must both be ready before measuring')
    bridge.command(5, struct.pack('<IB', round(seconds * 1000), save))
    print(f'Measuring {seconds:g}s; no diagnostic traffic during this interval.', flush=True)
    time.sleep(seconds + .4)
    deadline = time.monotonic() + 5
    while True:
        status = bridge.info()
        if not status['flags'] & (4 | 16 | 128):
            break
        if time.monotonic() > deadline:
            raise TimeoutError('Measurement did not finish')
        time.sleep(.1)
    return save_snapshot(bridge.snapshot(), path)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    subs = parser.add_subparsers(dest='command', required=True)
    for name in ('info', 'config', 'stop', 'reboot', 'bootsel', 'mouse-status'):
        subs.add_parser(name)
    p = subs.add_parser('output', help='Save the active profile output mode; USB reconnects if the layout changes')
    p.add_argument('mode', choices=('gamepad', 'mouse', 'combined', 'combined-gamepad'))
    p.add_argument('--gain', type=gain_tenths)
    p.add_argument('--invert-x', action=argparse.BooleanOptionalAction, default=None)
    p.add_argument('--invert-y', action=argparse.BooleanOptionalAction, default=None)
    p = subs.add_parser('profile-mouse', help='Update independent mouse settings in one profile without reboot')
    p.add_argument('--slot', type=int, choices=range(1, 17), help='1..16; defaults to the active profile')
    for control in ('trackball', 'paddle'):
        p.add_argument('--'+control, action=argparse.BooleanOptionalAction, default=None)
        p.add_argument('--'+control+'-gain', type=gain_tenths if control == 'trackball' else lambda value: gain_tenths(value, 10))
    for axis in ('x', 'y', 'paddle'):
        p.add_argument('--invert-'+axis, action=argparse.BooleanOptionalAction, default=None)
    p = subs.add_parser('set')
    p.add_argument('--input-ms', type=int, choices=range(256))
    p.add_argument('--output-ms', type=int, choices=range(1, 256))
    p.add_argument('--axis', choices=AXES)
    p.add_argument('--map', action='append', default=[], metavar='POSITION=BUTTON[+BUTTON]', help='e.g. top_left=Y or 9=Y; NONE unmaps')
    p.add_argument('--save', action='store_true')
    p = subs.add_parser('measure')
    p.add_argument('--seconds', type=float, default=10)
    p.add_argument('--out', required=True)
    p.add_argument('--save', action='store_true')
    p = subs.add_parser('snapshot')
    p.add_argument('--saved', action='store_true')
    p.add_argument('--out', required=True)
    p.add_argument('--expect-seconds', type=float, help='Reject stale snapshots with a different measurement duration')
    p.add_argument('--require-changes', action='store_true', help='Reject a snapshot without any recorded state changes')
    p = subs.add_parser('arm')
    p.add_argument('--seconds', type=float, default=60)
    p = subs.add_parser('sweep')
    p.add_argument('--intervals', default='10,8,4,2,1')
    p.add_argument('--seconds', type=float, default=10)
    p.add_argument('--out-dir', required=True)
    p = subs.add_parser('watch')
    p.add_argument('--seconds', type=float, default=60)
    p.add_argument('--out')
    args = parser.parse_args()
    bridge = Bridge()
    try:
        if args.command == 'info': print(json.dumps(bridge.info(), indent=2))
        elif args.command == 'config': print(json.dumps(decode_config(bridge.config()), indent=2))
        elif args.command == 'mouse-status':
            result = decode_mouse(bridge.command(36))
            features = bridge.profile_caps()['features']
            if features & 64:
                result['profile'] = decode_mouse_profile(bridge.command(37))
                result['gain_source'] = 'profile'
            if features & 1024:
                result['wire'] = decode_mouse_wire(bridge.command(38))
            print(json.dumps(result, indent=2))
        elif args.command == 'profile-mouse':
            print(json.dumps(update_mouse_profile(bridge, None if args.slot is None else args.slot-1,
                args.trackball, args.paddle, args.trackball_gain, args.paddle_gain, args.invert_x, args.invert_y, args.invert_paddle), indent=2))
        elif args.command == 'output':
            print(json.dumps(update_mouse_profile(bridge, trackball_gain=args.gain, invert_x=args.invert_x,
                invert_y=args.invert_y, output_mode=args.mode), indent=2))
        elif args.command == 'set':
            cfg = bridge.config()
            if args.input_ms is not None: cfg[4] = args.input_ms
            if args.output_ms is not None: cfg[5] = args.output_ms
            if args.axis: cfg[6] = AXES[args.axis]
            for mapping in args.map:
                key, value = mapping.split('=', 1)
                usage = PHYSICAL[key] if key in PHYSICAL else int(key)
                if not 1 <= usage <= 12: raise ValueError('HID usage must be 1..12')
                bits = 0
                if value.upper() != 'NONE':
                    for name in value.upper().split('+'): bits |= BUTTONS[name]
                struct.pack_into('<H', cfg, 8 + 2 * (usage - 1), bits)
            bridge.command(3, cfg)
            if args.save: bridge.command(4)
            print(json.dumps(decode_config(bridge.config()), indent=2))
            if args.output_ms is not None:
                print('Output descriptor interval takes effect after saving and rebooting.')
        elif args.command == 'measure': measure(bridge, args.seconds, args.out, args.save)
        elif args.command == 'snapshot':
            raw = bridge.snapshot(args.saved)
            result = decode_snapshot(raw)
            if args.expect_seconds is not None and abs(result['elapsed_seconds'] - args.expect_seconds) > .1:
                raise RuntimeError(f"Snapshot is {result['elapsed_seconds']:.6f}s, expected {args.expect_seconds:g}s; output file was not written")
            if args.require_changes and not result['counts']['input_changes']:
                raise RuntimeError('Snapshot contains no state changes; output file was not written')
            save_snapshot(raw, args.out)
        elif args.command == 'arm':
            duration = round(args.seconds * 1000)
            bridge.command(10, struct.pack('<I', duration))
            if decode_config(bridge.config())['boot_capture_ms'] != duration:
                raise RuntimeError('Read-back of next-boot duration differs')
            print(f'Next power-on: capture {args.seconds:g}s after both USB sides are ready plus configured warmup, then save once. Disconnect from PC now and connect to the console.')
        elif args.command in ('stop', 'reboot', 'bootsel'):
            bridge.command(dict(stop=6, reboot=14, bootsel=9)[args.command])
            print(args.command + ' accepted')
        elif args.command == 'sweep':
            original = bridge.config()
            intervals = [int(v) for v in args.intervals.split(',')]
            if not all(0 <= v <= 255 for v in intervals): raise ValueError('Invalid interval')
            try:
                for interval in intervals:
                    cfg = bytearray(original); cfg[4] = interval
                    bridge.command(3, cfg); time.sleep(.2)
                    measure(bridge, args.seconds, Path(args.out_dir) / f'input-{interval}ms.json')
            finally:
                bridge.command(3, original)
        elif args.command == 'watch':
            rows, previous = [], None
            deadline = time.monotonic() + args.seconds
            print('Watching mapped state (diagnostic traffic; not a latency measurement).', flush=True)
            while time.monotonic() < deadline:
                info = bridge.info()
                state = (info['last_raw'], info['last_output'])
                if state != previous:
                    row = dict(time_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(), **info)
                    rows.append(row); print(json.dumps(row), flush=True); previous = state
                time.sleep(.02)
            if args.out:
                Path(args.out).write_text(json.dumps(rows, indent=2) + '\n', encoding='utf-8')
    finally:
        bridge.close()


if __name__ == '__main__':
    main()
