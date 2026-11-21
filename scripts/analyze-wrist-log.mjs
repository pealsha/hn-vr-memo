// Summarises wrist-tap attempts in a HnMemo run log, so a failure report can be read without dumping samples.
// Usage: node scripts/analyze-wrist-log.mjs [log] [--from seconds] [--to seconds] [--near metres] [--rows count]
// Without a log path, the newest file in %LOCALAPPDATA%\HandwriteMemo\logs is read.
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

// Diagnostic samples are written every 0.2s while the fingertip is within 0.5m, plus one per event.
// A longer silence means the hand left, so the next near sample starts a new period. A pending pair
// always ends with a logged Confirmed or Cancelled event, so it stays one period across any silence.
const PeriodGapSeconds = 0.6;

const number = (text, pattern) => {
  const match = pattern.exec(text);
  return match ? Number(match[1]) : undefined;
};
const vector = (text, pattern) => {
  const match = pattern.exec(text);
  return match ? match[1].split(",").map(Number) : undefined;
};

export function parseLog(text) {
  const samples = [], handedness = [], calibrations = [], toggles = [];
  for (const line of text.split(/\r?\n/)) {
    const head = /^(\d+(?:\.\d+)?)s (.*)$/.exec(line);
    if (!head) continue;
    const seconds = Number(head[1]), body = head[2];
    const hand = /handedness=(Left|Right)/.exec(body);
    if (hand) handedness.push({ seconds, hand: hand[1] });
    const calibrated = /wrist calibration: \w+ -> Succeeded; centre=<([^>]+)>/.exec(body);
    if (calibrated) calibrations.push({ seconds, centre: calibrated[1].split(",").map(Number) });
    if (/^board (shown|hidden): wrist tap/.test(body)) toggles.push(seconds);
    if (!body.includes("wrist tap: events=")) continue;
    const finger = vector(body, /fingerInGrip=\(([^)]+)\)/);
    samples.push({
      seconds,
      events: /events=([^;]+)/.exec(body)[1],
      reason: /reason=([^;]+)/.exec(body)[1],
      pending: /pending=True/.test(body),
      looking: /look=True/.test(body),
      palm: number(body, /palm=(-?[\d.]+)/),
      palmNeed: number(body, /palm=-?[\d.]+ \(need >=(-?[\d.]+)\)/),
      gaze: number(body, /gaze=(-?[\d.]+)/),
      gazeNeed: number(body, /gaze=-?[\d.]+ \(need >=(-?[\d.]+)\)/),
      headDistance: number(body, /headDistance=([\d.]+)m/),
      finger,
      rightWrist: /wrist=Right/.test(body),
      returnDot: number(body, /returnDot=(-?[\d.]+)/),
      centre: vector(body, /centre=<([^>]+)>/),
      radius: number(body, /radius=([\d.]+)/),
      pairMax: number(body, /pairMax=([\d.]+)s/),
    });
  }
  return { samples, handedness, calibrations, toggles };
}

// The recogniser mirrors X for the right wrist before comparing with the saved centre.
function centreDistance(sample) {
  if (!sample.finger || !sample.centre) return undefined;
  const [x, y, z] = sample.finger, [cx, cy, cz] = sample.centre;
  return Math.hypot((sample.rightWrist ? -x : x) - cx, y - cy, z - cz);
}

function classify(period) {
  const firsts = period.samples.filter((s) => s.events.includes("CueStarted")).length;
  const confirmed = period.samples.filter((s) => s.events.includes("Confirmed")).length;
  const cancels = period.samples.filter((s) => s.events.includes("Cancelled")).map((s) => s.reason);
  const measured = period.samples.filter((s) => s.palm !== undefined);
  const looking = measured.filter((s) => s.looking).length;
  if (confirmed > 0) return `toggled x${confirmed}` + (cancels.length ? `; also cancelled: ${cancels.join(", ")}` : "");
  if (firsts > 0) return `first tap only; cancelled: ${cancels.join(", ") || "none logged"}`;
  if (measured.length === 0) return `blocked: ${[...new Set(period.samples.map((s) => s.reason))].join(", ")}`;
  if (looking === 0) {
    const low = (value, need) => value !== undefined && need !== undefined && value < need;
    const palmLow = measured.filter((s) => low(s.palm, s.palmNeed)).length;
    const gazeLow = measured.filter((s) => low(s.gaze, s.gazeNeed)).length;
    return `not looking (palm below need ${palmLow}/${measured.length}, gaze below need ${gazeLow}/${measured.length})`;
  }
  return "looking, no contact";
}

export function periods(samples, { near = 0.1, from = -Infinity, to = Infinity } = {}) {
  const result = [];
  let current;
  for (const sample of samples) {
    if (sample.seconds < from || sample.seconds > to) continue;
    const distance = centreDistance(sample);
    const relevant = (distance !== undefined && distance < near) || sample.events !== "None" || sample.pending;
    if (!relevant) continue;
    if (!current || (sample.seconds - current.end > PeriodGapSeconds && !current.samples.at(-1).pending)) {
      current = { start: sample.seconds, end: sample.seconds, samples: [] };
      result.push(current);
    }
    current.end = sample.seconds;
    current.samples.push({ ...sample, distance });
  }
  for (const period of result) {
    const measured = period.samples.filter((s) => s.palm !== undefined);
    const distances = period.samples.map((s) => s.distance).filter((d) => d !== undefined);
    period.outcome = classify(period);
    period.minDistance = distances.length ? Math.min(...distances) : undefined;
    period.palm = measured.length ? median(measured.map((s) => s.palm)) : undefined;
    period.gaze = measured.length ? median(measured.map((s) => s.gaze)) : undefined;
  }
  return result;
}

