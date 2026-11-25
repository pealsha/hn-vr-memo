using Valve.VR;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class AutoStartTests
{
    [TestMethod]
    public void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "task-028", "autostart"));
        string exe = Path.Combine(root, "ascii space", "HnVRMemo.exe");
        string manifest = Path.Combine(root, "ascii space", "input", "app.vrmanifest");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "Test placeholder; never executed.");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.Copy(AutoStart.ManifestPath, manifest, overwrite: true);
        Check(!AutoStart.HasNonAsciiPath(exe, manifest), "ASCII paths with spaces are accepted");
        foreach (var segment in new[] { "日本語", "élève", "메모", "📝" })
        {
            Check(AutoStart.HasNonAsciiPath(Path.Combine(root, segment, "HnVRMemo.exe"), manifest), "Non-ASCII executable path");
            Check(AutoStart.HasNonAsciiPath(exe, Path.Combine(root, segment, "app.vrmanifest")), "Non-ASCII manifest path");
        }
        Check(!AutoStart.HasNonAsciiPath(Path.Combine(root, "日本語", "..", "app.exe"), manifest), "Check resolved absolute paths");

        var fake = new Applications();
        int connects = 0, shutdowns = 0;
        IOpenVrApplications Connect()
        {
            connects++;
            return fake;
        }
        void Shutdown() => shutdowns++;
        foreach (var paths in new[] {
            (Path.Combine(root, "日本語", "app.exe"), manifest),
            (exe, Path.Combine(root, "日本語", "app.vrmanifest")) })
        {
            try
            {
                AutoStart.Configure("enable", paths.Item1, paths.Item2, Connect, Shutdown);
                throw new InvalidOperationException("Non-ASCII registration was accepted");
            }
            catch (InvalidOperationException e) when (e.Message == AutoStart.LocationMessage)
            {
                Check(e.Message.Contains(@"C:\HnVRMemo") && e.Message.Contains("ユーザー名"), "Actionable rejection");
            }
        }
        Check(connects == 0 && shutdowns == 0 && fake.Writes == 0, "Reject before connecting to SteamVR or registering");
        Check(AutoStart.Configure("enable", exe, manifest, Connect, Shutdown) == 0 &&
            fake.Installed && fake.AutoLaunch && fake.Manifest == manifest && fake.Writes == 2,
            "ASCII registration retains manifest and autolaunch behavior");
        Check(connects == 1 && shutdowns == 1, "Utility session always shuts down");
        string nonAsciiExe = Path.Combine(root, "利用者", "app.exe");
        Check(AutoStart.Configure("status", nonAsciiExe, manifest, Connect, Shutdown) == 0 && fake.Writes == 2,
            "Non-ASCII status is read-only and does not require an executable");

        var notice = new AutoStartLocationNotice();
        Check(notice.Take(fake, exe, manifest) is null, "ASCII installation does not notify");
        fake.AutoLaunch = false;
        Check(notice.Take(fake, nonAsciiExe, manifest) is null, "Disabled autostart does not notify");
        fake.AutoLaunch = true;
        fake.Installed = false;
        Check(notice.Take(fake, nonAsciiExe, manifest) is null, "Unregistered installation does not notify");
        fake.Installed = true;
        Check(notice.Take(fake, nonAsciiExe, manifest)?.Contains(@"C:\HnVRMemo") == true, "Registered non-ASCII installation notifies");
        Check(notice.Take(fake, nonAsciiExe, manifest) is null, "Reconnect does not repeat notification");
        Check(new AutoStartLocationNotice().Take(fake, exe, Path.Combine(root, "日本語", "app.vrmanifest")) is not null,
            "A non-ASCII manifest also triggers the startup notice");
        Check(!AppHost.ShouldOpenDesktop(desktopRequested: false, backgroundLaunch: true),
            "SteamVR autostart keeps the desktop hidden");

        Check(AutoStart.Configure("disable", nonAsciiExe, manifest, Connect, Shutdown) == 0 &&
            !fake.Installed && !fake.AutoLaunch && fake.Writes == 4,
            "Non-ASCII disable still disables and unregisters");
        Check(connects == 3 && shutdowns == 3, "All simulated utility sessions finish");
        Console.WriteLine("PASS: ASCII/non-ASCII paths, rejection before SteamVR, simulated enable/disable/status and once-per-run notice; no real registration changed");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void DeferredEnableSkipsNonAsciiPaths(bool nonAsciiExecutable)
    {
        var (config, exe, manifest) = DeferredPaths();
        if (nonAsciiExecutable)
            exe = Path.Combine(Path.GetDirectoryName(exe)!, "日本語", "HnVRMemo.exe");
        else
            manifest = Path.Combine(Path.GetDirectoryName(manifest)!, "日本語", "app.vrmanifest");

        var logs = new List<string>();
        int exit = AutoStart.ConfigureDeferred(config, exe, manifest,
            () => throw new AssertFailedException("SteamVR must not be checked for this path"),
            () => throw new AssertFailedException("SteamVR must not be contacted"),
            () => throw new AssertFailedException("There must be no session to shut down"), logs.Add);

        Check(exit == 0 && !File.Exists(config), "Non-ASCII path skips registration and reservation");
        Check(logs.Count == 1 && logs[0] == AutoStart.LocationMessage, "Log why the path was skipped");
    }

    [TestMethod]
    public void DeferredEnableRegistersImmediatelyAndClearsExistingRequest()
    {
        var (config, exe, manifest) = DeferredPaths();
        var options = new AppOptions { BoardBrightness = .8f, TrashRetentionDays = 14, PendingAutoStart = new(false, true) };
        options.Save(config);
        var fake = new Applications();
        int shutdowns = 0;

        int exit = AutoStart.ConfigureDeferred(config, exe, manifest, () => true, () => fake,
            () => shutdowns++, Console.WriteLine);

        Check(exit == 0 && fake.Installed && fake.AutoLaunch && fake.Manifest == manifest && fake.Writes == 2 && shutdowns == 1,
            "Immediate registration enables autostart and closes the utility connection");
        Check(AppOptions.Load(config) == (options with { PendingAutoStart = null }), "Clear only the pending request");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DeferredEnableSavesRequestForNextConnection(bool steamVrRunning)
    {
        var (config, exe, manifest) = DeferredPaths();
        var options = new AppOptions { BoardBrightness = .8f, TrashRetentionDays = 14, PendingAutoStart = new(false, true) };
        options.Save(config);
        int connections = 0, shutdowns = 0;
        var logs = new List<string>();

        int exit = AutoStart.ConfigureDeferred(config, exe, manifest, () => steamVrRunning, () =>
        {
            connections++;
            throw new InvalidOperationException("OpenVR utility initialization failed");
        }, () => shutdowns++, logs.Add);

        int expectedConnections = steamVrRunning ? 1 : 0;
        Check(exit == 0 && connections == expectedConnections && shutdowns == expectedConnections,
            "Only a running SteamVR receives an immediate connection attempt");
        Check(AppOptions.Load(config) == (options with { PendingAutoStart = new(true) }), "Save an unattempted enable request and retain other settings");
        if (steamVrRunning)
            Check(logs.Any(line => line.Contains("OpenVR utility initialization failed")), "Log the immediate connection failure");

        var fake = new Applications();
        var coordinator = new AutoStartCoordinator(new AppSettingsService(AppOptions.Load(config), logs.Add, config), logs.Add);
        Check(coordinator.ApplyPendingAutoStart(command => AutoStart.Apply(command, exe, manifest, fake, logs.Add)) &&
            fake.AutoLaunch && AppOptions.Load(config) == (options with { PendingAutoStart = null }),
            "On the next connection the saved request enables autostart and is cleared");
    }

    [TestMethod]
    public void DeferredEnableCreatesSettingsForFirstInstall()
    {
        var (config, exe, manifest) = DeferredPaths();
        int exit = AutoStart.ConfigureDeferred(config, exe, manifest, () => false,
            () => throw new AssertFailedException("SteamVR is stopped"), () => { }, Console.WriteLine);

        Check(exit == 0 && AppOptions.Load(config).PendingAutoStart == new AutoStartRequest(true), "First install saves the enable request");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void DeferredEnableLogsSettingsFailuresAndExitsSuccessfully(bool settingsReadable)
    {
        var (config, exe, manifest) = DeferredPaths();
        string original = settingsReadable ? "{}" : "{";
        File.WriteAllText(config, original);
        var logs = new List<string>();
        using var locked = settingsReadable ? File.Open(config, FileMode.Open, FileAccess.Read, FileShare.Read) : null;

        int exit = AutoStart.ConfigureDeferred(config, exe, manifest, () => false,
            () => throw new AssertFailedException("SteamVR is stopped"), () => { }, logs.Add);

        Check(exit == 0 && File.ReadAllText(config) == original, "Keep the existing settings when reading or writing fails");
        Check(logs.Any(line => line.Contains(settingsReadable ? "Settings save failed" : "Autostart settings unavailable")) &&
            !logs.Any(line => line.Contains("requested for the next SteamVR connection")), "Report the failure and leave the request unsaved");
    }

    [TestMethod]
    public void InstallerSelectsAutostartAndRunsItBeforeLaunchingHnVRMemo()
    {
        const string RelativePath = "installer/HnVRMemo.iss";
        string directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, RelativePath)))
            directory = Directory.GetParent(directory)?.FullName ?? throw new InvalidOperationException("Cannot find " + RelativePath);
        string[] lines = File.ReadAllLines(Path.Combine(directory, RelativePath));
        int tasks = Array.IndexOf(lines, "[Tasks]");
        int files = Array.IndexOf(lines, "[Files]");
        int task = Array.IndexOf(lines, "Name: \"autostart\"; Description: \"{cm:LaunchWithSteamVr}\"");
        Check(tasks >= 0 && tasks < task && task < files, "Autostart is selected by default in the installer");
        Check(lines.Any(line => line.StartsWith("english.LaunchWithSteamVr=")) &&
            lines.Any(line => line.StartsWith("japanese.LaunchWithSteamVr=")), "Both installer languages label the task");

        int run = Array.IndexOf(lines, "[Run]");
        int autoStart = Array.IndexOf(lines, """Filename: "{app}\HnVRMemo.exe"; Parameters: "--autostart enable --defer"; Tasks: autostart; Flags: runhidden runasoriginaluser skipifdoesntexist""");
        int launch = Array.FindIndex(lines, line => line.Contains("Description: \"{cm:LaunchHnVRMemo}\""));
        int uninstallRun = Array.IndexOf(lines, "[UninstallRun]");
        Check(run >= 0 && run < autoStart && autoStart < launch && launch < uninstallRun,
            "Setup waits for the selected autostart command to run as the original user before the optional app launch");
    }

    private static (string Config, string Exe, string Manifest) DeferredPaths()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "deferred-autostart-" + Guid.NewGuid().ToString("N")));
        string exe = Path.Combine(root, "HnVRMemo.exe");
        string manifest = Path.Combine(root, "input", "app.vrmanifest");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.WriteAllText(exe, "Test placeholder; never executed.");
        File.Copy(AutoStart.ManifestPath, manifest);
        return (Path.Combine(root, "settings.json"), exe, manifest);
    }

    private sealed class Applications : IOpenVrApplications
    {
        public bool Installed, AutoLaunch;
        public int Writes;
        public string? Manifest;
        private string _binary = "";
        public EVRApplicationError AddApplicationManifest(string path, bool temporary)
        {
            Check(!temporary, "Registration is persistent");
            if (!Installed)
            {
                Manifest = path;
                _binary = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Manifest!)!, "..", "HnVRMemo.exe"));
                Installed = true;
            }
            Writes++;
            return EVRApplicationError.None;
        }
        public EVRApplicationError RemoveApplicationManifest(string path)
        {
            if (string.Equals(Manifest, path, StringComparison.OrdinalIgnoreCase))
                Installed = false;
            Writes++;
            return EVRApplicationError.None;
        }
        public bool IsApplicationInstalled(string key)
        {
            CheckKey(key);
            return Installed;
        }
        public EVRApplicationError SetApplicationAutoLaunch(string key, bool enabled)
        {
            CheckKey(key);
            AutoLaunch = enabled;
            Writes++;
            return EVRApplicationError.None;
        }
        public bool GetApplicationAutoLaunch(string key)
        {
            CheckKey(key);
            return AutoLaunch;
        }
        public uint GetApplicationPropertyString(string key, EVRApplicationProperty property,
            System.Text.StringBuilder buffer, uint length, ref EVRApplicationError error)
        {
            CheckKey(key);
            Check(property == EVRApplicationProperty.BinaryPath_String, "Read registered binary property");
            buffer.Append(_binary);
            error = EVRApplicationError.None;
            return (uint)buffer.Length + 1;
        }
        public EVRApplicationError IdentifyApplication(uint processId, string key) => throw new NotSupportedException();

        private static void CheckKey(string key) => Check(key == "handwrite-memo.v1", "Existing app key is unchanged");
    }
}
