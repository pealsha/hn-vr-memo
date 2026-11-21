using System.Numerics;
using HandwriteMemo.Core;

[TestClass]
public sealed class WristCalibrationTests
{
    private const double FrameSeconds = 1d / 90;
    private static int Frames(double seconds) => (int)Math.Ceiling(seconds / FrameSeconds);
    [TestMethod]
    public void Success()
    {
        foreach (var hand in Enum.GetValues<HandSide>())
        {
            var c = new WristCalibration();
            c.Start(0, hand);
            Vector3 target = new(.14f, .19f, -.02f);
            Vector3 raw = hand == HandSide.Right ? target with { X = -target.X } : target;
            for (int i = 1; i <= Frames(WristCalibration.PreparationSeconds + WristCalibration.CollectionSeconds + 1) && c.Status != WristCalibrationStatus.Succeeded; i++)
            {
                double time = i * FrameSeconds;
                var point = time < WristCalibration.PreparationSeconds ? raw + Vector3.UnitY * (float)Math.Sin(time) : raw;
                c.Update(time, hand, point);
            }
            Check(c.Status == WristCalibrationStatus.Succeeded && c.ResultCentre is { } p && Vector3.Distance(p, target) < .001f,
                "stable target after preparation produces mirrored centre");
        }
    }
    [TestMethod]
    public void Retry()
    {
        var c = new WristCalibration();
        c.Start(0, HandSide.Left);
        int frame = Frames(WristCalibration.PreparationSeconds + .25);
        for (int i = 1; i <= frame; i++) c.Update(i * FrameSeconds, HandSide.Left, Vector3.Zero);
        Check(c.Update(++frame * FrameSeconds, HandSide.Left, Vector3.UnitX * (WristCalibration.MaximumMovementMetres * 2)) == WristCalibrationStatus.Retry,
            "large motion reports retry");
        Check(c.Update(++frame * FrameSeconds, HandSide.Left, null) == WristCalibrationStatus.Retry, "tracking loss reports retry");
        int finish = frame + Frames(WristCalibration.CollectionSeconds + .25);
        while (frame < finish) c.Update(++frame * FrameSeconds, HandSide.Left, Vector3.One * .1f);
        Check(c.Status == WristCalibrationStatus.Succeeded && Vector3.Distance(c.ResultCentre!.Value, Vector3.One * .1f) < .001f,
            "fresh stable samples succeed after retry");
    }
    [TestMethod]
    public void Interrupted()
    {
        var c = new WristCalibration();
        c.Start(0, HandSide.Left);
        c.Update(.1, HandSide.Right, Vector3.Zero);
        Check(c.Status == WristCalibrationStatus.Cancelled && c.ResultCentre is null, "hand change cancels");
        c.Start(0, HandSide.Left);
        c.Update(double.NaN, HandSide.Left, Vector3.Zero);
        Check(c.Status == WristCalibrationStatus.Cancelled, "invalid time cancels");
        c.Start(0, HandSide.Left);
        int frame = Frames(WristCalibration.PreparationSeconds + .25);
        for (int i = 1; i <= frame; i++) c.Update(i * FrameSeconds, HandSide.Left, Vector3.Zero);
        double afterGap = frame * FrameSeconds + WristCalibration.MaximumFrameGapSeconds * 2;
        Check(c.Update(afterGap, HandSide.Left, new Vector3(float.NaN)) == WristCalibrationStatus.Retry, "gap and invalid point retry");
        Check(c.Update(afterGap + FrameSeconds, HandSide.Left, new Vector3(float.PositiveInfinity)) == WristCalibrationStatus.Retry, "infinite point retries");
        c.Cancel();
        Check(c.Status == WristCalibrationStatus.Cancelled && c.ResultCentre is null, "cancel clears result");
        c.Start(0, HandSide.Left);
        for (int i = 1; i <= Frames(WristCalibration.TimeoutSeconds + .25); i++) c.Update(i * FrameSeconds, HandSide.Left, null);
        Check(c.Status == WristCalibrationStatus.TimedOut, "unavailable tracking times out");
    }
    [TestMethod]
    public void RandomMovement()
    {
        var random = new Random(37129);
        for (int run = 0; run < 50; run++)
        {
            var c = new WristCalibration();
            c.Start(0, HandSide.Left);
            for (int i = 1; i <= Frames(WristCalibration.TimeoutSeconds - .25); i++)
            {
                float x = ((float)random.NextDouble() - .5f) * .2f;
                c.Update(i * FrameSeconds, HandSide.Left, new Vector3(x, 0, 0));
                Check(c.Status != WristCalibrationStatus.Succeeded, $"random motion seed=37129 run={run} sample={i}");
            }
        }
    }
    [TestMethod]
    public void DiagonalOutside()
    {
        var g = new WristTapGesture();
        var head = new BoardPose(new(0, 1.5f, 0), Quaternion.Identity);
        var grip = new BoardPose(new(0, 1.35f, -.4f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2));
        var centre = WristTapGesture.ContactCentre;
        var diagonal = centre + new Vector3(WristTapGesture.ContactRadiusMetres * .8f);
        for (int i = 1; i <= 180; i++)
        {
            var local = i % 45 < 22 ? diagonal : centre + Vector3.UnitX * .2f;
            var result = g.Update(head, grip, HandSide.Left, grip.LocalToWorld(local), true, i * FrameSeconds,
                WristButtonActivationMode.Touch);
            Check(!result.Visible && !result.Triggered, "point inside cube but outside sphere does not count");
        }
    }
    [TestMethod]
    public void CentreChange()
    {
        var g = new WristTapGesture();
        var head = new BoardPose(new(0, 1.5f, 0), Quaternion.Identity);
        var grip = new BoardPose(new(0, 1.35f, -.4f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2));
        var centre = WristTapGesture.ContactCentre;
        var outside = centre + Vector3.UnitX * .2f;
        int frame = 0;
        WristTapState Step(Vector3 point, Vector3? currentCentre = null) =>
            g.Update(head, grip, HandSide.Left, grip.LocalToWorld(point), true, ++frame * FrameSeconds,
                WristButtonActivationMode.DoubleTap, currentCentre);
        for (int i = 0; i < Frames(WristTapGesture.LookMinimumSeconds + WristTapGesture.OutsideMinimumSeconds + .25); i++) Step(outside);
        Check(Step(centre).Pending, "first contact starts pending pair");
        var changed = Step(centre, centre + Vector3.UnitY * .1f);
        Check(changed.Events.HasFlag(WristTapEvent.Cancelled) && !changed.Pending && !changed.Triggered,
            "changing centre cancels old contact and pending pair");
    }
    [TestMethod]
    public void Sphere()
    {
        foreach (var side in Enum.GetValues<HandSide>())
        {
            var g = new WristTapGesture();
            var head = new BoardPose(new(0, 1.5f, 0), Quaternion.Identity);
            var grip = new BoardPose(new(0, 1.35f, -.4f), Quaternion.CreateFromAxisAngle(Vector3.UnitY,
                side == HandSide.Left ? -MathF.PI / 2 : MathF.PI / 2));
            Vector3 centre = new(.08f, .03f, .04f);
            for (int i = 1; i <= 400; i++)
            {
                Vector3 point = centre + Vector3.UnitX * (i % 50 < 25 ? .10f : .20f);
                if (side == HandSide.Right) point.X = -point.X;
                var state = g.Update(head, grip, side, grip.LocalToWorld(point), true, i / 90d,
                    WristButtonActivationMode.Touch, centre);
                Check(!state.Triggered && !state.Visible, "outside calibrated sphere cannot trigger");
            }
        }
    }
}
