namespace HandwriteMemo.App;

// Product branding is independent of the durable data location, including on a fresh install.
internal static class AppDataPaths
{
    internal static string MemoFolder(string documents) => Path.Combine(documents, "HandwriteMemo");
    internal static string ConfigPath(string localAppData) => Path.Combine(localAppData, "HandwriteMemo", "settings.json");
    internal static string LogFolder(string localAppData) => Path.Combine(localAppData, "HandwriteMemo", "logs");
    internal static string RecoveryFolder(string localAppData) => Path.Combine(localAppData, "HandwriteMemo", "Recovery");
}
