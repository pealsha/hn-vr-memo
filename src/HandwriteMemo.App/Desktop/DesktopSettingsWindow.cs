using HandwriteMemo.Core;

namespace HandwriteMemo.App;

internal sealed class DesktopSettingsWindow : Form
{
    private readonly AppSettingsService _settings;
    private readonly AutoStartCoordinator _coordinator;
    private readonly WristCalibrationController _calibration;
    private readonly MemoSession _page;
    private bool _wasConnected;
    private readonly Func<double> _seconds;
    private readonly Func<string, bool> _autoStart;
    private readonly Action<string> _openFolder;
    private readonly Func<string?> _chooseFolder;
    private readonly Label _error = ErrorLabel();
    private readonly Label _autoError = ErrorLabel();
    private readonly TextBox _folder = new()
    {
        ReadOnly = true,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.FixedSingle,
        Dock = DockStyle.Top,
        BackColor = UiTheme.Surface,
        ForeColor = UiTheme.Secondary,
        AccessibleName = "現在の保存先",
        Margin = new(0, 0, 0, 6)
    };
    private readonly SettingsSwitch _auto = new() { AccessibleName = "SteamVR と一緒に起動" };
    private readonly ComboBox _retention = Choice("ゴミ箱の自動削除", ["しない", "1日", "14日", "30日", "60日"]);
    private readonly ComboBox _hand = Choice("利き手", ["右利き", "左利き"]);
    private readonly ComboBox _brightness = Choice("ノートの明るさ", ["100%", "80%", "60%"]);
    private readonly ComboBox _gesture = Choice("手首タップ", ["使う", "使わない"]);
    private readonly ComboBox _activation = Choice("タップ回数", ["2回タップ", "1回タップ"]);
    private readonly ComboBox _haptics = Choice("タップ時の振動", ["オフ", "オン"]);
    private readonly Label _tapHint = HintLabel();
    private readonly Button _calibrate = new DesignButton { Text = "手首の位置を合わせる", AutoSize = true };
    private readonly Button _cancelCalibration = new DesignButton { Text = "位置合わせを中止", AutoSize = true };
    private readonly Label _calibrationStatus = new() { AutoSize = true, Dock = DockStyle.Fill, Margin = new(0, 4, 0, 0), ForeColor = UiTheme.Secondary };
    private readonly Button _retryAuto = new DesignButton { Text = "再読み込み", AutoSize = true };
    private readonly ComboBox _pendingAuto = Choice("自動起動の変更を予約", ["予約しない", "オンにする", "オフにする"]);
    private readonly Button _retryPendingAuto = new DesignButton { Text = "再試行", AutoSize = true, Visible = false };
    private readonly Label _pendingAutoHint = ErrorLabel();
    private readonly Control _pendingAutoRow;
    private readonly Label _pendingAutoLabel;
    private bool _refreshing;
    private string? _autoQueryFailure;
    internal ComboBox RetentionChoice => _retention;
    internal ComboBox HandChoice => _hand;
    internal ComboBox BrightnessChoice => _brightness;
    internal ComboBox GestureChoice => _gesture;
    internal ComboBox ActivationChoice => _activation;
    internal ComboBox HapticsChoice => _haptics;
    internal Button CalibrateButton => _calibration.IsActive ? _cancelCalibration : _calibrate;
    internal string CalibrationStatusText => _calibrationStatus.Text;
    internal CheckBox AutoStartSwitch => _auto;
    internal Button RetryAutoButton => _retryAuto;
    internal ComboBox PendingAutoChoice => _pendingAuto;
    internal Button RetryPendingAutoButton => _retryPendingAuto;
    internal string PendingAutoHint => _pendingAutoHint.Text;
    internal string ErrorText => _error.Text;
    internal string AutoStartErrorText => _autoError.Text;
    internal string SaveFolderText => _folder.Text;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<string, string, FolderChangeChoice>? ConfirmFolderChange { get; set; }

