"""Exercise Pro Controller initialization on the locally identified probe only."""
import ctypes as c
import json
from pathlib import Path
import struct
import time
import inspect_usb as usb

for name in ('HidD_SetOutputReport', 'HidD_GetInputReport'):
    usb.bind(usb.h, name, [usb.P, usb.P, usb.U32], c.c_ubyte)
usb.bind(usb.k, 'WriteFile', [usb.P, usb.P, usb.U32, usb.P, usb.P], c.c_int)
winusb = c.WinDLL('winusb', use_last_error=True)
usb.bind(winusb, 'WinUsb_Initialize', [usb.P, usb.P], c.c_int)
usb.bind(winusb, 'WinUsb_Free', [usb.P], c.c_int)
usb.bind(winusb, 'WinUsb_WritePipe', [usb.P,c.c_ubyte,usb.P,usb.U32,usb.P,usb.P], c.c_int)
usb.bind(winusb, 'WinUsb_ReadPipe', [usb.P,c.c_ubyte,usb.P,usb.U32,usb.P,usb.P], c.c_int)
usb.bind(winusb, 'WinUsb_SetPipePolicy', [usb.P,c.c_ubyte,usb.U32,usb.U32,usb.P], c.c_int)
log = []
seen = set()
until = time.monotonic() + 88
last_pro_path = None
last_nso_path = None
while time.monotonic() < until:
    paths = usb.interfaces('4d1e55b2-f16f-11cf-88cb-001111000030')
    pro_paths = [p for p in paths if 'vid_057e&pid_2009' in p.lower()]
    if not pro_paths:
        last_pro_path = None
    for path in paths:
        if not any(x in path.lower() for x in ('vid_20d6&pid_a713','vid_0f0d&pid_0092','vid_057e&pid_2009','vid_057e&pid_2073')):
            continue
        if path not in seen:
            seen.add(path); log.append(dict(event='enumerated', path=path)); print(path, flush=True)
    if pro_paths and last_pro_path is None:
        path = pro_paths[0]
        handle = None
        try:
            handle = usb.open_device(path, 0xc0000000)
            serial = c.create_unicode_buffer(64)
            if not usb.h.HidD_GetSerialNumberString(handle, serial, c.sizeof(serial)) or serial.value != 'UC2PRO000001':
                raise RuntimeError('Refusing to write to a controller without this probe serial')
            def send(raw, interrupt=False):
                packet = c.create_string_buffer(raw.ljust(64,b'\0'), 64)
                if interrupt:
                    written = usb.U32()
                    ok = usb.k.WriteFile(handle, packet, 64, c.byref(written), None)
                    if not ok or written.value != 64:
                        raise c.WinError(c.get_last_error())
                elif not usb.h.HidD_SetOutputReport(handle, packet, 64):
                    raise c.WinError(c.get_last_error())
                time.sleep(.03)
                reply = c.create_string_buffer(64); reply[0] = b'\x81' if raw[0] == 0x80 else b'\x21'
                if not usb.h.HidD_GetInputReport(handle, reply, 64):
                    raise c.WinError(c.get_last_error())
                expected = raw[1] if raw[0] == 0x80 else raw[10]
                actual = reply.raw[1] if raw[0] == 0x80 else reply.raw[14]
                if actual != expected:
                    raise RuntimeError(f'Command reply mismatch: {reply.raw.hex()}')
                log.append(dict(event='command', interrupt_out=interrupt, command=raw.hex(), reply=reply.raw.hex()))
            for command in (1,2,3,4):
                send(bytes((0x80,command)), interrupt=command==2)
            send(bytes((1,))+b'\0'*9+bytes((2,)), interrupt=True)
            send(bytes((1,))+b'\0'*9+bytes((0x10,))+struct.pack('<IB',0x603d,18), interrupt=True)
            # Select full reports last; the PC then continuously polls this IN endpoint.
            packet=c.create_string_buffer((bytes((1,))+b'\0'*9+bytes((3,0x30))).ljust(64,b'\0'),64)
            if not usb.h.HidD_SetOutputReport(handle, packet,64):
                raise c.WinError(c.get_last_error())
            last_pro_path=path
            print('Pro Controller handshake and OUT/IN transfers passed', flush=True)
        except (OSError, RuntimeError) as e:
            log.append(dict(event='error', error=str(e)))
            print(str(e), flush=True)
            last_pro_path=path
        finally:
            if handle is not None:usb.k.CloseHandle(handle)
    nso_paths=usb.interfaces('f055869b-a439-4e70-9169-dc64616cf330')
    if not nso_paths:last_nso_path=None
    if nso_paths and last_nso_path is None:
        handle=None; interface=usb.P()
        try:
            path=nso_paths[0]
            if 'vid_057e&pid_2073' not in path.lower():raise RuntimeError('Unexpected NSO USB identity')
            handle=usb.k.CreateFileW(path,0xc0000000,3,None,3,0x40000000,None)
            if handle==usb.INVALID:raise c.WinError(c.get_last_error())
            if not winusb.WinUsb_Initialize(handle,c.byref(interface)):raise c.WinError(c.get_last_error())
            timeout=usb.U32(700)
            for pipe in (2,0x82):
                if not winusb.WinUsb_SetPipePolicy(interface,pipe,3,4,c.byref(timeout)):raise c.WinError(c.get_last_error())
            def bulk(command,subcommand,data=b''):
                raw=bytes((command,0x91,0,subcommand,0,len(data),0,0))+data
                packet=c.create_string_buffer(raw,len(raw));written=usb.U32()
                if not winusb.WinUsb_WritePipe(interface,2,packet,len(raw),c.byref(written),None):raise c.WinError(c.get_last_error())
                reply=c.create_string_buffer(128);received=usb.U32()
                if not winusb.WinUsb_ReadPipe(interface,0x82,reply,128,c.byref(received),None):raise c.WinError(c.get_last_error())
                response=reply.raw[:received.value]
                if response[:4]!=bytes((command,1,0,subcommand)) or response[5]!=0xf8:raise RuntimeError('NSO command response mismatch')
                log.append(dict(event='nso-command',command=raw.hex(),reply=response.hex()))
                return response
            bulk(3,13,bytes((1,0,2,0,0,0x23,0x50,2)))
            bulk(3,10,bytes((10,0,0,0)))
            response=bulk(2,4,bytes((64,0x7e,0,0))+struct.pack('<I',0x13080))
            if len(response)!=80:raise RuntimeError('NSO multi-packet response must be 80 bytes')
            last_nso_path=path
            print('NSO initialization and 80-byte bulk response passed',flush=True)
        except (OSError,RuntimeError) as e:
            log.append(dict(event='error',error=str(e)));print(str(e),flush=True);last_nso_path=nso_paths[0]
        finally:
            if interface:winusb.WinUsb_Free(interface)
            if handle is not None and handle!=usb.INVALID:usb.k.CloseHandle(handle)
    time.sleep(.2)
Path('captures/pc-probe-protocol-validation.json').write_text(json.dumps(log,indent=2)+'\n',encoding='utf-8')
if sum(x['event']=='command' for x in log)!=12 or sum(x['event']=='nso-command' for x in log)!=6 or any(x['event']=='error' for x in log):
    raise RuntimeError('Pro Controller and NSO profiles must pass the protocol checks')
