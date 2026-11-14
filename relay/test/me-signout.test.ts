import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { garagePage } from "../src/garagepage.js";
import { myNightPage, nightPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * SIGNING OUT OF ME (6 Nov 2026, asked for by Asher): "when signed in if you
 * hit the signed in button you should get a popup asking to confirming
 * signing out. If you then sign out the app should forget the pin and
 * require a pin entry to sign back in to anyone's me tab. if signed out the
 * app should forget all of the info for that driver that populated in the
 * app when they signed in for privacy."
 */

const CODE = "a1b2c3d4e5f60718293a";
const ME = "hg-me-acme-rc";
const OUT = "hg-me-out-acme-rc";

function relay(): Answerer {
  return async (url, body) => {
    if (url.endsWith("/night")) {
      if (body["part"] === "live") return { status: 200, body: { ok: true } };
      return { status: 200, body: { ok: true, night: { driver: "Bill Hargrove", title: "Club Night", when: "Thursday 22 October 2026", night: "2026-10-22", lines: ["Bill raced twice."] } } };
    }
    return { status: 404 };
  };
}

/** What a phone holds after Bill has used Me for a night, and a desk's things that are not his. */
const KEPT: Record<string, string> = {
  "hg-night-pass-acme-rc": "ef".repeat(32),
  "hg-me-seen-acme-rc": '{"races":"2:r1"}',
  "hg-charge-acme-rc": '{"until":1,"told":false}',
  "hg-me-space": "garage",
  "hg-night-order": '["lv","ea"]',
  "hg-night-just-me": "1",
  "hg-night-hide-runs": "1",
  "hg-nighttalk-acme-rc": "1",
  "hg-nighttalk-short-acme-rc": "1",
  "hg-nighttalk-say-acme-rc": "laps",
  "hg-pin-acme-rc": "4321",
  "hg-pin-acme-rc-pass": "ab".repeat(32),
  "hg-pin-acme-rc-racer": "1",
  "hc-seen-acme-rc:/night": '{"at":1,"body":{}}',
  "hc-reads-acme-rc:/c/acme-rc/night": '["/night"]',
  // Not Bill's: another page's choice, and the Live tab's own.
  "hg-follow-acme-rc": "someone",
  "hg-laptalk-acme-rc": "1",
};

async function openNight(stored: Record<string, string> = {}, how?: string): Promise<FakePage> {
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, relay(), { hash: `#d=${CODE}` });
  for (const [k, v] of Object.entries(stored)) page.eval(`localStorage.setItem(${JSON.stringify(k)}, ${JSON.stringify(v)})`);
  if (how) page.eval(`globalThis.performance = { getEntriesByType: () => [{ type: ${JSON.stringify(how)} }] }`);
  page.run(script);
  await page.settle();
  return page;
}

function stored(page: FakePage, key: string): unknown {
  return page.eval(`localStorage.getItem(${JSON.stringify(key)})`);
}

function signedIn(page: FakePage) {
  const at = page.get("mesi");
  return page.button(at, /^Signed in$/);
}

