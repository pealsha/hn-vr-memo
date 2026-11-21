using HandwriteMemo.Core;

[TestClass]
public sealed class TrashRetentionStorageTests
{
    [TestMethod]
    public void Run()
    {
        DefaultThirtyDaysDeletesOnlyAtDeadline();
        DisabledDoesNotInitializeOrReportDays();
        LegacyTrashStartsAtFirstRetentionRun();
        LockedPageIsRetainedAndLogged();
        TrashTimestampStaysOutOfRestoredPageAndIsRenewed();
        FailedTrashMoveLeavesActivePageUntouched();
        MetadataWriteFailureLeavesActivePageUntouched();
        InvalidMetadataIsNeverDeleted();
        FailedPermanentDeletePreservesTimestamp();
        SuccessfulRetentionLogsItsTotal();
    }

    private static void DefaultThirtyDaysDeletesOnlyAtDeadline()
    {
        using var folder = new TestFolder();
        var catalog = TrashOne(folder.Path);
        var start = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var retention = new TrashRetention(() => start, _ =>
        {
        });
        Check(retention.Run(catalog, 30) == 0, "initial retention removed a legacy page");
        var entry = catalog.List(true).Single();
        Check(entry.TrashedUtc == start, "legacy page did not receive the first-run timestamp");
        Check(retention.RemainingDays(entry, 30) == 30, "initial remaining days is not 30");

        var beforeDeadline = new TrashRetention(() => start.AddDays(30).AddTicks(-1), _ =>
        {
        });
        Check(beforeDeadline.Run(catalog, 30) == 0 && catalog.List(true).Count == 1, "page was removed before the exact deadline");
        Check(beforeDeadline.RemainingDays(catalog.List(true).Single(), 30) == 1, "partial final day was not rounded up");
        var deadline = new TrashRetention(() => start.AddDays(30), _ =>
        {
        });
        Check(deadline.Run(catalog, 30) == 1 && catalog.List(true).Count == 0, "page was not removed at the 30-day deadline");
    }

