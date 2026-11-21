using HandwriteMemo.Core;
using System.Numerics;

namespace HandwriteMemo.App;

[Flags]
internal enum SessionChange
{
    None = 0,
    Document = 1,
    Saved = 2,
    State = 4,
    PageSwitched = 8,
    CatalogInvalidated = 16,
    FolderChanged = 32,
    Tools = 64
}

internal enum PageSaveState
{
    New,
    Saving,
    Saved,
    Failed
}

// One active page with per-page session histories. Disk snapshots are synchronous, bounded to dirty work,
// and page replacement happens only after both authoritative JSON and PNG succeed.
internal sealed class MemoSession
{
    private readonly HandwriteEngine _engine;
    private MemoStore _store;
    private readonly DateTimeOffset _clockOrigin;
    private readonly Action<string> _log;
    private readonly Dictionary<Guid, HandwriteEngine.EditHistory> _histories = [];
    private readonly Dictionary<Guid, Stack<string>> _titleHistories = [];
    private string _title = "";
    private Guid _id = Guid.NewGuid();
    private DateTimeOffset _created = DateTimeOffset.UtcNow;
    private bool _dirty;
    private double _lastAttempt = double.NegativeInfinity;
    public string? Error { get; private set; }
    public const string SaveFailureMessage = "保存に失敗しました。筆跡はアプリ内に残っています。";
    public const string SaveRetryHint = "保存先の空き容量や使用中のファイルを確認し、「保存を再試行」を押してください。";
    public PageSaveState SaveState => Error is not null ? PageSaveState.Failed : _dirty ? PageSaveState.Saving : _saved ? PageSaveState.Saved : PageSaveState.New;
    public string Status => SaveState switch
    {
        PageSaveState.Failed => "保存失敗・筆跡は保持中。PC画面の「保存を再試行」を押してください。",
        PageSaveState.Saving => "保存中…",
        PageSaveState.Saved => "保存済み",
        _ => "新しいページ",
    };
    public string? LoadError { get; private set; }
    private PageCatalogEntry? _failedOpen;
    public const string LoadFailureMessage = "ページを開けませんでした。今の筆跡は残っています。";
    public string? VrFailureMessage => Error is not null ? "保存失敗・筆跡は保持中。操作を終え、トリガーを離して待つと自動再試行。" : LoadError is not null
        ? "読込失敗・今の筆跡は保持中。PC画面の「読み込みを再試行」を押してください。" : null;
    private bool _saved;
    private bool _pinned, _vrNeedsRelease;
    public bool VrNeedsTriggerRelease => _vrNeedsRelease || _engine.RequiresTriggerRelease;
    public bool VrEraserPickupHint { get; private set; }
    public Guid Id => _id;
    public DateTimeOffset CreatedUtc => _created;
    public bool IsPinned => _pinned;
    public string Title => _title;
    public string DisplayName => MemoDocument.DisplayTitle(_title, _created);
    public bool DesktopOwnsInput { get; private set; }
    public bool IsResizing { get; private set; }
    public BoardSize BoardSize => _engine.BoardSize;
    public bool IsBusy => DesktopOwnsInput || _engine.IsWriting || _engine.IsErasing || IsResizing;
    public long Revision { get; private set; }
    public long SavedRevision { get; private set; }
    public string? Notice { get; private set; }
    private string? _triggerUndoNotice;
    private double _triggerUndoNoticeUntil;
    internal string? TriggerUndoNotice(double seconds) => seconds < _triggerUndoNoticeUntil ? _triggerUndoNotice : null;
    public PageCatalog Catalog { get; private set; }
    internal string Folder => _store.Folder;
    public event Action<SessionChange>? Changed;
    private (bool Busy, PageSaveState Save, string? Error, string? LoadError, string? Notice) _publishedState;

    public void NotifyCatalogChanged() => Notify(SessionChange.CatalogInvalidated);

    private void Notify(SessionChange change = SessionChange.None)
    {
        var state = (IsBusy, SaveState, Error, LoadError, Notice);
        if (_publishedState != state)
        {
            _publishedState = state;
            change |= SessionChange.State;
        }
        if (change != SessionChange.None)
            Changed?.Invoke(change);
    }

