[assembly: DoNotParallelize]

namespace HandwriteMemo.Testing;

[TestClass]
public sealed class TestOutput
{
    private static string? s_previousDirectory;
    private static string? s_temporaryDirectory;

    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        var directory = Environment.GetEnvironmentVariable("HNMEMO_TEST_OUTPUT");
        if (string.IsNullOrWhiteSpace(directory))
        {
            s_temporaryDirectory = Path.Combine(Path.GetTempPath(), "HnMemo-tests-" + Guid.NewGuid().ToString("N"));
            directory = s_temporaryDirectory;
        }
        else
        {
            directory = Path.Combine(directory, typeof(TestOutput).Assembly.GetName().Name!);
        }

        s_previousDirectory = Directory.GetCurrentDirectory();
        Directory.CreateDirectory(Path.Combine(directory, "artifacts"));
        Directory.SetCurrentDirectory(directory);
#if WINDOWS
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        HandwriteMemo.App.Program.InitializeApplication();
#endif
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        if (s_previousDirectory is not null)
            Directory.SetCurrentDirectory(s_previousDirectory);
        if (s_temporaryDirectory is not null)
            Directory.Delete(s_temporaryDirectory, recursive: true);
    }
}
