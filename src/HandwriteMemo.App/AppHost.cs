using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using HandwriteMemo.Core;
using Timer = System.Windows.Forms.Timer;

namespace HandwriteMemo.App;

internal sealed class AppHost(AppOptions options, double? duration, bool showAtStart, bool desktopAtStart = false,
    EventWaitHandle? activation = null, string? diagnosticFolder = null, bool disableVr = false, bool background = false,
    string? configPath = null)
{
    private readonly DateTimeOffset _clockOrigin = DateTimeOffset.UtcNow;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private volatile bool _stopped;
    private Control? _dispatcher;
    private ApplicationContext? _context;
    private bool _runningVr;

    internal bool IsStopped => _stopped;
    private double Seconds() => _clock.Elapsed.TotalSeconds;

    internal static int Launch(string? config, double? duration, bool show, bool desktop, bool background)
    {
        using var activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\HandwriteMemo.v1.Desktop");
        using var mutex = new Mutex(true, @"Local\HandwriteMemo.v1", out bool acquired);
        if (!acquired)
        {
            if (!background || desktop)
            {
                var window = FindWindow(null, "HnMemo");
                if (window != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(window, out uint owner);
                    AllowSetForegroundWindow(owner);
                }
                activation.Set();
            }
            return 0;
        }
        try
        {
            var options = AppOptions.Load(config);
            Program.InitializeApplication();
            return new AppHost(options, duration, show, desktop, activation, background: background, configPath: config).Run();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    internal int Run()
    {
        using var log = new RunLog(diagnosticFolder);
        var engine = new HandwriteEngine(new BoardPose(Vector3.Zero, Quaternion.Identity));
        var settings = new AppSettingsService(options, log.Event, configPath);
        var page = new MemoSession(engine, settings.Current.SaveFolder, _clockOrigin, log.Event);
        var autoStart = new AutoStartCoordinator(settings, log.Event);
        var calibration = new WristCalibrationController(settings, log.Event);
        var retention = new TrashRetention(() => DateTimeOffset.UtcNow, log.Event);
        CheckTrashRetention();
        using var window = new DesktopWindow(engine, page, Seconds);
        window.ConfigureSettings(() => new DesktopSettingsWindow(settings, autoStart, calibration, page, Seconds), settings);
        _ = window.Handle; // Available for foreground permission even after a hidden SteamVR launch.
        using var tray = new NotifyIcon { Icon = UiTheme.AppIcon, Text = "HnMemo" };
        using var vr = new VrOverlayHost(settings, calibration, engine, page, log, Seconds, RequestUserExit);
        using var menu = MakeTrayMenu(window.OpenWindow,
            () => Process.Start(new ProcessStartInfo(settings.Current.SaveFolder) { UseShellExecute = true }), RequestUserExit);
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
        tray.DoubleClick += (_, _) => window.OpenWindow();
        using var context = new ApplicationContext();
        using var dispatcher = new Control();
        _ = dispatcher.Handle;
        _context = context;
        _dispatcher = dispatcher;

        using var watchVr = new Timer { Interval = 3000 };
        using var trashTimer = new Timer { Interval = 60000 };
        using var durationTimer = new Timer();
        double? deadline = null;
        string? notifiedError = null;
        RegisteredWaitHandle? registeredActivation = activation is null ? null :
            ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => Post(OpenDesktop), null, Timeout.Infinite, false);
        ConsoleCancelEventHandler cancel = (_, e) =>
        {
            e.Cancel = true;
            RequestStop();
        };

        Console.CancelKeyPress += cancel;
        settings.Changed += OnSettingsChanged;
        settings.Notification += Notify;
        autoStart.Notification += Notify;
        vr.GestureStatusChanged += OnGestureStatusChanged;
        page.Changed += OnPageChanged;
        watchVr.Tick += (_, _) => CheckVr();
        trashTimer.Tick += (_, _) => CheckTrashRetention();
        durationTimer.Tick += (_, _) => ArmDurationTimer();
        try
        {
            if (ShouldOpenDesktop(!disableVr && SteamVrRunning(), desktopAtStart, background))
                window.OpenWindow();
            // Start counting only once WinForms begins dispatching messages, after shell/tray setup.
            dispatcher.BeginInvoke(() =>
            {
                if (duration is { } limit)
                {
                    deadline = Seconds() + limit;
                    ArmDurationTimer();
                }
                trashTimer.Start();
                if (!disableVr)
                {
                    watchVr.Start();
                    CheckVr();
                }
            });
            if (!_stopped)
                Application.Run(context);
        }
        finally
        {
            watchVr.Stop();
            trashTimer.Stop();
            durationTimer.Stop();
            registeredActivation?.Unregister(null);
            _dispatcher = null;
            _context = null;
            Console.CancelKeyPress -= cancel;
            page.Changed -= OnPageChanged;
            vr.GestureStatusChanged -= OnGestureStatusChanged;
            autoStart.Notification -= Notify;
            settings.Notification -= Notify;
            settings.Changed -= OnSettingsChanged;
        }

        SaveOnExit(page);
        return 0;

        void OpenDesktop()
        {
            if (!_stopped)
                window.OpenWindow();
        }

        void CheckVr()
        {
            if (_stopped || !SteamVrRunning())
                return;
            watchVr.Stop();
            _runningVr = true;
            try
            {
                vr.RunSession(pumpUi: Application.DoEvents, () => _stopped, showAtStart, autoStart);
            }
            catch (Exception e)
            {
                log.Event("VR disconnected/error: " + e.Message);
            }
            finally
            {
                _runningVr = false;
                if (_stopped)
                    StopMessageLoop();
                else
                    watchVr.Start();
            }
        }

        void ArmDurationTimer()
        {
            if (deadline is not { } end)
                return;
            double remaining = end - Seconds();
            if (remaining <= 0)
            {
                durationTimer.Stop();
                RequestStop();
                return;
            }
            durationTimer.Interval = (int)Math.Clamp(Math.Ceiling(remaining * 1000), 1, int.MaxValue);
            durationTimer.Start();
        }

        void CheckTrashRetention()
        {
            try
            {
                retention.Run(page.Catalog, settings.Current.TrashRetentionDays);
                page.NotifyCatalogChanged();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.Event("Trash retention failed; retry in 60 seconds: " + e.Message);
            }
        }

        void OnSettingsChanged(SettingsChange change)
        {
            if ((change & (SettingsChange.Retention | SettingsChange.SaveFolder)) == 0)
                return;
            CheckTrashRetention();
            trashTimer.Stop();
            trashTimer.Start();
        }

        void RequestUserExit()
        {
            page.InterruptVr(Seconds());
            if (page.Save(Seconds()))
                RequestStop();
            else
                Notify(new("保存できません", "終了を中止しました。保存先を確認し、PC画面の「保存を再試行」ボタンを押してください。", NotificationIcon.Error));
        }

        void OnGestureStatusChanged(string status) => tray.Text = "HnMemo / " + status;

        void OnPageChanged(SessionChange change)
        {
            if (notifiedError == page.Error)
                return;
            notifiedError = page.Error;
            if (notifiedError is not null)
                Notify(new("HnMemo — 保存できません", MemoSession.SaveFailureMessage +
                    "\nPC画面を開き、「保存を再試行」ボタンを押してください。", NotificationIcon.Error));
        }

        void Notify(AppNotification notification) => tray.ShowBalloonTip(10000, notification.Title, notification.Text,
            notification.Icon == NotificationIcon.Error ? ToolTipIcon.Error : ToolTipIcon.Warning);
    }

    private void SaveOnExit(MemoSession page)
    {
        page.DesktopPoint(null, false, Seconds(), DrawingTool.Pen);
        if (page.Save(Seconds()))
            return;
        if (!page.EmergencySave(out string? folder))
            MessageBox.Show("保存と復旧コピーに失敗しました。直前の変更は未保存です。", "HnMemo — 未保存");
        else if (folder is not null)
            MessageBox.Show("通常の保存先に保存できませんでした。復旧コピーを次へ保存しました。\n" + folder,
                "HnMemo — 保存先を確認してください", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    internal void RequestStop()
    {
        _stopped = true;
        Post(StopMessageLoop);
    }

    private void StopMessageLoop()
    {
        if (!_runningVr)
            _context?.ExitThread();
    }

    private void Post(Action action)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null)
            return;
        try
        {
            dispatcher.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // A wait or console handler can race with disposal after the message loop exits.
        }
    }

    internal static bool SteamVrRunning() => SteamVrRuntime.IsRunning();

    internal static bool ShouldOpenDesktop(bool steamVrRunning, bool desktopRequested, bool backgroundLaunch) =>
        desktopRequested || !backgroundLaunch && !steamVrRunning;

    internal static ContextMenuStrip MakeTrayMenu(Action openDesktop, Action openSaveFolder, Action requestStop)
    {
        var menu = new ContextMenuStrip();
        UiTheme.StyleMenu(menu);
        menu.Items.Add("デスクトップ画面を開く", null, (_, _) => openDesktop());
        menu.Items.Add("保存フォルダを開く", null, (_, _) => openSaveFolder());
        menu.Items.Add("終了", null, (_, _) => requestStop());
        return menu;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool AllowSetForegroundWindow(uint processId);
}
