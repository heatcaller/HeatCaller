import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { livePage } from "../src/pages.js";
import { FakePage, pageParts, type FakeNode } from "./driverexp-rp1-fakepage.js";

/**
 * RACE VIDEO, V4 (14 Nov 2026, plans/race-video-access-plan.md; Asher:
 * "Dock it"): Watch live docks YouTube's player at the top of Live, with
 * the board scrolling under it.
 */

async function live(answers: { live: string | null }, search = ""): Promise<FakePage> {
  const { html, script } = pageParts(livePage("acme-rc", "Acme RC"));
  const page = new FakePage(html, async (url) => {
    if (url.endsWith("/video/live")) return { status: 200, body: { ok: true, live: answers.live } };
    return { status: 404 };
  }, { search });
  page.run(script);
  await page.settle();
  return page;
}

function buttonIn(root: FakeNode, words: RegExp): FakeNode {
  const b = root.find((n) => n.tagName === "BUTTON" && words.test(n.textContent + " " + (n.getAttribute("aria-label") ?? "")))[0];
  assert.ok(b, `a button ${words}`);
  return b;
}

describe("the docked live player (V4)", () => {
  it("puts the dock above the flag bar and the board, and nothing in it until asked", async () => {
    const html = livePage("acme-rc", "Acme RC");
    assert.ok(html.indexOf('id="hcdock"') < html.indexOf('id="bar"'));
    assert.ok(html.indexOf('id="bar"') < html.indexOf('id="board"'));
    const page = await live({ live: null });
    assert.equal(page.get("hcwatch").hidden, true, "no Watch live while nothing streams");
    assert.equal(page.get("hcdock").children.length, 0, "an empty dock");
    assert.match(html, /<div id="hcdock" class="hcdock" hidden><\/div>/);
  });

  it("offers Watch live while the stream is on, and docks the player, playing, on a tap", async () => {
    const page = await live({ live: "AAAAAAAAAAA" });
    assert.equal(page.get("hcwatch").hidden, false);
    buttonIn(page.get("hcwatch"), /Watch live/).click();

    const dock = page.get("hcdock");
    assert.equal(dock.hidden, false);
    const frame = dock.find((n) => n.tagName === "IFRAME")[0]! as unknown as { src: string };
    assert.match(frame.src, /^https:\/\/www\.youtube-nocookie\.com\/embed\/AAAAAAAAAAA\?autoplay=1&playsinline=1/);
    assert.match(dock.textContent, /The video runs about 20 s behind the timing\./);
    assert.equal(page.get("hcwatch").hidden, true, "the button goes while it's docked");
  });

  it("shrinks to a corner and closes", async () => {
    const page = await live({ live: "AAAAAAAAAAA" });
    buttonIn(page.get("hcwatch"), /Watch live/).click();
    buttonIn(page.get("hcdock"), /Make the player smaller/).click();
    assert.equal(page.get("hcdock").className, "hcdock small");
    buttonIn(page.get("hcdock"), /Close the player/).click();
    assert.equal(page.get("hcdock").hidden, true);
    assert.equal(page.get("hcwatch").hidden, false, "Watch live is back");
  });

  it("says the stream has stopped when it does, and never holds the board back", async () => {
    const answers = { live: "AAAAAAAAAAA" as string | null };
    const page = await live(answers);
    buttonIn(page.get("hcwatch"), /Watch live/).click();
    answers.live = null;
    await page.advance(60000);
    await page.settle();
    assert.match(page.get("hcdock").textContent, /The stream has stopped\./);
    assert.equal(page.get("hcdock").find((n) => n.tagName === "IFRAME").length, 0);
    assert.doesNotMatch(livePage("acme-rc", "Acme RC"), /hcdock[^]*setTimeout\([^)]*board/);
  });

  it("docks by itself, not playing, when Home's Watch sent them", async () => {
    const page = await live({ live: "AAAAAAAAAAA" }, "?watch=1");
    const frame = page.get("hcdock").find((n) => n.tagName === "IFRAME")[0]! as unknown as { src: string };
    assert.match(frame.src, /autoplay=0/);
  });

  it("turns Listen off while watching, and says so", async () => {
    const { html, script } = pageParts(livePage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async (url) => (url.endsWith("/video/live") ? { status: 200, body: { ok: true, live: "AAAAAAAAAAA" } } : { status: 404 }), {});
    page.run(script);
    await page.settle();
    let stopped = 0;
    page.eval("window.hcListening = () => true; window.hcStopListen = () => { window.__stopped = (window.__stopped || 0) + 1; };");
    buttonIn(page.get("hcwatch"), /Watch live/).click();
    stopped = Number(page.eval("window.__stopped || 0"));
    assert.equal(stopped, 1);
    assert.match(page.get("hcdock").textContent, /Listen is off while you watch\./);
  });
});
