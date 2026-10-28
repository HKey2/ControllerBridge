"""Extract exact descriptors and scoped measurements from the EGRET UART/CDC logs.

No device access. Run after capture-diagnostic.ps1; only the captures directory
is written. Unchanged input reports are counted in STATS, not printed as REPORT.
"""
import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CAPTURES = ROOT / "captures"
LOG_NAMES = (
    "egret-enumeration.log",
    "egret-input-10ms-and-1ms.log",
    "egret-input-1ms-active.log",
)
DESC = re.compile(
    r"^DESC type=([0-9a-f]+) iface=(\d+) offset=(\d+) total=(\d+) hex=([0-9a-f]+)$"
)


def numbers(line):
    return {key: int(value) for key, value in re.findall(r"(\w+)=(\d+)(?:\s|$)", line)}


def read_log(path):
    descriptors, pending, stats, reports, states = {}, {}, [], [], []
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        match = DESC.match(line)
        if match:
            kind, interface, offset, total, payload = match.groups()
            key = (int(kind, 16), int(interface))
            offset, total = int(offset), int(total)
            if offset == 0:
                pending[key] = bytearray()
            assert key in pending and offset == len(pending[key]), line
            pending[key].extend(bytes.fromhex(payload))
            assert len(pending[key]) <= total, line
            if len(pending[key]) == total:
                data = bytes(pending.pop(key))
                assert key not in descriptors or descriptors[key] == data
                descriptors[key] = data
        elif line.startswith("STATS "):
            stats.append(numbers(line))
        elif line.startswith("STATE "):
            state = numbers(line)
            state["name"] = re.search(r"name=(\w+)", line).group(1)
            states.append(state)
        elif line.startswith("REPORT "):
            report = numbers(line)
            report["buttons"] = int(re.search(r"buttons=([0-9a-f]+)", line).group(1), 16)
            report["hex"] = re.search(r"hex=([0-9a-f]+)", line).group(1)
            assert len(bytes.fromhex(report["hex"])) == report["len"] == 4
            reports.append(report)
    assert not pending, f"Incomplete descriptor in {path}"
    periods = {}
    for before, after in zip(stats, stats[1:]):
        # Exclude windows containing mode/re-enumeration changes.
        if any(before["us"] < s["us"] <= after["us"] for s in states):
            continue
        if (before["interval_ms"], before["generation"]) != (after["interval_ms"], after["generation"]):
            continue
        delta_us = after["us"] - before["us"]
        assert 0 < delta_us < 2_000_000, "Counter wrap/gap needs separate treatment"
        interval = str(after["interval_ms"])
        period = periods.setdefault(interval, {"windows": 0, "duration_us": 0, "reports": 0, "window_report_counts": []})
        count = after["reports"] - before["reports"]
        assert count >= 0
        period["windows"] += 1
        period["duration_us"] += delta_us
        period["reports"] += count
        period["window_report_counts"].append(count)
    for period in periods.values():
        period["reports_per_second"] = round(period["reports"] * 1_000_000 / period["duration_us"], 3)
        counts = period.pop("window_report_counts")
        period["min_reports_per_window"], period["max_reports_per_window"] = min(counts), max(counts)
    mask = 0
    for report in reports:
        mask |= report["buttons"]
    return descriptors, {
        "log": path.name,
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "statistics_samples": len(stats),
        "first_statistics": stats[0] if stats else None,
        "last_statistics": stats[-1] if stats else None,
        "stable_interval_measurements": periods,
        "printed_report_lines": len(reports),
        "observed_axis_pairs": sorted({(r["x"], r["y"]) for r in reports}),
        "observed_button_usages": [i + 1 for i in range(12) if mask & (1 << i)],
        "counter_maxima": {key: max(s[key] for s in stats) for key in ("errors", "stalls", "queue_drop", "late_frames", "frame_us_max")} if stats else {},
    }


def main():
    all_descriptors, logs = {}, []
    for name in LOG_NAMES:
        descriptors, summary = read_log(CAPTURES / name)
        for key, data in descriptors.items():
            assert key not in all_descriptors or all_descriptors[key] == data
            all_descriptors[key] = data
        logs.append(summary)
    for kind, filename in ((1, "0ae4-0703-device.bin"), (2, "0ae4-0703-configuration.bin")):
        assert all_descriptors[kind, 0] == (CAPTURES / filename).read_bytes(), filename
    report = all_descriptors[0x22, 0]
    assert len(report) == 49
    (CAPTURES / "0ae4-0703-hid-report.bin").write_bytes(report)
    (CAPTURES / "0ae4-0703-hid-report.hex").write_text(report.hex(" ") + "\n", encoding="ascii")
    result = {
        "board": "Waveshare RP2350-USB-A / RP2350 A2 / R13 retained",
        "firmware": "egret_diagnostic 0.1.0",
        "device": "0ae4:0703",
        "device_and_configuration_match_pc_capture": True,
        "hid_report_descriptor": {"bytes": len(report), "hex": report.hex(), "sha256": hashlib.sha256(report).hexdigest()},
        "logs": logs,
        "limits": [
            "Statistics count successful USB input reports, not physical switch scans or end-to-end latency.",
            "REPORT lines show changed state plus snapshots after console open/dump; they are not a lossless record of every USB transaction.",
            "No independent physical-input reference was recorded; zero error/drop counters do not prove zero missed physical inputs.",
            "1 ms is the requested poll interval. The cause of approximately 500 successful reports/s has not been measured at the wire level.",
            "Each log covers its own console recording window. Operations outside those windows are not reconstructed.",
        ],
    }
    target = CAPTURES / "egret-diagnostic-summary.json"
    target.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
