using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

internal static class LayoutSuite
{
    const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    [DllImport("user32.dll")] static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    static readonly List<string> checks = [];
    static Assembly product = null!;
    static object Field(object target, string name) => target.GetType().GetField(name, Members)!.GetValue(target)!;
    static object? Call(object target, string name, params object?[] arguments) => target.GetType().GetMethod(name, Members)!.Invoke(target, arguments);
    static Type Type(string name) => product.GetType("ControllerBridge." + name)!;
    static object New(string name, params object?[] arguments) => Activator.CreateInstance(Type(name), Members, null, arguments, null)!;
    static IEnumerable<Control> All(Control parent)
    { foreach (Control child in parent.Controls) { yield return child; foreach (Control nested in All(child)) yield return nested; } }
    static void Check(bool condition, string description)
    { if (!condition) throw new InvalidDataException(description); checks.Add(description); }
    static void Pump(Form form)
    { form.PerformLayout(); Application.DoEvents(); form.PerformLayout(); Application.DoEvents(); }
    static void Show(Form form)
    { form.Opacity = 0; form.ShowInTaskbar = false; form.Show(); Pump(form); }
    static void Capture(Form form, string directory, string name)
    {
        Pump(form);
        using Bitmap image = new(form.Width, form.Height); form.DrawToBitmap(image, new(0, 0, image.Width, image.Height)); image.Save(Path.Combine(directory, name + ".png"));
    }
    static void Transition(Form form, int dpi)
    {
        // WinForms explicitly accepts a test DPI in BEFOREPARENT.wParam.
        // Use the same bottom-up / top-down notification order as Windows.
        Control[] controls = All(form).ToArray(); foreach (Control c in controls) _ = c.Handle;
        float factor = (float)dpi / form.DeviceDpi;
        NativeRect rectangle = new() { Left = 20, Top = 20, Right = 20 + (int)Math.Round(form.Width * factor), Bottom = 20 + (int)Math.Round(form.Height * factor) };
        foreach (Control c in controls.Reverse()) SendMessage(c.Handle, 0x02e2, dpi, 0);
        nint pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try { Marshal.StructureToPtr(rectangle, pointer, false); SendMessage(form.Handle, 0x02e0, dpi | (dpi << 16), pointer); }
        finally { Marshal.FreeHGlobal(pointer); }
        foreach (Control c in controls) SendMessage(c.Handle, 0x02e3, 0, 0);
        Pump(form);
        Check(form.DeviceDpi == dpi && controls.Where(c => c.IsHandleCreated).All(c => c.DeviceDpi == dpi), "native DPI notifications reach form and controls: " + dpi);
    }
    static void Reachable(Control control, string context)
    {
        Rectangle bounds = control.Bounds;
        for (Control? c = control; c?.Parent is Control parent; c = parent)
        {
            Rectangle available = parent is ScrollableControl { AutoScroll: true } scroll ? scroll.DisplayRectangle : parent.ClientRectangle;
            available.Inflate(2, 2);
            Check(available.Contains(bounds), context + ": reachable " + control.Text + " / " + bounds + " within " + parent.GetType().Name + " " + available);
            if (parent is ScrollableControl { AutoScroll: true }) bounds = Rectangle.Intersect(bounds, parent.ClientRectangle);
            if (bounds.IsEmpty) break; // This parent can scroll the control into view.
            bounds.Offset(parent.Location);
        }
    }
    static void TextFits(Control control, string context)
    {
        if (string.IsNullOrEmpty(control.Text)) return;
        int width = control.ClientSize.Width - control.Padding.Horizontal;
        int height = control.ClientSize.Height - control.Padding.Vertical;
        using Graphics graphics = control.CreateGraphics();
        Size text = TextRenderer.MeasureText(graphics, control.Text, control.Font, new(Math.Max(1, width), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        Check(text.Height <= height + 1 && text.Width <= width + 1, context + ": text fits " + control.Text + $" ({text.Width}x{text.Height} in {width}x{height}, DPI {control.DeviceDpi})");
    }
    static void ButtonsFit(Form form, string context)
    {
        foreach (Button button in All(form).OfType<Button>().Where(b => b.Visible))
        { TextFits(button, context); Reachable(button, context); }
    }
    static void DialogCheck(Form form, string context)
    {
        if (form.GetType().Name == "RegistrationWizard")
        {
            foreach (int step in new[] { 0, 1, 2, 0 })
            {
                Call(form, "PreviewStep", step); Pump(form); ButtonsFit(form, context + " step " + step);
                var grid = (TableLayoutPanel)Field(Field(form, "editor"), "buttonGrid");
                // A never-shown page can defer handle creation and its first
                // monitor-DPI adjustment. Check the size when it is displayed.
                if (step == 1) Check(Math.Abs(grid.ColumnStyles[0].Width - 56 * form.DeviceDpi / 96f) <= 2, context + $": wizard editor uses current DPI ({grid.ColumnStyles[0].Width} at {form.DeviceDpi})");
            }
        }
        else ButtonsFit(form, context);
    }
    static void MainCheck(Form form, string context)
    {
        var tabs = (TabControl)Field(form, "outputTabs"); var controller = (RadioButton)Field(form, "gamepadButtons");
        foreach ((int index, bool pad) in new[] { (0, false), (1, false), (2, false), (2, true) })
        {
            tabs.SelectedIndex = index; if (index == 2) controller.Checked = pad; Pump(form);
            Control editor = (Control)form.GetType().GetProperty("ActiveEditor", Members)!.GetValue(form)!;
            var grid = (TableLayoutPanel)Field(editor, "buttonGrid");
            float expected = (index == 0 || pad ? 56 : 170) * form.DeviceDpi / 96f;
            Check(Math.Abs(grid.ColumnStyles[0].Width - expected) <= 2, context + $": mapping grid keeps DPI dimensions for mode {index}/{pad}: {grid.ColumnStyles[0].Width} vs {expected}");
            if (index > 0 && !pad) Check(Math.Abs(grid.RowStyles[1].Height - (index == 2 ? grid.RowStyles[0].Height : 0)) <= 1, context + ": mouse navigation row matches first row");
            if (index > 0)
            {
                var paddle = (ComboBox)Field(form, "paddleAxis"); paddle.SelectedIndex = 1; Pump(form);
                Check(paddle.Visible && paddle.Enabled && paddle.Items.Count == 2, context + ": paddle axis available");
                foreach (Control control in paddle.Parent!.Controls) Reachable(control, context + ": paddle row");
                Size text = TextRenderer.MeasureText(paddle.Text, paddle.Font);
                Check(text.Width + SystemInformation.VerticalScrollBarWidth <= paddle.ClientSize.Width, context + ": paddle selection text fits");
            }
            ButtonsFit(form, context + $" mode {index}/{pad}");
        }
    }
    static void Fit(Form form, Size minimum, Rectangle work)
    {
        Type("Ui").GetMethod("FitToWorkingArea", Members)!.Invoke(null, new object?[] { form, minimum, work }); Pump(form);
        Check(work.Contains(form.Bounds), "window stays in bounded work area: " + form.GetType().Name);
    }
    internal static void Run(string assemblyPath, int startupDpi, string directory, string language, bool transitions)
    {
        Directory.CreateDirectory(directory);
        Type scaleHelper = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("System.Windows.Forms.ScaleHelper")).First(t => t is not null)!;
        scaleHelper.GetField("<InitialSystemDpi>k__BackingField", Members)!.SetValue(null, startupDpi);
        product = Assembly.LoadFrom(assemblyPath);
        object[] languageArgs = { new[] { "--language", language } }; Type("L").GetMethod("Initialize", Members)!.Invoke(null, languageArgs);
        using Form main = (Form)New("MainForm", true, false, false); Show(main); ((System.Windows.Forms.Timer)Field(main, "timer")).Stop();
        int selected = (int)Field(main, "selected"); object profile = ((Array)Field(main, "profiles")).GetValue(selected)!;
        profile.GetType().GetProperty("Flags")!.SetValue(profile, (ushort)1);
        object fields = Field(main, "fields");
        object caps = Field(main, "caps");
        Array group = Array.CreateInstance(Type("DeviceProfile"), 1); group.SetValue(profile, 0);
        object package = Type("FirmwarePackage").GetMethod("Load", Members)!.Invoke(null, null)!;
        string version = (string)package.GetType().GetProperty("Version")!.GetValue(package)!;
        object fakeDevice = New("FirmwareChecks+FakeDevice", "0.0.1", version);
        List<(string Name, Form Form)> dialogs = [
            ("led", (Form)New("LedRgbDialog", profile)),
            ("shift", (Form)New("ShiftDialog", profile, fields, true, true, new Func<Task<bool>>(() => Task.FromResult(true)))),
            ("rapid", (Form)New("RapidFireDialog", profile)),
            ("axis", (Form)New("AxisDialog", 0, New("AxisBinding"), fields, true, null)),
            ("switch", (Form)New("SwitchMethodDialog", group, (uint)0, false)),
            ("wizard", (Form)New("RegistrationWizard", 0, caps, fields, new Func<Task<bool>>(() => Task.FromResult(true)), null)),
            ("log", (Form)New("InputLogForm", caps, fields)),
            ("firmware", (Form)New("FirmwareUpdateDialog", package, fakeDevice, null))
        ];
        try
        {
            foreach (var entry in dialogs) Show(entry.Form);
            Call(dialogs[0].Form, "CheckSetColor", (byte)80, (byte)64, (byte)160); Pump(dialogs[0].Form);
            MainCheck(main, "startup " + startupDpi);
            foreach (var entry in dialogs) { DialogCheck(entry.Form, entry.Name + " startup " + startupDpi); Capture(entry.Form, directory, entry.Name + "-startup"); }
            Capture(main, directory, "main-startup");
            if (transitions)
            {
                foreach (int next in new[] { 120, 144, 192, 96, 192, 96 })
                {
                    Transition(main, next); MainCheck(main, "transition " + next);
                    foreach (var entry in dialogs) { Transition(entry.Form, next); Capture(entry.Form, directory, entry.Name + "-" + next); DialogCheck(entry.Form, entry.Name + " transition " + next); }
                    if (next is 192 or 96) { Capture(main, directory, "main-" + next); Capture(dialogs[0].Form, directory, "led-" + next); }
                }
            }
            Fit(main, new(1290, 700), new(0, 0, 1920, 1040)); MainCheck(main, "FHD work area"); Capture(main, directory, "main-fhd");
            Fit(dialogs[1].Form, new(960, 880), new(0, 0, 1280, 680)); ButtonsFit(dialogs[1].Form, "short shift dialog");
            var apply = All(dialogs[1].Form).OfType<Button>().Single(b => b.Text == (language == "ja" ? "編集に反映" : "Apply to draft"));
            apply.Select(); dialogs[1].Form.ScrollControlIntoView(apply); Pump(dialogs[1].Form);
            Capture(dialogs[1].Form, directory, "shift-short-scrolled");
            Check(dialogs[1].Form.ClientRectangle.Contains(dialogs[1].Form.RectangleToClient(apply.RectangleToScreen(apply.ClientRectangle))), "Apply remains reachable by scrolling on a short display");
            File.WriteAllText(Path.Combine(directory, "dpi-checks.json"), JsonSerializer.Serialize(new { passed = true, count = checks.Count, startupDpi, language, simulatedDpiMessages = transitions, operatingSystemDisplayChanged = false, physicalDeviceOpened = false, checks }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"DPI layout {language} initial {startupDpi}: {checks.Count} checks passed");
        }
        finally
        {
            foreach (var entry in dialogs) entry.Form.Dispose();
            Array.Clear((Array)Field(main, "dirty")); main.Close();
        }
    }
}
