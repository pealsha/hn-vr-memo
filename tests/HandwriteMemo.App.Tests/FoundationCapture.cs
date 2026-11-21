using System.Drawing.Imaging;
using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

// Uses the real painters and hidden WinForms controls; runs without SteamVR.
internal static class FoundationCapture
{
    internal static void Run(string destination)
    {
        Directory.CreateDirectory(destination);
        var origin = new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);
        string folder = Path.Combine(destination, "fixture-" + Guid.NewGuid().ToString("N"));
        var store = new MemoStore(folder);
        var sample = Sample();
        for (int i = 0; i < 5; i++)
        {
            var doc = new MemoDocument(2, i == 0 ? sample.Id : Guid.NewGuid(), origin.AddDays(-i), origin.AddMinutes(1), sample.BoardSize,
                sample.Strokes, i == 0, i == 0 ? sample.Title : "メモ " + i);
            store.Save(doc, stream => MemoRenderer.WritePng(doc, stream));
        }
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var page = new MemoSession(engine, folder, origin, _ => { });
        using var browser = new VrPageBrowser(page);
        page.Open(page.Catalog.List().Single(e => e.Document.Id == sample.Id), 0);
        using var painter = new BoardPainter();
        Vr("vr-editor", default);
        Vr("vr-hover", default(FrameResult) with { CursorUv = ToolPalette.Center(8), Contact = ContactState.Proximity });
        Vr("vr-grab", default, highlighted: true);
        engine.SetEraserMode(EraserMode.Partial);
        foreach (var contact in new[] { ContactState.Proximity, ContactState.Contact })
            Vr("vr-erase-" + contact.ToString().ToLowerInvariant(), default(FrameResult) with
            { CursorUv = new(.42f, .40f), Contact = contact, Tool = DrawingTool.Eraser });
        browser.Handle(default(FrameResult) with { Commands = [BoardCommand.PageList] }, 1);
        Vr("vr-list", default);
        browser.ReturnToPage();
        painter.Draw(engine, default, new("入力を確認してください", VrInputState.HandTracking), page: null);
        SaveScene("vr-notice");
        painter.Draw(engine, default, new("入力を確認してください", IsError: true), page: null);
        SaveScene("vr-error");
        using (var dashboard = VrOverlaySprites.MakeDashboard(hoverMask: 1, boardVisible: true)) Save(dashboard, "vr-dashboard");
        using (var cue = VrOverlaySprites.MakeWristCue(false)) Save(cue, "vr-wrist-pending");
        using (var cue = VrOverlaySprites.MakeWristCue(true)) Save(cue, "vr-wrist-confirmed");
        using (var handle = VrOverlaySprites.MakeResizeHandle(enabled: false)) Save(handle, "vr-resize-disabled");
        using (var handle = VrOverlaySprites.MakeResizeHandle(highlighted: true, resizing: true)) Save(handle, "vr-resize-held");
        using (var png = MemoRenderer.Render(sample)) Save(png, "exported-page");
        File.Copy(page.Catalog.List().Single(e => e.Document.Id == sample.Id).JsonPath, Path.Combine(destination, "exported-page.json"), true);
        using var window = new DesktopWindow(engine, page, () => 2);
        window.CaptureWithoutActivation = true;
        window.ShowInTaskbar = false;
        window.StartPosition = FormStartPosition.Manual;
        window.Location = new(-32000, -32000);
        window.Size = new(1280, 860);
        window.Show();
        window.RefreshCatalog();
        Pc("pc-editor");
        Descendants(window).OfType<ToolbarTile>().Single(b => b.AccessibleName == "部分消し").PerformClick();
        var paper = window.Canvas.PaperBounds;
        var point = new Point((int)(paper.X + paper.Width * .82f), (int)(paper.Y + paper.Height * .84f));
        window.Canvas.MoveInput(point);
        Pc("pc-erase-hover");
        window.Canvas.BeginInput(point);
        Pc("pc-erase-contact");
        window.Canvas.EndInput();
        Descendants(window).OfType<ToolbarTile>().Single(b => b.AccessibleName == "ペン").PerformClick();
        using (var states = new Bitmap(680, 100))
        using (var g = Graphics.FromImage(states))
        using (var button = new DesignButton { Text = "更新", Size = new(128, 48), Font = window.Font })
        {
            g.Clear(UiTheme.Background);
            using var caption = new Font(UiTheme.FontFamily, 14, FontStyle.Regular, GraphicsUnit.Pixel);
            string[] labels = ["通常", "ホバー", "押下中", "選択中", "無効"];
            for (int i = 0; i < 5; i++)
            {
                using var state = new Bitmap(button.Width, button.Height);
                using var stateGraphics = Graphics.FromImage(state);
                button.Enabled = i != 4;
                DesignButtonPaint.Draw(button, stateGraphics, i == 1, i == 2, i == 3, i == 1);
                g.DrawImageUnscaled(state, 8 + i * 134, 10);
                g.DrawString(labels[i], caption, Brushes.Black, 8 + i * 134, 70);
            }
            Save(states, "pc-button-states");
        }
        using (var menu = new ContextMenuStrip())
        {
            UiTheme.StyleMenu(menu);
            menu.Items.Add("開く");
            menu.Items.Add("ピン留め").Select();
            menu.Items.Add("ゴミ箱へ");
            menu.Size = menu.GetPreferredSize(Size.Empty);
            using var image = new Bitmap(menu.Width, menu.Height);
            menu.DrawToBitmap(image, new(Point.Empty, menu.Size));
            Save(image, "pc-menu");
        }
        window.SelectButton.PerformClick();
        window.PagesList.SetSelected(0, true);
        window.PagesList.SetSelected(1, true);
        Pc("pc-selection");
        window.SelectButton.PerformClick();
        var trashEntry = page.Catalog.List().First(e => e.Document.Id != sample.Id);
        page.Catalog.MoveToTrash(trashEntry);
        Descendants(window).OfType<RadioButton>().Single(b => b.Text == "ゴミ箱").Checked = true;
        window.RefreshCatalog();
        if (window.PagesList.Items.Count > 0) window.PagesList.SelectedIndex = 0;
        Pc("pc-trash");
        using (var dialog = new TrashDeleteDialog(1, false))
        {
            using var host = new CaptureHost();
            dialog.TopLevel = false;
            host.Controls.Add(dialog);
            dialog.Show();
            host.Show();
            using var image = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(image, new(Point.Empty, dialog.Size));
            Save(image, "pc-delete-confirm");
        }
        Console.WriteLine("Captured real painters and hidden PC controls: " + Path.GetFullPath(destination));

