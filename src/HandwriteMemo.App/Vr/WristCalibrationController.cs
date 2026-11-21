using System.Numerics;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

internal sealed class WristCalibrationController(AppSettingsService settings, Action<string> log)
{
    private readonly WristCalibration _wristCalibration = new();
    private bool _calibrationWaiting;
    private bool _calibrationRetrying;
    private double _calibrationNoticeUntil;
    private string? _calibrationNotice;
    internal event Action? Changed;
    internal string? Notice
    {
        get => _calibrationNotice;
        private set
        {
            if (_calibrationNotice != value)
            {
                _calibrationNotice = value;
                Changed?.Invoke();
            }
        }
    }
    internal bool CanCalibrate { get; private set; }

    internal void SetConnected(bool connected, double seconds)
    {
        if (CanCalibrate == connected)
            return;
        CanCalibrate = connected;
        if (!connected)
            Cancel(seconds);
        Changed?.Invoke();
    }
    internal static string WristPlacement(HandSide dominant) => dominant == HandSide.Right
        ? "右の人差し指を、左手首のタップしたい位置に置きます。"
        : "左の人差し指を、右手首のタップしたい位置に置きます。";
    internal const string CalibrationPurpose = "タップが反応しにくいときは、指が触れる位置を登録できます。";
    internal string Guidance => !CanCalibrate ? "手首の位置合わせには SteamVR への接続が必要です。" :
        Notice ?? CalibrationPurpose;
    internal bool IsActive => _calibrationWaiting ||
        _wristCalibration.Status is WristCalibrationStatus.Preparing or WristCalibrationStatus.Collecting or WristCalibrationStatus.Retry;

    internal bool Start()
    {
        if (!CanCalibrate)
            return false;
        if (IsActive)
            return true;
        _wristCalibration.Cancel();
        _calibrationWaiting = true;
        _calibrationRetrying = false;
        _calibrationNoticeUntil = double.PositiveInfinity;
        Notice = "位置合わせを開始しました。";
        log("wrist calibration requested; awaiting dashboard close and tracked hands");
        return true;
    }

    internal void Update(bool dashboardVisible, bool tracked, BoardPose? wristGrip, Vector3 finger, double seconds)
    {
        var hand = settings.Current.Handedness == HandSide.Right ? HandSide.Left : HandSide.Right;
        if (IsActive && dashboardVisible)
        {
            // Dashboard input takes the controller poses away. Resume with fresh samples on close.
            _calibrationWaiting = true;
            Notice = "開始を受け付けました。SteamVR のメニューを閉じると測定が始まります。";
            return;
        }
        if (_calibrationWaiting)
        {
            _calibrationWaiting = false;
            _calibrationRetrying = false;
            _wristCalibration.Start(seconds, hand);
            Notice = "準備中（3秒）。" + WristPlacement(settings.Current.Handedness);
            return;
        }
        if (!IsActive)
        {
            if (seconds >= _calibrationNoticeUntil)
                Notice = null;
            return;
        }
        var previous = _wristCalibration.Status;
        var state = _wristCalibration.Update(seconds, hand,
            tracked && wristGrip is { } grip ? grip.WorldToLocal(finger) : null);
        if (state == WristCalibrationStatus.Retry)
            _calibrationRetrying = true;
        Notice = state switch
        {
            WristCalibrationStatus.Preparing => Notice,
            WristCalibrationStatus.Collecting => _calibrationRetrying ? "測定をやり直しています。そのまま2秒、指を止めてください。" :
                "測定中。そのまま2秒、指を止めてください。",
            WristCalibrationStatus.Retry => tracked && wristGrip.HasValue ? "指が動きました。同じ位置でもう一度、指を止めてください。" :
                "両手を検出できません。ヘッドセットの前でコントローラーを持ってください。",
            WristCalibrationStatus.TimedOut => "時間切れです。位置は保存していません。両手が映る状態で始めてください。",
            WristCalibrationStatus.Cancelled => "位置合わせを中止しました。",
            _ => Notice
        };
        if (state == WristCalibrationStatus.Succeeded && _wristCalibration.ResultCentre is { } centre)
        {
            try
            {
                var offset = new OffsetMeters(centre.X, centre.Y, centre.Z);
                Notice = settings.SaveWristTapCentre(offset) ? "✓ 手首の位置を保存しました。指を離してからタップできます。" :
                    "手首の位置を保存できません。PCの設定ファイルを確認し、もう一度開始してください。";
            }
            catch (ArgumentException)
            {
                Notice = "位置を読み取れませんでした。手の追跡を確認して、もう一度開始してください。";
            }
        }
        if (previous != state)
            log($"wrist calibration: {previous} -> {state}; centre={_wristCalibration.ResultCentre}; {Notice}");
        if (!IsActive)
            _calibrationNoticeUntil = seconds + 6;
    }

    internal void Cancel(double seconds)
    {
        if (!IsActive)
            return;
        _wristCalibration.Cancel();
        _calibrationWaiting = false;
        Notice = "位置合わせを中止しました。";
        _calibrationNoticeUntil = seconds + 6;
    }
}
