using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ControllerBridge;

// Acceptance test of the OS mouse path, with volatile firmware timing capture. Filter by this development
// device before recording anything; other system mice are never logged.
internal sealed class MouseCheck : Form
{
    private readonly string directory;
    private readonly int seconds;
    private readonly Stopwatch clock = new();
    private readonly System.Windows.Forms.Timer finish = new();
    private readonly List<Sample> samples = [];
    private readonly List<PadSample> padSamples = [];
    private readonly Dictionary<IntPtr, bool> devices = [];
    private HidBridge? bridge;
    private byte[]? before, mouseProfile;
    private bool recording;
    private bool combined;
    private bool gamepadButtons;
    private string? lastPad;
    private uint padReports,unknownPadReports;
    public Exception? Failure { get; private set; }
    private sealed record Sample(double Seconds, ushort Flags, ushort Buttons, int X, int Y);
    private sealed record PadSample(double Seconds, string Report, ushort Buttons);
    [StructLayout(LayoutKind.Sequential)] private struct RawDevice { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetRawInputDeviceInfoW(IntPtr device, uint command, StringBuilder? data, ref uint size);
    public MouseCheck(string directory, int seconds)
    {
        if (seconds is < 5 or > 300) throw new ArgumentOutOfRangeException(nameof(seconds));
        this.directory = Path.GetFullPath(directory); this.seconds = seconds;
        Directory.CreateDirectory(this.directory); ShowInTaskbar = false; Opacity = 0; Size = new(1, 1);
        Shown += async (_, _) =>
        {
            try
            {
                bridge = await HidBridge.ConnectAsync(); before = await bridge.RawAsync(36);
                if (!before.AsSpan(0, 4).SequenceEqual("MSE1"u8) || (before[4] & 1) == 0) throw new IOException("マウス出力モードではありません。");
                combined = (before[4] & 64) != 0;
                gamepadButtons = (before[4] & 128) != 0;
                mouseProfile = await bridge.RawAsync(37);
                RawDevice[] devices = combined ? [new() { Page = 1, Usage = 2, Flags = 0x100, Target = Handle }, new() { Page = 1, Usage = 5, Flags = 0x100, Target = Handle }] :
                    [new() { Page = 1, Usage = 2, Flags = 0x100, Target = Handle }];
                if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawDevice>())) throw new Win32Exception(Marshal.GetLastWin32Error());
                byte[] configuration = await bridge.RawAsync(2);
                byte[] measure = new byte[5]; Wire.W32(measure, 0, (uint)(seconds * 1000)); await bridge.RawAsync(5, measure);
                // Let the firmware's configured warmup finish before asking for
                // movement. The final idle period also covers capture boundaries.
                await Task.Delay(checked((int)Wire.U32(configuration, 36) + 250));
                clock.Start(); recording = true; finish.Interval = seconds * 1000; finish.Start();
                File.WriteAllText(Path.Combine(this.directory, "ready.txt"), combined ? "Recording ControllerBridge mouse and gamepad input." : "Recording ControllerBridge mouse input only.");
            }
            catch (Exception ex) { Failure = ex; Close(); }
        };
        finish.Tick += async (_, _) =>
        {
            finish.Stop(); recording = false;
            RegisterRawInputDevices([new() { Page = 1, Usage = 2, Flags = 1 }], 1, (uint)Marshal.SizeOf<RawDevice>());
            if (combined) RegisterRawInputDevices([new() { Page = 1, Usage = 5, Flags = 1 }], 1, (uint)Marshal.SizeOf<RawDevice>());
            try
            {
                await Task.Delay(500);
                byte[] after = await bridge!.RawAsync(36), info = await bridge.RawAsync(1);
                byte[] snapshotInfo = await bridge.RawAsync(7); uint length = Wire.U32(snapshotInfo, 0);
                if (length is not (3552 or 3688 or 3784) || Wire.U32(snapshotInfo, 4) != 0x20000) throw new IOException("Unknown timing snapshot format");
                byte[] snapshot = new byte[length];
                for (int offset = 0; offset < snapshot.Length; offset += 56)
                {
                    byte[] arg = new byte[4]; Wire.W32(arg, 0, (uint)offset);
                    (await bridge.RawAsync(8, arg)).AsSpan(0, Math.Min(56, snapshot.Length - offset)).CopyTo(snapshot.AsSpan(offset));
                }
                File.WriteAllBytes(Path.Combine(this.directory, "timing.bin"), snapshot);
                File.WriteAllText(Path.Combine(this.directory, "mouse-check.json"), JsonSerializer.Serialize(new
                {
                    timestamp = DateTimeOffset.Now, seconds, records = samples.Count,
                    xTotal = samples.Sum(s => (long)s.X), yTotal = samples.Sum(s => (long)s.Y),
                    leftDown = samples.Count(s => (s.Buttons & 1) != 0), leftUp = samples.Count(s => (s.Buttons & 2) != 0),
                    rightDown = samples.Count(s => (s.Buttons & 4) != 0), rightUp = samples.Count(s => (s.Buttons & 8) != 0),
                    allRelative = samples.All(s => (s.Flags & 1) == 0),
                    combined, gamepadButtons, padReports, unknownPadReports,
                    padButtonsSeen = ButtonNames.Where((name, i) => padSamples.Any(s => (s.Buttons & (1 << i)) != 0)).ToArray(),
                    padButtonEdges = ButtonNames.Select((name, i) => new { name, edges = CountEdges(1 << i) }).ToDictionary(e => e.name, e => e.edges),
                    onlyValidPadButtons = padSamples.All(s => (s.Buttons & ~0x3fff) == 0),
                    navigationSeen = new[] { (Mask: 256, Name: "MINUS"), (Mask: 512, Name: "PLUS"), (Mask: 4096, Name: "HOME") }
                        .Where(b => padSamples.Any(s => (s.Buttons & b.Mask) != 0)).Select(b => b.Name).ToArray(),
                    onlyNavigationButtons = padSamples.All(s => (s.Buttons & ~0x1300) == 0), padSamples,
                    before = Convert.ToHexString(before!), after = Convert.ToHexString(after), mouseProfile = Convert.ToHexString(mouseProfile!), info = Convert.ToHexString(info), samples
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { Failure = ex; }
            finally { bridge?.Dispose(); bridge = null; Close(); }
        };
        FormClosed += (_, _) => { finish.Dispose(); bridge?.Dispose(); };
    }
    private static readonly string[] ButtonNames = ["Y", "B", "A", "X", "L", "R", "ZL", "ZR", "MINUS", "PLUS", "LSTICK", "RSTICK", "HOME", "CAPTURE"];
    private object CountEdges(int mask)
    {
        int down = 0, up = 0; bool previous = false;
        foreach (PadSample sample in padSamples)
        {
            bool pressed = (sample.Buttons & mask) != 0;
            if (pressed != previous) { if (pressed) down++; else up++; }
            previous = pressed;
        }
        return new { down, up };
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0xff && recording)
        {
            uint size = 0, header = (uint)(8 + 2 * IntPtr.Size);
            if (GetRawInputData(message.LParam, 0x10000003, IntPtr.Zero, ref size, header) == 0 && size >= header + 8 && size <= 4096)
            {
                IntPtr data = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (GetRawInputData(message.LParam, 0x10000003, data, ref size, header) == size)
                    {
                        IntPtr device = Marshal.ReadIntPtr(data, 8);
                        if (!devices.TryGetValue(device, out bool selected))
                        {
                            uint chars = 0; GetRawInputDeviceInfoW(device, 0x20000007, null, ref chars);
                            if (chars is > 0 and < 4096)
                            {
                                StringBuilder path = new((int)chars + 1);
                                if (GetRawInputDeviceInfoW(device, 0x20000007, path, ref chars) != uint.MaxValue)
                                    selected = path.ToString().Contains(combined ? "vid_20d6&pid_a713&mi_" : "vid_cafe&pid_4004", StringComparison.OrdinalIgnoreCase);
                            }
                            devices[device] = selected;
                        }
                        int type = Marshal.ReadInt32(data);
                        if (selected && type == 0 && size >= header + 24)
                        {
                            int offset = (int)header;
                            ushort flags = (ushort)Marshal.ReadInt16(data, offset), buttons = (ushort)Marshal.ReadInt16(data, offset + 4);
                            int x = Marshal.ReadInt32(data, offset + 12), y = Marshal.ReadInt32(data, offset + 16);
                            if (x != 0 || y != 0 || buttons != 0) samples.Add(new(clock.Elapsed.TotalSeconds, flags, buttons, x, y));
                        }
                        else if (selected && combined && type == 2)
                        {
                            int reportSize = Marshal.ReadInt32(data, (int)header), count = Marshal.ReadInt32(data, (int)header + 4);
                            if (reportSize is not (8 or 9) || count < 1 || (long)header + 8 + (long)count * reportSize > size) unknownPadReports++;
                            else for (int i = 0; i < count; i++)
                            {
                                byte[] report = new byte[reportSize]; Marshal.Copy(data + (int)header + 8 + i * reportSize, report, 0, reportSize);
                                int prefix = reportSize == 9 ? 1 : 0;
                                if (prefix == 1 && report[0] != 0) { unknownPadReports++; continue; }
                                padReports++; string wire = Convert.ToHexString(report.AsSpan(prefix));
                                if (wire == lastPad) continue;
                                lastPad = wire; padSamples.Add(new(clock.Elapsed.TotalSeconds, wire, (ushort)(report[prefix] | report[prefix + 1] << 8)));
                            }
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(data); }
            }
        }
        base.WndProc(ref message);
    }
}
