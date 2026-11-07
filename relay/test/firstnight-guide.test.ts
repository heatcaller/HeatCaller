import { describe, it } from "node:test";
import assert from "node:assert/strict";

import { DEFAULT_CONTEXT, homePage, nightPage, checkinPage } from "../src/pages.js";
import { DEFAULT_LOOK, guideFrom, lookFrom, updatesFrom } from "../src/site.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * THE FIRST-NIGHT GUIDE IN THE CLUB'S OWN WORDS (4 Nov 2026, Asher: "the
 * guide should be able to be edited on laptop under the website nav"). The
 * laptop sends it in the site document as "guide"; the relay tidies it
 * like the tagline and hands it to the page on the body, where the guide's
 * script draws it as text in place of the standard one.
 */
describe("the first-night guide from the app", () => {
  it("keeps a heading and words per part, tidied and capped", () => {
    assert.deepEqual(guideFrom([
      { heading: "  Parking\n", words: "Round the\tback,  by the bins. " },
      { heading: "", words: "Bring a chair." },
      { heading: "x".repeat(100), words: "y".repeat(900) },
    ]), [
      { heading: "Parking", words: "Round the back, by the bins." },
      { heading: "", words: "Bring a chair." },
      { heading: "x".repeat(60), words: "y".repeat(600) },
    ]);
  });

  it("drops a part with no words, stops at twenty, and is empty for anything not a list", () => {
    assert.deepEqual(guideFrom([{ heading: "Only a heading" }, { words: 7 }, null, "text"]), []);
    assert.equal(guideFrom(Array.from({ length: 30 }, (_, i) => ({ heading: "", words: "Part " + i }))).length, 20);

    for (const value of [undefined, null, "a guide", 3, { heading: "a", words: "b" }]) {
      assert.deepEqual(guideFrom(value), []);
    }
  });

  it("comes through the look, and an older app's document has none", () => {
    const own = lookFrom({ body: { guide: [{ heading: "Parking", words: "Round the back." }] }, publishedAt: "", seq: 1 } as never, undefined);
    assert.deepEqual(own.guide, [{ heading: "Parking", words: "Round the back." }]);

    const old = lookFrom({ body: { tagline: "Hi" }, publishedAt: "", seq: 1 } as never, undefined);
    assert.deepEqual(old.guide, []);
    assert.deepEqual(DEFAULT_LOOK.guide, []);
  });

  it("rides on the page's body, escaped, only when the club has one", () => {
    const guide = [{ heading: 'The "pits"', words: "<b>Not</b> markup & never will be" }];
    const page = nightPage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_LOOK, guide } });
    const attribute = /<body [^>]*data-guide="([^"]*)"/.exec(page);

    assert.ok(attribute, "the club's guide on the body");
    assert.doesNotMatch(attribute[1]!, /[<>"]/);
    const back = attribute[1]!.replace(/&quot;/g, '"').replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&#39;/g, "'").replace(/&amp;/g, "&");
    assert.deepEqual(JSON.parse(back), guide);

    assert.doesNotMatch(nightPage("acme-rc", "Acme RC", DEFAULT_CONTEXT), /data-guide=/);
    assert.match(checkinPage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_LOOK, guide } }), /<body [^>]*data-guide="/);
  });

  async function opened(guide?: string, store?: Map<string, string>, updates?: string): Promise<FakePage> {
    const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
    const answer: Answerer = async (url, body) =>
      url.endsWith("/night") && body["part"] !== "live"
        ? { status: 200, body: { ok: true, night: null, live: { firstNight: true } } }
        : { status: 200, body: { ok: true } };
    const page = new FakePage(html, answer, { hash: "#d=a1b2c3d4e5f60718293a" });
    if (guide !== undefined) page.body.dataset["guide"] = guide;
    if (updates !== undefined) page.body.dataset["updates"] = updates;
    if (store) for (const [k, v] of store) page.store.set(k, v);
    page.run(script);
    await page.settle();
    return page;
  }

  // The guide is drawn on Home, not Me, since 6 Nov 2026 (Asher: "remove
  // first night card from me tab") - so these read it there.
  async function guideOn(guide?: string, store?: Map<string, string>): Promise<FakePage> {
    const { html, script } = pageParts(homePage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async () => ({ status: 404 }));
    if (guide !== undefined) page.body.dataset["guide"] = guide;
    if (store) for (const [k, v] of store) page.store.set(k, v);
    page.run(script);
    await page.settle();
    return page;
  }

  async function drawn(guide?: string): Promise<string> {
    return (await guideOn(guide)).get("gd").textContent;
  }

  // On Home since 5 Nov 2026 (Asher: "move that card from the me tab to
  // the home tab"), with the same Got it key the night page kept.
  async function home(store?: Map<string, string>, updates?: string): Promise<FakePage> {
    const { html, script } = pageParts(homePage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async () => ({ status: 404, body: {} }), {});
    page.get("now").dataset = { when: "" };
    if (updates !== undefined) page.body.dataset["updates"] = updates;
    if (store) for (const [k, v] of store) page.store.set(k, v);
    page.run(script);
    await page.settle();
    return page;
  }

  it("keeps the club's updates off the driver's night now that they are on Home", () => {
    assert.doesNotMatch(nightPage("acme-rc", "Acme RC"), /id="up"/);
    assert.match(homePage("acme-rc", "Acme RC"), /id="up"/);
  });

  it("draws the club's updates on Home, hides them on Got it, and shows newer news again", async () => {
    const news = JSON.stringify([{ heading: "Track night moved", words: "Thursday this week." }]);
    const opened = (_g?: undefined, store?: Map<string, string>, updates?: string) => home(store, updates);
    const page = await opened(undefined, undefined, news);

    assert.match(page.get("up").textContent, /Club updates/);
    assert.match(page.get("up").textContent, /Track night moved/);
    page.button(page.get("up"), /^Got it/)!.click();
    assert.equal(page.get("up").textContent, "");

    assert.equal((await opened(undefined, page.store, news)).get("up").textContent, "", "the same news stays hidden");
    const newer = await opened(undefined, page.store, JSON.stringify([{ heading: "", words: "Back to Tuesdays." }]));
    assert.match(newer.get("up").textContent, /Back to Tuesdays\./);

    assert.equal((await opened()).get("up").textContent, "", "no updates sent is no card");
  });

  it("reads updates like the guide, ten at most, and puts them on the body only when there are some", () => {
    assert.equal(updatesFrom(Array.from({ length: 15 }, (_, i) => ({ heading: "", words: "News " + i }))).length, 10);
    assert.deepEqual(updatesFrom("news"), []);
    const look = lookFrom({ body: { updates: [{ heading: " New\nbays ", words: "Two more." }] }, publishedAt: "", seq: 1 } as never, undefined);
    assert.deepEqual(look.updates, [{ heading: "New bays", words: "Two more." }]);

    assert.match(nightPage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look }), /<body [^>]*data-updates="/);
    assert.doesNotMatch(nightPage("acme-rc", "Acme RC", DEFAULT_CONTEXT), /data-updates=/);
  });

  it("shows again after Got it once the club sends a new guide, and not before", async () => {
    const first = JSON.stringify([{ heading: "Parking", words: "Round the back." }]);
    const page = await guideOn(first);
    page.button(page.get("gd"), /^Got it/)!.click();

    const again = await guideOn(first, page.store);
    assert.equal(again.get("gd").textContent, "", "the same guide stays hidden");

    const updated = await guideOn(JSON.stringify([{ heading: "Parking", words: "Out front now." }]), page.store);
    assert.match(updated.get("gd").textContent, /Out front now\./, "the club's new guide is shown");
  });

  it("is drawn in place of the standard guide, and the standard one stays for everybody else", async () => {
    const own = await drawn(JSON.stringify([{ heading: "Parking", words: "Round the back." }, { heading: "", words: "Bring a chair." }]));
    assert.match(own, /Parking/);
    assert.match(own, /Bring a chair\./);
    assert.doesNotMatch(own, /Marshal after your race/);

    assert.match(await drawn(), /Marshal after your race/);
    assert.match(await drawn("not json"), /Marshal after your race/, "a broken attribute is the standard guide");
  });
});