// Contact timings come from event rows, which are logged on the frame they happen.
export function tapTimings(samples, { from = -Infinity, to = Infinity } = {}) {
  const contacts = [], pairGaps = [], returnDots = [];
  let contactStart, firstEntry;
  for (const s of samples) {
    if (s.seconds < from || s.seconds > to) continue;
    if (s.events.includes("CueStarted")) firstEntry = s.seconds;
    else if (s.events.includes("ContactStarted") && firstEntry !== undefined) pairGaps.push(s.seconds - firstEntry);
    if (s.events.includes("ContactStarted")) contactStart = s.seconds;
    if (s.events.includes("ContactEnded") && contactStart !== undefined) {
      contacts.push(s.seconds - contactStart);
      contactStart = undefined;
      if (s.returnDot !== undefined) returnDots.push(s.returnDot);
    }
    if (s.events.includes("Confirmed") || s.events.includes("Cancelled")) firstEntry = undefined;
  }
  return { contacts, pairGaps, returnDots };
}

function median(values) {
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.floor(sorted.length / 2)];
}
const fixed = (value, digits = 2) => (value === undefined ? "-" : value.toFixed(digits));
const range = (values, digits = 2) =>
  values.length ? `n=${values.length} min=${fixed(Math.min(...values), digits)} median=${fixed(median(values), digits)} max=${fixed(Math.max(...values), digits)}` : "n=0";

export function report(text, options = {}) {
  const { samples, handedness, calibrations, toggles } = parseLog(text);
  const { rows = 40 } = options;
  const found = periods(samples, options);
  const timings = tapTimings(samples, options);
  const outcomes = new Map();
  for (const period of found) {
    const key = period.outcome.replace(/ x\d+.*/, "").replace(/ \(.*|; cancelled.*/, "");
    outcomes.set(key, (outcomes.get(key) ?? 0) + 1);
  }
  const last = samples.findLast((s) => s.radius !== undefined);
  const lines = [
    `handedness: ${handedness.map((h) => `${fixed(h.seconds, 1)}s=${h.hand}`).join(", ") || "not logged"}`,
    `calibrations: ${calibrations.map((c) => `${fixed(c.seconds, 1)}s centre=(${c.centre.map((v) => fixed(v, 3)).join(",")})`).join(", ") || "none"}`,
    `limits (last sample): radius=${last?.radius ?? "-"}m pairMax=${last?.pairMax ?? "-"}s palmNeed=${last?.palmNeed ?? "-"} gazeNeed=${last?.gazeNeed ?? "-"}`,
    `board toggles by wrist tap: ${toggles.filter((t) => t >= (options.from ?? -Infinity) && t <= (options.to ?? Infinity)).length}`,
    `near-wrist periods (fingertip within ${options.near ?? 0.1}m of the centre, or a tap event): ${found.length}`,
    ...[...outcomes].map(([outcome, count]) => `  ${count} ${outcome}`),
    `contact seconds: ${range(timings.contacts, 3)}`,
    `first-to-second contact seconds: ${range(timings.pairGaps)}`,
    `return direction on release: ${range(timings.returnDots)}`,
    "",
    "start-end s | samples | min distance m | median palm | median gaze | outcome",
  ];
  for (const p of found.slice(-rows))
    lines.push(`${fixed(p.start, 1)}-${fixed(p.end, 1)} | ${p.samples.length} | ${fixed(p.minDistance, 3)} | ${fixed(p.palm)} | ${fixed(p.gaze)} | ${p.outcome}`);
  if (found.length > rows) lines.push(`(only the last ${rows} of ${found.length} periods; use --rows or --from)`);
  return lines.join("\n");
}

function newestLog() {
  const folder = join(process.env.LOCALAPPDATA ?? "", "HandwriteMemo", "logs");
  const files = readdirSync(folder).filter((f) => /^memo-.*\.log$/.test(f)).map((f) => join(folder, f));
  if (!files.length) throw new Error(`No memo-*.log in ${folder}`);
  return files.sort((a, b) => statSync(b).mtimeMs - statSync(a).mtimeMs)[0];
}

function main(args) {
  const options = {};
  let path;
  for (let i = 0; i < args.length; i++) {
    const flag = args[i];
    if (["--from", "--to", "--near", "--rows"].includes(flag)) {
      const value = Number(args[++i]);
      if (!Number.isFinite(value)) throw new Error(`${flag} needs a number`);
      options[flag.slice(2)] = value;
    } else path = flag;
  }
  path ??= newestLog();
  console.log(`log: ${path}`);
  console.log(report(readFileSync(path, "utf8"), options));
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? "").href) main(process.argv.slice(2));
