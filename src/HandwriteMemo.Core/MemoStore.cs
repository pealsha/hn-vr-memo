using System.Text.Json;

namespace HandwriteMemo.Core;

/// <summary>JSON is authoritative. Reads convert v1 in memory; saves preserve v1 bytes before replacement.</summary>
public sealed class MemoStore
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    // Keep names stable even if the PC time zone changes while a page is open.
    private readonly Dictionary<Guid, MemoSavedFiles> _pathsByDocumentId = [];
    public string Folder { get; }

    public MemoStore(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(Folder);
    }

    public MemoSavedFiles Save(MemoDocument document, Action<Stream> writePng)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writePng);
        var paths = PathsFor(document);
        WriteJsonAtomically(paths.JsonPath, document);
        WritePngAtomically(paths.PngPath, writePng);
        return paths;
    }

    public MemoDocument Read(string jsonPath)
    {
        var path = EnsureOwnedJsonPath(jsonPath);
        var (document, _) = ParseBytes(File.ReadAllBytes(path));
        _pathsByDocumentId[document.Id] = new(path, Path.ChangeExtension(path, ".png"));
        return document;
    }

    /// <summary>Rebuild absent/stale previews, leaving every source JSON untouched, including v1.</summary>
    public IReadOnlyList<MemoStoreError> RepairPreviews(Action<MemoDocument, Stream> writePng)
    {
        ArgumentNullException.ThrowIfNull(writePng);
        var errors = new List<MemoStoreError>();
        foreach (var path in Directory.EnumerateFiles(Folder, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var document = Read(path);
                var png = Path.ChangeExtension(path, ".png");
                if (!File.Exists(png) || File.GetLastWriteTimeUtc(png) < File.GetLastWriteTimeUtc(path))
                    WritePngAtomically(png, stream => writePng(document, stream));
            }
            catch (Exception error)
            {
                errors.Add(new(path, error));
            }
        }
        return errors;
    }

    private MemoSavedFiles PathsFor(MemoDocument document)
    {
        if (_pathsByDocumentId.TryGetValue(document.Id, out var known))
            return known;
        var suffix = $"-{document.Id:N}.json";
        var existing = Directory.EnumerateFiles(Folder, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(path => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        var json = existing ?? Path.Combine(Folder, $"{document.CreatedUtc.ToLocalTime():yyyyMMdd-HHmmssfff}-{document.Id:N}.json");
        var paths = new MemoSavedFiles(json, Path.ChangeExtension(json, ".png"));
        _pathsByDocumentId.Add(document.Id, paths);
        return paths;
    }

    private void WriteJsonAtomically(string path, MemoDocument document)
    {
        if (File.Exists(path))
        {
            // Validate again at save time, including files changed since Read.
            var original = File.ReadAllBytes(path);
            var (existing, version) = ParseBytes(original);
            if (existing.Id != document.Id)
                throw new InvalidDataException("Existing JSON belongs to another page.");
            if (version == 1)
                PreserveV1(path, original);
            File.Copy(path, path + ".bak", overwrite: true);
        }
        var temp = TempPath(path);
        try
        {
            WriteFlushedBytes(temp, JsonSerializer.SerializeToUtf8Bytes(MemoWire.From(document), s_json));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void PreserveV1(string path, byte[] original)
    {
        var backup = path + ".v1.bak";
        if (File.Exists(backup))
        {
            if (!File.ReadAllBytes(backup).SequenceEqual(original))
                throw new IOException("Existing v1 backup differs; refusing to replace the original memo.");
            return;
        }
        // A partial copy must never become the permanent backup. Do not overwrite an existing backup.
        var temp = TempPath(backup);
        try
        {
            WriteFlushedBytes(temp, original);
            File.Move(temp, backup, overwrite: false);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void WriteFlushedBytes(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static void WritePngAtomically(string path, Action<Stream> writer)
    {
        var temp = TempPath(path);
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                writer(stream);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static (MemoDocument Document, int Version) ParseBytes(byte[] bytes)
    {
        try
        {
            using var json = JsonDocument.Parse(bytes);
            var root = json.RootElement;
            RequireObject(root, "root");
            var versionField = Required(root, "formatVersion");
            if (versionField.ValueKind != JsonValueKind.Number || !versionField.TryGetInt32(out int version))
                throw new InvalidDataException("formatVersion must be an integer.");
            if (version is not 1 and not 2)
                throw new NotSupportedException($"Memo format version {version} is not supported.");
            return (ParseDocument(root, version), version);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Invalid memo JSON.", error);
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException("Invalid memo data.", error);
        }
    }

    private static MemoDocument ParseDocument(JsonElement root, int version)
    {
        var size = new BoardSize(Number(root, "boardWidthMetres"), Number(root, "boardHeightMetres"));
        size.Validate();
        bool pinned = false;
        string? title = null;
        if (version == 2)
        {
            var pin = Required(root, "isPinned");
            if (pin.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                throw new InvalidDataException("isPinned must be boolean.");
            pinned = pin.GetBoolean();
            if (root.TryGetProperty("title", out var name) && name.ValueKind != JsonValueKind.Null)
                title = name.ValueKind == JsonValueKind.String ? name.GetString() : throw new InvalidDataException("title must be a string or null.");
            // worldNames was retired in ADR 0005. Ignore even its type/value in older v2 files.
        }
        var strokes = new List<MemoStroke>();
        foreach (var stroke in Array(root, "strokes").EnumerateArray())
        {
            RequireObject(stroke, "stroke");
            var points = new List<MemoPoint>();
            foreach (var point in Array(stroke, "points").EnumerateArray())
            {
                RequireObject(point, "point");
                float x = Number(point, version == 1 ? "u" : "xMetres");
                float y = Number(point, version == 1 ? "v" : "yMetres");
                if (version == 1)
                {
                    if (x is < 0 or > 1 || y is < 0 or > 1)
                        throw new InvalidDataException("v1 coordinates must be normalized to 0..1.");
                    x *= size.WidthMetres;
                    y *= size.HeightMetres;
                }
                var elapsed = Required(point, "elapsedSeconds");
                if (elapsed.ValueKind != JsonValueKind.Number || !elapsed.TryGetDouble(out double seconds))
                    throw new InvalidDataException("elapsedSeconds must be a number.");
                points.Add(new MemoPoint(x, y, seconds));
            }
            // Legacy width is 3.2 pixels on a 1024-pixel-wide PNG, even for a non-default board.
            var color = version == 1 ? MemoStroke.DefaultColor : Text(stroke, "color");
            var width = version == 1 ? 3.2f * size.WidthMetres / (float)BoardMetrics.ReferenceWidthPixels : Number(stroke, "widthMetres");
            strokes.Add(new MemoStroke(Timestamp(stroke, "startedUtc"), points, color, width));
        }
        if (!Guid.TryParse(Text(root, "id"), out var id))
            throw new InvalidDataException("Invalid page id.");
        return new MemoDocument(2, id, Timestamp(root, "createdUtc"), Timestamp(root, "updatedUtc"), size, strokes, pinned, title);
    }

    // Read required fields explicitly: missing numeric/bool fields must not silently become zero/false.
    private static JsonElement Required(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value : throw new InvalidDataException($"Memo field '{name}' is missing or null.");

    private static void RequireObject(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Memo {name} must be an object.");
    }

    private static JsonElement Array(JsonElement parent, string name)
    {
        var value = Required(parent, name);
        return value.ValueKind == JsonValueKind.Array ? value : throw new InvalidDataException($"'{name}' must be an array.");
    }

    private static string Text(JsonElement parent, string name)
    {
        var value = Required(parent, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new InvalidDataException($"'{name}' must be a string.");
    }

    private static float Number(JsonElement parent, string name)
    {
        var value = Required(parent, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out float number) && float.IsFinite(number)
            ? number : throw new InvalidDataException($"'{name}' must be a finite number.");
    }

    private static DateTimeOffset Timestamp(JsonElement parent, string name)
    {
        var value = Required(parent, name);
        return value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var time)
            ? time : throw new InvalidDataException($"'{name}' must be an ISO 8601 timestamp.");
    }

    private string EnsureOwnedJsonPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(full), Folder, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(full), ".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Memo path must be a .json file directly inside this store.", nameof(path));
        return full;
    }

    private static string TempPath(string path) => path + "." + Guid.NewGuid().ToString("N") + ".tmp";
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class MemoWire
    {
        public int FormatVersion { get; set; }
        public Guid Id { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
        public float BoardWidthMetres { get; set; }
        public float BoardHeightMetres { get; set; }
        public bool IsPinned { get; set; }
        public string Title { get; set; } = "";
        public List<StrokeWire> Strokes { get; set; } = [];

        public static MemoWire From(MemoDocument document) => new()
        {
            FormatVersion = document.FormatVersion,
            Id = document.Id,
            CreatedUtc = document.CreatedUtc,
            UpdatedUtc = document.UpdatedUtc,
            BoardWidthMetres = document.BoardSize.WidthMetres,
            BoardHeightMetres = document.BoardSize.HeightMetres,
            IsPinned = document.IsPinned,
            Title = document.Title,
            Strokes = document.Strokes.Select(stroke => new StrokeWire
            {
                StartedUtc = stroke.StartedUtc,
                Color = stroke.Color,
                WidthMetres = stroke.WidthMetres,
                Points = stroke.Points.Select(point => new PointWire
                {
                    XMetres = point.XMetres,
                    YMetres = point.YMetres,
                    ElapsedSeconds = point.ElapsedSeconds,
                }).ToList(),
            }).ToList(),
        };
    }

    private sealed class StrokeWire
    {
        public DateTimeOffset StartedUtc { get; set; }
        public string Color { get; set; } = "";
        public float WidthMetres { get; set; }
        public List<PointWire> Points { get; set; } = [];
    }

    private sealed class PointWire
    {
        public float XMetres { get; set; }
        public float YMetres { get; set; }
        public double ElapsedSeconds { get; set; }
    }
}

public readonly record struct MemoSavedFiles(string JsonPath, string PngPath);
public sealed record MemoStoreError(string Path, Exception Error);
