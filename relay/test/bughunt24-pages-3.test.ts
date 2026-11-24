import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { DEFAULT_CONTEXT, homePage, livePage, nightPage, schedulePage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer, type FakeNode } from "./driverexp-rp1-fakepage.js";

/**
 * BUG HUNT 24 (23 Nov 2026), relay pages, group 3: Home and Live judging a
 * race on the relay's clock, Schedule's lateness on an old race day and its
 * provisional line running out, Me's "Who to ask" list under a poll, the
 * "Something wrong?" window on the relay's clock, and a returning driver's
 * first result of the night.
 */

const MIN = 60_000;
const STATE = "/api/v1/c/acme-rc/snapshot/state";
const racing = { state: "Racing", flag: "Green", countdown: "10 LAPS TO GO", standings: [] };

/** A frame the relay stamped at relayAt, with the relay's clock in the header. */
function stateAnswer(relayAt: number, publishedAt: number) {
  return {
    status: 200,
    body: { seq: 1, publishedAt: new Date(publishedAt).toISOString(), body: racing },
    headers: { "x-relay-now": String(relayAt) },
  };
}

describe("bug hunt 24 #346: Home's On track now on the relay's clock", () => {
  async function home(phone: number, relayAt: number, publishedAt: number): Promise<FakePage> {
    const answer: Answerer = async (url) => (url === STATE ? stateAnswer(relayAt, publishedAt) : { status: 404 });
    const { html, script } = pageParts(homePage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
    const page = new FakePage(html, answer, {});
    page.clock = phone;
    page.get("now").dataset = { when: "" };
    page.run(script);
    await page.settle();
    return page;
  }

  it("shows the running race on a phone five minutes fast", async () => {
    const relay = Date.parse("2026-10-22T19:00:00.000Z");
    const page = await home(relay + 5 * MIN, relay, relay);
    assert.match(page.get("now").innerHTML, /On track now/);
  });

  it("drops a laptop that went quiet three minutes ago on a phone five minutes slow", async () => {
    const relay = Date.parse("2026-10-22T19:00:00.000Z");
    const page = await home(relay - 5 * MIN, relay, relay - 3 * MIN);
    assert.doesNotMatch(page.get("now").innerHTML, /On track now/);
  });

  it("still shows a fresh race with the clocks agreeing", async () => {
    const relay = Date.parse("2026-10-22T19:00:00.000Z");
    const page = await home(relay, relay, relay - 10_000);
    assert.match(page.get("now").innerHTML, /On track now/);
  });
});

describe("bug hunt 24 #346: Live's on-air mark on the relay's clock", () => {
  async function live(phone: number, relayAt: number, publishedAt: number): Promise<Map<string, boolean>> {
    const flags = new Map<string, boolean>();
    const { html, script } = pageParts(livePage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
    const page: FakePage = new FakePage(html, async (url) => (url === STATE ? stateAnswer(relayAt + (page.clock - phone), publishedAt) : { status: 404 }));
    page.clock = phone;
    (page.body as unknown as { classList: unknown }).classList = {
      toggle: (name: string, on?: boolean) => { const v = on === undefined ? !flags.get(name) : !!on; flags.set(name, v); return v; },
      add: (name: string) => { flags.set(name, true); },
      remove: (name: string) => { flags.set(name, false); },
      contains: (name: string) => flags.get(name) === true,
    };
    page.run(script);
    await page.settle();
    return flags;
  }

  it("is on air mid-race on a phone five minutes fast", async () => {
    const relay = Date.parse("2026-10-22T19:00:00.000Z");
    const flags = await live(relay + 5 * MIN, relay, relay);
    assert.equal(flags.get("on-air"), true);
  });

  it("is off air for a laptop quiet three minutes on a phone five minutes slow", async () => {
    const relay = Date.parse("2026-10-22T19:00:00.000Z");
    const flags = await live(relay - 5 * MIN, relay, relay - 3 * MIN);
    assert.notEqual(flags.get("on-air"), true);
  });
});

describe("bug hunt 24 #347: Schedule's lateness on an old race day", () => {
  // 21:00 on the phone's own clock, whatever zone the machine is in, on
  // tonight's race night: hcIsTonight reads new Date(), which the fake page
  // leaves on the real clock (only Date.now is faked), so a fixed day would
  // never be tonight.
  const night = new Date(Date.now() - 6 * 60 * MIN);
  const TONIGHT = new Date(night.getFullYear(), night.getMonth(), night.getDate(), 21, 0, 0, 0).getTime();

  function local(dayMs: number, h: number, m: number): string {
    const d = new Date(dayMs);
    d.setHours(h, m, 0, 0);
    return d.toISOString();
  }

  async function schedule(dayMs: number): Promise<FakePage> {
    const doc = {
      event: { name: "Club Night", startedUtc: local(dayMs, 17, 0) },
      lines: [
        { id: "a", className: "Stock", time: "19:00", status: "done", startedUtc: local(dayMs, 19, 14) },
        { id: "b", className: "Stock", time: "19:20", status: "upcoming", estimate: "about 19:34" },
      ],
    };
    const answer: Answerer = async (url) => url.endsWith("/snapshot/programme")
      ? { status: 200, body: { seq: 1, publishedAt: local(dayMs, 19, 15), body: doc } }
      : { status: 404 };
    const { html, script } = pageParts(schedulePage("acme-rc", "Acme RC"));
    const page = new FakePage(html, answer);
    page.clock = TONIGHT;
    page.run(script);
    await page.settle();
    return page;
  }

  it("says nothing about running behind, and no about-times, a week on", async () => {
    const page = await schedule(TONIGHT - 7 * 24 * 60 * MIN);
    const board = page.get("board").innerHTML;
    assert.match(page.get("head").innerHTML, /Last race day/);
    assert.doesNotMatch(board, /Running/);
    assert.doesNotMatch(board, /class="late/);
    assert.doesNotMatch(board, /about /);
    assert.match(board, /14 min late/, "a started line still says when it went");
  });

  it("still says how late tonight is running, with the laptop's about-time", async () => {
    const page = await schedule(TONIGHT);
    const board = page.get("board").innerHTML;
    assert.match(page.get("head").innerHTML, /Today/);
    assert.match(board, /Running 14 min behind/);
    assert.match(board, /about 19:34/);
  });
});

describe("bug hunt 24 #348: Schedule's provisional line when the window closes", () => {
  async function schedule(): Promise<FakePage> {
    const start = Date.parse("2026-10-22T19:00:00.000Z");
    const qual = {
      classes: [{
        name: "Stock",
        provisional: "Provisional until 20:42",
        provisionalUntil: start + MIN,
        rows: [{ pos: 1, name: "Sam P.", main: "A", laps: 18, timeMs: 302103 }],
      }],
    };
    const answer: Answerer = async (url) => url.endsWith("/snapshot/qualifying")
      ? { status: 200, body: { seq: 1, publishedAt: new Date(start).toISOString(), body: qual } }
      : { status: 404 };
    const { html, script } = pageParts(schedulePage("acme-rc", "Acme RC"));
    const page = new FakePage(html, answer);
    page.clock = start;
    page.run(script);
    await page.settle();
    return page;
  }

  it("goes at the next read of the same copy once the time has passed", async () => {
    const page = await schedule();
    assert.match(page.get("qual").innerHTML, /Provisional until 20:42/);
    await page.advance(90_000);
    assert.doesNotMatch(page.get("qual").innerHTML, /Provisional until/);
    assert.match(page.get("qual").innerHTML, /Sam P\./, "the table stays");
  });

  it("stays while the window is still open", async () => {
    const page = await schedule();
    await page.advance(30_000);
    assert.match(page.get("qual").innerHTML, /Provisional until 20:42/);
  });
});

const CODE = "a1b2c3d4e5f60718293a";

async function openNight(answer: Answerer): Promise<FakePage> {
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, answer, { hash: `#d=${CODE}` });
  page.eval(`
    this.SpeechSynthesisUtterance = function (text) { this.text = text; };
    window.speechSynthesis = { speak() {}, cancel() {} };
    location.reload = () => {};
  `);
  page.run(script);
  await page.settle();
  return page;
}

describe("bug hunt 24 #349: Me's Who to ask list under a poll", () => {
  const live = {
    at: "2026-10-22T19:00:00Z",
    next: { race: "Q3 Heat 1 (Stock Buggy)", away: 4, slot: 4 },
    marshal: { race: "Q2 Heat 3", post: "Turn 3", away: 3, round: "c".repeat(32), choices: [{ k: "d".repeat(12), name: "Max P." }, { k: "e".repeat(12), name: "Jo R." }] },
  };
  const answer: Answerer = async (url, body) => {
    if (url.endsWith("/night")) {
      if (body["part"] === "live") return { status: 200, body: { ok: true, live } };
      return { status: 200, body: { ok: true, night: null, live, me: { name: "Sam Parker" }, asksOpen: true } };
    }
    return { status: 404 };
  };
  const coverSelect = (page: FakePage): FakeNode | undefined =>
    page.get("lv").find((n) => n.tagName === "SELECT" && n.getAttribute("aria-label") === "Who to ask")[0];
  const livePolls = (page: FakePage) => page.sent.filter((s) => s.body["part"] === "live").length;

  it("keeps the list the driver has open through the next poll, then lets go", async () => {
    const page = await openNight(answer);
    const pick = coverSelect(page);
    assert.ok(pick, "the list is on the card");

    // The driver opens the list.
    page.eval(`document.activeElement = (function find(n) {
      if (n.tagName === 'SELECT' && n.getAttribute('aria-label') === 'Who to ask') return n;
      for (const c of n.children) { const r = find(c); if (r) return r; }
      return null;
    })(document.getElementById('lv')); document.activeElement.fire('focus');`);

    const polls = livePolls(page);
    await page.advance(21_000);
    assert.ok(livePolls(page) > polls, "the live card was polled");
    assert.equal(coverSelect(page), pick, "the same list, still open");

    // Half a minute on, the card updates again even with the list focused.
    await page.advance(20_000);
    assert.notEqual(coverSelect(page), pick);
  });

  it("is built again at a poll while the driver is not in it", async () => {
    const page = await openNight(answer);
    const pick = coverSelect(page);
    await page.advance(21_000);
    assert.ok(coverSelect(page));
    assert.notEqual(coverSelect(page), pick);
  });
});

describe("bug hunt 24 #350: Something wrong? on the relay's clock", () => {
  it("closes by the relay's time, not the phone's", async () => {
    const page = await openNight(async () => ({ status: 404 }));
    // The phone five minutes slow: the window closed a minute ago.
    page.eval("skew = 300000");
    assert.equal(page.eval("queryStillOpen({ sid: 'a', qUntil: Date.now() + 240000 })"), false);
    assert.equal(page.eval("queryStillOpen({ sid: 'a', qUntil: Date.now() + 360000 })"), true);
    // The phone five minutes fast: a window with four minutes left stays.
    page.eval("skew = -300000");
    assert.equal(page.eval("queryStillOpen({ sid: 'a', qUntil: Date.now() - 60000 })"), true);
    assert.equal(page.eval("queryStillOpen({ sid: 'a', qUntil: 0 })"), true, "no window, as before");
  });
});

describe("bug hunt 24 #351: a returning driver's first result of the night", () => {
  const race = (n: number) => ({ sid: String(n).repeat(32).slice(0, 32), label: "Q" + n + " Heat 1", pos: 2, of: 6, laps: 9, lapsMs: [9000, 9100] });
  const races = (count: number) => Array.from({ length: count }, (_, i) => race(i + 1));

  async function open(): Promise<{ page: FakePage; serve: (night: string, count: number) => void }> {
    let current = { night: "2026-10-15", count: 5 };
    const answer: Answerer = async (url, body) => {
      if (url.endsWith("/night")) {
        if (body["part"] === "live") return { status: 200, body: { ok: true } };
        return { status: 200, body: { ok: true, night: { driver: "Ella Vator", title: "Club Night", night: current.night, lines: ["Raced."], races: races(current.count) } } };
      }
      return { status: 404 };
    };
    const page = await openNight(answer);
    page.eval("alertsOn = true");
    page.eval(`localStorage.setItem('hc-alert-kinds:acme-rc', '["race","marshal","result","news"]')`);
    return { page, serve: (night, count) => { current = { night, count }; } };
  }

  const banner = (page: FakePage) => String(page.eval("(document.getElementById('hc-banner') || { textContent: '' }).textContent"));

  it("says Your result is in for tonight's first race after a longer night last week", async () => {
    const { page, serve } = await open();
    assert.equal(page.eval("racesSeen"), 5);
    serve("2026-10-22", 1);
    const before = page.vibrations.length;
    page.eval("refreshNight()");
    await page.settle();
    assert.equal(page.vibrations.length, before + 1);
    assert.match(banner(page), /Your result is in/);
  });

  it("stays quiet for the same night with no new race, and alerts the next one", async () => {
    const { page, serve } = await open();
    const before = page.vibrations.length;
    page.eval("refreshNight()");
    await page.settle();
    assert.equal(page.vibrations.length, before, "the same night, the same five races");
    serve("2026-10-15", 6);
    page.eval("refreshNight()");
    await page.settle();
    assert.equal(page.vibrations.length, before + 1);
  });
});
