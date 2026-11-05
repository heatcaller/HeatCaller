import assert from "node:assert/strict";
import { afterEach, describe, it } from "node:test";
import { badgesPage, checkinPage, nightPage, resultsPage } from "../src/pages.js";
import { call } from "./helpers.js";
import { createContext, runInContext } from "node:vm";
import { SAM, checkinDoc, liveDoc, openEverything, publish, startDriver, type DriverHarness } from "./driverexp-r1-helpers.js";
import { FakePage, pageParts, type Answerer, type FakeNode } from "./driverexp-rp1-fakepage.js";

/**
 * THE NIGHT PAGE'S READ SIDE (22 Oct 2026, driver experience, RP1).
 *
 * The night answer now carries the driver's own live entry (next race,
 * marshal duty, transponder, check-in, first night) and their race clips,
 * and the night itself gains races, improve, progress and reach. These run
 * the whole page script against a small fake browser and check what is
 * drawn, what is sent, and - most of all - that the badge code still goes
 * only into POST bodies and that everything new is built as text.
 */

const CODE = "a1b2c3d4e5f60718293a";
const CLIP = "0123456789abcdef0123456789abcdef";
const RACE = "fedcba9876543210fedcba9876543210";

function night(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return { driver: "Bill Hargrove", title: "Club Night", when: "Thursday 22 October 2026", night: "2026-10-22", lines: ["Bill raced twice."], ...extra };
}

/** The night page, run, answering every relay call with `answer`. */
async function open(answer: Answerer, hash = `#d=${CODE}`): Promise<FakePage> {
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, answer, { hash });
  page.run(script);
  await page.settle();
  return page;
}

/** Answers the night read with `full`, the live poll with `poll()`, and check-in with `one`. */
function relay(full: unknown, options: { poll?: () => unknown; one?: { status: number; body?: unknown }; tap?: { status: number; body?: unknown } } = {}): Answerer {
  return async (url, body) => {
    if (url.endsWith("/night")) {
      if (body["part"] === "live") return { status: 200, body: options.poll ? options.poll() : { ok: true } };
      return full === 404 ? { status: 404, body: { ok: false, error: "Not found" } } : { status: 200, body: full };
    }

    if (url.endsWith("/checkin/one")) return options.one ?? { status: 409, body: { ok: false } };
    if (url.endsWith("/checkin/tap")) return options.tap ?? { status: 200, body: { ok: true, checkedIn: body["wanted"] } };

    return { status: 404 };
  };
}

/** Every element under `root` that came from markup rather than createElement. */
function markupUnder(root: FakeNode): FakeNode[] {
  return root.find((n) => n !== root && n.hadMarkup);
}

describe("night: null with a live entry", () => {
  it("draws the live card and says nothing is filed yet, not the refusal", async () => {
    const page = await open(relay({
      ok: true,
      night: null,
      live: { at: "2026-10-22T19:00:00Z", next: { race: "Q2 Heat 1 (Stock Buggy)", away: 2, slot: 3, now: false, words: "2 races before yours" } },
    }));

    const lv = page.get("lv").textContent;
    assert.match(lv, /2 races before yours/);
    assert.match(lv, /Q2 Heat 1 \(Stock Buggy\) · grid slot 3/);
    assert.match(page.get("nt").textContent, /Nothing is filed for you yet tonight/);
    assert.doesNotMatch(page.get("nt").drawn(), /No night was found/);
  });

  it("neither a night nor a live entry is still the one refusal", async () => {
    const page = await open(relay({ ok: true, night: null }));

    assert.match(page.get("nt").drawn(), /No night was found for that code/);
    assert.equal(page.get("lv").children.length, 0);
  });

  it("a 404 is the one refusal, and nothing is polled after it", async () => {
    const page = await open(relay(404));

    assert.match(page.get("nt").drawn(), /No night was found for that code/);
    await page.advance(60000);
    assert.equal(page.sent.filter((s) => s.body["part"] === "live").length, 0);
  });
});

