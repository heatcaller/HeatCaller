import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { Script } from "node:vm";
import { homePage, nightPage, signupPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer, type FakeNode } from "./driverexp-rp1-fakepage.js";
import { pageScriptMatch } from "./pagescript.js";

/**
 * RELAY NAVIGATION, ROUND 3 (29 Oct 2026): the little links. Watch live or
 * See the running order on the night's moment card (the Right now card's until 6 Nov 2026), Your next race on
 * Home for a phone that remembers its driver, and Go to Me after sign-up.
 */

const CODE = "a1b2c3d4e5f60718293a";
const ME = "hg-me-acme-rc";

function night(): Record<string, unknown> {
  return { driver: "Bill Hargrove", title: "Club Night", when: "Thursday 22 October 2026", night: "2026-10-22", lines: ["Bill raced twice."] };
}

/** The relay, answering the night read and its live poll with `next` as their next race. */
function relay(next: unknown, status = 200): Answerer {
  return async (url, body) => {
    if (url.endsWith("/night")) {
      if (status !== 200) return { status, body: { ok: false } };
      const live = next === undefined ? {} : { live: { at: "2026-10-22T19:00:00Z", next } };
      return { status: 200, body: body["part"] === "live" ? { ok: true, ...live } : { ok: true, night: night(), ...live } };
    }

    if (url.endsWith("/checkin/one")) return { status: 409, body: { ok: false } };
    return { status: 404 };
  };
}

/** The links under an element, as words and where they go. */
function links(under: FakeNode): { words: string; href: string | null }[] {
  return under.find((n) => n.tagName === "A").map((a) => ({ words: a.textContent, href: a.getAttribute("href") }));
}

describe("relay navigation, round 3: Watch live and the running order on the night", () => {
  // On Me's moment card since 6 Nov 2026, when the Alerts card lost its
  // first lines (the race, its slot, its countdown and these links).
  function hero(page: FakePage): FakeNode {
    page.eval("meMoment()");
    return page.get("me-moment");
  }

  async function openNight(next: unknown): Promise<FakePage> {
    const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, relay(next), { hash: `#d=${CODE}` });
    page.run(script);
    await page.settle();
    return page;
  }

  it("offers Watch live while their race is on track", async () => {
    const page = await openNight({ race: "Q2 Heat 1", away: 0, now: true });
    assert.deepEqual(links(hero(page)), [{ words: "Watch live", href: "/c/acme-rc/live" }]);
  });

  it("offers See the running order while it is next, staged or not", async () => {
    for (const next of [{ race: "Q2 Heat 1", away: 0 }, { race: "Q2 Heat 1", away: 0, staged: true }]) {
      const page = await openNight(next);
      assert.deepEqual(links(hero(page)), [{ words: "See the running order", href: "/c/acme-rc/schedule" }]);
    }
  });

  it("offers neither for a race further off, or no race in the next few", async () => {
    assert.deepEqual(links(hero(await openNight({ race: "Q2 Heat 1", away: 3 }))), []);
    assert.deepEqual(links(hero(await openNight(null))), []);
    // And the Alerts card has neither, whatever the race is doing.
    assert.deepEqual(links((await openNight({ race: "Q2 Heat 1", away: 0 })).get("lv")), []);
  });

  it("follows the race as it moves: next, then on track", async () => {
    let next: Record<string, unknown> = { race: "Q2 Heat 1", away: 0 };
    const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async (url, body) => relay(next)(url, body), { hash: `#d=${CODE}` });
    page.run(script);
    await page.settle();

    assert.equal(links(hero(page))[0]?.words, "See the running order");

    next = { race: "Q2 Heat 1", away: 0, now: true };
    await page.advance(30000);

    assert.equal(links(hero(page))[0]?.words, "Watch live");
  });
});

