"""Read cached input USB descriptors, including unsupported devices (firmware 0.5+)."""
import argparse
import datetime
import json
from pathlib import Path
import struct
import time
from controllerctl import Bridge


def descriptor(bridge, kind, generation):
    result = bytearray()
    while True:
        reply = bridge.command(29, bytes((kind, 0)) + struct.pack('<H', len(result)))
        size, offset, count = struct.unpack_from('<HHH', reply)
        current = struct.unpack_from('<I', reply, 8)[0]
        if current != generation:
            raise RuntimeError('Input connection changed during descriptor read')
        if offset != len(result) or count > 44 or offset + count > size:
            raise ValueError('Invalid descriptor chunk')
        result.extend(reply[12:12+count])
        if len(result) == size:
            return bytes(result)
        if not count:
            raise ValueError('Empty descriptor chunk')


def decode_configuration(data):
    rows = []
    offset = 0
    while offset < len(data):
        if offset+2 > len(data) or data[offset] < 2 or offset+data[offset] > len(data):
            raise ValueError('Malformed configuration descriptor')
        d = data[offset:offset+data[offset]]
        row = dict(offset=offset, type=d[1], hex=d.hex(' '))
        if d[1] == 4 and len(d) >= 9:
            row.update(interface=d[2], alternate=d[3], endpoints=d[4],
                       interface_class=f'{d[5]:02X}', subclass=f'{d[6]:02X}', protocol=f'{d[7]:02X}')
        if d[1] == 5 and len(d) >= 7:
            row.update(endpoint=f'{d[2]:02X}', attributes=d[3], max_packet_size=int.from_bytes(d[4:6], 'little'), interval_ms=d[6])
        rows.append(row)
        offset += len(d)
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', required=True)
    args = parser.parse_args()
    bridge = Bridge()
    try:
        last_error = None
        for _ in range(5):
            caps = bridge.command(20)
            generation = struct.unpack_from('<I', caps, 20)[0]
            try:
                device = descriptor(bridge, 1, generation)
                configuration = descriptor(bridge, 2, generation)
                if len(device) != 18 or len(configuration) < 9:
                    raise RuntimeError('Input descriptors not yet available')
                result = dict(captured_at_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                              generation=generation, host_status=caps[37],
                              vid=f'{int.from_bytes(device[8:10], "little"):04X}',
                              pid=f'{int.from_bytes(device[10:12], "little"):04X}',
                              revision=f'{int.from_bytes(device[12:14], "little"):04X}',
                              device_hex=device.hex(' '), configuration=decode_configuration(configuration))
                path = Path(args.out)
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
                path.with_suffix('.device.bin').write_bytes(device)
                path.with_suffix('.config.bin').write_bytes(configuration)
                print(json.dumps(result, indent=2))
                return
            except RuntimeError as error:
                last_error = error
                time.sleep(.15)
        raise last_error
    finally:
        bridge.close()


if __name__ == '__main__':
    main()
