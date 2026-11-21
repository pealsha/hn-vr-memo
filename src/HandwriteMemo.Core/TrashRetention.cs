namespace HandwriteMemo.Core;

/// <summary>Applies the selected lifetime to pages already in the application's trash.</summary>
public sealed class TrashRetention
{
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string> _log;

    public TrashRetention(Func<DateTimeOffset> clock, Action<string> log)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Initializes legacy trash timestamps and permanently removes expired pages. Returns the number removed.</summary>
    public int Run(PageCatalog catalog, int retentionDays)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (retentionDays < 0)
            throw new ArgumentOutOfRangeException(nameof(retentionDays));

        var now = _clock().ToUniversalTime();
        var deleted = 0;
        var entries = catalog.List(trash: true);
        foreach (var error in catalog.Errors)
            _log($"Trash retention could not list {error.Path}: {error.Error.Message}");
        foreach (var entry in entries)
        {
            DateTimeOffset trashed;
            try
            {
                if (entry.TrashedUtc is null)
                {
                    catalog.EnsureTrashedUtc(entry, now);
                    trashed = now;
                    _log($"Trash retention initialized legacy page {entry.Document.Id} at {now:O}.");
                }
                else
                    trashed = entry.TrashedUtc.Value;

                if (retentionDays == 0 || now < trashed.AddDays(retentionDays))
                    continue;
                var result = catalog.DeletePermanently(entry);
                if (result.Succeeded)
                {
                    deleted++;
                    _log($"Trash retention permanently deleted {entry.Document.Id} after {retentionDays} days.");
                }
                else
                    _log($"Trash retention could not delete {entry.Document.Id}: {string.Join("; ", result.Errors.Select(error => error.Error.Message))}");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                _log($"Trash retention could not process {entry.Document.Id}: {error.Message}");
            }
        }
        if (deleted > 0)
            _log($"Trash retention permanently deleted {deleted} page(s).");
        return deleted;
    }

    /// <summary>Returns a rounded-up count of days before deletion, or null when automatic deletion is disabled or untracked.</summary>
    public int? RemainingDays(PageCatalogEntry entry, int retentionDays)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (retentionDays < 0)
            throw new ArgumentOutOfRangeException(nameof(retentionDays));
        if (retentionDays == 0 || !entry.IsTrash || entry.TrashedUtc is null)
            return null;
        var remaining = entry.TrashedUtc.Value.AddDays(retentionDays) - _clock().ToUniversalTime();
        return Math.Max(0, (int)Math.Ceiling(remaining.TotalDays));
    }
}
