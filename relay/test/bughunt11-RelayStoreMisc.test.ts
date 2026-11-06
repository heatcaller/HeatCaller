import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { garagePage } from "../src/garagepage.js";
import { FakePage, pageParts, type Answerer, type FakeNode } from "./driverexp-rp1-fakepage.js";

/**
 * BUG HUNT 11 (5 Nov 2026), RelayStoreMisc: editing a car on the phone must
 * not wipe its chassis when the club has since retired that chassis type.
 * The laptop publishes only in-use chassis types, but the car still carries
 * its retired one; Save used to send chassis:'' and the laptop cleared it.
 */

const CODE = "a1b2c3d4e5f60718293a";
const CAR = "33333333333333333333333333333333";
const LISTED = "55555555555555555555555555555555";
const RETIRED = "88888888888888888888888888888888";

function garage(): Record<string, unknown> {
  return {
    v: 1, at: "2026-11-05T19:02:11Z", driver: "Sam Rivera",
    packs: [], chemistries: ["LiPo"], races: [], stats: { layouts: [], groups: [] },
    cars: [{
      id: CAR, name: "Old faithful", chassis: RETIRED, chassisName: "Tamiya M-05", cls: "Stock", body: "", number: "7",
      notes: "", retired: false, tonight: false, sheet: null, cover: null, photos: [], setups: [],
    }],
    chassis: [{ id: LISTED, name: "Kyosho MR-03", sheet: null }],
    classes: ["Stock"], sheets: {}, limits: { photosPerCar: 6, photoBytes: 460800 },
  };
}

const answer: Answerer = async (url) => {
  if (url.endsWith("/driver/garage")) return { status: 200, body: { ok: true, garage: garage(), pin: false, signedIn: false, asks: [] } };
  if (url.endsWith("/driver/ask")) return { status: 200, body: { ok: true, ref: "A1B2C3" } };
  return { status: 404 };
};

function field(root: FakeNode, words: RegExp): FakeNode {
  const label = root.find((n) => n.tagName === "LABEL" && words.test(n.textContent))[0];
  assert.ok(label, `a field ${words}`);
  const input = label.find((n) => n.tagName === "INPUT" || n.tagName === "TEXTAREA" || n.tagName === "SELECT")[0];
  assert.ok(input, `an input in ${words}`);
  return input;
}

function card(page: FakePage): FakeNode {
  const c = page.get("gg").find((n) => n.className.startsWith("gcar") && n.textContent.includes("Old faithful"))[0];
  assert.ok(c, "the car's card");
  return c;
}

describe("bug hunt 11: a car on a retired chassis keeps it when edited from the phone", () => {
  it("the chassis select starts on the car's own chassis and Save sends it back", async () => {
    const { html, script } = pageParts(garagePage("acme-rc", "Acme RC"));
    const page = new FakePage(html, answer, { hash: `#d=${CODE}` });
    page.run(script);
    await page.settle();

    page.button(card(page), /^Edit$/)!.click();
    await page.settle();
    const pick = field(card(page), /^Chassis/);
    assert.equal(pick.value, RETIRED, "starts on the car's chassis, not 'Not set'");
    assert.match(pick.textContent, /Tamiya M-05/);

    const num = field(card(page), /^Race number/);
    num.value = "8";
    num.fire("input");
    page.button(card(page), /^Save the car$/)!.click();
    await page.settle();

    const sent = page.sent.filter((s) => s.url.endsWith("/driver/ask"));
    assert.equal(sent.length, 1);
    const f = sent[0]?.body["f"] as Record<string, string>;
    assert.equal(f["chassis"], RETIRED, "the car's chassis is not wiped");
    assert.equal(f["number"], "8");
  });
});
