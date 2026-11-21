using System.Diagnostics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class FolderTransferTests
{
    [TestMethod]
    public void ManagedLinks()
    {
        var root = NewRoot();
        var actual = Path.Combine(root, "actual");
        var redirected = Path.Combine(root, "redirected");
        Directory.CreateDirectory(actual);
        CreateJunction(redirected, actual);
        try
        {
            var source = Path.Combine(redirected, "source");
            var destination = Path.Combine(redirected, "destination");
            var saved = SavePage(source);

            var transfer = new MemoFolderTransfer();
            transfer.CopyAndVerify(source, destination);
            Check(transfer.RemoveOriginals().Count == 0 && !File.Exists(saved.JsonPath) &&
                File.Exists(Path.Combine(destination, Path.GetFileName(saved.JsonPath))),
                "A link above both memo folders must allow a successful transfer.");

            Rejects(() => new MemoFolderTransfer().CopyAndVerify(redirected, Path.Combine(root, "other")));
            var sourceTrash = Path.Combine(source, "Trash");
            var outside = Path.Combine(root, "outside");
            var outsidePage = SavePage(outside);
            CreateJunction(sourceTrash, outside);
            try
            {
                Rejects(() => new MemoFolderTransfer().CopyAndVerify(source, destination));
            }
            finally
            {
                Directory.Delete(sourceTrash);
            }

            Directory.CreateDirectory(sourceTrash);
            var pageLink = Path.Combine(sourceTrash, "linked-page");
            CreateJunction(pageLink, outside);
            try
            {
                Rejects(() => new MemoFolderTransfer().CopyAndVerify(source, destination));
                Check(File.Exists(outsidePage.JsonPath) && File.Exists(outsidePage.PngPath), "Linked pages must stay intact.");
            }
            finally
            {
                Directory.Delete(pageLink);
            }
        }
        finally
        {
            Directory.Delete(redirected);
        }
    }

    [TestMethod]
    public void ChangedFiles()
    {
        var root = NewRoot();
        var source = Path.Combine(root, "source");
        var destination = Path.Combine(root, "destination");
        var saved = SavePage(source);
        var transfer = new MemoFolderTransfer();
        transfer.CopyAndVerify(source, destination);

        byte[] altered = File.ReadAllBytes(saved.JsonPath);
        altered[0] ^= 1; // Preserve size so only the fingerprint can detect the edit.
        File.WriteAllBytes(saved.JsonPath, altered);
        File.Delete(Path.Combine(destination, Path.GetFileName(saved.PngPath)));

        var failures = transfer.RemoveOriginals();
        Check(failures.Count == 2 && File.ReadAllBytes(saved.JsonPath).SequenceEqual(altered) && File.Exists(saved.PngPath),
            "Keep the source when it was edited or its verified destination disappeared.");
    }

    private static void Rejects(Action action)
    {
        try
        {
            action();
        }
        catch (IOException error) when (error.Message.Contains("リンク", StringComparison.Ordinal))
        {
            return;
        }
        throw new InvalidOperationException("A link within the managed folders was accepted.");
    }

    private static string NewRoot() => Path.GetFullPath(Path.Combine("artifacts", "folder-transfer-" + Guid.NewGuid().ToString("N")));

    private static MemoSavedFiles SavePage(string path)
    {
        var now = DateTimeOffset.UtcNow;
        var document = new MemoDocument(MemoDocument.CurrentFormatVersion, Guid.NewGuid(), now, now, BoardSize.Default, []);
        return new MemoStore(path).Save(document, stream => MemoRenderer.WritePng(document, stream));
    }

    private static void CreateJunction(string path, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", path, target })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Check(process.ExitCode == 0, "Could not create test junction: " + output);
    }
}
