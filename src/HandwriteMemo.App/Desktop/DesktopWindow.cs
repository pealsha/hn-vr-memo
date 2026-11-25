using HandwriteMemo.Core;

namespace HandwriteMemo.App;

internal sealed partial class DesktopWindow : Form
{
    private readonly HandwriteEngine _engine;
    private readonly MemoSession _page;
    private readonly Func<double> _seconds;
    private readonly ToolbarTile _settingsButton = new("設定", ToolbarIcon.Settings) { Enabled = false };
    private Func<DesktopSettingsWindow>? _makeSettings;
    private DesktopSettingsWindow? _settingsWindow;
    private readonly DesktopCatalogView _catalog;
    private readonly DesktopPageActions _actions;
    private readonly ListBox _pages;
    private AppSettingsService? _settings;
    private int RetentionDays => _settings?.Current.TrashRetentionDays ?? 30;
    internal Button SettingsButton => _settingsButton;
    internal void ConfigureSettings(Func<DesktopSettingsWindow> create, AppSettingsService settings)
    {
        _makeSettings = create;
        if (_settings is not null)
            _settings.Changed -= OnSettingsChanged;
        _settings = settings;
        _settings.Changed += OnSettingsChanged;
        _settingsButton.Enabled = true;
    }

    private void OpenSettings()
    {
        _canvas.EndInput();
        _actions.EndRename(false);
        if (_settingsWindow is null || _settingsWindow.IsDisposed)
            _settingsWindow = _makeSettings!();
        if (!_settingsWindow.Visible)
            _settingsWindow.Show(this);
        _settingsWindow.Activate();
    }

    internal void FolderChanged()
    {
        _actions.ResetForFolder();
        SetSelectionMode(false);
        RefreshCatalog();
    }

