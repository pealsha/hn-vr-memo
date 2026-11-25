using System.Drawing.Drawing2D;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

internal sealed class DesktopCatalogView : IDisposable
{
    private readonly MemoSession _page;
    private readonly Func<int> _retentionDays;
    private readonly TrashRetention _retention = new(() => DateTimeOffset.UtcNow, _ => { });
    private Dictionary<string, PageThumbnail> _previews = [];
    private int _refreshDepth;

    private sealed record PageThumbnail(MemoDocument Document, Bitmap Image);

    private const int PageItemHeight = 184;
    private const int PagePreviewHeight = 128;
    private const int MaximumPageItemHeight = 255; // Native owner-drawn ListBox limit.

    // At high DPI, give up preview space before cutting off titles or dates.
    internal int PreviewHeight => Math.Max(1, Math.Min(List.LogicalToDeviceUnits(PagePreviewHeight),
        List.ItemHeight - List.LogicalToDeviceUnits(PageItemHeight - PagePreviewHeight)));
    internal ListBox List { get; } = new PageListBox
    {
        Dock = DockStyle.Fill,
        DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = PageItemHeight,
        IntegralHeight = false,
        BorderStyle = BorderStyle.None,
        SelectionMode = SelectionMode.MultiExtended
    };

    internal DesktopCatalogView(MemoSession page, Func<int> retentionDays)
    {
        _page = page;
        _retentionDays = retentionDays;
        List.DrawItem += DrawPage;
    }

    internal IReadOnlyList<PageCatalogEntry> Entries => List.Items.Cast<PageCatalogEntry>().ToArray();
    internal IReadOnlyList<PageCatalogEntry> SelectedEntries => List.SelectedItems.Cast<PageCatalogEntry>().ToArray();
    internal bool IsSelectionMode => List.SelectionMode == SelectionMode.MultiSimple;
    internal bool IsRefreshing => _refreshDepth > 0;
    internal long SavedRevision { get; private set; } = -1;
    internal string? Warning { get; private set; }
    internal string? ErrorDetails { get; private set; }

    internal void ResizeRows() => List.ItemHeight = Math.Min(MaximumPageItemHeight, List.LogicalToDeviceUnits(PageItemHeight));
    internal int IndexOf(Guid id)
    {
        for (int i = 0; i < List.Items.Count; i++)
            if (((PageCatalogEntry)List.Items[i]).Document.Id == id)
                return i;
        return -1;
    }

    internal void InvalidateChangedLabel(int index, string title, bool pinned)
    {
        if (index < 0 || !List.IsHandleCreated)
            return;
        var document = ((PageCatalogEntry)List.Items[index]).Document;
        if (document.Title != title || document.IsPinned != pinned)
            List.Invalidate(List.GetItemRectangle(index));
    }

    private IDisposable BeginRefresh()
    {
        _refreshDepth++;
        return new RefreshScope(this);
    }

    private sealed class RefreshScope(DesktopCatalogView catalog) : IDisposable
    {
        private DesktopCatalogView? _catalog = catalog;

        public void Dispose()
        {
            if (_catalog is null)
                return;
            _catalog._refreshDepth--;
            _catalog = null;
        }
    }

    internal void SetSelectionMode(bool enabled, bool trash)
    {
        if (IsSelectionMode == enabled)
            return;
        int top = List.TopIndex;
        using (BeginRefresh())
        {
            // Native MultiSimple toggles a whole row without Ctrl and retains ListBox accessibility.
            // Keep one selection source: check marks and all batch actions use SelectedItems.
            List.SelectionMode = enabled ? SelectionMode.MultiSimple : SelectionMode.MultiExtended;
            List.ClearSelected();
            if (!enabled && !trash)
                SelectIndex(IndexOf(_page.Id), top);
            else
                RestoreScroll(top);
        }
        List.Invalidate();
    }

    internal void SelectAllPages(bool select = true)
    {
        int top = List.TopIndex;
        using (BeginRefresh())
        {
            try
            {
                for (int i = 0; i < List.Items.Count; i++)
                    List.SetSelected(i, select);
            }
            finally
            {
                RestoreScroll(top);
            }
        }
    }

