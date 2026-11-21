namespace HandwriteMemo.App;

[STATestClass]
public sealed class FoundationCaptureTests
{
    [TestMethod]
    [TestCategory("Manual")]
    public void CaptureAndCompare()
    {
        string? output = Environment.GetEnvironmentVariable("HNMEMO_FOUNDATION_OUTPUT");
        if (string.IsNullOrWhiteSpace(output))
            Assert.Inconclusive("Set HNMEMO_FOUNDATION_OUTPUT with scripts/capture-foundation.ps1.");

        string after = Path.Combine(output, "after");
        FoundationCapture.Run(after);
        if (Environment.GetEnvironmentVariable("HNMEMO_FOUNDATION_BEFORE") is { Length: > 0 } before)
            FoundationComparison.Run(before, after, Path.Combine(output, "compare"));
    }
}