    private readonly TableLayoutPanel _appHeader = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 2,
        RowCount = 1
    };
    private readonly RadioButton _listTab = new CatalogTab { Text = "一覧", Checked = true };
    private readonly RadioButton _trash = new CatalogTab { Text = "ゴミ箱" };
    private readonly Panel _selectionHeader = new() { Dock = DockStyle.Top, Padding = new(8, 0, 8, 8) };
    private readonly Label _selectionCount = new() { TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Button _select = new DesignButton() { Text = "選択" };
    private readonly Button _selectAll = new DesignButton() { Text = "すべて選択", Visible = false };
    private FlowLayoutPanel _pageGroup = null!;
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new(14, 8, 14, 4) };
    private readonly Button _retrySave = new DesignButton() { Text = "保存を再試行", AutoSize = true };
    private readonly Button _retryOpen = new DesignButton() { Text = "読み込みを再試行", AutoSize = true };
    private readonly Button _details = new DesignButton() { Text = "詳細", AutoSize = true };
    private readonly Label _title = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _titleDate = new() { Dock = DockStyle.Bottom, ForeColor = UiTheme.Secondary, Visible = false };
    private readonly PageTitleEditor _nameEditor = new() { Visible = false, MaxLength = 0, AccessibleName = "ページの名前" };
    private readonly Button _pin = new DesignButton() { Text = "☆ ピン留め", AutoSize = true };
    private readonly Button _remove = new DesignButton() { Text = "ゴミ箱へ", AutoSize = true };
    private readonly Button _restore = new DesignButton() { Text = "復元する", AutoSize = true };
    private readonly Button _delete = new DesignButton() { Text = "完全に削除", Visible = false };
    private readonly ToolbarTile _previous = new("前のページ", ToolbarIcon.PreviousPage);
    private readonly ToolbarTile _next = new("次のページ", ToolbarIcon.NextPage);
    private readonly MemoCanvas _canvas;
    private readonly DesktopToolbar _tools;
    private readonly ToolbarTile _pen = new("ペン", ToolbarIcon.Pen);
    private readonly ToolbarTile _eraseStroke = new("線ごと消す", ToolbarIcon.EraseStroke);
    private readonly ToolbarTile _erasePart = new("部分消し", ToolbarIcon.ErasePart);
    private readonly List<ToolbarTile> _widths = [];
    private readonly ToolTip _toolTips = new();
    private readonly ToolTip _disabledToolbarTips = new();
    private readonly System.Windows.Forms.Timer _disabledTipDelay = new();
    private readonly ContextMenuStrip _pageMenu = new();
    private readonly ContextMenuStrip _trashMenu = new();
    private PageCatalogEntry? _contextEntry;
    private bool _mouseContextRequest;
    private readonly ToolbarTile _undo = new("元に戻す", ToolbarIcon.Undo);
    private readonly ToolbarTile _redo = new("やり直す", ToolbarIcon.Redo);
    private readonly List<ToolbarTile> _colors = [];
    private DrawingTool _desktopTool = DrawingTool.Pen;
    private SessionChange _pendingSessionChange;
    private bool _sessionUpdatePosted, _catalogInvalidated;
    private (DrawingTool Tool, string Color, float Width, EraserMode Eraser)? _shownTools;
    private long _revision = -1;
    private (Guid Id, string Title, bool IsPinned) _displayedPage;
    internal IReadOnlyList<PageCatalogEntry> Entries => _catalog.Entries;
    internal MemoCanvas Canvas => _canvas;
    internal ListBox PagesList => _pages;
    internal Button RemoveButton => _remove;
    internal bool CanUndoTrash => _actions.CanUndoTrash;
    internal Button PinButton => _pin;
    internal Label StatusLabel => _status;
    internal Button NextButton => _next;
    internal Button RetryOpenButton => _retryOpen;
    internal TextBox NameEditor => _nameEditor;
    internal bool IsSelectionMode => _catalog.IsSelectionMode;
    internal Button SelectButton => _select;
    internal Button SelectAllButton => _selectAll;
    internal Label SelectionCountLabel => _selectionCount;
    internal Button RestoreButton => _restore;
    internal Button DeleteButton => _delete;

    private IReadOnlyList<PageCatalogEntry> SelectedEntries => _catalog.SelectedEntries;

    private void SetSelectionMode(bool enabled)
    {
        if (IsSelectionMode == enabled)
            return;
        _canvas.EndInput();
        _actions.EndRename(false);
        _pageMenu.Close();
        _catalog.SetSelectionMode(enabled, _trash.Checked);
        UpdateState(false);
    }

    private void SelectAllPages(bool select = true)
    {
        _catalog.SelectAllPages(select);
        UpdateState(false);
    }

    internal void SelectPages(IEnumerable<Guid> ids)
    {
        _catalog.SelectPages(ids);
        UpdateState(false);
    }

    internal void DispatchCommandKey(Keys keys)
    {
        var message = Message.Create(Handle, 0x0100, IntPtr.Zero, IntPtr.Zero);
        ProcessCmdKey(ref message, keys);
    }

    public void OpenWindow()
    {
        if (!Visible)
        {
            RefreshCatalog();
            Show();
        }
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    internal void RefreshCatalog() => ReloadCatalog(selectCurrent: false);

    private void ReloadCatalog(bool selectCurrent)
    {
        if (_nameEditor.Visible)
            return; // Autosave must not replace the row while its name is being edited.
        _catalogInvalidated = false;
        _actions.RunOperation(() => _catalog.Reload(_trash.Checked, selectCurrent, _displayedPage.Id),
            "一覧を読み込めませんでした。今の筆跡とファイルは残っています。「詳細」を確認し、回転矢印で一覧を更新してください。");
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is (Keys.Control | Keys.Tab) or (Keys.Control | Keys.Shift | Keys.Tab))
        {
            if (_trash.Checked)
                _listTab.Checked = true;
            else
                _trash.Checked = true;
            return true;
        }
        if (_nameEditor.Visible && _nameEditor.ContainsFocus)
        {
            // Let the native text box/IME handle text undo and composition confirmation.
            if (_nameEditor.IsComposing)
                return base.ProcessCmdKey(ref msg, keyData);
            if (keyData is Keys.Enter or Keys.Escape)
            {
                _actions.EndRename(keyData == Keys.Enter, focusList: true);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        if (IsSelectionMode && keyData == Keys.Escape)
        {
            SetSelectionMode(false);
            _select.Focus();
            return true;
        }
        if (_pages.ContainsFocus && keyData == Keys.F2)
        {
            if (!IsSelectionMode && _pages.SelectedItems.Count == 1 && _pages.SelectedItem is PageCatalogEntry entry)
                _actions.BeginRename(entry);
            return true;
        }
        if (_pages.ContainsFocus && keyData == (Keys.Control | Keys.A))
        {
            SelectAllPages();
            return true;
        }
        if (keyData == (Keys.Control | Keys.Z) && (_pages.Parent!.ContainsFocus || _listTab.Focused || _trash.Focused))
        {
            // The whole catalog column (tabs and batch buttons too) uses trash-move undo.
            // The native title editor was handled above; never fall through to ink history here.
            _actions.UndoTrash();
            return true;
        }
        if (_pages.ContainsFocus && keyData == Keys.Delete)
        {
            _actions.RunOperation(() =>
            {
                if (_trash.Checked)
                    _actions.ConfirmDelete(SelectedEntries);
                else
                    _actions.TrashEntries(SelectedEntries);
            });
            return true;
        }
        if (_pages.ContainsFocus && keyData is Keys.Apps or (Keys.Shift | Keys.F10))
        {
            Point location = new(16, 16);
            _contextEntry = null;
            if (_pages.SelectedItem is PageCatalogEntry entry)
            {
                _contextEntry = entry;
                var rect = _pages.GetItemRectangle(_pages.SelectedIndex);
                location = new Point(rect.Left + 16, rect.Top + Math.Min(rect.Height, 48));
            }
            _pageMenu.Show(_pages, location);
            return true;
        }
        if (keyData is (Keys.Control | Keys.Z) or (Keys.Control | Keys.Y) or (Keys.Control | Keys.Shift | Keys.Z))
        {
            // Use the same enabled state and action as the buttons, including trash and busy input.
            UpdateState(false);
            (keyData == (Keys.Control | Keys.Z) ? _undo : _redo).PerformClick();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // Width samples keep the fine/medium/bold ratio but are drawn larger than the ink so fine is visible.
    private const float MaximumWidthSampleScale = 4f;

    private ToolbarTile SampleButton(string name, Color color, Func<float> diameter)
    {
        var button = new ToolbarTile(name, color, diameter);
        _toolTips.SetToolTip(button, name);
        return button;
    }

    private float WidthSampleDiameter(float width)
    {
        if (_canvas is null || _widths.Count == 0)
            return 0;
        float available = _widths.Min(b => Math.Min(b.ClientSize.Width, b.ClientSize.Height) - 10 * b.DeviceDpi / 96f);
        // Reference the standard page at this window size, not the open/previewed page.
        float bold = _canvas.InkWidthPixels(ToolPalette.Widths.Max(), BoardSize.Default);
        // All three circles use one scale; the bold sample fits without enlarging any tile.
        float scale = bold > 0 ? Math.Min(MaximumWidthSampleScale, Math.Max(0, available) / bold) : 0;
        return _canvas.InkWidthPixels(width, BoardSize.Default) * scale;
    }

    private void UpdateWidthSamples()
    {
        foreach (var button in _widths)
            button.Invalidate();
    }

    private void UpdateState(bool refresh = true)
    {
        if (IsDisposed)
            return;
        SynchronizePageView(refresh);
        int selectedCount = _pages.SelectedItems.Count;
        bool multiple = selectedCount > 1;
        bool selecting = IsSelectionMode;
        bool readOnly = selecting || _trash.Checked || multiple;
        var selected = selectedCount == 1 ? _pages.SelectedItem as PageCatalogEntry : null;
        var preview = _trash.Checked && !selecting ? selected?.Document : null;
        if (_trash.Checked)
            _canvas.ShowTrashPreview(preview);
        else
            _canvas.ShowLivePage(editingEnabled: !readOnly);
        UpdateEditingState(readOnly, selecting);
        UpdatePageHeading(preview);
        UpdateStatus();
        UpdateListActions(selectedCount, multiple, selecting);
    }

    private void SelectTool(DrawingTool tool, EraserMode mode)
    {
        if (_page.CanNavigate())
        {
            _desktopTool = tool;
            if (tool == DrawingTool.Eraser)
                _engine.SetEraserMode(mode);
        }
        UpdateState();
    }

    private void SyncTools()
    {
        _pen.Selected = _desktopTool == DrawingTool.Pen;
        _eraseStroke.Selected = _desktopTool == DrawingTool.Eraser && _engine.EraserMode == EraserMode.Stroke;
        _erasePart.Selected = _desktopTool == DrawingTool.Eraser && _engine.EraserMode == EraserMode.Partial;
        for (int i = 0; i < _colors.Count; i++)
            _colors[i].Selected = _engine.PenColor == ToolPalette.Colors[i];
        for (int i = 0; i < _widths.Count; i++)
            _widths[i].Selected = _engine.PenWidthMetres == ToolPalette.Widths[i];
        var tools = (_desktopTool, _engine.PenColor, _engine.PenWidthMetres, _engine.EraserMode);
        if (_shownTools != tools)
        {
            _shownTools = tools;
            _canvas.Invalidate();
        }
    }

    private string ErrorDetails => string.Join("\n\n", new[] { _page.Error, _page.LoadError, _actions.Details, _catalog.ErrorDetails }.Where(s => s is not null));

    internal Form CreateDetailsWindow()
    {
        var dialog = new Form { Text = "HnVRMemo — エラーの詳細", Size = new(760, 440), MinimumSize = new(400, 240), StartPosition = FormStartPosition.CenterParent, Font = Font };
        dialog.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Text = ErrorDetails.Replace("\n", "\r\n") });
        return dialog;
    }

    private void ShowDetails()
    {
        using var dialog = CreateDetailsWindow();
        dialog.ShowDialog(this);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiTheme.ApplyWindow(this);
        PerformLayout();
        PostSessionUpdate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _page.Changed -= OnSessionChanged;
            if (_settings is not null)
                _settings.Changed -= OnSettingsChanged;
            _settingsWindow?.Dispose();
            _pageMenu.Dispose();
            _trashMenu.Dispose();
            _disabledTipDelay.Dispose();
            _disabledToolbarTips.Dispose();
            _toolTips.Dispose();
            _catalog.Dispose();
        }
        base.Dispose(disposing);
    }
}
