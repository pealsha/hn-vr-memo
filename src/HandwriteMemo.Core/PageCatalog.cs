using System.Text.Json;
using System.Text.Json.Serialization;

namespace HandwriteMemo.Core;

/// <summary>Lists pages without making the UI depend on the on-disk layout.</summary>
public sealed class PageCatalog
{
    private const string TrashName = "Trash";
    private const string TrashedAtName = ".trashed-at";
    private readonly string _folder;
    private readonly string _trashFolder;
    private readonly List<MemoStoreError> _errors = [];
    private readonly Action<string, DateTimeOffset> _writeTrashMetadata;

    public PageCatalog(string folder) : this(folder, WriteTrashedUtc)
    {
    }

    /// <summary>Creates a catalog with the supplied trash-timestamp writer. This overload supports isolated storage tests.</summary>
    public PageCatalog(string folder, Action<string, DateTimeOffset> writeTrashMetadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        _writeTrashMetadata = writeTrashMetadata ?? throw new ArgumentNullException(nameof(writeTrashMetadata));
        this._folder = Path.GetFullPath(folder);
        _trashFolder = Path.Combine(this._folder, TrashName);
        Directory.CreateDirectory(_folder);
    }

    public IReadOnlyList<MemoStoreError> Errors => _errors.AsReadOnly();

    public IReadOnlyList<PageCatalogEntry> List(bool trash = false)
    {
        _errors.Clear();
        var entries = new List<PageCatalogEntry>();
        IEnumerable<string> paths = trash
            ? EnumerateTrashJsonPaths()
            : Directory.EnumerateFiles(_folder, "*.json", SearchOption.TopDirectoryOnly);
        var listStore = trash ? null : new MemoStore(_folder);
        foreach (var path in paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            MemoDocument document;
            try
            {
                var store = listStore ?? new MemoStore(Path.GetDirectoryName(path)!);
                document = store.Read(path);
            }
            catch (Exception error)
            {
                _errors.Add(new MemoStoreError(path, error));
                continue;
            }

            DateTimeOffset? trashedUtc = null;
            if (trash)
            {
                try
                {
                    trashedUtc = ReadTrashedUtc(Path.GetDirectoryName(path)!);
                }
                catch (Exception error)
                {
                    _errors.Add(new MemoStoreError(path, error));
                }
            }
            entries.Add(new PageCatalogEntry(path, document, trash, trashedUtc));
        }
        return entries.OrderByDescending(entry => entry.Document.IsPinned)
            .ThenByDescending(entry => entry.Document.CreatedUtc)
            .ToList()
            .AsReadOnly();
    }

    public MemoDocument Read(PageCatalogEntry entry)
    {
        ValidateEntry(entry);
        return new MemoStore(Path.GetDirectoryName(entry.JsonPath)!).Read(entry.JsonPath);
    }

    public void SetPinned(PageCatalogEntry entry, bool pinned, Action<MemoDocument, Stream> writePng)
    {
        ArgumentNullException.ThrowIfNull(writePng);
        ValidateEntry(entry);
        var store = new MemoStore(Path.GetDirectoryName(entry.JsonPath)!);
        var current = store.Read(entry.JsonPath);
        if (current.IsPinned == pinned)
            return;
        var now = DateTimeOffset.UtcNow;
        var updated = new[] { current.CreatedUtc, current.UpdatedUtc, now }.Max();
        var changed = new MemoDocument(current.FormatVersion, current.Id, current.CreatedUtc, updated,
            current.BoardSize, current.Strokes, pinned, current.Title);
        store.Save(changed, stream => writePng(changed, stream));
    }

    public void MoveToTrash(PageCatalogEntry entry)
    {
        ValidateEntry(entry);
        if (entry.IsTrash)
            throw new ArgumentException("The page is already in the trash.", nameof(entry));
        MoveIntoIsolatedDirectory(entry.JsonPath, _trashFolder, Path.GetFileNameWithoutExtension(entry.JsonPath),
            folder => _writeTrashMetadata(folder, DateTimeOffset.UtcNow));
    }

    public void Restore(PageCatalogEntry entry)
    {
        ValidateEntry(entry);
        if (!entry.IsTrash)
            throw new ArgumentException("The page is not in the trash.", nameof(entry));
        var source = Path.GetDirectoryName(entry.JsonPath)!;
        var files = Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !IsTrashMetadataFile(path)).ToList();
        if (files.Count == 0)
            throw new InvalidDataException("Trash page folder is empty.");
        var names = files.Select(FileName).ToList();
        if (names.Any(name => File.Exists(Path.Combine(_folder, name)) || Directory.Exists(Path.Combine(_folder, name))))
            throw new IOException("A page with the same basename already exists; refusing to overwrite it.");

