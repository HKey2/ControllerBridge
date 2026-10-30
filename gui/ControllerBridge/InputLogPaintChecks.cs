using System.Runtime.InteropServices;
using System.Text.Json;

namespace ControllerBridge;

internal sealed partial class InputLogForm
{
    private sealed class PaintProbe : NativeWindow, IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetUpdateRect(IntPtr window, out Rect rect, bool erase);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        private readonly Control control;
        internal int Paints, FullPaints, Erases, CountChanges, Scrolls, EnsureVisibleCalls;
        internal long PaintedArea;
        internal readonly List<long> RequestedCountFlags = [];
        internal PaintProbe(Control control) { this.control = control; AssignHandle(control.Handle); }
        protected override void WndProc(ref Message message)
        {
            switch (message.Msg)
            {
                case 0xf:
                    if (GetUpdateRect(Handle, out Rect area, false))
                    {
                        Paints++; PaintedArea += (long)(area.Right - area.Left) * (area.Bottom - area.Top);
                        if (area.Left <= 0 && area.Top <= 0 && area.Right >= control.ClientSize.Width && area.Bottom >= control.ClientSize.Height) FullPaints++;
                    }
                    break;
                case 0x14: Erases++; break;
                case 0x102f: CountChanges++; RequestedCountFlags.Add(message.LParam.ToInt64()); break;
                case 0x1014: Scrolls++; break;
                case 0x1013: EnsureVisibleCalls++; break;
            }
            base.WndProc(ref message);
        }
        internal void Reset() { Paints = FullPaints = Erases = CountChanges = Scrolls = EnsureVisibleCalls = 0; PaintedArea = 0; RequestedCountFlags.Clear(); }
        public void Dispose() => ReleaseHandle();
    }

    internal static void RunPaintChecks(string directory)
    {
        Directory.CreateDirectory(directory);
        List<object> measurements = [];
        List<string> checks = [];
        void Check(bool condition, string description) { if (!condition) throw new InvalidDataException("Input log painting: " + description); checks.Add(description); }
        foreach (string scenario in new[] { "visible-append", "offscreen-append", "follow-append", "notice-append", "full-ring", "idle" })
        {
            InputField axis = new() { Index = 0, Bits = 16, UsagePage = 1, Usage = 0x30, Minimum = -32768, Maximum = 32767, Valid = true };
            Capabilities caps = new(255, 1, 0x1234, 1, 1, 5, 1, 1, 1, 0, 1, 5);
            using InputLogForm form = new(caps, [axis]) { Opacity = 0, ShowInTaskbar = false };
            form.Show(); form.refresh.Stop();
            form.follow.Checked = scenario is "follow-append" or "full-ring";
            int initial = scenario == "full-ring" ? 2100 : scenario is "offscreen-append" or "follow-append" ? 60 : 1;
            for (int i = 0; i < initial; i++) form.Observe(axis, new(0, i, (uint)i, false));
            if (scenario == "notice-append") form.buffer.Note("A retained notice / 保持した通知");
            form.Render(); Application.DoEvents(); form.view.Update();
            using PaintProbe probe = new(form.view);
            long nativeStyles = PaintProbe.SendMessageW(form.view.Handle, 0x1037, IntPtr.Zero, IntPtr.Zero).ToInt64();
            int originalTop = form.view.TopItem?.Index ?? 0;
            int sizeChanges = 0, textChanges = 0, layoutChanges = 0;
            form.view.ClientSizeChanged += (_, _) => sizeChanges++;
            form.status.TextChanged += (_, _) => textChanges++;
            form.Controls[0].Layout += (_, _) => layoutChanges++;
            probe.Reset();
            for (int tick = 0; tick < 12; tick++)
            {
                if (scenario != "idle") form.Observe(axis, new(0, tick + 10000, (uint)(tick + 10000), false));
                form.Render(); form.view.Update(); form.status.Update(); Application.DoEvents();
            }
            measurements.Add(new { scenario, ticks = 12, nativeDoubleBuffer = (nativeStyles & 0x10000) != 0,
                probe.Paints, probe.FullPaints, probe.Erases, probe.PaintedArea, probe.CountChanges, probe.RequestedCountFlags,
                probe.Scrolls, probe.EnsureVisibleCalls, sizeChanges, textChanges, layoutChanges });
            Check((nativeStyles & 0x10000) != 0, scenario + ": native list-view double buffering is enabled");
            Check(probe.FullPaints == 0, scenario + ": streaming does not invalidate the entire list client");
            Check(sizeChanges == 0, scenario + ": updates do not resize the log grid");
            Check(textChanges == (scenario == "idle" ? 0 : 12), scenario + ": each changed status is assigned once");
            if (scenario is "offscreen-append" or "idle")
                Check(probe.Paints == 0 && probe.Erases == 0, scenario + ": unchanged visible rows receive no repaint or erase");
            else Check(probe.Paints > 0, scenario + ": changed visible rows are repainted");
            if (!form.follow.Checked) Check((form.view.TopItem?.Index ?? 0) == originalTop, scenario + ": appends preserve the reader's position");
            else Check(form.view.GetItemRect(form.view.VirtualListSize - 1).Bottom <= form.view.ClientSize.Height,
                scenario + ": follow keeps the new last row visible");
            if (scenario == "full-ring") Check(probe.CountChanges == 0 && probe.EnsureVisibleCalls == 0,
                "full ring redraws changed row contents without resetting the count or redundantly scrolling");
            if (scenario == "idle") Check(probe.CountChanges == 0 && layoutChanges == 0,
                "idle timer ticks change neither count nor layout");
            if (scenario != "idle") Check(form.view.Items[^1].SubItems[10].Text == "10011", scenario + ": the last visible item exposes the newest raw value");
            using Bitmap bitmap = new(form.Width, form.Height); form.DrawToBitmap(bitmap, new(0, 0, form.Width, form.Height));
            bitmap.Save(Path.Combine(directory, scenario + ".png"));
            if (scenario == "visible-append")
            {
                form.ClearHistory(); form.view.Update();
                // Clear can replace content without changing the row count.
                axis.Value = 12345; probe.Reset(); form.ClearHistory(); form.view.Update();
                Check(form.view.VirtualListSize == 1 && form.view.Items[0].SubItems[10].Text == "12345" && probe.Paints > 0,
                    "same-count clear invalidates replaced snapshot rows");
            }
            form.Close();
        }
        File.WriteAllText(Path.Combine(directory, "paint-measurements.json"), JsonSerializer.Serialize(new { passed = true, count = checks.Count,
            syntheticInput = true, hardwareWrites = false, measurements, checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
