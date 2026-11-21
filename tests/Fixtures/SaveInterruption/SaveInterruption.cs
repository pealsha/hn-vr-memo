using HandwriteMemo.Core;

var time = DateTimeOffset.UnixEpoch;
var document = new MemoDocument(2, Guid.NewGuid(), time, time.AddSeconds(4), BoardSize.Default,
    [new MemoStroke(time.AddSeconds(1), [new MemoPoint(.1f, .2f, 1)])]);

new MemoStore(args[0]).Save(document, _ =>
{
    // The parent kills this process after JSON replacement and before the PNG write.
    File.WriteAllText(args[1], "ready");
    Thread.Sleep(Timeout.Infinite);
});
