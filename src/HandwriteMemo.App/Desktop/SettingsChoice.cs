using System.Runtime.InteropServices;

namespace HandwriteMemo.App;

/// <summary>A settings drop-down whose saved value changes only by an explicit choice.</summary>
/// <remarks>Windows sends the wheel to the control under the pointer, so scrolling the page past a closed
/// drop-down would otherwise switch and save its value. The wheel scrolls the page instead; an open list still scrolls.</remarks>
internal sealed class SettingsChoice : ComboBox
{
    private const int WmMouseWheel = 0x020A;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmMouseWheel && !DroppedDown)
        {
            // The parent's default handling passes the wheel up to the scrolling panel.
            if (Parent is { IsHandleCreated: true } parent)
                SendMessage(parent.Handle, m.Msg, m.WParam, m.LParam);
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
