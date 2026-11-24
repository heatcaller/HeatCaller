import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { garagePage } from "../src/garagepage.js";
import { FakePage, pageParts, type Answerer, type FakeNode } from "./driverexp-rp1-fakepage.js";
import { withSharedInline } from "./pagescript.js";

/**
 * BUG HUNT 24: the garage page (garagepage.ts).
 *
 * - #332 (F340): swiping to another car, or tapping Log setup, while the
 *   other car's sheet had unsent typing left the setup card on the first car.
 * - #333 (F341): one garage read that never came back stopped every later
 *   read, so the garage froze until a reload.
 * - #334 (F342): a race run on a pack no longer in the list (a club pack
 *   since taken out of the car) showed no pack, and Save wiped it.
 */

const CODE = "a1b2c3d4e5f60718293a";
const PACK_A = "0123456789abcdef0123456789abcdef";
const GONE = "99999999999999999999999999999999";
const RACE = "fedcba9876543210fedcba9876543210";
const CAR_A = "33333333333333333333333333333333";
const CAR_T = "88888888888888888888888888888888";

function garage(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    v: 1,
    at: "2026-11-05T19:02:11Z",
    driver: "Sam Rivera",
    packs: [{ id: PACK_A, label: "Blue 2", chem: "LiPo", notes: "", retired: false, club: false, inCar: true, sessions: 14, mine: true }],
    chemistries: ["NiMH", "LiPo", "LiFe", "LiHV"],
    races: [],
    stats: { layouts: [], groups: [] },
    cars: [
      {
        id: CAR_T, name: "TT-02", chassis: "", chassisName: "", cls: "", body: "", number: "", notes: "",
        retired: false, tonight: false, sheet: null, cover: null, photos: [], setups: [],
      },
      {
        id: CAR_A, name: "MR-03 Red", chassis: "", chassisName: "", cls: "", body: "", number: "", notes: "",
        retired: false, tonight: true, sheet: null, cover: null, photos: [], setups: [],
      },
    ],
    chassis: [],
    classes: ["Stock"],
    sheets: {},
    limits: { photosPerCar: 6, photoBytes: 460800 },
    ...extra,
  };
}

async function open(answer: Answerer): Promise<FakePage> {
  const { html, script } = pageParts(withSharedInline(garagePage("acme-rc", "Acme RC")));
  const page = new FakePage(html, answer, { hash: `#d=${CODE}` });
  page.run(`globalThis.crypto = { getRandomValues(a) { for (let i = 0; i < a.length; i++) a[i] = (i * 37 + 11) & 255; return a; } };`);
  page.run(script);
  await page.settle();
  return page;
}

function field(root: FakeNode, words: RegExp): FakeNode {
  const label = root.find((n) => n.tagName === "LABEL" && words.test(n.textContent))[0];
  assert.ok(label, `a field ${words}`);
  const input = label.find((n) => n.tagName === "INPUT" || n.tagName === "TEXTAREA" || n.tagName === "SELECT")[0];
  assert.ok(input, `an input in ${words}`);
  return input;
}

function garageReads(page: FakePage): number {
  return page.sent.filter((s) => s.url.endsWith("/driver/garage")).length;
}

