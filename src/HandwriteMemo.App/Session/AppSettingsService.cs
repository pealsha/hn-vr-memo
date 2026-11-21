using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[Flags]
internal enum SettingsChange
{
    Brightness = 1,
    Handedness = 2,
    Gesture = 4,
    Retention = 8,
    SaveFolder = 16,
    AutoStart = 32,
    WristCalibration = 64,
    WristHaptics = 128,
    Notice = 256
}

internal enum NotificationIcon { Warning, Error }

internal sealed record AppNotification(string Title, string Text, NotificationIcon Icon);

internal sealed class AppSettingsService(AppOptions initial, Action<string> log, string? configPath = null)
{
    internal AppOptions Current { get; private set; } = initial;
    internal string? Notice { get; private set; }
    internal event Action<SettingsChange>? Changed;
    internal event Action<AppNotification>? Notification;

    internal bool ChangeBrightness(float brightness)
    {
        if (!float.IsFinite(brightness) || brightness is < AppOptions.MinimumBoardBrightness or > AppOptions.MaximumBoardBrightness)
            throw new ArgumentOutOfRangeException(nameof(brightness));
        bool saved = Change(Current with { BoardBrightness = brightness }, SettingsChange.Brightness, "明るさ", showError: true);
        if (saved)
            log(FormattableString.Invariant($"boardBrightness={brightness:F2}; VR overlays only"));
        return saved;
    }

    internal bool ChangeHandedness(HandSide hand)
    {
        if (hand == Current.Handedness)
            return true;
        bool saved = Change(Current with { Handedness = hand }, SettingsChange.Handedness, "利き手", showError: true);
        if (saved)
            log("handedness=" + hand + "; recenter board/tools on next valid HMD pose");
        return saved;
    }

    internal bool ChangeGestureMode(ToggleGestureMode mode)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        return Change(Current with { GestureMode = mode }, SettingsChange.Gesture, "手首タップ");
    }

    internal bool ChangeWristButtonActivationMode(WristButtonActivationMode mode)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        return Change(Current with { WristButtonActivationMode = mode }, SettingsChange.Gesture, "タップ回数");
    }

    internal bool ChangeWristTapHaptics(bool enabled) =>
        Change(Current with { WristTapHapticsEnabled = enabled }, SettingsChange.WristHaptics, "合図の振動");

    internal bool ChangeRetention(int days)
    {
        if (!AppOptions.TrashRetentionChoices.Contains(days))
            throw new ArgumentOutOfRangeException(nameof(days));
        return Change(Current with { TrashRetentionDays = days }, SettingsChange.Retention, "ゴミ箱の自動削除");
    }

    internal bool SaveWristTapCentre(OffsetMeters offset)
    {
        AppOptions.ValidateWristTapCentre(offset);
        var next = Current.Handedness == HandSide.Right ? Current with { WristTapCentreRightHanded = offset } :
            Current with { WristTapCentreLeftHanded = offset };
        bool saved = TrySave(next, "手首の位置", reportNotice: false);
        if (saved)
            Changed?.Invoke(SettingsChange.WristCalibration);
        return saved;
    }

    internal bool TrySaveAutoStartRequest(AutoStartRequest? request)
    {
        if (Current.PendingAutoStart == request)
            return true;
        bool saved = TrySave(Current with { PendingAutoStart = request }, "自動起動の予約", reportNotice: false);
        if (saved)
            Changed?.Invoke(SettingsChange.AutoStart);
        return saved;
    }

    private bool Change(AppOptions next, SettingsChange change, string name, bool showError = false)
    {
        bool saved = TrySave(next, name);
        if (!saved && showError)
            Notification?.Invoke(new(name + "を保存できません", Notice!, NotificationIcon.Error));
        Changed?.Invoke(saved ? change : SettingsChange.Notice);
        return saved;
    }

    private bool TrySave(AppOptions next, string name, bool reportNotice = true)
    {
        try
        {
            next.Save(configPath ?? AppOptions.DefaultConfigPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (reportNotice)
                Notice = name + "を保存できません。設定ファイルを確認し、もう一度選んでください。\n" + e.Message;
            log("Settings save failed (" + name + "): " + e.Message);
            return false;
        }
        Current = next;
        if (reportNotice)
            Notice = null;
        return true;
    }

    internal bool ChangeSaveFolder(MemoSession page, string destination, bool move, double seconds, MemoFolderTransfer? transfer = null)
    {
        Notice = null;
        if (!page.CanNavigate() || !page.Save(seconds))
        {
            Notice = page.Notice ?? MemoSession.SaveFailureMessage;
            Changed?.Invoke(SettingsChange.Notice);
            return false;
        }
        transfer ??= new MemoFolderTransfer();
        bool committed = false;
        try
        {
            destination = MemoFolderTransfer.ValidateDestination(Current.SaveFolder, destination);
            if (move)
                transfer.CopyAndVerify(Current.SaveFolder, destination);
            var store = new MemoStore(destination);
            // A read-only catalog check also rejects broken destination pages when switching without moving.
            var catalog = new PageCatalog(destination);
            var entries = catalog.List();
            if (catalog.Errors.Count > 0)
                throw new IOException("新しい保存先に読めないページがあります:\n" + string.Join("\n", catalog.Errors.Select(e => e.Path)));
            // Seed the store's ID-to-path map even when an imported page has a noncanonical filename.
            if (move && entries.FirstOrDefault(e => e.Document.Id == page.Id) is { } active)
                store.Read(active.JsonPath);
            if (!TrySave(Current with
            {
                SaveFolder = destination
            }, "保存先"))
            {
                transfer.Rollback();
                Changed?.Invoke(SettingsChange.Notice);
                return false;
            }
            committed = true;
            page.UseFolder(store, catalog, move, seconds);
            if (move)
            {
                var failures = transfer.RemoveOriginals();
                if (failures.Count > 0)
                    Notice = "新しい保存先へ切り替えました。コピーは確認済みですが、次の元ファイルは削除できず残っています:\n" + string.Join("\n", failures);
            }
            log("Save folder changed: " + destination + "; moved=" + move);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (!committed)
                transfer.Rollback();
            Notice = (committed ? "保存先は切り替え済みです。新しいメモのコピーは残しています。\n" : "保存先を変更できませんでした。元のメモは残っています。\n") + e.Message;
            log("Save folder change failed: " + e.Message);
        }
        Changed?.Invoke(committed ? SettingsChange.SaveFolder : SettingsChange.Notice);
        return committed;
    }
}