    internal void SelectPages(IEnumerable<Guid> ids)
    {
        using (BeginRefresh())
        {
            List.ClearSelected();
            foreach (Guid id in ids)
                List.SetSelected(IndexOf(id), true);
        }
    }

    internal void SelectCurrentPage() => SelectIndex(IndexOf(_page.Id));

    internal void SelectIndex(int index) => SelectIndex(index, List.TopIndex);

    private void SelectIndex(int index, int top)
    {
        using var refresh = BeginRefresh();
        List.ClearSelected();
        if (index >= 0)
            List.SetSelected(index, true);
        RestoreScroll(top, index);
    }

    private void RestoreScroll(int top, int selectedIndex = -1)
    {
        if (List.Items.Count == 0)
            return;
        top = Math.Min(top, List.Items.Count - 1);
        if (List.TopIndex != top)
            List.TopIndex = top;
        if (selectedIndex < 0 || !List.IsHandleCreated ||
            List.ClientRectangle.IntersectsWith(List.GetItemRectangle(selectedIndex)))
            return;
        int wholeRows = Math.Max(1, List.ClientSize.Height / List.ItemHeight);
        List.TopIndex = Math.Clamp(List.TopIndex, Math.Max(0, selectedIndex - wholeRows + 1), selectedIndex);
    }

