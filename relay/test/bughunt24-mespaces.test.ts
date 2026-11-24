import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { nightPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * BUG HUNT 24 (23 Nov 2026), the Me spaces: "Yes, check me in" on the "Are
 * you at the track?" question only ever checks in. It used the night's
 * toggle, so a driver the desk checked in while the question was open was
 * checked out by their own Yes.
 */

const ELLA = "a1b2c3d4e5f60718293a";

function nightRelay(taps: Record<string, unknown>[]): Answerer {
  return async (url, body) => {
    if (url.endsWith("/night")) {
      if (body["part"] === "live") return { status: 200, body: { ok: true } };
      return { status: 200, body: { ok: true, night: { driver: "Ella Vator", title: "Club Night", when: "Thursday 22 October 2026", night: "2026-10-22", lines: ["Raced twice."] } } };
    }
    if (url.endsWith("/checkin/one")) return { status: 200, body: { ok: true, event: "Corn Dog Day", driver: { id: ELLA, checkedIn: false } } };
    if (url.endsWith("/checkin/tap")) { taps.push(body); return { status: 200, body: { ok: true, checkedIn: body["wanted"] === true } }; }
    return { status: 404 };
  };
}

async function openNight(taps: Record<string, unknown>[]): Promise<FakePage> {
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, nightRelay(taps), { hash: `#d=${ELLA}` });
  page.run(script);
  await page.settle();
  return page;
}

// What a poll does when the desk ticks the driver in.
const deskChecksThemIn = "if (live) live.checkedIn = true; ci.on = true; ci.pending = null;";

describe("bug hunt 24: Yes, check me in never checks out", () => {
  it("sends nothing, and says they're in, when the desk checked them in while the question was open", async () => {
    const taps: Record<string, unknown>[] = [];
    const page = await openNight(taps);
    page.eval("meMoment()");
    page.button(page.get("me-moment"), /check me in/)!.click();
    assert.match(page.get("me-sheet-body").drawn(), /Are you at the track/);

    page.eval(deskChecksThemIn);
    page.button(page.get("me-sheet-body"), /^Yes, check me in$/)!.click();
    await page.settle();

    assert.equal(taps.filter((t) => t["wanted"] === false).length, 0, "no check-out sent");
    assert.equal(taps.length, 0);
    assert.equal(page.get("toast").textContent, "You are already checked in.");
    assert.equal(page.eval("checkedIn()"), true);
  });

  it("still checks in on Yes when they are not in yet", async () => {
    const taps: Record<string, unknown>[] = [];
    const page = await openNight(taps);
    page.eval("meMoment()");
    page.button(page.get("me-moment"), /check me in/)!.click();
    page.button(page.get("me-sheet-body"), /^Yes, check me in$/)!.click();
    await page.settle();

    assert.equal(taps.length, 1);
    assert.equal(taps[0]!["wanted"], true);
    assert.equal(page.get("toast").textContent, "You are checked in.");
  });

  it("the plain-question fallback does not check them out either", async () => {
    const taps: Record<string, unknown>[] = [];
    const page = await openNight(taps);
    page.eval(deskChecksThemIn);
    // No sheet to open: the question is asked with the phone's own confirm box.
    page.eval("window.confirm = () => true; meOpenSheet = undefined;");
    page.eval("meAskCheckIn()");
    await page.settle();

    assert.equal(taps.length, 0, "no check-out sent");
    assert.equal(page.get("toast").textContent, "You are already checked in.");
  });

  it("the night's own button still toggles a checked-in driver out", async () => {
    const taps: Record<string, unknown>[] = [];
    const page = await openNight(taps);
    page.eval(deskChecksThemIn);
    page.eval("tapCheckIn()");
    await page.settle();

    assert.equal(taps.length, 1);
    assert.equal(taps[0]!["wanted"], false);
  });
});
