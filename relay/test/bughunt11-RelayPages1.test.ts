import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { resultsPage } from "../src/pages.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";
import { withSharedInline } from "./pagescript.js";

/**
 * Bug hunt 11 (5 Nov 2026), relay pages, part 1: the results page's head to
 * head and club health cards, View laps in the flat listing, and the framed
 * page's hand-off to the one bar.
 */

async function resultsWith(answer: (url: string) => { status: number; body?: unknown }): Promise<FakePage> {
  const { html, script } = pageParts(withSharedInline(resultsPage("acme-rc", "Acme RC")));
  const p = new FakePage(html, async (url) => answer(url));
  p.run(script);
  await p.settle();
  return p;
}

describe("Bug hunt 11: head to head finds the driver by the index's name", () => {
  it("counts a heat whose result shortened the driver's name further than the index did", async () => {
    const p = await resultsWith(() => ({ status: 404 }));
    // The year has Alex Kent and Alex Kemp, so the index says "Alex Ken";
    // a heat without Kemp shortened Kent to "Alex K".
    p.eval(`idx = { drivers: [{ n: 'Alex Ken' }, { n: 'Alex Kem' }, { n: 'Jordan M' }] };`);
    const races = {
      a: { results: [{ pos: 1, name: "Jordan M" }, { pos: 2, name: "Alex K" }] },
      b: { results: [{ pos: 1, name: "Alex K" }, { pos: 2, name: "Jordan M" }] },
      c: { results: [{ pos: 1, name: "Jordan M" }, { pos: 2, name: "Alex Ken" }, { pos: 3, name: "Alex Kem" }] },
    };
    const html = String(p.eval(`rivalsOf([{ id: 'a' }, { id: 'b' }, { id: 'c' }], new Map(Object.entries(${JSON.stringify(races)})), 'Alex Ken')`));
    assert.match(html, /Head to head/);
    assert.match(html, /Jordan M<\/span> <b>Alex Ken 1 - 2<\/b>/);
  });

  it("keeps one rival shortened two ways in two heats as one rival", async () => {
    const p = await resultsWith(() => ({ status: 404 }));
    p.eval(`idx = { drivers: [{ n: 'Asher K' }, { n: 'Sam Pa' }] };`);
    const races = {
      a: { results: [{ pos: 1, name: "Sam Pa" }, { pos: 2, name: "Asher K" }] },
      b: { results: [{ pos: 1, name: "Asher K" }, { pos: 2, name: "Sam P" }] },
    };
    const html = String(p.eval(`rivalsOf([{ id: 'a' }, { id: 'b' }], new Map(Object.entries(${JSON.stringify(races)})), 'Asher K')`));
    assert.match(html, /Sam Pa<\/span> <b>Asher K 1 - 1<\/b>/);
  });

  it("does not guess between two index drivers a short name could be", async () => {
    const p = await resultsWith(() => ({ status: 404 }));
    p.eval(`idx = { drivers: [{ n: 'Asher K' }, { n: 'Sam Pa' }, { n: 'Sam Po' }] };`);
    const races = {
      a: { results: [{ pos: 1, name: "Sam Pa" }, { pos: 2, name: "Asher K" }] },
      b: { results: [{ pos: 1, name: "Asher K" }, { pos: 2, name: "Sam P" }] },
    };
    const html = String(p.eval(`rivalsOf([{ id: 'a' }, { id: 'b' }], new Map(Object.entries(${JSON.stringify(races)})), 'Asher K')`));
    assert.equal(html, "", "Sam P could be either Sam, so neither is met twice");
  });
});

describe("Bug hunt 11: club health reads the laptop's newest-first days", () => {
  it("counts the oldest night's non-returners as first-timers and draws the bars oldest first", async () => {
    const p = await resultsWith(() => ({ status: 404 }));
    // Night 1 (1 Oct): A and B. Night 2 (8 Oct): A only. Newest first, as
    // RelayResultsIndex publishes them.
    p.eval(`idx = { days: [{ day: '2026-10-08', sessions: [1] }, { day: '2026-10-01', sessions: [0] }],
      sessions: [{ id: 'a' }, { id: 'b' }], drivers: [{ n: 'A' }, { n: 'B' }],
      raced: [[0, 1], [0]] }; year = '2026';`);
    const said = String(p.eval("clubHealth()"));
    assert.match(said, /<dt>First-timers who came back<\/dt><dd>1 of 2<\/dd>/);
    const titles = [...said.matchAll(/class="ch-bar"[^>]*title="([^"]+)"/g)].map((m) => m[1]);
    assert.deepEqual(titles, ["2026-10-01: 2", "2026-10-08: 1"]);
  });
});

describe("Bug hunt 11: View laps in the flat listing", () => {
  it("writes the lap panel the first time it opens", async () => {
    const p = await resultsWith((url) => {
      if (url.includes("/collection/results") && !url.includes("resultsindex")) {
        return {
          status: 200,
          body: {
            items: [{
              id: "s1",
              publishedAt: "2026-10-22T18:00:00.000Z",
              body: { label: "Heat 1", results: [{ pos: 1, name: "Sam P", laps: 3, elapsedMs: 60000, lapsMs: [20000, 19500, 20500] }] },
            }],
          },
        };
      }
      return { status: 404 };
    });
    assert.equal(p.eval("flat"), true, "no index, so the flat listing");
    assert.match(p.get("feed").drawn(), /data-lx="s1:0"/);

    const inserted: string[] = [];
    const target = {
      open: true,
      dataset: { lx: "s1:0" },
      querySelector: () => null,
      insertAdjacentHTML: (_where: string, html: string) => inserted.push(html),
    };
    for (const listener of p.get("feed").listeners.get("toggle") ?? []) listener({ target });
    assert.equal(inserted.length, 1, "the panel is written at once, not on the next redraw");
    assert.match(inserted[0] ?? "", /class="[^"]*lxp/);
  });
});

describe("Bug hunt 11: the framed page hands club links to the bar", () => {
  it("only treats a path with a file extension as a file", () => {
    const page = withSharedInline(resultsPage("acme-rc", "Acme RC"));
    const line = page.split("\n").find((l) => l.includes(".test(url.pathname)") && l.includes("A-Za-z0-9"));
    assert.ok(line, "the page has the looks-like-a-file guard");
    const literal = /\/(\\?\.\[A-Za-z0-9\]\+\$)\//.exec(line)?.[1];
    assert.ok(literal);
    const looksLikeFile = new RegExp(literal);
    assert.equal(looksLikeFile.test("/c/acme-rc/standings"), false);
    assert.equal(looksLikeFile.test("/c/acme-rc/results"), false);
    assert.equal(looksLikeFile.test("/c/acme-rc/logo.png"), true);
  });
});
