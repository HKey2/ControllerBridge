namespace ControllerBridge;

internal sealed class SlotMoveEventArgs(int source, int destination) : EventArgs
{
    public int Source { get; } = source;
    public int Destination { get; } = destination;
}

internal sealed class SlotList : ListBox
{
    private sealed record DragSlot(SlotList Owner, int Index);
    private Point mouseDown;
    private int dragSource = -1, insertion = -1;
    private readonly System.Windows.Forms.Timer scrollTimer = new() { Interval = 100 };
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<int, bool>? CanMove { get; set; }
    public event EventHandler<SlotMoveEventArgs>? MoveRequested;
    public int DragSource { get; private set; } = -1;
    public SlotList()
    {
        Dock = DockStyle.Fill; DrawMode = DrawMode.OwnerDrawFixed; IntegralHeight = false;
        BorderStyle = BorderStyle.Fixed3D; AllowDrop = true; ItemHeight = 58;
        AccessibleName = L.T("プロファイルリスト", "Profile list");
        AccessibleDescription = L.T("ドラッグして並べ替え。Altと上下キーでも移動できます。", "Drag to reorder. You can also use Alt and the arrow keys.");
        scrollTimer.Tick += (_, _) =>
        {
            Point at = PointToClient(Cursor.Position); int edge = LogicalToDeviceUnits(28);
            if (at.Y < edge && TopIndex > 0) TopIndex--;
            else if (at.Y > ClientSize.Height - edge && TopIndex < Items.Count - 1) TopIndex++;
            SetInsertion(at);
        };
    }
    protected override void Dispose(bool disposing) { if (disposing) scrollTimer.Dispose(); base.Dispose(disposing); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); mouseDown = e.Location;
        dragSource = e.Button == MouseButtons.Left ? IndexFromPoint(e.Location) : -1;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.Button != MouseButtons.Left || dragSource < 0 || CanMove?.Invoke(dragSource) != true) return;
        Size threshold = SystemInformation.DragSize;
        if (new Rectangle(mouseDown.X - threshold.Width / 2, mouseDown.Y - threshold.Height / 2, threshold.Width, threshold.Height).Contains(e.Location)) return;
        DragSource = dragSource; Invalidate(GetItemRectangle(dragSource));
        try { DoDragDrop(new DragSlot(this, dragSource), DragDropEffects.Move); }
        finally { DragSource = dragSource = insertion = -1; scrollTimer.Stop(); Invalidate(); }
    }
    private bool Accept(DragEventArgs e) => e.Data?.GetData(typeof(DragSlot)) is DragSlot data && data.Owner == this && CanMove?.Invoke(data.Index) == true;
    protected override void OnDragEnter(DragEventArgs e) { base.OnDragEnter(e); OnDragOver(e); }
    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effect = Accept(e) ? DragDropEffects.Move : DragDropEffects.None;
        if (e.Effect == DragDropEffects.None) { ClearInsertion(); return; }
        SetInsertion(PointToClient(new(e.X, e.Y))); scrollTimer.Start();
    }
    private void SetInsertion(Point at)
    {
        int row = IndexFromPoint(new Point(Math.Clamp(at.X, 1, Math.Max(1, ClientSize.Width - 2)), Math.Clamp(at.Y, 0, Math.Max(0, ClientSize.Height - 1))));
        int next = row < 0 ? Items.Count : row + (at.Y >= GetItemRectangle(row).Top + ItemHeight / 2 ? 1 : 0);
        if (next == insertion) return; insertion = next; Invalidate(); Update();
    }
    private void ClearInsertion() { scrollTimer.Stop(); if (insertion >= 0) { insertion = -1; Invalidate(); } }
    protected override void OnDragLeave(EventArgs e) { base.OnDragLeave(e); ClearInsertion(); }
    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e); int before = insertion;
        if (Accept(e) && before >= 0 && e.Data!.GetData(typeof(DragSlot)) is DragSlot data)
        {
            int destination = before > data.Index ? before - 1 : before;
            ClearInsertion();
            if (destination != data.Index) MoveRequested?.Invoke(this, new(data.Index, destination));
        }
        else ClearInsertion();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Alt && e.KeyCode is Keys.Up or Keys.Down && SelectedIndex >= 0 && CanMove?.Invoke(SelectedIndex) == true)
        {
            int destination = SelectedIndex + (e.KeyCode == Keys.Up ? -1 : 1);
            if (destination >= 0 && destination < Items.Count) MoveRequested?.Invoke(this, new(SelectedIndex, destination));
            e.Handled = e.SuppressKeyPress = true; return;
        }
        base.OnKeyDown(e);
    }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x000f || insertion < 0 || Items.Count == 0) return;
        using Graphics g = Graphics.FromHwnd(Handle);
        int y = insertion == Items.Count ? GetItemRectangle(Items.Count - 1).Bottom : GetItemRectangle(insertion).Top;
        y = Math.Clamp(y, 2, Math.Max(2, ClientSize.Height - 3));
        using Pen pen = new(Ui.Focus, LogicalToDeviceUnits(3)); using SolidBrush brush = new(Ui.Focus);
        int inset = LogicalToDeviceUnits(7);
        g.DrawLine(pen, inset, y, ClientSize.Width - inset, y);
        g.FillPolygon(brush, [new(1, y - inset), new(inset + 2, y), new(1, y + inset)]);
        g.FillPolygon(brush, [new(ClientSize.Width - 2, y - inset), new(ClientSize.Width - inset - 3, y), new(ClientSize.Width - 2, y + inset)]);
    }
}
