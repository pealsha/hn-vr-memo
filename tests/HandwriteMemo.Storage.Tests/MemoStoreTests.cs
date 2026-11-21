using System.Diagnostics;
using System.Runtime.InteropServices;
using HandwriteMemo.Core;

namespace HandwriteMemo.Storage.Tests;

[TestClass]
public sealed class MemoStoreTests
{
    [TestMethod]
    public void RoundTrip()
    {
        using var f = new Folder();
        var store = new MemoStore(f.Path);
        var points = new List<MemoPoint> { new(-.2f, .8f, 1), new(1.2f, -.1f, 2) };
        var doc = new MemoDocument(2, Guid.NewGuid(), T(0), T(4), new(.5f, .4f), [new MemoStroke(T(1), points, "#aBc123", .004f)], true);
        points[0] = new(0, 0, 1);

        var saved = store.Save(doc, s => s.Write([1]));
        var read = store.Read(saved.JsonPath);

        Ok(read.FormatVersion == 2 && read.IsPinned, "metadata changed");
        Ok(read.Strokes[0].Color == "#ABC123" && read.Strokes[0].WidthMetres == .004f && read.Strokes[0].Points[0].XMetres == -.2f && read.Strokes[0].Points[1].YMetres == -.1f,
            "style or outside point changed");
    }
    [TestMethod]
    public void Capture()
    {
        var size = new BoardSize(.8f, .4f);
        var engine = new HandwriteEngine(new BoardPose(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity), size);
        engine.UpdateProjectedTip(new TipSample(new System.Numerics.Vector3(0, .1f, .01f), true), true, .1f, 1);

        var doc = MemoDocument.Capture(Guid.NewGuid(), T(0), T(1), size, engine.Strokes);

        Ok(doc.FormatVersion == 2 && doc.Strokes[0].Points[0].XMetres == .4f && doc.Strokes[0].Points[0].YMetres == .1f, "capture conversion failed");
    }
    [TestMethod]
    public void V1Migration()
    {
        using var f = new Folder();
        var id = Guid.NewGuid();
        var path = Path.Combine(f.Path, $"legacy-{id:N}.json");
        var bytes = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new
        {
            formatVersion = 1,
            id,
            createdUtc = T(0),
            updatedUtc = T(4),
            boardWidthMetres = .8f,
            boardHeightMetres = .4f,
            strokes = new[] { new { startedUtc = T(1), points = new[] { new { u = .25f, v = 1f, elapsedSeconds = 1d } } } }
        }));
        File.WriteAllBytes(path, bytes);
        var store = new MemoStore(f.Path);

        var read = store.Read(path);

        Ok(read.FormatVersion == 2 && read.Strokes[0].Points[0].XMetres == .2f && read.Strokes[0].Points[0].YMetres == .4f, "v1 coordinate conversion failed");
        Ok(Math.Abs(read.Strokes[0].WidthMetres - (3.2f * .8f / 1024f)) < .000001, "legacy width failed");

        store.Save(read, s => s.Write([1]));

        Ok(File.ReadAllBytes(path + ".v1.bak").SequenceEqual(bytes), "v1 backup was not exact");
        Ok(store.Read(path).FormatVersion == 2, "migrated file unreadable");
    }
    [TestMethod]
    public void RepeatedSave()
    {
        using var f = new Folder();
        var id = Guid.NewGuid();
        var path = Path.Combine(f.Path, $"x-{id:N}.json");
        var original = V1(id);
        File.WriteAllBytes(path, original);
        var store = new MemoStore(f.Path);
        var doc = store.Read(path);
        store.Save(doc, s => s.Write([1]));
        var backup = File.ReadAllBytes(path + ".v1.bak");
        var newer = new MemoDocument(2, doc.Id, doc.CreatedUtc, T(5), doc.BoardSize, doc.Strokes);

        store.Save(newer, s => s.Write([2]));

        Ok(File.ReadAllBytes(path + ".v1.bak").SequenceEqual(backup) && backup.SequenceEqual(original), "repeat save changed v1 backup");
    }
    [TestMethod]
    public void InvalidWire()
    {
        using var f = new Folder();
        var store = new MemoStore(f.Path);
        foreach (var text in new[] { "not json", "{}", "{\"formatVersion\":2,\"id\":null}" })
        {
            var path = Path.Combine(f.Path, Guid.NewGuid() + ".json");
            File.WriteAllText(path, text);
            Throws<InvalidDataException>(() => store.Read(path));
        }
        var unsupportedPath = Path.Combine(f.Path, "unsupported.json");
        File.WriteAllText(unsupportedPath, "{\"formatVersion\":3}");
        Throws<NotSupportedException>(() => store.Read(unsupportedPath));
        Throws<ArgumentException>(() => new MemoStroke(T(1), [], "red"));
    }
    [TestMethod]
    public void BackupConflict()
    {
        using var f = new Folder();
        var id = Guid.NewGuid();
        var path = Path.Combine(f.Path, $"x-{id:N}.json");
        var original = V1(id);
        File.WriteAllBytes(path, original);
        File.WriteAllText(path + ".v1.bak", "other");
        var store = new MemoStore(f.Path);
        var doc = store.Read(path);

        Throws<IOException>(() => store.Save(doc, s => s.Write([1])));

        Ok(File.ReadAllBytes(path).SequenceEqual(original), "backup conflict overwrote original");
    }
    [TestMethod]
    public void PngFailure()
    {
        using var f = new Folder();
        var store = new MemoStore(f.Path);
        var doc = Doc();
        try
        {
            store.Save(doc, _ => throw new InvalidOperationException());
        }
        catch (InvalidOperationException)
        {
        }
        var path = Directory.GetFiles(f.Path, "*.json").Single();
        Ok(store.Read(path).Id == doc.Id, "PNG failure lost JSON");
    }
    [TestMethod]
    public void SnapshotIsImmutable()
    {
        var points = new List<MemoPoint> { new(.1f, .2f, 1) };
        var strokes = new List<MemoStroke> { new(T(1), points) };
        var doc = new MemoDocument(2, Guid.NewGuid(), T(0), T(2), BoardSize.Default, strokes);
        points[0] = new(.9f, .9f, 1);
        strokes.Clear();
        Ok(doc.Strokes.Count == 1 && doc.Strokes[0].Points[0].XMetres == .1f, "document retained caller collections");
    }
    [TestMethod]
    public void OverwriteKeepsBackup()
    {
        using var f = new Folder();
        var store = new MemoStore(f.Path);
        var doc = Doc();
        var first = store.Save(doc, s => s.Write([1]));
        store.Save(new MemoDocument(2, doc.Id, doc.CreatedUtc, T(10), doc.BoardSize, doc.Strokes), s => s.Write([2]));
        Ok(File.Exists(first.JsonPath + ".bak"), "backup was not made");
    }
    [TestMethod]
    public void InvalidPath()
    {
        using var f = new Folder();
        Throws<ArgumentException>(() => new MemoStore(f.Path).Read(Path.Combine(f.Path, "outside", "memo.json")));
    }
    [TestMethod]
    public void TempDoesNotReplaceJson()
    {
        using var f = new Folder();
        var store = new MemoStore(f.Path);
        var saved = store.Save(Doc(), s => s.Write([1]));
        File.WriteAllText(saved.JsonPath + ".interrupted.tmp", "{ broken");
        Ok(store.Read(saved.JsonPath).FormatVersion == 2, "temporary file changed committed JSON");
    }
    [TestMethod]
    public void NewPagesAreDistinct()
    {
        using var f = new Folder();
        var store = new MemoStore(f.Path);
        var a = store.Save(Doc(), s => s.Write([1]));
        var b = store.Save(Doc(), s => s.Write([1]));
        Ok(a.JsonPath != b.JsonPath && File.Exists(a.JsonPath) && File.Exists(b.JsonPath), "pages collided");
    }
    [TestMethod]
    public void RepeatedSavesKeepBasename()
    {
        using var f = new Folder();
        var store = new MemoStore(f.Path);
        var doc = Doc();
        var a = store.Save(doc, s => s.Write([1]));
        var b = store.Save(new MemoDocument(2, doc.Id, doc.CreatedUtc, T(10), doc.BoardSize, doc.Strokes), s => s.Write([2]));
        Ok(a == b, "repeated saves changed page basename");
    }
    [TestMethod]
    public void LocalFilenameKeepsUtcJson()
    {
        using var f = new Folder();
        var store = new MemoStore(f.Path);
        var doc = Doc();
        var saved = store.Save(doc, s => s.Write([1]));
        Ok(Path.GetFileNameWithoutExtension(saved.JsonPath) == $"{doc.CreatedUtc.ToLocalTime():yyyyMMdd-HHmmssfff}-{doc.Id:N}" && File.ReadAllText(saved.JsonPath).Contains("+00:00"),
            "local filename or UTC JSON failed");
    }
    [TestMethod]
    public void LocalFilenameCrossesDateBoundary()
    {
        using var f = new Folder();
        var store = new MemoStore(f.Path);
        var offset = TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.UtcNow);
        var utc = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(offset >= TimeSpan.Zero ? 23 : 0).AddMinutes(offset >= TimeSpan.Zero ? 59 : 1), TimeSpan.Zero);
        var doc = new MemoDocument(2, Guid.NewGuid(), utc, utc, BoardSize.Default, []);
        var saved = store.Save(doc, s => s.Write([1]));
        Ok(Path.GetFileNameWithoutExtension(saved.JsonPath).StartsWith($"{utc.ToLocalTime():yyyyMMdd-HHmmssfff}-", StringComparison.Ordinal), "filename did not use converted local time");
    }
    [TestMethod]
    public void LegacyFilenameIsNotRenamed()
    {
        using var f = new Folder();
        var id = Guid.NewGuid();
        var path = Path.Combine(f.Path, $"19700101-000000000-{id:N}.json");
        File.WriteAllBytes(path, V1(id));
        var store = new MemoStore(f.Path);
        var read = store.Read(path);
        var saved = store.Save(new MemoDocument(2, read.Id, read.CreatedUtc, T(10), read.BoardSize, read.Strokes, read.IsPinned), s => s.Write([2]));
        Ok(saved.JsonPath == path && File.Exists(path), "legacy basename changed");
    }
    [TestMethod]
    public void RepairPreviews()
    {
        using var f = new Folder();
        var store = new MemoStore(f.Path);
        var saved = store.Save(Doc(), s => s.Write([1]));
        File.Delete(saved.PngPath);
        File.WriteAllText(Path.Combine(f.Path, "bad.json"), "not json");
        var errors = store.RepairPreviews((_, s) => s.Write([9]));
        Ok(File.Exists(saved.PngPath) && errors.Count == 1, "repair failed");
    }
    [TestMethod]
    public void NullWireValues()
    {
        using var f = new Folder();
        var path = Path.Combine(f.Path, "null.json");
        File.WriteAllText(path, "{\"formatVersion\":2,\"id\":null}");
        Throws<InvalidDataException>(() => new MemoStore(f.Path).Read(path));
    }
    [TestMethod]
    public void RepairRendererFailure()
    {
        using var f = new Folder();
        var store = new MemoStore(f.Path);
        var saved = store.Save(Doc(), s => s.Write([1]));
        File.Delete(saved.PngPath);
        var errors = store.RepairPreviews((_, _) => throw new ExternalException("renderer failed"));
        Ok(errors.Count == 1 && errors[0].Error is ExternalException, "renderer failure not returned");
    }
    [TestMethod]
    public void KillAfterJsonReplace()
    {
        using var f = new Folder();
        var marker = Path.Combine(f.Path, "marker");
        using var child = StartSaveInterruption(f.Path, marker);
        try
        {
            var watch = Stopwatch.StartNew();
            while (!File.Exists(marker) && watch.Elapsed < TimeSpan.FromSeconds(5))
                Thread.Sleep(20);
            Ok(File.Exists(marker), "child did not reach PNG");
        }
        finally
        {
            // Disposing Process releases its handle; the child itself must also exit on test failure.
            if (!child.HasExited)
                child.Kill(entireProcessTree: true);
            Ok(child.WaitForExit(5000), "child did not exit after kill");
        }
        var json = Directory.GetFiles(f.Path, "*.json").Single();
        var store = new MemoStore(f.Path);
        Ok(store.Read(json).FormatVersion == 2, "JSON unreadable after kill");
        Ok(store.RepairPreviews((_, s) => s.Write([7])).Count == 0, "preview not repaired");
    }
    private static Process StartSaveInterruption(string folder, string marker)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "SaveInterruption.dll"));
        start.ArgumentList.Add(folder);
        start.ArgumentList.Add(marker);
        return Process.Start(start) ?? throw new InvalidOperationException();
    }
    [TestMethod]
    public void BackupDirectoryFailure()
    {
        using var f = new Folder();
        var id = Guid.NewGuid();
        var path = Path.Combine(f.Path, $"x-{id:N}.json");
        var original = V1(id);
        File.WriteAllBytes(path, original);
        Directory.CreateDirectory(path + ".v1.bak");
        var store = new MemoStore(f.Path);
        var doc = store.Read(path);
        Throws<IOException>(() => store.Save(doc, s => s.Write([1])));
        Ok(File.ReadAllBytes(path).SequenceEqual(original), "failed migration changed source JSON");
    }
    [TestMethod]
    public void InvalidExistingSourceIsPreserved()
    {
        using var f = new Folder();
        var store = new MemoStore(f.Path);
        var document = Doc();
        var path = store.Save(document, stream => stream.Write([1])).JsonPath;
        var other = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        other["id"] = Guid.NewGuid().ToString();
        foreach (var source in new[] { "not json", other.ToJsonString() })
        {
            File.WriteAllText(path, source);
            Throws<InvalidDataException>(() => store.Save(document, s => s.Write([1])));
            Ok(File.ReadAllText(path) == source, "invalid or different page was overwritten");
        }
        const string Future = "{\"formatVersion\":99}";
        File.WriteAllText(path, Future);
        Throws<NotSupportedException>(() => store.Save(document, s => s.Write([1])));
        Ok(File.ReadAllText(path) == Future, "unknown version was overwritten");
    }
    [TestMethod]
    public void RequiredWireFields()
    {
        using var f = new Folder();
        var store = new MemoStore(f.Path);
        var path = store.Save(Doc(), stream => stream.Write([1])).JsonPath;
        var original = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        var rootKeys = new[] { "formatVersion", "id", "createdUtc", "updatedUtc", "boardWidthMetres", "boardHeightMetres", "isPinned", "strokes" };
        foreach (var key in rootKeys)
        {
            var missing = original.DeepClone().AsObject();
            missing.Remove(key);
            File.WriteAllText(path, missing.ToJsonString());
            Throws<InvalidDataException>(() => store.Read(path));
            var nullValue = original.DeepClone();
            nullValue[key] = null;
            File.WriteAllText(path, nullValue.ToJsonString());
            Throws<InvalidDataException>(() => store.Read(path));
        }
        var mutations = new Action<System.Text.Json.Nodes.JsonNode>[]
        {
            n => n["strokes"]![0] = null,
            n => n["strokes"]![0]!["points"]![0] = null,
            n => n["strokes"]![0]!["color"] = "red",
            n => n["strokes"]![0]!["widthMetres"] = 0,
            n => n["strokes"]![0]!["points"]![0]!.AsObject().Remove("xMetres"),
            n => n["strokes"]![0]!["points"]![0]!["elapsedSeconds"] = -1,
        };
        foreach (var change in mutations)
        {
            var invalid = original.DeepClone();
            change(invalid);
            File.WriteAllText(path, invalid.ToJsonString());
            Throws<InvalidDataException>(() => store.Read(path));
        }
    }
    [TestMethod]
    public void PointClockOffsets()
    {
        var stroke = new MemoStroke(T(5), [new(.1f, .2f, 10000), new(.2f, .3f, 10001)]);
        var page = new MemoDocument(2, Guid.NewGuid(), T(0), T(6), BoardSize.Default, [stroke]);
        Ok(page.Strokes[0].StartedUtc == T(5), "valid point clock offset changed the stroke time");
        Throws<ArgumentOutOfRangeException>(() => new MemoDocument(2, Guid.NewGuid(), T(0), T(5), BoardSize.Default, [stroke]));
        var resized = new MemoDocument(2, page.Id, page.CreatedUtc, page.UpdatedUtc, new(.1f, .1f), page.Strokes);
        Ok(resized.Strokes[0].Points.SequenceEqual(stroke.Points) && resized.Strokes[0].WidthMetres == stroke.WidthMetres,
            "changing board dimensions must not rescale stored ink");
    }
    [TestMethod]
    public void RetiredWorldNames()
    {
        using var f = new Folder();
        var store = new MemoStore(f.Path);
        var path = store.Save(Doc(), s => s.Write([1])).JsonPath;
        var original = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        Ok(!original.AsObject().ContainsKey("worldNames"), "new v2 contains worldNames");
        foreach (var legacyValue in new[] { "[null]", "[\"World\",null]", "null", "123", "[\" \"]" })
        {
            var legacy = original.DeepClone();
            legacy["worldNames"] = System.Text.Json.Nodes.JsonNode.Parse(legacyValue);
            var bytes = System.Text.Encoding.UTF8.GetBytes(legacy.ToJsonString());
            File.WriteAllBytes(path, bytes);
            var read = store.Read(path);
            Ok(File.ReadAllBytes(path).SequenceEqual(bytes), "read changed source");
            store.Save(read, s => s.Write([1]));
            Ok(!System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject().ContainsKey("worldNames"), "retired field saved again");
            Ok(File.ReadAllBytes(path + ".bak").SequenceEqual(bytes), "legacy backup was not preserved");
        }
    }
    [TestMethod]
    public void CatalogOrdering()
    {
        using var f = new Folder();
        var store = new MemoStore(f.Path);
        var oldest = new MemoDocument(2, Guid.NewGuid(), T(1), T(1), BoardSize.Default, []);
        var pinned = new MemoDocument(2, Guid.NewGuid(), T(2), T(2), BoardSize.Default, [], true);
        var newest = new MemoDocument(2, Guid.NewGuid(), T(3), T(3), BoardSize.Default, []);
        store.Save(oldest, s => s.Write([1]));
        store.Save(pinned, s => s.Write([1]));
        store.Save(newest, s => s.Write([1]));
        var pages = new PageCatalog(f.Path).List();
        Ok(pages.Select(p => p.Document.Id).SequenceEqual([pinned.Id, newest.Id, oldest.Id]), "catalog order is wrong");
    }
    [TestMethod]
    public void CatalogErrors()
    {
        using var f = new Folder();
        var store = new MemoStore(f.Path);
        var doc = Doc();
        store.Save(doc, s => s.Write([1]));
        File.WriteAllText(Path.Combine(f.Path, "bad.json"), "broken");
        var catalog = new PageCatalog(f.Path);
        var pages = catalog.List();
        Ok(pages.Count == 1 && pages[0].Document.Id == doc.Id && catalog.Errors.Count == 1, "catalog did not retain valid pages or report invalid JSON");
    }
    [TestMethod]
    public void CatalogTrashAndRestore()
    {
        using var f = new Folder();
        var store = new MemoStore(f.Path);
        var doc = Doc();
        var saved = store.Save(doc, s => s.Write([1]));
        File.WriteAllText(saved.JsonPath + ".v1.bak", "backup");
        var catalog = new PageCatalog(f.Path);
        var entry = catalog.List().Single();
        catalog.MoveToTrash(entry);
        Ok(catalog.List().Count == 0, "page remained in active catalog");
        var trashed = catalog.List(true).Single();
        Ok(File.Exists(trashed.JsonPath) && File.Exists(Path.ChangeExtension(trashed.JsonPath, ".png")) && File.Exists(trashed.JsonPath + ".v1.bak"), "page companions were not moved");
        Ok(!Directory.EnumerateDirectories(Path.Combine(f.Path, "Trash"), ".moving-*", SearchOption.TopDirectoryOnly).Any(), "trash retained an incomplete move directory");
        catalog.Restore(trashed);
        var restored = catalog.List().Single();
        Ok(Path.GetFileName(restored.JsonPath) == Path.GetFileName(saved.JsonPath) && File.Exists(restored.JsonPath + ".v1.bak"), "page was not restored with basename and backup");
    }
    [TestMethod]
    public void CatalogRestoreConflict()
    {
        using var f = new Folder();
        var store = new MemoStore(f.Path);
        var doc = Doc();
        store.Save(doc, s => s.Write([1]));
        var catalog = new PageCatalog(f.Path);
        catalog.MoveToTrash(catalog.List().Single());
        var trashed = catalog.List(true).Single();
        File.WriteAllText(Path.Combine(f.Path, Path.GetFileName(trashed.JsonPath)), "occupied");
        Throws<IOException>(() => catalog.Restore(trashed));
        Ok(File.Exists(trashed.JsonPath), "conflicted restore removed the trashed page");
    }
    private static byte[] V1(Guid id) => System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new
    {
        formatVersion = 1,
        id,
        createdUtc = T(0),
        updatedUtc = T(4),
        boardWidthMetres = .48f,
        boardHeightMetres = .36f,
        strokes = Array.Empty<object>()
    }));
    private static MemoDocument Doc() => new(2, Guid.NewGuid(), T(0), T(4), BoardSize.Default, [new MemoStroke(T(1), [new MemoPoint(.1f, .2f, 1)])]);
    private static DateTimeOffset T(int s) => DateTimeOffset.UnixEpoch.AddSeconds(s);
    private static void Ok(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }
    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.GetFullPath(System.IO.Path.Combine("artifacts", "storage-tests", Guid.NewGuid().ToString("N")));
        public Folder() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch
            {
            }
        }
    }
}