describe("the code stays out of every address", () => {
  it("every call is a POST to a fixed address with the code only in the body", async () => {
    const page = await open(relay({ ok: true, night: night(), live: { next: { race: "A", away: 0 } } }, {
      one: { status: 200, body: { ok: true, driver: { id: CODE, name: "Bill", checkedIn: false }, nights: true } },
    }));

    await page.advance(45000);
    const tap = page.button(page.get("lv"), /^Check in$/);
    assert.ok(tap, "the check-in button is offered");
    tap.click();
    await page.settle();

    assert.ok(page.sent.length >= 4, "the read, check-in, two polls and a tap");

    for (const s of page.sent) {
      assert.doesNotMatch(s.url, new RegExp(CODE), "never in an address");
      assert.match(s.url, /^\/api\/v1\/c\/acme-rc\/(night|checkin\/one|checkin\/tap)$/);
      assert.equal(s.body["id"], CODE);
    }

    assert.deepEqual(page.sent.filter((s) => s.body["part"] === "live").map((s) => Object.keys(s.body).sort()), [["id", "part"], ["id", "part"]]);
  });

  it("the source concatenates the code into nothing but the fragment", () => {
    const { script } = pageParts(nightPage("acme-rc", "Acme RC"));
    const code = script.replace(/^\s*\/\/.*$/gm, "");

    assert.equal((code.match(/\+ CODE/g) ?? []).length, 1);
    assert.doesNotMatch(code, /fetch\([^)]*CODE/);
  });
});

