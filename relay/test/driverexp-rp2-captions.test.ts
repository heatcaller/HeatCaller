import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { DEFAULT_CONTEXT, livePage } from "../src/pages.js";
import { FakePage, pageParts, type FakeNode } from "./driverexp-rp1-fakepage.js";

/**
 * THE LIVE PAGE'S CAPTIONS VIEW (22 Oct 2026, driver experience, RP2).
 *
 * A "Captions" button (aria-pressed) and a polite live region that reads
 * the public captions snapshot every two seconds - with no audio, and
 * beside the Listen button rather than instead of it. Run as the whole
 * live page script against the RP1 fake browser.
 */

const CAPTIONS = "/api/v1/c/acme-rc/snapshot/captions";

interface Line {
  n: number;
  t: string;
  at?: string;
}

/** What the relay answers the captions read with: a stored snapshot, a 404, a deleted (null) body - or no answer at all ("hang"). */
type Held = { lines: Line[] } | 404 | null | 429 | "hang";

function page(held: () => Held, listening = false): FakePage {
  const context = { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, listening } };
  const { html, script } = pageParts(livePage("acme-rc", "Acme RC", context));
  const p = new FakePage(html, async (url) => {
    if (url === CAPTIONS) {
      const h = held();
      if (h === 404) return { status: 404, body: { ok: false, error: "Nothing has been published to \"captions\" yet." } };
      if (h === 429) return { status: 429, body: { ok: false } };
      if (h === "hang") return { status: 0, hang: true };
      return { status: 200, body: { seq: 1, publishedAt: "2026-10-22T19:00:00Z", body: h === null ? null : { at: "2026-10-22T19:00:00Z", ...h } } };
    }
    if (url.endsWith("/snapshot/state")) return { status: 404 };
    if (url.endsWith("/snapshot/listen") || url.includes("/listen")) return { status: 404 };
    return { status: 404 };
  });
  p.run(script);
  return p;
}

function reads(p: FakePage): number {
  return p.sent.filter((s) => s.url === CAPTIONS).length;
}

function shown(p: FakePage): string[] {
  return p.get("caplines").children.map((c: FakeNode) => c.textContent);
}

function lines(...n: number[]): { lines: Line[] } {
  return { lines: n.map((i) => ({ n: i, t: `Line ${i}`, at: "2026-10-22T19:00:00Z" })) };
}