    // The caller has already saved pending ink and persisted the selected destination.
    internal void UseFolder(MemoStore store, PageCatalog catalog, bool moved, double seconds)
    {
        _store = store;
        Catalog = catalog;
        if (!moved)
        {
            NewPage(seconds, SessionChange.FolderChanged);
            _histories.Clear();
            _titleHistories.Clear();
        }
        else
        {
            Revision++;
            SavedRevision++;
            _vrNeedsRelease = true;
            Notify(SessionChange.Document | SessionChange.FolderChanged);
        }
    }

    public MemoSession(HandwriteEngine engine, string folder, DateTimeOffset clockOrigin, Action<string> log)
    {
        this._engine = engine;
        this._clockOrigin = clockOrigin;
        this._log = log;
        _created = clockOrigin;
        _store = new MemoStore(folder);
        Catalog = new PageCatalog(_store.Folder);
        // Fail early on an unwritable destination before letting a user write.
        var probe = Path.Combine(folder, ".write-check-" + Guid.NewGuid().ToString("N"));
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
        {
        }
        foreach (var issue in _store.RepairPreviews(MemoRenderer.WritePng))
            log("Preview recovery: " + issue);
    }

    public void Observe(FrameResult frame, double seconds)
    {
        if (frame.StrokeEnded)
            Notice = null; // An old "wait for this stroke" notice must not hide the next release/pickup guide.
        if (frame.DocumentChanged)
        {
            _dirty = true;
            Revision++;
        }
        var change = frame.DocumentChanged ? SessionChange.Document : SessionChange.None;
        if (frame.PaletteSelection.HasValue)
            change |= SessionChange.Tools;
        Notify(change);
        if (_dirty && (frame.StrokeEnded || frame.Commands.Contains(BoardCommand.Undo) || frame.Commands.Contains(BoardCommand.Redo) || seconds - _lastAttempt >= 1))
            Save(seconds);
    }

    public bool Save(double seconds)
    {
        if (!_dirty)
            return true;
        _lastAttempt = seconds;
        try
        {
            var document = Snapshot();
            var files = _store.Save(document, stream => MemoRenderer.WritePng(document, stream));
            _dirty = false;
            _saved = true;
            SavedRevision++;
            if (Error is not null)
                _log("Saving recovered.");
            Error = null;
            _log("Saved page: " + Path.GetFileName(files.JsonPath));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            var message = _store.Folder + "\n" + e.Message;
            if (Error != message)
                _log("SAVE ERROR: " + message);
            Error = message;
            Notify();
            return false;
        }
        Notify(SessionChange.Saved);
        return true;
    }

    public bool NewPage(double seconds) => NewPage(seconds, SessionChange.None);

    private bool NewPage(double seconds, SessionChange change)
    {
        if (!Save(seconds))
            return false;
        RememberHistory();
        _engine.StartNewPage(seconds);
        _id = Guid.NewGuid();
        _created = _clockOrigin.AddSeconds(seconds);
        _saved = false;
        _dirty = false;
        _pinned = false;
        _title = "";
        Revision++;
        LoadError = null;
        _failedOpen = null;
        Notice = null;
        VrEraserPickupHint = false;
        _vrNeedsRelease = true;
        Notify(change | SessionChange.Document | SessionChange.PageSwitched | SessionChange.CatalogInvalidated);
        return true;
    }

    public bool Open(PageCatalogEntry entry, double seconds)
    {
        if (entry.IsTrash)
            return false;
        if (entry.Document.Id == _id)
        {
            Notify(SessionChange.PageSwitched);
            return true; // In-progress VR ink is already the desktop's page.
        }
        if (!CanNavigate() || !Save(seconds))
            return false;
        try
        {
            var document = _store.Read(entry.JsonPath);
            RememberHistory();
            _engine.LoadPage(document, seconds);
            if (_histories.Remove(document.Id, out var history))
                _engine.RestoreHistory(history);
            _id = document.Id;
            _created = document.CreatedUtc;
            _pinned = document.IsPinned;
            _title = document.Title;
            _saved = true;
            _dirty = false;
            Error = null;
            Revision++;
            Notice = null;
            VrEraserPickupHint = false;
            LoadError = null;
            _failedOpen = null;
            _vrNeedsRelease = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            _failedOpen = entry;
            LoadError = entry.JsonPath + "\n" + e.Message;
            Notify();
            return false;
        }
        Notify(SessionChange.Document | SessionChange.PageSwitched);
        return true;
    }

    public bool RetryOpen(double seconds) => _failedOpen is { } entry && Open(entry, seconds);

