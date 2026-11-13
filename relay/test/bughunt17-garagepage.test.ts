import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { garagePage } from "../src/garagepage.js";
import { FakePage, pageParts, type FakeNode } from "./driverexp-rp1-fakepage.js";

/**
 * BUG HUNT 17: the garage page (garagepage.ts).
 *
 * One read that found no garage (a 404: check-in or phone requests shut for
 * a moment, a store hiccup) wipes every section. When the garage came back,
 * a section holding a half-typed form was never drawn again - the draft that
 * is there to stop a poll redrawing over what is being typed also stopped
 * the redraw of an empty box - and only a reload, which loses the typing,
 * brought it back.
 */

const CODE = "a1b2c3d4e5f60718293a";
const PACK_A = "0123456789abcdef0123456789abcdef";

function garage(): Record<string, unknown> {
  return {
    v: 1,
    at: "2026-11-05T19:02:11Z",
    driver: "Sam Rivera",
    packs: [{ id: PACK_A, label: "Blue 2", chem: "LiPo", notes: "", retired: false, club: false, inCar: true, sessions: 14, mine: true }],
    chemistries: ["NiMH", "LiPo", "LiFe", "LiHV"],
    races: [],
    stats: { layouts: [], groups: [] },
    cars: [],
    chassis: [],
    classes: ["Stock"],
    sheets: {},
    limits: { photosPerCar: 6, photoBytes: 460800 },
  };
}

function field(root: FakeNode, words: RegExp): FakeNode {
  const label = root.find((n) => n.tagName === "LABEL" && words.test(n.textContent))[0];
  assert.ok(label, `a field ${words}`);
  const input = label.find((n) => n.tagName === "INPUT" || n.tagName === "TEXTAREA" || n.tagName === "SELECT")[0];
  assert.ok(input, `an input in ${words}`);
  return input;
}

describe("bug hunt 17: a garage that comes back after a 404", () => {
  it("draws the section with the half-typed form again, with what was typed still in it", async () => {
    let found = true;
    const { html, script } = pageParts(garagePage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async (url) => {
      if (url.endsWith("/driver/garage")) {
        return found
          ? { status: 200, body: { ok: true, garage: garage(), pin: false, signedIn: false, asks: [] } }
          : { status: 404, body: { ok: false, error: "Not found" } };
      }
      return { status: 404 };
    }, { hash: `#d=${CODE}` });
    page.run(`globalThis.crypto = { getRandomValues(a) { for (let i = 0; i < a.length; i++) a[i] = (i * 37 + 11) & 255; return a; } };`);
    page.run(script);
    await page.settle();

    page.button(page.get("gp"), /^Add a pack$/)!.click();
    const input = field(page.get("gp"), /^Label/);
    input.value = "Half typ";
    input.fire("input");

    found = false;
    await page.advance(15000);
    assert.match(page.get("gm").textContent, /No garage was found for that code/);
    assert.equal(page.get("gp").children.length, 0, "wiped with the rest");

    found = true;
    await page.advance(15000);
    assert.doesNotMatch(page.get("gm").textContent, /No garage was found/);
    assert.match(page.get("gp").textContent, /Battery packs/);
    assert.equal(field(page.get("gp"), /^Label/).value, "Half typ", "the draft refills the form");
    assert.ok(page.button(page.get("gp"), /^Cancel$/), "and there is a way out of it");
  });
});
