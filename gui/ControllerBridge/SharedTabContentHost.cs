namespace ControllerBridge;

// Tabs select the output mode; the shared settings keep a single, permanent
// parent. Reparenting live controls raises ambient font changes and can apply
// a cached DPI scale again, even when the monitor itself has not changed.
internal sealed class SharedTabContentHost : Panel
{
    private readonly DarkTabControl tabs;
    private readonly Control content;

    public SharedTabContentHost(DarkTabControl tabs, Control content)
    {
        this.tabs = tabs; this.content = content;
        Dock = DockStyle.Fill; Margin = Padding.Empty;
        tabs.Dock = DockStyle.Fill; content.Dock = DockStyle.None;
        Controls.Add(tabs); Controls.Add(content); content.BringToFront();
        tabs.HandleCreated += (_, _) => ArrangeContent();
        tabs.Layout += (_, _) => ArrangeContent();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); ArrangeContent();
    }

    private void ArrangeContent()
    {
        if (tabs is null || content is null || !tabs.IsHandleCreated) return;
        Rectangle area = tabs.DisplayRectangle;
        int inset = LogicalToDeviceUnits(5);
        area.Inflate(-inset, -inset);
        area.Width = Math.Max(0, area.Width); area.Height = Math.Max(0, area.Height);
        if (content.Bounds != area) content.Bounds = area;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // The shared page is a sibling of the native tab pages, so forward the
        // standard page shortcuts explicitly while preserving selection policy.
        int direction = keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.PageDown) ? 1 :
            keyData == (Keys.Control | Keys.Shift | Keys.Tab) || keyData == (Keys.Control | Keys.PageUp) ? -1 : 0;
        if (direction != 0) { tabs.SelectAdjacentAvailableTab(direction); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
