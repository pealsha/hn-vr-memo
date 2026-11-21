using System.Globalization;
using Valve.VR;

namespace HandwriteMemo.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string mode = "vr";
        try
        {
            string? config = null, auto = null;
            double? duration = null;
            bool show = false, desktop = false, background = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--help":
                        PrintHelp();
                        return 0;
                    case "--diagnose":
                        SetMode("diagnose");
                        break;
                    case "--autostart":
                        SetMode("autostart");
                        auto = Value();
                        break;
                    case "--config":
                        config = Value();
                        break;
                    case "--show":
                        show = true;
                        break;
                    case "--desktop":
                        desktop = true;
                        break;
                    case "--background":
                        background = true;
                        break;
                    case "--duration":
                        duration = double.Parse(Value(), CultureInfo.InvariantCulture);
                        if (!double.IsFinite(duration.Value) || duration <= 0)
                            throw new ArgumentException("--duration must be positive seconds.");
                        break;
                    default:
                        throw new ArgumentException("Unknown argument: " + args[i]);
                }
                string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
            }
            if (mode == "autostart")
                return AutoStart.Configure(auto!);
            if (mode == "diagnose")
            {
                Console.WriteLine($"OpenVR runtime installed: {OpenVR.IsRuntimeInstalled()}");
                Console.WriteLine($"OpenVR HMD present: {OpenVR.IsHmdPresent()}");
                return 0;
            }
            return AppHost.Launch(config, duration, show, desktop, background);

            void SetMode(string value)
            {
                if (mode != "vr")
                    throw new ArgumentException("Choose only one utility operation.");
                mode = value;
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"ERROR: {e.GetType().Name}: {e.Message}");
            if (mode == "vr")
                MessageBox.Show(e.Message, "HnMemo — error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static void InitializeApplication() => ApplicationConfiguration.Initialize();

    private static void PrintHelp() => Console.WriteLine($"""
        HnMemo {Application.ProductVersion} (Windows x64 / .NET 10)
        No options              Desktop without SteamVR; hidden overlay when SteamVR is running.
        --desktop               Open desktop window even when SteamVR is running.
        --background            SteamVR auto-launch: keep desktop hidden until requested.
        --config PATH           Load settings JSON (default: LocalAppData/HandwriteMemo/settings.json).
        --show                  Show board immediately; valid HMD tracking required.
        --autostart enable      Register this installation to launch with SteamVR.
        --autostart disable     Disable and unregister SteamVR autostart.
        --autostart status      Print current SteamVR autostart setting.
        --diagnose              Read OpenVR installation/HMD availability.
        --duration N            Development check: exit after N seconds.
        Hold GRIP to pick up pen/board/eraser freely. TRIGGER draws/erases at projected tip.
        Board: UNDO / NEW / LIST / PREV / NEXT / PIN / CLOSE. LIST shows saved pages.
        Bottom palette: color, width, stroke/partial erase. Release trigger after opening a page.
        Saving is automatic.
        Dashboard: SHOW/HIDE / RECENTER / EXIT. Tray menu: open desktop window / open save folder / exit.
        Grip/trigger still reach VRChat. No suppression is attempted.
        """);
}
