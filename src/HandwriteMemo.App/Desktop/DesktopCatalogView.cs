using System.Drawing.Drawing2D;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

internal sealed class DesktopCatalogView : IDisposable
{
    private readonly MemoSession _page;
    private readonly Func<int> _retentionDays;
    private readonly TrashRetention _retention = new(() => DateTimeOffset.UtcNow, _ => { });
    private readonly Dictionary<string, Bitmap> _previews = [];
    private int _refreshDepth;

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
    internal int IndexOf(Guid id) => Entries.ToList().FindIndex(entry => entry.Document.Id == id);

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
                SelectCurrentPage();
            else if (List.Items.Count > 0)
                List.TopIndex = Math.Min(top, List.Items.Count - 1);
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
                if (List.Items.Count > 0)
                    List.TopIndex = Math.Min(top, List.Items.Count - 1);
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

    internal void SelectIndex(int index)
    {
        using var refresh = BeginRefresh();
        List.ClearSelected();
        if (index >= 0)
            List.SetSelected(index, true);
    }

    internal void Reload(bool trash, bool selectCurrent, Guid displayedPage)
    {
        using (BeginRefresh())
        {
            List.BeginUpdate();
            try
            {
                var selectedPaths = SelectedEntries.Select(e => e.JsonPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                bool currentWasListed = Entries.Any(e => e.Document.Id == _page.Id);
                int top = List.TopIndex;
                var catalog = _page.Catalog;
                var entries = catalog.List(trash);
                foreach (var image in _previews.Values)
                    image.Dispose();
                _previews.Clear();
                List.Items.Clear();
                int previewWidth = Math.Max(List.LogicalToDeviceUnits(20), List.ClientSize.Width - List.LogicalToDeviceUnits(24));
                int previewHeight = PreviewHeight;
                foreach (var entry in entries)
                {
                    _previews[entry.JsonPath] = PagePreview.Create(entry.Document, previewWidth, previewHeight);
                    List.Items.Add(entry);
                }
                if (IsSelectionMode || trash || !selectCurrent && displayedPage == _page.Id && currentWasListed)
                {
                    for (int i = 0; i < entries.Count; i++)
                        if (selectedPaths.Contains(entries[i].JsonPath))
                            List.SetSelected(i, true);
                    if (entries.Count > 0)
                        List.TopIndex = Math.Min(top, entries.Count - 1);
                }
                else
                    SelectCurrentPage();
                var firstName = catalog.Errors.Count == 0 ? "" : Path.GetFileName(catalog.Errors[0].Path);
                if (firstName.Length > 48)
                    firstName = firstName[..48] + "…";
                Warning = catalog.Errors.Count == 0 ? null : $"読めないページ: {firstName}（計 {catalog.Errors.Count} 件）。元のファイルは残しています。「詳細」で対象を確認し、直した後に回転矢印で一覧を更新してください。";
                ErrorDetails = catalog.Errors.Count == 0 ? null : string.Join("\n\n", catalog.Errors.Select(e => e.Path + "\n" + e.Error.Message));
                SavedRevision = _page.SavedRevision;
                _page.NotifyCatalogChanged();
            }
            finally
            {
                List.EndUpdate();
            }
        }
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
        if (_previews.TryGetValue(entry.JsonPath, out var image))
            e.Graphics.DrawImage(image, new Rectangle(x, y, w, previewHeight));
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
        foreach (var image in _previews.Values)
            image.Dispose();
        _previews.Clear();
    }
}
