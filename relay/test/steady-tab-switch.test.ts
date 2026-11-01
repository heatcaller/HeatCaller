import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { homePage, livePage, nightPage, resultsPage, schedulePage, tracksPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * A STEADY TAB SWITCH (1 Nov 2026, Asher on his iPhone's home-screen icon:
 * "nav tabs still flicker when clicking", "the content above it flashes into
 * place"). Every page used to open as an empty frame saying Loading... until
 * its first read came back; now it is drawn from the last copy this phone
 * read, and a fresh read follows at once. The tapped tab lights at once and
 * never turns grey, and its page is read ahead while the finger is down.
 */

const SEEN = "hc-seen-acme-rc:";

/** The Tracks page's one read, naming the track it was answered with. */
function tracks(name: string, status = 200): Answerer {
  return async (url) => {
    if (!url.endsWith("/snapshot/tracks")) return { status: 404 };
    if (status !== 200) return { status };
    return { status: 200, body: { publishedAt: "2026-10-22T18:59:00.000Z", body: { tracks: [{ name, lengthFeet: 140 }] } } };
  };
}

/** Opens the Tracks page, holding whatever an earlier page kept, `laterMs` after it. */
async function open(answer: Answerer, kept?: FakePage, laterMs = 0): Promise<FakePage> {
  const { html, script } = pageParts(tracksPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, answer, {});
  if (kept) {
    for (const [key, value] of kept.store) page.store.set(key, value);
    page.clock = kept.clock + laterMs;
  }
  page.run(script);
  await page.settle();
  return page;
}

const readsOf = (page: FakePage) => page.sent.filter((s) => s.url.endsWith("/snapshot/tracks")).length;

describe("a club page drawn from what this phone last read", () => {
  it("is drawn from the copy before any answer, then read afresh at once", async () => {
    const before = await open(tracks("Long"));
    assert.match(before.get("list").innerHTML, /Long/);
    assert.ok(before.store.has(SEEN + "/snapshot/tracks"), "the answer is kept");

    const page = await open(tracks("Tight"), before, 5_000);
    assert.match(page.get("list").innerHTML, /Long/, "drawn at once from the copy");
    assert.doesNotMatch(page.get("list").innerHTML, /Loading/);
    assert.equal(readsOf(page), 0, "before it has asked the relay anything");

    await page.advance(0);
    assert.equal(readsOf(page), 1, "then a fresh read straight after");
    assert.match(page.get("list").innerHTML, /Tight/);
  });

  it("does not read again when the copy was read ahead a moment ago", async () => {
    const before = await open(tracks("Long"));
    const page = await open(tracks("Tight"), before, 1_000);
    await page.advance(0);
    assert.equal(readsOf(page), 0);
    assert.match(page.get("list").innerHTML, /Long/);
  });

  it("ignores a copy over ten minutes old", async () => {
    const before = await open(tracks("Long"));
    const page = await open(tracks("Tight"), before, 600_001);
    assert.equal(readsOf(page), 1);
    assert.match(page.get("list").innerHTML, /Tight/);
  });

  it("keeps 'nothing published yet', but not a busy or broken relay", async () => {
    assert.ok((await open(tracks("x", 404))).store.has(SEEN + "/snapshot/tracks"));
    assert.equal((await open(tracks("x", 503))).store.has(SEEN + "/snapshot/tracks"), false);
    assert.equal((await open(tracks("x", 500))).store.has(SEEN + "/snapshot/tracks"), false);
  });

  it("keeps the race state thirty seconds, everything else ten minutes, and the captions never", async () => {
    const page = await open(tracks("Long"));
    assert.equal(page.eval("seenFor('/snapshot/state')"), 30_000);
    assert.equal(page.eval("seenFor('/snapshot/tracks')"), 600_000);
    assert.equal(page.eval("seenFor('/snapshot/captions')"), 0);
  });

  it("keeps nothing over a megabyte", async () => {
    const page = await open(tracks("Long"));
    page.eval("seenKeep('/snapshot/big', 'y'.repeat(1000001))");
    assert.equal(page.store.has(SEEN + "/snapshot/big"), false);
    page.eval("seenKeep('/snapshot/small', 'y')");
    assert.ok(page.store.has(SEEN + "/snapshot/small"));
  });

  it("remembers which reads the page made, for touching its tab next time", async () => {
    const page = await open(tracks("Long"));
    assert.deepEqual(JSON.parse(page.store.get("hc-reads-acme-rc:/c/acme-rc/night") ?? "[]"), ["/snapshot/tracks"]);
  });

  it("never speaks a lap on Live from a copy", () => {
    const live = livePage("acme-rc", "Acme RC");
    assert.match(live, /function render\(state, seen\) \{[\s\S]{0,120}if \(!seen\) lapTalk\.take\(state\);/);
    assert.match(live, /render\(state, answer\.seen === true\);/);
  });
});

describe("the bottom tabs when tapped", () => {
  const pages: [string, string][] = [
    ["home", homePage("acme-rc", "Acme RC")],
    ["live", livePage("acme-rc", "Acme RC")],
    ["schedule", schedulePage("acme-rc", "Acme RC")],
    ["results", resultsPage("acme-rc", "Acme RC")],
    ["a driver's night", nightPage("acme-rc", "Acme RC")],
  ];

  for (const [name, html] of pages) {
    it(`never turn grey under a finger, on ${name}`, () => {
      // An iPhone counts a tap as hovering: the grey is only for a real mouse.
      assert.match(html, /@media \(hover: hover\) \{ nav a:hover \{ color: var\(--text\); background: var\(--surface\); \} \}/);
      assert.doesNotMatch(html.replace(/@media \(hover: hover\) \{[^\n]*\n/g, ""), /nav a:hover/);
      assert.match(html, /nav a \{ -webkit-tap-highlight-color: transparent; \}/);
    });
  }

  it("light the tapped tab at once and wait a third of a second at most for its page's reads", () => {
    const html = homePage("acme-rc", "Acme RC");
    assert.match(html, /tabs\.addEventListener\('pointerdown'/);
    assert.match(html, /light\(a\);/);
    assert.match(html, /setTimeout\(go, 300\);/);
  });
});
