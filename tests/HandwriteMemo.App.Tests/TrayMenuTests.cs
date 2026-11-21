namespace HandwriteMemo.App;

[STATestClass]
public sealed class TrayMenuTests
{
    [TestMethod]
    public void Run()
    {
        var calls = new List<string>();
        using var menu = AppHost.MakeTrayMenu(() => calls.Add("desktop"), () => calls.Add("folder"), () => calls.Add("exit"));
        string[] labels = ["デスクトップ画面を開く", "保存フォルダを開く", "終了"];
        string[] actions = ["desktop", "folder", "exit"];
        if (menu.Items.Count != labels.Length || calls.Count != 0)
            throw new InvalidOperationException("Tray creation must add exactly three items without invoking any action.");

        for (int i = 0; i < labels.Length; i++)
        {
            if (menu.Items[i] is not ToolStripMenuItem item || item.Text != labels[i])
                throw new InvalidOperationException($"Unexpected tray menu item at position {i + 1}.");

            item.PerformClick();
            if (!calls.SequenceEqual(actions.Take(i + 1)))
                throw new InvalidOperationException($"Unexpected action for tray menu item: {item.Text}");
        }
    }
}
