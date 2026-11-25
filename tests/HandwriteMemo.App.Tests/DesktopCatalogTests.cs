using System.Collections;
using System.Numerics;
using System.Reflection;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class DesktopCatalogTests
{
    [TestMethod]
    public void OpeningListedPagesKeepsRowsAndScroll()
    {
        var folder = Directory.CreateTempSubdirectory("hnvrmemo-catalog-");
        try
        {
            var (engine, page) = CreatePages(folder.FullName, 12);
            using var window = new DesktopWindow(engine, page, () => 60);
            window.Show();
            Application.DoEvents();
            var list = window.PagesList;
            var entries = window.Entries;
            list.TopIndex = 4;
            int top = list.TopIndex;
            Check(top == 4, "Fixture must scroll down");
            int clicked = top + (list.ClientSize.Height > list.ItemHeight + 30 ? 1 : 0);
            SelectionModeTests.ClickRow(list, clicked);
            Check(page.Id == entries[clicked].Document.Id && list.SelectedIndex == clicked, "Click opens the visible row");
            Check(list.TopIndex == top, "Click preserves the scrolled position");
            Check(ReferenceEquals(list.Items[clicked], entries[clicked]), "Click keeps the existing list rows");

            window.NextButton.PerformClick();
            Application.DoEvents();
            Check(page.Id == entries[clicked + 1].Document.Id && ReferenceEquals(list.SelectedItem, entries[clicked + 1]),
                "Next selects the existing row");

            Check(page.Open(entries[0], 61), "Open a page above the viewport through the shared session");
            Application.DoEvents();
            Check(list.TopIndex == 0 && ReferenceEquals(list.SelectedItem, entries[0]), "VR page switch reveals the existing row");

            list.TopIndex = 4;
            Check(page.NewPage(62), "Create a new blank page through the shared session");
            Application.DoEvents();
            Check(list.SelectedItems.Count == 0 && list.TopIndex == top, "Blank page clears selection and preserves scroll");
            Check(ReferenceEquals(list.Items[0], entries[0]), "Blank page keeps the existing list rows");
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ReloadAfterSavingKeepsScrollAndReusesUnchangedThumbnail()
    {
        var folder = Directory.CreateTempSubdirectory("hnvrmemo-previews-");
        try
        {
            var (_, page) = CreatePages(folder.FullName, 12);
            using var catalog = new DesktopCatalogView(page, () => 30);
            using var host = new Form { ClientSize = new(290, 420) };
            host.Controls.Add(catalog.List);
            host.Show();
            catalog.Reload(trash: false, selectCurrent: true, displayedPage: page.Id);
            var list = catalog.List;
            var edited = catalog.Entries[5];
            var unchanged = catalog.Entries[4];
            var oldPreview = Thumbnail(catalog, edited);
            var keptPreview = Thumbnail(catalog, unchanged);

            list.TopIndex = 4;
            Check(page.Open(edited, 60) && page.BeginDesktop(61), "Open the second visible row and start drawing");
            page.DesktopPoint(new(.3f, .5f), true, 61, DrawingTool.Pen);
            page.DesktopPoint(new(.7f, .5f), true, 62, DrawingTool.Pen);
            page.DesktopPoint(null, false, 63, DrawingTool.Pen);
            Check(page.SaveState == PageSaveState.Saved, "Edit is saved before reloading");

            catalog.Reload(trash: false, selectCurrent: true, displayedPage: page.Id);
            Check(list.SelectedIndex == 5 && list.TopIndex == 4, "Reload restores the viewport when the selected row is visible");
            Check(ReferenceEquals(keptPreview, Thumbnail(catalog, unchanged)), "Reload reuses an unchanged bitmap");
            var updated = Thumbnail(catalog, edited);
            var ink = updated.GetPixel(updated.Width / 2, updated.Height / 2);
            Check(!ReferenceEquals(oldPreview, updated) && ink.R < 90, "Reload draws the newly saved ink");
            Assert.ThrowsExactly<ArgumentException>(() => oldPreview.GetPixel(0, 0));

            page.Catalog.MoveToTrash(unchanged);
            catalog.Reload(trash: false, selectCurrent: true, displayedPage: page.Id);
            Assert.ThrowsExactly<ArgumentException>(() => keptPreview.GetPixel(0, 0));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static (HandwriteEngine Engine, MemoSession Page) CreatePages(string folder, int count)
    {
        var origin = DateTimeOffset.UtcNow.AddMinutes(-10);
        var store = new MemoStore(folder);
        for (int i = 0; i < count; i++)
        {
            var created = origin.AddSeconds(i);
            var document = new MemoDocument(2, Guid.NewGuid(), created, created, BoardSize.Default, []);
            store.Save(document, stream => MemoRenderer.WritePng(document, stream));
        }
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        return (engine, new MemoSession(engine, folder, origin, _ => { }));
    }

    private static Bitmap Thumbnail(DesktopCatalogView catalog, PageCatalogEntry entry)
    {
        // Observe bitmap identity and lifetime without exposing ownership through the production API.
        var cache = (IDictionary)typeof(DesktopCatalogView).GetField("_previews", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(catalog)!;
        var thumbnail = cache[entry.JsonPath]!;
        return (Bitmap)thumbnail.GetType().GetProperty("Image")!.GetValue(thumbnail)!;
    }
}