    internal void Reload(bool trash, bool selectCurrent, Guid displayedPage)
    {
        using var refresh = BeginRefresh();
        var selectedPaths = SelectedEntries.Select(e => e.JsonPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool currentWasListed = IndexOf(_page.Id) >= 0;
        int top = List.TopIndex;
        var catalog = _page.Catalog;
        var entries = catalog.List(trash);
        List.BeginUpdate();
        try
        {
            List.Items.Clear();
            foreach (var entry in entries)
                List.Items.Add(entry);
            if (IsSelectionMode || trash || !selectCurrent && displayedPage == _page.Id && currentWasListed)
            {
                for (int i = 0; i < entries.Count; i++)
                    if (selectedPaths.Contains(entries[i].JsonPath))
                        List.SetSelected(i, true);
                RestoreScroll(top);
            }
            else
                SelectIndex(IndexOf(_page.Id), top);
        }
        finally
        {
            List.EndUpdate();
        }
        // The native scroll bar can change the client width when row updates end.
        RefreshPreviews(entries);
        var firstName = catalog.Errors.Count == 0 ? "" : Path.GetFileName(catalog.Errors[0].Path);
        if (firstName.Length > 48)
            firstName = firstName[..48] + "…";
        Warning = catalog.Errors.Count == 0 ? null : $"読めないページ: {firstName}（計 {catalog.Errors.Count} 件）。元のファイルは残しています。「詳細」で対象を確認し、直した後に回転矢印で一覧を更新してください。";
        ErrorDetails = catalog.Errors.Count == 0 ? null : string.Join("\n\n", catalog.Errors.Select(e => e.Path + "\n" + e.Error.Message));
        SavedRevision = _page.SavedRevision;
        _page.NotifyCatalogChanged();
    }

    private void RefreshPreviews(IReadOnlyList<PageCatalogEntry> entries)
    {
        int width = Math.Max(List.LogicalToDeviceUnits(20), List.ClientSize.Width - List.LogicalToDeviceUnits(24));
        var size = new Size(width, PreviewHeight);
        var previews = new Dictionary<string, PageThumbnail>(entries.Count);
        try
        {
            foreach (var entry in entries)
            {
                var document = entry.Document;
                var image = _previews.TryGetValue(entry.JsonPath, out var cached) && cached.Image.Size == size &&
                    HasSameInk(cached.Document, document)
                    ? cached.Image
                    : PagePreview.Create(document, size.Width, size.Height);
                previews.Add(entry.JsonPath, new(document, image));
            }
        }
        catch
        {
            DisposeUnusedPreviews(previews, _previews);
            throw;
        }
        DisposeUnusedPreviews(_previews, previews);
        _previews = previews;
    }

    private static bool HasSameInk(MemoDocument previous, MemoDocument current)
    {
        if (previous.BoardSize != current.BoardSize || previous.Strokes.Count != current.Strokes.Count)
            return false;
        for (int i = 0; i < previous.Strokes.Count; i++)
        {
            var before = previous.Strokes[i];
            var after = current.Strokes[i];
            if (before.Color != after.Color || before.WidthMetres != after.WidthMetres || !before.Points.SequenceEqual(after.Points))
                return false;
        }
        return true;
    }

    private static void DisposeUnusedPreviews(Dictionary<string, PageThumbnail> previews, Dictionary<string, PageThumbnail> retained)
    {
        foreach (var (path, preview) in previews)
            if (!retained.TryGetValue(path, out var keep) || keep.Image != preview.Image)
                preview.Image.Dispose();
    }

    private void DrawPage(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || List.Items[e.Index] is not PageCatalogEntry entry)
            return;
        int Px(int logical) => List.LogicalToDeviceUnits(logical);
        int previewHeight = PreviewHeight;
        float scale = List.DeviceDpi / 96f;
        var rect = e.Bounds;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var background = new SolidBrush(UiTheme.Background))
            e.Graphics.FillRectangle(background, rect);
        UiTheme.Tile(e.Graphics, new RectangleF(rect.X + Px(5), rect.Y + Px(3), rect.Width - 2 * Px(5), rect.Height - 2 * Px(3)),
            selected ? UiTheme.Selection : UiTheme.Surface, selected ? UiTheme.Accent : UiTheme.Border, (selected ? 2 : 1) * scale, UiDesign.DesktopRadius * scale);
        int x = rect.X + Px(12), y = rect.Y + Px(10), w = Math.Max(Px(20), rect.Width - Px(24));
        if (IsSelectionMode)
        {
            // Standard checkbox glyph; native selected state is the only source of its value.
            int side = CheckBoxRenderer.GetGlyphSize(e.Graphics, System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal).Width;
            int gutter = side + Px(10);
            CheckBoxRenderer.DrawCheckBox(e.Graphics, new Point(x, rect.Y + (rect.Height - side) / 2),
                selected ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal);
            x += gutter;
            w = Math.Max(Px(20), w - gutter);
        }
        if (_previews.TryGetValue(entry.JsonPath, out var preview))
            e.Graphics.DrawImage(preview.Image, new Rectangle(x, y, w, previewHeight));
        var document = entry.Document;
        bool live = document.Id == _page.Id && !entry.IsTrash;
        var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(e.Graphics, ((live ? _page.IsPinned : document.IsPinned) ? "★ " : "") + (live ? _page.DisplayName : document.DisplayName), List.Font,
            new Rectangle(x, y + previewHeight, w, Px(24)), selected ? UiTheme.OnAccent : List.ForeColor, flags);
        var dateFont = UiTheme.DesktopSmallFont;
        var remaining = entry.IsTrash ? _retention.RemainingDays(entry, _retentionDays()) : null;
        if (remaining is { } days)
        {
            string text = $"あと {days} 日";
            int badgeWidth = TextRenderer.MeasureText(e.Graphics, text, dateFont).Width + Px(8);
            var badge = new Rectangle(x + w - badgeWidth, y + Math.Max(0, previewHeight - Px(24)), badgeWidth, Px(24));
            UiTheme.Tile(e.Graphics, badge, UiTheme.Surface, UiTheme.Border, scale, 4 * scale);
            TextRenderer.DrawText(e.Graphics, text, dateFont, badge, UiTheme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
        TextRenderer.DrawText(e.Graphics, (live ? _page.Title : document.Title).Length > 0 ? $"作成 {document.CreatedUtc.ToLocalTime():yyyy/MM/dd  HH:mm}" : "", dateFont,
            new Rectangle(x, y + previewHeight + Px(24), w, Px(22)), selected ? UiTheme.OnAccent : UiTheme.Secondary, flags);
        if ((e.State & DrawItemState.Focus) != 0)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(rect, -Px(8), -Px(5)), selected ? UiTheme.OnAccent : UiTheme.Text, selected ? UiTheme.Selection : UiTheme.Surface);
    }

    public void Dispose()
    {
        List.DrawItem -= DrawPage;
        foreach (var preview in _previews.Values)
            preview.Image.Dispose();
        _previews.Clear();
    }
}
