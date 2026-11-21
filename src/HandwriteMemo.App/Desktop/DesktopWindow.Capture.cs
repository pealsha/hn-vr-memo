namespace HandwriteMemo.App;

internal sealed partial class DesktopWindow
{
    internal bool CaptureWithoutActivation;
    protected override bool ShowWithoutActivation => CaptureWithoutActivation || base.ShowWithoutActivation;
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            if (CaptureWithoutActivation)
                parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE: the capture window never takes foreground focus.
            return parameters;
        }
    }
}
