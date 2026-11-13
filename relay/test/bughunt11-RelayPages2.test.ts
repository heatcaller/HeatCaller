import assert from "node:assert/strict";
import { createContext, runInContext } from "node:vm";
import { describe, it } from "node:test";
import { DEFAULT_CONTEXT, homePage, livePage, schedulePage } from "../src/pages.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";

/**
 * Bug hunt 11 (5 Nov 2026), relay pages, second half: Listen's sweep lock
 * across a rejoin, Home's last night kept after a failed results read, Who
 * wins? picks settled by a race the phone never saw finish, and how late
 * the night is running once it crosses midnight.
 */

function piece(script: string, pattern: RegExp, what: string): string {
  const found = pattern.exec(script)?.[0];
  assert.ok(found, `${what} is where the test expects it`);
  return found;
}

describe("Listen: a rejoin keeps one sweep at a time", () => {
  it("does not let the sweep from before the rejoin unlock the new one", async () => {
    const { script } = pageParts(livePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, listening: true } }));
    const lock = piece(script, /  let sweeping = false;[\s\S]*?(?=\n  \/\/ When the page was hidden)/, "the sweep lock");
    const sweep = piece(script, /  async function sweep\(\) \{[\s\S]*?\n  \}\n/, "sweep");
    const rejoin = piece(script, /  function rejoin\(\) \{[\s\S]*?\n  \}\n/, "rejoin");

    const waiting: Array<() => void> = [];
    let sweeps = 0;
    const context = createContext({
      sweepOnce: () => {
        sweeps += 1;
        return new Promise<void>((resolve) => waiting.push(resolve));
      },
    });

    runInContext(
      `let turn = 0; let chain = null; let started = true; const ctx = null; const keepAlive = null; function dropAll() {}\n`
        + `const STREAM_AHEAD_S = 1; let streamAhead = STREAM_AHEAD_S;\n`
        + `${lock}\n${sweep}\n${rejoin}\nthis.sweep = sweep; this.rejoin = rejoin;`,
      context,
    );
    const page = context as { sweep: () => Promise<void>; rejoin: () => void };

    void page.sweep();
    assert.equal(sweeps, 1, "the old sweep, waiting on slow Wi-Fi");

    // Back from the background: a new sweep starts at once.
    page.rejoin();
    assert.equal(sweeps, 2, "the rejoin's sweep");

    // The old sweep's request comes back and it gives up.
    waiting[0]!();
    for (let i = 0; i < 5; i++) await new Promise((resolve) => setImmediate(resolve));

    // The next tick, with the rejoin's sweep still waiting: no second one.
    void page.sweep();
    assert.equal(sweeps, 2, "a tick while the rejoin's sweep is out starts nothing");

    // Once that one is done, the next tick sweeps as always.
    waiting[1]!();
    for (let i = 0; i < 5; i++) await new Promise((resolve) => setImmediate(resolve));
    void page.sweep();
    assert.equal(sweeps, 3);
  });
});

describe("Home: the last night after a failed results read", () => {
  it("asks for the results again on the next poll, and then shows the fastest lap", async () => {
    const { script } = pageParts(homePage("acme-rc", "Acme RC"));
    const helper = piece(script, /let nightSig = null;[\s\S]*?\nasync function lastNight\(\) \{[\s\S]*?\n\}\n/, "lastNight");

    const index = {
      shape: 1, year: "2026", drivers: [], raced: [],
      sessions: [{ id: "s9", day: "2026-11-04", at: "20:00", type: "Race", label: "A Main", won: "Sam P." }],
      days: [{ day: "2026-11-04", sessions: [0] }],
    };
    const results = {
      items: [{ id: "s9", body: { label: "A Main", results: [{ pos: 1, name: "Sam P.", laps: 20, elapsedMs: 300000, fastLapMs: 14210 }] } }],
    };

    const asked: string[] = [];
    let busy = true;
    const context = createContext({
      grab: async (path: string) => {
        asked.push(path);
        if (path === "/collection/resultsindex?meta=1") return { body: { items: [{ id: "2026", publishedAt: "2026-11-04T22:00:00Z", bytes: 900 }] } };
        if (path === "/collection/resultsindex/2026") return { body: { body: index } };
        if (path.startsWith("/collection/results?ids=")) return busy ? { missing: true, status: 429 } : { body: results };
        return { missing: true, status: 404 };
      },
    });

    runInContext(`let nightHeld = null;\n${helper}\nthis.lastNight = lastNight;`, context);
    const lastNight = (context as { lastNight: () => Promise<any> }).lastNight;

    const first = await lastNight();
    assert.equal(first.day, "2026-11-04", "the night still shows from the index");
    assert.equal(first.fast, null);

    // The relay is no longer busy: the next poll reads the results.
    busy = false;
    const second = await lastNight();
    assert.equal(asked.filter((p) => p.startsWith("/collection/results?ids=")).length, 2, "the results were asked for again");
    assert.deepEqual({ ...second.fast }, { ms: 14210, name: "Sam P.", label: "A Main" });

    // Read in full now: the poll after is the small meta read alone.
    const before = asked.length;
    await lastNight();
    assert.deepEqual(asked.slice(before), ["/collection/resultsindex?meta=1"]);
  });
});

