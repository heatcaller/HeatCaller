import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { nightPage } from "../src/pages.js";
import { FakePage, pageParts, type FakeNode } from "./driverexp-rp1-fakepage.js";

/**
 * BUG HUNT 18: a marshal who taps No to covering another's post. The laptop
 * OKs a no by itself (DriverRequests.Marshals.cs ApplyCover), so it comes
 * back approved, and Me headlined it "Marshal swap done" over the laptop's
 * own "OK - you're not covering Turn 3...".
 */
const CODE = "a1b2c3d4e5f60718293a";
// The fake page's own clock is 2026-10-22T19:00Z.
const NOW = "2026-10-22T19:00:00Z";

async function open(asks: unknown[]): Promise<FakePage> {
  const live = { at: NOW, next: { race: "Q3 Heat 1 (Stock Buggy)", away: 2, slot: 4 }, asks };
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, async (url, body) => {
    if (url.endsWith("/night")) {
      if (body["part"] === "live") return { status: 200, body: { ok: true, live } };
      return { status: 200, body: { ok: true, night: null, live, me: { name: "Sam Parker" }, asksOpen: true } };
    }
    return { status: 404 };
  }, { hash: `#d=${CODE}` });
  page.run(script);
  await page.settle();
  page.eval("meMoment()");
  return page;
}

function headline(page: FakePage): string {
  const box = page.get("me-moment").find((n) => /^me-reply/.test(n.className))[0];
  assert.ok(box, "the answer is on the card");
  return box.find((n) => n.tagName === "B")[0]?.textContent ?? "";
}

describe("bug hunt 18: the answer to a cover offer", () => {
  it("says you said no when you turned it down", async () => {
    const page = await open([{ ref: "A1B2C3", kind: "cover", state: "approved", at: NOW, said: "OK - you're not covering Turn 3 in Q2 Heat 3." }]);
    assert.equal(headline(page), "You said no");
  });

  it("still says the swap is done when you said yes", async () => {
    const page = await open([{ ref: "A1B2C3", kind: "cover", state: "approved", at: NOW, said: "You're covering Turn 3 in Q2 Heat 3." }]);
    assert.equal(headline(page), "Marshal swap done");
  });
});
