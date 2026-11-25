using System.Drawing.Imaging;
using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class BrandingUiTests
{
    [TestMethod]
    public void SurfacesAndActions()
    {
        string output = Path.GetFullPath(Path.Combine("artifacts", "task-040", "screens"));
        Directory.CreateDirectory(output);
        string run = Path.Combine(output, "synthetic-" + Guid.NewGuid().ToString("N"));
        var options = new AppOptions { SaveFolder = Path.Combine(run, "HandwriteMemo"), TrashRetentionDays = 0 };
        string config = Path.Combine(run, "settings.json");
        options.Save(config);
        using var log = new RunLog(Path.Combine(run, "logs"));
        var app = new AppSettingsService(options, log.Event, config);
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var page = new MemoSession(engine, options.SaveFolder, DateTimeOffset.UtcNow, log.Event);
        var calibration = new WristCalibrationController(app, log.Event);
        var autoStart = new AutoStartCoordinator(app, log.Event);
        double time = 1;
        Stroke(new(.15f, .4f), new(.25f, .6f));
        Stroke(new(.25f, .4f), new(.15f, .6f));
        Stroke(new(.35f, .5f), new(.5f, .5f));
        Stroke(new(.42f, .4f), new(.42f, .6f));
        Stroke(new(.64f, .43f), new(.69f, .4f), new(.69f, .6f));
        Check(page.Save(time += .02), "synthetic page saves");
        var entry = page.Catalog.List().Single();
        Check(page.Rename(entry, "描画サンプル", time += .02), "synthetic title saves");
        bool enabled = true, unavailable = false;
        bool Auto(string command)
        {
            if (unavailable)
                throw new InvalidOperationException(AutoStart.NotRunningMessage);
            if (command == "enable") enabled = true;
            if (command == "disable") enabled = false;
            return enabled;
        }
        using var window = new DesktopWindow(engine, page, () => time += .02);
        Check(window.SettingsButton.AccessibleName == "設定" && !window.SettingsButton.Enabled,
            "Settings has an accessible name and starts disabled");
        window.ConfigureSettings(() => new DesktopSettingsWindow(app, autoStart, calibration, page, () => time += .02, Auto, _ => { }, () => null), app);
        Check(window.SettingsButton.Enabled, "Configuring settings enables the settings button");
        window.Show();
        Application.DoEvents();
        Check(window.Text == "HnVRMemo" && ReferenceEquals(window.Icon, UiTheme.AppIcon), "PC title and product icon");
        Capture(window, "desktop");
        window.SelectButton.PerformClick();
        Capture(window, "desktop-selection");
        window.SelectButton.PerformClick();
        window.SettingsButton.PerformClick();
        Application.DoEvents();
        using var settings = window.OwnedForms.OfType<DesktopSettingsWindow>().Single();
        Check(settings.Text == "設定 — HnVRMemo" && settings.AutoStartSwitch.Checked, "settings title and mocked existing autostart");
        Capture(settings, "settings");
        settings.HandChoice.SelectedIndex = 1;
        settings.BrightnessChoice.SelectedIndex = 2;
        Check(AppOptions.Load(config).Handedness == HandSide.Left && app.Current.BoardBrightness == .6f, "settings save through real controls");
        Capture(settings, "settings-changed");
        unavailable = true;
        settings.RefreshAutoStart();
        Check(settings.RetryAutoButton.Visible && !settings.AutoStartSwitch.Enabled, "registration unavailable is visible");
        Capture(settings, "settings-steamvr-unavailable");
        unavailable = false;
        settings.RetryAutoButton.PerformClick();
        Check(settings.AutoStartSwitch.Enabled && settings.AutoStartSwitch.Checked, "mocked registration retry recovers");
        Check(settings.AutoStartSwitch.CheckState == CheckState.Checked, "recovering to on restores a two-state switch");
        Capture(settings, "settings-recovered");
        settings.Close();
        window.Size = window.MinimumSize;
        Capture(window, "desktop-minimum");
        using var painter = new BoardPainter();
        painter.Draw(engine, default, new VrInputStatus(""), page: page);
        painter.Image.Save(Path.Combine(output, "board.png"), ImageFormat.Png);
        painter.ToolsImage.Save(Path.Combine(output, "tools.png"), ImageFormat.Png);
        Dashboard("dashboard", options);
        Dashboard("dashboard-left-dim", app.Current);
        using var thumb = UiTheme.AppIcon.ToBitmap();
        thumb.Save(Path.Combine(output, "app-icon.png"), ImageFormat.Png);
        File.WriteAllLines(Path.Combine(output, "capture-notes.txt"),
        ["Real WinForms and production VR renderer; synthetic data and injected SteamVR registration only.",
            "PC: select/unselect, open settings, change handedness/brightness, unavailable registration and retry, minimum size.",
            "VR dashboard: production-rendered texture at default and changed settings; HMD visibility and controller input are unverified.",
            "No singleton entry point, user data, real SteamVR registration or existing process was used."]);
        Console.WriteLine("PASS: HnVRMemo PC/settings/icon and synthetic controls/retry; PC/VR textures saved for independent visual review.");

        void Stroke(params Vector2[] points)
        {
            Check(page.BeginDesktop(time += .02), "begin synthetic input");
            foreach (var point in points)
                page.DesktopPoint(point, true, time += .02, DrawingTool.Pen);
            page.DesktopPoint(null, false, time += .02, DrawingTool.Pen);
        }
        void Capture(Form form, string name)
        {
            form.PerformLayout();
            form.Refresh();
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
        }
        void Dashboard(string name, AppOptions state)
        {
            using var image = VrOverlaySprites.MakeDashboard(handedness: state.Handedness, boardBrightness: state.BoardBrightness);
            image.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
        }
    }
}
