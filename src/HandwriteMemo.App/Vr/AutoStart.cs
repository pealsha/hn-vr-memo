using Valve.VR;

namespace HandwriteMemo.App;

internal static class AutoStart
{
    public const string AppKey = "handwrite-memo.v1";
    internal static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "HnMemo.exe");
    public static string ManifestPath => Path.Combine(AppContext.BaseDirectory, "input", "app.vrmanifest");

    internal const string NotRunningMessage = "現在のオン・オフは SteamVR の起動中に確認できます。";
    internal const string LocationMessage = "SteamVR は日本語などの非ASCII文字を含むパスからの自動起動に失敗するため、登録できません。\n" +
        "C:\\HnMemo など、パス全体が半角英数字・記号だけの場所へフォルダごと移してください。\n" +
        "Windows のユーザー名が日本語の場合、ドキュメントやデスクトップの下も使えません。";

    internal static bool HasNonAsciiPath(string executablePath, string manifestPath) =>
        Path.GetFullPath(executablePath).Any(c => c > 0x7f) || Path.GetFullPath(manifestPath).Any(c => c > 0x7f);

    public static int Configure(string command)
    {
        using var log = new RunLog();
        return Configure(command, ExecutablePath, ManifestPath, Connect, OpenVR.Shutdown, log.Event);
    }

    // Keep the SteamVR connection at this boundary so tests never change a real registration.
    internal static int Configure(string command, string executablePath, string manifestPath,
        Func<IOpenVrApplications> connect, Action shutdown, Action<string>? report = null)
    {
        if (command is not ("enable" or "disable" or "status"))
            throw new ArgumentException("--autostart must be enable, disable, or status.");
        executablePath = Path.GetFullPath(executablePath);
        manifestPath = Path.GetFullPath(manifestPath);
        if (command == "enable")
        {
            if (HasNonAsciiPath(executablePath, manifestPath))
                throw new InvalidOperationException(LocationMessage);
            if (!File.Exists(executablePath))
                throw new FileNotFoundException("HnMemo.exe must exist beside this application.", executablePath);
        }
        try
        {
            var apps = connect();
            bool enabled = Apply(command, executablePath, manifestPath, apps, report);
            bool installed = apps.IsApplicationInstalled(AppKey);
            string registered = installed ? RegisteredBinaryPath(apps) : "";
            (report ?? Console.WriteLine)($"installed={installed}; autoLaunch={enabled}; registered={registered}");
            return 0;
        }
        finally
        {
            shutdown();
        }
    }

    // Shared by the command line and the desktop. An existing overlay connection stays alive.
    internal static bool Apply(string command, string executablePath, string manifestPath, IOpenVrApplications apps,
        Action<string>? report = null)
    {
        if (command is not ("enable" or "disable" or "status"))
            throw new ArgumentException("Invalid autostart command.");
        if (command == "enable")
        {
            executablePath = Path.GetFullPath(executablePath);
            manifestPath = Path.GetFullPath(manifestPath);
            if (HasNonAsciiPath(executablePath, manifestPath))
                throw new InvalidOperationException(LocationMessage);
            if (!File.Exists(executablePath))
                throw new FileNotFoundException("HnMemo.exe must exist beside this application.", executablePath);
        }
        if (command == "enable")
        {
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException("Application manifest must exist before replacing a registration.", manifestPath);
            report ??= Console.WriteLine;
            if (apps.IsApplicationInstalled(AppKey))
            {
                string registered = RegisteredBinaryPath(apps, required: true);
                if (!string.Equals(registered, executablePath, StringComparison.OrdinalIgnoreCase))
                {
                    // All released layouts place this manifest in input/ beside the executable.
                    string previousManifest = ManifestForBinary(registered);
                    Check(apps.RemoveApplicationManifest(previousManifest), $"Unregister previous application manifest {previousManifest}");
                    report($"autostart removed previous manifest={previousManifest}; registered={registered}");
                    if (apps.IsApplicationInstalled(AppKey))
                        throw new InvalidOperationException("SteamVR retained the previous application registration; registration stopped.");
                }
            }
            report($"autostart registering manifest={manifestPath}; binary={executablePath}");
            Check(apps.AddApplicationManifest(manifestPath, false), "自動起動を登録できません。SteamVRと配置先を確認して登録を再実行してください");
            string current = RegisteredBinaryPath(apps, required: true);
            // Real SteamVR can report the replaced path until a new session queries it, so this is only noted.
            if (!string.Equals(current, executablePath, StringComparison.OrdinalIgnoreCase))
                report($"autostart registered path not yet refreshed in this session: {current}; expected {executablePath}");
            Check(apps.SetApplicationAutoLaunch(AppKey, true), "Enable SteamVR auto launch");
            if (!apps.GetApplicationAutoLaunch(AppKey))
                throw new InvalidOperationException("SteamVR did not retain auto launch setting.");
            report($"autostart enabled manifest={manifestPath}; registered={current}");
        }
        if (command == "disable" && apps.IsApplicationInstalled(AppKey))
        {
            string registeredManifest = ManifestForBinary(RegisteredBinaryPath(apps, required: true));
            Check(apps.SetApplicationAutoLaunch(AppKey, false), "Disable SteamVR auto launch");
            Check(apps.RemoveApplicationManifest(registeredManifest), "Unregister application");
            (report ?? Console.WriteLine)($"autostart removed manifest={registeredManifest}");
        }
        return apps.IsApplicationInstalled(AppKey) && apps.GetApplicationAutoLaunch(AppKey);
    }

    private static string ManifestForBinary(string binary) =>
        Path.Combine(Path.GetDirectoryName(binary)!, "input", "app.vrmanifest");

    internal static bool Desktop(string command, IOpenVrApplications? connectedApplications, Action<string> report)
    {
        if (command == "enable" && HasNonAsciiPath(ExecutablePath, ManifestPath))
            throw new InvalidOperationException(LocationMessage);
        return WithApplications(connectedApplications, apps => Apply(command, ExecutablePath, ManifestPath, apps, report));
    }

    private static T WithApplications<T>(IOpenVrApplications? connectedApplications, Func<IOpenVrApplications, T> useApplications)
    {
        if (connectedApplications is not null)
            return useApplications(connectedApplications);
        if (!SteamVrRuntime.IsRunning())
            throw new InvalidOperationException(NotRunningMessage);
        try
        {
            return useApplications(Connect());
        }
        finally
        {
            OpenVR.Shutdown();
        }
    }

    private static string RegisteredBinaryPath(IOpenVrApplications apps, bool required = false)
    {
        var error = EVRApplicationError.None;
        var buffer = new System.Text.StringBuilder(32768);
        apps.GetApplicationPropertyString(AppKey, EVRApplicationProperty.BinaryPath_String, buffer, (uint)buffer.Capacity, ref error);
        if (required)
        {
            Check(error, "Read registered executable path");
            if (buffer.Length == 0 || !Path.IsPathFullyQualified(buffer.ToString()))
                throw new InvalidOperationException("SteamVR did not provide an absolute registered executable path; registration stopped.");
            return Path.GetFullPath(buffer.ToString());
        }
        return error == EVRApplicationError.None ? buffer.ToString() : $"unknown ({error})";
    }

    private static IOpenVrApplications Connect()
    {
        var error = EVRInitError.None;
        OpenVR.Init(ref error, EVRApplicationType.VRApplication_Utility);
        if (error != EVRInitError.None)
            throw new InvalidOperationException($"OpenVR utility initialization: {error}");
        return new OpenVrApplicationsAdapter(OpenVR.Applications ?? throw new InvalidOperationException("OpenVR Applications unavailable."));
    }

    public static void Check(EVRApplicationError error, string operation)
    {
        if (error != EVRApplicationError.None)
            throw new InvalidOperationException($"{operation}: {error}");
    }
}

internal sealed record AutoStartRequest(bool Enabled, bool Attempted = false);

// One notice per application run, including SteamVR reconnects; no settings or marker file is written.
internal sealed class AutoStartLocationNotice
{
    private bool _shown;

    internal string? Take(IOpenVrApplications apps, string executablePath, string manifestPath)
    {
        if (_shown || !AutoStart.HasNonAsciiPath(executablePath, manifestPath) ||
            !apps.IsApplicationInstalled(AutoStart.AppKey) || !apps.GetApplicationAutoLaunch(AutoStart.AppKey))
            return null;
        _shown = true;
        return "この置き場所には日本語などがあり、SteamVR から自動起動できません。" +
            "自動起動を解除し、C:\\HnMemo などへ移して登録し直してください。" +
            "ユーザー名が日本語の場合、ドキュメント・デスクトップの下も使えません。PC画面は使えます。";
    }
}
