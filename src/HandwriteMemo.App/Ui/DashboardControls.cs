using HandwriteMemo.Core;
using System.Numerics;

namespace HandwriteMemo.App;

// One definition for the painted buttons and the OpenVR mouse hit regions.
internal static class DashboardControls
{
    public const int Width = 900, Height = 770, Count = 12;
    public static Rectangle Bounds(int index) => index < 3
        ? new(index * 300 + 8, 155, 284, 145)
        : index < 6 ? new(250 + (index - 3) * 214, 370, 202, 64)
        : index switch
        {
            6 => new(190, 452, 340, 64),
            7 => new(542, 452, 340, 64),
            8 => new(190, 534, 340, 64),
            9 => new(542, 534, 340, 64),
            10 => new(190, 616, 340, 64),
            11 => new(542, 616, 340, 64),
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
    public static float Brightness(int index) => index switch
    {
        3 => AppOptions.MaximumBoardBrightness,
        4 => AppOptions.MediumBoardBrightness,
        5 => AppOptions.MinimumBoardBrightness,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
    public static ToggleGestureMode Gesture(int index) => index switch
    {
        6 => ToggleGestureMode.WristButton,
        7 => ToggleGestureMode.Disabled,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
    public static WristButtonActivationMode Activation(int index) => index switch
    {
        8 => WristButtonActivationMode.DoubleTap,
        9 => WristButtonActivationMode.Touch,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
    public static string GestureLabel(ToggleGestureMode mode) => mode == ToggleGestureMode.WristButton ? "使う" : "使わない";
    public static string ActivationLabel(WristButtonActivationMode mode) => mode == WristButtonActivationMode.DoubleTap ? "2回タップ" : "1回タップ";

    public static int HitTest(Vector2 mouse, bool calibrationActive = true)
    {
        if (!float.IsFinite(mouse.X) || !float.IsFinite(mouse.Y))
            return -1;
        // OpenVR mouse coordinates start at the bottom left; GDI drawing starts at the top left.
        var point = new PointF(mouse.X, Height - mouse.Y);
        for (int i = 0; i < Count; i++)
            if ((i != 11 || calibrationActive) && ((RectangleF)Bounds(i)).Contains(point))
                return i;
        return -1;
    }
}
