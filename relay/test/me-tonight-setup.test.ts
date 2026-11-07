import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { nightPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * ME TONIGHT, 6 NOV 2026 (Asher: "remove first night card from me tab.
 * remove top lines of right now card on me tonight. change the title of
 * right now to Setup. move the qualifying and best information on the
 * right now card to the hero card above it").
 */
const CODE = "a1b2c3d4e5f60718293a";

const QUAL = {
  classes: [{
    name: "Stock Buggy", method: "TotalLaps", consecutive: 3, mainSize: 2,
    rows: [
      { pos: 1, name: "Sam P.", tag: "t-sam", main: "A", laps: 18, timeMs: 302103 },
      { pos: 2, name: "Jo M.", tag: "t-jo", main: "A", laps: 18, timeMs: 305500 },
      { pos: 3, name: "Bill H.", tag: "t-bill", main: "B", laps: 17, timeMs: 299000 },
    ],
  }],
};

async function open(withQual = true): Promise<FakePage> {
  const live = { at: "2026-10-22T19:00:00Z", next: { race: "Q3 Heat 1 (Stock Buggy)", away: 2, slot: 4, words: "2 races before yours" }, cars: [{ tag: "t-bill" }] };
  const answer: Answerer = async (url, body) => {
    if (url.endsWith("/night")) return { status: 200, body: body["part"] === "live" ? { ok: true, live } : { ok: true, night: null, live } };
    if (url.endsWith("/snapshot/qualifying")) return withQual ? { status: 200, body: { body: QUAL } } : { status: 404, body: {} };
    return { status: 404 };
  };
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, answer, { hash: `#d=${CODE}` });
  page.run(script);
  await page.settle();
  // Me's layer redraws the moment card from a MutationObserver, which the
  // fake page has not got: drawn here, once to ask for qualifying and once
  // it has come back.
  page.eval("meMoment()");
  await page.settle();
  page.eval("meMoment()");
  return page;
}

describe("Me tonight: Alerts, and qualifying on the moment card", () => {
  it("calls the card Alerts and leaves the race itself to the moment card", async () => {
    const page = await open();
    const lv = page.get("lv").textContent;

    assert.match(lv, /^Alerts/);
    assert.doesNotMatch(lv, /Right now|Q3 Heat 1|2 races before yours|grid slot/);
    assert.match(lv, /Your transponder/, "getting ready stays");

    const hero = page.get("me-moment").textContent;
    assert.match(hero, /Q3 Heat 1 \(Stock Buggy\)/);
    assert.match(hero, /Grid4th/);
  });

  it("puts their qualifying place, main and best on the moment card, not Alerts", async () => {
    const page = await open();
    const hero = page.get("me-moment").textContent;

    assert.match(hero, /Qualifying3rd of 3 · B main/);
    assert.match(hero, /Qual best17\/4:59\.000/);
    assert.match(hero, /Beat 18\/5:05\.500 to make the A main/);
    assert.doesNotMatch(page.get("lv").textContent, /Qualifying|Best 17/);
  });

  it("says nothing about qualifying before the laptop has published it", async () => {
    const page = await open(false);
    assert.doesNotMatch(page.get("me-moment").textContent, /Qualifying/);
  });
});
