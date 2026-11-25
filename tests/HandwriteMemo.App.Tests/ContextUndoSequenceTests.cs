using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class ContextUndoSequenceTests
{
    [TestMethod]
    public void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "task-030"));
        var trace = new List<string>();
        try
        {
            foreach (int seed in new[] { 30, 20261022, 7301 })
                RunSequence(root, seed, trace);
        }
        finally
        {
            File.WriteAllLines(Path.Combine(root, "random-sequences.log"), trace);
        }
        Console.WriteLine("PASS: random input sequences: 3 fixed seeds x 60 actions; exact catalog IDs, shared page, ink history and contextual undo.");
    }

    private static void RunSequence(string root, int seed, List<string> trace)
    {
        var random = new Random(seed);
        string folder = Path.Combine(root, "sequence-" + seed + "-" + Guid.NewGuid().ToString("N"));
        var origin = DateTimeOffset.UtcNow;
        var documents = Enumerable.Range(0, 4).Select(i => new MemoDocument(MemoDocument.CurrentFormatVersion,
            Guid.NewGuid(), origin.AddMinutes(-i - 1), origin, BoardSize.Default,
            [new MemoStroke(origin.AddSeconds(-2), [new MemoPoint(.05f, .1f, 0), new MemoPoint(.2f, .15f, 1)], "#182634", .0015f)],
            isPinned: false, title: "Sequence " + i)).ToArray();
        var store = new MemoStore(folder);
        foreach (var document in documents)
            store.Save(document, stream => MemoRenderer.WritePng(document, stream));
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var page = new MemoSession(engine, folder, origin, _ =>
        {
        });
        double time = 0;
        double Now() => time += .05;
        page.Open(page.Catalog.List().Single(e => e.Document.Id == documents[0].Id), Now());
        page.Catalog.MoveToTrash(page.Catalog.List().Single(e => e.Document.Id == documents[3].Id));
        using var window = new DesktopWindow(engine, page, Now);
        window.Show();
        Application.DoEvents();

        // Model only the specified user-visible effects, independent of the window's private state.
        var normal = documents.Take(3).Select(d => d.Id).ToHashSet();
        var pending = new HashSet<Guid>();
        bool trashTab = false, selecting = false, busy = false;
        int undoInk = 0, redoInk = 0;
        void Tab(bool trash)
        {
            if (trashTab == trash)
                return;
            window.DispatchCommandKey(Keys.Control | Keys.Tab);
            trashTab = trash;
            selecting = false;
        }
        void Editor()
        {
            Tab(false);
            if (selecting)
            {
                window.SelectButton.PerformClick();
                selecting = false;
            }
            window.SelectPages([documents[0].Id]);
            window.Canvas.Focus();
        }
        void UndoMove()
        {
            if (busy)
                return;
            normal.UnionWith(pending);
            pending.Clear();
        }
        for (int step = 0; step < 60; step++)
        {
            int operation = random.Next(11);
            trace.Add(
                $"seed={seed} step={step} operation={operation} trash={trashTab} selecting={selecting} busy={busy} normal={normal.Count} pending={pending.Count} inkUndo={undoInk} inkRedo={redoInk}");
            switch (operation)
            {
                case 0: // Move a noncurrent page. The latest successful move replaces the previous one.
                    var candidates = documents.Skip(1).Where(d => normal.Contains(d.Id)).ToArray();
                    if (candidates.Length == 0)
                        break;
                    Tab(false);
                    Guid moved = candidates[random.Next(candidates.Length)].Id;
                    window.SelectPages([moved]);
                    window.RemoveButton.PerformClick();
                    if (!busy)
                    {
                        normal.Remove(moved);
                        pending.Clear();
                        pending.Add(moved);
                    }
                    break;
                case 1: // Catalog keyboard undo, including zero selection and busy input.
                    window.PagesList.Focus();
                    window.DispatchCommandKey(Keys.Control | Keys.Z);
                    UndoMove();
                    break;
                case 2: // The same undo through a real menu, with its enabled state checked first.
                    var command = ContextUndoTests.MenuCommand(window, "移動を元に戻す");
                    Require(command.Enabled == (pending.Count > 0 && !busy), seed, step, "menu enablement");
                    if (command.Enabled)
                    {
                        command.PerformClick();
                        UndoMove();
                    }
                    break;
                case 3: // A standalone restore removes that target from any pending move undo.
                    var trashed = documents.Where(d => !normal.Contains(d.Id)).ToArray();
                    if (trashed.Length == 0)
                        break;
                    Tab(true);
                    Guid restored = trashed[random.Next(trashed.Length)].Id;
                    window.SelectPages([restored]);
                    window.RestoreButton.PerformClick();
                    normal.Add(restored);
                    pending.Remove(restored);
                    break;
                case 4:
                    window.RefreshCatalog();
                    break;
                case 5:
                    window.SelectButton.PerformClick();
                    selecting = !selecting;
                    break;
                case 6:
                    Tab(!trashTab);
                    break;
                case 7: // Editing a page must not invalidate a pending trash move.
                    Editor();
                    if (busy)
                        break;
                    var paper = window.Canvas.PaperBounds;
                    int y = (int)(paper.Top + paper.Height * (.3 + random.NextDouble() * .3));
                    window.Canvas.BeginInput(new((int)(paper.Left + paper.Width * .3), y));
                    window.Canvas.MoveInput(new((int)(paper.Left + paper.Width * .6), y));
                    window.Canvas.EndInput();
                    undoInk++;
                    redoInk = 0;
                    break;
                case 8:
                    Editor();
                    window.DispatchCommandKey(Keys.Control | Keys.Z);
                    if (!busy && undoInk > 0)
                    {
                        undoInk--;
                        redoInk++;
                    }
                    break;
                case 9:
                    Editor();
                    window.DispatchCommandKey(Keys.Control | Keys.Y);
                    if (!busy && redoInk > 0)
                    {
                        undoInk++;
                        redoInk--;
                    }
                    break;
                case 10:
                    busy = !busy;
                    page.SetVrResizing(busy, Now());
                    window.RefreshCatalog();
                    break;
            }
            var actual = page.Catalog.List().Select(e => e.Document.Id).ToHashSet();
            var actualTrash = page.Catalog.List(true).Select(e => e.Document.Id).ToHashSet();
            Require(actual.SetEquals(normal) && actualTrash.SetEquals(documents.Select(d => d.Id).Except(normal)), seed, step, "exact page locations");
            Require(page.Id == documents[0].Id && engine.Strokes.Count == 1 + undoInk, seed, step, "shared page and ink");
            Require(window.CanUndoTrash == (pending.Count > 0 && !busy) && window.IsSelectionMode == selecting, seed, step, "context and selection state");
            Require(engine.CanUndo == (undoInk > 0) && engine.CanRedo == (redoInk > 0), seed, step, "independent ink undo/redo history");
        }
        page.SetVrResizing(false, Now());
        window.Hide();
        trace.Add($"PASS seed={seed}: 60 actions");
    }

    private static void Require(bool condition, int seed, int step, string invariant)
    {
        if (!condition)
            throw new InvalidOperationException($"Context undo random sequence seed={seed}, step={step}: {invariant}");
    }
}
