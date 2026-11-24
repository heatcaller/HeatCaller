import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { badgesPage, DEFAULT_CONTEXT, homePage, livePage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * BUG HUNT 24 (23 Nov 2026), relay pages, group 2: Live's flag card in a
 * warm-up and after a reset, the position chart with an odd driver name,
 * Home's race night in the club's zone, Home's sponsor on a busy relay, and
 * Home's "Racing tonight?" alerts step.
 */

function fn(source: string, name: string): string {
  const match = new RegExp(`function ${name}\\([^)]*\\) \\{[\\s\\S]*?\\n\\}`).exec(source);
  assert.ok(match, `the page has ${name}()`);
  return match[0];
}

const STATE = "/api/v1/c/acme-rc/snapshot/state";

/** The Live page, answered with whatever frame the test last set. */
async function live(first: Record<string, unknown>, stored: Record<string, string> = {}) {
  let frame = first;
  const { html, script } = pageParts(livePage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
  const p: FakePage = new FakePage(html, async (url: string) => url === STATE
    ? { status: 200, body: { seq: 1, publishedAt: new Date(p.clock).toISOString(), body: frame } }
    : { status: 404 });
  for (const [k, v] of Object.entries(stored)) p.store.set(k, v);
  p.run(script);
  await p.settle();
  return {
    p,
    bar: () => p.get("bar"),
    board: () => p.get("board").innerHTML,
    you: () => p.get("you").innerHTML,
    async send(next: Record<string, unknown>) { frame = next; await p.advance(2000); await p.settle(); },
  };
}

const row = (name: string, laps: number[]) => ({
  name, laps, best: Math.min(...laps), cells: [{ text: "1", emphasis: "none" }, { text: name, emphasis: "none" }],
});
const finished = { state: "Finished", flag: "Checkered", countdown: "", standings: [row("Sam P.", [9100, 9000]), row("Asher K.", [9500, 9400])] };

describe("bug hunt 24 #59: the Live flag card in a warm-up", () => {
  it("shows the warm-up's own countdown, not the last race's lap and winner, while the laptop still says Finished", async () => {
    const r = await live(finished);
    assert.match(r.bar().innerHTML, /Winner Sam P\./, "the race before, as it was");

    await r.send({ ...finished, flag: "WarmUp", countdown: "WARM-UP 2:00 LEFT" });
    assert.match(r.bar().className, /f-warmup/);
    assert.match(r.bar().innerHTML, /Free laps/);
    assert.match(r.bar().innerHTML, /warm-up 2:00 left/);
    assert.doesNotMatch(r.bar().innerHTML, /Lap 3/);
    assert.doesNotMatch(r.bar().innerHTML, /Leader/);
    assert.doesNotMatch(r.bar().innerHTML, /Sam P\./);
    assert.match(r.board(), /Sam P\./, "the last race's board stays below");
  });

  it("does the same over a kept board once the laptop has gone Idle", async () => {
    const r = await live(finished);
    await r.send({ state: "Idle", flag: "WarmUp", countdown: "WARM-UP 2:00 LEFT", standings: [] });
    assert.match(r.bar().innerHTML, /Free laps/);
    assert.match(r.bar().innerHTML, /warm-up 2:00 left/);
    assert.doesNotMatch(r.bar().innerHTML, /Leader|Lap 3|Sam P\./);
  });

  it("still names the leader and the lap in a race", async () => {
    const r = await live({ state: "Racing", flag: "Green", countdown: "10 LAPS TO GO", standings: [row("Sam P.", [9100, 9000])] });
    assert.match(r.bar().innerHTML, /Lap 3/);
    assert.match(r.bar().innerHTML, /Leader Sam P\./);
  });
});

describe("bug hunt 24 #343: a driver named like a built-in on the position chart", () => {
  it("keeps the board, the chart and the driver's card updating", async () => {
    const odd = (name: string) => ({ name, laps: [9000], best: 9000, lap: 2, cells: [{ text: name, emphasis: "none" }] });
    const r = await live({ state: "Racing", flag: "Green", countdown: "10 LAPS TO GO", standings: [odd("constructor"), odd("__proto__"), odd("toString")] });
    for (const name of ["constructor", "__proto__", "toString"]) {
      assert.match(r.board(), new RegExp(name), `the board shows ${name}`);
      assert.match(r.p.get("lposbody").innerHTML, new RegExp(name), `the chart has a line for ${name}`);
    }

    // The next lap still comes through.
    await r.send({ state: "Racing", flag: "Green", countdown: "9 LAPS TO GO", standings: [odd("toString"), odd("constructor"), odd("__proto__")] });
    assert.match(r.board(), /toString[\s\S]*constructor[\s\S]*__proto__/);
  });
});

describe("bug hunt 24 #60: a race reset part-way through", () => {
  const racer = (name: string, flying: number, ahead?: string) => ({
    name, laps: Array.from({ length: flying }, () => 9000), best: 9000, lap: flying + 1, ...(ahead ? { ahead } : {}),
    cells: [{ text: name, emphasis: "none" }],
  });
  const racing = { state: "Racing", flag: "Green", countdown: "10 LAPS TO GO", standings: [racer("Sam P.", 2), racer("Asher K.", 2, "+1.20")] };

  it("names no winner and says nothing about catching by the flag", async () => {
    const r = await live(racing, { "hg-follow-acme-rc": "Asher K." });
    assert.match(r.you(), /catch by the flag/, "live, as before");

    // Reset race: the laptop goes Idle with an empty board.
    await r.send({ state: "Idle", flag: "None", countdown: "", standings: [] });
    assert.doesNotMatch(r.bar().innerHTML, /Race over/);
    assert.doesNotMatch(r.bar().innerHTML, /Chequered flag/);
    assert.doesNotMatch(r.bar().innerHTML, /Winner/);
    assert.doesNotMatch(r.bar().className, /\bover\b|f-checkered/);
    assert.match(r.board(), /Sam P\./, "the board stays up");
    assert.doesNotMatch(r.you(), /catch by the flag/);
  });

  it("still shows a finished race as Race over with its winner", async () => {
    const r = await live(finished);
    await r.send({ state: "Idle", flag: "None", countdown: "", standings: [] });
    assert.match(r.bar().innerHTML, /Race over/);
    assert.match(r.bar().innerHTML, /Winner Sam P\./);
  });
});

describe("bug hunt 24 #344: the race night read in the club's zone", () => {
  // UTC+14: six hours back from 16:30 UTC is 00:30 the next day there, a
  // day no phone zone west of it reads.
  const kiritimati = (d: Date) => d.toLocaleDateString("en-CA", { timeZone: "Pacific/Kiritimati", year: "numeric", month: "2-digit", day: "2-digit" });
  const london = (d: Date) => d.toLocaleDateString("en-CA", { timeZone: "Europe/London", year: "numeric", month: "2-digit", day: "2-digit" });
  const local = (ms: number) => {
    const d = new Date(ms - 6 * 3600000);
    return d.getFullYear() + "-" + String(d.getMonth() + 1).padStart(2, "0") + "-" + String(d.getDate()).padStart(2, "0");
  };
  const homeNightOf = (ymd: unknown) =>
    new Function("hcYmd", `${fn(homePage("acme-rc", "Acme RC"), "nightOf")}; return nightOf;`)(ymd) as (ms: number) => string;
  const badgesEarnedDay = (ymd: unknown) =>
    new Function("hcYmd", `${fn(badgesPage("acme-rc", "Acme RC"), "earnedDay")}; return earnedDay;`)(ymd) as (when: unknown) => string;

  it("files Home's night by the club's day, not the phone's", () => {
    const at = Date.parse("2026-11-21T16:30:00Z");
    assert.equal(homeNightOf(kiritimati)(at), "2026-11-22");
    assert.equal(homeNightOf(london)(Date.parse("2026-11-21T20:30:00Z")), "2026-11-21");
    assert.equal(homeNightOf(london)(Date.parse("2026-11-22T05:30:00Z")), "2026-11-21", "the small hours are the night before");
  });

  it("falls back to the phone's own day with no club zone helper", () => {
    const at = Date.parse("2026-11-21T16:30:00Z");
    assert.equal(homeNightOf(undefined)(at), local(at));
  });

  it("links an achievement to the club's night", () => {
    assert.equal(badgesEarnedDay(kiritimati)("2026-11-21T16:30:00Z"), "2026-11-22");
    assert.equal(badgesEarnedDay(kiritimati)("not a time"), "");
    assert.equal(badgesEarnedDay(undefined)("2026-11-21T16:30:00Z"), local(Date.parse("2026-11-21T16:30:00Z")));
  });
});

describe("bug hunt 24 #345: Home's sponsor on a busy relay", () => {
  const NOW = (() => {
    const d = new Date();
    d.setHours(21, 0, 0, 0);
    return d.getTime();
  })();

  async function home(): Promise<{ page: FakePage; answer: (status: number) => void }> {
    let status = 200;
    const answer: Answerer = async (url) => {
      const path = url.replace("/api/v1/c/acme-rc", "");
      if (path === "/snapshot/sponsors") {
        return status === 200
          ? { status: 200, body: { publishedAt: new Date(NOW).toISOString(), body: { sponsors: [{ id: "s1", name: "Hobby Hut", tagline: "Kits" }] } } }
          : { status, body: {} };
      }
      if (path === "/sponsors/seen") return { status: 200, body: { ok: true } };
      return { status: 404, body: {} };
    };
    const { html, script } = pageParts(homePage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
    const page = new FakePage(html, answer, {});
    page.clock = NOW;
    page.get("now").dataset = { when: "" };
    page.run(script);
    await page.settle();
    return { page, answer: (s: number) => { status = s; } };
  }

  it("keeps the card through a busy answer", async () => {
    const { page, answer } = await home();
    assert.equal(page.get("spnight").hidden, false);
    answer(429);
    await page.advance(300000);
    assert.equal(page.get("spnight").hidden, false);
    assert.match(page.get("spnight").textContent, /Hobby Hut/);
  });

  it("still hides it once the relay says nothing is published", async () => {
    const { page, answer } = await home();
    answer(404);
    await page.advance(300000);
    assert.equal(page.get("spnight").hidden, true);
  });
});

describe("bug hunt 24 #61: Home's Racing tonight? alerts step", () => {
  const ELLA = "a1b2c3d4e5f60718293a";

  async function openHome(stored: Record<string, string>, subscribed: boolean): Promise<FakePage> {
    const started = Date.now();
    const answer: Answerer = async (url) => {
      if (url.endsWith("/snapshot/programme")) {
        return { status: 200, body: { ok: true, body: { event: { name: "Corn Dog Day", startedUtc: new Date(started).toISOString() }, lines: [] } } };
      }
      if (url.endsWith("/checkin/one")) return { status: 200, body: { ok: true, event: "Corn Dog Day", driver: { id: ELLA, checkedIn: true } } };
      return { status: 404 };
    };
    const { html, script } = pageParts(homePage("acme-rc", "Acme RC"));
    const page = new FakePage(html, answer, {});
    page.clock = started;
    for (const [k, v] of Object.entries({ "hg-me-acme-rc": ELLA, ...stored })) page.eval(`localStorage.setItem(${JSON.stringify(k)}, ${JSON.stringify(v)})`);
    // One push sign-up on the phone - here from follower alerts.
    page.eval(`navigator.serviceWorker = {
      getRegistration: () => Promise.resolve({ pushManager: { getSubscription: () => Promise.resolve(${subscribed ? "{ endpoint: 'https://push.example/one' }" : "null"}) } }),
    };`);
    page.run(script);
    await page.settle();
    return page;
  }

  it("does not tick Alerts on for a push sign-up the driver's own alerts did not make", async () => {
    const card = (await openHome({}, true)).get("rtw").innerHTML;
    assert.match(card, /<b>Turn on alerts<\/b>/);
    assert.doesNotMatch(card, /all set for tonight/);
  });

  it("folds to You're all set once the driver's own alerts are on", async () => {
    const card = (await openHome({ "hc-alerts:acme-rc": "1" }, true)).get("rtw").innerHTML;
    assert.match(card, /all set for tonight/);
  });

  it("still asks with the switch on but no push sign-up", async () => {
    const card = (await openHome({ "hc-alerts:acme-rc": "1" }, false)).get("rtw").innerHTML;
    assert.match(card, /<b>Turn on alerts<\/b>/);
  });
});
