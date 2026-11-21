using HandwriteMemo.Core;

namespace HandwriteMemo.App;

// Only VR presentation/navigation state. The catalog, current ink, tools and saving remain shared with PC.
internal sealed class VrPageBrowser : IDisposable
{
    private readonly MemoSession _page;
    private bool _invalidated = true;

    public VrPageBrowser(MemoSession page)
    {
        _page = page;
        _page.Changed += OnSessionChanged;
    }

    private void OnSessionChanged(SessionChange change)
    {
        if ((change & (SessionChange.PageSwitched | SessionChange.FolderChanged)) != 0)
        {
            if (IsOpen)
                _page.RequireVrTriggerRelease();
            ReturnToPage();
        }
        if ((change & (SessionChange.CatalogInvalidated | SessionChange.FolderChanged | SessionChange.Saved)) != 0)
            Invalidate();
    }

    public void Dispose() => _page.Changed -= OnSessionChanged;
    public bool IsOpen { get; private set; }
    public IReadOnlyList<PageCatalogEntry> Entries { get; private set; } = [];
    public int Screen { get; private set; }
    public int ScreenCount => Math.Max(1, (Entries.Count + BoardControls.PagesPerScreen - 1) / BoardControls.PagesPerScreen);
    public long Revision { get; private set; }
    public string? Notice { get; private set; }
    public int CurrentIndex
    {
        get
        {
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].Document.Id == _page.Id)
                    return i;
            return -1;
        }
    }

    public void Invalidate() => _invalidated = true;
    public void ReturnToPage()
    {
        IsOpen = false;
        Notice = null;
    }

    public void RefreshIfNeeded()
    {
        if (!_page.IsBusy && _invalidated)
            Refresh();
    }

    public void Handle(FrameResult frame, double seconds)
    {
        foreach (var command in frame.Commands)
        {
            switch (command)
            {
                case BoardCommand.NewPage:
                    if (_page.CanNavigate())
                        _page.NewPage(seconds);
                    break;
                case BoardCommand.PageList:
                    if (!_page.CanNavigate() || !_page.Save(seconds) || !Refresh())
                        break;
                    IsOpen = true;
                    Screen = CurrentIndex < 0 ? 0 : CurrentIndex / BoardControls.PagesPerScreen;
                    break;
                case BoardCommand.BackToPage:
                    ReturnToPage();
                    break;
                case BoardCommand.RefreshList:
                    if (_page.CanNavigate())
                        Refresh();
                    break;
                case BoardCommand.OpenPage when IsOpen && frame.CursorUv is { } uv:
                    int row = BoardControls.CardAt(uv, _page.BoardSize);
                    int index = Screen * BoardControls.PagesPerScreen + row;
                    if (row >= 0 && index < Entries.Count)
                        _page.Open(Entries[index], seconds);
                    break;
                case BoardCommand.PreviousPage:
                case BoardCommand.NextPage:
                    Navigate(command == BoardCommand.PreviousPage ? -1 : 1, seconds);
                    break;
                case BoardCommand.TogglePin:
                    if (_page.CanNavigate() && _page.SetPinned(!_page.IsPinned, seconds))
                        Refresh();
                    break;
            }
        }
    }

    public bool CanNavigate(int direction)
    {
        if (IsOpen)
            return direction < 0 ? Screen > 0 : Screen < ScreenCount - 1;
        int index = CurrentIndex;
        return direction < 0 ? index > 0 : Entries.Count > 0 && index < Entries.Count - 1;
    }

    private void Navigate(int direction, double seconds)
    {
        if (!_page.CanNavigate() || !_page.Save(seconds) || !Refresh())
            return;
        if (IsOpen)
            Screen = Math.Clamp(Screen + direction, 0, ScreenCount - 1);
        else
        {
            int current = CurrentIndex;
            int index = current < 0 && direction > 0 ? 0 : current + direction;
            if (index >= 0 && index < Entries.Count)
                _page.Open(Entries[index], seconds);
        }
    }

    internal bool Refresh()
    {
        try
        {
            var catalog = _page.Catalog;
            var entries = catalog.List();
            Entries = entries;
            Screen = Math.Clamp(Screen, 0, ScreenCount - 1);
            _invalidated = false;
            Revision++;
            Notice = catalog.Errors.Count == 0 ? null : $"読めないページ {catalog.Errors.Count} 件。PC画面の「詳細」で確認し、回転矢印で一覧を更新してください。";
            return true;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            _invalidated = false;
            Notice = "一覧を開けません。筆跡は保持中。ノートの「更新」、またはPC画面の回転矢印で再試行してください。";
            return false;
        }
    }
}