describe("Me: the Signed in button asks before signing out", () => {
  it("shows Signed in at the top of Me once this phone remembers the driver", async () => {
    const page = await openNight();

    assert.equal(page.get("mesi").hidden, false);
    assert.ok(signedIn(page), "a Signed in button");
    assert.equal(signedIn(page)!.getAttribute("aria-label"), "Signed in on this phone. Tap to sign out.");
    // Above the byline in the page's head (7 Nov 2026), not beside their name.
    assert.match(nightPage("acme-rc", "Acme RC"), /<div class="me-si" id="mesi" hidden><\/div>\s*<div class="hcby">/);
    assert.match(nightPage("acme-rc", "Acme RC"), /<div class="me-top" id="metop"><div id="nh"><\/div><\/div>/);
  });

  it("asks first, and Stay signed in keeps everything", async () => {
    const page = await openNight(KEPT);

    signedIn(page)!.click();

    const sheet = page.get("me-sheet-body");
    assert.equal(page.get("me-sheet").hidden, false, "the popup opens");
    assert.match(sheet.textContent, /Sign out on this phone\?/);
    assert.match(sheet.textContent, /a personal PIN is needed/);
    assert.equal(page.button(sheet, /^Done$/), undefined, "its own two answers, no Done");
    assert.equal(stored(page, ME), CODE, "nothing is forgotten on the question alone");

    page.button(sheet, /^Stay signed in$/)!.click();

    assert.equal(page.get("me-sheet").hidden, true);
    assert.equal(stored(page, ME), CODE);
    assert.equal(stored(page, "hg-night-pass-acme-rc"), KEPT["hg-night-pass-acme-rc"]);
    assert.equal(page.eval("location.href"), undefined, "still on their night");
  });

  it("Sign out forgets the PIN sign-in and everything Me kept for the driver, and goes to the PIN", async () => {
    const page = await openNight(KEPT);

    signedIn(page)!.click();
    page.button(page.get("me-sheet-body"), /^Sign out$/)!.click();

    assert.deepEqual(
      Object.fromEntries(page.store.entries()),
      // The phone's own visit count (the install card, 14 Nov 2026) is the phone's, not Bill's.
      { "hg-follow-acme-rc": "someone", "hg-laptalk-acme-rc": "1", "hg-visits-acme-rc": "1", [OUT]: CODE },
      "only what was never Bill's is left, and the note that this tab signed out",
    );
    assert.equal(page.eval("location.href"), "/c/acme-rc/mynight");
  });

  it("leaves a desk's staff PIN on the check-in page alone", async () => {
    const page = await openNight({ "hg-pin-acme-rc": "9999", "hg-pin-acme-rc-pass": "cd".repeat(32) });

    signedIn(page)!.click();
    page.button(page.get("me-sheet-body"), /^Sign out$/)!.click();

    assert.equal(stored(page, "hg-pin-acme-rc"), "9999");
    assert.equal(stored(page, "hg-pin-acme-rc-pass"), "cd".repeat(32));
  });

  it("has no Sign out link at the foot of the Garage - Signed in is the way (7 Nov 2026)", () => {
    assert.doesNotMatch(nightPage("acme-rc", "Acme RC"), /id="meout"|meoutlink|Sign out on this phone<\/a>/);
  });
});

describe("Me: after signing out, the PIN again", () => {
  it("Back or a reload onto their night in the same tab goes to the PIN and remembers nothing", async () => {
    for (const how of ["back_forward", "reload", undefined]) {
      const page = await openNight({ [OUT]: CODE }, how);

      assert.equal(page.eval("location.href"), "/c/acme-rc/mynight", String(how));
      assert.equal(stored(page, ME), null, String(how));
      assert.equal(signedIn(page), undefined, String(how));
    }
  });

  it("the badge link opened afresh still opens the night, and ends the sign-out note", async () => {
    const page = await openNight({ [OUT]: CODE }, "navigate");

    assert.equal(page.eval("location.href"), undefined);
    assert.equal(stored(page, OUT), null);
    assert.equal(stored(page, ME), CODE);
  });

  it("another driver's night is not held back by it", async () => {
    const page = await openNight({ [OUT]: "ffffffffffffffffffff" }, "back_forward");

    assert.equal(page.eval("location.href"), undefined);
    assert.equal(stored(page, ME), CODE);
  });

  it("Me asks for the PIN, and a right PIN ends the note", async () => {
    const { html, script } = pageParts(myNightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async (url) => {
      if (url.endsWith("/checkin/list")) return { status: 200, body: { mode: "racer", pass: "ab".repeat(32), driver: { id: CODE, checkedIn: true } } };
      return { status: 404 };
    }, {});
    page.eval(`localStorage.setItem(${JSON.stringify(OUT)}, ${JSON.stringify(CODE)})`);
    page.run(script);
    await page.settle();

    const box = page.get("mn");
    assert.match(box.drawn(), /Your personal PIN/);

    const input = box.find((n) => n.id === "mnpin")[0]!;
    (input as unknown as { value: string }).value = "4321";
    page.button(box, /^Sign in$/)!.click();
    await page.settle();

    assert.equal(stored(page, OUT), null);
    assert.equal(page.eval("location.href"), `/c/acme-rc/night#d=${CODE}`);
  });

  it("the Garage page goes to the PIN too", () => {
    assert.match(garagePage("acme-rc", "Acme RC"), /if \(meSignedOut\(CODE\)\) meToPin\(\);/);
  });
});
