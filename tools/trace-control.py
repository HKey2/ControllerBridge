"""Capture one control-traffic failure using an explicitly enabled RAM trace."""
import argparse
import ctypes as c
import json
from pathlib import Path
import struct
import time
from controllerctl import Bridge, usb

ap = argparse.ArgumentParser(description=__doc__)
ap.add_argument('--out', type=Path, required=True)
ap.add_argument('--seconds', type=int, default=60)
args = ap.parse_args()
usb.bind(usb.h, 'HidD_GetInputReport', [usb.P, usb.P, usb.U32], c.c_ubyte)
b = Bridge()
failures = []
try:
    header = b.command(27, b'\1')
    if header[:4] != b'TRC1':
        raise RuntimeError('Trace firmware required')
    print('Trace armed', flush=True)
    start = time.monotonic()
    while time.monotonic() - start < args.seconds:
        stage = 'info-before'
        try:
            b.info()
            stage = 'input-report'
            report = c.create_string_buffer(9)
            if not usb.h.HidD_GetInputReport(b.handle, report, 9):
                raise c.WinError(c.get_last_error())
            stage = 'info-after'
            b.info()
        except OSError as ex:
            failures.append(dict(time=time.monotonic() - start, stage=stage, error=str(ex)))
            time.sleep(1)
            break
        time.sleep(.02)
    header = b.command(27, b'\2')
    _, head, count, armed, size = struct.unpack_from('<5I', header)
    entries = []
    for i in range(count):
        raw = b.command(28, struct.pack('<H', i))[:size]
        entry = dict(zip(('time','event','a','b','ep0_in','ep0_out','sie','buffers'), struct.unpack('<8I', raw)))
        entries.append(entry)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(dict(head=head, failures=failures, entries=entries), indent=2), encoding='utf-8')
    print(f'Captured {count} entries from head={head}; failures={failures}', flush=True)
finally:
    b.close()
