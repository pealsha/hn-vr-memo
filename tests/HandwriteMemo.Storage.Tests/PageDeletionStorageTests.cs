using System.Diagnostics;
using System.Text.Json.Nodes;
using HandwriteMemo.Core;

[TestClass]
public sealed class PageDeletionStorageTests
{
    [TestMethod]
    public void Run()
    {
        OwnFilesOnly();
        LockAndRetry();
        RejectLiveAndForeignEntries();
        RejectReplacedPage();
        RejectJunction();
    }

    private static void OwnFilesOnly()
    {
        var (root, catalog, entry) = TrashPage();
        var other = catalog.List().Single();
        var otherBytes = File.ReadAllBytes(other.JsonPath);
        var source = Path.GetDirectoryName(entry.JsonPath)!;
        var stem = Path.GetFileNameWithoutExtension(entry.JsonPath);
        string[] owned = [entry.JsonPath, Path.ChangeExtension(entry.JsonPath, ".png"), entry.JsonPath + ".bak", entry.JsonPath + ".v1.bak",
            entry.JsonPath + "." + Guid.NewGuid().ToString("N") + ".tmp",
            Path.ChangeExtension(entry.JsonPath, ".png") + "." + Guid.NewGuid().ToString("N") + ".tmp",
            entry.JsonPath + ".v1.bak." + Guid.NewGuid().ToString("N") + ".tmp"];
        foreach (var file in owned.Skip(2))
            File.WriteAllText(file, "synthetic backup/temp");
        string[] unowned = [Path.Combine(source, "other.json"), Path.Combine(source, stem + ".other.json"),
            Path.Combine(source, stem + ".png.json"), Path.Combine(source, stem + ".json.tmp"), Path.Combine(root, "outside.txt")];
        foreach (var file in unowned)
            File.WriteAllText(file, "must remain");
        var nested = Path.Combine(source, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, stem + ".json"), "must remain");
        var result = catalog.DeletePermanently(entry);
        Check(result.Succeeded && owned.All(p => !File.Exists(p)), "owned files not deleted");
        Check(unowned.All(p => File.ReadAllText(p) == "must remain") && Directory.Exists(nested), "unrelated file/directory changed");
        Check(File.ReadAllBytes(other.JsonPath).SequenceEqual(otherBytes), "active page changed");
    }

    private static void LockAndRetry()
    {
        var (_, catalog, entry) = TrashPage();
        string png = Path.ChangeExtension(entry.JsonPath, ".png");
        using (var locked = new FileStream(png, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = catalog.DeletePermanently(entry);
            Check(!failed.Succeeded && failed.Errors.Any(e => e.Path == png), "locked companion not reported");
            Check(failed.RemainingFiles.Contains(png) && failed.RemainingFiles.Contains(entry.JsonPath), "remaining paths incomplete");
            Check(catalog.List(true).Single().Document.Id == entry.Document.Id, "retry page disappeared");
        }
        Check(catalog.DeletePermanently(catalog.List(true).Single()).Succeeded, "retry failed after unlock");
        Check(catalog.List(true).Count == 0 && !Directory.Exists(Path.GetDirectoryName(entry.JsonPath)), "deleted page still listed");

        var (_, secondCatalog, second) = TrashPage();
        using var jsonLock = new FileStream(second.JsonPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var result = secondCatalog.DeletePermanently(second);
        Check(!result.Succeeded && File.Exists(Path.ChangeExtension(second.JsonPath, ".png")), "unreadable JSON caused deletion");
    }

    private static void RejectLiveAndForeignEntries()
    {
        var (root, catalog, trash) = TrashPage();
        var live = catalog.List().Single();
        ThrowsArgument(() => catalog.DeletePermanently(live));
        ThrowsArgument(() => catalog.DeletePermanently(live with { IsTrash = true }));
        ThrowsArgument(() => catalog.DeletePermanently(trash with { JsonPath = Path.Combine(root, "sibling", "page.json") }));
        ThrowsArgument(() => catalog.DeletePermanently(trash with { JsonPath = Path.Combine(Path.GetDirectoryName(trash.JsonPath)!, "..", "page.json") }));
        Check(File.Exists(live.JsonPath) && File.Exists(trash.JsonPath), "rejected entries changed files");
    }

    private static void RejectReplacedPage()
    {
        var (_, catalog, entry) = TrashPage();
        var json = JsonNode.Parse(File.ReadAllText(entry.JsonPath))!;
        json["id"] = Guid.NewGuid().ToString();
        File.WriteAllText(entry.JsonPath, json.ToJsonString());
        var result = catalog.DeletePermanently(entry);
        Check(!result.Succeeded && File.Exists(entry.JsonPath) && File.Exists(Path.ChangeExtension(entry.JsonPath, ".png")), "replaced page deleted");
    }

    private static void RejectJunction()
    {
        var (root, catalog, entry) = TrashPage();
        string source = Path.GetDirectoryName(entry.JsonPath)!;
        string detached = Path.Combine(root, "outside-trash");
        Directory.Move(source, detached);
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", source, detached })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Check(process.ExitCode == 0, "junction setup failed: " + output);
        try
        {
            var before = Directory.GetFiles(detached).ToDictionary(
                path => Path.GetFileName(path) ?? throw new InvalidOperationException("Test file has no filename."), File.ReadAllBytes);
            var result = catalog.DeletePermanently(entry);
            Check(!result.Succeeded && result.Errors.Count > 0, "junction accepted");
            Check(before.All(p => File.ReadAllBytes(Path.Combine(detached, p.Key)).SequenceEqual(p.Value)), "junction target changed");
        }
        finally
        {
            // Removing the link itself keeps the output folder deletable by ordinary recursive deletion.
            Directory.Delete(source);
        }
    }

    private static (string Root, PageCatalog Catalog, PageCatalogEntry Entry) TrashPage()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "task-025", "storage-" + Guid.NewGuid().ToString("N")));
        var store = new MemoStore(Path.Combine(root, "pages"));
        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 2; i++)
        {
            var document = new MemoDocument(2, Guid.NewGuid(), now.AddSeconds(i), now.AddSeconds(i), BoardSize.Default, []);
            store.Save(document, stream => stream.Write([1]));
        }
        var catalog = new PageCatalog(store.Folder);
        catalog.MoveToTrash(catalog.List()[0]);
        return (root, catalog, catalog.List(true).Single());
    }
    private static void ThrowsArgument(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            return;
        }
        throw new InvalidOperationException("Unsafe deletion entry accepted");
    }
}
