namespace HandwriteMemo.Testing;

internal static class TestAssert
{
    internal static void Check(bool condition, string message) => Assert.IsTrue(condition, message);
}
