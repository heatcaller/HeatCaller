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

describe("tonight's car on the moment card", () => {
  it("is a chip beside the kicker, with the car's name", async () => {
    const page = await open();
    const chip = page.button(page.get("me-moment"), /^Stinger 2$/);
    assert.ok(chip, "the chip is there");
    assert.match(chip!.className, /\bme-chip\b/);
    assert.equal(chip!.parent?.className, "me-hero-top");
  });

  it("opens Garage", async () => {
    const page = await open();
    page.button(page.get("me-moment"), /^Stinger 2$/)!.click();
    assert.equal(page.eval("meSpace"), "garage");
  });

  it("is not there with no car racing tonight", async () => {
    const page = await open();
    page.eval("meGarage = { cars: [{ id: 'c1', name: 'Stinger 2', tonight: false }], packs: [] }; meMoment();");
    assert.equal(page.button(page.get("me-moment"), /^Stinger 2$/), undefined);
  });
});
