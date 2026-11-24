using Valve.VR;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class AutoStartTests
{
    [TestMethod]
    public void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "task-028", "autostart"));
        string exe = Path.Combine(root, "ascii space", "HnMemo.exe");
        string manifest = Path.Combine(root, "ascii space", "input", "app.vrmanifest");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "Test placeholder; never executed.");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.Copy(AutoStart.ManifestPath, manifest, overwrite: true);
        Check(!AutoStart.HasNonAsciiPath(exe, manifest), "ASCII paths with spaces are accepted");
        foreach (var segment in new[] { "日本語", "élève", "메모", "📝" })
        {
            Check(AutoStart.HasNonAsciiPath(Path.Combine(root, segment, "HnMemo.exe"), manifest), "Non-ASCII executable path");
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
                Check(e.Message.Contains(@"C:\HnMemo") && e.Message.Contains("ユーザー名"), "Actionable rejection");
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
        Check(notice.Take(fake, nonAsciiExe, manifest)?.Contains(@"C:\HnMemo") == true, "Registered non-ASCII installation notifies");
        Check(notice.Take(fake, nonAsciiExe, manifest) is null, "Reconnect does not repeat notification");
        Check(new AutoStartLocationNotice().Take(fake, exe, Path.Combine(root, "日本語", "app.vrmanifest")) is not null,
            "A non-ASCII manifest also triggers the startup notice");
        Check(!AppHost.ShouldOpenDesktop(desktopRequested: false, backgroundLaunch: true),
            "SteamVR autostart keeps the desktop hidden");

        Check(AutoStart.Configure("disable", nonAsciiExe, manifest, Connect, Shutdown) == 0 &&
            !fake.Installed && !fake.AutoLaunch && fake.Writes == 4,
            "Non-ASCII disable still disables and unregisters");
        Check(connects == 3 && shutdowns == 3, "All simulated utility sessions finish");
        Console.WriteLine("PASS: Task 028 ASCII/non-ASCII paths, rejection before SteamVR, simulated enable/disable/status and once-per-run notice; no real registration changed");
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
                _binary = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Manifest!)!, "..", "HnMemo.exe"));
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
