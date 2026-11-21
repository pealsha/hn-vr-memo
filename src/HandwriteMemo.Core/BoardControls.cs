using System.Numerics;

namespace HandwriteMemo.Core;

/// <summary>VR board buttons and list cards, shared by painting and fresh-press hit testing.</summary>
public static class BoardControls
{
    public const int PagesPerScreen = 3;
    public const int DockTopPixels = 216;
    private const float ButtonTop = (DockTopPixels - ToolPalette.DockHeightPixels - ToolPalette.DockGapPixels) / (float)BoardMetrics.ReferenceHeightPixels;
    public const float ButtonBottom = ButtonTop + .097f;
    public static IReadOnlyList<BoardCommand> EditorButtons { get; } = Array.AsReadOnly(new[]
    { BoardCommand.Undo, BoardCommand.Redo, BoardCommand.PageList, BoardCommand.PreviousPage,
        BoardCommand.NextPage, BoardCommand.TogglePin, BoardCommand.NewPage, BoardCommand.Close });
    public static IReadOnlyList<BoardCommand> ListButtons { get; } = Array.AsReadOnly(new[]
    { BoardCommand.BackToPage, BoardCommand.NewPage, BoardCommand.RefreshList, BoardCommand.Close });
    private static readonly int[] s_editorLeft = [0, 96, 192, 344, 456, 568, 720, 888];
    private static readonly int[] s_editorWidth = [88, 88, 144, 104, 104, 144, 160, 128];

    // Reference UV uses the standard 48 x 36 cm board, also when the paper grows.
    public static (float Left, float Top, float Width, float Height) ButtonBounds(int index, bool pageList)
    {
        var buttons = pageList ? ListButtons : EditorButtons;
        if (index < 0 || index >= buttons.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return pageList ? ((index * 256 + 4) / (float)BoardMetrics.ReferenceWidthPixels, ButtonTop, 248 / (float)BoardMetrics.ReferenceWidthPixels, .097f)
            : ((s_editorLeft[index] + 4) / (float)BoardMetrics.ReferenceWidthPixels, ButtonTop, s_editorWidth[index] / (float)BoardMetrics.ReferenceWidthPixels, .097f);
    }

    public static (float Left, float Top, float Width, float Height) CardBounds(int row)
    {
        if (row is < 0 or >= PagesPerScreen)
            throw new ArgumentOutOfRangeException(nameof(row));
        return (.02f + row * .325f, .02f, .31f, .80f);
    }

    public static int CardAt(Vector2 uv)
    {
        for (int i = 0; i < PagesPerScreen; i++)
        {
            var r = CardBounds(i);
            if (uv.X >= r.Left && uv.X <= r.Left + r.Width && uv.Y >= r.Top && uv.Y <= r.Top + r.Height)
                return i;
        }
        return -1;
    }

    public static Vector2 ReferenceUv(Vector2 uv, BoardSize size) => new(
        uv.X * size.WidthMetres / BoardSize.Default.WidthMetres,
        uv.Y * size.HeightMetres / BoardSize.Default.HeightMetres);

    public static int CardAt(Vector2 uv, BoardSize size) => CardAt(ReferenceUv(uv, size));
    public static BoardCommand? HitTest(Vector2 uv, bool pageList, BoardSize size) => HitTest(ReferenceUv(uv, size), pageList);

    public static BoardCommand? HitTest(Vector2 uv, bool pageList)
    {
        if (uv.X is < 0 or > 1 || uv.Y > 1)
            return null;
        if (uv.Y <= ButtonBottom)
        {
            var buttons = pageList ? ListButtons : EditorButtons;
            for (int i = 0; i < buttons.Count; i++)
            {
                var r = ButtonBounds(i, pageList);
                if (uv.X >= r.Left && uv.X <= r.Left + r.Width && uv.Y >= r.Top && uv.Y <= r.Top + r.Height)
                    return buttons[i];
            }
            return null;
        }
        if (!pageList)
            return null;
        if (CardAt(uv) >= 0)
            return BoardCommand.OpenPage;
        if (uv.Y is >= .87f and <= .98f)
        {
            if (uv.X is >= .02f and <= .24f)
                return BoardCommand.PreviousPage;
            if (uv.X is >= .76f and <= .98f)
                return BoardCommand.NextPage;
        }
        return null;
    }
}
