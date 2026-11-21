using System.Drawing.Imaging;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class DesktopSettingsTests
{
    [TestMethod]
    public void AutoStartReservations()
    {
        using var fixture = new SettingsFixture();
        var config = fixture.Config;
        var settings = fixture.Settings;
        fixture.Unavailable = true;
        settings.RefreshAutoStart();
        Check(settings.PendingAutoChoice.Visible && settings.PendingAutoChoice.SelectedIndex == 0, "offline change has no initial reservation");
        settings.PendingAutoChoice.SelectedIndex = 1;
        Check(!fixture.AutoEnabled && settings.AutoStartSwitch.CheckState == CheckState.Indeterminate, "offline selection leaves actual value unknown");
        Check(AppOptions.Load(config).PendingAutoStart == new AutoStartRequest(true), "offline on persists through restarts");
        var reservationHint = Descendants(settings).OfType<Label>().Single(label => label.Text == settings.PendingAutoHint);
        settings.Controls.OfType<Panel>().Single(panel => panel.AutoScroll).ScrollControlIntoView(reservationHint);
        Capture(settings, fixture.Root, "settings-autostart-pending");
        settings.PendingAutoChoice.SelectedIndex = 2;
        Check(AppOptions.Load(config).PendingAutoStart == new AutoStartRequest(false), "offline off replaces the earlier choice");
        settings.PendingAutoChoice.SelectedIndex = 0;
        Check(AppOptions.Load(config).PendingAutoStart is null, "cancel removes the request");
        settings.PendingAutoChoice.SelectedIndex = 2;
        fixture.Unavailable = false;
        settings.RetryAutoButton.PerformClick();
        Check(AppOptions.Load(config).PendingAutoStart is null && !settings.AutoStartSwitch.Checked, "opening the connection applies the queued off");

        fixture.Unavailable = true;
        settings.RefreshAutoStart();
        settings.PendingAutoChoice.SelectedIndex = 1;
        fixture.Unavailable = false;
        fixture.Reject = true;
        settings.RetryAutoButton.PerformClick();
        Check(settings.RetryPendingAutoButton.Visible && settings.PendingAutoHint.Contains("反映できません"), "registration failure offers retry");
        Check(AppOptions.Load(config).PendingAutoStart == new AutoStartRequest(true, true), "failed change is saved without reporting on");
        fixture.Reject = false;
        settings.RefreshAutoStart();
        Check(!fixture.AutoEnabled, "status refresh does not replay a failed change");
        settings.RetryPendingAutoButton.PerformClick();
        Check(settings.AutoStartSwitch.Checked && !settings.PendingAutoChoice.Visible && AppOptions.Load(config).PendingAutoStart is null, "explicit retry completes");

        settings.Close();
        using var log = new RunLog(Path.Combine(fixture.Run, "reservation-logs"));
        AppSettingsService reloadedSettings = null!;
        AutoStartCoordinator Reload()
        {
            reloadedSettings = new AppSettingsService(AppOptions.Load(config), log.Event, config);
            return new AutoStartCoordinator(reloadedSettings, log.Event);
        }

        var restarted = Reload();
        Check(restarted.ChangeAutoStartRequest(false), "reserve off with no settings window");
        restarted = Reload();
        int nativeCalls = 0;
        bool Configure(string command)
        {
            nativeCalls++;
            return fixture.Auto(command);
        }
        using (File.Open(config, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(!restarted.ApplyPendingAutoStart(Configure) && nativeCalls == 0 && fixture.AutoEnabled, "failure to persist the attempt prevents a SteamVR write");
        }
        FileStream? lockedConfig = null;
        try
        {
            Check(restarted.ApplyPendingAutoStart(command =>
            {
                lockedConfig = File.Open(config, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Configure(command);
            }), "apply can succeed even when acknowledging the change fails");
        }
        finally
        {
            lockedConfig?.Dispose();
        }
        Check(!fixture.AutoEnabled && nativeCalls == 1 && AppOptions.Load(config).PendingAutoStart == new AutoStartRequest(false, true),
            "locked acknowledgment preserves the attempt state");
        fixture.Auto("enable");
        restarted = Reload();
        Check(!restarted.ApplyPendingAutoStart(Configure) && fixture.AutoEnabled && nativeCalls == 1, "restart leaves a newer SteamVR change intact");
        Check(restarted.ChangeAutoStartRequest(null), "clear interrupted request");

        var random = new Random(50427);
        AutoStartRequest? expected = null;
        for (int i = 0; i < 120; i++)
        {
            switch (random.Next(7))
            {
                case 0 or 1:
                    expected = new AutoStartRequest(random.Next(2) == 1);
                    Check(restarted.ChangeAutoStartRequest(expected.Enabled), "reserve");
                    break;
                case 2:
                    expected = null;
                    Check(restarted.ChangeAutoStartRequest(null), "cancel");
                    break;
                case 3:
                    restarted = Reload();
                    break;
                case 4:
                    fixture.Auto(random.Next(2) == 1 ? "enable" : "disable");
                    break;
                default:
                    bool retry = random.Next(2) == 1;
                    fixture.Reject = random.Next(2) == 1;
                    int before = nativeCalls;
                    bool attempt = expected is not null && (retry || !expected.Attempted);
                    bool succeeds = attempt && (!expected!.Enabled || !fixture.Reject);
                    Check(restarted.ApplyPendingAutoStart(Configure, retry) == succeeds, "application outcome follows the pending choice and SteamVR feedback");
                    Check(nativeCalls == before + (attempt ? 1 : 0), "only current requests may change SteamVR");
                    if (succeeds)
                    {
                        Check(fixture.AutoEnabled == expected!.Enabled, "applied value matches the request");
                        expected = null;
                    }
                    else if (attempt)
                        expected = expected! with
                        {
                            Attempted = true
                        };
                    fixture.Reject = false;
                    break;
            }
            Check(reloadedSettings.Current.PendingAutoStart == expected && AppOptions.Load(config).PendingAutoStart == expected, "memory and disk agree");
        }
    }

    [TestMethod]
    public void ConfigValidation()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "task-031"));
        string run = Path.Combine(root, "synthetic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        string config = Path.Combine(run, "settings.json");
        File.WriteAllText(config, "{}");
        Check(AppOptions.Load(config).TrashRetentionDays == 30, "old config defaults to thirty days");
        foreach (int invalid in new[] { -1, 2, 365 })
        {
            File.WriteAllText(config, "{\"TrashRetentionDays\":" + invalid + "}");
            try
            {
                AppOptions.Load(config);
                throw new InvalidOperationException("Invalid retention accepted");
            }
            catch (ArgumentException)
            {
            }
        }

        foreach (string invalid in new[] { "0.005", "0.301" })
        {
            File.WriteAllText(config, "{\"GrabRadiusMeters\":" + invalid + "}");
            try
            {
                AppOptions.Load(config);
                throw new InvalidOperationException("Out-of-range grab radius accepted: " + invalid);
            }
            catch (ArgumentException)
            {
            }
        }
    }

    [TestMethod]
    public void SettingsControls()
    {
        using var fixture = new SettingsFixture();
        var (root, config, initial, app, settings) = (fixture.Root, fixture.Config, fixture.Initial, fixture.App, fixture.Settings);
        Check(settings.Visible && settings.RetentionChoice.SelectedIndex == 3, "settings entry opens with persisted values");
        Capture(settings, root, "settings-default");
        foreach (int index in new[] { 0, 1, 2, 4, 3 })
        {
            settings.RetentionChoice.SelectedIndex = index;
            Check(AppOptions.Load(config).TrashRetentionDays == AppOptions.TrashRetentionChoices[index], "immediate retention save");
            Capture(settings, root, "settings-retention-" + AppOptions.TrashRetentionChoices[index]);
        }
        settings.HandChoice.SelectedIndex = 1;
        settings.BrightnessChoice.SelectedIndex = 2;
        settings.GestureChoice.SelectedIndex = 1;
        var saved = AppOptions.Load(config);
        Check(saved.Handedness == HandSide.Left && saved.BoardBrightness == .6f && saved.GestureMode == ToggleGestureMode.Disabled &&
            saved.RightIndexTipOffset == initial.RightIndexTipOffset, "VR settings and calibration roundtrip");
        Capture(settings, root, "settings-vr-left-dark-disabled");
        app.ChangeBrightness(.8f);
        app.ChangeHandedness(HandSide.Right);
        app.ChangeGestureMode(ToggleGestureMode.WristButton);
        Check(settings.HandChoice.SelectedIndex == 0 && settings.BrightnessChoice.SelectedIndex == 1 && settings.GestureChoice.SelectedIndex == 0,
            "shared VR changes appear on desktop");
        Capture(settings, root, "settings-vr-shared-update");
        using (File.Open(config, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            settings.BrightnessChoice.SelectedIndex = 0;
            Check(app.Current.BoardBrightness == .8f && settings.BrightnessChoice.SelectedIndex == 1 && settings.ErrorText.Length > 0,
                "failed settings save retains runtime and control state");
            Capture(settings, root, "settings-save-error");
        }
        settings.BrightnessChoice.SelectedIndex = 0;
        Check(settings.ErrorText.Length == 0 && AppOptions.Load(config).BoardBrightness == 1, "settings retry recovers");
        Click(settings.AutoStartSwitch);
        Check(fixture.AutoEnabled && settings.AutoStartSwitch.Checked, "enable reflects actual registration");
        Capture(settings, root, "settings-autostart-on");
        Click(settings.AutoStartSwitch);
        Check(!fixture.AutoEnabled && !settings.AutoStartSwitch.Checked, "disable reflects actual registration");
        fixture.Reject = true;
        Click(settings.AutoStartSwitch);
        Check(!fixture.AutoEnabled && !settings.AutoStartSwitch.Checked && settings.AutoStartErrorText.Contains("非ASCII"), "rejected enable re-queries actual state");
        Capture(settings, root, "settings-autostart-rejected");
        fixture.Unavailable = true;
        settings.RefreshAutoStart();
        Check(settings.RetryAutoButton.Visible, "re-check is offered while the state is unknown");
        Check(!settings.AutoStartSwitch.Enabled && settings.AutoStartSwitch.CheckState == CheckState.Indeterminate && settings.AutoStartErrorText.Contains("SteamVR"),
            "unavailable registration is unknown instead of a false off state");
        Capture(settings, root, "settings-steamvr-unavailable");
        Size normalSize = settings.Size;
        settings.Size = settings.MinimumSize;
        Application.DoEvents();
        var autoLabel = Descendants(settings).OfType<Label>().Single(label => label.Text == "SteamVR と一緒に起動");
        int CentreY(Control control) => control.PointToScreen(new Point(0, control.ClientSize.Height / 2)).Y;
        Check(Math.Abs(CentreY(autoLabel) - CentreY(settings.AutoStartSwitch)) <= 1 &&
            Math.Abs(CentreY(autoLabel) - CentreY(settings.RetryAutoButton)) <= 1, "label, switch and retry share the same vertical centre at minimum width");
        var autoError = Descendants(settings).OfType<Label>().Single(label => label.Text == settings.AutoStartErrorText);
        var pendingLabel = Descendants(settings).OfType<Label>().Single(label => label.Text == "自動起動の変更を予約");
        var pendingRow = settings.PendingAutoChoice.Parent!;
        int TextHeight(Label label) => TextRenderer.MeasureText(label.Text, label.Font).Height;
        int parentGap = autoError.PointToScreen(Point.Empty).Y - (CentreY(autoLabel) + TextHeight(autoLabel) / 2);
        int reservationGap = CentreY(pendingLabel) - TextHeight(pendingLabel) / 2 -
            autoError.PointToScreen(new Point(0, autoError.ClientSize.Height)).Y;
        Check(autoError.Visible && pendingLabel.Visible && pendingRow.Visible, "Offline registration shows both the explanation and reservation");
        Check(parentGap >= 0 && reservationGap >= parentGap + 8,
            $"The offline explanation is closer to its parent (gaps: {parentGap}px and {reservationGap}px)");
        Check(Math.Abs(CentreY(pendingLabel) - CentreY(pendingRow)) <= 1, "Reservation label and action row share the same vertical centre");
        Console.WriteLine($"Auto-start spacing at minimum width: {parentGap}px above, {reservationGap}px below");
        Capture(settings, root, "settings-steamvr-unavailable-minimum");
        fixture.Unavailable = false;
        fixture.Reject = false;
        settings.RetryAutoButton.PerformClick();
        Check(!settings.RetryAutoButton.Visible, "re-check hides once the state is known");
        Check(settings.AutoStartSwitch.Enabled && !settings.AutoStartSwitch.Checked && settings.AutoStartErrorText.Length == 0, "registration status retry");
        Check(Math.Abs(CentreY(autoLabel) - CentreY(settings.AutoStartSwitch)) <= 1, "label and switch stay centred after retry");
        settings.Size = normalSize;
        Descendants(settings).OfType<Button>().Single(b => b.Text == "保存先を開く").PerformClick();
        Check(fixture.Opened == initial.SaveFolder, "open uses current save path with injected shell boundary");
        Descendants(settings).OfType<Button>().Single(b => b.Text.StartsWith("保存先を変更")).PerformClick();
        Check(app.Current.SaveFolder == initial.SaveFolder, "folder picker cancel leaves configuration untouched");

    }

    // Windows routes the wheel to the control under the pointer, so these messages match scrolling past each choice.
    [TestMethod]
    public void WheelAndOpenList()
    {
        using var fixture = new SettingsFixture();
        var (config, settings) = (fixture.Config, fixture.Settings);
        settings.ClientSize = new(700, 320);
        Application.DoEvents();
        var page = settings.Controls.OfType<Panel>().Single();
        ComboBox[] choices = [settings.RetentionChoice, settings.HandChoice, settings.BrightnessChoice, settings.GestureChoice,
            settings.ActivationChoice, settings.HapticsChoice];
        string saved = File.ReadAllText(config);
        foreach (var choice in choices)
            foreach (int delta in new[] { -120, -120, 120, -120 })
            {
                var at = choice.PointToScreen(new Point(choice.Width / 2, choice.Height / 2));
                int before = choice.SelectedIndex;
                SendMessage(choice.Handle, WmMouseWheel, (IntPtr)(delta << 16), (IntPtr)((at.Y << 16) | (at.X & 0xFFFF)));
                Application.DoEvents();
                Check(choice.SelectedIndex == before, "wheel over a closed choice keeps " + choice.AccessibleName);
            }
        Check(File.ReadAllText(config) == saved, "wheel never saves a setting");
        Check(page.VerticalScroll.Value > 0, "wheel over a closed choice scrolls the page instead");

        var brightness = settings.BrightnessChoice;
        brightness.Focus();
        brightness.DroppedDown = true;
        Application.DoEvents();
        var info = new ComboBoxInfo { Size = Marshal.SizeOf<ComboBoxInfo>() };
        Check(GetComboBoxInfo(brightness.Handle, ref info) && info.List != IntPtr.Zero, "open list is available");
        int thirdItem = 2 * brightness.ItemHeight + brightness.ItemHeight / 2;
        SendMessage(info.List, WmMouseMove, IntPtr.Zero, (IntPtr)((thirdItem << 16) | 10));
        Application.DoEvents();
        Check(SendMessage(info.List, LbGetCurSel, IntPtr.Zero, IntPtr.Zero) == 2, "pointer highlights the third item");
        Check(fixture.App.ChangeHandedness(HandSide.Left) && settings.HandChoice.SelectedIndex == 1,
            "external changes refresh closed controls while the list is open");
        Check(fixture.App.ChangeBrightness(.8f) && AppOptions.Load(config).BoardBrightness == .8f,
            "external change to the open choice persists");
        Application.DoEvents();
        Check(SendMessage(info.List, LbGetCurSel, IntPtr.Zero, IntPtr.Zero) == 2, "external settings refresh keeps the highlight under the pointer");
        brightness.DroppedDown = false;
        Application.DoEvents();
        Check(brightness.SelectedIndex == 1, "closing the list shows the persisted brightness");
    }

    private const int WmMouseMove = 0x0200, WmMouseWheel = 0x020A, LbGetCurSel = 0x0188;
    [StructLayout(LayoutKind.Sequential)]
    private struct ComboBoxInfo
    {
        public int Size;
        public Rectangle Item, Button;
        public int State;
        public IntPtr Combo, Edit, List;
    }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetComboBoxInfo(IntPtr combo, ref ComboBoxInfo info);

    [TestMethod]
    public void RandomSynchronization()
    {
        using var fixture = new SettingsFixture();
        var (root, config, app, settings) = (fixture.Root, fixture.Config, fixture.App, fixture.Settings);
        // A deterministic random sequence checks control events, persistence, and external VR updates together.
        const int Seed = 31;
        var random = new Random(Seed);
        var sequence = new List<string>();
        try
        {
            for (int i = 0; i < 100; i++)
            {
                int action = random.Next(8);
                int? choice = action == 7 ? null : random.Next(action is 1 or 4 ? 3 : action == 3 ? 5 : 2);
                sequence.Add($"seed={Seed}; step={i}; action={action}; choice={choice}");
                switch (action)
                {
                    case 0:
                        settings.HandChoice.SelectedIndex = choice!.Value;
                        break;
                    case 1:
                        settings.BrightnessChoice.SelectedIndex = choice!.Value;
                        break;
                    case 2:
                        settings.GestureChoice.SelectedIndex = choice!.Value;
                        break;
                    case 3:
                        settings.RetentionChoice.SelectedIndex = choice!.Value;
                        break;
                    case 4:
                        app.ChangeBrightness(AppOptions.BoardBrightnessChoices[choice!.Value]);
                        break;
                    case 5:
                        app.ChangeHandedness((HandSide)choice!.Value);
                        break;
                    case 6:
                        settings.ActivationChoice.SelectedIndex = choice!.Value;
                        break;
                    default:
                        Click(settings.AutoStartSwitch);
                        break;
                }
                var disk = AppOptions.Load(config);
                Check(disk == app.Current && settings.HandChoice.SelectedIndex == (disk.Handedness == HandSide.Right ? 0 : 1) &&
                    AppOptions.BoardBrightnessChoices[settings.BrightnessChoice.SelectedIndex] == disk.BoardBrightness &&
                    (settings.GestureChoice.SelectedIndex == 0 ? ToggleGestureMode.WristButton : ToggleGestureMode.Disabled) == disk.GestureMode &&
                    settings.ActivationChoice.SelectedIndex == (int)disk.WristButtonActivationMode && fixture.AutoEnabled == settings.AutoStartSwitch.Checked,
                    "random settings sequence remains synchronized");
                sequence.Add($"{i}: action={action}; retention={disk.TrashRetentionDays}; hand={disk.Handedness}; brightness={disk.BoardBrightness}; gesture={disk.GestureMode}; auto={fixture.AutoEnabled}");
            }
        }
        finally
        {
            File.WriteAllLines(Path.Combine(root, "random-sequences.log"), sequence);
        }
        settings.RetentionChoice.SelectedIndex = 3;

    }

    [TestMethod]
    public void FolderTransfer()
    {
        using var fixture = new SettingsFixture();
        var (root, run, config, initial, app, engine, origin, page, desktop, settings) =
            (fixture.Root, fixture.Run, fixture.Config, fixture.Initial, fixture.App, fixture.Engine, fixture.Origin, fixture.Page, fixture.Desktop, fixture.Settings);
        double Now() => fixture.Now();
        using var browser = new VrPageBrowser(page);
        // Real page data, shared engine, backup and trash all travel together.
        Check(page.BeginDesktop(Now()), "begin synthetic ink");
        page.DesktopPoint(new(.2f, .3f), true, Now(), DrawingTool.Pen);
        page.DesktopPoint(new(.6f, .5f), true, Now(), DrawingTool.Pen);
        page.DesktopPoint(null, false, Now(), DrawingTool.Pen);
        Check(page.Save(Now()) && engine.Strokes.Count == 1, "shared ink saved");
        Guid id = page.Id;
        var store = new MemoStore(initial.SaveFolder);
        var trashDoc = new MemoDocument(2, Guid.NewGuid(), origin.AddDays(-3), origin, BoardSize.Default, [], title: "期限を確認するページ");
        store.Save(trashDoc, stream => MemoRenderer.WritePng(trashDoc, stream));
        page.Catalog.MoveToTrash(page.Catalog.List().Single(e => e.Document.Id == trashDoc.Id));
        desktop.RefreshCatalog();
        Descendants(desktop).OfType<RadioButton>().Single(b => b.Text == "ゴミ箱").Checked = true;
        Application.DoEvents();
        Capture(desktop, root, "trash-remaining-days");
        settings.RetentionChoice.SelectedIndex = 0;
        desktop.RefreshCatalog();
        Capture(desktop, root, "trash-no-retention");
        settings.RetentionChoice.SelectedIndex = 3;

        string destination = Path.Combine(run, "moved pages");
        using (var confirmation = new FolderChangeDialog(initial.SaveFolder, destination))
        {
            confirmation.Show();
            Application.DoEvents();
            Check(ReferenceEquals(confirmation.AcceptButton, confirmation.CancelChoiceButton) && ReferenceEquals(confirmation.CancelButton, confirmation.CancelChoiceButton),
                "folder change confirmation defaults to cancel");
            Capture(confirmation, root, "folder-change-confirmation");
            confirmation.CancelChoiceButton.PerformClick();
            Check(confirmation.Choice == FolderChangeChoice.Cancel, "confirmation cancel does not choose a move");
        }
        Check(!settings.ChangeFolder(destination, FolderChangeChoice.Cancel) && app.Current.SaveFolder == initial.SaveFolder && !Directory.Exists(destination), "cancel has no filesystem effects");
        string[] originals = Directory.GetFiles(initial.SaveFolder, "*", SearchOption.AllDirectories);
        var bytes = originals.ToDictionary(p => p, File.ReadAllBytes);
        int copies = 0;
        var failing = new MemoFolderTransfer
        {
            CopyFile = (a, b) =>
        {
            File.Copy(a, b);
            if (++copies == 2)
                throw new IOException("Synthetic second-copy failure: " + a);
        }
        };
        Check(!app.ChangeSaveFolder(page, destination, true, Now(), failing) && copies == 2, "partial copy failure is reported");
        Check(bytes.All(p => File.Exists(p.Key) && File.ReadAllBytes(p.Key).SequenceEqual(p.Value)) && app.Current.SaveFolder == initial.SaveFolder,
            "every original remains byte-identical after copy failure");
        Check(Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Length == 0, "owned partial copies rolled back");
        var extra = new MemoDocument(2, Guid.NewGuid(), origin.AddDays(-2), origin, BoardSize.Default, [], title: "新しい保存先のメモ");
        var targetStore = new MemoStore(destination);
        targetStore.Save(extra, stream => MemoRenderer.WritePng(extra, stream));
        using (File.Open(config, FileMode.Open, FileAccess.Read, FileShare.Read))
            Check(!app.ChangeSaveFolder(page, destination, true, Now()) && bytes.All(p => File.Exists(p.Key)) && page.Folder == initial.SaveFolder,
                "config save failure leaves original store and files intact");
        Check(settings.ChangeFolder(destination, FolderChangeChoice.Move), "move succeeds");
        Check(page.Id == id && engine.Strokes.Count == 1 && page.Folder == destination && AppOptions.Load(config).SaveFolder == destination,
            "move keeps current PC/VR page, ink and persisted path");
        Check(page.Catalog.List().Count == 2 && page.Catalog.List(true).Single().Document.Id == trashDoc.Id && originals.All(p => !File.Exists(p)),
            "move merges distinct destination pages and includes trash");
        // Exact paths learned by MemoStore.Read must survive replacing the store, too.
        var live = page.Catalog.List().Single(e => e.Document.Id == id);
        string importedName = Path.Combine(destination, "imported-page.json");
        File.Move(live.JsonPath, importedName);
        File.Move(Path.ChangeExtension(live.JsonPath, ".png"), Path.ChangeExtension(importedName, ".png"));
        string importedDestination = Path.Combine(run, "imported moved again");
        Check(settings.ChangeFolder(importedDestination, FolderChangeChoice.Move) && page.SetPinned(true, Now()) &&
            page.Catalog.List().Count == 2 && File.Exists(Path.Combine(importedDestination, "imported-page.json")),
            "moving an imported filename preserves its exact path on the next save");
        destination = importedDestination;
        Capture(settings, root, "settings-folder-changed");
        string duplicateDestination = Path.Combine(run, "duplicate");
        var duplicateStore = new MemoStore(duplicateDestination);
        duplicateStore.Save(page.Snapshot(), stream => MemoRenderer.WritePng(page.Snapshot(), stream));
        Check(!settings.ChangeFolder(duplicateDestination, FolderChangeChoice.Move) && page.Folder == destination && settings.ErrorText.Contains("同じページ"),
            "duplicate IDs never overwrite either copy");
        Capture(settings, root, "settings-folder-duplicate-error");
        string switchDestination = Path.Combine(run, "without moving");
        browser.Handle(default(FrameResult) with { Commands = [BoardCommand.PageList] }, Now());
        Check(browser.IsOpen, "VR list is open before the settings change");
        Check(settings.ChangeFolder(switchDestination, FolderChangeChoice.Switch) && page.Id != id && engine.Strokes.Count == 0 &&
            new PageCatalog(destination).List().Any(e => e.Document.Id == id), "switch without moving leaves old data and starts a shared blank page");
        Check(page.Folder == switchDestination && browser.IsOpen == false, "VR and desktop share switched store");
        var lockedSource = page.Catalog;
        var sourceDoc = new MemoDocument(2, Guid.NewGuid(), origin, origin, BoardSize.Default, [], title: "削除に失敗する元ファイル");
        new MemoStore(switchDestination).Save(sourceDoc, stream => MemoRenderer.WritePng(sourceDoc, stream));
        var sourceEntry = lockedSource.List().Single();
        string deletionFailureDestination = Path.Combine(run, "source delete failed");
        using (File.Open(sourceEntry.JsonPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(settings.ChangeFolder(deletionFailureDestination, FolderChangeChoice.Move) && File.Exists(sourceEntry.JsonPath) &&
                page.Catalog.List().Single().Document.Id == sourceDoc.Id && settings.ErrorText.Contains(sourceEntry.JsonPath),
                "source deletion failure retains verified target and reports original remaining path");
            Capture(settings, root, "settings-source-delete-error");
        }
        var reloaded = new AppSettingsService(AppOptions.Load(config), _ => { }, config);
        using var reopened = new DesktopSettingsWindow(reloaded, new(reloaded, _ => { }), new(reloaded, _ => { }), page, Now, fixture.Auto);
        Check(reopened.SaveFolderText == deletionFailureDestination && reopened.RetentionChoice.SelectedIndex == 3, "restart restores settings and path");
        settings.Size = settings.MinimumSize;
        Application.DoEvents();
        Capture(settings, root, "settings-minimum-size");
        settings.Close();

    }

    private sealed class SettingsFixture : IDisposable
    {
        private readonly RunLog _log;
        private double _seconds = 1;

        internal string Root { get; }
        internal string Run { get; }
        internal string Config { get; }
        internal AppOptions Initial { get; }
        internal AppSettingsService App { get; }
        internal HandwriteEngine Engine { get; } = new(new(Vector3.Zero, Quaternion.Identity));
        internal DateTimeOffset Origin { get; }
        internal MemoSession Page { get; }
        internal DesktopWindow Desktop { get; private set; } = null!;
        internal DesktopSettingsWindow Settings { get; private set; } = null!;
        internal bool AutoEnabled { get; private set; }
        internal bool Reject { get; set; }
        internal bool Unavailable { get; set; }
        internal string? Opened { get; private set; }

        internal SettingsFixture()
        {
            Root = Path.GetFullPath(Path.Combine("artifacts", "task-031"));
            Run = Path.Combine(Root, "synthetic-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Run);
            Config = Path.Combine(Run, "settings.json");
            Initial = new AppOptions { SaveFolder = Path.Combine(Run, "pages"), RightIndexTipOffset = new(.01f, -.07f, -.035f) };
            Initial.Save(Config);
            Origin = DateTimeOffset.UtcNow;
            _log = new RunLog(Path.Combine(Run, "logs"));
            App = new AppSettingsService(Initial, _log.Event, Config);
            try
            {
                Page = new MemoSession(Engine, Initial.SaveFolder, Origin, _log.Event);
                Desktop = new DesktopWindow(Engine, Page, Now);
                var calibration = new WristCalibrationController(App, _log.Event);
                var autoStart = new AutoStartCoordinator(App, _log.Event);
                DesktopSettingsWindow Make() => new(App, autoStart, calibration, Page, Now, Auto, folder => Opened = folder, () => null);
                Desktop.ConfigureSettings(Make, App);
                Desktop.Show();
                Application.DoEvents();
                Capture(Desktop, Root, "desktop-entry");
                Desktop.SettingsButton.PerformClick();
                Application.DoEvents();
                Settings = Desktop.OwnedForms.OfType<DesktopSettingsWindow>().Single();
            }
            catch
            {
                Desktop?.Dispose();
                _log.Dispose();
                throw;
            }
        }

        internal double Now() => _seconds += .02;

        internal bool Auto(string command)
        {
            if (Unavailable)
                throw new InvalidOperationException(AutoStart.NotRunningMessage);
            if (command == "enable" && Reject)
                throw new InvalidOperationException(AutoStart.LocationMessage);
            if (command == "enable")
                AutoEnabled = true;
            if (command == "disable")
                AutoEnabled = false;
            return AutoEnabled;
        }

        public void Dispose()
        {
            Desktop.Dispose();
            _log.Dispose();
        }
    }

    private static void Click(CheckBox control) => typeof(CheckBox).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, [EventArgs.Empty]);
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static void Capture(Control control, string folder, string name)
    {
        control.PerformLayout();
        control.Refresh();
        Application.DoEvents();
        using var image = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(image, new(Point.Empty, image.Size));
        image.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png);
    }
}
