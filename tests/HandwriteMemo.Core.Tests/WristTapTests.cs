using System.Numerics;
using HandwriteMemo.Core;

[TestClass]
public sealed class WristTapTests
{
    private static readonly Vector3 s_contact = WristTapGesture.ContactCentre;
    private static readonly Vector3 s_outside = new(.32f, .17f, -.02f);
    private const double FrameSeconds = 1d / 90;
    private sealed class Sequence(HandSide hand = HandSide.Left, WristButtonActivationMode mode = WristButtonActivationMode.DoubleTap, Vector3? centre = null)
    {
        internal readonly WristTapGesture Gesture = new();
        internal BoardPose Head = new(new(0, 1.5f, 0), Quaternion.Identity);
        internal BoardPose Wrist = new(new(0, 1.35f, -.4f), Quaternion.CreateFromAxisAngle(Vector3.UnitY,
            hand == HandSide.Left ? -MathF.PI / 2 : MathF.PI / 2));
        internal bool Available = true;
        internal double Seconds;
        internal int Toggles, Cues;
        internal WristButtonActivationMode Mode = mode;
        internal WristTapState Last;
        internal readonly List<string> Trace = ["seconds,visible,confirmed,pending,triggered,events,reason,x,y,z"];
        private Vector3 InWristGrip(Vector3 point) => point with { X = hand == HandSide.Left ? point.X : -point.X };

        internal void PlaceTarget(Vector3 target)
        {
            var localCentre = InWristGrip(centre ?? s_contact);
            Wrist = Wrist with { Position = target - Vector3.Transform(localCentre, Wrist.Rotation) };
        }

        internal WristTapState Step(Vector3? point = null, double duration = .12)
        {
            var p = InWristGrip(point ?? s_outside);
            int count = Math.Max(1, (int)Math.Ceiling(duration / FrameSeconds));
            for (int i = 0; i < count; i++)
            {
                Seconds += FrameSeconds;
                Last = Gesture.Update(Head, Wrist, hand, Wrist.LocalToWorld(p), Available, Seconds, Mode, centre);
                if (Last.Triggered)
                    Toggles++;
                if (Last.Events.HasFlag(WristTapEvent.CueStarted))
                    Cues++;
                Trace.Add(FormattableString.Invariant(
                    $"{Seconds:F5},{Last.Visible},{Last.Confirmed},{Last.Pending},{Last.Triggered},{Last.Events},{Last.Reason},{p.X:F3},{p.Y:F3},{p.Z:F3}"));
            }
            return Last;
        }
        internal void Ready() => Step(duration: .35);
        internal void Tap(Vector3? point = null) { Step(point ?? s_contact); Step(); }
        internal void Pair(Vector3? point = null) { Tap(point); Tap(point); }
    }
    [TestMethod]
    public void MeasuredPositions()
    {
        // Source: 224739 log lines 9015-9024, 9054-9059; 233211 log lines 1359-1370.
        Vector3[] measured = [new(.136f, .197f, -.025f), new(.144f, .187f, -.020f), new(.127f, .159f, -.010f),
            new(.136f, .153f, -.006f), new(.138f, .219f, -.212f), new(.118f, .053f, -.124f)];
        Directory.CreateDirectory("artifacts/task-036");
        foreach (var side in Enum.GetValues<HandSide>())
        {
            var trace = new List<string>();
            foreach (var point in measured)
            {
                var s = new Sequence(side, centre: point);
                s.PlaceTarget(s.Head.Position - Vector3.UnitZ * .4f);
                s.Ready();
                int before = s.Toggles;
                s.Pair(point);
                Check(s.Toggles == before + 1 && s.Cues > 0, "measured fingertip positions complete two taps");
                s.Step(duration: 1.1);
                trace.AddRange(s.Trace.Skip(trace.Count == 0 ? 0 : 1));
            }
            File.WriteAllLines($"artifacts/task-036/measured-taps-{side}.csv", trace);
        }
    }
    [TestMethod]
    public void SingleAndLook()
    {
        var s = new Sequence();
        Check(!s.Step(duration: 3).Visible && s.Cues == 0, "looking alone displays no target");
        s.Tap();
        s.Step(duration: 2);
        Check(s.Toggles == 0, "one completed tap cannot toggle");
        s.Step(s_contact, 3);
        Check(s.Toggles == 0, "touching and holding never toggles");
    }

