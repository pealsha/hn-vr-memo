using HandwriteMemo.Core;

namespace HandwriteMemo.App;

internal sealed partial class DesktopWindow
{
    private void BindReloadButton(Button reload) => reload.Click += (_, _) => RefreshCatalog();

    private void BindTrashMenu()
    {
        _trashMenu.Opening += (_, e) =>
        {
            _trashMenu.Items.Clear();
            AddEmptyTrashCommand(_trashMenu);
            _trashMenu.Size = _trashMenu.GetPreferredSize(Size.Empty);
            e.Cancel = false; // WinForms initially cancels a dynamically populated, empty strip.
        };
    }

    private void AddEmptyTrashCommand(ContextMenuStrip menu)
    {
        var empty = menu.Items.Add("ゴミ箱を空にする");
        empty.Enabled = false;
        _actions.RunOperation(() => empty.Enabled = _page.Catalog.List(true).Count > 0, clearFeedback: false);
        empty.Click += (_, _) => _actions.RunOperation(() => _actions.ConfirmDelete(_page.Catalog.List(true), emptyTrash: true));
    }

    private void BindColorButton(Button button, int index)
    {
        button.Click += (_, _) =>
        {
            if (_page.CanNavigate())
            {
                _engine.SetPenStyle(ToolPalette.Colors[index], _engine.PenWidthMetres);
                _desktopTool = DrawingTool.Pen;
            }
            UpdateState();
        };
    }

    private void BindWidthButton(Button button, int index)
    {
        button.Click += (_, _) =>
        {
            if (_page.CanNavigate())
                _engine.SetPenStyle(_engine.PenColor, ToolPalette.Widths[index]);
            UpdateState();
        };
    }

    private void BindDrawingTools()
    {
        _pen.Click += (_, _) => SelectTool(DrawingTool.Pen, _engine.EraserMode);
        _eraseStroke.Click += (_, _) => SelectTool(DrawingTool.Eraser, EraserMode.Stroke);
        _erasePart.Click += (_, _) => SelectTool(DrawingTool.Eraser, EraserMode.Partial);
        _undo.Click += (_, _) =>
        {
            _page.Undo(_seconds());
            UpdateState();
        };
        _redo.Click += (_, _) =>
        {
            _page.Redo(_seconds());
            UpdateState();
        };
    }

    private void BindCanvas() => _canvas.SizeChanged += (_, _) => UpdateWidthSamples();

    private void BindPageEvents(Button newPage)
    {
        BindSelectionAndRecovery();
        BindPageListEvents();
        BindPageNavigation(newPage);
        BindWindowLifetime();
    }

    private void BindSelectionAndRecovery()
    {
        _select.Click += (_, _) => SetSelectionMode(!IsSelectionMode);
        _selectAll.Click += (_, _) => SelectAllPages(_pages.SelectedItems.Count != _pages.Items.Count);
        _delete.Click += (_, _) => _actions.RunOperation(() => _actions.ConfirmDelete(SelectedEntries));
        _retrySave.Click += (_, _) =>
        {
            _page.Save(_seconds());
            UpdateState();
        };
        _retryOpen.Click += (_, _) =>
        {
            if (_page.RetryOpen(_seconds()))
                RefreshCatalog();
            else
                UpdateState();
        };
        _details.Click += (_, _) => ShowDetails();
    }

    private void BindPageListEvents()
    {
        _nameEditor.LostFocus += (_, _) => _actions.EndRename(false);
        _pages.MouseWheel += (_, _) => _actions.EndRename(false);
        _pages.SizeChanged += (_, _) => _actions.EndRename(false);
        _pages.MouseDown += (_, e) => SelectContextTarget(e);
        _pageMenu.Opening += (_, e) => BuildPageMenu(e);
        _pageMenu.Closed += (_, _) =>
        {
            _contextEntry = null;
            _mouseContextRequest = false;
            UpdateState();
        };
        _pages.SelectedIndexChanged += (_, _) =>
        {
            if (_catalog.IsRefreshing)
                return;
            if (IsSelectionMode)
            {
                UpdateState(false);
                return;
            }
            if (_pages.SelectedItems.Count != 1 || _pages.SelectedItem is not PageCatalogEntry entry)
            {
                UpdateState(false);
                return;
            }
            _actions.RunOperation(() =>
            {
                if (!entry.IsTrash && !_page.Open(entry, _seconds()))
                    _catalog.SelectCurrentPage();
            });
        };
    }

    private void BindPageNavigation(Button newPage)
    {
        _trash.CheckedChanged += (_, _) =>
        {
            _canvas.EndInput();
            _actions.EndRename(false);
            _pageMenu.Close();
            SetSelectionMode(false);
            _actions.ClearNotice();
            ReloadCatalog(selectCurrent: !_trash.Checked);
        };
        newPage.Click += (_, _) => _actions.RunOperation(_actions.NewPage);
        _previous.Click += (_, _) => _actions.Navigate(-1);
        _next.Click += (_, _) => _actions.Navigate(1);
        _pin.Click += (_, _) => _actions.RunOperation(() => _actions.PinEntries(SelectedEntries));
        _remove.Click += (_, _) => _actions.RunOperation(() => _actions.TrashEntries(SelectedEntries));
        _restore.Click += (_, _) => _actions.RunOperation(() => _actions.RestoreEntries(SelectedEntries));
    }

