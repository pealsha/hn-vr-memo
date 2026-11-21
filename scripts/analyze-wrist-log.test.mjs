import { strict as assert } from "node:assert";
import { test } from "node:test";
import { parseLog, periods, report, tapTimings } from "./analyze-wrist-log.mjs";

const centre = [0.07, 0, 0];
// Mirrors the diagnostic row written by OverlayApp.LogWristDiagnostics.
function sample(seconds, { events = "None", reason = "no", pending = false, look = true, palm = 0.8, gaze = 0.8,
  finger = centre, wrist = "Left", returnDot = "" } = {}) {
  return `${seconds.toFixed(3)}s sampleSeconds=${seconds}; wrist tap: events=${events}; reason=${reason}; pending=${pending ? "True" : "False"}; ` +
    `confirmed=False; look=${look ? "True" : "False"}; palm=${palm} (need >=0.1); gaze=${gaze} (need >=0.1); ` +
    `headDistance=0.400m (need 0.15-1); fingerToGrip=0.070m; fingerInGrip=(${finger.join(",")}); controller source=/pose/grip; ` +
    `wrist=${wrist}; right wrist uses mirrored X; haptics=False; returnDot=${returnDot} (need >=0.5); ` +
    `centre=<${centre.join(", ")}>; radius=0.05; releaseMargin=0.015; contactMax=0.5s; pairMax=1.6s; outsideMin=0.06s`;
}
const far = [0.4, 0.2, 0];

const log = [
  "1.000s motion=device-relative; handedness=Right",
  `2.000s wrist calibration: Collecting -> Succeeded; centre=<${centre.join(", ")}>; saved`,
  // A completed pair.
  sample(10.0, { events: "ContactStarted, CueStarted", pending: true }),
  sample(10.08, { events: "ContactEnded", pending: true, returnDot: "0.950" }),
  sample(10.4, { events: "ContactStarted", pending: true }),
  "10.480s board shown: wrist tap DoubleTap",
  sample(10.48, { events: "ContactEnded, Confirmed", returnDot: "0.900" }),
  // Fingertip at the wrist while the palm faces sideways.
  sample(20.0, { look: false, palm: 0.05, reason: "look away" }),
  sample(20.2, { look: false, palm: 0.04, reason: "look away" }),
  // The hand swings away between taps and the pair times out; this stays one attempt.
  sample(30.0, { events: "ContactStarted, CueStarted", pending: true }),
  sample(30.05, { events: "ContactEnded", pending: true, returnDot: "0.990" }),
  sample(30.8, { pending: true, finger: far }),
  sample(31.7, { events: "Cancelled", reason: "pair timeout", finger: far }),
  // A right wrist reports the fingertip with mirrored X.
  sample(40.0, { wrist: "Right", finger: [-0.07, 0, 0] }),
].join("\n");

test("parses handedness, calibration and board toggles", () => {
  const parsed = parseLog(log);
  assert.deepEqual(parsed.handedness, [{ seconds: 1, hand: "Right" }]);
  assert.deepEqual(parsed.calibrations[0].centre, centre);
  assert.deepEqual(parsed.toggles, [10.48]);
});

test("classifies each near-wrist period by what stopped it", () => {
  const found = periods(parseLog(log).samples);
  assert.deepEqual(found.map((p) => p.outcome), [
    "toggled x1",
    "not looking (palm below need 2/2, gaze below need 0/2)",
    "first tap only; cancelled: pair timeout",
    "looking, no contact",
  ]);
  assert.equal(found[3].minDistance, 0, "right wrist distance uses mirrored X");
});

test("measures contact length, pair gap and release direction from event rows", () => {
  const timings = tapTimings(parseLog(log).samples);
  assert.deepEqual(timings.contacts.map((c) => +c.toFixed(2)), [0.08, 0.08, 0.05]);
  assert.deepEqual(timings.pairGaps.map((g) => +g.toFixed(2)), [0.4]);
  assert.deepEqual(timings.returnDots, [0.95, 0.9, 0.99]);
});

test("report honours the time window and row limit", () => {
  const text = report(log, { from: 25, rows: 1 });
  assert.match(text, /board toggles by wrist tap: 0/);
  assert.match(text, /near-wrist periods .*: 2/);
  assert.match(text, /only the last 1 of 2 periods/);
  assert.doesNotMatch(text, /toggled/);
});