    internal DesktopSettingsWindow(AppSettingsService settings, AutoStartCoordinator coordinator,
        WristCalibrationController calibration, MemoSession page, Func<double> seconds,
        Func<string, bool>? autoStart = null, Action<string>? openFolder = null, Func<string?>? chooseFolder = null)
    {
        _settings = settings;
        _coordinator = coordinator;
        _calibration = calibration;
        _page = page;
        _wasConnected = calibration.CanCalibrate;
        _seconds = seconds;
        _autoStart = autoStart ?? coordinator.ConfigureAutoStart;
        _openFolder = openFolder ?? (folder => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true }));
        _chooseFolder = chooseFolder ?? ChooseFolder;
        Text = "設定 — HnVRMemo";
        Icon = UiTheme.AppIcon;
        Font = UiTheme.DesktopFont;
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Text;
        ClientSize = new(700, 740);
        MinimumSize = new(620, 600);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var sections = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new(18, 8, 18, 16) };
        sections.ColumnStyles.Add(new(SizeType.Percent, 100));
        TableLayoutPanel Group(string name)
        {
            var group = new TableLayoutPanel
            {
                AccessibleRole = AccessibleRole.Grouping,
                AccessibleName = name,
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                BackColor = UiTheme.Surface,
                Padding = new(14, 8, 14, 8),
                Margin = new(0, 0, 0, 6)
            };
            group.ColumnStyles.Add(new(SizeType.Percent, 45));
            group.ColumnStyles.Add(new(SizeType.Percent, 55));
            sections.Controls.Add(group);
            return group;
        }
        void Section(string text) => sections.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Font = UiTheme.DesktopBoldFont,
            Margin = new(0, 10, 0, 5)
        });
        Section("VRのノート");
        var rows = Group("VRのノート");
        void Wide(Control control)
        {
            int row = rows.RowCount++;
            rows.RowStyles.Add(new(SizeType.AutoSize));
            rows.Controls.Add(control, 0, row);
            rows.SetColumnSpan(control, 2);
        }
        Label Row(string title, Control control, Label? description = null)
        {
            int row = rows.RowCount++;
            rows.RowStyles.Add(new(SizeType.AutoSize));
            var label = new Label { Text = title, AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new(0, 0, 8, 0) };
            if (description is null)
                rows.Controls.Add(label, 0, row);
            else
            {
                var caption = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1, Margin = new(0, 4, 12, 6) };
                caption.ColumnStyles.Add(new(SizeType.Percent, 100));
                caption.Controls.Add(label);
                caption.Controls.Add(description);
                rows.Controls.Add(caption, 0, row);
            }
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            if (description is not null)
                control.Anchor |= AnchorStyles.Top;
            control.Margin = new(0, 5, 0, 5);
            rows.Controls.Add(control, 1, row);
            return label;
        }
        Row("利き手", _hand);
        Row("ノートの明るさ", _brightness);
        Section("ノートを出す・隠す");
        rows = Group("手首タップ");
        Row("手首タップ", _gesture, _tapHint);
        Row("タップ回数", _activation);
        Row("タップ時の振動", _haptics);
        var calibrationActions = ActionRow(_calibrate, _cancelCalibration);
        calibrationActions.Margin = new(0, 8, 0, 0);
        Wide(calibrationActions);
        Wide(_calibrationStatus);
        rows = Group("コントローラーのボタン");
        Wide(new Label { Text = "コントローラーのボタン", AutoSize = true, Margin = Padding.Empty });
        Wide(HintLabel("SteamVR のコントローラ設定で、HnVRMemo の「ノートを出す・隠す」を割り当てます。\n手首タップを使わないときも、そのボタンで操作できます。"));
        Section("自動起動");
        rows = Group("自動起動");
        var autoRow = ActionRow(_auto, _retryAuto);
        Row("SteamVR と一緒に起動", autoRow);
        autoRow.Margin = new(0, 2, 0, 2);
        _autoError.Margin = new(0, 1, 0, 12);
        Wide(_autoError);
        _pendingAutoRow = ActionRow(_pendingAuto, _retryPendingAuto, fillFirst: true);
        _pendingAutoLabel = Row("自動起動の変更を予約", _pendingAutoRow);
        _pendingAutoLabel.Margin = new(0, 8, 8, 0);
        _pendingAutoRow.Margin = new(0, 8, 0, 0);
        void FitReservationRow() => _pendingAutoRow.MinimumSize = new(0, _pendingAuto.PreferredHeight + LogicalToDeviceUnits(4));
        _pendingAuto.FontChanged += (_, _) => FitReservationRow();
        _pendingAuto.DpiChangedAfterParent += (_, _) => FitReservationRow();
        FitReservationRow();
        _pendingAutoLabel.Visible = _pendingAutoRow.Visible = false;
        _pendingAutoHint.Margin = new(0, 2, 0, 0);
        Wide(_pendingAutoHint);
        Section("メモの管理");
        rows = Group("メモの保存先");
        Wide(new Label { Text = "保存先", AutoSize = true, Margin = new(0, 0, 0, 4) });
        Wide(_folder);
        _folder.TextChanged += (_, _) => FitFolderPath();
        _folder.SizeChanged += (_, _) => FitFolderPath();
        _folder.HandleCreated += (_, _) => FitFolderPath();
        var open = new DesignButton { Text = "保存先を開く", AutoSize = true };
        var change = new DesignButton { Text = "保存先を変更…", AutoSize = true };
        open.Click += (_, _) => Run(() => _openFolder(_settings.Current.SaveFolder));
        change.Click += (_, _) => Run(() =>
        {
            string? destination = _chooseFolder();
            if (destination is null)
                return;
            var choice = ConfirmFolderChange?.Invoke(_settings.Current.SaveFolder, destination) ?? AskFolderChange(destination);
            ChangeFolder(destination, choice);
        });
        Wide(ActionRow(open, change));
        rows = Group("ゴミ箱");
        Row("ゴミ箱の自動削除", _retention);
        Wide(HintLabel("期限を過ぎたメモは完全に削除され、元に戻せません。"));
        sections.Controls.Add(_error);
        scroll.Controls.Add(sections);
        Controls.Add(scroll);
        _retention.SelectedIndexChanged += (_, _) => Change(() => _settings.ChangeRetention(AppOptions.TrashRetentionChoices[_retention.SelectedIndex]));
        _hand.SelectedIndexChanged += (_, _) => Change(() => _settings.ChangeHandedness(_hand.SelectedIndex == 0 ? HandSide.Right : HandSide.Left));
        _brightness.SelectedIndexChanged += (_, _) => Change(() => _settings.ChangeBrightness(AppOptions.BoardBrightnessChoices[_brightness.SelectedIndex]));
        _gesture.SelectedIndexChanged += (_, _) => Change(() => _settings.ChangeGestureMode(_gesture.SelectedIndex == 0 ? ToggleGestureMode.WristButton : ToggleGestureMode.Disabled));
        _haptics.SelectedIndexChanged += (_, _) => Change(() => _settings.ChangeWristTapHaptics(_haptics.SelectedIndex == 1));
        _calibrate.Click += (_, _) => _calibration.Start();
        _cancelCalibration.Click += (_, _) => _calibration.Cancel(_seconds());
        _activation.SelectedIndexChanged += (_, _) => Change(() => _settings.ChangeWristButtonActivationMode((WristButtonActivationMode)_activation.SelectedIndex));
        _auto.CheckedChanged += (_, _) =>
        {
            if (!_refreshing)
                ChangeAutoStart(_auto.Checked);
        };
        _retryAuto.Click += (_, _) => RefreshAutoStart();
        _pendingAuto.SelectedIndexChanged += (_, _) =>
        {
            if (_refreshing || _pendingAuto.SelectedIndex < 0)
                return;
            bool? enabled = _pendingAuto.SelectedIndex == 0 ? null : _pendingAuto.SelectedIndex == 1;
            _coordinator.ChangeAutoStartRequest(enabled);
            RefreshAutoStart();
        };
        _retryPendingAuto.Click += (_, _) => RefreshAutoStart(retryPending: true);
        foreach (var choice in new[] { _retention, _hand, _brightness, _gesture, _activation, _haptics })
            choice.DropDownClosed += (_, _) =>
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(() =>
                    {
                        if (!IsDisposed)
                            RefreshValues();
                    });
            };
        _pendingAuto.DropDownClosed += (_, _) =>
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(() =>
                {
                    if (!IsDisposed)
                        RefreshPendingAutoStart();
                });
        };
        _settings.Changed += OnSettingsChanged;
        _calibration.Changed += OnCalibrationChanged;
        _coordinator.StateChanged += RefreshPendingAutoStart;
        Shown += (_, _) => RefreshAutoStart();
        RefreshValues();
    }

    private void FitFolderPath()
    {
        if (!_folder.IsHandleCreated || _folder.ClientSize.Width <= 0)
            return;
        const int MaxLines = 4;
        int lines = 1 + _folder.GetLineFromCharIndex(_folder.TextLength);
        _folder.Height = _folder.Font.Height * Math.Clamp(lines, 1, MaxLines) + _folder.LogicalToDeviceUnits(6);
    }

    private static ComboBox Choice(string name, string[] choices)
    {
        var combo = new SettingsChoice { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = name };
        combo.Items.AddRange(choices);
        return combo;
    }

    private static TableLayoutPanel ActionRow(Control first, Control second, bool fillFirst = false)
    {
        var row = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        row.ColumnStyles.Add(fillFirst ? new(SizeType.Percent, 100) : new(SizeType.AutoSize));
        row.ColumnStyles.Add(new(SizeType.AutoSize));
        first.Anchor = fillFirst ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Left;
        second.Anchor = AnchorStyles.Left;
        first.Margin = Padding.Empty;
        second.Margin = new(8, 0, 0, 0);
        row.Controls.Add(first, 0, 0);
        row.Controls.Add(second, 1, 0);
        return row;
    }

    private static Label ErrorLabel()
    {
        var label = new Label { AutoSize = true, Dock = DockStyle.Fill, ForeColor = UiTheme.ErrorInk, UseMnemonic = false, Visible = false, Margin = new(0, 4, 0, 0) };
        label.TextChanged += (_, _) => label.Visible = label.Text.Length > 0;
        return label;
    }

    private static Label HintLabel(string text = "") => new()
    {
        Text = text,
        AutoSize = true,
        Dock = DockStyle.Fill,
        ForeColor = UiTheme.Secondary,
        Margin = new(0, 4, 0, 0)
    };

    internal void RefreshValues()
    {
        _refreshing = true;
        try
        {
            var options = _settings.Current;
            ShowChoice(_retention, AppOptions.TrashRetentionChoices.ToList().IndexOf(options.TrashRetentionDays));
            ShowChoice(_hand, options.Handedness == HandSide.Right ? 0 : 1);
            ShowChoice(_brightness, AppOptions.BoardBrightnessChoices.ToList().FindIndex(v => Math.Abs(v - options.BoardBrightness) < .001f));
            ShowChoice(_gesture, options.GestureMode == ToggleGestureMode.WristButton ? 0 : 1);
            _tapHint.Text = options.Handedness == HandSide.Right
                ? "左手のタップ位置を見て、右の人差し指でタップします。"
                : "右手のタップ位置を見て、左の人差し指でタップします。";
            ShowChoice(_activation, (int)options.WristButtonActivationMode);
            _activation.Enabled = options.GestureMode == ToggleGestureMode.WristButton;
            ShowChoice(_haptics, options.WristTapHapticsEnabled ? 1 : 0);
            _haptics.Enabled = options.GestureMode == ToggleGestureMode.WristButton;
            RefreshCalibration();
            _folder.Text = options.SaveFolder;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void OnSettingsChanged(SettingsChange change)
    {
        if ((change & SettingsChange.AutoStart) != 0)
            return; // The coordinator publishes its final state after the reservation is handled.
        _error.Text = _settings.Notice ?? "";
        RefreshValues();
    }

    private void OnCalibrationChanged()
    {
        RefreshCalibration();
        if (_wasConnected == _calibration.CanCalibrate)
            return;
        _wasConnected = _calibration.CanCalibrate;
        if (Visible)
        {
            if (_wasConnected)
                RefreshAutoStart(applyPending: false);
            else
            {
                ShowAutoStartUnavailable(AutoStart.NotRunningMessage);
                RefreshPendingAutoStart();
            }
        }
    }

    private void RefreshCalibration()
    {
        _calibrationStatus.Text = _calibration.Guidance;
        _calibrate.Enabled = _calibration.CanCalibrate && !_calibration.IsActive;
        _cancelCalibration.Visible = _calibration.IsActive;
    }

    // A change from another surface must not move the highlight of a list the user has open.
    private static void ShowChoice(ComboBox choice, int index)
    {
        if (!choice.DroppedDown && choice.SelectedIndex != index)
            choice.SelectedIndex = index;
    }

    private void Change(Func<bool> action)
    {
        if (_refreshing)
            return;
        Run(() => _error.Text = action() ? "" : _settings.Notice ?? "設定を保存できませんでした。");
        RefreshValues();
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            _error.Text = e.Message;
        }
    }

    private void ChangeAutoStart(bool value)
    {
        if (_settings.Current.PendingAutoStart is not null && !_coordinator.ChangeAutoStartRequest(null))
        {
            RefreshAutoStart();
            return;
        }
        try
        {
            SetAutoState(_autoStart(value ? "enable" : "disable"));
            _autoError.Text = "";
            RefreshPendingAutoStart();
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            string failure = e.Message;
            RefreshAutoStart();
            _autoError.Text = failure + (string.IsNullOrEmpty(_autoError.Text) ? "" : "\n" + _autoError.Text);
        }
    }

    internal void RefreshAutoStart(bool retryPending = false, bool applyPending = true)
    {
        try
        {
            bool enabled = _autoStart("status");
            if (applyPending && _settings.Current.PendingAutoStart is { } request && (!request.Attempted || retryPending))
            {
                _coordinator.ApplyPendingAutoStart(_autoStart, retryPending);
                enabled = _autoStart("status");
            }
            SetAutoState(enabled);
            _autoError.Text = "";
            _autoQueryFailure = null;
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            ShowAutoStartUnavailable(e.Message);
        }
        _retryAuto.Visible = _autoQueryFailure is not null;
        RefreshPendingAutoStart();
    }

    private void ShowAutoStartUnavailable(string message)
    {
        _refreshing = true;
        try
        {
            _auto.Enabled = false;
            _auto.CheckState = CheckState.Indeterminate;
            _auto.Text = message == AutoStart.NotRunningMessage ? "SteamVR 停止中" : "読み取り失敗";
            _auto.AccessibleDescription = _auto.Text;
            _auto.AccessibleRole = AccessibleRole.StaticText;
        }
        finally
        {
            _refreshing = false;
        }
        _autoError.Text = message == AutoStart.NotRunningMessage ? message : "自動起動のオン・オフを読み取れません。" + Environment.NewLine + message;
        _autoError.ForeColor = message == AutoStart.NotRunningMessage ? UiTheme.Secondary : UiTheme.ErrorInk;
        _autoQueryFailure = message;
        _retryAuto.Visible = true;
    }

    private void RefreshPendingAutoStart()
    {
        var request = _settings.Current.PendingAutoStart;
        string? error = _coordinator.AutoStartRequestError;
        bool available = _autoQueryFailure is null;
        _pendingAutoLabel.Visible = _pendingAutoRow.Visible = !available || request is not null;
        _retryPendingAuto.Visible = available && request is { Attempted: true };
        _refreshing = true;
        try
        {
            ShowChoice(_pendingAuto, request is null ? 0 : request.Enabled ? 1 : 2);
        }
        finally
        {
            _refreshing = false;
        }
        _pendingAutoHint.ForeColor = error is null ? UiTheme.Secondary : UiTheme.ErrorInk;
        _pendingAutoHint.Text = error ?? (request switch
        {
            { Attempted: true } => "前回の反映を確認できません。再試行するか、予約を取り消してください。",
            not null => "予約済み。HnVRMemo と SteamVR が次に接続したときに反映します。",
            _ => available ? "" : "HnVRMemo と SteamVR が次に接続したときに反映します。"
        });
    }

    private void SetAutoState(bool value)
    {
        _refreshing = true;
        try
        {
            _auto.Enabled = true;
            _auto.AccessibleRole = AccessibleRole.CheckButton;
            _auto.AccessibleDescription = null;
            _auto.CheckState = value ? CheckState.Checked : CheckState.Unchecked;
            _auto.Text = value ? "オン" : "オフ";
            _autoError.ForeColor = UiTheme.ErrorInk;
            _auto.Invalidate();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private string? ChooseFolder()
    {
        using var picker = new FolderBrowserDialog { Description = "メモの保存先", UseDescriptionForTitle = true, SelectedPath = _settings.Current.SaveFolder };
        return picker.ShowDialog(this) == DialogResult.OK ? picker.SelectedPath : null;
    }

    private FolderChangeChoice AskFolderChange(string destination)
    {
        using var dialog = new FolderChangeDialog(_settings.Current.SaveFolder, destination);
        dialog.ShowDialog(this);
        return dialog.Choice;
    }

    internal bool ChangeFolder(string destination, FolderChangeChoice choice)
    {
        if (choice == FolderChangeChoice.Cancel)
            return false;
        bool result = _settings.ChangeSaveFolder(_page, destination, choice == FolderChangeChoice.Move, _seconds());
        _error.Text = _settings.Notice ?? "";
        RefreshValues();
        return result;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiTheme.ApplyWindow(this);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _settings.Changed -= OnSettingsChanged;
            _calibration.Changed -= OnCalibrationChanged;
            _coordinator.StateChanged -= RefreshPendingAutoStart;
        }
        base.Dispose(disposing);
    }
}

internal enum FolderChangeChoice
{
    Cancel,
    Move,
    Switch
}

internal sealed class FolderChangeDialog : Form
{
    internal FolderChangeChoice Choice { get; private set; }
    internal Button MoveButton { get; } = new DesignButton { Text = "移して変更", AutoSize = true };
    internal Button SwitchButton { get; } = new DesignButton { Text = "移さず変更", AutoSize = true };
    internal Button CancelChoiceButton { get; } = new DesignButton { Text = "キャンセル", AutoSize = true };
    internal FolderChangeDialog(string source, string destination)
    {
        Text = "保存先を変更";
        Font = UiTheme.DesktopFont;
        BackColor = UiTheme.Background;
        ClientSize = new(650, 520);
        MinimumSize = new(620, 480);
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(24), ColumnCount = 1, RowCount = 4 };
        body.ColumnStyles.Add(new(SizeType.Percent, 100));
        body.RowStyles.Add(new(SizeType.AutoSize));
        body.RowStyles.Add(new(SizeType.Percent, 100));
        body.RowStyles.Add(new(SizeType.AutoSize));
        body.RowStyles.Add(new(SizeType.AutoSize));
        body.Controls.Add(new Label { Text = "今のメモを新しい保存先へ移しますか？", AutoSize = true, Font = UiTheme.DesktopBoldFont, Margin = new(0, 0, 0, 12) });
        body.Controls.Add(new TextBox
        {
            Text = "今の保存先\r\n" + source + "\r\n\r\n新しい保存先\r\n" + destination,
            ReadOnly = true,
            Multiline = true,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = UiTheme.Background,
            ScrollBars = ScrollBars.Vertical
        });
        body.Controls.Add(new Label
        {
            Text = "移す場合はゴミ箱も含めてコピーを確かめてから、元を削除します。\n移さない場合は今のメモを元の場所に残します。\n新しい場所のメモも表示します。同じページがあれば移動を中止します。",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new(0, 12, 0, 12)
        });
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange([CancelChoiceButton, SwitchButton, MoveButton]);
        body.Controls.Add(buttons);
        Controls.Add(body);
        CancelChoiceButton.DialogResult = DialogResult.Cancel;
        CancelButton = CancelChoiceButton;
        AcceptButton = CancelChoiceButton;
        MoveButton.Click += (_, _) =>
        {
            Choice = FolderChangeChoice.Move;
            DialogResult = DialogResult.OK;
            Close();
        };
        SwitchButton.Click += (_, _) =>
        {
            Choice = FolderChangeChoice.Switch;
            DialogResult = DialogResult.OK;
            Close();
        };
        Shown += (_, _) => CancelChoiceButton.Focus();
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiTheme.ApplyWindow(this);
    }
}

