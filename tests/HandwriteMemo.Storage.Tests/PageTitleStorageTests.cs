using System.Text.Json.Nodes;
using HandwriteMemo.Core;

[TestClass]
public sealed class PageTitleStorageTests
{
    [TestMethod]
    public void Run()
    {
        var folder = Path.GetFullPath(Path.Combine("artifacts", "title-storage-" + Guid.NewGuid().ToString("N")));
        var store = new MemoStore(folder);
        var now = DateTimeOffset.UtcNow;
        var document = new MemoDocument(2, Guid.NewGuid(), now, now, BoardSize.Default, []);
        var files = store.Save(document, s => s.Write([1]));
        var original = JsonNode.Parse(File.ReadAllText(files.JsonPath))!.AsObject();
        original.Remove("title");
        File.WriteAllText(files.JsonPath, original.ToJsonString());
        var before = File.ReadAllBytes(files.JsonPath);
        Check(store.Read(files.JsonPath).Title == "", "old v2 without title");
        Check(before.SequenceEqual(File.ReadAllBytes(files.JsonPath)), "reading modified legacy bytes");
        original["title"] = null;
        File.WriteAllText(files.JsonPath, original.ToJsonString());
        Check(store.Read(files.JsonPath).Title == "", "null title fallback");
        original["title"] = 123;
        File.WriteAllText(files.JsonPath, original.ToJsonString());
        try
        {
            store.Read(files.JsonPath);
            throw new InvalidOperationException("numeric title accepted");
        }
        catch (InvalidDataException)
        {
        }
        original.Remove("title");
        File.WriteAllText(files.JsonPath, original.ToJsonString());
        string title = "日本語 & < > / \\ : ? 😀\n" + new string('長', 1000);
        var named = new MemoDocument(2, document.Id, now, now, document.BoardSize, document.Strokes, true, title);
        var renamed = store.Save(named, s => s.Write([2]));
        Check(renamed == files && Directory.GetFiles(folder, "*.json").Length == 1, "rename changed basename");
        Check(store.Read(files.JsonPath).Title == title, "arbitrary title did not round trip");
        Check(!named.DisplayName.Contains('\n') && named.DisplayName.Contains('&'), "single-line display lost title text");
        var catalog = new PageCatalog(folder);
        catalog.SetPinned(catalog.List().Single(), false, (_, s) => s.Write([3]));
        Check(store.Read(files.JsonPath).Title == title, "pin operation lost title");
        store.Save(document, s => s.Write([4]));
        Check(store.Read(files.JsonPath).Title == "" && store.Read(files.JsonPath).DisplayName.Contains(now.ToLocalTime().ToString("yyyy/MM/dd")), "empty title fallback");
    }
}