describe("bug hunt 24: the setup card follows the car picked, even with unsent typing (F340)", () => {
  it("swipe to the other car draws its sheet, and Log setup back puts the typing back", async () => {
    let g = garage();
    const page = await open(async (url) =>
      url.endsWith("/driver/garage") ? { status: 200, body: { ok: true, garage: g, pin: false, signedIn: false, asks: [] } } : { status: 404 });
    assert.match(page.get("gs").textContent, /Setup · MR-03 Red/, "the car racing tonight first");

    const notes = field(page.get("gs"), /^Notes for this entry/);
    notes.value = "Half typ";
    notes.fire("input");

    // What the carousel does when it lands on another car.
    page.eval(`view.carAt = '${CAR_T}'; paint('setup', false)`);
    assert.match(page.get("gs").textContent, /Setup · TT-02/, "the swiped-to car's sheet");
    assert.equal(field(page.get("gs"), /^Notes for this entry/).value, "", "not the other car's typing");

    // Log setup on MR-03 Red, the car racing tonight.
    const slide = page.get("gg").find((n) => /\bgslide\b/.test(n.className) && n.textContent.includes("MR-03 Red"))[0];
    assert.ok(slide, "the MR-03 Red slide");
    page.button(slide, /^Log setup$/)!.click();
    await page.settle();
    assert.match(page.get("gs").textContent, /Setup · MR-03 Red/);
    const back = field(page.get("gs"), /^Notes for this entry/);
    assert.equal(back.value, "Half typ", "the draft is kept per car");

    // A garage read that changes nothing about which car is in view still
    // does not redraw over the typing.
    g = garage({ at: "2026-11-05T19:05:00Z", classes: ["Stock", "Modified"] });
    (g["cars"] as Record<string, unknown>[])[0]!["body"] = "New shell";
    await page.advance(15000);
    assert.equal(field(page.get("gs"), /^Notes for this entry/), back, "the same field, not redrawn");
  });

  it("carMoved only lets through a car the driver picked that is not the one drawn", async () => {
    const page = await open(async (url) =>
      url.endsWith("/driver/garage") ? { status: 200, body: { ok: true, garage: garage(), pin: false, signedIn: false, asks: [] } } : { status: 404 });
    assert.equal(page.eval(`carMoved('${CAR_A}', '${CAR_T}', '${CAR_T}')`), true);
    assert.equal(page.eval(`carMoved('${CAR_A}', '${CAR_A}', '${CAR_A}')`), false, "the same car");
    assert.equal(page.eval(`carMoved('${CAR_A}', '${CAR_T}', '')`), false, "not picked by the driver");
    assert.equal(page.eval(`carMoved('${CAR_A}', '', '')`), false, "no car");
  });
});

describe("bug hunt 24: a garage read that never comes back (F341)", () => {
  it("is given up on and the page reads again", async () => {
    let first = true;
    const page = await open(async (url) => {
      if (url.endsWith("/driver/garage")) {
        if (first) {
          first = false;
          return { status: 200, hang: true };
        }
        return { status: 200, body: { ok: true, garage: garage(), pin: false, signedIn: false, asks: [] } };
      }
      return { status: 404 };
    });
    assert.equal(garageReads(page), 1, "the first read is out");
    assert.equal(page.get("gp").children.length, 0, "nothing drawn yet");

    await page.advance(12000 + 15000 + 1000);
    assert.ok(garageReads(page) >= 2, "a second read was made");
    assert.match(page.get("gp").textContent, /Battery packs/, "the garage is drawn");
    assert.doesNotMatch(page.get("gm").textContent, /Could not reach the club/, "and the trouble words are gone");
  });
});

describe("bug hunt 24: a race on a pack no longer in the list (F342)", () => {
  function withRace(packLabel?: string): Record<string, unknown> {
    const race: Record<string, unknown> = { id: RACE, label: "Heat 9", at: "2026-11-05T18:40:00Z", layout: "Club layout", best: 12.3, pack: GONE, by: "car" };
    if (packLabel !== undefined) race["packLabel"] = packLabel;
    return garage({ races: [race] });
  }

  it("shows the pack as picked, named from the race", async () => {
    const page = await open(async (url) =>
      url.endsWith("/driver/garage") ? { status: 200, body: { ok: true, garage: withRace("Club 3"), pin: false, signedIn: false, asks: [] } } : { status: 404 });
    const pick = field(page.get("gr"), /^Pack in Heat 9/);
    const option = pick.find((n) => n.tagName === "OPTION" && n.value === GONE)[0];
    assert.ok(option, "a choice for the pack the race ran on");
    assert.equal(option.textContent, "Club 3");
    assert.equal(pick.value, GONE);
  });

  it("names it plainly with no label, and Save with it still picked sends nothing", async () => {
    const page = await open(async (url) => {
      if (url.endsWith("/driver/garage")) return { status: 200, body: { ok: true, garage: withRace(), pin: false, signedIn: false, asks: [] } };
      if (url.endsWith("/driver/ask")) return { status: 200, body: { ok: true, ref: "A1B2C3" } };
      return { status: 404 };
    });
    const pick = field(page.get("gr"), /^Pack in Heat 9/);
    const option = pick.find((n) => n.tagName === "OPTION" && n.value === GONE)[0];
    assert.ok(option, "a choice for the pack the race ran on");
    assert.equal(option.textContent, "A pack not in your garage");

    const row = page.get("gr").find((n) => /\bgrow\b/.test(n.className) && n.textContent.includes("Heat 9"))[0]!;
    page.button(row, /^Save$/)!.click();
    await page.settle();
    assert.equal(page.sent.filter((s) => s.url.endsWith("/driver/ask")).length, 0, "nothing sent, so the pack stays on the race");
    assert.match(page.get("gr").textContent, /Nothing changed/);
  });
});
