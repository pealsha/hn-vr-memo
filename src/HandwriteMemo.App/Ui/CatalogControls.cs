namespace HandwriteMemo.App;

// Standard radio group keyboard/focus semantics with the existing Design A surface.
internal sealed class CatalogTab : RadioButton
{
    private bool _hovered;
    internal CatalogTab()
    {
        Appearance = Appearance.Button;
        FlatStyle = FlatStyle.Flat;
        AutoSize = false;
        Size = new(76, 32);
        Margin = new(3, 2, 3, 2);
        AccessibleRole = AccessibleRole.PageTab;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }
    protected override void OnCheckedChanged(EventArgs e)
    {
        Invalidate();
        base.OnCheckedChanged(e);
    }
    protected override void OnPaint(PaintEventArgs e) => DesignButtonPaint.Draw(this, e.Graphics, _hovered, false, Checked, Focused && ShowFocusCues);
}

internal sealed class PageListBox : ListBox
{
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0204) // WM_RBUTTONDOWN: native ListBox would collapse an extended selection first.
        {
            Focus();
            OnMouseDown(new MouseEventArgs(MouseButtons.Right, 1,
                (short)((long)m.LParam & 0xffff), (short)(((long)m.LParam >> 16) & 0xffff), 0));
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }
}

internal sealed class TrashDeleteDialog : Form
{
    internal TrashDeleteDialog(int count, bool emptyTrash)
    {
        Text = emptyTrash ? "ゴミ箱を空にする" : "完全に削除";
        Font = UiTheme.DesktopFont;
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Text;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var layout = new TableLayoutPanel { AutoSize = true, Padding = new(20), ColumnCount = 1, RowCount = 2 };
        var message = new Label
        {
            AutoSize = true,
            Margin = new(0, 0, 0, 20),
            Text = emptyTrash
                ? $"ゴミ箱の{count}件を完全に削除しますか？\n\n元に戻せません。JSON・PNG・バックアップも削除します。"
                : $"選択した{count}件を完全に削除しますか？\n\n元に戻せません。JSON・PNG・バックアップも削除します。"
        };
        void SizeMessage()
        {
            message.MaximumSize = new(message.LogicalToDeviceUnits(440), 0);
            message.MinimumSize = new(message.LogicalToDeviceUnits(400), 0);
        }
        message.HandleCreated += (_, _) => SizeMessage();
        message.DpiChangedAfterParent += (_, _) => SizeMessage();
        SizeMessage();
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new(0) };
        var cancel = new DesignButton { Text = "キャンセル", AutoSize = true, DialogResult = DialogResult.Cancel };
        var delete = new DesignButton { Text = "削除", AutoSize = true, DialogResult = DialogResult.OK };
        actions.Controls.AddRange([cancel, delete]);
        layout.Controls.Add(message, 0, 0);
        layout.Controls.Add(actions, 0, 1);
        Controls.Add(layout);
        AcceptButton = cancel;
        CancelButton = cancel;
        Shown += (_, _) => cancel.Focus();
    }
}
