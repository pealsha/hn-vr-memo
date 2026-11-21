namespace HandwriteMemo.App;

// Shared foundation values; desktop surface brightness is a product-specific extension.
internal static class UiDesign
{
    internal const string FoundationVersion = "0.3.1";
    internal static readonly Color Plate = Color.FromArgb(185, 189, 199);
    internal static readonly Color Surface = Color.FromArgb(218, 218, 218);
    internal static readonly Color Ink = Color.FromArgb(31, 34, 41);
    internal static readonly Color Muted = Color.FromArgb(78, 82, 92);
    internal static readonly Color Border = Color.FromArgb(201, 205, 214);
    internal static readonly Color Control = Color.FromArgb(116, 121, 135);
    internal static readonly Color Accent = Color.FromArgb(122, 79, 176);
    internal static readonly Color AccentInk = Color.FromArgb(110, 69, 163);
    internal static readonly Color OnAccent = Color.FromArgb(244, 244, 244);
    internal static readonly Color Star = Color.FromArgb(138, 101, 0);
    internal static readonly Color Notice = Color.FromArgb(213, 197, 173);
    internal static readonly Color NoticeInk = Color.FromArgb(78, 57, 38);
    internal static readonly Color Error = Color.FromArgb(214, 186, 186);
    internal static readonly Color ErrorInk = Color.FromArgb(102, 46, 50);
    internal static readonly Color Shadow = Color.FromArgb(18, 26, 26, 26);
    internal static readonly Color DesktopCanvas = Color.FromArgb(243, 243, 243);
    internal static readonly Color DesktopSurface = Color.White;
    internal static readonly Color DesktopBorder = Color.FromArgb(218, 218, 218);
    internal static readonly Color DesktopHover = Color.FromArgb(246, 242, 250);
    internal static readonly Color DesktopPressed = Color.FromArgb(232, 222, 242);
    internal const float BodyPixels = 20;
    internal const float RadiusPerBody = .333f;
    internal const float PlateRadiusExtraPerBody = .417f;
    internal const float VrRadius = BodyPixels * RadiusPerBody;
    internal const float VrPlateRadius = BodyPixels * (RadiusPerBody + PlateRadiusExtraPerBody);
    internal const float DesktopRadius = 4.5f;
    internal const float PaperRadius = 8;
    internal const int GrainEveryPixels = 9;
    internal const int GrainWhiteAlpha = 34;
    internal const int GrainBlackAlpha = 16;
    internal const int SelectedGlowAlpha = 20;
    internal const int SelectedGlowGrowDivisor = 12;
    internal const int HaloWidth = 3;
    internal const int HaloAlpha = 14;
    internal const int ShadowOffsetY = 2;
    internal const int FocusWidth = 2;
    internal const int BorderWidth = 1;
}
