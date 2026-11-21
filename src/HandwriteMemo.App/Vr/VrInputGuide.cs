using HandwriteMemo.Core;

namespace HandwriteMemo.App;

// Explains the existing input guards; it does not change pickup or drawing rules.
internal static class VrInputGuide
{
    public static VrInputStatus Status(HandwriteEngine engine, MemoSession page, GrabScene scene, GrabFrameResult result,
        HandInput left, HandInput right, bool dashboardVisible, bool headTracked)
    {
        if (dashboardVisible)
            return new("ダッシュボードを閉じ、グリップを離して掴み直してください。", VrInputState.Dashboard);
        if (!headTracked)
            return new("追跡待ち。戻ったらグリップを離して掴み直してください。", VrInputState.HeadTracking);
        if (page.IsResizing)
            return new("大きさ変更中", VrInputState.Resizing);
        if (page.DesktopOwnsInput)
            return new("PCで筆記中。終わったらトリガーを離して押し直してください。", VrInputState.DesktopDrawing);
        bool hasTool = result.Pen.InputAvailable || result.Eraser.InputAvailable;
        if (hasTool && page.VrNeedsTriggerRelease)
            return new("", VrInputState.TriggerRelease);
        if (result.Pen.InputAvailable && page.VrEraserPickupHint)
            return result.Eraser.InputAvailable ? new("消しゴムを持った手のトリガーで消せます。", VrInputState.EraserReady) :
                new("消すにはペンを離し、消しゴムをグリップで掴んでください。", VrInputState.EraserPickup);
        if (engine.IsErasing)
            return new("消去中 / " + page.Status, VrInputState.Erasing);
        if (engine.IsWriting)
            return new("筆記中 / " + page.Status, VrInputState.Writing);
        if (!hasTool)
        {
            bool NeedsRelease(HandSide side, HandInput hand) => hand.IsTracked && hand.ButtonsValid && hand.GripDown && scene.NeedsGripRelease(side);
            bool l = NeedsRelease(HandSide.Left, left), r = NeedsRelease(HandSide.Right, right);
            if (l || r)
                return new((l && r ? "両手" : l ? "左手" : "右手") + "のグリップを一度離し、ペンか消しゴムを掴み直してください。", VrInputState.GripRelease);
            if (!left.IsTracked || !right.IsTracked || !left.ButtonsValid || !right.ButtonsValid)
                return new("手の追跡・入力待ち。戻ったらグリップを離して掴み直してください。", VrInputState.HandTracking);
        }
        // Ordinary pickup is explained by the highlight and dashboard; keep this band for state/recovery.
        return VrInputStatus.ForPage(page);
    }
}