describe("the captions control", () => {
  it("is drawn, pressed false, with a polite live region, where the club has no Listen", () => {
    const { html } = pageParts(livePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, listening: false } }));
    assert.match(html, /<button type="button" id="capbtn"[^>]*aria-pressed="false"[^>]*>Captions<\/button>/);
    assert.match(html, /id="caplines"[^>]*aria-live="polite"/);
    assert.doesNotMatch(html, /id="listenbtn"/);
  });

  // 4 Nov 2026, asked for by Asher: Listen shows the words itself, so a
  // second Captions card under Message the announcer is gone.
  it("is not drawn where the club has Listen, which shows the words itself", () => {
    const { html } = pageParts(livePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, listening: true } }));
    assert.match(html, /id="listenbtn"/);
    assert.doesNotMatch(html, /id="capbtn"/);
  });

  it("reads nothing until pressed, then every two seconds, and stops when pressed again", async () => {
    const p = page(() => lines(1, 2));
    await p.settle();
    await p.advance(10000);
    assert.equal(reads(p), 0, "nothing fetched for a closed view");

    const btn = p.get("capbtn");
    btn.click();
    await p.settle();
    assert.equal(btn.getAttribute("aria-pressed"), "true");
    assert.equal(reads(p), 1);
    assert.deepEqual(shown(p), ["Line 1", "Line 2"]);

    await p.advance(2000);
    assert.equal(reads(p), 2);
    await p.advance(4000);
    assert.equal(reads(p), 4);

    btn.click();
    await p.settle();
    assert.equal(btn.getAttribute("aria-pressed"), "false");
    assert.deepEqual(shown(p), []);
    await p.advance(10000);
    assert.equal(reads(p), 4, "no reads once off");
  });

  it("adds only new lines, keeps the last six, and starts afresh when the count goes back", async () => {
    let held: Held = lines(1, 2, 3);
    const p = page(() => held);
    p.get("capbtn").click();
    await p.settle();

    const first = p.get("caplines").children[0];
    held = lines(1, 2, 3, 4);
    await p.advance(2000);
    assert.deepEqual(shown(p), ["Line 1", "Line 2", "Line 3", "Line 4"]);
    assert.equal(p.get("caplines").children[0], first, "an old line is not drawn again - a screen reader would repeat it");

    held = lines(3, 4, 5, 6, 7, 8, 9, 10);
    await p.advance(2000);
    assert.deepEqual(shown(p), ["Line 5", "Line 6", "Line 7", "Line 8", "Line 9", "Line 10"]);

    held = lines(1);
    await p.advance(2000);
    assert.deepEqual(shown(p), ["Line 1"], "a laptop restart starts the view again");
  });

  it("says so when there is nothing: never published, switched off, or no lines", async () => {
    for (const held of [404, null, { lines: [] }] as Held[]) {
      const p = page(() => held);
      p.get("capbtn").click();
      await p.settle();
      assert.match(p.get("capnote").textContent, /No captions just now/);
      assert.deepEqual(shown(p), []);
    }
  });

  it("a busy relay keeps what is on screen", async () => {
    let held: Held = lines(1);
    const p = page(() => held);
    p.get("capbtn").click();
    await p.settle();
    held = 429;
    await p.advance(2000);
    assert.deepEqual(shown(p), ["Line 1"]);
    assert.match(p.get("capnote").textContent, /busy/);
  });

  // 26 Oct 2026, asked for by Asher: "when watching the live captions
  // through the relay they stopped updating". One read at a time (busy), so
  // a read that never answered held every later one back for good.
  it("gives up on a read that never answers, and carries on reading", async () => {
    let held: Held = lines(1);
    const p = page(() => held);
    p.get("capbtn").click();
    await p.settle();
    assert.deepEqual(shown(p), ["Line 1"]);

    held = "hang";
    await p.advance(2000);
    assert.equal(reads(p), 2);

    // Still waiting on it: nothing new is asked for meanwhile.
    held = lines(1, 2);
    await p.advance(6000);
    assert.equal(reads(p), 2, "one read at a time, as before");

    // Ten seconds on it is given up, and the next read draws the new line.
    await p.advance(8000);
    assert.ok(reads(p) >= 3, "reading again after the stuck read was given up");
    assert.deepEqual(shown(p), ["Line 1", "Line 2"]);
  });

  it("draws the words as text, never markup, and skips lines of the wrong shape", async () => {
    const p = page(() => ({ lines: [{ n: 1, t: "<img src=x onerror=alert(1)> Sam takes the lead" }, { n: "2", t: "bad" } as unknown as Line, { n: 3, t: "  " }] }));
    p.get("capbtn").click();
    await p.settle();
    assert.deepEqual(shown(p), ["<img src=x onerror=alert(1)> Sam takes the lead"]);
    assert.equal(p.get("caplines").hadMarkup, false);
    assert.equal(p.get("caplines").children.some((c) => c.hadMarkup), false);
  });

  it("does not read while the page is hidden, and catches up on return", async () => {
    const p = page(() => lines(1));
    p.get("capbtn").click();
    await p.settle();
    p.hidden = true;
    await p.advance(10000);
    assert.equal(reads(p), 1);

    p.hidden = false;
    for (const listener of p.documentListeners.get("visibilitychange") ?? []) listener({});
    await p.settle();
    assert.equal(reads(p), 2);
  });

  // "leaves the Listen button alone" went on 4 Nov 2026: Captions and
  // Listen are never on the page together now (see the test above).
});