        var staging = Path.Combine(_folder, ".restore-" + Guid.NewGuid().ToString("N"));
        Directory.Move(source, staging);
        var moved = new List<string>();
        try
        {
            foreach (var name in names)
            {
                var destination = Path.Combine(_folder, name);
                File.Move(Path.Combine(staging, name), destination);
                moved.Add(name);
            }
            var metadata = TrashMetadataPath(staging);
            if (File.Exists(metadata))
                File.Delete(metadata);
            Directory.Delete(staging);
        }
        catch
        {
            foreach (var name in moved.AsEnumerable().Reverse())
            {
                var destination = Path.Combine(_folder, name);
                if (File.Exists(destination))
                    File.Move(destination, Path.Combine(staging, name));
            }
            Directory.Move(staging, source);
            throw;
        }
    }

    /// <summary>Manual deletion requires confirmation; scheduled deletion requires an enabled retention policy.</summary>
    public PageDeleteResult DeletePermanently(PageCatalogEntry entry)
    {
        ValidateEntry(entry);
        if (!entry.IsTrash)
            throw new ArgumentException("Only trash pages can be permanently deleted.", nameof(entry));
        var source = Path.GetDirectoryName(entry.JsonPath)!;
        var failures = new List<MemoStoreError>();
        var files = new List<string>();
        try
        {
            // A lexical path check alone does not confine access through junctions/symbolic links.
            foreach (var directory in new[] { _folder, _trashFolder, source })
                RejectLink(new DirectoryInfo(directory));
            files = Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly)
                .Where(path => IsPageFile(path, entry.JsonPath) || IsTrashMetadataFile(path)).ToList();
            foreach (var file in files)
                RejectLink(new FileInfo(file));
            if (new MemoStore(source).Read(entry.JsonPath).Id != entry.Document.Id)
                throw new InvalidDataException("The trash page changed; refresh the list and try again.");

            // Keep both the JSON and retention timestamp for retry if any ordinary companion could not be removed.
            // The timestamp itself is removed only after the JSON was successfully removed.
            foreach (var file in files.Where(path => !string.Equals(path, entry.JsonPath, StringComparison.OrdinalIgnoreCase) && !IsTrashMetadataFile(path)))
                Delete(file);
            if (failures.Count == 0)
                Delete(entry.JsonPath);
            if (failures.Count == 0)
                foreach (var file in files.Where(IsTrashMetadataFile))
                    Delete(file);
            if (failures.Count == 0 && !Directory.EnumerateFileSystemEntries(source).Any())
            {
                // Never recurse into an unknown file or subdirectory. Report the directory itself
                // if cleanup fails after all page files have already been removed.
                try
                {
                    DeleteDirectoryWithTransientRetry(source);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    failures.Add(new MemoStoreError(source, error));
                }
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            failures.Add(new MemoStoreError(entry.JsonPath, error));
        }
        // File.Exists also hides access errors, so retain every failed path in the report.
        var remaining = files.Where(File.Exists).Concat(failures.Select(f => f.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new PageDeleteResult(entry, remaining, failures.AsReadOnly());

        void Delete(string path)
        {
            try
            {
                foreach (var directory in new[] { _folder, _trashFolder, source })
                    RejectLink(new DirectoryInfo(directory));
                RejectLink(new FileInfo(path));
                File.Delete(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                failures.Add(new MemoStoreError(path, error));
            }
        }
    }

    private static void RejectLink(FileSystemInfo entry)
    {
        if (entry.LinkTarget is not null)
            throw new IOException("Refusing to permanently delete through a link: " + entry.FullName);
    }

    internal bool EnsureTrashedUtc(PageCatalogEntry entry, DateTimeOffset timestamp)
    {
        ValidateEntry(entry);
        if (!entry.IsTrash)
            throw new ArgumentException("Only trash pages have a trash timestamp.", nameof(entry));
        var folder = Path.GetDirectoryName(entry.JsonPath)!;
        if (ReadTrashedUtc(folder).HasValue)
            return false;
        WriteTrashedUtc(folder, timestamp);
        return true;
    }

    private static DateTimeOffset? ReadTrashedUtc(string folder)
    {
        var path = TrashMetadataPath(folder);
        if (!File.Exists(path))
            return null;
        try
        {
            var metadata = JsonSerializer.Deserialize<TrashMetadata>(File.ReadAllText(path));
            if (metadata?.TrashedUtc is null || metadata.TrashedUtc.Value.Offset != TimeSpan.Zero)
                throw new InvalidDataException("The trash timestamp is invalid.");
            return metadata.TrashedUtc.Value;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("The trash timestamp is invalid.", error);
        }
    }

    private static void WriteTrashedUtc(string folder, DateTimeOffset timestamp)
    {
        if (timestamp.Offset != TimeSpan.Zero)
            timestamp = timestamp.ToUniversalTime();
        var path = TrashMetadataPath(folder);
        File.WriteAllText(path, JsonSerializer.Serialize(new TrashMetadata(timestamp)));
    }

    private static string TrashMetadataPath(string folder) => Path.Combine(folder, TrashedAtName);
    private static bool IsTrashMetadataFile(string path) => string.Equals(Path.GetFileName(path), TrashedAtName, StringComparison.Ordinal);
    private sealed record TrashMetadata([property: JsonPropertyName("trashedUtc")] DateTimeOffset? TrashedUtc);

    private static bool IsPageFile(string path, string jsonPath)
    {
        var stem = Path.GetFileNameWithoutExtension(jsonPath);
        var name = Path.GetFileName(path);
        foreach (var suffix in new[] { ".json", ".png", ".json.bak", ".json.v1.bak" })
        {
            var ownedName = stem + suffix;
            if (string.Equals(name, ownedName, StringComparison.OrdinalIgnoreCase))
                return true;
            // MemoStore's interrupted atomic writes use <owned name>.<32-digit GUID>.tmp.
            if (name.Length == ownedName.Length + 37 && name.StartsWith(ownedName + ".", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                var token = name.AsSpan(ownedName.Length + 1, name.Length - ownedName.Length - 5);
                if (Guid.TryParseExact(token, "N", out _))
                    return true;
            }
        }
        return false;
    }

    private IEnumerable<string> EnumerateTrashJsonPaths()
    {
        if (!Directory.Exists(_trashFolder))
            return [];
        return Directory.EnumerateDirectories(_trashFolder, "*", SearchOption.TopDirectoryOnly)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly));
    }

    private void MoveIntoIsolatedDirectory(string jsonPath, string parent, string preferredName, Action<string>? initializeIsolation = null)
    {
        var sourceDirectory = Path.GetDirectoryName(jsonPath)!;
        var baseName = Path.GetFileNameWithoutExtension(jsonPath);
        var files = Directory.EnumerateFiles(sourceDirectory, baseName + ".*", SearchOption.TopDirectoryOnly).ToList();
        if (!files.Contains(jsonPath, StringComparer.OrdinalIgnoreCase))
            throw new FileNotFoundException("The page JSON does not exist.", jsonPath);
        Directory.CreateDirectory(parent);
        var isolation = Path.Combine(parent, ".moving-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolation);
        var moved = new List<string>();
        try
        {
            initializeIsolation?.Invoke(isolation);
            foreach (var file in files)
            {
                var name = FileName(file);
                File.Move(file, Path.Combine(isolation, name));
                moved.Add(name);
            }
            var destination = AvailableDirectory(parent, preferredName);
            MoveDirectoryWithTransientRetry(isolation, destination);
        }
        catch
        {
            foreach (var name in moved.AsEnumerable().Reverse())
            {
                var isolated = Path.Combine(isolation, name);
                if (File.Exists(isolated))
                    File.Move(isolated, Path.Combine(sourceDirectory, name));
            }
            if (Directory.Exists(isolation))
            {
                var metadata = TrashMetadataPath(isolation);
                if (File.Exists(metadata))
                    File.Delete(metadata);
                DeleteDirectoryWithTransientRetry(isolation);
            }
            throw;
        }
    }

    // The final rename can transiently fail when another process has the new directory open.
    // Retrying only that operation leaves the already-isolated page files untouched.
    private static void MoveDirectoryWithTransientRetry(string source, string destination)
    {
        const int Attempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (Exception error) when (attempt < Attempts && IsTransientDirectoryLock(error))
            {
                Thread.Sleep(attempt * 25);
            }
        }
    }

    private static void DeleteDirectoryWithTransientRetry(string path)
    {
        const int Attempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(path);
                return;
            }
            catch (Exception error) when (attempt < Attempts && IsTransientDirectoryLock(error))
            {
                Thread.Sleep(attempt * 25);
            }
        }
    }

    private static bool IsTransientDirectoryLock(Exception error) => error.HResult switch
    {
        unchecked((int)0x80070005) when error is UnauthorizedAccessException or IOException => true, // ERROR_ACCESS_DENIED
        unchecked((int)0x80070020) or unchecked((int)0x80070021) when error is IOException => true, // sharing or lock violation
        _ => false,
    };

    private static string AvailableDirectory(string parent, string preferredName)
    {
        var candidate = Path.Combine(parent, preferredName);
        if (!Directory.Exists(candidate) && !File.Exists(candidate))
            return candidate;
        return Path.Combine(parent, preferredName + "-" + Guid.NewGuid().ToString("N"));
    }

    private static string FileName(string path) => Path.GetFileName(path)
        ?? throw new InvalidDataException("A page file has no name.");

    private void ValidateEntry(PageCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var path = Path.GetFullPath(entry.JsonPath);
        if (!string.Equals(path, entry.JsonPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The entry has an invalid JSON path.", nameof(entry));
        var parent = Path.GetDirectoryName(path)!;
        var valid = entry.IsTrash
            ? string.Equals(Path.GetDirectoryName(parent), _trashFolder, StringComparison.OrdinalIgnoreCase)
            : string.Equals(parent, _folder, StringComparison.OrdinalIgnoreCase);
        if (!valid)
            throw new ArgumentException("The entry does not belong to this catalog.", nameof(entry));
    }
}

public sealed record PageCatalogEntry(string JsonPath, MemoDocument Document, bool IsTrash, DateTimeOffset? TrashedUtc = null);
public sealed record PageDeleteResult(PageCatalogEntry Entry, IReadOnlyList<string> RemainingFiles, IReadOnlyList<MemoStoreError> Errors)
{
    public bool Succeeded => Errors.Count == 0 && RemainingFiles.Count == 0;
}
