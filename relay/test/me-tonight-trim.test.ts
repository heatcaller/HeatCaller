import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { nightPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * ME'S TONIGHT, TRIMMED (16 Nov 2026, Asher: "remove the pack in card, car
 * tonight, and transponder cards that are BELOW the four tabs. add tonights
 * car chip to the hero card that jumps to the garage on click").
 */

const CODE = "a1b2c3d4e5f60718293a";

const answer: Answerer = async (url, body) => {
  if (url.endsWith("/night")) {
    const live = { at: "2026-10-22T19:00:00Z", next: { race: "Q2 Heat 1 (Stock Buggy)", away: 2, slot: 3, now: false, words: "2 races before yours" } };
    if (body["part"] === "live") return { status: 200, body: { ok: true, live } };
    return { status: 200, body: { ok: true, night: null, live } };
  }
  return { status: 404 };
};

async function open(): Promise<FakePage> {
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, answer, { hash: `#d=${CODE}` });
  page.run(script);
  await page.settle();
  // What the Garage hands Me once it has loaded: a car racing tonight, a pack in it.
  page.eval(`meGarage = {
    cars: [{ id: 'c1', name: 'Stinger 2', tonight: true, setups: [] }],
    packs: [{ id: '0123456789abcdef0123456789abcdef', label: 'Red 2S', inCar: true }],
  }; meMoment(); meTiles();`);
  return page;
}

describe("Me's Tonight under the four tabs", () => {
  it("has no Pack in the car, Car tonight or Transponder tiles", async () => {
    const page = await open();
    const tiles = page.get("me-tiles").textContent;
    assert.doesNotMatch(tiles, /Pack in the car|Car tonight|Transponder/);
  });

  it("the moment card still has its Pack and Transponder", async () => {
    const page = await open();
    const hero = page.get("me-moment").textContent;
    assert.match(hero, /Red 2S/);
    assert.match(hero, /Transponder/);
  });
});

// Option C of relay/mockups/me-hero-car-chip.html (16 Nov 2026).
describe("Up next's car bar", () => {
  const bar = (page: FakePage) => page.get("me-moment").find((n) => n.tagName === "BUTTON" && /\bme-carbar\b/.test(n.className))[0];

  it("names tonight's car and says no setup is logged, with Log setup to fix it", async () => {
    const page = await open();
    const b = bar(page);
    assert.ok(b, "the bar is there");
    assert.match(b!.textContent, /^Racing tonightStinger 2No setup todayLog setup$/);
    assert.match(b!.className, /\bwait\b/);
    assert.equal(b!.getAttribute("aria-label"), "Racing tonight: Stinger 2. No setup today. Log setup");
  });

  it("goes to Log setup in Garage while none is logged", async () => {
    const page = await open();
    // meLogSetup itself finds the Garage's Log setup button, which this fake
    // page cannot look for; that the bar calls it is the point here.
    page.eval("meLogged = 0; meLogSetup = () => { meLogged++; }; meMoment();");
    bar(page)!.click();
    assert.equal(page.eval("meLogged"), 1);
  });

  it("says Garage once a setup is logged, and opens it", async () => {
    const page = await open();
    page.eval("meSetupTonight = true; meMoment();");
    const b = bar(page)!;
    assert.match(b.textContent, /Setup logged today.*Garage$/);
    assert.match(b.className, /\bok\b/);
    b.click();
    assert.equal(page.eval("meSpace"), "garage");
  });

  it("took the Setup tile's place: Pack and Transponder, two across, and no chip", async () => {
    const page = await open();
    const hero = page.get("me-moment");
    const row = hero.find((n) => /\bme-acts\b/.test(n.className))[0]!;
    assert.equal(row.className, "me-acts two");
    assert.doesNotMatch(row.textContent, /Setup/);
    assert.equal(hero.find((n) => /\bme-chip\b/.test(n.className)).length, 0);
  });

  it("is not there with no car racing tonight", async () => {
    const page = await open();
    page.eval("meGarage = { cars: [{ id: 'c1', name: 'Stinger 2', tonight: false }], packs: [] }; meMoment();");
    assert.equal(bar(page), undefined);
  });
});

describe("tonight's car on the other moment cards", () => {
  it("is a chip beside the kicker that opens Garage", async () => {
    const page = await open();
    // Your night: races filed, nothing next.
    page.eval(`live = { at: '2026-10-22T19:00:00Z' };
      night = { races: [{ label: 'Q1 Heat 1', laps: 12, bestMs: 14218, at: '2026-10-22T17:00:00Z' }] }; meMoment();`);
    const chip = page.button(page.get("me-moment"), /^Stinger 2$/);
    assert.ok(chip, "the chip is there");
    assert.equal(chip!.parent?.className, "me-hero-top");
    chip!.click();
    assert.equal(page.eval("meSpace"), "garage");
  });
});