describe("Who wins?: a pick is settled by its own race", () => {
  const STATE = "/api/v1/c/acme-rc/snapshot/state";
  const MIN = 60000;

  // A finished board, published at `at`, won by `winner` over five minutes.
  // lapMs: a later race is told from the one before by its winner's time
  // (Bug hunt 17), so one won by the same driver is given its own.
  const finished = (winner: string, at: number, lapMs = 10000) => ({
    publishedAt: new Date(at).toISOString(),
    body: {
      state: "Finished", flag: "Checkered", countdown: "",
      standings: [
        { name: winner, laps: Array.from({ length: 30 }, () => lapMs), best: lapMs, lap: 31, cells: [{ text: winner, emphasis: "none" }] },
        { name: "Riley T.", laps: Array.from({ length: 29 }, () => 10300), best: 10300, lap: 30, cells: [{ text: "Riley T.", emphasis: "none" }] },
      ],
    },
  });

  async function page(): Promise<{ p: FakePage; show: (frame: unknown) => void }> {
    let frame: unknown = null;
    const { html, script } = pageParts(livePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, vote: true } }));
    const p: FakePage = new FakePage(html, async (url: string) => (url === STATE && frame !== null ? { status: 200, body: frame } : { status: 404 }));
    // The last race's board is up when the page opens.
    frame = finished("Jordan M.", p.clock - 2 * MIN);
    p.run(script);
    await p.settle();
    return { p, show: (f) => { frame = f; } };
  }

  it("puts a pick aside when the phone was away while its race finished and the next one ran", async () => {
    const { p, show } = await page();
    p.eval("picksAdd('Heat 3', 'Sam P.')");
    const picked = p.clock;

    // Locked: Heat 3 runs and finishes (Sam wins), Heat 4 is staged, runs
    // and finishes (Jordan wins). The page reads none of it.
    p.hidden = true;
    show(finished("Sam P.", picked + 8 * MIN));
    await p.advance(12 * MIN);
    show(finished("Jordan M.", picked + 20 * MIN));
    await p.advance(9 * MIN);

    // Unlocked on Heat 4's finished board.
    p.hidden = false;
    await p.advance(2000);

    assert.equal(p.eval("picksWords()"), "", "the Heat 3 pick is not marked wrong against Heat 4's winner");
  });

  it("still settles a pick whose own race finished while the phone was locked", async () => {
    const { p, show } = await page();
    p.eval("picksAdd('Heat 3', 'Sam P.')");
    const picked = p.clock;

    // Locked for the race; unlocked on its finished board.
    p.hidden = true;
    show(finished("Sam P.", picked + 7 * MIN));
    await p.advance(8 * MIN);
    p.hidden = false;
    await p.advance(2000);

    assert.equal(p.eval("picksWords()"), "Your picks: 1 of 1 right");
  });

  it("settles by the first finish after the pick when the page watched throughout", async () => {
    const { p, show } = await page();
    p.eval("picksAdd('Heat 3', 'Sam P.')");
    const picked = p.clock;

    // A long wait before the race (more than twice its length), watched.
    await p.advance(10 * MIN);
    show(finished("Jordan M.", picked + 16 * MIN, 10100));
    await p.advance(7 * MIN);

    assert.equal(p.eval("picksWords()"), "Your picks: 0 of 1 right");
  });
});

describe("Running late across midnight", () => {
  // Local times on 4 and 5 November.
  const on = (day: number, h: number, m: number) => new Date(2026, 10, day, h, m, 0, 0);

  it("Schedule: reads a slot on the day nearest when the line started", async () => {
    const { html, script } = pageParts(schedulePage("acme-rc", "Acme RC"));
    const p = new FakePage(html, async () => ({ status: 404 }));
    p.run(script);
    await p.settle();

    const late = (lines: unknown[]) => p.eval(`lateWords(runningLate(${JSON.stringify(lines)}))`);
    assert.equal(late([{ time: "11:55 pm", startedUtc: on(5, 0, 10).toISOString() }]), "Running 15 min behind");
    assert.equal(late([{ time: "23:55", startedUtc: on(5, 0, 10).toISOString() }]), "Running 15 min behind");
    assert.equal(late([{ time: "12:05 am", startedUtc: on(4, 23, 58).toISOString() }]), "Running 7 min ahead");

    // A line not started yet, looked at just before midnight: tonight's
    // 00:20 is half an hour away, not a day ago.
    const ahead = p.eval(`(clockOn('12:20 am', ${on(4, 23, 50).getTime()}) - ${on(4, 23, 50).getTime()}) / 60000`);
    assert.equal(ahead, 30);
  });

  it("Home: the Schedule tile reads the same", () => {
    const { script } = pageParts(homePage("acme-rc", "Acme RC"));
    const planAt = piece(script, /function planAt\(text, dayOf\) \{[\s\S]*?\n\}\n/, "planAt");
    const behindBy = piece(script, /function behindBy\(lines\) \{[\s\S]*?\n\}\n/, "behindBy");
    const context = createContext({});
    runInContext(`${planAt}\n${behindBy}\nthis.behindBy = behindBy;`, context);
    const behind = (context as { behindBy: (lines: unknown[]) => number | null }).behindBy;

    assert.equal(behind([{ time: "11:55 pm", startedUtc: on(5, 0, 10).toISOString() }]), 15);
    assert.equal(behind([{ time: "12:05 am", startedUtc: on(4, 23, 58).toISOString() }]), -7);
    assert.equal(behind([{ time: "7:00 pm", startedUtc: on(4, 19, 4).toISOString() }]), 4, "a same-day line as before");
  });
});
