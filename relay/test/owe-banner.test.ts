import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { homePage, livePage, myNightPage, nightPage, schedulePage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * OWING, ON EVERY PAGE (15 Nov 2026, Asher: "the notification should be a
 * banner across all pages with the scroll thing you just built"): Me's fee
 * banner, under the header of every club page while the driver signed in on
 * this phone owes tonight's entry, stuck at the top as the page scrolls.
 */

const CODE = "a1b2c3d4e5f60718293a";
const ME = "hg-me-acme-rc";

/** The relay, answering the night's live poll with `entry` as their entry. */
function relay(entry: unknown, status = 200): Answerer {
  return async (url, body) => {
    if (url.endsWith("/night")) {
      if (status !== 200) return { status, body: { ok: false } };
      const live = entry === undefined ? {} : { live: { at: "2026-10-22T19:00:00Z", entry } };
      return { status: 200, body: body["part"] === "live" ? { ok: true, ...live } : { ok: true, night: null, ...live } };
    }
    return { status: 404 };
  };
}

async function open(html: string, stored: Record<string, string>, answer: Answerer): Promise<FakePage> {
  const parts = pageParts(html);
  const page = new FakePage(parts.html, answer, {});
  for (const [k, v] of Object.entries(stored)) page.eval(`localStorage.setItem(${JSON.stringify(k)}, ${JSON.stringify(v)})`);
  page.run(parts.script);
  await page.settle();
  // The bar starts after the page's own script (setTimeout 0).
  await page.advance(0);
  return page;
}

const liveReads = (page: FakePage) => page.sent.filter((s) => s.url.endsWith("/night") && (s.body as { part?: string }).part === "live");
const owing = { words: "Entry: $15.00 - pay at the desk", paid: false };

describe("Owing, on every page (15 Nov 2026)", () => {
  it("draws the bar under the header of every club page but Me's own, as a link to Me", () => {
    for (const html of [homePage("acme-rc", "Acme RC"), livePage("acme-rc", "Acme RC"), schedulePage("acme-rc", "Acme RC")]) {
      assert.match(html, /<\/header>\s*<div class="hc-owe-bar" id="hc-owe-bar" hidden><a class="me-fee hc-owe" id="hc-owe" href="\/c\/acme-rc\/mynight"><\/a><\/div>/);
      assert.match(html, /\.hc-owe-bar \{ position: sticky; top: var\(--stick-t, 0px\);/);
    }
    for (const html of [nightPage("acme-rc", "Acme RC"), myNightPage("acme-rc", "Acme RC")]) {
      assert.ok(!html.includes('id="hc-owe-bar"'));
    }
  });

  it("shows the desk's words while they owe, on Schedule", async () => {
    const page = await open(schedulePage("acme-rc", "Acme RC"), { [ME]: CODE }, relay(owing));
    assert.equal(page.get("hc-owe-bar").hidden, false);
    assert.equal(page.get("hc-owe").textContent, "Entry: $15.00 - pay at the desk");
    assert.deepEqual(liveReads(page).map((s) => s.body), [{ id: CODE, part: "live" }]);
  });

  it("stays hidden once paid, with no entry, and with nothing tonight", async () => {
    for (const answer of [relay({ words: "Paid ✓", paid: true }), relay(undefined), relay(owing, 404)]) {
      const page = await open(schedulePage("acme-rc", "Acme RC"), { [ME]: CODE }, answer);
      assert.equal(page.get("hc-owe").textContent, "");
      assert.equal(page.get("hc-owe-bar").hidden, true);
    }
  });

  it("asks nothing for a phone that remembers nobody", async () => {
    const page = await open(schedulePage("acme-rc", "Acme RC"), {}, relay(owing));
    assert.equal(liveReads(page).length, 0);
  });

  it("rides on Home's own live poll rather than asking twice", async () => {
    const page = await open(homePage("acme-rc", "Acme RC"), { [ME]: CODE }, relay(owing));
    assert.equal(page.get("hc-owe").textContent, "Entry: $15.00 - pay at the desk");
    assert.equal(liveReads(page).length, 1);
  });

  it("puts the page's own sticky bars under it", () => {
    const html = livePage("acme-rc", "Acme RC");
    assert.match(html, /\.hcdock \{ position: sticky; top: calc\(var\(--stick-t\) \+ var\(--owe-h, 0px\)\);/);
  });
});
