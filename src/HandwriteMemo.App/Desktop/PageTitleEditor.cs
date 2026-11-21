namespace HandwriteMemo.App;

// Native IME composition owns Enter/Esc until the composition is committed/cancelled.
internal sealed class PageTitleEditor : TextBox
{
    internal bool IsComposing { get; private set; }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x010D)
            IsComposing = true; // WM_IME_STARTCOMPOSITION
        if (m.Msg == 0x010E)
            IsComposing = false; // WM_IME_ENDCOMPOSITION
        base.WndProc(ref m);
    }
}
