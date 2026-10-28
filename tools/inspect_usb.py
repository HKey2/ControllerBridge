"""Read-only USB/HID inspection of the two selected game controllers on Windows.

Uses only USB hub GET information/descriptor IOCTLs and HID capability queries.
No reset, driver changes, firmware writes, SET_REPORT, or output transfers.
Run with an existing Python runtime; no third-party packages are required.
"""
import ctypes as c
from ctypes import wintypes as w
import datetime
import json
from pathlib import Path
import struct
import sys
import uuid

TARGETS = {(0x0AE4, 0x0703): "EGRET II mini panel", (0x33DD, 0x0013): "X68000 Z JOYCARD"}
k = c.WinDLL("kernel32", use_last_error=True)
s = c.WinDLL("setupapi", use_last_error=True)
h = c.WinDLL("hid", use_last_error=True)
P = c.c_void_p
U32 = c.c_uint32
U16 = c.c_uint16

class GUID(c.Structure):
    _fields_ = [("data", c.c_ubyte * 16)]
    def __init__(self, value):
        super().__init__()
        self.data[:] = uuid.UUID(value).bytes_le

class IFACE(c.Structure):
    _fields_ = [("cbSize", U32), ("guid", GUID), ("flags", U32), ("reserved", P)]

def bind(lib, name, args, ret):
    fn = getattr(lib, name)
    fn.argtypes, fn.restype = args, ret
    return fn

bind(s, "SetupDiGetClassDevsW", [P, w.LPCWSTR, P, U32], P)
bind(s, "SetupDiEnumDeviceInterfaces", [P, P, P, U32, P], w.BOOL)
bind(s, "SetupDiGetDeviceInterfaceDetailW", [P, P, P, U32, P, P], w.BOOL)
bind(s, "SetupDiDestroyDeviceInfoList", [P], w.BOOL)
bind(k, "CreateFileW", [w.LPCWSTR, U32, U32, P, U32, U32, P], P)
bind(k, "CloseHandle", [P], w.BOOL)
bind(k, "DeviceIoControl", [P, U32, P, U32, P, U32, P, P], w.BOOL)
bind(h, "HidD_GetPreparsedData", [P, P], c.c_ubyte)
bind(h, "HidD_FreePreparsedData", [P], c.c_ubyte)
bind(h, "HidP_GetCaps", [P, P], c.c_int32)
bind(h, "HidP_GetButtonCaps", [c.c_int, P, P, P], c.c_int32)
bind(h, "HidP_GetValueCaps", [c.c_int, P, P, P], c.c_int32)
for fn in ["HidD_GetManufacturerString", "HidD_GetProductString", "HidD_GetSerialNumberString"]:
    bind(h, fn, [P, P, U32], c.c_ubyte)
INVALID = c.c_void_p(-1).value

def interfaces(guid_text):
    guid = GUID(guid_text)
    devs = s.SetupDiGetClassDevsW(c.byref(guid), None, None, 0x12)
    if devs == INVALID:
        raise c.WinError(c.get_last_error())
    paths = []
    try:
        index = 0
        while True:
            iface = IFACE()
            iface.cbSize = c.sizeof(iface)
            if not s.SetupDiEnumDeviceInterfaces(devs, None, c.byref(guid), index, c.byref(iface)):
                if c.get_last_error() == 259:
                    break
                raise c.WinError(c.get_last_error())
            size = U32()
            s.SetupDiGetDeviceInterfaceDetailW(devs, c.byref(iface), None, 0, c.byref(size), None)
            buf = c.create_string_buffer(size.value)
            struct.pack_into("<I", buf, 0, 8 if c.sizeof(P) == 8 else 6)
            if not s.SetupDiGetDeviceInterfaceDetailW(devs, c.byref(iface), buf, size, None, None):
                raise c.WinError(c.get_last_error())
            paths.append(c.wstring_at(c.addressof(buf) + 4))
            index += 1
    finally:
        s.SetupDiDestroyDeviceInfoList(devs)
    return paths

def open_device(path, access=0):
    handle = k.CreateFileW(path, access, 3, None, 3, 0, None)
    if handle == INVALID:
        raise c.WinError(c.get_last_error())
    return handle

