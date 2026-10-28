"""Read-only observation of physical button states and USB GET_REPORT output.

Keep the GUI closed. Hold A and B together for 8-10 seconds, then release.
Stops after both have been held for 6 continuous seconds and released for 1 second.
No profile writes, generated physical inputs, reboot, or firmware changes.
GET_REPORT reads the device's last armed output, not individual Interrupt ACKs.
"""
import argparse
import ctypes as c
import datetime
import json
from pathlib import Path
import statistics
import struct
import time
from controllerctl import Bridge, usb


TARGETS = {'A': 2, 'B': 1}


def describe_edges(rows, key, bit):
    previous = False
    rises, falls = [], []
    on_lengths, off_lengths = [], []
    last_edge = None
    for row in rows:
        current = bool(row[key] & bit)
        if current != previous:
            stamp = row['seconds']
            (rises if current else falls).append(stamp)
            if last_edge is not None:
                (on_lengths if previous else off_lengths).append(stamp - last_edge)
            previous, last_edge = current, stamp
    periods = [b - a for a, b in zip(rises, rises[1:])]
    return dict(rising_edges=len(rises), falling_edges=len(falls),
                first_rise_seconds=rises[0] if rises else None,
                last_rise_seconds=rises[-1] if rises else None,
                mean_hz=(len(rises)-1)/(rises[-1]-rises[0]) if len(rises)>1 else None,
                median_period_ms=1000*statistics.median(periods) if periods else None,
                minimum_period_ms=1000*min(periods) if periods else None,
                maximum_period_ms=1000*max(periods) if periods else None,
                median_on_ms=1000*statistics.median(on_lengths) if on_lengths else None,
                median_off_ms=1000*statistics.median(off_lengths) if off_lengths else None,
                maximum_on_ms=1000*max(on_lengths) if on_lengths else None,
                ended_released=not previous)


def physical_mask(profile, packet):
    raw = packet[12:12+packet[1]]
    mask = 0
    for name, index in TARGETS.items():
        iface, rid, offset, bits, mode, flags, hz, low, high = struct.unpack_from('<BBHBBBBii', profile, 64+16*index)
        if mode != 1 or iface != packet[2]:
            raise ValueError(f'{name}: requires a range binding on the captured interface')
        source = raw
        if flags & 2:
            if not source or source[0] != rid:
                raise ValueError(f'{name}: unexpected report ID')
            source = source[1:]
        if offset + bits > len(source)*8:
            raise ValueError(f'{name}: binding is outside captured bytes')
        value = (int.from_bytes(source, 'little') >> offset) & ((1 << bits)-1)
        if flags & 1 and value & (1 << (bits-1)):
            value -= 1 << bits
        if low <= value <= high:
            mask |= 1 << index
    return mask


def describe_holds(rows, bit):
    """Analyze each physical hold separately so pauses do not dilute its rate."""
    holds, start = [], None
    for i, row in enumerate(rows):
        held = bool(row['physical'] & bit)
        if held and start is None:
            start = i
        elif not held and start is not None:
            segment = rows[start:i+1]
            holds.append(dict(start_seconds=segment[0]['seconds'],
                              end_seconds=row['seconds'],
                              duration_seconds=row['seconds']-segment[0]['seconds'],
                              usb_output=describe_edges(segment, 'output', bit)))
            start = None
    return holds


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--timeout', type=float, default=120)
    ap.add_argument('--out', type=Path, required=True)
    args = ap.parse_args()
    usb.bind(usb.h, 'HidD_GetInputReport', [usb.P, usb.P, usb.U32], c.c_ubyte)
    bridge = Bridge()
    rows, gaps = [], []
    result = {}
    try:
        caps = bridge.command(20)
        slot = caps[9]
        size = struct.unpack_from('<H', caps, 6)[0]
        if slot >= caps[8] or size not in (480, 488) or not (int.from_bytes(caps[18:20], 'little') & 1):
            raise RuntimeError('No active connected profile')
        profile = b''.join(bridge.command(23, struct.pack('<BH', slot, offset)) for offset in range(0, size, 56))[:size]
        before = bridge.info()
        rates = {name: profile[64+index*16+7] for name, index in TARGETS.items()}
        print(json.dumps(dict(ready=True, active_slot=slot+1, saved_rates=rates)), flush=True)
        # perf_counter uses QPC on Windows, unlike the coarse clock used by
        # time.monotonic in some bundled Python versions.
        start = time.perf_counter()
        previous_state = None
        previous_time = start
        held_since, longest_hold = {}, dict.fromkeys(TARGETS, 0)
        released_since = None
        completed = False
        while time.perf_counter()-start < args.timeout:
            packet = bridge.command(30)
            physical = physical_mask(profile, packet)
            report = c.create_string_buffer(9)
            read_begin = time.perf_counter()
            if not usb.h.HidD_GetInputReport(bridge.handle, report, 9):
                raise c.WinError(c.get_last_error())
            now = time.perf_counter()
            stamp = (read_begin+now)/2-start
            gaps.append(now-previous_time)
            previous_time = now
            output = int.from_bytes(report.raw[1:3], 'little')
            state = (physical, output)
            if state != previous_state:
                rows.append(dict(seconds=stamp, physical=physical, output=output,
                                 source_sequence=int.from_bytes(packet[4:8], 'little'),
                                 source_time_us=int.from_bytes(packet[8:12], 'little'),
                                 raw_prefix=packet[12:20].hex(), report=report.raw[1:].hex()))
                previous_state = state
            for name, index in TARGETS.items():
                if physical & (1 << index):
                    held_since.setdefault(name, stamp)
                    longest_hold[name] = max(longest_hold[name], stamp-held_since[name])
                else:
                    held_since.pop(name, None)
            if physical == 0:
                if released_since is None:
                    released_since = now
            else:
                released_since = None
            if all(longest_hold[name] >= 6 for name in TARGETS):
                if released_since is not None and now-released_since >= 1:
                    completed = True
                    break
            time.sleep(.001)
        after = bridge.info()
        caps_after = bridge.command(20)
        result = dict(timestamp=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                      completed=completed, duration_seconds=time.perf_counter()-start,
                      active_slot=slot+1, saved_rates=rates,
                      profile_unchanged=bridge.command(23, struct.pack('<BH', slot, 0))[:4] == profile[:4]
                          and caps[28:32] == caps_after[28:32],
                      samples=len(gaps), median_sample_gap_ms=1000*statistics.median(gaps) if gaps else None,
                      max_sample_gap_ms=1000*max(gaps) if gaps else None,
                      before=before, after=after,
                      buttons={name: dict(physical=describe_edges(rows, 'physical', 1 << index),
                                          usb_output=describe_edges(rows, 'output', 1 << index),
                                          holds=describe_holds(rows, 1 << index))
                               for name, index in TARGETS.items()}, rows=rows,
                      note='USB GET_REPORT samples of last armed output; sampling gaps bound edge-time accuracy. Rates spanning separate holds include their pauses.')
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
        print(json.dumps({k: v for k, v in result.items() if k not in ('rows', 'before', 'after')}, indent=2), flush=True)
    finally:
        bridge.close()


if __name__ == '__main__':
    main()
