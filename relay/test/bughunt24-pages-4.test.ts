import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { familyPage, myNightPage, nightPage, resultsPage, tracksPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer, type FakeNode } from "./driverexp-rp1-fakepage.js";

/**
 * BUG HUNT 24 (23 Nov 2026), relay pages, group 4: Me's live card after a
 * poll that never came back, a remembered driver the relay no longer
 * answers, Stop on a family link, My night's sign-out words, the family
 * page's Tonight heading, sector times to the hundredth as the laptop has
 * them, a credited start's dash out of the lap figures, and the sign-in
 * link's limit words.
 */

const CODE = "a1b2c3d4e5f60718293a";
const OTHER = "0f1e2d3c4b5a69788796";

async function openNight(answer: Answerer, store: Record<string, string> = {}): Promise<FakePage> {
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, answer, { hash: `#d=${CODE}` });
  for (const [k, v] of Object.entries(store)) page.store.set(k, v);
  page.eval(`
    this.SpeechSynthesisUtterance = function (text) { this.text = text; };
    window.speechSynthesis = { speak() {}, cancel() {} };
    location.reload = () => {};
    location.replace = () => {};
  `);
  page.run(script);
  await page.settle();
  return page;
}

// Through JSON, so an array the page made compares equal to one made here.
function call(page: FakePage, expression: string): unknown {
  const value = page.eval(expression);
  return value === undefined ? value : JSON.parse(JSON.stringify(value));
}

describe("bug hunt 24 #352: Me's live card after a poll that never came back", () => {
  it("polls again once a hung poll has been given up on", async () => {
    let livePolls = 0;
    const live = { at: "2026-10-22T19:00:00Z", next: { race: "Q3 Heat 1", away: 4, slot: 4 } };
    const page = await openNight(async (url, body) => {
      if (url.endsWith("/night")) {
        if (body["part"] === "live") {
          livePolls++;
          // The first live poll never answers: a phone between networks.
          if (livePolls === 1) return { status: 200, hang: true };
          return { status: 200, body: { ok: true, live } };
        }
        return { status: 200, body: { ok: true, night: null, live, me: { name: "Sam Parker" } } };
      }
      return { status: 404 };
    });

    await page.advance(90_000);
    assert.ok(livePolls >= 2, "the card was polled again after the hung one");
  });
});

describe("bug hunt 24 #353: a remembered driver the relay no longer answers", () => {
  const gone: Answerer = async () => ({ status: 404 });
  const pinLink = (page: FakePage): FakeNode | undefined =>
    page.get("nt").find((n) => n.tagName === "A" && /\/mynight\?add=1$/.test(n.getAttribute("href") ?? ""))[0];

  it("offers the PIN box and the Signed in button under the same words", async () => {
    const page = await openNight(gone, {
      "hg-me-acme-rc": CODE,
      "hg-me-list-acme-rc": JSON.stringify([{ c: CODE, n: "Sam Parker" }, { c: OTHER, n: "Jo Parker" }]),
    });
    assert.match(page.get("nt").drawn(), /No night was found for that code/);
    assert.ok(pinLink(page), "a way to the PIN box");
    assert.equal(pinLink(page)?.textContent, "Sign in with your PIN");
    assert.equal(page.get("mesi").hidden, false, "the Signed in button, to switch or sign out");
    assert.ok(page.get("mesi").find((n) => n.tagName === "BUTTON").length > 0);
  });

  it("shows only the words to a phone that never had the code", async () => {
    const page = await openNight(gone);
    assert.match(page.get("nt").drawn(), /No night was found for that code/);
    assert.equal(pinLink(page), undefined);
    assert.equal(page.get("mesi").find((n) => n.tagName === "BUTTON").length, 0, "no Signed in button");
  });
});