// Standard CheckBox keyboard and accessibility behavior, painted as an immediate on/off switch.
internal sealed class SettingsSwitch : CheckBox
{
    internal SettingsSwitch()
    {
        AutoSize = true;
        CheckState = CheckState.Indeterminate;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int trackWidth = CheckState == CheckState.Indeterminate ? 0 : LogicalToDeviceUnits(52);
        return new(TextRenderer.MeasureText(Text, Font).Width + trackWidth, LogicalToDeviceUnits(36));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        const TextFormatFlags Flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
        if (CheckState == CheckState.Indeterminate)
        {
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, UiTheme.Secondary, Flags);
            return;
        }
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        var track = new RectangleF(2 * scale, (Height - 20 * scale) / 2, 42 * scale, 20 * scale);
        UiTheme.Tile(e.Graphics, track, Enabled && Checked ? UiTheme.Accent : Color.LightGray, Color.Gray, 1, 10 * scale);
        using var brush = new SolidBrush(Color.White);
        int knobOffset = Checked ? 24 : 4;
        e.Graphics.FillEllipse(brush, track.X + knobOffset * scale, track.Y + 4 * scale, 12 * scale, 12 * scale);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle((int)(52 * scale), 0, Width - (int)(52 * scale), Height),
            Enabled ? ForeColor : UiTheme.Secondary, Flags);
        if (Focused)
            ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
    }
}
