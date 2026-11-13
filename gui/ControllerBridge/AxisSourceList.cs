namespace ControllerBridge;

// Each input is one 60/40 split choice: whole axis on the left, positive over
// negative on the right. Native buttons preserve keyboard and accessibility.
internal sealed class AxisSourceList : TableLayoutPanel
{
    private readonly List<SourceTile> tiles = [];
    private readonly ToolTip tips = new() { AutoPopDelay = 15000 };
    private bool arranging;
    internal event Action<InputField, int>? Choose;
    internal IReadOnlyList<InputField> Items => tiles.Select(t => t.Field).ToArray();
    internal AxisSourceList()
    {
        Dock = DockStyle.Top; AutoSize = false;
        Margin = Padding.Empty; Padding = Padding.Empty;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        SizeChanged += (_, _) => ArrangeTiles();
    }
    internal void SetInputs(IEnumerable<InputField> fields, Func<InputField, string> label, int axis = 0)
    {
        foreach (InputField f in fields)
        {
            SourceTile tile = new(f, label(f), tips, axis); tiles.Add(tile);
            tile.Choose += part => Choose?.Invoke(f, part);
        }
        ArrangeTiles();
    }
    internal void FitToArea() => ArrangeTiles();
    private void ArrangeTiles()
    {
        if (arranging) return;
        int rowHeight = LogicalToDeviceUnits(48);
        int availableHeight = Parent?.ClientSize.Height ?? rowHeight * 7;
        int availableRows = Math.Max(1, availableHeight / rowHeight);
        int columns = Math.Max(ClientSize.Width >= LogicalToDeviceUnits(960) ? 3 : 2,
            (tiles.Count + availableRows - 1) / availableRows);
        int rows = Math.Max(1, (tiles.Count + columns - 1) / columns);
        arranging = true;
        SuspendLayout();
        try
        {
            if (ColumnCount != columns || RowCount != rows || Controls.Count != tiles.Count)
            {
                Controls.Clear(); ColumnStyles.Clear(); RowStyles.Clear(); ColumnCount = columns; RowCount = rows;
                for (int i = 0; i < columns; i++) ColumnStyles.Add(new(SizeType.Percent, 100f / columns));
                for (int i = 0; i < rows; i++) RowStyles.Add(new(SizeType.Absolute, rowHeight));
                for (int i = 0; i < tiles.Count; i++) Controls.Add(tiles[i], i % columns, i / columns);
            }
            foreach (RowStyle row in RowStyles) row.Height = rowHeight;
            Height = rows * rowHeight;
        }
        finally { ResumeLayout(true); arranging = false; }
    }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); ArrangeTiles(); }
    internal void ShowSelection(AxisSelection selection)
    {
        foreach (SourceTile tile in tiles)
            tile.ShowSelection(AxisSelection.Matches(tile.Field, selection.Whole),
                AxisSelection.Matches(tile.Field, selection.Halves[0]), AxisSelection.Matches(tile.Field, selection.Halves[1]));
    }
    internal void RefreshValue(InputField field) => tiles.FirstOrDefault(t => ReferenceEquals(t.Field, field))?.RefreshValue();
    internal static int Midpoint(InputField field) => field.Pressure ? field.Minimum : field.Minimum < 0 && field.Maximum > 0 ? 0 :
        (int)(field.Minimum + ((long)field.Maximum - field.Minimum + 1) / 2);
    internal static double Position(InputField field, int value)
    {
        long origin = Midpoint(field), delta = (long)value - origin;
        long span = delta < 0 ? origin - field.Minimum : (long)field.Maximum - origin;
        return span <= 0 ? 0 : Math.Clamp((double)delta / span, -1, 1);
    }
    protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }

    private sealed class SourceTile : TableLayoutPanel
    {
        internal InputField Field { get; }
        private readonly InputButton whole;
        private readonly Button positive, negative;
        private readonly int axis;
        internal event Action<int>? Choose;
        internal SourceTile(InputField field, string label, ToolTip tips, int axis)
        {
            this.axis = axis; Field = field; Dock = DockStyle.Fill; Margin = new(4, 2, 4, 2); ColumnCount = 2; RowCount = 2;
            ColumnStyles.Add(new(SizeType.Percent, 60)); ColumnStyles.Add(new(SizeType.Percent, 40));
            RowStyles.Add(new(SizeType.Percent, 50)); RowStyles.Add(new(SizeType.Percent, 50));
            whole = new(field) { Text = label, AccessibleName = label + L.T(" 全体を割り当て・解除", " assign or clear whole axis") };
            whole.Click += (_, _) => Choose?.Invoke(-1);
            positive = Part(AxisDirections.Label(axis, 1), 1); negative = Part(AxisDirections.Label(axis, 0), 0);
            positive.Enabled = negative.Enabled = !field.Gyroscope;
            Controls.Add(whole, 0, 0); SetRowSpan(whole, 2); Controls.Add(positive, 1, 0); Controls.Add(negative, 1, 1);
            string origin = $"IF{field.Interface} / ID{field.ReportId} / bit{field.BitOffset}";
            tips.SetToolTip(whole, label + L.T("：全体に割り当て。同じ場所をもう一度押すと解除。", ": assign the whole axis. Press again to clear.") + "\n" + origin);
            tips.SetToolTip(positive, label + "：" + AxisDirections.Label(axis, 1) + L.T("に割り当て・解除。", " — assign or clear."));
            tips.SetToolTip(negative, label + "：" + AxisDirections.Label(axis, 0) + L.T("に割り当て・解除。", " — assign or clear."));
        }
        private Button Part(string label, int part)
        {
            Button b = Ui.Button(label, (_, _) => Choose?.Invoke(AxisDirections.ProfileSide(axis, part)));
            b.AutoSize = false; b.MinimumSize = Size.Empty; b.Dock = DockStyle.Fill; b.Margin = Padding.Empty; b.Padding = new(4, 1, 4, 1);
            b.SizeChanged += (_, _) => b.Text = b.Width >= TextRenderer.MeasureText(label, b.Font).Width + b.Padding.Horizontal ? label : AxisDirections.Label(axis, part, compact: true);
            b.AccessibleName = Field.Label + " " + label + L.T("を割り当て・解除", " assign or clear"); return b;
        }
        internal void ShowSelection(bool all, bool minus, bool plus)
        {
            if ((axis & 1) != 0) (minus, plus) = (plus, minus);
            Ui.ButtonColors(whole, false, all); Ui.ButtonColors(negative, false, minus); Ui.ButtonColors(positive, false, plus);
            foreach ((Button button, bool selected) in new[] { ((Button)whole, all), (negative, minus), (positive, plus) })
            { button.AccessibleDescription = selected ? L.T("選択中。もう一度押すと解除。", "Selected. Press again to clear.") : L.T("未選択", "Not selected"); button.Invalidate(); }
        }
        internal void RefreshValue() => whole.Invalidate();
    }
    private sealed class InputButton : DarkButton
    {
        private readonly InputField field;
        internal InputButton(InputField field)
        {
            this.field = field; Dock = DockStyle.Fill; AutoSize = false; Margin = Padding.Empty;
            Padding = new(7, 1, 7, 17); FlatStyle = FlatStyle.Flat; UseVisualStyleBackColor = false; Cursor = Cursors.Hand;
            Ui.ButtonColors(this, false);
        }
        protected override TextFormatFlags LabelFlags => TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int U(int value) => LogicalToDeviceUnits(value);
            Rectangle track = new(U(8), Height - U(14), Math.Max(U(16), Width - U(68)), U(7));
            using SolidBrush empty = new(Ui.Input); e.Graphics.FillRectangle(empty, track);
            int origin = field.Pressure ? track.Left : track.Left + track.Width / 2;
            if (field.Valid)
            {
                double value = Position(field, field.Value);
                int end = origin + (int)Math.Round(value * (value < 0 ? origin - track.Left : track.Right - origin));
                using SolidBrush fill = new(Ui.Focus);
                e.Graphics.FillRectangle(fill, Math.Min(origin, end), track.Top, Math.Abs(end - origin), track.Height);
            }
            using Pen mark = new(Ui.Muted); e.Graphics.DrawLine(mark, origin, track.Top - U(2), origin, track.Bottom + U(2));
            TextRenderer.DrawText(e.Graphics, field.Valid ? field.Value.ToString("N0") : "—", Font,
                new Rectangle(track.Right + U(4), Height - U(21), Width - track.Right - U(10), U(19)), Ui.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
