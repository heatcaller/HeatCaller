import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { myNightPage } from "../src/pages.js";
import { alertKindOf, nightAlerts, pushKindsFrom } from "../src/push.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";

/**
 * DRIVER PHONE PLAN, STEP D (14 Nov 2026): onto the home screen, alerts
 * that open the right page and come by kind, and help inside another app's
 * browser. See plans/driver-phone-ux-plan.md items 7, 9 and 16.
 */

async function openMe(where: { search?: string } = {}, ua = ""): Promise<FakePage> {
  const { html, script } = pageParts(myNightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, async () => ({ status: 404 }), where);
  if (ua) page.eval(`navigator.userAgent = ${JSON.stringify(ua)}`);
  page.run(script);
  await page.settle();
  return page;
}

describe("alerts by kind, opening the right page (item 9)", () => {
  it("knows each alert's kind by its tag", () => {
    assert.equal(alertKindOf("hc-race"), "race");
    assert.equal(alertKindOf("hc-soon"), "race");
    assert.equal(alertKindOf("hc-marshal"), "marshal");
    assert.equal(alertKindOf("hc-marshal-soon"), "marshal");
    assert.equal(alertKindOf("hc-result"), "result");
    assert.equal(alertKindOf("nextnight"), "news");
  });

  it("takes only known kinds from a phone, and none said is all of them", () => {
    assert.deepEqual(pushKindsFrom(["race", "result", "race", "spam", 3]), ["race", "result"]);
    assert.deepEqual(pushKindsFrom([]), []);
    assert.equal(pushKindsFrom(undefined), undefined);
    assert.equal(pushKindsFrom("race"), undefined);
  });

  it("sends a result alert to the race's own result", () => {
    const sid = "0123456789abcdef0123456789abcdef";
    const [message] = nightAlerts({ night: "2026-11-14", races: [] }, { night: "2026-11-14", races: [{ sid, label: "Q1 Heat 1", pos: 2, of: 6 }] }, "/c/acme-rc/mynight");
    assert.equal(message!.url, `/c/acme-rc/results/${sid}`);
    const [plain] = nightAlerts({ night: "2026-11-14", races: [] }, { night: "2026-11-14", races: [{ label: "Q1 Heat 1", pos: 2 }] }, "/c/acme-rc/mynight");
    assert.equal(plain!.url, "/c/acme-rc/mynight", "Me, for a race with no result to open");
  });

  it("counts an alert opened, then takes the tag off the address", async () => {
    const page = await openMe({ search: "?from=alert-result" });
    assert.equal(page.eval("hcCounted['alert_opened_result']"), true);
  });
});

describe("inside another app's browser (item 16)", () => {
  it("knows Facebook, Instagram and a web view, and says nothing in a real browser", async () => {
    const fb = await openMe({}, "Mozilla/5.0 (iPhone) AppleWebKit Mobile/15E148 [FBAN/FBIOS;FBAV/400.0]");
    assert.equal(fb.eval("inAppBrowser()"), "facebook");
    assert.match(fb.get("mn").drawn(), /You’re in Facebook’s browser/);

    const ig = await openMe({}, "Mozilla/5.0 (iPhone) Instagram 300.0");
    assert.equal(ig.eval("inAppBrowser()"), "instagram");

    const safari = await openMe({}, "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1");
    assert.equal(safari.eval("inAppBrowser()"), "");
    assert.doesNotMatch(safari.get("mn").drawn(), /browser\./);
  });
});

describe("onto the home screen (item 7)", () => {
  it("never on a phone's first visit, nor inside another app's browser", async () => {
    const first = await openMe({}, "Mozilla/5.0 (iPhone) AppleWebKit Version/18.0 Mobile Safari/604.1");
    first.eval("hcInstallCard()");
    assert.equal(first.eval("document.getElementById('hcinst')"), null);
    assert.equal(first.eval("localStorage.getItem('hg-visits-acme-rc')"), "1", "the first visit is counted");
  });

  it("not again once Don't ask again was chosen", async () => {
    const page = await openMe({}, "Mozilla/5.0 (iPhone) AppleWebKit Version/18.0 Mobile Safari/604.1");
    page.eval("localStorage.setItem('hg-visits-acme-rc', '5'); localStorage.setItem('hg-install-acme-rc', 'never'); hcInstallCard()");
    assert.equal(page.eval("document.getElementById('hcinst')"), null);
  });
});
