using HandwriteMemo.Core;

namespace HandwriteMemo.App;

internal sealed partial class DesktopWindow
{
    private const int ListPaneWidth = 290;
    private Action? _sizeResponsiveControls;

    public DesktopWindow(HandwriteEngine engine, MemoSession page, Func<double> seconds)
    {
        this._engine = engine;
        this._page = page;
        this._seconds = seconds;
        _catalog = new(page, () => RetentionDays);
        _pages = _catalog.List;
        _actions = new(this, page, seconds, _catalog, _trash, _nameEditor, _toolTips,
            ReloadCatalog, UpdateState, () => _listTab.Checked = true, () => SetSelectionMode(false));
        Text = "HnVRMemo";
        Icon = UiTheme.AppIcon;
        Font = UiTheme.DesktopFont;
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Text;
        UiTheme.StyleMenu(_pageMenu);
        UiTheme.StyleMenu(_trashMenu);
        Size = new(1180, 840);
        MinimumSize = new(800, 620);
        StartPosition = FormStartPosition.CenterScreen;
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new(0), Margin = new(0) };
        split.ColumnStyles.Add(new(SizeType.Absolute, LogicalToDeviceUnits(ListPaneWidth)));
        split.ColumnStyles.Add(new(SizeType.Percent, 100));
        split.RowStyles.Add(new(SizeType.Percent, 100));
        var listPanel = new Panel { Dock = DockStyle.Fill, Margin = new(0) };
        var editorPanel = new Panel { Dock = DockStyle.Fill, Margin = new(1, 0, 0, 0) };
        split.Controls.Add(listPanel, 0, 0);
        split.Controls.Add(editorPanel, 1, 0);
        var appHeader = _appHeader;
        appHeader.ColumnStyles.Add(new(SizeType.Absolute, split.ColumnStyles[0].Width));
        appHeader.ColumnStyles.Add(new(SizeType.Percent, 100));
        var listHeader = new FlowLayoutPanel { Dock = DockStyle.Top, Margin = Padding.Empty, Padding = new(8, 4, 8, 4), WrapContents = false };
        listHeader.Controls.AddRange([_listTab, _trash]);
        appHeader.Controls.Add(listHeader, 0, 0);
        _settingsButton.Anchor = AnchorStyles.Right;
        _settingsButton.Margin = new(8, 4, 8, 4);
        _settingsButton.Click += (_, _) => OpenSettings();
        _toolTips.SetToolTip(_settingsButton, _settingsButton.AccessibleName);
        appHeader.Controls.Add(_settingsButton, 1, 0);
        var reload = new ToolbarTile("更新", ToolbarIcon.Refresh);
        _toolTips.SetToolTip(reload, "更新");
        BindReloadButton(reload);
        _trash.ContextMenuStrip = _trashMenu;
        BindTrashMenu();
        var listActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new(8) };
        // Select All sits with the batch actions at the bottom, so entering selection mode
        // leaves the header and the rows below it in place.
        listActions.Controls.AddRange([_selectAll, _pin, _remove, _restore, _delete]);
        listActions.SetFlowBreak(_selectAll, true);
        _selectionHeader.Controls.AddRange([_selectionCount, reload, _select]);
        listPanel.Controls.Add(_pages);
        listPanel.Controls.Add(listActions);
        listPanel.Controls.Add(_selectionHeader);
        var editorHeader = CreateEditorHeader();
        var newPage = new DesignButton { Text = "新しいページ", LeadingIcon = ToolbarIcon.NewPage, AutoSize = true, Margin = new(2) };
        _tools = new DesktopToolbar { Dock = DockStyle.Top };
        _tools.AddGroup("道具", _pen, _eraseStroke, _erasePart);
        foreach (var button in new[] { _pen, _eraseStroke, _erasePart })
            _toolTips.SetToolTip(button, button.AccessibleName);
        var colorGroup = _tools.AddGroup("色");
        string[] names = ["黒", "赤", "青", "緑"];
        for (int i = 0; i < ToolPalette.Colors.Count; i++)
        {
            int index = i;
            var button = SampleButton(names[i], ColorTranslator.FromHtml(ToolPalette.Colors[i]), () => 16 * _pen.Width / 36f);
            BindColorButton(button, index);
            _colors.Add(button);
            colorGroup.Controls.Add(button);
        }
        var widthGroup = _tools.AddGroup("太さ");
        string[] widthNames = ["細", "中", "太"];
        for (int i = 0; i < ToolPalette.Widths.Count; i++)
        {
            int index = i;
            var button = SampleButton(widthNames[i], Color.Black, () => WidthSampleDiameter(ToolPalette.Widths[index]));
            BindWidthButton(button, index);
            _widths.Add(button);
            widthGroup.Controls.Add(button);
        }
        BindDrawingTools();
        _tools.AddGroup("履歴", _undo, _redo);
        _pageGroup = _tools.AddGroup("ページ", _previous, _next, newPage);
        BindActionToolTips();
        _canvas = new MemoCanvas(engine, page, seconds, () => _desktopTool,
            () => engine.EraserMode)
        {
            Dock = DockStyle.Fill,
            Margin = new(12)
        };
        BindCanvas();
        editorPanel.Controls.Add(_canvas);
        editorPanel.Controls.Add(_tools);
        editorPanel.Controls.Add(editorHeader);
        var feedback = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 1, RowCount = 2, BackColor = UiTheme.Surface };
        _status.TextAlign = ContentAlignment.MiddleLeft;
        feedback.Controls.Add(_status, 0, 0);
        var recovery = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty };
        recovery.Controls.AddRange([_retrySave, _retryOpen, _details]);
        feedback.Controls.Add(recovery, 0, 1);
        editorPanel.Controls.Add(feedback);
        Controls.Add(split);
        Controls.Add(appHeader);
        int previousDpi = 0;
        bool previousLarge = false;
        _sizeResponsiveControls = () =>
        {
            int listWidth = LogicalToDeviceUnits(ListPaneWidth);
            _tools.UpdateButtonMetrics(ClientSize.Width - listWidth - editorPanel.Margin.Horizontal, DeviceDpi);
            if (previousDpi == DeviceDpi && previousLarge == _tools.LargeButtons)
                return;
            previousDpi = DeviceDpi;
            previousLarge = _tools.LargeButtons;

            split.SuspendLayout();
            appHeader.SuspendLayout();
            listPanel.SuspendLayout();
            listHeader.SuspendLayout();
            listActions.SuspendLayout();
            _selectionHeader.SuspendLayout();
            recovery.SuspendLayout();
            try
            {
                foreach (var button in new ButtonBase[] { _listTab, _trash, _select, _selectAll, _pin, _remove, _restore, _delete, _retrySave, _retryOpen, _details })
                    _tools.SizeTextButton(button);
                split.ColumnStyles[0].Width = appHeader.ColumnStyles[0].Width = listWidth;
                _catalog.ResizeRows();
                int height = _tools.ActionHeight;
                int available = listWidth - listHeader.Padding.Horizontal
                    - _listTab.Margin.Horizontal - _trash.Margin.Horizontal;
                _listTab.Size = _trash.Size = new(Math.Max(1, available / 2), height);
                _settingsButton.Size = new(height, height);
                listHeader.Height = height + listHeader.Padding.Vertical + _listTab.Margin.Vertical;
                // Share the column width so the two list actions stay on one row at every size.
                int actionWidth = Math.Max(1, (listWidth - listActions.Padding.Horizontal
                    - _pin.Margin.Horizontal - _remove.Margin.Horizontal) / 2);
                foreach (var action in new Button[] { _pin, _remove, _restore, _delete })
                {
                    action.AutoSize = false;
                    action.Size = new(actionWidth, height);
                }
                _selectAll.AutoSize = false;
                _selectAll.Size = new(2 * actionWidth + _pin.Margin.Horizontal, height);
                var padding = _selectionHeader.Padding;
                int gap = LogicalToDeviceUnits(4);
                int commandHeight = _tools.TileSide;
                int width = Math.Max(1, listWidth - padding.Horizontal);
                int selectWidth = Math.Max(LogicalToDeviceUnits(72), _select.GetPreferredSize(Size.Empty).Width);
                _select.Bounds = new(padding.Left + width - selectWidth, padding.Top, selectWidth, commandHeight);
                reload.Bounds = new(_select.Left - gap - commandHeight, padding.Top, commandHeight, commandHeight);
                _selectionCount.Font = _select.Font;
                _selectionCount.Bounds = new(padding.Left, padding.Top, Math.Max(1, reload.Left - gap - padding.Left), commandHeight);
                _selectionHeader.Height = padding.Vertical + commandHeight;
            }
            finally
            {
                listHeader.ResumeLayout(true);
                listActions.ResumeLayout(true);
                _selectionHeader.ResumeLayout(true);
                recovery.ResumeLayout(true);
                listPanel.ResumeLayout(true);
                appHeader.ResumeLayout(true);
                split.ResumeLayout(true);
            }
            _tools.PerformLayout();
        };
        split.DpiChangedAfterParent += (_, _) => PerformLayout();
        PerformLayout();
        _pages.Controls.Add(_nameEditor);
        _pages.ContextMenuStrip = _pageMenu;
        BindPageEvents(newPage);
        RefreshCatalog();
        UpdateState();
        _page.Changed += OnSessionChanged;
    }

    protected override void OnLoad(EventArgs e)
    {
        _tools.PerformLayout();
        Rectangle workArea = Screen.FromControl(this).WorkingArea;
        int widthForSingleRow = Width - _tools.Width + _tools.SingleRowWidth;
        int startupWidth = Math.Max(MinimumSize.Width, Math.Min(workArea.Width, Math.Max(Width, widthForSingleRow)));
        if (startupWidth != Width)
            SetBounds(workArea.Left + Math.Max(0, (workArea.Width - startupWidth) / 2), Top, startupWidth, Height, BoundsSpecified.X | BoundsSpecified.Width);
        base.OnLoad(e);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        // Resolve shared dimensions before WinForms docks the header, list and editor.
        _sizeResponsiveControls?.Invoke();
        base.OnLayout(levent);
    }

    private Panel CreateEditorHeader()
    {
        var heading = new Panel
        {
            Dock = DockStyle.Top,
            BackColor = UiTheme.Surface,
            Padding = new(12, 6, 12, 6)
        };
        _title.AutoEllipsis = true;
        _title.UseMnemonic = false;
        _title.Font = UiTheme.DesktopBoldFont;
        _titleDate.Font = UiTheme.DesktopSmallFont;
        _titleDate.AutoEllipsis = true;
        heading.Controls.Add(_title);
        heading.Controls.Add(_titleDate);

        void UpdateHeadingSize()
        {
            bool hasDetails = _titleDate.Text.Length > 0;
            _titleDate.Height = _titleDate.Font.Height;
            _titleDate.Visible = hasDetails;
            int headingHeight = _title.Font.Height + (hasDetails ? _titleDate.Height : 0);
            heading.Height = heading.Padding.Vertical + headingHeight;
        }
        _titleDate.TextChanged += (_, _) => UpdateHeadingSize();
        _titleDate.FontChanged += (_, _) => UpdateHeadingSize();
        _title.FontChanged += (_, _) => UpdateHeadingSize();
        heading.PaddingChanged += (_, _) => UpdateHeadingSize();
        heading.DpiChangedAfterParent += (_, _) => UpdateHeadingSize();
        UpdateHeadingSize();
        return heading;
    }
}
