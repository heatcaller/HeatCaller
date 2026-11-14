import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { askFieldsFrom } from "../src/driverasks.js";
import { nightPage } from "../src/pages.js";
import { call } from "./helpers.js";
import { API, SAM, openEverything, startDriver } from "./driverexp-r1-helpers.js";
import { FakePage, pageParts, type FakeNode } from "./driverexp-rp1-fakepage.js";

/**
 * RACE VIDEO, V5 AND V6 (14 Nov 2026, plans/race-video-access-plan.md):
 * "video starts in the wrong place" and "ask the club to hide this video",
 * from the player sheet, through the desk's request queue. Buttons only.
 */

const RACE = "fedcba9876543210fedcba9876543210";
const CODE = "a1b2c3d4e5f60718293a";

describe("the two video requests (V5, V6)", () => {
  it("take which race, which way and about how far - and nothing typed", () => {
    assert.deepEqual(askFieldsFrom("videooff", { race: RACE, d: "early", s: 20, m: "free text" }, false), { ok: true, f: { race: RACE, d: "early", s: "20" } });
    assert.deepEqual(askFieldsFrom("videooff", { race: RACE, d: "sideways", s: 9999 }, false), { ok: true, f: { race: RACE } });
    assert.deepEqual(askFieldsFrom("videohide", { race: RACE, d: "early" }, false), { ok: true, f: { race: RACE } });
    assert.equal(askFieldsFrom("videohide", { race: "nope" }, false).ok, false);
  });

  it("land in the desk's queue for the laptop", async () => {
    const h = await startDriver();
    try {
      await openEverything(h);
      const sent = await call(h, "POST", `${API}/driver/ask`, { json: { id: SAM, kind: "videooff", f: { race: RACE, d: "late", s: 10 } } });
      assert.equal(sent.status, 200, JSON.stringify(sent.body));
      assert.equal((await call(h, "POST", `${API}/driver/ask`, { json: { id: SAM, kind: "videohide", f: { race: RACE } } })).status, 200);
    } finally {
      await h.close();
    }
  });
});

describe("the sheet's report (V5, V6)", () => {
  async function sheet(): Promise<{ page: FakePage; open: FakeNode }> {
    const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async (url) => {
      if (url.endsWith("/video/night")) return { status: 200, body: { ok: true, videos: { [RACE]: { id: "AAAAAAAAAAA", t: 10, end: 300 } }, missed: [] } };
      if (url.endsWith("/driver/ask")) return { status: 200, body: { ok: true, ref: "A1B2C3" } };
      if (url.endsWith("/night")) {
        return { status: 200, body: { ok: true, night: { driver: "Bill", title: "Club Night", when: "Thursday", night: "2026-10-22",
          races: [{ sid: RACE, label: "Q1 Heat 2", at: "2026-10-22T18:30:00Z", pos: 3, of: 8, laps: 7, bestMs: 8500 }] } } };
      }
      return { status: 404 };
    }, { hash: `#d=${CODE}` });
    page.run(script);
    await page.settle();
    page.get("nx").find((n) => n.tagName === "BUTTON" && /Watch my race/.test(n.textContent))[0]!.click();
    return { page, open: page.body.find((n) => n.className === "hcvideo")[0]! };
  }

  it("sends 'opens too early' for that race through the desk", async () => {
    const { page, open } = await sheet();
    assert.match(open.textContent, /Something wrong with this video\?/);
    open.find((n) => n.tagName === "BUTTON" && n.textContent === "It opens too early")[0]!.click();
    await page.settle();
    const ask = page.sent.find((s) => s.url.endsWith("/driver/ask"))!;
    assert.equal(ask.body["kind"], "videooff");
    assert.deepEqual(ask.body["f"], { race: RACE, d: "early" });
    assert.match(open.textContent, /Sent - the desk will check it/);
  });

  it("asks the club to hide it, and says what hiding does", async () => {
    const { page, open } = await sheet();
    assert.match(open.textContent, /To remove the video itself, the club edits it on YouTube/);
    open.find((n) => n.tagName === "BUTTON" && n.textContent === "Ask the club to hide this video")[0]!.click();
    await page.settle();
    const ask = page.sent.find((s) => s.url.endsWith("/driver/ask"))!;
    assert.equal(ask.body["kind"], "videohide");
    assert.deepEqual(ask.body["f"], { race: RACE });
  });
});
