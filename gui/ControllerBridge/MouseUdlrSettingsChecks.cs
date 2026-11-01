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
        foreach (Control c in All(form).Where(c => c.Visible && c is Button or Label or NumericUpDown or ComboBox))
        {
            check(c.Width > 0 && c.Height > 0 && c.Parent!.ClientRectangle.Contains(c.Bounds), context + ": fits " + c.Text);
            // An auto-sized grid can contain its controls while the grid itself
            // is clipped by the tab. Check every ancestor up to the root content.
            Rectangle bounds = c.RectangleToScreen(c.ClientRectangle);
            for (Control? ancestor = c.Parent; ancestor is not null && ancestor != form; ancestor = ancestor.Parent)
                check(ancestor.RectangleToScreen(ancestor.ClientRectangle).Contains(bounds), context + ": ancestor fits " + c.Text);
            if (c is NumericUpDown or ComboBox || c.Text.Length == 0) continue;
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
                CheckSavedHoldFirstOpen(directory, Check);
                CheckStableModeLayout(directory, Check);
                await CheckModeSaveFailure(Check);
                await CheckDialog(directory, Check);
                await CheckRollingDialog(directory, Check);
                await CheckRollingStepDialog(directory, Check);
                await CheckTrackballHoldDialog(directory, Check);
                File.WriteAllText(Path.Combine(directory, "mouse-udlr-settings-checks.json"), JsonSerializer.Serialize(new { passed = true, count = checks.Count, checks, hardwareOpened = false, simulatedDpiMessages = true }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { failure = ex; }
            finally { main.Close(); }
        };
        Application.Run(main); if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static void CheckSavedHoldFirstOpen(string directory, Action<bool, string> check)
    {
        using MouseUdlrSettingsDialog form = new(MouseUdlrSettings.Defaults with { Trackball = 0x101 }, (_, next) => Task.FromResult(next)) { Opacity = 0, ShowInTaskbar = false };
        form.Show(); Application.DoEvents();
        CaptureImage(form, Path.Combine(directory, "saved-hold-first-open.png"));
        File.WriteAllText(Path.Combine(directory, "saved-hold-first-open.json"), JsonSerializer.Serialize(new {
            tab = form.sourceTabs.SelectedIndex, mode = form.trackballMode.SelectedIndex,
            clickVisible = form.clickValues.Visible, clickEnabled = form.threshold.Enabled,
            holdVisible = form.trackballHoldRow.Visible, holdEnabled = form.trackballHoldThreshold.Enabled,
            bounds = All(form).Where(c => c.Visible).Select(c => new { type = c.GetType().Name, text = c.Text, bounds = c.Bounds.ToString(), parent = c.Parent?.ClientRectangle.ToString() })
        }, new JsonSerializerOptions { WriteIndented = true }));
        check(form.sourceTabs.SelectedIndex == 0 && form.trackballMode.SelectedIndex == 1, "saved Hold opens directly on Trackball");
        check(form.clickValues.Visible && !form.threshold.Enabled && !form.hold.Enabled && !form.wait.Enabled,
            "first open of saved Hold shows disabled click parameters without tab cycling");
        check(form.trackballHoldRow.Visible && form.trackballHoldThreshold.Enabled, "first open of saved Hold enables its dedicated threshold");
        CheckLayout(form, check, "saved Hold first open");
    }
    private static void CheckStableModeLayout(string directory, Action<bool, string> check)
    {
        // Start with persisted Hold, before any tab or mode selection. Exercise
        // the exact user flow and compare positions after both mode changes.
        MouseUdlrSettings current = new(123, 37, 29, true, 0x04010203, 0x0302110F, 0x0301001B, 0x107);
        using MouseUdlrSettingsDialog form = new(current, (_, next) => Task.FromResult(next)) { Opacity = 0, ShowInTaskbar = false };
        form.Show(); Application.DoEvents();
        foreach (int dpi in new[] { 96, 144, 192, 96 })
        {
            Transition(form, dpi);
            Rectangle tabs = form.sourceTabs.Bounds, click = form.clickValues.Bounds;
            Rectangle ballMode = form.trackballMode.Bounds, holdValue = form.trackballHoldThreshold.Bounds;
            Rectangle down = form.directionChoices[1].Bounds, reset = form.resetTargets[0].Bounds;
            Rectangle footer = form.save.Bounds;
            void Stable(string flow)
            {
                form.PerformLayout(); Application.DoEvents();
                check(form.sourceTabs.Bounds == tabs && form.clickValues.Bounds == click && form.save.Bounds == footer,
                    flow + ": tabs, common parameters and footer stay in place at " + dpi);
                check(form.trackballMode.Bounds == ballMode && form.trackballHoldThreshold.Bounds == holdValue &&
                    form.directionChoices[1].Bounds == down && form.resetTargets[0].Bounds == reset,
                    flow + ": trackball mode, threshold and lower controls stay in place at " + dpi);
                CheckLayout(form, check, flow + " " + dpi);
            }
            check(form.clickValues.Visible && !form.clickValues.Enabled && form.trackballHoldRow.Visible && form.trackballHoldThreshold.Enabled, "saved Hold starts with inactive click fields disabled at " + dpi);
            Stable("saved Hold");
            CaptureImage(form, Path.Combine(directory, "saved-hold-stable-" + dpi + ".png"));
            form.sourceTabs.SelectedIndex = 1; Application.DoEvents();
            check(form.paddleDirections.Visible && !form.paddleDirections.Enabled && form.rollingPanel.Visible && form.rollingPanel.Enabled && !form.clickValues.Enabled, "saved paddle rolling shows disabled directions/common fields at " + dpi);
            Stable("paddle rolling");
            Rectangle rolling = form.rollingPanel.Bounds, paddleGrid = form.paddleDirections.Bounds;
            form.paddleMode.SelectedIndex = 0; Application.DoEvents();
            check(form.paddleDirections.Visible && form.paddleDirections.Enabled && form.rollingPanel.Visible && !form.rollingPanel.Enabled && form.clickValues.Enabled, "paddle clicks enables directions/common fields and disables rolling at " + dpi);
            check(form.rollingPanel.Bounds == rolling && form.paddleDirections.Bounds == paddleGrid, "paddle mode does not move either parameter group at " + dpi);
            Stable("paddle clicks");
            form.sourceTabs.SelectedIndex = 0; Application.DoEvents();
            check(form.clickValues.Visible && !form.clickValues.Enabled && form.trackballHoldThreshold.Enabled, "return from paddle restores Hold enabled state at " + dpi);
            Stable("return to Hold");
            form.trackballMode.SelectedIndex = 0; Application.DoEvents();
            check(form.clickValues.Visible && form.clickValues.Enabled && form.trackballHoldRow.Visible && !form.trackballHoldThreshold.Enabled, "trackball clicks keeps its inactive Hold threshold visible at " + dpi);
            Stable("trackball clicks");
            form.trackballMode.SelectedIndex = 1;
            form.sourceTabs.SelectedIndex = 1; form.paddleMode.SelectedIndex = 1; form.sourceTabs.SelectedIndex = 0;
            Stable("restore saved modes");
            check(form.threshold.Value == 123 && form.hold.Value == 37 && form.wait.Value == 29 && form.TrackballValue == current.Trackball && form.RollingValue == current.Rolling && form.buttonMaps.SequenceEqual(new[] { current.ButtonMap, current.PaddleButtonMap }), "all inactive and active drafts survive navigation at " + dpi);
        }
    }
    private static async Task CheckModeSaveFailure(Action<bool, string> check)
    {
        foreach ((int source, uint trackball, uint rolling, bool clicks) in new[] {
            (0, 1u, 10u, true), (0, 0x107u, 10u, false), (1, 0x107u, 10u, true), (1, 1u, 0x0301001Bu, false) })
        {
            TaskCompletionSource<MouseUdlrSettings> pending = new();
            MouseUdlrSettings current = new(123, 37, 29, true, Rolling: rolling, Trackball: trackball);
            using MouseUdlrSettingsDialog form = new(current, (_, _) => pending.Task) { Opacity = 0, ShowInTaskbar = false };
            form.Show(); form.sourceTabs.SelectedIndex = source; Application.DoEvents();
            Control[] inputs = All(form).Where(c => c is NumericUpDown or ComboBox or CheckBox or Button).ToArray();
            var before = inputs.Select(c => (c.Enabled, c.Visible, c.Bounds)).ToArray();
            Rectangle tabs = form.sourceTabs.Bounds;
            check(form.clickValues.Visible && form.threshold.Enabled == clicks, "mode selects common parameter enabled state before save " + source + "/" + clicks);
            Task saving = form.SaveSettings();
            check(inputs.All(c => !c.Enabled) && form.clickValues.Visible && form.sourceTabs.Bounds == tabs, "save disables controls without changing layout " + source + "/" + clicks);
            pending.SetException(new IOException("Expected mode-state retry check")); await saving; Application.DoEvents();
            check(inputs.Select(c => (c.Enabled, c.Visible, c.Bounds)).SequenceEqual(before) && form.sourceTabs.Bounds == tabs,
                "save failure restores exactly the original enabled/visible/layout state " + source + "/" + clicks);
            check(!form.Saved && form.TrackballValue == current.Trackball && form.RollingValue == current.Rolling && form.threshold.Value == 123 && form.hold.Value == 37 && form.wait.Value == 29,
                "save failure preserves active and inactive parameter values " + source + "/" + clicks);
            form.Close();
        }
    }
    private static async Task CheckTrackballHoldDialog(string directory, Action<bool, string> check)
    {
        MouseUdlrSettings current = new(123, 37, 29, true, 0x04010203, 0x0302110F, 0x0301001B);
        MouseUdlrSettings? sent = null;
        using (MouseUdlrSettingsDialog form = new(current, (_, next) => { sent = next; return Task.FromResult(next); }) { Opacity = 0, ShowInTaskbar = false })
        {
            form.Show(); Application.DoEvents();
            check(form.trackballMode.Items.Cast<string>().SequenceEqual(new[] { "Virtual Rolling", "Hold" }) && form.trackballMode.SelectedIndex == 0, "trackball legacy mode is named Virtual Rolling");
            check(form.trackballHoldRow.Visible && !form.trackballHoldThreshold.Enabled && form.clickValues.Visible && form.threshold.Enabled && form.trackballHoldThreshold.Value == 1, "legacy mode shows disabled Hold parameters and initializes Hold to one point");
            form.trackballMode.SelectedIndex = 1; Application.DoEvents();
            check(form.trackballHoldRow.Visible && form.trackballHoldThreshold.Enabled && form.clickValues.Visible && !form.threshold.Enabled && !form.hold.Enabled && !form.wait.Enabled && form.directionChoices.Take(4).All(c => c.Visible), "Hold disables click parameters in place and retains assignments");
            check(form.trackballHoldThreshold.Minimum == 1 && form.trackballHoldThreshold.Maximum == 127 && form.trackballHoldThreshold.DecimalPlaces == 0, "Hold threshold is 1..127 integer points");
            check(!All(form).OfType<Label>().Where(c => c.Visible).Any(c => c.Text.Contains("127")), "Hold does not display a range annotation");
            foreach (int dpi in new[] { 96, 144, 192, 96 })
            {
                Transition(form, dpi);
                CaptureImage(form, Path.Combine(directory, "trackball-hold-" + dpi + ".png"));
                File.WriteAllText(Path.Combine(directory, "trackball-hold-layout-" + dpi + ".json"), JsonSerializer.Serialize(All(form).Where(c => c.Visible).Select(c => new { type = c.GetType().Name, text = c.Text, bounds = c.Bounds.ToString(), parent = c.Parent?.ClientRectangle.ToString() }), new JsonSerializerOptions { WriteIndented = true }));
                CheckLayout(form, check, "Trackball Hold " + dpi);
                Control units = form.trackballHoldRow.GetControlFromPosition(2, 0)!;
                check(form.trackballHoldThreshold.Right == units.Left, "Hold pt sits adjacent to the numeric editor " + dpi);
                foreach (ComboBox combo in new[] { form.trackballMode })
                    check(combo.Items.Cast<object>().Max(item => TextRenderer.MeasureText(item.ToString(), combo.Font).Width) + (int)Math.Ceiling(24 * dpi / 96.0) <= combo.ClientSize.Width, "trackball mode names fit at " + dpi);
                CaptureImage(form, Path.Combine(directory, "trackball-hold-" + dpi + ".png"));
            }
            form.trackballHoldThreshold.Value = 127; form.trackballMode.SelectedIndex = 0;
            check(form.clickValues.Visible && form.threshold.Enabled && !form.trackballHoldThreshold.Enabled && form.threshold.Value == 123 && form.hold.Value == 37 && form.wait.Value == 29, "returning to Virtual Rolling enables retained click parameters");
            form.trackballMode.SelectedIndex = 1; form.sourceTabs.SelectedIndex = 1;
            check(form.clickValues.Visible && !form.threshold.Enabled && form.RollingValue == current.Rolling, "paddle Virtual Rolling disables click parameters and retains its settings independently");
            form.sourceTabs.SelectedIndex = 0;
            check(form.clickValues.Visible && !form.threshold.Enabled && form.trackballHoldThreshold.Value == 127 && form.buttonMaps[0] == current.ButtonMap, "Hold draft and assignments survive tab switching with disabled click fields visible");
            await form.SaveSettings(); check(form.Saved && sent == current with { Trackball = 0x17F }, "save includes Hold mode and threshold without changing other parameters");
        }
        using (MouseUdlrSettingsDialog reopened = new(sent!, (_, next) => Task.FromResult(next)))
            check(reopened.trackballMode.SelectedIndex == 1 && reopened.trackballHoldThreshold.Value == 127, "saved Hold settings reopen");
        int writes = 0;
        foreach (uint invalid in new uint[] { 0, 128, 255, 0x201, 0x10001, uint.MaxValue })
        {
            MouseUdlrSettingsClient client = new((_, _) => { writes++; return Task.FromResult(new byte[56]); }); bool rejected = false;
            try { await client.SaveAsync(current, current with { Trackball = invalid }); } catch (InvalidDataException) { rejected = true; }
            check(rejected && writes == 0, "invalid Hold setting rejected before transport " + invalid);
        }
        foreach (uint malformed in new uint[] { 0, 0x0080080A, 0x0101080A, 0x0001180A, 0x00010865 })
        {
            bool rejected = false; try { _ = current.WithModeOptions(malformed); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "reserved bits and invalid packed mode values rejected " + malformed);
        }
        foreach (uint trackball in new uint[] { 1, 127, 0x101, 0x17F })
        {
            MouseUdlrSettings desired = current with { Trackball = trackball };
            check(desired.WithModeOptions(desired.ModeOptions) == desired, "both Hold boundary values round-trip with independent paddle settings " + trackball);
        }
        byte[] Reply(MouseUdlrSettings s)
        {
            byte[] b = new byte[56]; "UDS7"u8.CopyTo(b); Wire.W32(b, 4, 7); Wire.W32(b, 8, s.Threshold); Wire.W32(b, 12, s.HoldMs);
            Wire.W32(b, 16, s.WaitMs); Wire.W32(b, 20, 1); Wire.W32(b, 24, s.ButtonMap); Wire.W32(b, 28, s.PaddleButtonMap); Wire.W32(b, 32, s.ModeOptions); return b;
        }
        MouseUdlrSettings edited = current with { Trackball = 0x107 };
        foreach (uint wrong in new uint[] { 7, 0x106 })
        {
            bool rejected = false;
            MouseUdlrSettingsClient client = new((cmd, data) => {
                if (cmd == 53) { check(data is { Length: 56 } && Wire.U32(data, 48) == current.ModeOptions && Wire.U32(data, 52) == edited.ModeOptions, "guarded save carries both mode groups within one payload"); return Task.FromResult(Reply(edited)); }
                return Task.FromResult(Reply(edited with { Trackball = wrong }));
            });
            try { await client.SaveAsync(current, edited); } catch (IOException) { rejected = true; }
            check(rejected, "Hold mode/threshold mismatch on independent readback rejected " + wrong);
        }
        using (MouseUdlrSettingsDialog cancelled = new(current, (_, next) => { writes++; return Task.FromResult(next); }) { Opacity = 0, ShowInTaskbar = false })
        {
            cancelled.Show(); cancelled.trackballMode.SelectedIndex = 1; cancelled.trackballHoldThreshold.Value = 127; cancelled.cancel.PerformClick();
            check(!cancelled.Saved && writes == 0, "cancelling Hold draft performs no write");
        }
        TaskCompletionSource<MouseUdlrSettings> pending = new();
        using (MouseUdlrSettingsDialog saving = new(edited, (_, _) => pending.Task) { Opacity = 0, ShowInTaskbar = false })
        {
            saving.Show(); Task saveTask = saving.SaveSettings();
            check(!saving.trackballMode.Enabled && !saving.trackballHoldThreshold.Enabled && !saving.cancel.Enabled, "Hold controls disabled during save");
            pending.SetException(new IOException("Expected test save failure")); await saveTask;
            check(!saving.Saved && saving.trackballMode.Enabled && saving.trackballHoldThreshold.Enabled && !saving.threshold.Enabled && !saving.hold.Enabled && !saving.wait.Enabled && saving.clickValues.Visible && saving.trackballHoldThreshold.Value == 7, "failed save restores Hold state without enabling inactive click parameters"); saving.Close();
        }
    }
    private static async Task CheckRollingStepDialog(string directory, Action<bool, string> check)
    {
        MouseUdlrSettings current = MouseUdlrSettings.Defaults with { Rolling = 0x0301001B, Trackball = 0x107 };
        MouseUdlrSettings? sent = null;
        using (MouseUdlrSettingsDialog form = new(current, (_, next) => { sent = next; return Task.FromResult(next); }) { Opacity = 0, ShowInTaskbar = false })
        {
            form.Show(); form.sourceTabs.SelectedIndex = 1; Application.DoEvents();
            check(form.rollingStep.DropDownStyle == ComboBoxStyle.DropDownList && form.rollingStep.Items.Cast<int>().SequenceEqual(new[] { 5, 10, 15, 20, 30, 40, 45, 60, 90 }), "only the nine equal-circle five-degree multiples are offered");
            check(form.RollingStepValue == 30 && form.rollingStep.Enabled, "old settings default to 30 degree rotation steps");
            foreach (int dpi in new[] { 96, 144, 192, 96 })
            {
                Transition(form, dpi); Rectangle tabs = form.sourceTabs.Bounds, field = form.rollingStep.Bounds;
                foreach (int value in new[] { 5, 90, 30 })
                {
                    form.rollingStep.SelectedItem = value; CheckLayout(form, check, "rotation step " + value + " at " + dpi);
                    check(form.RollingStepValue == value && form.rollingStep.Bounds == field && form.sourceTabs.Bounds == tabs, "step selection preserves layout at " + dpi + "/" + value);
                }
                Control units = form.rollingPanel.GetControlFromPosition(2, 1)!;
                check(form.rollingStep.Right == units.Left, "degree unit is adjacent to rotation-step list at " + dpi);
                CaptureImage(form, Path.Combine(directory, "rotation-step-" + dpi + ".png"));
                form.paddleMode.SelectedIndex = 0; Application.DoEvents();
                check(form.rollingStep.Visible && !form.rollingStep.Enabled && form.sourceTabs.Bounds == tabs && form.rollingStep.Bounds == field, "click mode greys the step in place at " + dpi);
                form.paddleMode.SelectedIndex = 1;
            }
            form.rollingStep.SelectedItem = 90;form.sourceTabs.SelectedIndex = 0;form.sourceTabs.SelectedIndex = 1;
            check(form.RollingStepValue == 90 && form.RollingValue == current.Rolling && form.TrackballValue == current.Trackball, "tab changes preserve the step and both modes");
            await form.SaveSettings(); check(form.Saved && sent == current with { RollingStepDeg = 90 }, "step saves independently of sensitivity, targets and Hold");
        }
        using (MouseUdlrSettingsDialog reopened = new(sent!, (_, next) => Task.FromResult(next)) { Opacity = 0, ShowInTaskbar = false })
        {
            reopened.Show();reopened.sourceTabs.SelectedIndex = 1;
            check(reopened.RollingStepValue == 90 && reopened.rollingStep.Enabled, "persisted step restores on reopen");reopened.Close();
        }
        byte[] Reply(MouseUdlrSettings value) {
            byte[] data = new byte[56]; "UDS7"u8.CopyTo(data); Wire.W32(data, 4, 7); Wire.W32(data, 8, value.Threshold); Wire.W32(data, 12, value.HoldMs);
            Wire.W32(data, 16, value.WaitMs); Wire.W32(data, 20, 1); Wire.W32(data, 24, value.ButtonMap); Wire.W32(data, 28, value.PaddleButtonMap); Wire.W32(data, 32, value.ModeOptions); return data;
        }
        foreach (uint step in new uint[] { 5, 10, 15, 20, 30, 40, 45, 60, 90 })
        {
            MouseUdlrSettings desired = current with { RollingStepDeg = step };
            MouseUdlrSettingsClient client = new((cmd, data) => {
                if (cmd == 53) check(data is { Length: 56 } && Wire.U32(data, 48) == current.ModeOptions && Wire.U32(data, 52) == desired.ModeOptions, "step uses guarded old/new options " + step);
                return Task.FromResult(Reply(desired));
            });
            check(await client.SaveAsync(current, desired) == desired && desired.WithModeOptions(desired.ModeOptions) == desired, "step round-trip and independent readback " + step);
        }
        int writes = 0;
        foreach (uint step in new uint[] { 0, 1, 4, 6, 25, 35, 50, 55, 65, 70, 75, 80, 85, 91, 95, uint.MaxValue })
        {
            MouseUdlrSettingsClient client = new((_, _) => { writes++; return Task.FromResult(Reply(current)); }); bool rejected = false;
            try { await client.SaveAsync(current, current with { RollingStepDeg = step }); } catch (InvalidDataException) { rejected = true; }
            check(rejected && writes == 0, "invalid/non-divisor step never reaches transport " + step);
        }
        bool mismatch = false; MouseUdlrSettings changed = current with { RollingStepDeg = 90 };
        MouseUdlrSettingsClient wrong = new((cmd, _) => Task.FromResult(Reply(cmd == 53 ? changed : current)));
        try { await wrong.SaveAsync(current, changed); } catch (IOException) { mismatch = true; }
        check(mismatch, "step mismatch in independent readback rejects save success");
        byte[] oldReply = Reply(current);"UDS6"u8.CopyTo(oldReply);Wire.W32(oldReply, 4, 6);bool oldRejected = false;
        try { MouseUdlrSettings.Read(oldReply); } catch (InvalidDataException) { oldRejected = true; }
        check(oldRejected, "old fixed-step firmware cannot be misread as UDS7");
        TaskCompletionSource<MouseUdlrSettings> pending = new();
        using (MouseUdlrSettingsDialog form = new(current, (_, _) => pending.Task) { Opacity = 0, ShowInTaskbar = false })
        {
            form.Show();form.sourceTabs.SelectedIndex = 1;form.rollingStep.SelectedItem = 5;Task saving = form.SaveSettings();
            check(!form.rollingStep.Enabled && !form.cancel.Enabled, "step disabled during save");
            pending.SetException(new IOException("Expected step save failure"));await saving;
            check(!form.Saved && form.rollingStep.Enabled && form.RollingStepValue == 5 && !form.clickValues.Enabled, "failed save restores active step and keeps inactive controls disabled");
            form.cancel.PerformClick();check(!form.Saved, "cancel does not persist the step draft");
        }
    }
    private static async Task CheckRollingDialog(string directory, Action<bool, string> check)
    {
        MouseUdlrSettings current = new(64, 30, 20, true, 0x04010203, 0x0302110F, 10);
        MouseUdlrSettings? sent = null;
        using (MouseUdlrSettingsDialog form = new(current, (_, next) => { sent = next; return Task.FromResult(next); }) { Opacity = 0, ShowInTaskbar = false })
        {
            form.Show(); form.sourceTabs.SelectedIndex = 1; Application.DoEvents();
            check(form.paddleMode.SelectedIndex == 0 && form.paddleDirections.Visible && form.paddleDirections.Enabled && form.rollingPanel.Visible && !form.rollingGain.Enabled && form.threshold.Enabled, "old paddle click mode shows inactive rolling parameters disabled");
            form.paddleMode.SelectedIndex = 1; Application.DoEvents();
            check(form.rollingPanel.Visible && form.rollingGain.Enabled && form.paddleDirections.Visible && !form.paddleDirections.Enabled && !form.threshold.Enabled && form.rollingGain.Value == 1.0m && form.rollingStick.SelectedIndex == 0 && !form.rollingInvert.Checked, "paddle rolling defaults remain and inactive click controls stay visible disabled");
            check(form.rollingGain.Minimum == 0.1m && form.rollingGain.Maximum == 10m && form.rollingGain.DecimalPlaces == 1 && form.rollingGain.Increment == 0.1m, "rolling sensitivity bounds and fractional step");
            foreach (int dpi in new[] { 96, 144, 192, 96 })
            {
                Transition(form, dpi); CheckLayout(form, check, "Virtual Rolling " + dpi);
                check(form.rollingPanel.ClientRectangle.Contains(form.rollingInvert.Bounds), "reversal checkbox fits at " + dpi);
                foreach (ComboBox combo in new[] { form.paddleMode, form.rollingStick })
                    check(combo.Items.Cast<object>().Max(item => TextRenderer.MeasureText(item.ToString(), combo.Font).Width) + (int)Math.Ceiling(24 * dpi / 96.0) <= combo.ClientSize.Width, "rolling choices fit at " + dpi);
                CaptureImage(form, Path.Combine(directory, "virtual-rolling-" + dpi + ".png"));
            }
            form.rollingGain.Value = 2.7m; form.rollingStick.SelectedIndex = 1; form.rollingInvert.Checked = true;
            form.paddleMode.SelectedIndex = 0; form.paddleMode.SelectedIndex = 1;
            form.sourceTabs.SelectedIndex = 0; form.sourceTabs.SelectedIndex = 1;
            check(form.RollingValue == 0x0301001B && form.buttonMaps[0] == current.ButtonMap && form.buttonMaps[1] == current.PaddleButtonMap, "mode/tab switches preserve rolling draft and both click maps");
            await form.SaveSettings(); check(form.Saved && sent == current with { Rolling = 0x0301001B }, "rolling settings save with maps and shared timings");
        }
        using (MouseUdlrSettingsDialog reopened = new(sent!, (_, next) => Task.FromResult(next)))
            check(reopened.paddleMode.SelectedIndex == 1 && reopened.rollingGain.Value == 2.7m && reopened.rollingStick.SelectedIndex == 1 && reopened.rollingInvert.Checked, "saved rolling fields reopen correctly");
        int writes = 0;
        byte[] Reply(MouseUdlrSettings s) {
            byte[] b = new byte[56]; "UDS7"u8.CopyTo(b); Wire.W32(b, 4, 7); Wire.W32(b, 8, s.Threshold); Wire.W32(b, 12, s.HoldMs);
            Wire.W32(b, 16, s.WaitMs); Wire.W32(b, 20, 1); Wire.W32(b, 24, s.ButtonMap); Wire.W32(b, 28, s.PaddleButtonMap); Wire.W32(b, 32, s.ModeOptions); return b;
        }
        foreach (uint value in new uint[] { 0, 101, 65535, 0x0002000A, 0x0401000A })
        {
            MouseUdlrSettingsClient badClient = new((_, _) => { writes++; return Task.FromResult(Reply(current)); }); bool rejected = false;
            try { await badClient.SaveAsync(current, current with { Rolling = value }); } catch (InvalidDataException) { rejected = true; }
            check(rejected && writes == 0, "invalid rolling request never reaches USB: " + value);
            rejected = false; try { MouseUdlrSettings.Read(Reply(current with { Rolling = value })); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "invalid rolling readback rejected: " + value);
        }
        MouseUdlrSettings desired = current with { Rolling = 0x0301001B };
        foreach (uint wrong in new uint[] { desired.Rolling ^ 1u, desired.Rolling ^ 0x10000u, desired.Rolling ^ 0x1000000u, desired.Rolling ^ 0x2000000u })
        {
            bool rejected = false;
            MouseUdlrSettingsClient mismatch = new((cmd, data) => {
                if (cmd == 53) { check(data is { Length: 56 } && Wire.U32(data, 48) == current.ModeOptions && Wire.U32(data, 52) == desired.ModeOptions, "rolling wire request guards old and sends desired settings"); return Task.FromResult(Reply(desired)); }
                return Task.FromResult(Reply(desired with { Rolling = wrong }));
            });
            try { await mismatch.SaveAsync(current, desired); } catch (IOException) { rejected = true; }
            check(rejected, "independent rolling readback mismatch rejected: " + wrong);
        }
        using (MouseUdlrSettingsDialog cancelled = new(current, (_, next) => { writes++; return Task.FromResult(next); }) { Opacity = 0, ShowInTaskbar = false }) {
            cancelled.Show(); cancelled.sourceTabs.SelectedIndex = 1; cancelled.paddleMode.SelectedIndex = 1; cancelled.rollingGain.Value = 10; cancelled.cancel.PerformClick();
            check(!cancelled.Saved && writes == 0, "cancel rolling draft without a write");
        }
        TaskCompletionSource<MouseUdlrSettings> pending = new();
        using (MouseUdlrSettingsDialog locked = new(desired, (_, _) => pending.Task) { Opacity = 0, ShowInTaskbar = false }) {
            locked.Show(); locked.sourceTabs.SelectedIndex = 1; Task saving = locked.SaveSettings();
            check(!locked.paddleMode.Enabled && !locked.rollingGain.Enabled && !locked.rollingStick.Enabled && !locked.rollingInvert.Enabled, "all rolling controls disabled during saving");
            pending.SetResult(desired); await saving; check(locked.Saved, "rolling save finishes");
        }
    }
    private static async Task CheckDialog(string directory, Action<bool, string> check)
    {
        MouseUdlrSettings device = MouseUdlrSettings.Defaults; int reads = 0, writes = 0; bool failWrite = false, failReadback = false, wrongMap = false, wrongPaddleMap = false;
        byte[] Reply(MouseUdlrSettings s)
        {
            byte[] reply = new byte[56]; "UDS7"u8.CopyTo(reply); Wire.W32(reply, 4, 7);
            Wire.W32(reply, 8, s.Threshold); Wire.W32(reply, 12, s.HoldMs); Wire.W32(reply, 16, s.WaitMs); Wire.W32(reply, 20, s.Persisted ? 1u : 0u); Wire.W32(reply, 24, s.ButtonMap); Wire.W32(reply, 28, s.PaddleButtonMap); Wire.W32(reply, 32, s.ModeOptions); return reply;
        }
        Task<byte[]> Command(byte cmd, byte[]? data)
        {
            if (cmd == 52) { reads++; return Task.FromResult(Reply(failReadback ? device with { Persisted = false } : wrongMap ? device with { ButtonMap = device.ButtonMap ^ 1u } : wrongPaddleMap ? device with { PaddleButtonMap = device.PaddleButtonMap ^ 1u } : device)); }
            check(cmd == 53 && data is { Length: 56 } && data.AsSpan(0, 4).SequenceEqual("UDS7"u8) && Wire.U32(data, 4) == 7, "save uses the versioned settings command");
            check(Wire.U32(data!, 8) == 64 && Wire.U32(data!, 12) == 30 && Wire.U32(data!, 16) == 20 && Wire.U32(data!, 32) == MouseUdlrSettings.DefaultButtonMap && Wire.U32(data!, 40) == MouseUdlrSettings.DefaultButtonMap, "save guards the original values and all four assignments");
            writes++; if (failWrite) throw new IOException("Simulated disconnect / save failed");
            device = new MouseUdlrSettings(Wire.U32(data!, 20), Wire.U32(data!, 24), Wire.U32(data!, 28), true, Wire.U32(data!, 36), Wire.U32(data!, 44)).WithModeOptions(Wire.U32(data!, 52)); return Task.FromResult(Reply(device));
        }
        MouseUdlrSettingsClient client = new(Command);
        MouseUdlrSettings initial = await client.ReadAsync();
        check(initial == new MouseUdlrSettings(64, 30, 20, true) && writes == 0, "opening reads the saved defaults without a write");
        foreach (var invalid in new[] { (0u, 16u, 16u), (257u, 16u, 16u), (64u, 0u, 16u), (64u, 513u, 16u), (uint.MaxValue, 16u, 16u), (64u, 16u, 0u), (64u, 16u, 513u), (64u, 16u, uint.MaxValue) })
        {
            bool rejected = false; try { await client.SaveAsync(initial, new(invalid.Item1, invalid.Item2, invalid.Item3, true)); } catch (InvalidDataException) { rejected = true; }
            check(rejected && writes == 0, "invalid edit rejected before USB: " + invalid);
        }
        for (int direction = 0; direction < 4; direction++)
        {
            bool invalid = false;
            try { await client.SaveAsync(initial, initial with { ButtonMap = 19u << (8 * direction) }); } catch (InvalidDataException) { invalid = true; }
            check(invalid && writes == 0, "invalid target rejected before USB: " + direction);
            byte[] badTarget = Reply(initial); badTarget[24 + direction] = 255; invalid = false;
            try { MouseUdlrSettings.Read(badTarget); } catch (InvalidDataException) { invalid = true; }
            check(invalid, "invalid device assignment rejected: " + direction);
        }
        for (int direction = 0; direction < 4; direction++)
        {
            bool invalid = false;
            try { await client.SaveAsync(initial, initial with { PaddleButtonMap = 19u << (8 * direction) }); } catch (InvalidDataException) { invalid = true; }
            check(invalid && writes == 0, "invalid paddle target rejected before USB: " + direction);
            byte[] badTarget = Reply(initial); badTarget[28 + direction] = 255; invalid = false;
            try { MouseUdlrSettings.Read(badTarget); } catch (InvalidDataException) { invalid = true; }
            check(invalid, "invalid device paddle assignment rejected: " + direction);
        }
        byte[] oldReply = Reply(initial); "UDS3"u8.CopyTo(oldReply); Wire.W32(oldReply, 4, 3); bool rejectedOld = false;
        try { MouseUdlrSettings.Read(oldReply); } catch (InvalidDataException) { rejectedOld = true; }
        check(rejectedOld, "shared-map firmware cannot be misread as two maps");
        byte[] malformed = Reply(initial); Wire.W32(malformed, 4, 1); bool bad = false;
        try { MouseUdlrSettings.Read(malformed); } catch (InvalidDataException) { bad = true; }
        check(bad, "unknown settings schema rejected");
        using (MouseUdlrSettingsDialog form = new(initial, client.SaveAsync) { Opacity = 0, ShowInTaskbar = false })
        {
            form.Show(); Application.DoEvents();
            check(form.threshold.Value == 64 && form.hold.Value == 30 && form.wait.Value == 20, "dialog loads saved values");
            check(form.threshold.Minimum == 1 && form.threshold.Maximum == 256 && form.hold.Minimum == 1 && form.hold.Maximum == 512 && form.wait.Minimum == 1 && form.wait.Maximum == 512, "all numeric editor ranges match the request");
            check(form.threshold.DecimalPlaces == 0 && form.hold.DecimalPlaces == 0 && form.wait.DecimalPlaces == 0, "settings use whole pt and ms");
            check(form.buttonMaps[0] == MouseUdlrSettings.DefaultButtonMap, "four explicit directional defaults");
            check(form.sourceTabs.TabPages.Cast<TabPage>().Select(p => p.Text).SequenceEqual(new[] { L.T("トラックボール", "Trackball"), L.T("パドル", "Paddle") }), "two source tabs have the requested labels");
            check(form.buttonMaps[1] == MouseUdlrSettings.DefaultButtonMap, "paddle defaults to all four original directions");
            int[,] cells = { { 1, 0 }, { 1, 2 }, { 0, 1 }, { 2, 1 } };
            for (int indexOfChoice = 0; indexOfChoice < 8; indexOfChoice++)
            {
                int source = indexOfChoice / 4, direction = indexOfChoice % 4;
                form.sourceTabs.SelectedIndex = source; Application.DoEvents();
                uint otherBefore = form.buttonMaps[1 - source];
                ComboBox choice = form.directionChoices[indexOfChoice];
                TableLayoutPanel grid = (TableLayoutPanel)choice.Parent!;
                check(choice.DropDownStyle == ComboBoxStyle.DropDownList && choice.Items.Count == 18,
                    "only the 18 Switch digital outputs are selectable: " + direction);
                check(choice.Items.Cast<TargetChoice>().Select(x => (int)x.Target).Order().SequenceEqual(Enumerable.Range(1, 18)),
                    "complete Switch buttons and D-pad, no unassigned or analog axes: " + direction);
                check(choice.Text == DeviceProfile.Targets[DirectionOutputs[direction]], "default displayed direction: " + direction);
                check(grid.GetColumn(choice) == cells[direction, 0] && grid.GetRow(choice) == cells[direction, 1], "dropdown cross placement: " + direction);
                for (int index = 0; index < choice.Items.Count; index++)
                {
                    choice.SelectedIndex = index;
                    byte target = ((TargetChoice)choice.SelectedItem!).Target;
                    check(MouseUdlrSettings.Target(form.buttonMaps[source], direction) == target && form.buttonMaps[1 - source] == otherBefore && choice.Text == DeviceProfile.Targets[target - 1],
                        "dropdown selection updates the encoded Switch target " + direction + "/" + target);
                }
            }
            form.threshold.Value = 123; form.hold.Value = 37; form.wait.Value = 29;
            uint paddleDraft = form.buttonMaps[1];
            form.sourceTabs.SelectedIndex = 0; form.resetTargets[0].PerformClick();
            check(form.buttonMaps[1] == paddleDraft, "trackball reset leaves paddle assignments untouched");
            form.AssignTarget(0, 3); uint ballDraft = form.buttonMaps[0];
            form.sourceTabs.SelectedIndex = 1; form.resetTargets[1].PerformClick();
            check(form.buttonMaps[0] == ballDraft && form.buttonMaps[1] == MouseUdlrSettings.DefaultButtonMap, "paddle reset leaves trackball assignments untouched");
            form.sourceTabs.SelectedIndex = 0; form.resetTargets[0].PerformClick();
            check(form.buttonMaps[0] == MouseUdlrSettings.DefaultButtonMap && writes == 0, "Reset to defaults restores all four assignments without a USB write");
            check(form.threshold.Value == 123 && form.hold.Value == 37 && form.wait.Value == 29, "reset does not change threshold or timings");
            form.threshold.Value = initial.Threshold; form.hold.Value = initial.HoldMs; form.wait.Value = initial.WaitMs;
            foreach (int dpi in new[] { 96, 144, 192, 96 })
            {
                Transition(form, dpi); check(form.DeviceDpi == dpi, "dialog DPI transition " + dpi);
                for (int source = 0; source < 2; source++)
                {
                    form.sourceTabs.SelectedIndex = source; Application.DoEvents();
                    CheckLayout(form, check, "dialog " + dpi + "/source " + source);
                    Rectangle tabBounds = form.sourceTabs.GetTabRect(source);
                    Size tabText = TextRenderer.MeasureText(form.sourceTabs.TabPages[source].Text, form.sourceTabs.Font);
                    check(tabBounds.Width >= tabText.Width && tabBounds.Height >= tabText.Height, "source tab text fits at " + dpi + "/" + source);
                    CaptureImage(form, Path.Combine(directory, "mouse-udlr-settings-" + (source == 0 ? "trackball-" : "paddle-") + dpi + ".png"));
                }
                form.sourceTabs.SelectedIndex = 0;
                foreach (NumericUpDown input in new[] { form.threshold, form.hold, form.wait })
                {
                    TableLayoutPanel row = (TableLayoutPanel)input.Parent!;
                    Control units = row.GetControlFromPosition(2, row.GetRow(input))!;
                    check(input.Right == units.Left, "unit touches numeric editor at " + dpi + ": " + input.AccessibleName);
                }
                foreach (ComboBox choice in form.directionChoices)
                {
                    int longest = choice.Items.Cast<TargetChoice>().Max(item => TextRenderer.MeasureText(item.Name, choice.Font).Width);
                    check(longest + (int)Math.Ceiling(24 * dpi / 96.0) <= choice.ClientSize.Width,
                        "every output name fits the dropdown at " + dpi + ": " + choice.AccessibleName);
                }
                check(!All(form).OfType<Label>().Any(l => l.Text is "1–256" or "1–512"), "range labels are omitted");
                CaptureImage(form, Path.Combine(directory, "mouse-udlr-settings-" + dpi + ".png"));
            }
            form.AssignTarget(0, 3); form.AssignTarget(1, 2); form.AssignTarget(2, 1); form.AssignTarget(3, 4);
            CaptureImage(form, Path.Combine(directory, "mouse-udlr-settings-assigned.png"));
            form.AssignTarget(2, 2, 1); form.AssignTarget(3, 3, 1); form.sourceTabs.SelectedIndex = 1;
            CaptureImage(form, Path.Combine(directory, "mouse-udlr-settings-paddle-assigned.png"));
            form.sourceTabs.SelectedIndex = 0;
            check(form.buttonMaps[0] == 0x04010203 && form.buttonMaps[1] == 0x0302110F, "tab changes retain independent drafts");
            form.threshold.Value = 256; form.hold.Value = 512; form.wait.Value = 1; form.cancel.PerformClick();
            check(!form.Saved && writes == 0 && device == initial, "Cancel closes without persisting edits");
        }
        using (MouseUdlrSettingsDialog form = new(initial, client.SaveAsync) { Opacity = 0, ShowInTaskbar = false })
        {
            form.Show(); form.threshold.Value = 256; form.hold.Value = 512; form.wait.Value = 1; form.AssignTarget(0, 3); form.AssignTarget(3, 2, 1); failWrite = true; await form.SaveSettings();
            check(!form.Saved && form.Visible && form.threshold.Value == 256 && form.hold.Value == 512 && form.wait.Value == 1 && form.buttonMaps[0] == 0x10121103 && form.buttonMaps[1] == 0x0212110F && form.sourceTabs.Enabled && form.directionChoices.All(c => c.Enabled) && form.resetTargets.All(b => b.Enabled) && form.save.Enabled, "failed save keeps the dialog and entered values");
            check(form.status.Text.Contains("failed"), "save error is visible inline");
            failWrite = false; int beforeReads = reads; await form.SaveSettings();
            check(form.Saved && form.DialogResult == DialogResult.OK && device == new MouseUdlrSettings(256, 512, 1, true, 0x10121103, 0x0212110F) && reads == beforeReads + 1, "successful retry writes and verifies a separate readback");
        }
        device = initial; failReadback = true; bool unverified = false;
        try { await client.SaveAsync(initial, new(1, 1, 512, true)); } catch (IOException) { unverified = true; }
        check(unverified && device == new MouseUdlrSettings(1, 1, 512, true), "unpersisted readback cannot be reported as success");
        failReadback = false; device = initial; wrongMap = true; bool wrongAssignment = false;
        try { await client.SaveAsync(initial, initial with { ButtonMap = 0x04010203 }); } catch (IOException) { wrongAssignment = true; }
        check(wrongAssignment, "mapping mismatch in separate readback cannot report success"); wrongMap = false;
        device = initial; wrongPaddleMap = true; bool wrongPaddle = false;
        try { await client.SaveAsync(initial, initial with { ButtonMap = 0x04010203, PaddleButtonMap = 0x0302110F }); } catch (IOException) { wrongPaddle = true; }
        check(wrongPaddle, "paddle mismatch in separate readback cannot report success"); wrongPaddleMap = false;
        using (MouseUdlrSettingsDialog reopened = new(device, client.SaveAsync))
        {
            check(reopened.buttonMaps[1] == 0x0302110F && reopened.directionChoices.Skip(4).Select(c => ((TargetChoice)c.SelectedItem!).Target).SequenceEqual(new byte[] { 15, 17, 2, 3 }) && reopened.buttonMaps[0] == 0x04010203 && reopened.directionChoices.Take(4).Select(c => ((TargetChoice)c.SelectedItem!).Target).SequenceEqual(new byte[] { 3, 2, 1, 4 }), "reopening restores all persisted target assignments");
        }
        MouseUdlrSettings legacy = initial with { ButtonMap = 0, PaddleButtonMap = 0 };
        using (MouseUdlrSettingsDialog restored = new(legacy, (old, next) =>
        {
            check(old.ButtonMap == 0 && old.PaddleButtonMap == 0 && next.ButtonMap == MouseUdlrSettings.DefaultButtonMap && next.PaddleButtonMap == MouseUdlrSettings.DefaultButtonMap, "legacy zero save guards raw settings and writes explicit defaults");
            return Task.FromResult(next);
        }) { Opacity = 0, ShowInTaskbar = false })
        {
            restored.Show();
            check(restored.directionChoices.Take(4).Select(c => c.Text).SequenceEqual(new[] { "↑", "↓", "←", "→" }), "legacy unassigned bytes display the actual directional outputs");
            await restored.SaveSettings(); check(restored.Saved, "legacy default mappings save successfully");
        }
        TaskCompletionSource<MouseUdlrSettings> pending = new();
        using (MouseUdlrSettingsDialog form = new(initial, (_, _) => pending.Task) { Opacity = 0, ShowInTaskbar = false })
        {
            form.Show(); Task savingTask = form.SaveSettings();
            check(!form.save.Enabled && !form.cancel.Enabled && !form.threshold.Enabled && !form.hold.Enabled && !form.wait.Enabled && !form.sourceTabs.Enabled && form.directionChoices.All(c => !c.Enabled) && form.resetTargets.All(b => !b.Enabled), "controls are disabled while save is pending");
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
        Capabilities old = caps! with { Features = 1048575 }, current = caps! with { Features = 2097151 };
        check(old.SupportsDirectionTargets && !old.SupportsDirectionSources && current.SupportsDirectionSources && !current.SupportsVirtualRolling && (current with { Features = 4194303 }).SupportsVirtualRolling, "source mapping capability distinguishes older shared-map firmware");
        check(!(current with { Features = 4194303 }).SupportsTrackballHold && (current with { Features = 8388607 }).SupportsTrackballHold, "Hold capability requires the new mode schema");
        check(!(current with { Features = 8388607 }).SupportsRollingStep && (current with { Features = 16777215 }).SupportsRollingStep, "rotation-step capability requires UDS7");
    }
}