    private void BindWindowLifetime()
    {
        FormClosing += (_, e) => HandleWindowClosing(e);
        Deactivate += (_, _) => _canvas.EndInput();
    }

    private void SelectContextTarget(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
            return;
        _mouseContextRequest = true;
        int index = _pages.IndexFromPoint(e.Location);
        _contextEntry = index < 0 ? null : _pages.Items[index] as PageCatalogEntry;
        _pages.Focus();
        if (_contextEntry is null)
            return;
        // Highlight the menu target without opening it or changing the shared VR page.
        if (!_pages.GetSelected(index))
            _catalog.SelectIndex(index);
        _pages.Focus();
        UpdateState(false);
    }

    private void BuildPageMenu(System.ComponentModel.CancelEventArgs e)
    {
        // Windows can also open this menu via WM_CONTEXTMENU without ProcessCmdKey.
        var entry = _contextEntry ?? (_mouseContextRequest ? null : _pages.SelectedItem as PageCatalogEntry);
        _mouseContextRequest = false;
        _contextEntry = entry;
        _pageMenu.Items.Clear();
        void Add(string text, Action action, bool enabled = true)
        {
            var item = _pageMenu.Items.Add(text);
            item.Enabled = enabled;
            item.Click += (_, _) => _actions.RunOperation(action);
        }
        var targets = SelectedEntries;
        if (entry is not null && !IsSelectionMode && targets.Count == 1)
            Add(entry.IsTrash ? "開く（閲覧）" : "開く", () => _actions.OpenEntry(entry));
        if (entry is { IsTrash: true })
        {
            Add("復元する", () => _actions.RestoreEntries(targets));
            Add("完全に削除", () => _actions.ConfirmDelete(targets));
            ((ToolStripMenuItem)_pageMenu.Items[^1]).ShortcutKeyDisplayString = "Delete";
        }
        else if (entry is not null)
        {
            bool pinned = _actions.AllPinned(targets);
            // Menu labels lead with the action word; users scan only the start of each item.
            Add(pinned ? "ピン解除" : "ピン留め", () => _actions.PinEntries(targets), !_page.IsBusy);
            if (!IsSelectionMode && targets.Count == 1)
            {
                Add("複製", () => _actions.Duplicate(entry), !_page.IsBusy);
                var rename = new ToolStripMenuItem("名前を変更") { ShortcutKeyDisplayString = "F2", Enabled = !_page.IsBusy };
                rename.Click += (_, _) => _actions.BeginRename(entry);
                _pageMenu.Items.Add(rename);
                // Like Explorer, offer an undo only when there is something to undo.
                if (_page.CanUndoRename(entry))
                    Add("名前の変更を元に戻す", () => _actions.UndoRename(entry), !_page.IsBusy);
            }
            _pageMenu.Items.Add(new ToolStripSeparator());
            Add("ゴミ箱へ移動", () => _actions.TrashEntries(targets), !_page.IsBusy);
            ((ToolStripMenuItem)_pageMenu.Items[^1]).ShortcutKeyDisplayString = "Delete";
        }
        if (entry is null)
        {
            if (!_trash.Checked)
                Add("新しいページ", _actions.NewPage, !IsSelectionMode && !_page.IsBusy);
            Add("すべて選択", () => SelectAllPages(), _pages.Items.Count > 0);
            ((ToolStripMenuItem)_pageMenu.Items[^1]).ShortcutKeyDisplayString = "Ctrl+A";
            Add("更新", RefreshCatalog);
            if (_trash.Checked)
                AddEmptyTrashCommand(_pageMenu);
        }
        // The undo group is always last, so it stays in the same place on a row and on the background.
        if (_pageMenu.Items.Count > 0)
            _pageMenu.Items.Add(new ToolStripSeparator());
        string undoCount = _actions.LastTrashedCount > 1 ? $"（{_actions.LastTrashedCount}件）" : "";
        Add("移動を元に戻す" + undoCount, _actions.UndoTrash, CanUndoTrash);
        ((ToolStripMenuItem)_pageMenu.Items[^1]).ShortcutKeyDisplayString = "Ctrl+Z";
        if (_actions.HasRestoredEntry)
            Add("復元したページを開く", _actions.OpenRestored, !_page.IsBusy);
        // Items are rebuilt while opening; include the final row in the popup's size.
        _pageMenu.Size = _pageMenu.GetPreferredSize(Size.Empty);
        e.Cancel = false;
    }

    private void HandleWindowClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            _canvas.EndInput();
            if (_page.Save(_seconds()))
                Hide();
        }
    }
}
