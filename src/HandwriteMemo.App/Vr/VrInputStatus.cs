namespace HandwriteMemo.App;

// Presentation state travels with its text; painters never inspect translated wording.
internal enum VrInputState
{
    Other,
    Page,
    Dashboard,
    HeadTracking,
    Resizing,
    DesktopDrawing,
    TriggerRelease,
    EraserReady,
    EraserPickup,
    Erasing,
    Writing,
    GripRelease,
    HandTracking,
    Undo
}

internal readonly record struct VrInputStatus(string Text, VrInputState State = VrInputState.Other, bool IsError = false)
{
    public bool ShowInDock => State is VrInputState.Dashboard or VrInputState.HeadTracking or
        VrInputState.DesktopDrawing or VrInputState.HandTracking or VrInputState.GripRelease or
        VrInputState.EraserPickup or VrInputState.EraserReady;

    public static VrInputStatus ForPage(MemoSession page) => new(page.Status, VrInputState.Page);
}