    private static void DisabledDoesNotInitializeOrReportDays()
    {
        using var folder = new TestFolder();
        var catalog = TrashOne(folder.Path);
        var retention = new TrashRetention(() => new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), _ =>
        {
        });
        var legacy = catalog.List(true).Single();
        Check(retention.Run(catalog, 0) == 0 && catalog.List(true).Single().TrashedUtc is not null, "disabled retention did not record the first-run timestamp");
        Check(retention.RemainingDays(legacy, 0) is null, "disabled retention returned a day count");
    }

    private static void LegacyTrashStartsAtFirstRetentionRun()
    {
        using var folder = new TestFolder();
        var catalog = TrashOne(folder.Path);
        var logs = new List<string>();
        var initial = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var retention = new TrashRetention(() => initial, logs.Add);
        retention.Run(catalog, 1);
        Check(catalog.List(true).Single().TrashedUtc == initial && logs.Any(log => log.Contains("initialized legacy", StringComparison.Ordinal)),
            "old trash did not start at the first retention run");
    }

    private static void LockedPageIsRetainedAndLogged()
    {
        using var folder = new TestFolder();
        var catalog = TrashOne(folder.Path);
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        new TrashRetention(() => start, _ =>
        {
        }).Run(catalog, 1);
        var entry = catalog.List(true).Single();
        var logs = new List<string>();
        using (var locked = new FileStream(entry.JsonPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var removed = new TrashRetention(() => start.AddDays(1), logs.Add).Run(catalog, 1);
            Check(removed == 0 && File.Exists(entry.JsonPath), "locked page was removed");
        }
        Check(logs.Any(log => log.Contains("could not", StringComparison.Ordinal)), "retention failure was not logged");
    }

    private static void TrashTimestampStaysOutOfRestoredPageAndIsRenewed()
    {
        using var folder = new TestFolder();
        var catalog = CreateCatalog(folder.Path);
        var live = catalog.List().Single();
        catalog.MoveToTrash(live);
        var firstTrash = catalog.List(true).Single();
        var firstMetadata = Path.Combine(Path.GetDirectoryName(firstTrash.JsonPath)!, ".trashed-at");
        Check(firstTrash.TrashedUtc is not null && File.Exists(firstMetadata) && File.ReadAllText(firstMetadata).Contains("trashedUtc", StringComparison.Ordinal),
            "moving to trash did not create its timestamp");
        catalog.Restore(firstTrash);
        var restored = catalog.List().Single();
        Check(!File.Exists(Path.Combine(folder.Path, ".trashed-at")), "trash metadata was restored with the page");
        catalog.MoveToTrash(restored);
        var retrash = catalog.List(true).Single();
        Check(retrash.TrashedUtc is not null && File.Exists(Path.Combine(Path.GetDirectoryName(retrash.JsonPath)!, ".trashed-at")),
            "re-trashing did not create a fresh timestamp");
    }

    private static void FailedTrashMoveLeavesActivePageUntouched()
    {
        using var folder = new TestFolder();
        var catalog = CreateCatalog(folder.Path);
        var entry = catalog.List().Single();
        File.WriteAllText(Path.Combine(folder.Path, "Trash"), "blocks trash directory");
        try
        {
            catalog.MoveToTrash(entry);
            throw new InvalidOperationException("blocked trash move unexpectedly succeeded");
        }
        catch (IOException)
        {
        }
        Check(File.Exists(entry.JsonPath) && catalog.List().Single().Document.Id == entry.Document.Id,
            "a failed trash move removed the active page");
    }

    private static void MetadataWriteFailureLeavesActivePageUntouched()
    {
        using var folder = new TestFolder();
        var catalog = new PageCatalog(folder.Path, (_, _) => throw new IOException("synthetic metadata write failure"));
        var store = new MemoStore(folder.Path);
        var time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var saved = store.Save(new MemoDocument(2, Guid.NewGuid(), time, time, BoardSize.Default, []), stream => stream.Write([1]));
        var entry = catalog.List().Single();
        try
        {
            catalog.MoveToTrash(entry);
            throw new InvalidOperationException("metadata write failure unexpectedly moved page");
        }
        catch (IOException)
        {
        }
        Check(File.Exists(saved.JsonPath) && catalog.List().Single().Document.Id == entry.Document.Id && catalog.List(true).Count == 0,
            "metadata write failure did not preserve the active page");
    }

    private static void InvalidMetadataIsNeverDeleted()
    {
        using var folder = new TestFolder();
        var catalog = CreateCatalog(folder.Path);
        catalog.MoveToTrash(catalog.List().Single());
        var trash = catalog.List(true).Single();
        var metadataPath = Path.Combine(Path.GetDirectoryName(trash.JsonPath)!, ".trashed-at");
        foreach (var invalid in new[] { "not json", "{}", "{\"trashedUtc\":null}" })
        {
            File.WriteAllText(metadataPath, invalid);
            var listed = catalog.List(true);
            Check(listed.Count == 1 && listed.Single().Document.Id == trash.Document.Id && listed.Single().TrashedUtc is null && catalog.Errors.Count == 1,
                "invalid metadata hid an otherwise valid trash page from the list");
            var logs = new List<string>();
            var removed = new TrashRetention(() => new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), logs.Add).Run(catalog, 1);
            Check(removed == 0 && File.Exists(trash.JsonPath) && File.ReadAllText(metadataPath) == invalid &&
                logs.Any(log => log.Contains("could not list", StringComparison.Ordinal)),
                "invalid metadata was removed or overwritten instead of retained for recovery");
        }
        catalog.Restore(catalog.List(true).Single());
        Check(catalog.List().Single().Document.Id == trash.Document.Id, "a page with invalid trash metadata could not be restored manually");
    }

    private static void FailedPermanentDeletePreservesTimestamp()
    {
        using var folder = new TestFolder();
        var catalog = CreateCatalog(folder.Path);
        catalog.MoveToTrash(catalog.List().Single());
        var entry = catalog.List(true).Single();
        var timestamp = entry.TrashedUtc;
        var metadata = Path.Combine(Path.GetDirectoryName(entry.JsonPath)!, ".trashed-at");
        using (var locked = new FileStream(Path.ChangeExtension(entry.JsonPath, ".png"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(!catalog.DeletePermanently(entry).Succeeded, "locked companion allowed permanent deletion");
        }
        var afterFailure = catalog.List(true).Single();
        Check(File.Exists(metadata) && afterFailure.TrashedUtc == timestamp,
            "failed permanent deletion discarded the retention timestamp");
    }

    private static void SuccessfulRetentionLogsItsTotal()
    {
        using var folder = new TestFolder();
        var catalog = TrashOne(folder.Path);
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        new TrashRetention(() => start, _ =>
        {
        }).Run(catalog, 1);
        var logs = new List<string>();
        Check(new TrashRetention(() => start.AddDays(1), logs.Add).Run(catalog, 1) == 1 &&
            logs.Last().Contains("1 page(s)", StringComparison.Ordinal), "successful deletion did not end with a total count log");
    }

    private static PageCatalog TrashOne(string root)
    {
        var catalog = CreateCatalog(root);
        catalog.MoveToTrash(catalog.List().Single());
        var trashFolder = Path.GetDirectoryName(catalog.List(true).Single().JsonPath)!;
        File.Delete(Path.Combine(trashFolder, ".trashed-at"));
        return catalog;
    }

    private static PageCatalog CreateCatalog(string root)
    {
        var store = new MemoStore(root);
        var time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var document = new MemoDocument(2, Guid.NewGuid(), time, time, BoardSize.Default, []);
        store.Save(document, stream => stream.Write([1]));
        return new PageCatalog(root);
    }

    private sealed class TestFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.GetFullPath(System.IO.Path.Combine("artifacts", "task-031", "storage-tests", Guid.NewGuid().ToString("N")));
        public TestFolder() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}

