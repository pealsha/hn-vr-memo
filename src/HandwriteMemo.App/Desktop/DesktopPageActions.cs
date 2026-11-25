using System.Runtime.InteropServices;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

internal sealed class DesktopPageActions(
    Form _owner, MemoSession _page, Func<double> _seconds, DesktopCatalogView _catalog,
    RadioButton _trash, PageTitleEditor _nameEditor, ToolTip _toolTips,
    Action<bool> _reloadCatalog, Action<bool> _updateState, Action _showActiveList, Action _exitSelectionMode)
{
    private readonly HashSet<Guid> _lastTrashed = [];
    private PageCatalogEntry? _restoredEntry;
    private PageCatalogEntry? _renameEntry;

    internal string? Error { get; private set; }
    internal string? Details { get; private set; }
    internal string? Notice { get; private set; }
    internal bool CanUndoTrash => _lastTrashed.Count > 0 && !_page.IsBusy;
    internal int LastTrashedCount => _lastTrashed.Count;
    internal bool HasRestoredEntry => _restoredEntry is not null;

    internal void ResetForFolder()
    {
        _lastTrashed.Clear();
        _restoredEntry = null;
        _renameEntry = null;
        _nameEditor.Hide();
    }

    internal void ClearNotice() => Notice = null;

    internal void Duplicate(PageCatalogEntry entry)
    {
        if (_page.Duplicate(entry, _seconds()))
            _reloadCatalog(true);
    }

    internal void UndoRename(PageCatalogEntry entry)
    {
        _page.UndoRename(entry, _seconds());
        _reloadCatalog(false);
    }

    internal void OpenEntry(PageCatalogEntry entry)
    {
        // Trash entries are previewed as part of the window's selection state.
        if (entry.IsTrash)
            return;
        if (!_page.Open(entry, _seconds()))
            _catalog.SelectCurrentPage();
    }

    internal bool AllPinned(IReadOnlyList<PageCatalogEntry> entries) => entries.Count == 0 ? _page.IsPinned :
        entries.All(e => e.Document.Id == _page.Id ? _page.IsPinned : e.Document.IsPinned);

    internal void PinEntries(IReadOnlyList<PageCatalogEntry> entries)
    {
        if (_trash.Checked || _catalog.IsSelectionMode && entries.Count == 0 || !_page.CanNavigate())
            return;
        bool value = !AllPinned(entries);
        var failures = new List<string>();
        if (entries.Count == 0)
            _page.SetPinned(value, _seconds()); // Unsaved blank page.
        foreach (var entry in entries)
        {
            try
            {
                if (entry.Document.Id == _page.Id)
                    _page.SetPinned(value, _seconds());
                else
                    _page.Catalog.SetPinned(entry, value, MemoRenderer.WritePng);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ExternalException)
            {
                failures.Add(entry.Document.DisplayName + "\n" + entry.JsonPath + "\n" + error.Message);
            }
        }
        _reloadCatalog(false);
        if (failures.Count > 0)
        {
            Error = $"{failures.Count}件のピン留めを変更できませんでした。「詳細」で対象を確認してください。";
            Details = string.Join("\n\n", failures);
        }
    }

    internal void TrashEntries(IReadOnlyList<PageCatalogEntry> entries)
    {
        if (entries.Count == 0 || entries.Any(e => e.IsTrash) || !_page.CanNavigate())
            return;
        Guid currentId = _page.Id;
        if (entries.Any(e => e.Document.Id == currentId) && !_page.Save(_seconds()))
            return;
        var catalog = _page.Catalog;
        var order = catalog.List();
        var moved = new List<Guid>();
        var failures = new List<string>();
        // Move the active page last. Failed targets can then remain as its neighbour.
        foreach (var entry in entries.OrderBy(e => e.Document.Id == currentId))
        {
            try
            {
                if (entry.Document.Id == currentId)
                {
                    var remaining = catalog.List().Where(e => e.Document.Id != currentId).ToArray();
                    int currentIndex = order.ToList().FindIndex(e => e.Document.Id == currentId);
                    var neighbour = order.Skip(currentIndex + 1).Concat(order.Take(currentIndex).Reverse())
                        .FirstOrDefault(e => remaining.Any(r => r.Document.Id == e.Document.Id));
                    // Switch through the shared session before moving its files. A failed read/save
                    // leaves the active page in the normal list, never writable in the trash.
                    if (neighbour is null ? !_page.NewPage(_seconds()) : !_page.Open(neighbour, _seconds()))
                    {
                        failures.Add(entry.Document.DisplayName + "\n" + entry.JsonPath + "\n次のページを開けなかったため、このページは移動していません。");
                        continue;
                    }
                }
                catalog.MoveToTrash(entry);
                moved.Add(entry.Document.Id);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                failures.Add(entry.Document.DisplayName + "\n" + entry.JsonPath + "\n" + error.Message);
            }
        }
        if (moved.Count > 0)
        {
            _lastTrashed.Clear();
            _lastTrashed.UnionWith(moved);
            _restoredEntry = null;
        }
        _reloadCatalog(_page.Id != currentId);
        if (failures.Count > 0)
        {
            Error = $"{failures.Count}件をゴミ箱へ移せませんでした。一覧に残った対象は「詳細」で確認できます。";
            Details = string.Join("\n\n", failures);
        }
    }

    internal void UndoTrash()
    {
        if (CanUndoTrash)
            RunOperation(() => RestoreEntries(
                _page.Catalog.List(true).Where(e => _lastTrashed.Contains(e.Document.Id)).ToArray()));
    }

    internal void OpenRestored()
    {
        if (_restoredEntry is not null && _page.Open(_restoredEntry, _seconds()))
        {
            _showActiveList();
            _exitSelectionMode();
            _restoredEntry = null;
            _reloadCatalog(true);
        }
    }

    internal void NewPage()
    {
        if (_page.CanNavigate() && _page.NewPage(_seconds()))
            _showActiveList();
    }

    internal void RestoreEntries(IReadOnlyList<PageCatalogEntry> entries)
    {
        if (entries.Count == 0 || entries.Any(e => !e.IsTrash))
            return;
        var catalog = _page.Catalog;
        var failures = new List<string>();
        foreach (var entry in entries)
        {
            try
            {
                catalog.Restore(entry);
                _restoredEntry = catalog.List().Single(e => e.Document.Id == entry.Document.Id);
                _lastTrashed.Remove(entry.Document.Id);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                failures.Add(entry.Document.DisplayName + "\n" + entry.JsonPath + "\n" + error.Message);
            }
        }
        _reloadCatalog(false);
        if (failures.Count > 0)
        {
            Error = $"{failures.Count}件を復元できませんでした。ゴミ箱に残った対象は「詳細」で確認できます。";
            Details = string.Join("\n\n", failures);
        }
    }

    internal void ConfirmDelete(IReadOnlyList<PageCatalogEntry> entries, bool emptyTrash = false)
    {
        if (entries.Count == 0 || entries.Any(e => !e.IsTrash))
            return;
        // Snapshot the exact targets before showing a modal dialog. Never expand after confirmation.
        var targets = entries.ToArray();
        using var dialog = new TrashDeleteDialog(targets.Length, emptyTrash);
        if (dialog.ShowDialog(_owner) != DialogResult.OK)
            return;
        var results = targets.Select(_page.Catalog.DeletePermanently).ToArray();
        int deleted = results.Count(r => r.Succeeded);
        foreach (var result in results.Where(r => r.Succeeded))
            _lastTrashed.Remove(result.Entry.Document.Id);
        // RefreshCatalog uses RunOperation; set feedback afterwards so a successful reload cannot hide failures.
        _reloadCatalog(false);
        var failed = results.Where(r => !r.Succeeded).ToArray();
        Notice = deleted > 0 ? $"{deleted}件を完全に削除しました。" : null;
        if (failed.Length > 0)
        {
            Error = $"{failed.Length}件を完全に削除できませんでした。残ったページ・ファイルは「詳細」で確認できます。確認してから再試行してください。";
            Details = string.Join("\n\n", failed.Select(r => r.Entry.Document.DisplayName + "\n残ったファイル:\n" +
                string.Join("\n", r.RemainingFiles) + "\n" + string.Join("\n", r.Errors.Select(e => e.Error.Message))));
        }
        if (_restoredEntry is not null && targets.Any(e => e.Document.Id == _restoredEntry.Document.Id))
            _restoredEntry = null;
    }

    internal void Navigate(int direction)
    {
        if (_trash.Checked || !_page.CanNavigate())
            return;
        var entries = _catalog.Entries;
        int current = _catalog.IndexOf(_page.Id);
        int index = current < 0 ? 0 : current + direction;
        if (index >= 0 && index < entries.Count)
            RunOperation(() => OpenEntry(entries[index]));
    }

    internal void BeginRename(PageCatalogEntry entry)
    {
        if (entry.IsTrash || !_page.CanNavigate())
            return;
        EndRename(false);
        int index = _catalog.IndexOf(entry.Document.Id);
        if (index < 0)
            return;
        _renameEntry = entry;
        _catalog.SelectIndex(index);
        var rect = _catalog.List.GetItemRectangle(index);
        int Px(int logical) => _owner.LogicalToDeviceUnits(logical);
        _nameEditor.Bounds = new Rectangle(rect.Left + Px(12), rect.Top + Px(10) + _catalog.PreviewHeight, Math.Max(Px(20), rect.Width - Px(24)), Px(26));
        _nameEditor.Text = entry.Document.Id == _page.Id ? _page.Title : entry.Document.Title;
        _toolTips.SetToolTip(_nameEditor, "Enterで確定 / Escまたは欄の外をクリックして取り消し。空欄で日時の名前に戻ります。");
        _nameEditor.Show();
        _nameEditor.BringToFront();
        _nameEditor.Focus();
        _nameEditor.SelectAll();
    }

    internal void EndRename(bool commit, bool focusList = false)
    {
        if (_renameEntry is not { } entry)
            return;
        if (commit && !_page.CanNavigate())
        {
            _updateState(false);
            return;
        }
        string value = _nameEditor.Text;
        if (commit)
        {
            bool accepted = false;
            RunOperation(() => accepted = _page.Rename(entry, value, _seconds()) || (entry.Document.Id == _page.Id && _page.Title == value));
            if (!accepted)
                return; // Keep typed text when earlier unsaved work prevents the rename.
        }
        _renameEntry = null;
        _nameEditor.Hide();
        if (commit)
            _reloadCatalog(false);
        if (!_trash.Checked)
            _catalog.SelectCurrentPage();
        if (focusList)
            _catalog.List.Focus();
        _catalog.List.Invalidate();
        _updateState(true);
    }

    internal void RunOperation(Action action, string failureMessage = "操作に失敗しました。今の筆跡は残っています。「詳細」を確認してから、もう一度操作してください。", bool clearFeedback = true)
    {
        try
        {
            if (clearFeedback)
            {
                Error = null;
                Details = null;
                Notice = null;
            }
            action();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or ExternalException)
        {
            Error = failureMessage;
            Details = e.Message;
        }
        _updateState(false);
    }
}
