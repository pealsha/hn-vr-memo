namespace HandwriteMemo.Core;

/// <summary>
/// Compatibility baselines for UV layout, the default physical page and exported PNG density.
/// These values also describe legacy v1 ink; changing UI resolution must not change them.
/// </summary>
public static class BoardMetrics
{
    public const int ReferenceWidthPixels = 1024;
    public const int ReferenceHeightPixels = 768;
    public const float ReferenceWidthMetres = .48f;
    public const float ReferenceHeightMetres = .36f;
    public const float ExportPixelsPerMetre = ReferenceWidthPixels / ReferenceWidthMetres;
}