describe("relay navigation, round 3: Your next race on Home", () => {
  async function openHome(stored: Record<string, string>, answer: Answerer): Promise<FakePage> {
    const { html, script } = pageParts(homePage("acme-rc", "Acme RC"));
    const page = new FakePage(html, answer, {});
    for (const [k, v] of Object.entries(stored)) page.eval(`localStorage.setItem(${JSON.stringify(k)}, ${JSON.stringify(v)})`);
    page.run(script);
    await page.settle();
    return page;
  }

  // The live poll only: since round 6 (30 Oct 2026) Home also reads the
  // driver's whole night, every five minutes, for its "You" strip.
  const nightReads = (page: FakePage) => page.sent.filter((s) => s.url.endsWith("/night") && (s.body as { part?: string }).part === "live");

  // No "Open your night" since 5 Nov 2026 (Asher: "remove open your night
  // and check in from home screen"): Me is on the bar.
  it("shows their next race, with Watch live, when the phone remembers them", async () => {
    const page = await openHome({ [ME]: CODE }, relay({ race: "Q2 Heat 1 (Stock Buggy)", away: 0, now: true }));
    const mine = page.get("mine");

    assert.equal(mine.hidden, false);
    assert.match(mine.textContent, /Your next race/);
    assert.match(mine.textContent, /On track now/);
    assert.match(mine.textContent, /Q2 Heat 1 \(Stock Buggy\)/);
    assert.deepEqual(links(mine), [
      { words: "Watch live", href: "/c/acme-rc/live" },
    ]);
    assert.deepEqual(nightReads(page).map((s) => s.body), [{ id: CODE, part: "live" }]);
  });

  it("says how far off it is, and points at the running order when it is next", async () => {
    const off = (await openHome({ [ME]: CODE }, relay({ race: "A", away: 2 }))).get("mine");
    assert.match(off.textContent, /2 races before yours/);
    assert.deepEqual(links(off).map((l) => l.words), []);

    const next = (await openHome({ [ME]: CODE }, relay({ race: "A", away: 0 }))).get("mine");
    assert.match(next.textContent, /You're up next/);
    assert.equal(next.className, "lc next");
    assert.deepEqual(links(next).map((l) => l.words), ["See the running order"]);
  });

  it("asks nothing and shows nothing for a phone that remembers nobody, or keeps something that is not a code", async () => {
    for (const kept of [{}, { [ME]: "javascript:alert(1)" }]) {
      const page = await openHome(kept, relay({ race: "A", away: 0 }));

      assert.equal(nightReads(page).length, 0);
      assert.equal(page.get("mine").children.length, 0);
    }
  });

  it("stays hidden with no race in the next few, and when the club is not opening nights", async () => {
    assert.equal((await openHome({ [ME]: CODE }, relay(undefined))).get("mine").hidden, true);
    assert.equal((await openHome({ [ME]: CODE }, relay({ race: "A", away: 0 }, 404))).get("mine").hidden, true);
  });

  it("builds it as text, never markup: the race's name is the laptop's words", async () => {
    const page = await openHome({ [ME]: CODE }, relay({ race: "<img src=x onerror=alert(1)>", away: 1 }));
    const mine = page.get("mine");

    assert.equal(mine.find((n) => n.hadMarkup).length, 0);
    assert.match(mine.textContent, /<img src=x/);
  });

  it("asks again once a minute, not every few seconds", async () => {
    const page = await openHome({ [ME]: CODE }, relay({ race: "A", away: 3 }));
    await page.advance(59000);
    assert.equal(nightReads(page).length, 1);
    await page.advance(2000);
    assert.equal(nightReads(page).length, 2);
  });

  it("puts it above everything else on Home, hidden until there is something to say", () => {
    assert.match(homePage("acme-rc", "Acme RC"), /<section class="lc" id="mine" hidden><\/section><div id="now" data-when="">/);
    // The followed driver's card (overlay plan 7.1, 14 Nov 2026) comes first
    // of all the cards about a driver, above Racing tonight?.
    assert.match(homePage("acme-rc", "Acme RC"), /<section class="lc" id="fol" hidden><\/section><div id="rtw"><\/div>/);
  });
});

describe("relay navigation, round 3: Go to Me after sign-up", () => {
  it("points a new driver at Me once the desk has said yes", () => {
    const html = signupPage("acme-rc", "Acme RC");

    assert.match(html, /Your own races and results open under Me, at the bottom of the page on a phone\./);
    assert.match(html, /'<a class="more" href="' \+ BASE \+ '\/mynight">Go to Me<\/a>'/);
  });

  it("ships scripts that parse", () => {
    for (const html of [homePage("acme-rc", "Acme RC"), nightPage("acme-rc", "Acme RC"), signupPage("acme-rc", "Acme RC")]) {
      const script = pageScriptMatch(html)?.[1] ?? "";
      assert.doesNotThrow(() => new Script(script));
    }
  });
});
