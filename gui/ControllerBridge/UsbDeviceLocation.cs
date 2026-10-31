using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ControllerBridge;

internal sealed record UsbDeviceLocation(string InstanceId, string Location)
{
    internal bool IsRp2350Boot => InstanceId.StartsWith(@"USB\VID_2E8A&PID_000F\", StringComparison.OrdinalIgnoreCase);
    internal bool IsPicoSdkUsb => InstanceId.StartsWith(@"USB\VID_2E8A&PID_0009\", StringComparison.OrdinalIgnoreCase);

    // The supplied factory board exposes Pico SDK CDC/reset USB, not our HID
    // transport. These IDs identify installation candidates, not board wiring;
    // programming still requires confirmation and a verified RP2350 BOOT disk.
    internal static List<UsbDeviceLocation> SelectUnconfigured(IEnumerable<UsbDeviceLocation> devices) => devices
        .Where(device => device.IsPicoSdkUsb || device.IsRp2350Boot)
        .DistinctBy(device => device.InstanceId, StringComparer.OrdinalIgnoreCase)
        .OrderBy(device => device.InstanceId, StringComparer.OrdinalIgnoreCase).ToList();

    internal static List<UsbDeviceLocation> FindUnconfigured() => SelectUnconfigured(
        Interfaces(new("a5dcbf10-6530-11d2-901f-00c04fb951ed"))
            .Select(item => ParentUsb(item.Node)).OfType<UsbDeviceLocation>());

    [StructLayout(LayoutKind.Sequential)] private struct InterfaceData { public int Size; public Guid Guid; public uint Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceData { public int Size; public Guid Guid; public uint DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Guid; public uint Id; }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceNumber { public uint Type, Number, Partition; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref InterfaceData data, IntPtr detail, uint length, out uint needed, ref DeviceData device);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_Device_IDW(uint node, StringBuilder id, uint length, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_DevNode_PropertyW(uint node, ref PropertyKey key, out uint type, byte[] buffer, ref uint length, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, uint inputLength, out DeviceNumber output, uint outputLength, out uint returned, IntPtr overlapped);

    private static List<(string Path, uint Node)> Interfaces(Guid guid)
    {
        IntPtr set = SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        List<(string, uint)> result = [];
        try
        {
            for (uint index = 0; ; index++)
            {
                InterfaceData data = new() { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data))
                {
                    if (Marshal.GetLastWin32Error() == 259) break;
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                DeviceData device = new() { Size = Marshal.SizeOf<DeviceData>() };
                SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out uint size, ref device);
                if (size < 8 || size > 65536) continue;
                IntPtr detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, size, out _, ref device))
                        result.Add((Marshal.PtrToStringUni(detail + 4) ?? "", device.DevInst));
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    private static UsbDeviceLocation? ParentUsb(uint node)
    {
        for (int depth = 0; depth < 12; depth++)
        {
            StringBuilder id = new(512);
            if (CM_Get_Device_IDW(node, id, (uint)id.Capacity, 0) != 0) return null;
            string text = id.ToString();
            if (text.StartsWith(@"USB\VID_", StringComparison.OrdinalIgnoreCase) && !text.Contains("&MI_", StringComparison.OrdinalIgnoreCase))
            {
                PropertyKey key = new() { Guid = new("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 37 };
                byte[] buffer = new byte[8192]; uint length = (uint)buffer.Length;
                if (CM_Get_DevNode_PropertyW(node, ref key, out uint type, buffer, ref length, 0) != 0 || type != 0x2012 || length < 4) return null;
                string location = Encoding.Unicode.GetString(buffer, 0, (int)length).Split('\0')[0];
                return location.Length == 0 ? null : new(text, location);
            }
            if (CM_Get_Parent(out node, node, 0) != 0) return null;
        }
        return null;
    }

    internal static UsbDeviceLocation? ForHid(string path)
    {
        foreach (var item in Interfaces(new("4d1e55b2-f16f-11cf-88cb-001111000030")))
            if (item.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) return ParentUsb(item.Node);
        return null;
    }

    private static DeviceNumber? Number(string path)
    {
        using SafeFileHandle handle = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        return !handle.IsInvalid && DeviceIoControl(handle, 0x2d1080, IntPtr.Zero, 0, out DeviceNumber number, 12, out uint count, IntPtr.Zero) && count >= 12
            ? number : null;
    }

    internal static UsbDeviceLocation? ForDrive(string root)
    {
        DeviceNumber? volume = Number(@"\\.\" + root.TrimEnd('\\'));
        if (volume is not { Type: 7 }) return null;
        foreach (var item in Interfaces(new("53f56307-b6bf-11d0-94f2-00a0c91efb8b")))
        {
            DeviceNumber? disk = Number(item.Path);
            if (disk is { Type: 7 } && disk.Value.Number == volume.Value.Number) return ParentUsb(item.Node);
        }
        return null;
    }
}
