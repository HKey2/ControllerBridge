"""Read-only input packet/capability capture for firmware 0.5+. Keep the GUI closed."""
import argparse
import datetime
import json
from pathlib import Path
import struct
import time
from controllerctl import Bridge


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--seconds', type=float, default=60)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    bridge = Bridge()
    start = time.monotonic()
    records = []
    previous = None
    try:
        print('Capturing real input; configuration is read-only.', flush=True)
        while time.monotonic() - start < args.seconds:
            caps = bridge.command(20)
            packet = bridge.command(30)
            key = (caps[12:28], caps[38], packet[:4], packet[12:])
            if key != previous:
                record = dict(seconds=round(time.monotonic()-start, 3),
                              vid=f'{int.from_bytes(caps[12:14], "little"):04X}',
                              pid=f'{int.from_bytes(caps[14:16], "little"):04X}',
                              generation=struct.unpack_from('<I', caps, 20)[0],
                              fields=int.from_bytes(caps[10:12], 'little'),
                              flags=int.from_bytes(caps[18:20], 'little'),
                              protocol=caps[38], initialization=struct.unpack_from('<I', caps, 40)[0],
                              length=packet[0], captured_bytes=packet[1],
                              interface=packet[2], endpoint=packet[3],
                              sequence=struct.unpack_from('<I', packet, 4)[0],
                              input_time_us=struct.unpack_from('<I', packet, 8)[0],
                              raw=packet[12:12+packet[1]].hex())
                records.append(record)
                print(json.dumps(record), flush=True)
                previous = key
            time.sleep(.04)
        result = dict(captured_at_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                      duration_seconds=args.seconds, info=bridge.info(), records=records)
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
    finally:
        bridge.close()


if __name__ == '__main__':
    main()