    public bool Duplicate(PageCatalogEntry entry, double seconds)
    {
        if (entry.IsTrash || !CanNavigate() || !Save(seconds))
            return false;
        var source = entry.Document.Id == _id ? Snapshot() : Catalog.Read(entry);
        var now = _clockOrigin.AddSeconds(seconds);
        // These marks are copied at once onto a new page. Old recorder times cannot precede
        // its creation in the existing format, so only the copy receives fresh stroke times.
        var strokes = source.Strokes.Select(s => new MemoStroke(now,
            s.Points.Select(p => new MemoPoint(p.XMetres, p.YMetres, 0)).ToArray(), s.Color, s.WidthMetres)).ToArray();
        var copy = new MemoDocument(MemoDocument.CurrentFormatVersion, Guid.NewGuid(), now, now,
            source.BoardSize, strokes, isPinned: false, title: source.Title);
        var files = _store.Save(copy, stream => MemoRenderer.WritePng(copy, stream));
        bool opened = Open(new PageCatalogEntry(files.JsonPath, copy, false), seconds);
        NotifyCatalogChanged();
        return opened;
    }

    private void RememberHistory()
    {
        if (_engine.CanUndo || _engine.CanRedo)
            _histories[_id] = _engine.CaptureHistory();
        else
            _histories.Remove(_id);
    }

    public bool CanNavigate()
    {
        bool busy = IsBusy;
        Notice = !busy ? null : IsResizing ? "大きさの変更を終えてからページを切り替えてください。" : "一筆が終わってからページを切り替えてください。";
        Notify();
        return !busy;
    }

    internal void RequireVrTriggerRelease() => _vrNeedsRelease = true;

    public bool BeginDesktop(double seconds)
    {
        if (IsBusy)
        {
            Notice = IsResizing ? "VR で大きさを変更中です。変更を終えてから書いてください。" : "VR で筆記中です。ペンを離してから書いてください。";
            Notify();
            return false;
        }
        DesktopOwnsInput = true;
        Notice = null;
        // Even a desktop click wholly between VR frames must not rearm a held VR trigger.
        _vrNeedsRelease = true;
        _engine.SetTracking(true, seconds);
        _engine.UpdatePagePoint(null, false, seconds);
        Notify();
        return true;
    }

    public FrameResult DesktopPoint(Vector2? uv, bool down, double seconds, DrawingTool tool)
    {
        if (!DesktopOwnsInput)
            return EmptyFrame();
        var frame = _engine.UpdatePagePoint(uv, down, seconds, tool);
        Observe(frame, seconds);
        if (!down)
        {
            DesktopOwnsInput = false;
            Notify();
            Save(seconds);
        }
        return frame;
    }

    public FrameResult VrPoint(TipSample sample, bool down, float distance, double seconds, DrawingTool tool, bool isListOpen = false)
    {
        if (DesktopOwnsInput || IsResizing)
        {
            _vrNeedsRelease |= down;
            return EmptyFrame();
        }
        if (_vrNeedsRelease)
        {
            if (down)
                return EmptyFrame();
            _vrNeedsRelease = false;
        }
        var frame = _engine.UpdateProjectedTip(sample, down, distance, seconds, tool, isListOpen);
        if (tool == DrawingTool.Eraser)
            VrEraserPickupHint = false;
        if (frame.PaletteSelection is { } selection)
            VrEraserPickupHint = selection >= 7 && tool == DrawingTool.Pen;
        Observe(frame, seconds);
        return frame;
    }

    public void InterruptVr(double seconds)
    {
        SetVrResizing(false, seconds);
        if (DesktopOwnsInput)
            return;
        Observe(_engine.SetTracking(false, seconds), seconds);
        _engine.SetTracking(true, seconds);
        Save(seconds);
    }

    public bool SetVrResizing(bool active, double seconds)
    {
        if (active == IsResizing)
            return true;
        if (active && IsBusy)
            return false;
        IsResizing = active;
        _vrNeedsRelease = true;
        Notice = null;
        Notify();
        if (!active)
            Save(seconds);
        return true;
    }

    public bool Resize(BoardSize size, double seconds, bool anchorRight = false)
    {
        if (!IsResizing || DesktopOwnsInput || !_engine.ResizeBoard(size, anchorRight))
            return false;
        _dirty = true;
        Revision++;
        Notify(SessionChange.Document);
        if (seconds - _lastAttempt >= 1)
            Save(seconds);
        return true;
    }