describe("bug hunt 24 #354: Stop on a family link says Stopped only when it was", () => {
  const LINK = { code: "f".repeat(24), label: "Grandma" };

  async function family(stop: () => Promise<{ status: number; body?: unknown }>): Promise<FakePage> {
    const page = await openNight(async (url, body) => {
      if (url.endsWith("/night")) {
        return { status: 200, body: { ok: true, night: { driver: "Sam Parker", title: "Club Night", night: "2026-10-22", lines: ["Raced."], races: [] } } };
      }
      if (url.endsWith("/family/manage")) {
        if (body["action"] === "list") return { status: 200, body: { ok: true, links: [LINK] } };
        if (body["action"] === "stop") return stop();
      }
      return { status: 404 };
    });
    const box = page.get("famlinks");
    const button = page.button(box, /^Stop$/);
    assert.ok(button, "the link has a Stop button");
    button.click();
    await page.settle();
    return page;
  }

  it("keeps quiet about stopping when the relay refused", async () => {
    const page = await family(async () => ({ status: 429, body: { ok: false, error: "Too many requests. Wait a minute and try again." } }));
    const box = page.get("famlinks");
    assert.doesNotMatch(box.textContent, /Stopped\. That link no longer opens\./);
    assert.match(box.textContent, /Too many requests/);
    assert.equal(box.hidden, false);
  });

  it("says it could not be stopped with no signal", async () => {
    const page = await family(async () => { throw new Error("no signal"); });
    const box = page.get("famlinks");
    assert.doesNotMatch(box.textContent, /Stopped\./);
    assert.match(box.textContent, /couldn’t be stopped just now/);
    assert.match(box.textContent, /Grandma/, "the link is still listed");
  });

  it("says Stopped when the relay stopped it", async () => {
    const page = await family(async () => ({ status: 200, body: { ok: true } }));
    assert.match(page.get("famlinks").textContent, /Stopped\. That link no longer opens\./);
  });
});

describe("bug hunt 24 #355: My night's sign-out words", () => {
  it("points at Signed in at the top, not the bottom of the page", () => {
    const html = myNightPage("acme-rc", "Acme RC");
    assert.doesNotMatch(html, /sign out at the bottom/);
    assert.match(html, /until you sign out - tap Signed in at the top of your page\./);
  });
});

describe("bug hunt 24 #356: the family page heads an old night with its day", () => {
  async function fam(night: string | undefined): Promise<FakePage> {
    const answer = {
      ok: true, driver: "Ella Vator", title: "Club Night", when: "Thursday", lines: [], videos: {},
      races: [{ sid: "a".repeat(32), label: "Q2 Heat 2", pos: 3, of: 8, laps: 12 }],
      next: { race: "Q3 Heat 2", away: 2 },
      ...(night === undefined ? {} : { night }),
    };
    const { html, script } = pageParts(familyPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async (url) => (url.endsWith("/family/read") ? { status: 200, body: answer } : { status: 404 }), { hash: "#f=" + "ab".repeat(12) });
    page.run(script);
    await page.settle();
    return page;
  }
  const heading = (page: FakePage) => page.get("fam").find((n) => n.tagName === "H3" && n.textContent !== "Next race")[0]?.textContent;

  it("is not Tonight for last week's races", async () => {
    // Weeks before today; the night filed was 15 Oct 2026.
    const page = await fam("2026-10-15");
    assert.notEqual(heading(page), "Tonight");
    assert.match(heading(page) ?? "", /^Last time out - .*15/);
    assert.match(page.get("fam").textContent, /Tonight’s races show here once the desk files them\./);
  });

  it("is Tonight for tonight's races", async () => {
    // hcIsTonight reads new Date(), which the fake page leaves on the real
    // clock (only Date.now is faked), so tonight is the real race night:
    // six hours back, in this machine's zone, as hcYmd writes it.
    const night = new Date(Date.now() - 6 * 3600 * 1000).toLocaleDateString("en-CA", { year: "numeric", month: "2-digit", day: "2-digit" });
    const page = await fam(night);
    assert.equal(heading(page), "Tonight");
    assert.doesNotMatch(page.get("fam").textContent, /once the desk files them/);
  });

  it("is Tonight as before when the relay sends no day", async () => {
    assert.equal(heading(await fam(undefined)), "Tonight");
  });

  it("no longer promises tonight under the heading", () => {
    assert.doesNotMatch(familyPage("acme-rc", "Acme RC"), /Their races tonight/);
  });
});

