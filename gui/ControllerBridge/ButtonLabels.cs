namespace ControllerBridge;

// These are saved presentation IDs, not list positions or output-mode IDs.
// An empty name never disables a mapping or changes its output bit.
internal static class ButtonLabels
{
    internal sealed record SystemChoice(byte Id, string Name, string[] Names);
    private static readonly string[] switchNames = ["Y", "B", "A", "X", "L", "R", "ZL", "ZR", "−", "+", L.T("L押込", "L stick"), L.T("R押込", "R stick"), "Home", L.T("撮影", "CAPTURE")];
    internal static readonly SystemChoice[] Systems = [
        new(1, "Switch", switchNames),
        new(2, "Xbox 360", ["X","A","B","Y","LB","RB","LT","RT","BACK","START",L.T("L押込","L stick"),L.T("R押込","R stick"),"GUIDE",""]),
        new(13, "PS3", ["□","×","○","△","L1","R1","L2","R2","SELECT","START","L3","R3","PS",""]),
        new(3, "PS4", ["□","×","○","△","L1","R1","L2","R2","SHARE","OPTIONS","L3","R3","PS",""]),
        new(4, "POLYMEGA / PlayStation", ["□","×","○","△","L1","R1","L2","R2","SELECT","START","L3","R3","Home",""]),
        new(5, L.T("POLYMEGA / サターン","POLYMEGA / Saturn"), ["A","B","C","Y","X","Z","L","R","","START","","","Home",""]),
        // Provisional fighting-game layouts; described as reference mappings in the user guide.
        new(6, L.T("POLYMEGA / サターン（格闘）","POLYMEGA / Saturn (Fighting)"), ["X","A","B","Y","C","Z","L","R","","START","","","Home",""]),
        new(7, L.T("POLYMEGA / メガドライブ","POLYMEGA / Mega Drive"), ["A","B","C","Y","X","Z","","","MODE","START","","","Home",""]),
        new(8, L.T("POLYMEGA / メガドライブ（格闘）","POLYMEGA / Mega Drive (Fighting)"), ["X","A","B","Y","C","Z","","","MODE","START","","","Home",""]),
        new(9, L.T("POLYMEGA / スーパーファミコン","POLYMEGA / Super Nintendo"), ["Y","B","A","X","","","L","R","SELECT","START","","","Home",""]),
        new(10, L.T("POLYMEGA / ネオジオCD","POLYMEGA / Neo Geo CD"), ["A","B","D","C","","","","","SELECT","START","","","Home",""]),
        new(11, L.T("POLYMEGA / ファミコン","POLYMEGA / NES"), ["","B","A","","","","","","SELECT","START","","","Home",""]),
        // PC Engine IV/VI positions are unresolved; retain empty editable buttons.
        new(12, L.T("POLYMEGA / PCエンジン","POLYMEGA / PC Engine"), ["III","I","II","V","","","","","SELECT","RUN","","","Home",""])
    ];
    // Former None (ID 0) was identical to Switch. Keep stored profiles readable
    // without presenting the duplicate choice or changing their saved bytes.
    internal static bool Valid(byte system) => system == 0 || Systems.Any(s => s.Id == system);
    internal static SystemChoice Find(byte system) => Systems.Single(s => s.Id == (system == 0 ? 1 : system));
    internal static string Name(byte system, int index) => index is >= 14 and < 18 ? DeviceProfile.Targets[index] :
        Find(system).Names[index];
    internal static string Describe(byte system, int index)
    {
        string name = Name(system, index);
        return name.Length != 0 ? name : L.T($"無名 {index + 1:00}", $"Unnamed {index + 1:00}");
    }
    internal static string Caption(byte system, int index)
    {
        string name = Name(system, index);
        return index is 10 or 11 && name.EndsWith(L.T("押込", " stick")) ? name[..1] + L.T("\n押込", "\nstick") : name;
    }
}

// Keep the selection compact while giving localized system names readable text.
internal sealed class ButtonNameComboBox : ComboBox
{
    internal ButtonNameComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList; DrawMode = DrawMode.OwnerDrawFixed;
        Font = new Font(L.FontFamily, 13, FontStyle.Bold);
        IntegralHeight = false; DisplayMember = nameof(ButtonLabels.SystemChoice.Name);
        Items.AddRange(ButtonLabels.Systems.Cast<object>().ToArray());
        UpdateDimensions();
    }
    private void UpdateDimensions()
    {
        if (Items.Count == 0) return;
        int width = ButtonLabels.Systems.Max(choice => TextRenderer.MeasureText(choice.Name, Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width) + LogicalToDeviceUnits(38);
        ItemHeight = LogicalToDeviceUnits(38); DropDownHeight = LogicalToDeviceUnits(380);
        MinimumSize = new(width, 0); Width = width; DropDownWidth = width;
    }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdateDimensions(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); UpdateDimensions(); }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        e.DrawBackground();
        string text = e.Index >= 0 && e.Index < Items.Count && Items[e.Index] is ButtonLabels.SystemChoice choice ? choice.Name : Text;
        Rectangle bounds = Rectangle.Inflate(e.Bounds, -LogicalToDeviceUnits(8), 0);
        TextRenderer.DrawText(e.Graphics, text, Font, bounds, Enabled ? e.ForeColor : Ui.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        e.DrawFocusRectangle();
    }
}
