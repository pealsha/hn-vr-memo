namespace HandwriteMemo.App;

internal sealed class AutoStartCoordinator(AppSettingsService settings, Action<string> log)
{
    private readonly AutoStartLocationNotice _locationNotice = new();
    private IOpenVrApplications? _applications;
    internal string? AutoStartRequestError { get; private set; }
    internal event Action? StateChanged;
    internal event Action<AppNotification>? Notification;

    internal bool ConfigureAutoStart(string command) => AutoStart.Desktop(command, _applications, log);

    internal void HandleVrConnected(IOpenVrApplications applications)
    {
        _applications = applications;
        ApplyPendingAutoStart(ConfigureAutoStart);
        if (_locationNotice.Take(applications, AutoStart.ExecutablePath, AutoStart.ManifestPath) is { } notice)
        {
            log(notice);
            Notification?.Invoke(new("HnVRMemo — 自動起動の置き場所", notice, NotificationIcon.Warning));
        }
    }

    internal void HandleVrDisconnected() => _applications = null;

    internal bool ChangeAutoStartRequest(bool? enabled)
    {
        try
        {
            if (enabled == true && AutoStart.HasNonAsciiPath(AutoStart.ExecutablePath, AutoStart.ManifestPath))
            {
                AutoStartRequestError = AutoStart.LocationMessage;
                return false;
            }
            AutoStartRequestError = null;
            return SaveAutoStartRequest(enabled is bool value ? new(value) : null);
        }
        finally
        {
            StateChanged?.Invoke();
        }
    }

    private bool SaveAutoStartRequest(AutoStartRequest? request)
    {
        if (settings.TrySaveAutoStartRequest(request))
            return true;
        AutoStartRequestError = "自動起動の予約を保存できません。設定ファイルを確認してください。";
        return false;
    }

    internal bool ApplyPendingAutoStart(Func<string, bool> configure, bool retry = false)
    {
        try
        {
            return TryApplyPendingAutoStart(configure, retry);
        }
        finally
        {
            StateChanged?.Invoke();
        }
    }

    private bool TryApplyPendingAutoStart(Func<string, bool> configure, bool retry)
    {
        if (settings.Current.PendingAutoStart is not { } request || request.Attempted && !retry)
            return false;
        // After an interrupted attempt, a newer SteamVR setting must survive until the user retries.
        if (!SaveAutoStartRequest(request with
        {
            Attempted = true
        }))
            return false;

        try
        {
            if (configure(request.Enabled ? "enable" : "disable") != request.Enabled)
                throw new InvalidOperationException("SteamVR の設定に変更が反映されませんでした。");
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            AutoStartRequestError = "予約した変更を反映できませんでした。\n" + e.Message;
            log("Autostart request failed: " + e.Message);
            return false;
        }
        AutoStartRequestError = null;
        if (!SaveAutoStartRequest(null))
            AutoStartRequestError = "SteamVR の設定は変更しましたが、予約の記録を消せませんでした。";
        return true;
    }
}
