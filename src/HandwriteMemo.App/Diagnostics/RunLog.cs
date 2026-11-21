using System.Diagnostics;
using System.Globalization;

namespace HandwriteMemo.App;

internal sealed class RunLog : IDisposable
{
    private const int MaxRetainedLogs = 20;
    private readonly StreamWriter _writer;
    private readonly List<double> _workTimes = [];
    private readonly List<double> _intervals = [];
    private readonly List<double> _renderTimes = [], _uploadTimes = [];
    private double _lastFrame;
    private double _lastReport;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public RunLog(string? folder = null)
    {
        folder ??= AppDataPaths.LogFolder(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(Path.GetFullPath(folder), $"memo-{DateTime.Now:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.log");
        _writer = new StreamWriter(path) { AutoFlush = true };
        Event("log=" + path);
        Rotate(folder, path);
    }

    private static void Rotate(string folder, string currentLog)
    {
        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("memo-*.log")
                         .Where(file => !string.Equals(file.FullName, currentLog, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(file => file.LastWriteTimeUtc)
                         .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                         .Skip(MaxRetainedLogs - 1))
            {
                try
                {
                    file.Delete();
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Event(string message)
    {
        var line = $"{_clock.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)}s {message}";
        _writer.WriteLine(line);
        Console.WriteLine(line);
    }

    public void Frame(double workMs, double seconds, bool visible, bool tracked, int strokes, double predictionMs = 0, string sync = "none",
        double renderMs = 0, double uploadMs = 0)
    {
        _workTimes.Add(workMs);
        _renderTimes.Add(renderMs);
        _uploadTimes.Add(uploadMs);
        if (_lastFrame > 0)
            _intervals.Add((seconds - _lastFrame) * 1000);
        _lastFrame = seconds;
        if (seconds - _lastReport < 5)
            return;
        _workTimes.Sort();
        _intervals.Sort();
        double loopHz = _workTimes.Count / (seconds - _lastReport);
        double meanWorkMs = _workTimes.Average();
        double p95WorkMs = _workTimes[(int)((_workTimes.Count - 1) * .95)];
        double p95IntervalMs = _intervals.Count == 0 ? 0 : _intervals[(int)((_intervals.Count - 1) * .95)];
        double meanRenderMs = _renderTimes.Average();
        double meanUploadMs = _uploadTimes.Average();
        Event(FormattableString.Invariant($"frames={_workTimes.Count} loopHz={loopHz:F1} workMs(mean/p95)={meanWorkMs:F2}/{p95WorkMs:F2} intervalMs(p95)={p95IntervalMs:F2} visible={visible} tracked={tracked} strokes={strokes} renderMs(mean)={meanRenderMs:F2} uploadMs(mean)={meanUploadMs:F2} predictionMs={predictionMs:F2} sync={sync}"));
        _workTimes.Clear();
        _intervals.Clear();
        _renderTimes.Clear();
        _uploadTimes.Clear();
        _lastReport = seconds;
    }

    public void Dispose() => _writer.Dispose();
}