    public void Undo(double seconds)
    {
        if (!CanNavigate())
            return;
        if (_engine.Undo())
        {
            _dirty = true;
            Revision++;
            Notify(SessionChange.Document);
            Save(seconds);
        }
    }

    public bool UndoFromTrigger(double seconds)
    {
        if (IsBusy || !_engine.CanUndo)
            return false;
        Undo(seconds); // Reuse the same history and persistence as the PC/board Undo command.
        _triggerUndoNotice = "元に戻しました";
        _triggerUndoNoticeUntil = seconds + 1.2;
        _log("double-trigger undo");
        return true;
    }

    public void Redo(double seconds)
    {
        if (!CanNavigate())
            return;
        if (_engine.Redo())
        {
            _dirty = true;
            Revision++;
            Notify(SessionChange.Document);
            Save(seconds);
        }
    }

    public bool SetPinned(bool value, double seconds)
    {
        _pinned = value;
        _dirty = true;
        Revision++;
        Notify(SessionChange.Document);
        return Save(seconds);
    }

    public bool CanUndoRename(PageCatalogEntry entry) => !entry.IsTrash &&
        _titleHistories.TryGetValue(entry.Document.Id, out var history) && history.Count > 0;

    public bool Rename(PageCatalogEntry entry, string value, double seconds) => ChangeTitle(entry, value, seconds, undo: false);

    public bool UndoRename(PageCatalogEntry entry, double seconds) => CanUndoRename(entry) &&
        ChangeTitle(entry, _titleHistories[entry.Document.Id].Peek(), seconds, undo: true);

    private bool ChangeTitle(PageCatalogEntry entry, string value, double seconds, bool undo)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (entry.IsTrash || !CanNavigate() || !Save(seconds))
            return false;
        bool current = entry.Document.Id == _id;
        var document = current ? Snapshot() : _store.Read(entry.JsonPath);
        if (document.Title == value)
            return true;
        void RememberChange()
        {
            if (undo)
                _titleHistories[document.Id].Pop();
            else
            {
                if (!_titleHistories.TryGetValue(document.Id, out var history))
                    _titleHistories[document.Id] = history = new();
                history.Push(document.Title);
            }
            Revision++;
            if (!current)
                SavedRevision++;
            Notify(SessionChange.Document | SessionChange.CatalogInvalidated);
        }
        // Keep metadata undo separate from ink undo. Current-page failures keep the new title
        // dirty, just like ink/pinning, so the existing retry action can save it later.
        if (current)
        {
            _title = value;
            _dirty = true;
            RememberChange();
            return Save(seconds);
        }
        else
        {
            var changed = new MemoDocument(document.FormatVersion, document.Id, document.CreatedUtc,
                new[] { document.UpdatedUtc, DateTimeOffset.UtcNow }.Max(), document.BoardSize,
                document.Strokes, document.IsPinned, value);
            try
            {
                _store.Save(changed, stream => MemoRenderer.WritePng(changed, stream));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
            {
                // JSON is authoritative and may already be committed when the PNG fails.
                // Such a change must still be visible and undoable for an inactive page.
                try
                {
                    if (_store.Read(entry.JsonPath).Title == value)
                        RememberChange();
                }
                catch (Exception readError) when (readError is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
                {
                }
                throw;
            }
            RememberChange();
        }
        return true;
    }

    private static FrameResult EmptyFrame() => new(ContactState.None, null, [], [], false);

    public bool EmergencySave(out string? recoveryFolder)
    {
        recoveryFolder = null;
        if (!_dirty)
            return true;
        try
        {
            var folder = AppDataPaths.RecoveryFolder(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            var recovery = new MemoStore(folder);
            var document = Snapshot();
            recovery.Save(document, stream => MemoRenderer.WritePng(document, stream));
            _dirty = false;
            _log("Emergency copy saved: " + folder);
            recoveryFolder = folder;
            return true;
        }
        catch (Exception e)
        {
            _log("EMERGENCY SAVE ERROR: " + e.Message);
            return false;
        }
    }

    public MemoDocument Snapshot()
    {
        var doc = MemoDocument.Capture(_id, _created, _clockOrigin, _engine.BoardSize, _engine.Strokes);
        return new(doc.FormatVersion, doc.Id, doc.CreatedUtc, doc.UpdatedUtc, doc.BoardSize, doc.Strokes, _pinned, _title);
    }
}