def ioctl(handle, function, initial, size):
    buf = c.create_string_buffer(size)
    buf[:len(initial)] = initial
    received = U32()
    if not k.DeviceIoControl(handle, (0x22 << 16) | (function << 2), buf, size, buf, size, c.byref(received), None):
        raise c.WinError(c.get_last_error())
    return buf.raw[:received.value]

def get_descriptor(handle, port, dtype, index, lang, length):
    setup = struct.pack("<IBBHHH", port, 0x80, 6, (dtype << 8) | index, lang, length)
    return ioctl(handle, 260, setup, 12 + length)[12:]

def u16(b, offset):
    return struct.unpack_from("<H", b, offset)[0]

def parse_configuration(data):
    result = []
    pos = 0
    while pos + 2 <= len(data):
        n, typ = data[pos:pos+2]
        if n < 2 or pos + n > len(data):
            raise ValueError("Malformed/truncated descriptor at " + str(pos))
        b = data[pos:pos+n]
        d = {"offset": pos, "type": typ, "hex": b.hex(" ")}
        if typ == 2 and n >= 9:
            d.update(total_length=u16(b, 2), interfaces=b[4], configuration=b[5], attributes=b[7], max_power_ma=b[8]*2)
        elif typ == 4 and n >= 9:
            d.update(interface=b[2], alternate=b[3], endpoints=b[4], class_code=b[5], subclass=b[6], protocol=b[7])
        elif typ == 5 and n >= 7:
            d.update(endpoint=f"0x{b[2]:02X}", direction="IN" if b[2]&128 else "OUT", transfer_type=b[3]&3, max_packet=u16(b, 4), interval=b[6])
        elif typ == 0x21 and n >= 9:
            d.update(hid_bcd=f"0x{u16(b,2):04X}", country=b[4], report_descriptor_length=u16(b,7))
        result.append(d)
        pos += n
    return result

def inspect_usb():
    devices, errors = [], []
    for path in interfaces("f18a0e88-c30c-11d0-8815-00a0c906bed8"):
        handle = None
        try:
            handle = open_device(path)
            info = ioctl(handle, 258, bytes(4), 256)
            for port in range(1, info[6]+1):
                conn = ioctl(handle, 274, struct.pack("<I", port), 4096)
                if len(conn) < 35:
                    continue
                b = conn[4:22]
                ids = (u16(b,8), u16(b,10))
                if ids not in TARGETS:
                    continue
                row = {"label": TARGETS[ids], "vid": f"{ids[0]:04X}", "pid": f"{ids[1]:04X}", "hub": path, "port": port,
                       "speed_code": conn[23], "speed": {0:"Low Speed (1.5 Mbit/s)",1:"Full Speed (12 Mbit/s)",2:"High Speed (480 Mbit/s)"}.get(conn[23], "unknown"),
                       "connection_status": struct.unpack_from("<I",conn,31)[0], "device_descriptor_hex": b.hex(" "),
                       "bcdUSB": f"0x{u16(b,2):04X}", "device_class": b[4], "ep0_max_packet": b[7], "bcdDevice": f"0x{u16(b,12):04X}", "configurations": b[17]}
                row["strings"] = {}
                try:
                    langs = get_descriptor(handle,port,3,0,0,255)
                    row["string_language_descriptor_hex"] = langs.hex(" ")
                    lang = u16(langs,2) if len(langs) >= 4 else 0x0409
                    for key, idx in zip(["manufacturer","product","serial"],b[14:17]):
                        if idx:
                            data = get_descriptor(handle,port,3,idx,lang,255)
                            row["strings"][key] = {"index":idx,"hex":data.hex(" "),"text":data[2:data[0]].decode("utf-16-le",errors="replace")}
                except OSError as e:
                    row["string_error"] = str(e)
                configs = []
                for idx in range(b[17]):
                    first = get_descriptor(handle,port,2,idx,0,9)
                    data = get_descriptor(handle,port,2,idx,0,u16(first,2))
                    configs.append({"index":idx,"hex":data.hex(" "),"descriptors":parse_configuration(data)})
                row["configuration_descriptors"] = configs
                row["hid_report_descriptors"] = []
                for config in configs:
                    interface = 0
                    for descriptor in config["descriptors"]:
                        if descriptor["type"] == 4:
                            interface = descriptor["interface"]
                        if descriptor["type"] != 0x21:
                            continue
                        report = {"interface":interface,"expected_length":descriptor["report_descriptor_length"],
                                  "method":"Hub GET_DESCRIPTOR; Windows forces bmRequestType=0x80 (device recipient), so some HID devices reject this route"}
                        try:
                            raw = get_descriptor(handle,port,0x22,0,interface,report["expected_length"])
                            report.update(actual_length=len(raw),hex=raw.hex(" "))
                        except OSError as e:
                            report["error"] = str(e)
                        row["hid_report_descriptors"].append(report)
                devices.append(row)
        except OSError as e:
            errors.append({"hub":path,"error":str(e)})
        finally:
            if handle is not None:
                k.CloseHandle(handle)
    return devices, errors

