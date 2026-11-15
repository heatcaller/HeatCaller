import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { appShellPage, livePage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * DRIVER PHONE PLAN, STEP C (14 Nov 2026): the board made for a phone and
 * the back button. See plans/driver-phone-ux-plan.md items 4 and 18, and the
 * mockup relay/mockups/live-phone-board.html.
 */

const STATE = "/api/v1/c/acme-rc/snapshot/state";
const COLUMNS = ["Pos", "Driver", "Laps", "Last Lap", "Best Lap", "Gap to P1", "Trend"];
const row = (pos: number, name: string, laps: number, last: string, best: string, gap: string, trend: string) => ({
  cells: [pos, name, laps, last, best, gap, trend].map((text) => ({ text: String(text), emphasis: "none" })),
});
const BOARD = [
  row(1, "Terry Byte", 8, "6.402", "6.247", "-", "▼ 0.35"),
  row(2, "Al Gorithim", 8, "6.388", "6.311", "+1.10", "▼ 0.46"),
  row(3, "Ella Vator", 8, "6.511", "6.311", "+1.52", "▲ 0.18"),
  row(4, "Annie Wayout", 8, "6.690", "6.499", "+2.62", "▲ 0.25"),
];

async function openLive(stored: Record<string, string> = {}): Promise<FakePage> {
  const { html, script } = pageParts(livePage("acme-rc", "Acme RC"));
  let p: FakePage;
  const answer: Answerer = async (url) => url === STATE
    ? { status: 200, body: { seq: 1, publishedAt: new Date(p.clock).toISOString(), body: { state: "Racing", flag: "Green", countdown: "4 LAPS TO GO", columns: COLUMNS, standings: BOARD } } }
    : { status: 404 };
  p = new FakePage(html, answer);
  for (const [k, v] of Object.entries(stored)) p.store.set(k, v);
  p.run(script);
  await p.settle();
  return p;
}

describe("the board made for a phone (item 4)", () => {
  it("marks the four columns a phone keeps: position, driver, the club's gap and last lap", async () => {
    const page = await openLive();
    const head = page.get("board").innerHTML.match(/<thead>[\s\S]*?<\/thead>/)![0];
    const kept = [...head.matchAll(/<th class="([^"]*)">([^<]*)<\/th>/g)].filter((m) => /\bk\b/.test(m[1]!)).map((m) => m[2]);
    assert.deepEqual(kept, ["Pos", "Driver", "Last Lap", "Gap to P1"]);
    assert.match(page.get("board").innerHTML, /All columns/);
  });

  it("has the style that hides the rest under 480 px, and keeps the own row in view below the status bar", () => {
    const html = livePage("acme-rc", "Acme RC");
    assert.match(html, /@media \(max-width: 480px\) \{\s*#board \.card:not\(\.all\) th:not\(\.k\), #board \.card:not\(\.all\) td:not\(\.k\):not\(\.bmc\) \{ display: none; \}/);
    assert.match(html, /tr\.me td \{ position: sticky; top: var\(--stick-t\); bottom: calc\(68px \+ var\(--sa-b\)\)/);
  });

  it("opens a tapped row in place, and All columns brings the whole table back", async () => {
    const page = await openLive();
    page.eval("boardOpen = 'Al Gorithim'; boardHtml = ''; drawBoard(boardState)");
    assert.match(page.get("board").innerHTML, /<tr class="bmore"><td class="bmc"[^>]*><div class="bm"><span><b>8<\/b><i>Laps<\/i><\/span><span><b>6\.311<\/b><i>Best Lap<\/i><\/span><span><b>▼ 0\.46<\/b><i>Trend<\/i><\/span>/);
    page.eval("boardAll = true; boardHtml = ''; drawBoard(boardState)");
    assert.match(page.get("board").innerHTML, /<div class="card all">/);
    assert.match(page.get("board").innerHTML, /Fewer columns/);
  });
});

describe("your card on Live (item 4)", () => {
  it("shows a followed driver's place and both gaps, worked out from Gap to P1", async () => {
    const page = await openLive({ "hg-follow-acme-rc": "Ella Vator" });
    const card = page.get("you").innerHTML;
    assert.equal(page.get("you").hidden, false);
    assert.match(card, /☆ Following<\/span> <b>Ella Vator<\/b>/);
    assert.match(card, /<div class="yp">P3<small>of 4<\/small>/);
    assert.match(card, /Al Gorithim<\/span><span class="t">0\.42<em>to catch<\/em>/);
    assert.match(card, /Annie Wayout<\/span><span class="t">1\.10<em>clear<\/em>/);
    assert.match(card, /<b class="">6\.511<\/b><span>Last<\/span>/);
  });

  it("says You for the driver signed in on this phone, and Leading for P1", async () => {
    const page = await openLive({ "hg-me-acme-rc": "a1b2c3d4e5f60718293a", "hg-me-list-acme-rc": JSON.stringify([{ c: "a1b2c3d4e5f60718293a", n: "Terry Byte" }]) });
    const card = page.get("you").innerHTML;
    assert.match(card, /<span class="chip">You<\/span> <b>Terry Byte<\/b>/);
    assert.match(card, /Nobody ahead<\/span><span class="t">Leading/);
    assert.match(page.get("board").innerHTML, /<tr data-row="Terry Byte" class="me"/, "their row is marked on the board");
  });

  it("is not drawn for somebody who isn't on the board, or with nobody followed or signed in", async () => {
    assert.equal((await openLive()).get("you").hidden, true);
    assert.equal((await openLive({ "hg-follow-acme-rc": "Nobody Here" })).get("you").hidden, true);
  });
});

describe("Back walks the tabs in the home-screen app (item 18)", () => {
  it("adds a history step for each tab, and shows that page again on Back", () => {
    const shell = appShellPage("acme-rc", "Acme RC");
    assert.match(shell, /history\.pushState\(\{ hc: 1 \}, '', to\);\s*\}\s*catch \(e2\) \{[^}]*\}\s*hcGo\(to\);/);
    // Bug hunt 20 #4: a Live page that let go of the bar takes it again on Back.
    assert.match(shell, /addEventListener\('popstate', \(\) => \{\s*const path = [^;]*;[\s\S]*?if \(!SHELL && !hcHeld\) \{\s*if \(hcHeldOnce === ''[^)]*\)[^;]*return;\s*hcHold\(hcHeldOnce\);/);
  });
});
