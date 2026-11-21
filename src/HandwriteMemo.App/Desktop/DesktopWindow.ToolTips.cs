namespace HandwriteMemo.App;

internal sealed partial class DesktopWindow
{
    private void BindActionToolTips()
    {
        _toolTips.SetToolTip(_trash, "右クリックでゴミ箱を空にするメニューを開く");
        _toolTips.SetToolTip(_undo, "筆跡を元に戻す (紙・道具にフォーカスして Ctrl+Z)");
        _toolTips.SetToolTip(_redo, "元に戻した筆跡をやり直す (Ctrl+Y / Ctrl+Shift+Z)");
        _undo.AccessibleDescription = "Ctrl+Z";
        _redo.AccessibleDescription = "Ctrl+Y / Ctrl+Shift+Z";
        _toolTips.SetToolTip(_previous, _previous.AccessibleName);
        _toolTips.SetToolTip(_next, _next.AccessibleName);
        BindDisabledToolbarTips();
    }

    // Windows sends mouse input for a disabled button to its parent. Check from both the
    // group and the toolbar because the entire group may be disabled during preview.
    private void BindDisabledToolbarTips()
    {
        Button? candidate = null;
        bool showing = false;
        _disabledTipDelay.Interval = Math.Max(1, _toolTips.InitialDelay);

        Button? DisabledButtonAt(Point screenPoint)
        {
            var group = _tools.GetChildAtPoint(_tools.PointToClient(screenPoint), GetChildAtPointSkip.Invisible);
            return group?.GetChildAtPoint(group.PointToClient(screenPoint), GetChildAtPointSkip.Invisible)
                is Button { Enabled: false } button ? button : null;
        }

        void ResetTip()
        {
            _disabledTipDelay.Stop();
            candidate = null;
            if (showing)
            {
                showing = false;
                _disabledToolbarTips.Hide(_tools);
            }
        }

        void TrackPointer()
        {
            var hovered = DisabledButtonAt(MousePosition);
            if (ReferenceEquals(hovered, candidate))
                return;
            ResetTip();
            if (hovered is null)
                return;
            candidate = hovered;
            _disabledTipDelay.Start();
        }

        void BindPointerEvents(Control parent)
        {
            parent.MouseMove += (_, _) => TrackPointer();
            parent.MouseLeave += (_, _) => TrackPointer();
            parent.MouseDown += (_, _) => ResetTip();
        }

        BindPointerEvents(_tools);
        foreach (Control group in _tools.Controls)
        {
            BindPointerEvents(group);
            foreach (Button button in group.Controls)
                button.EnabledChanged += (_, _) =>
                {
                    if (ReferenceEquals(button, candidate))
                        ResetTip();
                };
        }
        Deactivate += (_, _) => ResetTip();
        _disabledTipDelay.Tick += (_, _) =>
        {
            _disabledTipDelay.Stop();
            if (candidate is not { } target || !ReferenceEquals(target, DisabledButtonAt(MousePosition)))
            {
                ResetTip();
                return;
            }
            var point = _tools.PointToClient(target.PointToScreen(new Point(0, target.Height + 4)));
            _disabledToolbarTips.Show(DisabledToolbarTip(target), _tools, point, _toolTips.AutoPopDelay);
            showing = true;
        };
    }

    private string DisabledToolbarTip(Button button)
    {
        string reason;
        if (IsSelectionMode)
            reason = "複数選択を終了すると使えます。";
        else if (_trash.Checked)
            reason = "ゴミ箱ではページのプレビューのみ表示します。";
        else if (_canvas.ReadOnly)
            reason = "ページを1件選ぶと使えます。";
        else if (button == _previous)
            reason = "前に開けるページがありません。";
        else if (button == _next)
            reason = "次に開けるページがありません。";
        else if (_page.IsBusy)
            reason = "筆記中は使えません。";
        else if (button == _undo)
            reason = "元に戻せる筆跡がありません。";
        else
            reason = "やり直せる筆跡がありません。";
        return button.AccessibleName is { Length: > 0 } name ? name + "\n" + reason : reason;
    }
}
