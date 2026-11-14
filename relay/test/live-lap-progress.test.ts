import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { DEFAULT_CONTEXT, livePage } from "../src/pages.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";

/**
 * THE FLAG CARD AS A LAP PROGRESS TRACK (4 Nov 2026, Asher picked
 * direction 5 of ten: "build 5 lap progress"). The race's length is the
 * leader's lap plus what the countdown says is left, so the countdown has
 * to be read right as the page ships it - a regex losing its backslashes
 * inside the page's template string once left every race without a bar.
 */

/** One function from the live page's script, as the phone gets it. */
function pageFunction(name: string): (...args: unknown[]) => unknown {
  const html = livePage("acme-rc", "Acme RC");
  const match = new RegExp(`function ${name}\\([^)]*\\) \\{[\\s\\S]*?\\n\\}`).exec(html);
  assert.ok(match, `the page has ${name}()`);
  return new Function(`${match[0]}; return ${name};`)() as (...args: unknown[]) => unknown;
}

describe("the Live flag card's lap progress", () => {
  it("reads the laps left from the countdown", () => {
    const toGo = pageFunction("lbToGo");
    assert.equal(toGo("5 LAPS TO GO"), 5);
    assert.equal(toGo("1 LAP TO GO"), 1);
    assert.equal(toGo("LAST LAP"), 1);
    assert.equal(toGo("12 laps to go"), 12);
  });

  it("has no laps left for a timed race's countdown", () => {
    const toGo = pageFunction("lbToGo");
    assert.equal(toGo("04:32 REMAINING"), null);
    assert.equal(toGo(""), null);
  });

  it("draws a segment a lap with the lap being run lit", () => {
    const bar = pageFunction("lbBar");
    const html = String(bar(7, 12, false));
    assert.equal((html.match(/<i class="/g) ?? []).length, 12);
    assert.equal((html.match(/<i class="on">/g) ?? []).length, 6);
    assert.equal((html.match(/<i class="now">/g) ?? []).length, 1);
    assert.match(html, /<span class="lp-fin"><\/span>/);
  });

  it("draws one track past forty laps, and nothing without a length", () => {
    const bar = pageFunction("lbBar");
    assert.match(String(bar(30, 60, false)), /<span class="lp-track"><b style="width:50%"><\/b><\/span>/);
    assert.equal(bar(5, 0, false), "");
  });
});

/**
 * A TIMED RACE'S BAR (5 Nov 2026, Asher: "on flag card on relay make a
 * progress bar for a timed race as well as the lap number race"). The
 * countdown is the time left; raceSecs, the race's length, comes with it.
 */
describe("the Live flag card's time progress", () => {
  it("reads the time left from the countdown, minutes past the hour included", () => {
    const left = pageFunction("lbTimeLeft");
    assert.equal(left("07:30 REMAINING"), 450);
    assert.equal(left("00:00 REMAINING"), 0);
    assert.equal(left("125:05 remaining"), 7505);
    assert.equal(left("5 LAPS TO GO"), null);
    assert.equal(left(""), null);
  });

  it("fills one track with the share of the race gone, and draws nothing without a length", () => {
    const bar = pageFunction("lbTimeBar");
    assert.match(String(bar(450, 600)), /<span class="lp-track"><b style="width:25%"><\/b><\/span><span class="lp-fin">/);
    assert.match(String(bar(0, 600)), /width:100%/);
    assert.match(String(bar(900, 600)), /width:0%/, "never below empty");
    assert.equal(bar(450, 0), "");
    assert.equal(bar(null, 600), "");
  });

  it("shows the bar on a timed race as the laptop sends it", async () => {
    const STATE = "/api/v1/c/acme-rc/snapshot/state";
    const { html, script } = pageParts(livePage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
    let frame: Record<string, unknown> = {
      state: "Racing", flag: "Green", countdown: "03:00 REMAINING", raceSecs: 300,
      standings: [{ name: "Sam P.", laps: [9000, 9100], best: 9000, lap: 3, cells: [{ text: "Sam P.", emphasis: "none" }] }],
    };
    const p: FakePage = new FakePage(html, async (url: string) => url === STATE
      ? { status: 200, body: { seq: 1, publishedAt: new Date(p.clock).toISOString(), body: frame } }
      : { status: 404 });
    p.run(script);
    await p.settle();

    assert.match(p.get("bar").innerHTML, /lp-timed[\s\S]*width:40%/);
    assert.match(p.get("bar").innerHTML, /Lap 3/);

    // From a laptop that does not send the length: no bar, as before.
    frame = { ...frame, raceSecs: undefined, countdown: "02:00 REMAINING" };
    await p.advance(2000);
    await p.settle();
    assert.doesNotMatch(p.get("bar").innerHTML, /lp-bar/);
  });
});

/**
 * A RACE AS THE LAPTOP SENDS IT (4 Nov 2026, Asher: "it didn't show the
 * stages as they updated and at one point went back to race over"). The
 * laptop empties the board when a race is staged and again at the green
 * (RaceControlSession.Stage, GreenFlag), so Staging, Countdown and the
 * start of Racing all arrive with nobody on the board.
 */
describe("the Live flag card through a race", () => {
  const STATE = "/api/v1/c/acme-rc/snapshot/state";
  const row = (name: string, laps: number[]) => ({
    name, laps, best: Math.min(...laps), cells: [{ text: "1", emphasis: "none" }, { text: name, emphasis: "none" }],
  });
  const finished = { state: "Finished", flag: "Checkered", countdown: "", standings: [row("Sam P.", [9100, 9000]), row("Asher K.", [9500, 9400])] };

  async function race() {
    let frame: Record<string, unknown> = finished;
    const { html, script } = pageParts(livePage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
    const p: FakePage = new FakePage(html, async (url: string) => url === STATE
      ? { status: 200, body: { seq: 1, publishedAt: new Date(p.clock).toISOString(), body: frame } }
      : { status: 404 });
    p.run(script);
    await p.settle();
    return {
      p,
      bar: () => p.get("bar"),
      board: () => p.get("board").innerHTML,
      async send(next: Record<string, unknown>) { frame = next; await p.advance(2000); await p.settle(); },
    };
  }

  it("names every stage of the next race and never says it is over until it is", async () => {
    const r = await race();
    assert.match(r.bar().className, /\bover\b/);
    assert.match(r.bar().innerHTML, /Race over/);

    await r.send({ state: "Staging", flag: "None", countdown: "", standings: [] });
    assert.doesNotMatch(r.bar().className, /\bover\b/);
    assert.match(r.bar().innerHTML, /<span class="state">Staging<\/span>/);
    assert.match(r.bar().innerHTML, /Cars to the start line/);
    assert.match(r.board(), /lining up/);

    await r.send({ state: "Countdown", flag: "None", countdown: "", standings: [] });
    assert.match(r.bar().innerHTML, /<span class="state">Countdown<\/span>/);
    assert.match(r.bar().innerHTML, /Start sequence/);

    // The green, before anybody has crossed the line.
    await r.send({ state: "Racing", flag: "Green", countdown: "12 LAPS TO GO", standings: [] });
    assert.doesNotMatch(r.bar().className, /\bover\b/);
    assert.doesNotMatch(r.bar().innerHTML, /Race over/);
    assert.match(r.bar().innerHTML, /<span class="state">Racing<\/span>/);
    assert.match(r.board(), /Green flag/);

    await r.send({ state: "Racing", flag: "Green", countdown: "10 LAPS TO GO", standings: [row("Jordan M.", [9000])] });
    assert.match(r.bar().innerHTML, /Lap 2<span> \/ 12<\/span>/);
    assert.match(r.bar().innerHTML, /10 laps to go/);

    await r.send({ state: "Finished", flag: "Checkered", countdown: "", standings: [row("Jordan M.", [9000, 9100])] });
    assert.match(r.bar().innerHTML, /Race over/);
    assert.match(r.bar().innerHTML, /Winner Jordan M\./);

    // Back to Idle: the race just run stays up, over.
    await r.send({ state: "Idle", flag: "None", countdown: "", standings: [] });
    assert.match(r.bar().innerHTML, /Race over/);
    assert.match(r.board(), /Jordan M\./);
  });
});

/**
 * THE DRIVER THIS PHONE FOLLOWS (4 Nov 2026, from the relay roadmap): their
 * place, the gap to the car ahead, whether it is closing over the last
 * three laps, how much a lap they need to catch by the flag, and the room
 * to the car behind - on the flag card.
 */
describe("the Live flag card for the driver this phone follows", () => {
  const STATE = "/api/v1/c/acme-rc/snapshot/state";
  const row = (name: string, flying: number, ahead?: string) => ({
    name, laps: Array.from({ length: flying }, () => 9000), best: 9000, lap: flying + 1, ...(ahead ? { ahead } : {}),
    cells: [{ text: name, emphasis: "none" }],
  });
  const frame = (lap: number, gap: string, behind: string, toGo: number) => ({
    state: "Racing", flag: "Green", countdown: `${toGo} LAPS TO GO`,
    standings: [row("Sam P.", lap - 1), row("Asher K.", lap - 1, gap), row("Jordan M.", lap - 1, behind)],
  });

  it("says the gap, the trend, what it takes to catch, and the car behind", async () => {
    let now: Record<string, unknown> = frame(2, "+1.20", "+0.80", 10);
    const { html, script } = pageParts(livePage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
    const p: FakePage = new FakePage(html, async (url: string) => url === STATE
      ? { status: 200, body: { seq: 1, publishedAt: new Date(p.clock).toISOString(), body: now } }
      : { status: 404 });
    p.store.set("hg-follow-acme-rc", "Asher K.");
    p.run(script);
    await p.settle();
    // On the driver's own card above the board since 14 Nov 2026 (driver
    // phone plan item 4), no longer on the flag card.
    const me = () => /<div class="yx">([\s\S]*?)<\/div>/.exec(p.get("you").innerHTML)?.[1] ?? "";

    assert.match(me(), /<b>Asher K\.<\/b> P2/);
    assert.match(me(), /1\.20s behind Sam P\./);
    assert.match(me(), /needs 0\.12s a lap to catch by the flag/);
    assert.match(me(), /0\.80s clear of Jordan M\./);
    assert.doesNotMatch(me(), /closing|losing/, "no trend before three laps");

    for (const [lap, gap] of [[3, "+1.05"], [4, "+0.90"], [5, "+0.75"]] as const) {
      now = frame(lap, gap, "+0.80", 12 - lap);
      await p.advance(2000);
      await p.settle();
    }
    assert.match(me(), /0\.75s behind Sam P\. · closing 0\.15s a lap/);

    // Somebody not on the board, or nobody followed: no line.
    p.store.set("hg-follow-acme-rc", "");
    p.eval(`setFollowed("")`);
    now = frame(6, "+0.60", "+0.80", 6);
    await p.advance(2000);
    await p.settle();
    assert.equal(me(), "");
  });
});

/**
 * THE LIVE TRACK MAP (4 Nov 2026, Asher: "the live track map should be on
 * the live tab"): the laptop's line, and a numbered dot per car found by
 * its row's tag - never anything that is not on the board.
 */
describe("the Live track map", () => {
  const STATE = "/api/v1/c/acme-rc/snapshot/state";
  const square = [[0, 0], [10, 0], [10, 10], [0, 10], [0, 0]];
  const row = (name: string, tag: string) => ({ name, tag, laps: [9000], best: 9000, lap: 2, cells: [{ text: name, emphasis: "none" }] });

  async function page(frame: () => Record<string, unknown>) {
    const { html, script } = pageParts(livePage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
    const p: FakePage = new FakePage(html, async (url: string) => url === STATE
      ? { status: 200, body: { seq: 1, publishedAt: new Date(p.clock).toISOString(), body: frame() } }
      : { status: 404 });
    p.run(script);
    await p.settle();
    return p;
  }

  it("draws the line and a dot per car on the board, numbered by place", async () => {
    const p = await page(() => ({
      state: "Racing", flag: "Green", countdown: "5 LAPS TO GO",
      standings: [row("Sam P.", "aaaa"), row("Asher K.", "bbbb")],
      map: { pts: square, splits: [0.5], cars: [
        { tag: "aaaa", pos: 1, f: 0.25, rate: 0.1, cap: 0.49, stop: false },
        { tag: "bbbb", pos: 2, f: 0.1, rate: 0.1, cap: 0.49, stop: true },
        { tag: "zzzz", pos: 3, f: 0.3, rate: 0.1, cap: 0.49, stop: false },
      ] },
    }));
    assert.equal(p.get("livemap").hidden, false);
    const drawn = p.get("lmapbody").innerHTML;
    assert.match(drawn, /<path class="lm-road" d="M/);
    assert.equal((drawn.match(/lm-split/g) ?? []).length, 1, "the split loop");

    await p.advance(300);
    const dots = p.get("lmapbody").find((n) => n.className === "lm-cars")[0]?.innerHTML
      ?? /<g class="lm-cars">([\s\S]*)<\/g><\/svg>/.exec(p.get("lmapbody").innerHTML)?.[1] ?? "";
    assert.equal((dots.match(/class="lm-car/g) ?? []).length, 2, "a car not on the board has no dot");
    assert.match(dots, /class="lm-car stop"/, "the overdue car is marked");
    assert.match(dots, />1<\/text>/);
    assert.match(dots, />2<\/text>/);
  });

  it("goes when the frame has no map, and when the race is over", async () => {
    let finished = false;
    const p = await page(() => ({
      state: finished ? "Finished" : "Racing", flag: "Green", countdown: "",
      standings: [row("Sam P.", "aaaa")],
      ...(finished ? {} : { map: { pts: square, splits: [], cars: [] } }),
    }));
    assert.equal(p.get("livemap").hidden, false);
    finished = true;
    await p.advance(2000);
    await p.settle();
    assert.equal(p.get("livemap").hidden, true);
  });
});

/**
 * THE WHO WINS? RECORD (4 Nov 2026, from the relay roadmap): this phone's
 * picks, each settled by the first finished race after it.
 */
describe("Who wins?: this phone's record", () => {
  it("settles a pick by the next finished race and keeps the tally", async () => {
    const { html, script } = pageParts(livePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, vote: true } }));
    const p = new FakePage(html, async () => ({ status: 404 }));
    p.run(script);
    await p.settle();

    assert.equal(p.eval("picksWords()"), "");
    p.eval("picksAdd('Q1 Heat 1', 'Sam P.')");
    p.eval("picksSettle('Sam P.', Date.now() + 60000)");
    p.eval("picksAdd('Q1 Heat 2', 'Sam P.')");
    p.eval("picksSettle('Jordan M.', Date.now() + 120000)");
    assert.equal(p.eval("picksWords()"), "Your picks: 1 of 2 right");

    // A finish from before a pick never settles it.
    p.eval("picksAdd('Q1 Heat 3', 'Riley T.')");
    p.eval("picksSettle('Riley T.', Date.now() - 60000)");
    assert.equal(p.eval("picksWords()"), "Your picks: 1 of 2 right");
  });
});