        void Save(Bitmap image, string name) => image.Save(Path.Combine(destination, name + ".png"), ImageFormat.Png);
        void Vr(string name, FrameResult frame, bool highlighted = false)
        {
            painter.Draw(engine, frame, VrInputStatus.ForPage(page), highlighted, page, browser);
            SaveScene(name);
        }
        void SaveScene(string name)
        {
            using var image = new Bitmap(painter.Image.Width + 160, painter.Image.Height + painter.ToolsImage.Height + 100);
            using var g = Graphics.FromImage(image);
            g.Clear(Color.Transparent);
            g.DrawImageUnscaled(painter.ToolsImage, 30, 0);
            g.DrawImageUnscaled(painter.Image, 30, painter.ToolsImage.Height + ToolPalette.DockGapPixels);
            using var handle = VrOverlaySprites.MakeResizeHandle();
            g.DrawImage(handle, image.Width - 120, image.Height - 95, 70, 70);
            Save(image, name);
        }
        void Pc(string name)
        {
            window.PerformLayout();
            using var image = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(image, new(Point.Empty, window.Size));
            Save(image, name);
        }
    }

    internal static MemoDocument Sample()
    {
        var origin = new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);
        return new MemoDocument(2, Guid.Parse("65d871a6-2682-431d-a7ca-f04a19755493"), origin, origin.AddMinutes(1), BoardSize.Default,
        [
            Line("#182634", .0015f, new(.05f, .07f), new(.15f, .18f)),
            Line("#182634", .0015f, new(.15f, .07f), new(.05f, .18f)),
            Line("#C53939", .003f, new(.08f, .23f), new(.35f, .23f)),
            Line("#2464C4", .00075f, new(.08f, .27f), new(.35f, .27f)),
            Line("#23834B", .0015f, new(.10f, .30f), new(.13f, .28f), new(.16f, .30f), new(.19f, .28f)),
        ], true, "既存のページ");

        MemoStroke Line(string color, float width, params Vector2[] points) => new(origin,
            points.Select((p, i) => new MemoPoint(p.X, p.Y, i * .1)).ToArray(), color, width);
    }

    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));

    private sealed class CaptureHost : Form
    {
        internal CaptureHost()
        {
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new(-32000, -32000);
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000000;
                return parameters;
            }
        }
    }
}
