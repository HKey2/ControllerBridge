using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ControllerBridge;

// The hub owns EP0 even while xusb22 owns the whole game controller. Its
// documented descriptor IOCTL reaches an unadvertised string protocol without
// replacing that driver or adding an interface to the gamepad configuration.
internal sealed class XInputPortal : IDisposable
{
    private readonly SafeFileHandle hub;
    private readonly string hubPath;
    private readonly uint port;
    private readonly string mutexName;
    private byte[] nonce = [];
    private byte[]? deviceId;
    internal string DevicePath => hubPath + "#" + port + "#" + Convert.ToHexString(deviceId ?? []);
    internal UsbDeviceLocation? UsbLocation => UsbDeviceLocation.ForHubPort(hubPath, port);
    internal byte Sequence { get; private set; }

    private XInputPortal(string path, uint port)
    {
        hubPath = path; this.port = port;
        hub = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (hub.IsInvalid) { hub.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        string key = path.ToUpperInvariant() + "#" + port;
        mutexName = @"Local\ControllerBridge.XUSB." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, uint inputLength, [Out] byte[] output, uint outputLength, out uint returned, IntPtr overlapped);

    private static byte[] Ioctl(SafeFileHandle handle, uint function, byte[] input, int capacity)
    {
        byte[] output = new byte[capacity];
        if (!DeviceIoControl(handle, (0x22u << 16) | (function << 2), input, (uint)input.Length, output, (uint)output.Length, out uint count, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("デバイスのUSB通信が切れました。USB-Cの接続を確認してください。", "USB communication was interrupted. Check the USB-C connection."));
        if (count > output.Length) throw new InvalidDataException("USB hub response length");
        return output[..(int)count];
    }

    private byte[] Descriptor(byte index, ushort value, ushort length)
    {
        byte[] request = new byte[12 + length];
        Wire.W32(request, 0, port); request[4] = 0x80; request[5] = 6;
        request[6] = index; request[7] = 3;
        request[8] = (byte)value; request[9] = (byte)(value >> 8);
        request[10] = (byte)length; request[11] = (byte)(length >> 8);
        byte[] result = Ioctl(hub, 260, request, request.Length);
        if (result.Length != request.Length || result[12] != length || result[13] != 3)
            throw new InvalidDataException("XUSB configuration descriptor");
        return result[12..];
    }

    private byte[] Request(byte index, ushort value, ushort length)
    {
        byte[] raw = Descriptor(index, value, length), result = new byte[(length - 2) / 2];
        for (int i = 0; i < result.Length; i++)
        {
            if (raw[3 + i * 2] != 1) throw new InvalidDataException("XUSB configuration encoding");
            result[i] = raw[2 + i * 2];
        }
        return result;
    }

    private static ushort Crc(ReadOnlySpan<byte> bytes, ushort crc = 0xffff)
    {
        foreach (byte value in bytes)
        {
            crc ^= (ushort)(value << 8);
            for (int bit = 0; bit < 8; bit++) crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }

    private void OpenSession()
    {
        nonce = RandomNumberGenerator.GetBytes(4);
        if (!Request(0xe1, Wire.U16(nonce, 0), 10).AsSpan().SequenceEqual("CBL1"u8)) throw new InvalidDataException("XUSB configuration handshake");
        byte[] reply = Request(0xe2, Wire.U16(nonce, 2), 34);
        if (!reply.AsSpan(0, 4).SequenceEqual("CBH1"u8) || !reply.AsSpan(4, 4).SequenceEqual(nonce))
            throw new InvalidDataException("XUSB configuration nonce");
        if (deviceId is not null && !reply.AsSpan(8, 8).SequenceEqual(deviceId))
            throw new IOException(L.T("接続先の基板が変わりました。再接続して設定を読み直してください。", "The connected board changed. Reconnect and reload its settings."));
        deviceId ??= reply[8..16];
    }

    private sealed class Transaction : IDisposable
    {
        private readonly Mutex mutex;
        internal Transaction(string name)
        {
            mutex = new(false, name);
            try
            {
                try
                {
                    if (!mutex.WaitOne(5000)) throw new IOException(L.T("別のツールがデバイスを使用しています。操作が終わってから再試行してください。", "Another tool is using the device. Retry after it finishes."));
                }
                catch (AbandonedMutexException) { /* A new nonce replaces the interrupted transaction. */ }
            }
            catch { mutex.Dispose(); throw; }
        }
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }

    internal IDisposable BeginCommand() => new Transaction(mutexName);

    internal void Send(byte[] command)
    {
        if (command.Length != 64 || !command.AsSpan(0, 5).SequenceEqual("UC2C\x01"u8)) throw new ArgumentException("XUSB configuration command");
        OpenSession();
        byte[] expected = [.. nonce, command[5], command[6]];
        if (!Request(0xe3, Wire.U16(command, 5), 14).AsSpan().SequenceEqual(expected)) throw new InvalidDataException("XUSB configuration begin");
        // BEGIN clears all payload bytes, so zero pairs need no USB transfer.
        for (int offset = 8; offset < 64; offset += 2)
        {
            ushort word = Wire.U16(command, offset);
            if (word != 0 && !Request((byte)(0x80 + (offset - 8) / 2), word, 10).AsSpan().SequenceEqual(nonce))
                throw new InvalidDataException("XUSB configuration payload");
        }
        if (!Request(0xe4, Crc(command, Crc(nonce)), 14).AsSpan().SequenceEqual(expected))
            throw new InvalidDataException("XUSB configuration commit");
    }

    internal byte[] Receive()
    {
        byte[] reply = Request(0xe5, Crc(nonce), 138);
        if (!reply.AsSpan(0, 4).SequenceEqual(nonce)) throw new InvalidDataException("XUSB configuration response");
        return reply[4..];
    }

    internal static List<XInputPortal> Find()
    {
        List<XInputPortal> found = [];
        try
        {
            foreach (var item in UsbDeviceLocation.Interfaces(new("f18a0e88-c30c-11d0-8815-00a0c906bed8")))
            {
                using SafeFileHandle hub = CreateFileW(item.Path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (hub.IsInvalid) continue;
                byte[] info;
                try { info = Ioctl(hub, 258, new byte[256], 256); }
                catch (Win32Exception) { continue; }
                if (info.Length < 7) continue;
                for (uint port = 1; port <= info[6]; port++)
                {
                    XInputPortal? candidate = null;
                    try
                    {
                        byte[] query = new byte[4096]; Wire.W32(query, 0, port);
                        byte[] node = Ioctl(hub, 274, query, query.Length);
                        if (node.Length < 35 || Wire.U32(node, 31) != 1 || Wire.U16(node, 12) != 0x045e || Wire.U16(node, 14) != 0x028e) continue;
                        candidate = new(item.Path, port);
                        byte[] identity = candidate.Descriptor(0xe0, 0x4342, 34);
                        if (!identity.AsSpan(2).SequenceEqual(Encoding.Unicode.GetBytes("CBXUSB-PORTAL-V1"))) continue;
                        using (candidate.BeginCommand())
                        {
                            candidate.OpenSession();
                            byte[] response = candidate.Receive();
                            if (!response.AsSpan(0, 5).SequenceEqual("UC2R\x01"u8)) continue;
                            candidate.Sequence = response[6];
                        }
                        found.Add(candidate); candidate = null;
                    }
                    catch (Win32Exception) { /* A real Xbox controller does not implement this protocol. */ }
                    catch (InvalidDataException) { }
                    finally { candidate?.Dispose(); }
                }
            }
            return found;
        }
        catch { foreach (XInputPortal portal in found) portal.Dispose(); throw; }
    }

    public void Dispose() => hub.Dispose();
}
