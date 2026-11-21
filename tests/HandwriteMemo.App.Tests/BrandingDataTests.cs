using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class BrandingDataTests
{
    [TestMethod]
    public void LegacyData()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "task-040", "synthetic-" + Guid.NewGuid().ToString("N")));
        string documents = Path.Combine(root, "Documents"), local = Path.Combine(root, "LocalAppData");
        string pages = AppDataPaths.MemoFolder(documents), config = AppDataPaths.ConfigPath(local);
        Check(pages == Path.Combine(documents, "HandwriteMemo"), "document folder remains compatible");
        Check(config == Path.Combine(local, "HandwriteMemo", "settings.json"), "configuration path remains compatible");
        var original = new AppOptions { SaveFolder = pages, Handedness = HandSide.Left, BoardBrightness = .6f, TrashRetentionDays = 0 };
        original.Save(config);
        var old = DateTimeOffset.UtcNow.AddDays(-2);
        var stroke = new MemoStroke(old, [new(.05f, .05f, 0), new(.15f, .1f, 1)], "#182634", .0015f);
        var doc = new MemoDocument(2, Guid.NewGuid(), old, old.AddSeconds(2), BoardSize.Default, [stroke], true, "旧版の確認用メモ");
        var store = new MemoStore(pages);
        store.Save(doc, stream => MemoRenderer.WritePng(doc, stream));
        var before = Directory.GetFiles(pages).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        byte[] configBefore = File.ReadAllBytes(config);
        var loaded = AppOptions.Load(null, local);
        Check(loaded == original, "implicit loading finds existing settings without a manual config argument");
        var entries = new PageCatalog(loaded.SaveFolder).List();
        Check(entries.Single().Document.Id == doc.Id && entries.Single().Document.Title == doc.Title && entries.Single().Document.IsPinned,
            "old page and metadata remain visible");
        foreach (var entry in before)
            Check(entry.Value.SequenceEqual(File.ReadAllBytes(Path.Combine(pages, entry.Key!))), "loading leaves old bytes intact");
        Check(configBefore.SequenceEqual(File.ReadAllBytes(config)), "loading does not rewrite old configuration");
        var engine = new HandwriteEngine(new(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity));
        var session = new MemoSession(engine, loaded.SaveFolder, DateTimeOffset.UtcNow, _ => { });
        Check(session.Open(entries.Single(), 1) && engine.Strokes.Count == 1, "old ink opens in the actual session");
        Check(session.BeginDesktop(2), "old page accepts new input");
        session.DesktopPoint(new(.4f, .4f), true, 2.02, DrawingTool.Pen);
        session.DesktopPoint(new(.5f, .5f), true, 2.04, DrawingTool.Pen);
        session.DesktopPoint(null, false, 2.06, DrawingTool.Pen);
        Check(session.Save(2.1) && new PageCatalog(pages).List().Single().Document.Strokes.Count == 2, "new ink saves beside old ink in the old folder");
        string custom = Path.Combine(root, "custom pages");
        (loaded with { SaveFolder = custom }).Save(config);
        Check(AppOptions.Load(null, local).SaveFolder == custom, "user-selected path survives a restart");
        Check(AppOptions.Load(config, Path.Combine(root, "unused")).SaveFolder == custom, "explicit config retains precedence");
        var empty = AppOptions.Load(null, Path.Combine(root, "fresh"));
        Check(empty.SaveFolder == new AppOptions().SaveFolder, "fresh install keeps the same default folder");
        string logs = AppDataPaths.LogFolder(local), recovery = AppDataPaths.RecoveryFolder(local);
        Directory.CreateDirectory(recovery);
        File.WriteAllText(Path.Combine(recovery, "old.json"), "synthetic recovery sentinel");
        using (var log = new RunLog(logs))
            log.Event("HnMemo compatibility check");
        Check(logs == Path.Combine(local, "HandwriteMemo", "logs") && Directory.GetFiles(logs).Length == 1, "logs retain their location");
        Check(File.ReadAllText(Path.Combine(recovery, "old.json")) == "synthetic recovery sentinel", "recovery copies stay intact");
        Check(!Directory.Exists(Path.Combine(local, "HnMemo")) && !Directory.Exists(Path.Combine(documents, "HnMemo")), "no split or implicit migration");
        Console.WriteLine("PASS: old data/settings/custom path/logs/recovery retained; synthetic roots only; no migration or format change.");
    }
}