def inspect_hid():
    rows = []
    for path in interfaces("4d1e55b2-f16f-11cf-88cb-001111000030"):
        match = next((ids for ids in TARGETS if f"vid_{ids[0]:04x}&pid_{ids[1]:04x}" in path.lower()), None)
        if match is None:
            continue
        handle, pp = None, P()
        row = {"label":TARGETS[match],"path":path,"source":"Windows parsed HID capabilities, not original raw report descriptor"}
        try:
            handle = open_device(path)
            for field, fn in [("manufacturer","HidD_GetManufacturerString"),("product","HidD_GetProductString"),("serial","HidD_GetSerialNumberString")]:
                buf = c.create_unicode_buffer(256)
                if getattr(h,fn)(handle,buf,c.sizeof(buf)):
                    row[field] = buf.value
            if not h.HidD_GetPreparsedData(handle,c.byref(pp)):
                raise c.WinError(c.get_last_error())
            caps = c.create_string_buffer(64)
            if h.HidP_GetCaps(pp,caps) != 0x110000:
                raise RuntimeError("HidP_GetCaps failed")
            v = struct.unpack("<32H",caps.raw)
            row.update(usage=v[0],usage_page=v[1],input_report_bytes_windows=v[2],output_report_bytes_windows=v[3],feature_report_bytes_windows=v[4])
            row["reports"] = {}
            for typ, name, button_n, value_n in [(0,"input",v[23],v[24]),(1,"output",v[26],v[27]),(2,"feature",v[29],v[30])]:
                groups = {}
                for kind,n,fn in [("buttons",button_n,h.HidP_GetButtonCaps),("values",value_n,h.HidP_GetValueCaps)]:
                    items = []
                    if n:
                        buf = c.create_string_buffer(n*72)
                        count = U16(n)
                        if fn(typ,buf,c.byref(count),pp) != 0x110000:
                            raise RuntimeError("HID caps query failed")
                        for i in range(count.value):
                            b = buf.raw[i*72:(i+1)*72]
                            item = {"usage_page":u16(b,0),"report_id":b[2],"bit_field":u16(b,4),"is_range":bool(b[12]),"absolute":bool(b[15]),"usage_min":u16(b,56),"usage_max":u16(b,58) if b[12] else u16(b,56)}
                            if kind == "values":
                                item.update(bit_size=u16(b,18),report_count=u16(b,20),logical_min=struct.unpack_from("<i",b,40)[0],logical_max=struct.unpack_from("<i",b,44)[0],has_null=bool(b[16]))
                            items.append(item)
                    groups[kind] = items
                row["reports"][name] = groups
        except (OSError,RuntimeError) as e:
            row["error"] = str(e)
        finally:
            if pp.value:
                h.HidD_FreePreparsedData(pp)
            if handle is not None:
                k.CloseHandle(handle)
        rows.append(row)
    return rows

if __name__ == "__main__":
    usb, errors = inspect_usb()
    result = {"captured_at":datetime.datetime.now(datetime.timezone(datetime.timedelta(hours=9))).isoformat(),"usb_devices":usb,"hid_collections":inspect_hid(),"errors":errors}
    text = json.dumps(result,ensure_ascii=False,indent=2)
    if len(sys.argv)>1:
        out = Path(sys.argv[1])
        out.parent.mkdir(parents=True,exist_ok=True)
        out.write_text(text+"\n",encoding="utf-8")
    print(text)
