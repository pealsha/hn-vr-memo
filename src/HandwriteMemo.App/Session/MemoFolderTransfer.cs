using System.Security.Cryptography;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

// Copy and verify every page before the caller persists the new folder. Never overwrite a destination.
internal sealed class MemoFolderTransfer
{
    private sealed record CopyPlan(string Source, string Destination, bool IsTrash);
    private sealed record VerifiedCopy(CopyPlan File, long Length, byte[] SourceHash);

    private readonly List<VerifiedCopy> _copies = [];
    internal Action<string, string> CopyFile { get; init; } = (source, destination) => File.Copy(source, destination, overwrite: false);

    internal static string ValidateDestination(string source, string destination)
    {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        string sourcePrefix = Path.EndsInDirectorySeparator(source) ? source : source + Path.DirectorySeparatorChar;
        string destinationPrefix = Path.EndsInDirectorySeparator(destination) ? destination : destination + Path.DirectorySeparatorChar;
        if (source.Equals(destination, StringComparison.OrdinalIgnoreCase) ||
            destination.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("同じ場所や、その内側・外側のフォルダには変更できません。別の保存先を選んでください。");
        RejectLink(new DirectoryInfo(source));
        RejectLink(new DirectoryInfo(destination));
        Directory.CreateDirectory(destination);
        var probe = Path.Combine(destination, ".write-check-" + Guid.NewGuid().ToString("N"));
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
        {
        }
        return destination;
    }

    internal void CopyAndVerify(string source, string destination)
    {
        destination = ValidateDestination(source, destination);
        var filesToCopy = new List<CopyPlan>();
        var original = ReadAll(source);
        var target = ReadAll(destination);
        if (original.Select(e => e.Document.Id).Intersect(target.Select(e => e.Document.Id)).Any())
            throw new IOException("新しい保存先に同じページが既にあります。上書きせず中止しました。別のフォルダを選んでください。");
        foreach (var entry in original)
        {
            string sourceDirectory = Path.GetDirectoryName(entry.JsonPath)!;
            string targetDirectory = destination;
            if (entry.IsTrash)
            {
                if (Directory.EnumerateDirectories(sourceDirectory).Any())
                    throw new IOException("ゴミ箱に不明なサブフォルダがあります: " + sourceDirectory);
                targetDirectory = Path.Combine(destination, "Trash", Path.GetFileName(sourceDirectory));
                RejectLink(new DirectoryInfo(targetDirectory));
                if (Directory.Exists(targetDirectory) || File.Exists(targetDirectory))
                    targetDirectory += "-" + Guid.NewGuid().ToString("N");
            }
            string stem = Path.GetFileNameWithoutExtension(entry.JsonPath);
            var files = entry.IsTrash ? Directory.EnumerateFiles(sourceDirectory) : Directory.EnumerateFiles(sourceDirectory, stem + ".*");
            foreach (string file in files)
            {
                RejectLink(new FileInfo(file));
                var dest = Path.Combine(targetDirectory, Path.GetFileName(file));
                RejectLink(new FileInfo(dest));
                if (File.Exists(dest) || Directory.Exists(dest))
                    throw new IOException("同名のファイルがあり、移せません: " + dest);
                filesToCopy.Add(new(file, dest, entry.IsTrash));
            }
        }
        try
        {
            foreach (var file in filesToCopy)
            {
                RejectManagedFileLink(file.Source, file.IsTrash);
                RejectManagedFileLink(file.Destination, file.IsTrash);
                Directory.CreateDirectory(Path.GetDirectoryName(file.Destination)!);
                string temporary = file.Destination + ".transfer-" + Guid.NewGuid().ToString("N");
                try
                {
                    CopyFile(file.Source, temporary);
                    using var sourceFile = File.OpenRead(file.Source);
                    byte[] sourceHash = SHA256.HashData(sourceFile);
                    long length = sourceFile.Length;
                    if (!Matches(temporary, length, sourceHash))
                        throw new IOException("コピーの内容が一致しません: " + file.Source);
                    File.Move(temporary, file.Destination, overwrite: false);
                    _copies.Add(new(file, length, sourceHash));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    throw new IOException("移せなかったファイル: " + file.Source + "\n" + e.Message, e);
                }
                finally
                {
                    TryDelete(temporary);
                }
            }
        }
        catch
        {
            Rollback();
            throw;
        }
    }

    internal IReadOnlyList<string> RemoveOriginals()
    {
        var failures = new List<string>();
        foreach (var (file, length, sourceHash) in _copies)
        {
            try
            {
                RejectManagedFileLink(file.Source, file.IsTrash);
                RejectManagedFileLink(file.Destination, file.IsTrash);
                if (new FileInfo(file.Destination).Length != length || !Matches(file.Source, length, sourceHash))
                    throw new IOException("コピー後にファイルが変わったため元を残しました。");
                File.Delete(file.Source);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failures.Add(file.Source + "\n" + e.Message);
            }
        }
        // Leave unknown files and empty source directories alone; never recursively delete a user folder.
        return failures;
    }

    internal void Rollback()
    {
        foreach (var (file, _, _) in _copies)
        {
            try
            {
                RejectManagedFileLink(file.Destination, file.IsTrash);
                File.Delete(file.Destination);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        _copies.Clear();
    }

    private static IReadOnlyList<PageCatalogEntry> ReadAll(string folder)
    {
        var directory = new DirectoryInfo(folder);
        RejectLink(directory);
        foreach (var json in directory.EnumerateFiles("*.json"))
            RejectLink(json);
        var trash = new DirectoryInfo(Path.Combine(directory.FullName, "Trash"));
        RejectLink(trash);
        if (trash.Exists)
        {
            foreach (var page in trash.EnumerateDirectories())
            {
                RejectLink(page);
                foreach (var file in page.EnumerateFiles())
                    RejectLink(file);
            }
        }
        var catalog = new PageCatalog(folder);
        var pages = catalog.List().ToList();
        var errors = catalog.Errors.ToList();
        pages.AddRange(catalog.List(true));
        errors.AddRange(catalog.Errors);
        if (errors.Count > 0)
            throw new IOException("読めないページがあり、保存先を変更できません:\n" + string.Join("\n", errors.Select(e => e.Path + ": " + e.Error.Message)));
        if (pages.GroupBy(e => e.Document.Id).Any(g => g.Count() > 1))
            throw new IOException("同じページIDが複数あります: " + folder);
        return pages;
    }

    private static bool Matches(string path, long length, byte[] hash)
    {
        using var file = File.OpenRead(path);
        return file.Length == length && SHA256.HashData(file).SequenceEqual(hash);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void RejectManagedFileLink(string path, bool isTrash)
    {
        var file = new FileInfo(path);
        var parent = file.Directory!;
        if (isTrash)
        {
            var trash = parent.Parent!;
            RejectLink(trash.Parent!);
            RejectLink(trash);
        }
        RejectLink(parent);
        RejectLink(file);
    }

    private static void RejectLink(FileSystemInfo entry)
    {
        if (entry.LinkTarget is not null)
            throw new IOException("リンクを経由する保存先の変更はできません: " + entry.FullName);
    }
}