async function resultsScript(): Promise<FakePage> {
  const { html, script } = pageParts(resultsPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, async () => ({ status: 404 }));
  page.run(script);
  await page.settle();
  return page;
}

describe("bug hunt 24: sector times to the hundredth, rounded as the laptop rounds", () => {
  it("rounds half up from whole ms", async () => {
    const page = await resultsScript();
    assert.equal(page.eval("hcHundredths(12315)"), "12.32");
    assert.equal(page.eval("hcHundredths(3405)"), "3.41");
    assert.equal(page.eval("hcHundredths(3404)"), "3.40");
    assert.equal(page.eval("hcHundredths(9995)"), "10.00");
    assert.equal(page.eval("sectors([12315, 3405])"), "S1 12.32 · S2 3.41");
  });

  it("is what the Tracks page's sector records use", () => {
    const { script } = pageParts(tracksPage("acme-rc", "Acme RC"));
    assert.match(script, /'S' \+ r\.sector \+ ' ' \+ hcHundredths\(r\.ms\)/);
  });
});

describe("bug hunt 24: a credited start's dash is listed but left out of the figures", () => {
  it("leaves the dash out of the results page's lap stats", async () => {
    const page = await resultsScript();
    const stats = call(page, "lapStats([2100, 8500, 8400, 8600], 8400, 1)") as Record<string, number>;
    assert.equal(stats["avg"], 8500);
    assert.equal(stats["consec2"], 16900);
    assert.equal(stats["fast"], 8400);
    const before = call(page, "lapStats([2100, 8500, 8400, 8600], 8400)") as Record<string, number>;
    assert.equal(before["avg"], 6900, "without startLaps, as it was");
  });

  it("lists the dash as lap 2, never best and with no pace", async () => {
    const page = await resultsScript();
    const rows = call(page, "lapRows([4000, 11000, 11200], [5, 4, 3], 0, 180, 11000, 1)") as { n: number; ms: number; best: boolean; pace: string; pos: number }[];
    assert.deepEqual(rows.map((r) => r.n), [1, 2, 3, 4]);
    assert.equal(rows[1]?.ms, 4000);
    assert.equal(rows[1]?.best, false);
    assert.equal(rows[1]?.pace, "");
    assert.equal(rows[1]?.pos, 5, "its place stays on its own row");
    assert.equal(rows[2]?.best, true);
    assert.match(rows[2]?.pace ?? "", /^17\//, "pace from the flying laps alone");
  });

  it("keeps the dash off a driver's own Lap times chart", async () => {
    const page = await openNight(async () => ({ status: 404 }));
    const lines = call(page, "meLines({ lapsMs: [4000, 11000, 11200], startMs: 0, startLaps: 1, posAt: [5, 4, 3] }, null)") as { laps: number[]; first: number; lapsFirst: number }[];
    assert.deepEqual(lines[0]?.laps, [11000, 11200]);
    assert.equal(lines[0]?.first, 2, "positions stay in step with lapsMs");
    assert.equal(lines[0]?.lapsFirst, 3);
  });

  it("leaves the dash out of a driver's own night stats", async () => {
    const page = await openNight(async () => ({ status: 404 }));
    const rows = call(page, "lapRows([4000, 11000, 11200], null, 0, 0, 11000, 1)") as { n: number; best: boolean }[];
    assert.deepEqual(rows.map((r) => r.best), [false, false, true, false]);
    const stats = call(page, "lapStats([4000, 11000, 11200], 11000, 1)") as Record<string, number>;
    assert.equal(stats["avg"], 11100);
  });
});

describe("bug hunt 24: the sign-in link's limit words", () => {
  it("says ten minutes, as the relay's limit now is", () => {
    const html = myNightPage("acme-rc", "Acme RC");
    assert.doesNotMatch(html, /Try again in an hour, or ask at the desk/);
    assert.match(html, /Try again in ten minutes, or ask at the desk\./);
  });
});