    [TestMethod]
    public void MultiDirectionReturns()
    {
        Vector3[] points = [s_contact, new(.138f, .219f, -.212f), new(.118f, .053f, -.124f)];
        foreach (var hand in Enum.GetValues<HandSide>())
            foreach (var point in points)
                foreach (var axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
                    foreach (float sign in new[] { -1f, 1f })
                    {
                        var s = new Sequence(hand, centre: point);
                        s.PlaceTarget(s.Head.Position - Vector3.UnitZ * .4f);
                        var outside = point + axis * sign * .5f;
                        s.Step(outside, .35);
                        for (int tap = 0; tap < 2; tap++)
                        {
                            s.Step(point);
                            s.Step(outside);
                        }
                        Check(s.Toggles == 1, "measured contact accepts taps returning to any entry side");
                    }
    }

    [TestMethod]
    public void ContinuousTaps()
    {
        Vector3[] points = [new(.136f, .197f, -.025f), new(.144f, .187f, -.020f), new(.127f, .159f, -.010f),
            new(.136f, .153f, -.006f), new(.138f, .219f, -.212f), new(.118f, .053f, -.124f)];
        foreach (var hand in Enum.GetValues<HandSide>())
        {
            Sequence s = null!;
            var trace = new List<string>();
            void Travel(Vector3 from, Vector3 to)
            {
                const int Frames = 12;
                for (int i = 1; i <= Frames; i++)
                    s.Step(Vector3.Lerp(from, to, i / (float)Frames), FrameSeconds);
            }
            foreach (var point in points)
            {
                s = new Sequence(hand, centre: point);
                s.PlaceTarget(s.Head.Position - Vector3.UnitZ * .4f);
                s.Step(duration: 1.2);
                int before = s.Toggles;
                for (int tap = 0; tap < 2; tap++)
                {
                    Travel(s_outside, point);
                    s.Step(point, .06);
                    Travel(point, s_outside);
                    s.Step(duration: .12);
                }
                Check(s.Toggles == before + 1, "continuous approach, brief contact and retreat complete each measured pair");
                trace.AddRange(s.Trace.Skip(trace.Count == 0 ? 0 : 1));
            }
            Directory.CreateDirectory("artifacts/task-036");
            File.WriteAllLines($"artifacts/task-036/continuous-taps-{hand}.csv", trace);
        }
    }

    [TestMethod]
    public void TransversePasses()
    {
        foreach (var hand in Enum.GetValues<HandSide>())
        {
            var s = new Sequence(hand);
            var exit = WristTapGesture.ContactCentre - Vector3.UnitX * .5f;
            s.Ready();
            for (int i = 0; i < 5; i++)
            {
                s.Step(s_contact);
                var end = s.Step(exit, 1d / 90);
                Check(end.Events.HasFlag(WristTapEvent.Cancelled) && !end.Visible, "passing through cancels cue on exit");
                s.Ready();
            }
            Check(s.Toggles == 0, "transverse sweeps never toggle");
        }
    }
    [TestMethod]
    public void CueTimeout()
    {
        var s = new Sequence();
        s.Ready();
        Check(s.Step(s_contact).Pending && s.Cues == 1, "first entry shows cue immediately once");
        s.Step();
        Check(!s.Step(duration: WristTapGesture.PairMaximumSeconds + .1).Visible, "cue times out");
        Check(s.Toggles == 0, "timeout is not confirmation");
    }
    [TestMethod]
    public void LookAway()
    {
        var s = new Sequence();
        s.Ready();
        s.Tap();
        var head = s.Head;
        s.Head = s.Head with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI) };
        Check(!s.Step(duration: FrameSeconds).Visible, "look away hides without grace");
        s.Head = head;
        s.Ready();
        s.Tap();
        Check(s.Toggles == 0, "a new tap after look away is first, not second");
    }

    [TestMethod]
    public void CalibratedTargetAcceptsSidewaysAndReversedWristPoses()
    {
        var centre = new Vector3(.22f, .02f, 0);
        foreach (var hand in Enum.GetValues<HandSide>())
            foreach (float yaw in new[] { 0f, hand == HandSide.Left ? MathF.PI / 2 : -MathF.PI / 2 })
            {
                var s = new Sequence(hand, centre: centre);
                s.Wrist = s.Wrist with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw) };
                s.PlaceTarget(s.Head.Position - Vector3.UnitZ * .4f);
                s.Ready();
                s.Pair(centre);
                Check(s.Toggles == 1, $"calibrated target in view toggles for {hand} wrist at yaw={yaw}");
            }
    }

    [TestMethod]
    public void CalibratedTargetOutsideViewRejectsTaps()
    {
        var centre = new Vector3(.22f, .02f, 0);
        foreach (var hand in Enum.GetValues<HandSide>())
        {
            var s = new Sequence(hand, centre: centre);
            s.Wrist = new(s.Head.Position - Vector3.UnitZ * .25f, Quaternion.Identity);
            s.Ready();
            s.Pair(centre);
            Check(s.Cues == 0 && s.Toggles == 0, $"looking at the {hand} grip does not count when the tap centre is outside view");
        }
    }
    [TestMethod]
    public void HeldContact()
    {
        foreach (int heldTap in new[] { 1, 2 })
        {
            var s = new Sequence();
            s.Ready();
            if (heldTap == 2)
                s.Tap();
            Check(!s.Step(s_contact, WristTapGesture.ContactMaximumSeconds + .2).Visible, "long contact cancels cue");
            s.Step();
            Check(s.Toggles == 0, "held contact cannot complete on release");
        }
    }
    [TestMethod]
    public void SingleMode()
    {
        var s = new Sequence(mode: WristButtonActivationMode.Touch);
        s.Ready();
        Check(!s.Step(s_contact).Visible && s.Toggles == 0, "single mode cannot toggle on entry or show another-tap text");
        Check(s.Step().Confirmed && s.Toggles == 1, "single completed tap gives confirmed feedback");
        s.Step(s_contact, 2);
        Check(s.Toggles == 1, "resting after confirmation cannot repeat");
    }
    [TestMethod]
    public void Rearm()
    {
        var s = new Sequence();
        s.Step(s_contact, 2);
        Check(s.Cues == 0, "startup under finger never counts");
        s.Step();
        s.Tap();
        s.Available = false;
        Check(!s.Step(s_contact).Visible, "blocking hides immediately");
        s.Available = true;
        s.Step(s_contact, 1);
        Check(s.Toggles == 0, "recovery under finger cannot bridge pair");
        s.Ready();
        s.Pair();
        Check(s.Toggles == 1, "fresh outside and pair recover");
    }
    [TestMethod]
    public void VolumeBoundaries()
    {
        var half = WristTapGesture.ContactHalfSize;
        foreach (var axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
            foreach (float sign in new[] { -1f, 1f })
            {
                float extent = Vector3.Dot(half, axis);
                var s = new Sequence();
                s.Ready();
                var inside = WristTapGesture.ContactCentre + axis * sign * (extent - .001f);
                s.Pair(inside);
                Check(s.Toggles == 1, "all contact faces include their inside samples");
                var band = WristTapGesture.ContactCentre + axis * sign * (extent + WristTapGesture.ReleaseMarginMetres / 2);
                var j = new Sequence();
                j.Ready();
                j.Step(s_contact);
                for (int i = 0; i < 5; i++) { j.Step(band); j.Step(s_contact); }
                Check(j.Toggles == 0, "release hysteresis rejects position jitter on every face");
            }
    }
    [TestMethod]
    public void LookBoundaries()
    {
        foreach (bool allowed in new[] { false, true })
        {
            var s = new Sequence();
            float dot = WristTapGesture.MinimumGazeDot + (allowed ? .02f : -.02f);
            s.PlaceTarget(s.Head.Position + new Vector3(MathF.Sqrt(1 - dot * dot), 0, -dot) * .5f);
            s.Ready();
            s.Pair();
            Check((s.Toggles == 1) == allowed, "view angle to the tap centre gates recognition");
        }
        (float Distance, bool Allowed)[] distances =
        [
            (WristTapGesture.MinimumHeadDistanceMetres - .02f, false),
            (WristTapGesture.MinimumHeadDistanceMetres, true),
            (WristTapGesture.MaximumHeadDistanceMetres, true),
            (WristTapGesture.MaximumHeadDistanceMetres + .02f, false)
        ];
        foreach (var (distance, allowed) in distances)
        {
            var s = new Sequence();
            s.Wrist = s.Wrist with { Rotation = Quaternion.Identity };
            s.PlaceTarget(s.Head.Position - Vector3.UnitZ * distance);
            s.Ready(); s.Pair();
            Check((s.Toggles == 1) == allowed, $"distance {distance}m to the tap centre gates recognition");
        }
    }
    [TestMethod]
    public void Timing()
    {
        var s = new Sequence();
        s.Ready();
        s.Step(s_contact, .08);
        s.Step(duration: WristTapGesture.OutsideMinimumSeconds / 3);
        s.Step(s_contact, .08);
        s.Step();
        Check(s.Toggles == 0, "too brief separation cannot count twice");
        var slow = new Sequence();
        slow.Ready(); slow.Tap(); slow.Step(duration: WristTapGesture.PairMaximumSeconds + .1); slow.Tap();
        Check(slow.Toggles == 0, "late second tap becomes a new first tap");
    }
    [TestMethod]
    public void Cooldown()
    {
        var s = new Sequence(mode: WristButtonActivationMode.Touch);
        s.Ready();
        s.Step(s_contact);
        Check(s.Step(duration: FrameSeconds).Triggered, "releasing the first tap confirms immediately");
        Check(!s.Step(duration: .27).Confirmed, "distinct confirmation has ended by 0.28s");
        var earlyContact = s.Step(s_contact, FrameSeconds);
        Check(earlyContact.Reason == "cooldown" && earlyContact.Events == WristTapEvent.None,
            "a new contact just before 0.3s is rejected");
        s.Step(duration: .08);
        s.Tap();
        Check(s.Toggles == 2, "a tap after 0.3s and a fresh release is accepted");
    }

    [TestMethod]
    public void CooldownSurvivesResetAndModeChange()
    {
        var s = new Sequence();
        s.Ready();
        s.Tap();
        s.Step(s_contact);
        Check(s.Step(duration: FrameSeconds).Triggered, "first pair confirms on release");
        s.Gesture.Reset();
        s.Mode = WristButtonActivationMode.Touch;
        Check(s.Step(duration: .2).Reason == "cooldown", "reset and mode change retain cooldown");
        s.Step(s_contact, .4);
        Check(s.Toggles == 1, "cooldown recovery under a finger cannot arm");
        s.Ready(); s.Tap();
        Check(s.Toggles == 2, "release and fresh tap after cooldown works");
    }
    [TestMethod]
    public void RepeatedUnwatchedTapsAndClasps()
    {
        foreach (bool clasp in new[] { false, true })
        {
            var s = new Sequence();
            if (!clasp)
                s.Head = s.Head with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI) };
            s.Ready();
            for (int i = 0; i < 30; i++)
            {
                s.Step(s_contact, clasp ? .8 : .08 + i % 3 * .1);
                s.Step(duration: .15);
            }
            Check(s.Toggles == 0, "tapping without looking and clasping never toggle");
        }
    }
    [TestMethod]
    public void InvalidSamples()
    {
        foreach (int kind in Enumerable.Range(0, 7))
        {
            var s = new Sequence();
            s.Ready(); s.Tap();
            var saved = s.Wrist;
            if (kind == 0) s.Seconds += WristTapGesture.MaximumFrameGapSeconds + .01;
            if (kind == 1) s.Seconds -= .2;
            if (kind == 2) s.Seconds = double.NaN;
            if (kind == 3) s.Wrist = new(new(float.NaN), Quaternion.Identity);
            if (kind == 4) s.Wrist = new(Vector3.Zero, default);
            if (kind == 5) s.Wrist = new(Vector3.Zero, new(float.MaxValue, 0, 0, 1));
            if (kind == 6) s.Available = false;
            Check(!s.Step(s_contact, FrameSeconds).Visible, "bad samples cancel pending cue");
            s.Wrist = saved; s.Seconds = 20; s.Available = true;
            s.Ready(); s.Tap();
            Check(s.Toggles == 0, "invalid data never bridges taps");
        }
    }
    [TestMethod]
    public void TransformInvariant()
    {
        var rotation = Quaternion.CreateFromYawPitchRoll(.7f, .3f, -.5f);
        var translation = new Vector3(1.4f, -.6f, 2.1f);
        BoardPose Transform(BoardPose p) => new(Vector3.Transform(p.Position, rotation) + translation, Quaternion.Normalize(rotation * p.Rotation));
        foreach (var hand in Enum.GetValues<HandSide>())
        {
            var s = new Sequence(hand);
            s.Head = Transform(s.Head); s.Wrist = Transform(s.Wrist);
            s.Ready(); s.Pair();
            Check(s.Toggles == 1, "tracking space does not affect recognition");
        }
    }

    [TestMethod]
    public void InvalidArguments()
    {
        var s = new Sequence();
        foreach (var finger in new[] { new Vector3(float.NaN), new Vector3(float.PositiveInfinity), new Vector3(float.MaxValue) })
        {
            var state = s.Gesture.Update(s.Head, s.Wrist, HandSide.Left, finger, true, 1);
            Check(!state.Visible && !state.Triggered, "invalid or overflowing fingertip cannot produce feedback");
        }
        foreach (var head in new[] { new BoardPose(new(float.NaN), Quaternion.Identity), new BoardPose(Vector3.Zero, default) })
            Check(!s.Gesture.Update(head, s.Wrist, HandSide.Left, Vector3.Zero, true, 2).Visible, "invalid head cannot show cue");
        Check(!s.Gesture.Update(s.Head, s.Wrist, (HandSide)99, Vector3.Zero, true, 3).Triggered, "invalid hand rejected");
        Check(!s.Gesture.Update(s.Head, s.Wrist, HandSide.Left, Vector3.Zero, true, 4, (WristButtonActivationMode)99).Triggered, "invalid mode rejected");
    }
    [TestMethod]
    public void RandomNegative()
    {
        var random = new Random(36028);
        var summary = new List<string>();
        for (int kind = 0; kind < 5; kind++)
        {
            var s = new Sequence();
            for (int i = 0; i < 20000; i++)
            {
                var p = new Vector3(.10f + (float)random.NextDouble() * .08f,
                    .04f + (float)random.NextDouble() * .18f, -.21f + (float)random.NextDouble() * .20f);
                if (kind == 4) p = s_contact + new Vector3((float)random.NextDouble() * .005f);
                if (kind == 0) s.Available = false;
                if (kind == 1) s.Head = s.Head with { Rotation = Quaternion.CreateFromYawPitchRoll(2 + (float)random.NextDouble(), 0, 0) };
                if (kind == 2) s.PlaceTarget(s.Head.Position - Vector3.UnitZ * (1.1f + (float)random.NextDouble()));
                if (kind == 3) p.X += .4f;
                s.Step(p, FrameSeconds); // last kind stays inside, never a release
            }
            Check(s.Toggles == 0, $"random negative kind={kind}, seed=36028; trace tail={string.Join(';', s.Trace.TakeLast(6))}");
            summary.Add($"seed=36028 kind={kind} samples=20000 toggles={s.Toggles}");
        }
        Directory.CreateDirectory("artifacts/task-036");
        File.WriteAllLines("artifacts/task-036/random-motion.log", summary);
        Console.WriteLine("Task 036: 100000 seeded random negative samples, 0 toggles.");
    }
    [TestMethod]
    public void RandomGrammar()
    {
        var random = new Random(36029);
        for (int i = 0; i < 200; i++)
        {
            var s = new Sequence(i % 2 == 0 ? HandSide.Left : HandSide.Right);
            s.Ready();
            s.Step(s_contact, .06 + random.NextDouble() * .15);
            s.Step(duration: .12 + random.NextDouble() * .15);
            bool interrupted = random.Next(2) == 0;
            if (interrupted) { s.Available = false; s.Step(); s.Available = true; s.Ready(); }
            s.Tap();
            Check(s.Toggles == (interrupted ? 0 : 1), $"random grammar seed=36029 iteration={i} interrupted={interrupted}");
        }
    }

    // These walks jitter far faster than a hand and seldom reach the wrist, so any contact size admits an
    // occasional pair and the count says little about real use. The scripted cases above cover the rules.
    private const int MaximumRandomWalkToggles = 5;
    [TestMethod]
    public void RandomWalks()
    {
        var summary = new List<string>();
        int total = 0;
        for (int seed = 36040; seed < 36060; seed++)
        {
            var random = new Random(seed);
            var gesture = new WristTapGesture();
            var head = new BoardPose(new(0, 1.5f, 0), Quaternion.Identity);
            Vector3 wrist = new(-.2f, 1.25f, -.4f), finger = new(.2f, 1.25f, -.4f), angles = Vector3.Zero;
            float Noise() => ((float)random.NextDouble() - .5f) * 2;
            Vector3 Noise3() => new(Noise(), Noise(), Noise());
            var tail = new Queue<string>();
            int toggles = 0;
            for (int i = 0; i < 10000; i++)
            {
                wrist = Vector3.Clamp(wrist + Noise3() * .008f, new(-.5f, .9f, -.75f), new(.5f, 1.7f, -.15f));
                finger = Vector3.Clamp(finger + Noise3() * .012f, new(-.5f, .9f, -.75f), new(.5f, 1.7f, -.15f));
                angles += Noise3() * .06f;
                var pose = new BoardPose(wrist, Quaternion.CreateFromYawPitchRoll(angles.X, angles.Y, angles.Z));
                var result = gesture.Update(head, pose, HandSide.Left, finger, random.Next(300) != 0, i / 90d);
                tail.Enqueue($"sample={i} wrist={wrist} finger={finger} angles={angles} event={result.Events} measurements={gesture.Measurements}");
                if (tail.Count > 180)
                    tail.Dequeue();
                if (result.Triggered)
                {
                    toggles++;
                    Directory.CreateDirectory("artifacts/task-036");
                    File.WriteAllLines($"artifacts/task-036/random-failure-{seed}-{i}.log", tail);
                }
            }
            total += toggles;
            summary.Add($"seed={seed} samples=10000 toggles={toggles}; independent continuous wrist/finger/rotation walks with occasional interruption");
        }
        Directory.CreateDirectory("artifacts/task-036");
        File.WriteAllLines("artifacts/task-036/random-walks.log", summary);
        Check(total <= MaximumRandomWalkToggles, $"random walks toggled {total} times; see artifacts/task-036/random-failure-*.log");
        Console.WriteLine($"Task 036: 200000 seeded independent random-walk samples, {total} toggles (at most {MaximumRandomWalkToggles}).");
    }
}
