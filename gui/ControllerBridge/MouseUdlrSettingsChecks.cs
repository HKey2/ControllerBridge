using System.Runtime.InteropServices;
using System.Text.Json;

namespace ControllerBridge;

internal sealed partial class MouseUdlrSettingsDialog
{
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    private static IEnumerable<Control> All(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(All(c)));
    internal static void CheckLayout(Form form, Action<bool, string> check, string context)
    {
        form.PerformLayout(); Application.DoEvents();
        foreach (Control c in All(form).Where(c => c.Visible && c is Button or Label or NumericUpDown))
        {
            check(c.Width > 0 && c.Height > 0 && c.Parent!.ClientRectangle.Contains(c.Bounds), context + ": fits " + c.Text);
            if (c is NumericUpDown || c.Text.Length == 0) continue;
            Size available = c.ClientSize - c.Padding.Size;
            Size text = TextRenderer.MeasureText(c.Text, c.Font, new(Math.Max(1, available.Width), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            check(text.Width <= available.Width + 1 && text.Height <= available.Height + 1, context + ": text " + c.Text);
        }
    }
    internal static void Transition(Form form, int dpi)
    {
        Control[] controls = All(form).ToArray(); foreach (Control c in controls) _ = c.Handle;
        float factor = (float)dpi / form.DeviceDpi;
        NativeRect rectangle = new() { Left = 20, Top = 20, Right = 20 + (int)Math.Round(form.Width * factor), Bottom = 20 + (int)Math.Round(form.Height * factor) };
        foreach (Control c in controls.Reverse()) SendMessage(c.Handle, 0x02e2, dpi, 0);
        nint pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try { Marshal.StructureToPtr(rectangle, pointer, false); SendMessage(form.Handle, 0x02e0, dpi | (dpi << 16), pointer); }
        finally { Marshal.FreeHGlobal(pointer); }
        foreach (Control c in controls) SendMessage(c.Handle, 0x02e3, 0, 0);
        form.PerformLayout(); Application.DoEvents();
    }
    internal static void CaptureImage(Form form, string path)
    {
        form.PerformLayout(); Application.DoEvents();
        using Bitmap image = new(form.Width, form.Height); form.DrawToBitmap(image, new(0, 0, image.Width, image.Height)); image.Save(path);
    }
    internal static void RunChecks(string directory)
    {
        Directory.CreateDirectory(directory);
        using MainForm main = new(preview: true, autoConnect: false) { Opacity = 0, ShowInTaskbar = false };
        List<string> checks = []; Exception? failure = null;
        void Check(bool ok, string label) { if (!ok) throw new InvalidDataException(label); checks.Add(label); }
        main.Shown += async (_, _) =>
        {
            try
            {
                await main.CheckMouseUdlrHeader(directory, Check);
                await CheckDialog(directory, Check);
                File.WriteAllText(Path.Combine(directory, "mouse-udlr-settings-checks.json"), JsonSerializer.Serialize(new { passed = true, count = checks.Count, checks, hardwareOpened = false, simulatedDpiMessages = true }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { failure = ex; }
            finally { main.Close(); }
        };
        Application.Run(main); if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static async Task CheckDialog(string directory, Action<bool, string> check)
    {
        MouseUdlrSettings device = MouseUdlrSettings.Defaults; int reads = 0, writes = 0; bool failWrite = false, failReadback = false;
        byte[] Reply(MouseUdlrSettings s)
        {
            byte[] reply = new byte[56]; "UDS2"u8.CopyTo(reply); Wire.W32(reply, 4, 2);
            Wire.W32(reply, 8, s.Threshold); Wire.W32(reply, 12, s.HoldMs); Wire.W32(reply, 16, s.WaitMs); Wire.W32(reply, 20, s.Persisted ? 1u : 0u); return reply;
        }
        Task<byte[]> Command(byte cmd, byte[]? data)
        {
            if (cmd == 52) { reads++; return Task.FromResult(Reply(failReadback ? device with { Persisted = false } : device)); }
            check(cmd == 53 && data is { Length: 32 } && data.AsSpan(0, 4).SequenceEqual("UDS2"u8) && Wire.U32(data, 4) == 2, "save uses the versioned settings command");
            check(Wire.U32(data!, 8) == 64 && Wire.U32(data!, 12) == 30 && Wire.U32(data!, 16) == 20, "save guards the original values");
            writes++; if (failWrite) throw new IOException("Simulated disconnect / save failed");
            device = new(Wire.U32(data!, 20), Wire.U32(data!, 24), Wire.U32(data!, 28), true); return Task.FromResult(Reply(device));
        }
        MouseUdlrSettingsClient client = new(Command);
        MouseUdlrSettings initial = await client.ReadAsync();
        check(initial == new MouseUdlrSettings(64, 30, 20, true) && writes == 0, "opening reads the saved defaults without a write");
        foreach (var invalid in new[] { (0u, 16u, 16u), (257u, 16u, 16u), (64u, 0u, 16u), (64u, 513u, 16u), (uint.MaxValue, 16u, 16u), (64u, 16u, 0u), (64u, 16u, 513u), (64u, 16u, uint.MaxValue) })
        {
            bool rejected = false; try { await client.SaveAsync(initial, new(invalid.Item1, invalid.Item2, invalid.Item3, true)); } catch (InvalidDataException) { rejected = true; }
            check(rejected && writes == 0, "invalid edit rejected before USB: " + invalid);
        }
        byte[] malformed = Reply(initial); Wire.W32(malformed, 4, 1); bool bad = false;
        try { MouseUdlrSettings.Read(malformed); } catch (InvalidDataException) { bad = true; }
        check(bad, "unknown settings schema rejected");
        using (MouseUdlrSettingsDialog form = new(initial, client.SaveAsync) { Opacity = 0, ShowInTaskbar = false })
        {
            form.Show(); Application.DoEvents();
            check(form.threshold.Value == 64 && form.hold.Value == 30 && form.wait.Value == 20, "dialog loads saved values");
            check(form.threshold.Minimum == 1 && form.threshold.Maximum == 256 && form.hold.Minimum == 1 && form.hold.Maximum == 512 && form.wait.Minimum == 1 && form.wait.Maximum == 512, "all numeric editor ranges match the request");
            check(form.threshold.DecimalPlaces == 0 && form.hold.DecimalPlaces == 0 && form.wait.DecimalPlaces == 0, "settings use whole pt and ms");
            foreach (int dpi in new[] { 96, 144, 192, 96 })
            {
                Transition(form, dpi); check(form.DeviceDpi == dpi, "dialog DPI transition " + dpi);
                CheckLayout(form, check, "dialog " + dpi);
                foreach (NumericUpDown input in new[] { form.threshold, form.hold, form.wait })
                {
                    TableLayoutPanel row = (TableLayoutPanel)input.Parent!;
                    Control units = row.GetControlFromPosition(2, row.GetRow(input))!;
                    check(input.Right == units.Left, "unit touches numeric editor at " + dpi + ": " + input.AccessibleName);
                }
                check(!All(form).OfType<Label>().Any(l => l.Text is "1–256" or "1–512"), "range labels are omitted");
                CaptureImage(form, Path.Combine(directory, "mouse-udlr-settings-" + dpi + ".png"));
            }
            form.threshold.Value = 256; form.hold.Value = 512; form.wait.Value = 1; form.cancel.PerformClick();
            check(!form.Saved && writes == 0 && device == initial, "Cancel closes without persisting edits");
        }
        using (MouseUdlrSettingsDialog form = new(initial, client.SaveAsync) { Opacity = 0, ShowInTaskbar = false })
        {
            form.Show(); form.threshold.Value = 256; form.hold.Value = 512; form.wait.Value = 1; failWrite = true; await form.SaveSettings();
            check(!form.Saved && form.Visible && form.threshold.Value == 256 && form.hold.Value == 512 && form.wait.Value == 1 && form.save.Enabled, "failed save keeps the dialog and entered values");
            check(form.status.Text.Contains("failed"), "save error is visible inline");
            failWrite = false; int beforeReads = reads; await form.SaveSettings();
            check(form.Saved && form.DialogResult == DialogResult.OK && device == new MouseUdlrSettings(256, 512, 1, true) && reads == beforeReads + 1, "successful retry writes and verifies a separate readback");
        }
        device = initial; failReadback = true; bool unverified = false;
        try { await client.SaveAsync(initial, new(1, 1, 512, true)); } catch (IOException) { unverified = true; }
        check(unverified && device == new MouseUdlrSettings(1, 1, 512, true), "unpersisted readback cannot be reported as success");
        failReadback = false;
        TaskCompletionSource<MouseUdlrSettings> pending = new();
        using (MouseUdlrSettingsDialog form = new(initial, (_, _) => pending.Task) { Opacity = 0, ShowInTaskbar = false })
        {
            form.Show(); Task savingTask = form.SaveSettings();
            check(!form.save.Enabled && !form.cancel.Enabled && !form.threshold.Enabled && !form.hold.Enabled && !form.wait.Enabled, "controls are disabled while save is pending");
            form.Close(); check(form.Visible, "closing waits for the in-progress save result");
            pending.SetResult(initial); await savingTask; check(form.Saved, "pending save finishes cleanly");
        }
        using (MouseUdlrSettingsDialog retained = new(new(64, 16, 16, true), client.SaveAsync))
        {
            check(retained.hold.Value == 16 && retained.wait.Value == 16, "saved 16/16 values are not replaced by new defaults");
        }
        using (MouseUdlrSettingsDialog form = new(new(65535, 1000, 1000, true), client.SaveAsync) { Opacity = 0, ShowInTaskbar = false })
        {
            check(form.threshold.Value == 256 && form.hold.Value == 512 && form.wait.Value == 512 && form.status.Text.Length > 0, "legacy out-of-range values require explicit save with a visible explanation");
        }
    }
}

internal sealed partial class MainForm
{
    internal async Task CheckMouseUdlrHeader(string directory, Action<bool, string> check)
    {
        timer.Stop(); Array.Clear(dirty); Size = MinimumSize; PerformLayout(); Application.DoEvents();
        TableLayoutPanel header = (TableLayoutPanel)mouseUdlrSettings.Parent!;
        check(header.GetColumn(switchMethod) == 3 && header.GetColumn(mouseUdlrSettings) == 4, "settings button follows Profile switching");
        check(mouseUdlrSettings.Text == L.T("マウス UDLR設定", "Mouse UDLR settings"), "requested button label");
        check(mouseUdlrSettings.Enabled, "preview settings button opens without hardware");
        byte[][] before = profiles.Select(p => p.Encode()).ToArray();
        using System.Windows.Forms.Timer closeDialog = new() { Interval = 30 }; bool opened = false;
        closeDialog.Tick += (_, _) =>
        {
            MouseUdlrSettingsDialog? dialog = Application.OpenForms.OfType<MouseUdlrSettingsDialog>().FirstOrDefault();
            if (dialog is null) return; opened = true; closeDialog.Stop(); dialog.Close();
        };
        closeDialog.Start(); await ConfigureMouseUdlr(); closeDialog.Stop();
        check(opened && !working && profiles.Select((p, i) => p.Encode().SequenceEqual(before[i])).All(x => x), "opening and cancelling leaves every profile unchanged");
        foreach (int dpi in new[] { 96, 144, 192, 96 })
        {
            MouseUdlrSettingsDialog.Transition(this, dpi); Size = MinimumSize; PerformLayout(); Application.DoEvents();
            foreach (Button button in header.Controls.OfType<Button>())
            {
                check(header.ClientRectangle.Contains(button.Bounds), "header button fits at " + dpi + ": " + button.Text);
                Size measured = TextRenderer.MeasureText(button.Text, button.Font);
                check(measured.Width <= button.ClientSize.Width - button.Padding.Horizontal + 1, "header text fits at " + dpi + ": " + button.Text);
            }
        }
        MouseUdlrSettingsDialog.CaptureImage(this, Path.Combine(directory, "main-settings-button.png"));
        using MainForm offline = new(autoConnect: false) { Opacity = 0, ShowInTaskbar = false };
        check(!offline.mouseUdlrSettings.Enabled, "offline settings button is disabled");
        Capabilities old = caps! with { Features = 262143 }, current = caps! with { Features = 524287 };
        check(!old.SupportsDirectionSettings && current.SupportsDirectionSettings, "capability distinguishes older firmware");
    }
}

