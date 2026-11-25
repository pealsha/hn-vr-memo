using HandwriteMemo.Core;

namespace HandwriteMemo.App;

internal sealed partial class DesktopWindow
{
    private void OnSettingsChanged(SettingsChange change)
    {
        if ((change & SettingsChange.Retention) != 0)
            _pages.Invalidate();
    }

    private void OnSessionChanged(SessionChange change)
    {
        if (_catalog.IsRefreshing && change == SessionChange.CatalogInvalidated)
            return; // Reloading this catalog already updated the desktop list.
        // A blank page adds no row. Saving the previous page sends a separate notification.
        if ((change & SessionChange.PageSwitched) != 0 && _page.SaveState == PageSaveState.New)
            change &= ~SessionChange.CatalogInvalidated;
        _pendingSessionChange |= change;
        PostSessionUpdate();
    }

    private void PostSessionUpdate()
    {
        if (_sessionUpdatePosted || !IsHandleCreated || _pendingSessionChange == SessionChange.None)
            return;
        _sessionUpdatePosted = true;
        BeginInvoke(() =>
        {
            _sessionUpdatePosted = false;
            if (IsDisposed)
                return;
            var change = _pendingSessionChange;
            _pendingSessionChange = SessionChange.None;
            if ((change & SessionChange.FolderChanged) != 0)
                FolderChanged();
            else if ((change & SessionChange.CatalogInvalidated) != 0)
                _catalogInvalidated = true;
            UpdateState();
        });
    }

    private void SynchronizePageView(bool refresh)
    {
        var previous = _displayedPage;
        _displayedPage = (_page.Id, _page.Title, _page.IsPinned);
        if (previous.Id != _page.Id)
        {
            _actions.EndRename(false);
            _pageMenu.Close();
            // A shared page opened from VR returns the PC from trash preview to the editor.
            if (_trash.Checked)
                _listTab.Checked = true;
            else if (!_catalog.IsRefreshing)
                SynchronizeCatalogSelection(previous);
        }
        if (refresh && !_catalog.IsRefreshing && Visible && !_pageMenu.Visible && !_nameEditor.Visible && !_page.IsBusy &&
            (_catalogInvalidated || _catalog.SavedRevision != _page.SavedRevision))
            RefreshCatalog();
        if (_revision != _page.Revision)
        {
            _revision = _page.Revision;
            _canvas.Invalidate();
        }
    }

    private void SynchronizeCatalogSelection((Guid Id, string Title, bool IsPinned) previous)
    {
        int index = _catalog.IndexOf(_page.Id);
        if (index < 0 && _page.SaveState != PageSaveState.New)
        {
            ReloadCatalog(selectCurrent: true);
            return;
        }
        if (!IsSelectionMode && (index < 0
            ? _pages.SelectedItems.Count > 0
            : _pages.SelectedItems.Count != 1 || !_pages.GetSelected(index)))
            _catalog.SelectIndex(index);
        _catalog.InvalidateChangedLabel(_catalog.IndexOf(previous.Id), previous.Title, previous.IsPinned);
        _catalog.InvalidateChangedLabel(index, _page.Title, _page.IsPinned);
    }

    private void UpdateEditingState(bool readOnly, bool selecting)
    {
        // Keep page creation available while the trash preview is read-only.
        foreach (Control group in _tools.Controls)
            if (group != _pageGroup)
                group.Enabled = !readOnly;
        _pageGroup.Enabled = !selecting;
        SyncTools();
        _undo.Enabled = !readOnly && !_page.IsBusy && _engine.CanUndo;
        _redo.Enabled = !readOnly && !_page.IsBusy && _engine.CanRedo;
    }

    private void UpdatePageHeading(MemoDocument? shown)
    {
        // The selection count is shown only in the list header; the page area keeps showing what the canvas shows.
        string displayName = _trash.Checked ? shown?.DisplayName ?? "ゴミ箱のプレビュー" : _page.DisplayName;
        bool hasTitle = _trash.Checked ? shown?.Title.Length > 0 : _page.Title.Length > 0;
        _title.Text = displayName + ((_trash.Checked ? shown?.IsPinned == true : _page.IsPinned) ? "  ★" : "");
        string created = hasTitle ? $"作成 {(shown?.CreatedUtc ?? _page.CreatedUtc).ToLocalTime():yyyy/MM/dd  HH:mm}" : "";
        const string Preview = "ゴミ箱のプレビュー（閲覧のみ）";
        _titleDate.Text = shown is null ? created : hasTitle ? created + "  " + Preview : Preview;
    }

    private void UpdateStatus()
    {
        string saved = _trash.Checked || _page.SaveState == PageSaveState.New ? "" : _page.Status;
        var messages = new List<string>();
        if (_page.Error is not null)
            messages.Add(MemoSession.SaveFailureMessage + "\n" + MemoSession.SaveRetryHint);
        if (_page.LoadError is not null)
            messages.Add(MemoSession.LoadFailureMessage + " ファイルを確認し、「読み込みを再試行」を押してください。");
        if (messages.Count == 0 && saved.Length > 0)
            messages.Add(saved);
        if (_actions.Error is not null)
            messages.Add(_actions.Error);
        if (_actions.Notice is not null)
            messages.Add(_actions.Notice);
        if (_catalog.Warning is not null)
            messages.Add(_catalog.Warning);
        if (_page.Error is null && _page.LoadError is null && _actions.Error is null && _catalog.Warning is null)
        {
            if (_page.Notice is { } notice)
                messages.Add(notice);
        }
        _status.Text = string.Join("\n", messages);
        _status.ForeColor = _page.Error is not null || _page.LoadError is not null || _actions.Error is not null || _catalog.Warning is not null ? UiTheme.ErrorInk : ForeColor;
        _retrySave.Visible = _page.Error is not null;
        _retrySave.Enabled = !_page.IsBusy;
        _retryOpen.Visible = _page.LoadError is not null;
        _retryOpen.Enabled = !_page.IsBusy;
        _details.Visible = ErrorDetails.Length > 0;
        _status.Visible = _status.Text.Length > 0;
    }

    private void UpdateListActions(int selectedCount, bool multiple, bool selecting)
    {
        _select.Text = selecting ? "完了" : "選択";
        _selectionCount.Text = selecting || multiple ? $"{selectedCount}件選択" : "";
        _selectAll.Visible = selecting;
        _selectAll.Enabled = _pages.Items.Count > 0;
        _selectAll.Text = selectedCount > 0 && selectedCount == _pages.Items.Count ? "すべて解除" : "すべて選択";
        _pin.Enabled = !_trash.Checked && (!selecting || selectedCount > 0);
        _remove.Enabled = !_trash.Checked && selectedCount > 0;
        _restore.Visible = _trash.Checked;
        _pin.Visible = _remove.Visible = !_trash.Checked;
        _delete.Visible = selecting && _trash.Checked;
        _delete.Enabled = selectedCount > 0;
        _restore.Enabled = selectedCount > 0;
        // The selection count is shown only in the list header, which also keeps these buttons from overflowing.
        bool selectedPinned = (!selecting || selectedCount > 0) && _actions.AllPinned(SelectedEntries);
        _pin.Text = selectedPinned ? "★ ピン解除" : "☆ ピン留め";
        int index = _catalog.IndexOf(_page.Id);
        int count = _pages.Items.Count;
        _previous.Enabled = !_trash.Checked && index > 0;
        _next.Enabled = !_trash.Checked && count > 0 && index < count - 1;
    }

}
