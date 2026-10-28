"""Arm, inspect and retrieve the finite USB output profile comparison."""
import argparse
import datetime
import json
from pathlib import Path
import secrets
import struct
import controllerctl as ctl

MAGIC = 0x31425250
SIZE = 3504
PROFILES = ['powera-original', 'powera-usb2', 'powera-packet8', 'hori-pokken-1ms', 'pro-controller-8ms', 'pro-controller-1ms', 'nso-gamecube-4ms', 'nso-gamecube-1ms']
FIELDS = ('profile start_us end_us configured_begin configured_end acks naks sof control_stalls bus_errors abort_timeouts resets setups '
          'input_reports input_changes input_errors input_disconnects event_drops transition_drops max_depth transitions_acked '
          'normal_acks reply_acks out_packets out_drops unknown_commands handshake_mask subcommand_mask report_mode reserved0 reserved1 reserved2').split()

def info(bridge):
    raw = bridge.command(16)
    values = struct.unpack('<14I', raw)
    if values[:3] != (MAGIC, 1, SIZE):
        raise RuntimeError('Device is not the supported poll-probe firmware')
    keys = 'magic version bytes state token duration_ms completed profiles save_errors trigger reserved0 reserved1 runtime_state active_profile'.split()
    return dict(zip(keys, values))

def hist(data, offset):
    count, low, high, step, total = struct.unpack_from('<IIIIQ', data, offset)
    bins = list(struct.unpack_from('<16I', data, offset + 24))
    if count != sum(bins):
        raise RuntimeError('Invalid histogram')
    return dict(count=count, min_us=low if count else None, max_us=high if count else None,
                mean_us=total / count if count else None, bin_width_us=step, bins=bins)

def decode(raw):
    header = struct.unpack_from('<12I', raw)
    if header[:2] != (MAGIC, 1) or header[2] != len(raw) or len(raw) != 48+432*header[7]:
        raise RuntimeError('Invalid experiment data')
    result = dict(zip('magic version bytes state token duration_ms completed profiles save_errors trigger reserved0 reserved1'.split(), header))
    result['retrieved_utc'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
    result['results'] = []
    for i in range(result['completed']):
        off = 48 + i * 432
        record = dict(zip(FIELDS, struct.unpack_from('<32I', raw, off)))
        if record['profile'] != i:
            raise RuntimeError('Profile order mismatch')
        elapsed = ((record['end_us'] - record['start_us']) & 0xffffffff) / 1e6
        record.update(name=PROFILES[i], elapsed_seconds=elapsed, output_hz=record['acks']/elapsed,
                      input_hz=record['input_reports']/elapsed, ack_gap=hist(raw, off + 128),
                      transition_rx_to_ack=hist(raw, off + 216),
                      initialization_trace=[raw[off + 304 + j*8:off + 312 + j*8].hex() for j in range(16)])
        result['results'].append(record)
    result['limits'] = ['Each profile has a 4-second warmup followed by a fixed measurement interval.',
                        'A zero-ACK/unconfigured profile is not evidence of a polling-rate limit.',
                        'Pro Controller input is emitted only after report mode 0x30 is selected.',
                        'Initialization command counters cover warmup and measurement; ACK/error counters cover measurement.',
                        'Trace entries contain packet bytes 0..3 and 10..13, not full packets.',
                        'Input transfer-error counter uses the most recently received host statistics (up to one second old).']
    return result

def main():
    p = argparse.ArgumentParser(description=__doc__)
    sub = p.add_subparsers(dest='action', required=True)
    sub.add_parser('info')
    arm = sub.add_parser('arm'); arm.add_argument('--seconds', type=float, default=12)
    arm.add_argument('--receipt', required=True)
    sub.add_parser('start-pc')
    get = sub.add_parser('get'); get.add_argument('--receipt', required=True); get.add_argument('--out', required=True)
    get.add_argument('--allow-partial', action='store_true')
    args = p.parse_args()
    bridge = ctl.Bridge()
    try:
        current = info(bridge)
        if args.action == 'arm':
            duration = round(args.seconds * 1000)
            if not 2000 <= duration <= 30000:
                raise ValueError('Use 2..30 seconds per profile')
            token = secrets.randbits(32)
            bridge.command(15, struct.pack('<II', duration, token))
            current = info(bridge)
            if (current['token'], current['state'], current['duration_ms']) != (token, 1, duration):
                raise RuntimeError('Arm readback mismatch')
            Path(args.receipt).write_text(json.dumps(current, indent=2)+'\n', encoding='utf-8')
        elif args.action == 'start-pc':
            bridge.command(18)
            print('PC validation started; USB identities will reconnect automatically.'); return
        elif args.action == 'get':
            receipt = json.loads(Path(args.receipt).read_text(encoding='utf-8'))
            if current['token'] != receipt['token']:
                raise RuntimeError('Stale measurement token; no output written')
            if current['runtime_state'] != 0:
                raise RuntimeError('Experiment is still running')
            if not args.allow_partial and (current['state'] != 3 or current['completed'] != current['profiles']):
                raise RuntimeError('Experiment incomplete; no output written')
            raw = b''.join(bridge.command(17, struct.pack('<I', offset)) for offset in range(0, SIZE, 56))[:SIZE]
            current = decode(raw)
            if current['token'] != receipt['token']:
                raise RuntimeError('Result token mismatch')
            out = Path(args.out); out.parent.mkdir(parents=True, exist_ok=True)
            out.write_text(json.dumps(current, indent=2)+'\n', encoding='utf-8'); out.with_suffix('.bin').write_bytes(raw)
            print(json.dumps([{k:r[k] for k in ('name','output_hz','input_hz','normal_acks','reply_acks','report_mode','acks','naks','control_stalls','bus_errors','event_drops','out_drops','ack_gap')} for r in current['results']], indent=2))
            return
        print(json.dumps(current, indent=2))
    finally:
        bridge.close()

if __name__ == '__main__':
    main()
