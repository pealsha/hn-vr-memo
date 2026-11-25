using System.Reflection;
using System.Numerics;
using HandwriteMemo.Core;
using System.Text.Json;
using Valve.VR;

namespace HandwriteMemo.App;

[STATestClass]
public sealed class BrandingRegistrationTests
{
    [TestMethod]
    public void UpdateExisting()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "task-040", "registration-" + Guid.NewGuid().ToString("N")));
        string exe = Path.Combine(root, "HnVRMemo.exe"), manifest = Path.Combine(root, "input", "app.vrmanifest");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.WriteAllText(exe, "Synthetic placeholder; never executed.");
        File.Copy(AutoStart.ManifestPath, manifest);
        using var json = JsonDocument.Parse(File.ReadAllText(manifest));
        var app = json.RootElement.GetProperty("applications")[0];
        Check(app.GetProperty("app_key").GetString() == "handwrite-memo.v1" &&
            app.GetProperty("strings").GetProperty("en_us").GetProperty("name").GetString() == "HnVRMemo", "manifest identity and display name");
        Check(Path.GetFileName(AutoStart.ExecutablePath) == "HnVRMemo.exe", "production utility points to new executable");
        string oldManifest = Path.Combine(root, "old installation", "input", "app.vrmanifest");
        Directory.CreateDirectory(Path.GetDirectoryName(oldManifest)!);
        File.Copy(manifest, oldManifest);
        var fake = new Applications(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(oldManifest)!, "..", "HandwriteMemo.exe")));
        string original = fake.Binary;
        Check(AutoStart.Apply("status", exe, manifest, fake) && fake.Writes == 0 && fake.Binary == original, "status does not silently replace an old registration");
        Check(fake.AddApplicationManifest(manifest, false) == EVRApplicationError.None && fake.Binary == original,
            "mock reproduces successful Add without replacing an existing app key");
        fake.Writes = 0;
        using (var log = new RunLog(Path.Combine(root, "logs")))
        {
            Check(AutoStart.Configure("enable", exe, manifest, () => fake, () => { }, log.Event) == 0,
                "CLI replaces old registration");
        }
        string output = File.ReadAllText(Directory.GetFiles(Path.Combine(root, "logs"), "*.log").Single());
        Check(output.Contains("removed previous manifest=" + oldManifest) && output.Contains("registered=" + exe),
            "removal and verified new binary reach the persistent log");
        Check(fake.Binary == exe && fake.Enabled && fake.Removed.Single() == oldManifest && fake.Writes == 3,
            "remove old manifest before Add and auto launch");
        for (int i = 0; i < 3; i++)
        {
            Check(AutoStart.Apply("enable", exe, manifest, fake), "Enable works for an already installed app key");
            Check(fake.Binary == exe && fake.Enabled && fake.Writes == 3 + (i + 1) * 2 && fake.Removed.Count == 1,
                "repeated Enable reloads the same manifest without unnecessary removal");
        }
        fake.Reject = true;
        try
        {
            AutoStart.Apply("enable", exe, manifest, fake);
            throw new InvalidOperationException("Registration error was hidden.");
        }
        catch (InvalidOperationException error) when (error.Message.Contains("InvalidManifest"))
        {
            Check(fake.Binary == exe && fake.Enabled, "registration failure leaves previous state intact in the mock");
        }
        string missingBinary = Path.Combine(root, "deleted old installation", "HandwriteMemo.exe");
        var missing = new Applications(missingBinary);
        string missingManifest = missing.Manifest!;
        Check(!File.Exists(missingManifest), "old manifest really is absent");
        Check(AutoStart.Apply("enable", exe, manifest, missing) && missing.Binary == exe && missing.Removed.Single() == missingManifest,
            "missing old manifest does not prevent removing its registration");
        var renamed = new Applications(Path.Combine(root, "HandwriteMemo.exe"));
        Check(AutoStart.Apply("enable", exe, manifest, renamed) && renamed.Binary == exe && renamed.Removed.Single() == manifest,
            "rename in the same directory also replaces the old binary");
        var toggle = new Applications(missingBinary);
        Check(!AutoStart.Apply("disable", exe, manifest, toggle) && !toggle.Installed &&
            toggle.Removed.Single() == missingManifest, "desktop off removes the actual old registration from the new location");
        Check(AutoStart.Apply("enable", exe, manifest, toggle) && toggle.Binary == exe,
            "desktop off/on from a moved installation registers the new location");

        var blocked = new Applications(missingBinary) { RejectRemove = true };
        Fails(() => AutoStart.Apply("enable", exe, manifest, blocked), "InvalidManifest");
        Check(blocked.Binary == missingBinary && blocked.Writes == 0, "failed removal stops before Add or enabling");
        var unreadable = new Applications(missingBinary) { PropertyError = EVRApplicationError.UnknownApplication };
        Fails(() => AutoStart.Apply("enable", exe, manifest, unreadable), "UnknownApplication");
        Check(unreadable.Writes == 0, "failed BinaryPath query cannot remove an invented path");
        var invalidPath = new Applications("relative.exe");
        Fails(() => AutoStart.Apply("enable", exe, manifest, invalidPath), "absolute");
        Check(invalidPath.Writes == 0, "relative BinaryPath is rejected before writes");
        var retained = new Applications(missingBinary) { RetainOnRemove = true };
        Fails(() => AutoStart.Apply("enable", exe, manifest, retained), "retained");
        Check(retained.Writes == 1 && retained.Binary == missingBinary, "successful removal API with stale key is not reported as success");
        var addFailure = new Applications(missingBinary) { Reject = true };
        Fails(() => AutoStart.Apply("enable", exe, manifest, addFailure), "InvalidManifest");
        Check(!addFailure.Installed && addFailure.Writes == 1, "Add failure after removal stays unregistered and is reported");
        addFailure.Reject = false;
        Check(AutoStart.Apply("enable", exe, manifest, addFailure) && addFailure.Binary == exe, "Enable retry recovers after Add failure");
        var wrongBinary = new Applications(missingBinary) { AddedBinary = missingBinary };
        // Real SteamVR may report the replaced path until the next session; enabling still proceeds.
        Check(AutoStart.Apply("enable", exe, manifest, wrongBinary) && wrongBinary.Enabled, "a stale same-session path still enables auto launch");
        var noManifest = new Applications(missingBinary);
        Fails(() => AutoStart.Apply("enable", exe, Path.Combine(root, "missing.vrmanifest"), noManifest), "must exist");
        Check(noManifest.Writes == 0, "missing new manifest leaves old registration intact");

        var random = new Random(41);
        var sequence = new Applications(missingBinary);
        for (int i = 0; i < 100; i++)
        {
            string command = new[] { "enable", "disable", "status" }[random.Next(3)];
            int writes = sequence.Writes;
            bool installed = sequence.Installed, enabled = sequence.Enabled;
            bool result = AutoStart.Apply(command, exe, manifest, sequence);
            if (command == "status")
                Check(sequence.Writes == writes && result == (installed && enabled), "random status remains read-only");
            else if (command == "enable")
                Check(result && sequence.Binary == exe && sequence.Installed, "random enable points to current exe");
            else
                Check(!result && !sequence.Installed && !sequence.Enabled, "random disable clears registration and auto launch");
        }
        var connected = new Applications(missingBinary);
        var preferences = new AppSettingsService(new AppOptions { SaveFolder = Path.Combine(root, "pages") }, _ => { },
            Path.Combine(root, "connected-settings.json"));
        var coordinator = new AutoStartCoordinator(preferences, _ => { });
        int changes = 0;
        coordinator.StateChanged += () => changes++;
        Check(coordinator.ChangeAutoStartRequest(false), "queue a change before the overlay connects");
        coordinator.HandleVrConnected(connected);
        Check(changes == 2 && !connected.Installed && connected.Writes == 2 && preferences.Current.PendingAutoStart is null,
            "the active applications interface applies and clears the reservation");
        coordinator.HandleVrDisconnected();
        coordinator.HandleVrConnected(connected);
        Check(connected.Writes == 2, "a reconnect does not replay an acknowledged reservation");

        SettingsToggle(root, exe, manifest, missingBinary);
        Console.WriteLine("PASS: existing SteamVR app key reloads HnVRMemo manifest/exe on explicit Enable; repeat/status/failure tested with an injected API only.");
        Console.WriteLine("PASS: old/missing/same-location registration, strict mock, log, failure/retry and 100 seeded operations; no real SteamVR registration changed.");
    }

    private static void SettingsToggle(string root, string exe, string manifest, string oldBinary)
    {
        var options = new AppOptions { SaveFolder = Path.Combine(root, "pages") };
        string config = Path.Combine(root, "settings.json");
        options.Save(config);
        using var log = new RunLog(Path.Combine(root, "ui-logs"));
        var app = new AppSettingsService(options, log.Event, config);
        var engine = new HandwriteEngine(new(Vector3.Zero, Quaternion.Identity));
        var page = new MemoSession(engine, options.SaveFolder, DateTimeOffset.UtcNow, log.Event);
        var calibration = new WristCalibrationController(app, log.Event);
        var autoStart = new AutoStartCoordinator(app, log.Event);
        var fake = new Applications(oldBinary);
        using var settings = new DesktopSettingsWindow(app, autoStart, calibration, page, () => 1,
            command => AutoStart.Apply(command, exe, manifest, fake, log.Event));
        settings.Show();
        Application.DoEvents();
        Check(settings.AutoStartSwitch.Checked, "real settings display old enabled registration");
        Capture("old-registration");
        Click();
        Check(!settings.AutoStartSwitch.Checked && !fake.Installed && settings.AutoStartErrorText.Length == 0,
            "real settings off removes old registration without an error");
        Capture("off");
        fake.Reject = true;
        Click();
        Check(!settings.AutoStartSwitch.Checked && settings.AutoStartErrorText.Contains("InvalidManifest"),
            "real settings display Add failure and keep actual off state");
        Capture("enable-error");
        fake.Reject = false;
        Click();
        Check(settings.AutoStartSwitch.Checked && fake.Binary == exe && settings.AutoStartErrorText.Length == 0,
            "real settings retry enables the new executable and clears error");
        Capture("new-registration");

        void Click() => typeof(CheckBox).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings.AutoStartSwitch, [EventArgs.Empty]);
        void Capture(string name)
        {
            Application.DoEvents();
            using var image = new System.Drawing.Bitmap(settings.Width, settings.Height);
            settings.DrawToBitmap(image, new(System.Drawing.Point.Empty, image.Size));
            image.Save(Path.Combine(root, "settings-" + name + ".png"));
        }
    }

    private static void Fails(Action action, string expected)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidOperationException or FileNotFoundException)
        {
            Check(error.Message.Contains(expected), "actionable failure: " + expected);
            return;
        }
        throw new InvalidOperationException("Registration failure was hidden: " + expected);
    }

    private sealed class Applications : IOpenVrApplications
    {
        internal string Binary;
        internal string? Manifest;
        internal string? AddedBinary;
        internal bool Installed = true, Enabled = true, Reject, RejectRemove, RetainOnRemove;
        internal EVRApplicationError PropertyError;
        internal List<string> Removed = [];
        internal int Writes;

        internal Applications(string binary)
        {
            Binary = binary;
            Manifest = Path.Combine(Path.GetDirectoryName(binary) ?? "", "input", "app.vrmanifest");
        }

        public EVRApplicationError AddApplicationManifest(string path, bool temporary)
        {
            if (Reject)
                return EVRApplicationError.InvalidManifest;
            Check(!temporary, "persistent manifest");
            string manifest = path;
            using var json = JsonDocument.Parse(File.ReadAllText(manifest));
            var app = json.RootElement.GetProperty("applications")[0];
            Check(app.GetProperty("app_key").GetString() == AutoStart.AppKey, "same app key updated");
            string binary = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest)!, app.GetProperty("binary_path_windows").GetString()!));
            Check(File.Exists(binary), "manifest binary exists");
            if (!Installed || string.Equals(Manifest, manifest, StringComparison.OrdinalIgnoreCase))
            {
                Binary = AddedBinary ?? binary;
                Manifest = manifest;
                Installed = true;
            }
            Writes++;
            return EVRApplicationError.None;
        }

        public EVRApplicationError RemoveApplicationManifest(string path)
        {
            if (RejectRemove)
                return EVRApplicationError.InvalidManifest;
            string manifest = path;
            Removed.Add(manifest);
            if (string.Equals(Manifest, manifest, StringComparison.OrdinalIgnoreCase) && !RetainOnRemove)
            {
                Installed = false;
                Enabled = false;
                Manifest = null;
            }
            Writes++;
            return EVRApplicationError.None;
        }

        public uint GetApplicationPropertyString(string key, EVRApplicationProperty property,
            System.Text.StringBuilder buffer, uint length, ref EVRApplicationError error)
        {
            CheckKey(key);
            Check(property == EVRApplicationProperty.BinaryPath_String, "query registered binary");
            error = Installed ? PropertyError : EVRApplicationError.UnknownApplication;
            if (error == EVRApplicationError.None)
                buffer.Append(Binary);
            return (uint)buffer.Length + 1;
        }

        public bool IsApplicationInstalled(string key)
        {
            CheckKey(key);
            return Installed;
        }
        public EVRApplicationError SetApplicationAutoLaunch(string key, bool enabled)
        {
            CheckKey(key);
            if (!Installed)
                return EVRApplicationError.UnknownApplication;
            Enabled = enabled;
            Writes++;
            return EVRApplicationError.None;
        }
        public bool GetApplicationAutoLaunch(string key)
        {
            CheckKey(key);
            return Enabled;
        }
        public EVRApplicationError IdentifyApplication(uint processId, string key) => throw new NotSupportedException();

        private static void CheckKey(string key) => Check(key == "handwrite-memo.v1", "stable key, no second application");
    }
}
