using System.Diagnostics;

namespace HandwriteMemo.App;

internal static class SteamVrRuntime
{
    internal static bool IsRunning() => IsProcessRunning("vrserver");

    internal static bool IsProcessRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }
}