describe("the live poll", () => {
  it("asks for the live part every twenty seconds while the page is on screen", async () => {
    const page = await open(relay({ ok: true, night: night(), live: {} }));
    const polls = () => page.sent.filter((s) => s.body["part"] === "live").length;

    await page.advance(19000);
    assert.equal(polls(), 0);
    await page.advance(1000);
    assert.equal(polls(), 1);
    await page.advance(20000);
    assert.equal(polls(), 2);

    page.hidden = true;
    await page.advance(60000);
    assert.equal(polls(), 2, "not while hidden");
  });

  it("a poll without a live entry takes the card away but keeps the night", async () => {
    let entry: unknown = { next: { race: "Q1 Heat 2", away: 1 } };
    const page = await open(relay({ ok: true, night: night(), live: entry }, { poll: () => ({ ok: true, ...(entry ? { live: entry } : {}) }) }));

    assert.match(page.get("lv").textContent, /Q1 Heat 2/);
    entry = undefined;
    await page.advance(20000);

    assert.doesNotMatch(page.get("lv").textContent, /Q1 Heat 2/);
    assert.match(page.get("nt").drawn(), /Bill raced twice/);
  });

  it("counts down from the laptop's estimate, on the relay's clock", async () => {
    // The relay's clock says 19:00; the phone's says 18:55. "about 7 min"
    // must be measured from the relay's.
    const eta = "2026-10-22T19:07:00Z";
    const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
    // The relay's clock runs five minutes ahead of the phone's, all along.
    const page: FakePage = new FakePage(html, async (url, body) => ({
      status: 200,
      date: new Date(page.clock + 300000).toUTCString(),
      body: !url.endsWith("/night") ? { ok: false }
        : body["part"] === "live" ? { ok: true, live: { next: { race: "A", away: 1, eta } } }
        : { ok: true, night: null, live: { next: { race: "A", away: 1, eta } } },
    }), { hash: `#d=${CODE}` });
    page.clock = Date.parse("2026-10-22T18:55:00Z");
    page.run(script);
    await page.settle();

    assert.match(page.get("lc-eta").textContent, /^about 7 min \(/);

    await page.advance(120000);
    assert.match(page.get("lc-eta").textContent, /^about 5 min \(/);
  });
});

describe("the countdown words", () => {
  it("minutes, due now, and a clock time when it is far off", async () => {
    const page = await open(relay(404));
    const now = Date.parse("2026-10-22T19:00:00Z");
    const words = (eta: string | null) => page.eval(`etaWords(${JSON.stringify(eta)}, ${now})`) as string;

    assert.match(words("2026-10-22T19:07:00Z"), /^about 7 min \(.+\)$/);
    assert.match(words("2026-10-22T19:01:00Z"), /^about 1 min/);
    assert.match(words("2026-10-22T18:58:00Z"), /^due now/);
    assert.match(words("2026-10-22T21:00:00Z"), /^at about /);
    assert.equal(words(null), "");
    assert.equal(words("soon"), "");
  });
});

describe("alerts", () => {
  it("buzz when the race becomes next and again when it is staged, and not before they are turned on", async () => {
    let next: Record<string, unknown> = { race: "Q2 Heat 1", away: 2, slot: 3 };
    const page = await open(relay({ ok: true, night: null, live: { next } }, { poll: () => ({ ok: true, live: { next } }) }));

    // Off: a change of call-up does nothing.
    next = { race: "Q2 Heat 1", away: 1, slot: 3 };
    await page.advance(20000);
    assert.equal(page.vibrations.length, 0);

    page.button(page.get("lv"), /Turn on alerts/)!.click();
    assert.equal(page.vibrations.length, 1, "a test buzz on the tap");
    assert.ok(page.button(page.get("lv"), /Alerts on/));

    // Unchanged: nothing.
    await page.advance(20000);
    assert.equal(page.vibrations.length, 1);

    next = { race: "Q2 Heat 1", away: 0, slot: 3 };
    await page.advance(20000);
    assert.equal(page.vibrations.length, 2, "their race is next");

    await page.advance(20000);
    assert.equal(page.vibrations.length, 2, "once, not every poll");

    next = { race: "Q2 Heat 1", away: 0, slot: 3, now: true, staged: true };
    await page.advance(20000);
    assert.equal(page.vibrations.length, 3, "staged");

    next = { race: "Q2 Heat 1", away: 0, slot: 3, now: true, staged: false };
    await page.advance(20000);
    assert.equal(page.vibrations.length, 3, "going green is not a call-up");
  });

  it("the rule on its own", async () => {
    const page = await open(relay(404));
    const worth = (before: string, n: unknown) => page.eval(`worthABuzz(${JSON.stringify(before)}, ${JSON.stringify(n)})`);

    assert.equal(worth("", { race: "A", away: 0 }), false, "never on the first look");
    assert.equal(worth("A|1|false", { race: "A", away: 0 }), true);
    assert.equal(worth("A|0|false", { race: "A", away: 0 }), false);
    assert.equal(worth("A|0|false", { race: "A", away: 0, now: true, staged: true }), true);
    assert.equal(worth("A|2|false", { race: "A", away: 1 }), false);
    assert.equal(worth("A|0|false", null), false);
  });
});

describe("marshal duty", () => {
  it("names the race, the post and how far off it is", async () => {
    const page = await open(relay({ ok: true, night: null, live: { marshal: { race: "Q2 Heat 2 (Stock Buggy)", post: "Turn 3", away: 1 } } }));
    const lv = page.get("lv").textContent;

    assert.match(lv, /Your marshal duty/);
    assert.match(lv, /Q2 Heat 2 \(Stock Buggy\) - Turn 3/);
    assert.match(lv, /1 race before it/);
  });

  it("says nothing about marshalling when there is no duty", async () => {
    const page = await open(relay({ ok: true, night: null, live: { next: { race: "A", away: 1 } } }));

    assert.doesNotMatch(page.get("lv").textContent, /marshal/i);
  });
});

describe("check-in from the night page", () => {
  it("is hidden when check-in is closed (409)", async () => {
    const page = await open(relay({ ok: true, night: night() }, { one: { status: 409, body: { ok: false } } }));

    assert.equal(page.button(page.get("lv"), /Check in/), undefined);
  });

  it("checks in with the code and shows it at once", async () => {
    const page = await open(relay({ ok: true, night: night() }, {
      one: { status: 200, body: { ok: true, driver: { id: CODE, name: "Bill", checkedIn: false }, nights: true } },
    }));

    page.button(page.get("lv"), /^Check in$/)!.click();
    await page.settle();

    const tap = page.sent.find((s) => s.url.endsWith("/checkin/tap"));
    assert.deepEqual(tap?.body, { id: CODE, wanted: true });
    assert.ok(page.button(page.get("lv"), /Checked in/));
  });

  it("a tap answered 409 takes the button away", async () => {
    const page = await open(relay({ ok: true, night: night() }, {
      one: { status: 200, body: { ok: true, driver: { id: CODE, name: "Bill", checkedIn: false } } },
      tap: { status: 409, body: { ok: false } },
    }));

    page.button(page.get("lv"), /^Check in$/)!.click();
    await page.settle();

    assert.equal(page.button(page.get("lv"), /Check in/), undefined);
  });

  it("the live entry says checked in without another call", async () => {
    const page = await open(relay({ ok: true, night: null, live: { checkedIn: true } }, {
      one: { status: 200, body: { ok: true, driver: { id: CODE, name: "Bill", checkedIn: false } } },
    }));

    assert.ok(page.button(page.get("lv"), /Checked in/));
  });
});

describe("the transponder check", () => {
  it("watches every five seconds and says when the car was seen", async () => {
    let seen: Record<string, unknown> = { at: "2026-10-22T18:40:00Z", signal: "good" };
    const page = await open(relay({ ok: true, night: null, live: { seen } }, { poll: () => ({ ok: true, live: { seen } }) }));

    assert.match(page.get("lv").textContent, /Last seen at .+, signal good/);

    page.button(page.get("lv"), /Check my transponder/)!.click();
    assert.match(page.get("lv").textContent, /Drive past the timing line now/);
    assert.equal(page.button(page.get("lv"), /Check my transponder/), undefined, "no second press while waiting");

    await page.advance(5000);
    assert.equal(page.sent.filter((s) => s.body["part"] === "live").length, 1, "five seconds, not twenty");
    assert.match(page.get("lv").textContent, /Drive past/);

    seen = { at: "2026-10-22T19:00:10Z", signal: "weak" };
    await page.advance(5000);

    assert.match(page.get("lv").textContent, /Seen at .+, signal weak/);
    assert.ok(page.button(page.get("lv"), /Check my transponder/), "can be run again");
  });

  it("gives up after three minutes with advice", async () => {
    const seen = { at: "2026-10-22T18:40:00Z", signal: "good" };
    const page = await open(relay({ ok: true, night: null, live: { seen } }, { poll: () => ({ ok: true, live: { seen } }) }));

    page.button(page.get("lv"), /Check my transponder/)!.click();
    await page.advance(185000);

    assert.match(page.get("lv").textContent, /Not seen in 3 minutes\. Check it is switched on/);
  });

  it("a sighting from before the press does not count", async () => {
    const page = await open(relay(404));
    const pressed = Date.parse("2026-10-22T19:00:00Z");
    const s = (at: string, baseline: string) => page.eval(`sighted(${JSON.stringify({ at, signal: "good" })}, ${pressed}, ${JSON.stringify(baseline)})`);

    assert.equal(s("2026-10-22T18:59:55Z", "2026-10-22T18:59:55Z"), false, "the one already on screen");
    assert.equal(s("2026-10-22T18:59:55Z", "2026-10-22T18:40:00Z"), true, "rounded down to ten seconds");
    assert.equal(s("2026-10-22T19:00:20Z", "2026-10-22T18:40:00Z"), true);
  });
});

describe("the first-night guide", () => {
  for (const [what, body] of [
    ["the night", { ok: true, night: night({ firstNight: true }) }],
    ["the live entry", { ok: true, night: null, live: { firstNight: true } }],
  ] as const) {
    it(`appears when ${what} says so`, async () => {
      const page = await open(relay(body));
      const guide = page.get("gd").find((n) => n.tagName === "DETAILS")[0];

      assert.ok(guide, "a collapsible guide");
      assert.match(guide.textContent, /Your first night/);
      assert.match(guide.textContent, /Marshal after your race/);
      assert.match(guide.textContent, /jump start/);
    });
  }

  it("is not there otherwise", async () => {
    const page = await open(relay({ ok: true, night: night(), live: { next: { race: "A", away: 1 } } }));

    assert.equal(page.get("gd").find((n) => n.tagName === "DETAILS").length, 0);
    assert.doesNotMatch(page.get("gd").textContent, /first night/i);
  });

  it("stays open across the poll's redraws once opened", async () => {
    const page = await open(relay({ ok: true, night: null, live: { firstNight: true } }, { poll: () => ({ ok: true, live: { firstNight: true } }) }));
    const guide = page.get("gd").find((n) => n.tagName === "DETAILS")[0]!;

    guide.open = true;
    guide.fire("toggle");
    await page.advance(20000);

    assert.equal(page.get("gd").find((n) => n.tagName === "DETAILS")[0]!.open, true);
  });

  it("goes for good once they tap Got it, poll or no poll", async () => {
    const page = await open(relay({ ok: true, night: null, live: { firstNight: true } }, { poll: () => ({ ok: true, live: { firstNight: true } }) }));
    const done = page.button(page.get("gd"), /^Got it/);

    assert.ok(done, "a way to put it away once read");
    done.click();
    assert.equal(page.get("gd").find((n) => n.tagName === "DETAILS").length, 0);

    await page.advance(20000);
    assert.equal(page.get("gd").find((n) => n.tagName === "DETAILS").length, 0, "the next redraw leaves it gone");
  });
});

describe("the night's own extras", () => {
  const laps = [9100, 8800, 8650, 8700, 30000, 8600, 8720];

  const full = {
    ok: true,
    night: night({
      races: [
        { sid: RACE, label: "Q1 Heat 2", at: "2026-10-22T18:30:00Z", pos: 3, of: 8, start: 5, laps: 7, bestMs: 8600, lapsMs: laps,
          pen: ["+5 s - jump start", "Warning 1 - <img src=x onerror=alert(1)>"] },
        { sid: "11111111111111111111111111111111", label: "Q2 Heat 2", pos: 0, of: 8, laps: 3, bestMs: 9000, out: "DNF" },
      ],
      improve: "Your middle lap is 0.40 s quicker than in your first race tonight (Q1 Heat 2)",
      progress: {
        layout: "Club Oval",
        nights: Array.from({ length: 8 }, (_, i) => ({ night: `2026-09-${String(i + 1).padStart(2, "0")}`, bestMs: 9500 - i * 50 })),
        since: "Your best ever on Club Oval",
      },
      reach: [
        { id: "podium-regular", name: "Podium Regular", have: 3, need: 5, noun: "podiums", words: "2 more podiums for Podium Regular" },
        "1 more race for Regular",
        { name: "Century", have: 90, need: 100, noun: "laps" },
        "Fourth one, never shown",
      ],
    }),
    clips: [
      { url: `/c/acme-rc/clip/${CLIP}.mp3`, download: `/c/acme-rc/clip/${CLIP}.mp3?download=1`, label: "Q1 Heat 2", at: "2026-10-22T18:36:00Z", race: RACE },
      { url: `/c/acme-rc/clip/${"2".repeat(32)}.mp3`, download: `/c/acme-rc/clip/${"2".repeat(32)}.mp3?download=1`, label: "Practice", at: "2026-10-22T18:00:00Z" },
      { url: "https://evil.example/x.mp3", download: "https://evil.example/x.mp3?download=1", label: "Bad" },
      { url: `/c/brookside/clip/${CLIP}.mp3`, download: `/c/brookside/clip/${CLIP}.mp3?download=1`, label: "Other club" },
      { url: `/c/acme-rc/clip/${CLIP}.mp3`, download: `/c/acme-rc/clip/${"3".repeat(32)}.mp3?download=1`, label: "Mismatched" },
    ],
  };

  // Since 28 Oct 2026 the races are a list with one open at a time - the
  // latest by itself - so a test about an earlier race opens it first.
  function openRace(page: FakePage, label: string): void {
    const row = page.get("nx").find((n) => n.className === "rbtn" && n.textContent.startsWith(label))[0];
    assert.ok(row, `a row for ${label}`);
    row.click();
  }

  it("draws one card per race with its facts and penalty lines", async () => {
    const page = await open(relay(full));
    openRace(page, "Q1 Heat 2");
    const nx = page.get("nx");
    const text = nx.textContent;

    assert.match(text, /Your races/);
    assert.match(text, /Q1 Heat 2/);
    assert.match(text, /3rd of 8 · started 5th · 7 laps · best 8\.600/);
    assert.match(text, /Did not finish · 3 laps/);
    assert.match(text, /\+5 s - jump start/);

    // The club's typing as text, never as an element.
    assert.match(text, /Warning 1 - <img src=x onerror=alert\(1\)>/);
    assert.equal(nx.find((n) => n.tagName === "IMG").length, 0);
    assert.deepEqual(markupUnder(nx), [], "nothing under #nx came from markup");
  });

  it("draws the lap chart: a point per lap, the best marked, the crash lap held at the edge", async () => {
    const page = await open(relay(full));
    openRace(page, "Q1 Heat 2");
    const chart = page.get("nx").find((n) => n.tagName === "SVG" && n.getAttribute("class") === "lapchart")[0];

    assert.ok(chart, "a chart for the race with laps");
    const line = chart.find((n) => n.tagName === "POLYLINE")[0]!;
    assert.equal(line.getAttribute("points")!.split(" ").length, laps.length);
    assert.equal(chart.find((n) => n.tagName === "CIRCLE" && n.getAttribute("class") === "slow").length, 1, "the 30 s lap");
    assert.match(chart.getAttribute("aria-label")!, /best 8\.600 on lap 6/);

    // The quickest lap is the highest point (smallest y).
    const ys = line.getAttribute("points")!.split(" ").map((p) => Number(p.split(",")[1]));
    assert.equal(Math.min(...ys), ys[5]);

    // The DNF race had no laps, so no chart.
    assert.equal(page.get("nx").find((n) => n.tagName === "SVG" && n.getAttribute("class") === "lapchart").length, 1);
  });

  it("shows improvement, six bars of progress with the since line, and at most three in reach", async () => {
    const page = await open(relay(full));
    const nx = page.get("nx");

    assert.match(nx.textContent, /Your improvement tonight/);
    assert.match(nx.textContent, /0\.40 s quicker/);
    assert.match(nx.textContent, /Your progress on Club Oval/);

    const bars = nx.find((n) => n.tagName === "RECT");
    assert.equal(bars.length, 6, "the last six nights");
    assert.equal(bars[5]!.getAttribute("class"), "tonight");
    assert.ok(Number(bars[5]!.getAttribute("height")) > Number(bars[0]!.getAttribute("height")), "quicker stands taller");
    assert.match(nx.textContent, /Your best ever on Club Oval/);

    assert.match(nx.textContent, /Achievements in reach/);
    assert.match(nx.textContent, /2 more podiums for Podium Regular/);
    assert.match(nx.textContent, /1 more race for Regular/);
    assert.match(nx.textContent, /10 more laps for Century/);
    assert.doesNotMatch(nx.textContent, /Fourth one/);
  });

  it("plays only the relay's own clips for this club, the race's own in its card", async () => {
    const page = await open(relay(full));
    openRace(page, "Q1 Heat 2");
    const nx = page.get("nx");
    const players = nx.find((n) => n.tagName === "AUDIO");

    assert.deepEqual(players.map((p) => p.src), [`/c/acme-rc/clip/${CLIP}.mp3`, `/c/acme-rc/clip/${"2".repeat(32)}.mp3`]);
    assert.ok(players.every((p) => p.preload === "none" && p.controls));

    const links = nx.find((n) => n.tagName === "A");
    assert.deepEqual(links.map((a) => a.href), [`/c/acme-rc/clip/${CLIP}.mp3?download=1`, `/c/acme-rc/clip/${"2".repeat(32)}.mp3?download=1`]);
    assert.ok(links.every((a) => a.getAttribute("download") === ""));

    // The race's clip sits in its card; the other under "Your race calls".
    const card = nx.find((n) => n.className === "rc")[0]!;
    assert.equal(card.find((n) => n.tagName === "AUDIO").length, 1);
    assert.match(nx.textContent, /Your race calls/);
    assert.doesNotMatch(nx.drawn(), /evil|brookside|Mismatched/);
  });

  it("a night from an older laptop draws exactly as before", async () => {
    const page = await open(relay({ ok: true, night: night() }));

    // Its facts as before; its cards (4 Nov 2026) each say when they fill in.
    assert.match(page.get("nt").drawn(), /Bill raced twice/);
    assert.match(page.get("n-races").textContent, /Your races show here after each one/);
    assert.match(page.get("n-score").textContent, /Your driver score shows here after your first race/);
  });
});

describe("the results page penalty chip", () => {
  /** penChip on its own, lifted out of the results page's script. */
  async function chip(row: unknown): Promise<string> {
    const { script } = pageParts(resultsPage("acme-rc", "Acme RC"));
    const from = script.indexOf("function penChip(");
    assert.notEqual(from, -1);
    let depth = 0;
    let to = from;
    for (let i = script.indexOf("{", from); i < script.length; i++) {
      if (script[i] === "{") depth++;
      if (script[i] === "}") depth--;
      if (depth === 0) { to = i; break; }
    }
    const context = createContext({});
    runInContext(script.slice(from, to + 1), context);
    return runInContext(`penChip(${JSON.stringify(row)})`, context) as string;
  }

  it("shows time added and warnings as numbers only", async () => {
    assert.match(await chip({ penS: 5 }), />\+5s</);
    assert.match(await chip({ warn: 1 }), />1 warning</);
    assert.match(await chip({ penS: 10, warn: 2 }), />\+10s · 2 warnings</);
  });

  it("draws nothing for a clean row, an old row, or anything that is not a number", async () => {
    assert.equal(await chip({}), "");
    assert.equal(await chip({ penS: 0, warn: 0 }), "");
    assert.equal(await chip({ penS: "<b>5</b>", warn: "x" }), "");
    assert.equal(await chip(null), "");
  });

  // 28 Oct 2026: the time column is LiveRC's Laps/Time now ("20/3:10.513").
  it("is in the time column of every row", () => {
    assert.match(resultsPage("acme-rc", "Acme RC"), /'<td class="n">' \+ lapsAndTime \+ penChip\(r\) \+ '<\/td>'/);
  });
});

describe("the badges page: what is next for the followed driver", () => {
  const catalogue = { badges: [{ id: "podium-regular", name: "Podium Regular", tier: "Silver", description: "Five podiums." }] };
  const achievements = {
    recent: [],
    drivers: [
      { name: "Sam Parker", count: 3, badges: [], next: [{ id: "podium-regular", left: 2, noun: "podiums" }, { id: "unknown", left: 1, noun: "x" }] },
      { name: "Bill Travers", count: 1, badges: [], next: [{ id: "podium-regular", left: 4, noun: "podiums" }] },
    ],
  };

  async function badges(follow: string | null): Promise<FakePage> {
    const { html, script } = pageParts(badgesPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async (url) => ({
      status: 200,
      body: { body: url.endsWith("/snapshot/badges") ? catalogue : achievements },
    }));
    if (follow !== null) page.store.set("hg-follow-acme-rc", follow);
    page.run(script);
    await page.settle();
    return page;
  }

  it("shows only the followed driver's", async () => {
    const page = await badges("Sam Parker");
    const next = page.get("nextup");

    assert.equal(next.hidden, false);
    assert.match(next.textContent, /Next for Sam Parker/);
    assert.match(next.textContent, /2 more podiums for Podium Regular/);
    assert.doesNotMatch(next.textContent, /4 more/);
    assert.deepEqual(markupUnder(next), []);
  });

  it("shows nothing when nobody is followed", async () => {
    const page = await badges(null);

    assert.equal(page.get("nextup").hidden, true);
    assert.equal(page.get("nextup").textContent, "");
  });
});

describe("the badge page's first-night guide", () => {
  async function checkin(firstNight: boolean): Promise<FakePage> {
    const { html, script } = pageParts(checkinPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async () => ({
      status: 200,
      body: { ok: true, event: "Club Night", nights: true, driver: { id: CODE, name: "Bill", checkedIn: false }, ...(firstNight ? { firstNight: true } : {}) },
    }), { search: `?d=${CODE}` });
    page.run(script);
    await page.settle();
    return page;
  }

  it("appears under their card when checkin/one says it is their first night", async () => {
    const page = await checkin(true);
    const guide = page.get("ci").find((n) => n.tagName === "DETAILS")[0];

    assert.ok(guide);
    assert.match(guide.textContent, /Your first night/);
    assert.match(page.get("ci").drawn(), /Check in/);
  });

  it("is not there otherwise", async () => {
    const page = await checkin(false);

    assert.equal(page.get("ci").find((n) => n.tagName === "DETAILS").length, 0);
  });
});

// --- Against the real relay ------------------------------------------------

describe("against R1's relay", () => {
  let h: DriverHarness | undefined;

  afterEach(async () => {
    await h?.close();
    h = undefined;
  });

  it("draws a driver before their first race from the relay's own answer", async () => {
    h = await startDriver();
    await openEverything(h, false);
    assert.equal((await publish(h, "checkin", checkinDoc())).status, 200);
    assert.equal((await publish(h, "driverlive", liveDoc({
      [SAM]: {
        next: { race: "Q2 Heat 1 (Stock Buggy)", away: 0, slot: 3, now: false, staged: false, eta: "2026-10-22T19:07:00Z", words: "You're up next" },
        marshal: { race: "Q2 Heat 2 (Stock Buggy)", post: "Turn 3", away: 1 },
        seen: { at: "2026-10-22T18:59:50Z", signal: "good" },
        firstNight: true,
      },
    }))).status, 200);

    const harness = h;
    const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async (url, body) => {
      const answer = await call(harness, "POST", url, { json: body });
      // No Date header passed on: this relay runs on a test clock, and the
      // header is the machine's real one.
      return { status: answer.status, body: answer.body };
    }, { hash: `#d=${SAM}` });
    page.run(script);
    await page.settle();

    const lv = page.get("lv").textContent;
    assert.match(lv, /You're up next/);
    assert.match(lv, /Q2 Heat 1 \(Stock Buggy\) · grid slot 3/);
    assert.match(lv, /about 7 min/);
    assert.match(lv, /Turn 3/);
    assert.match(lv, /Last seen at/);
    assert.match(page.get("gd").textContent, /Your first night/, "the guide, under their name");
    assert.ok(page.button(page.get("lv"), /^Check in$/), "checkin/one said check-in is open");
    assert.match(page.get("nt").textContent, /Nothing is filed for you yet/);

    // And the live poll reads the same entry.
    await page.advance(20000);
    assert.ok(page.sent.some((s) => s.body["part"] === "live"));
    assert.match(page.get("lv").textContent, /You're up next/);
  });
});
