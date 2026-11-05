import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { resultsPage } from "../src/pages.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";

/**
 * RIVALS (4 Nov 2026, from the relay roadmap): on a driver's history, the
 * drivers met most in the races shown and who finished ahead how often.
 * Plus the theoretical best on View laps (lxTheoretical).
 */
describe("Results: head to head, and the theoretical best", () => {
  async function page(): Promise<FakePage> {
    const { html, script } = pageParts(resultsPage("acme-rc", "Acme RC"));
    const p = new FakePage(html, async () => ({ status: 404 }));
    p.run(script);
    await p.settle();
    return p;
  }

  it("tallies who finished ahead, for drivers met twice or more", async () => {
    const p = await page();
    const races = {
      a: { results: [{ pos: 1, name: "Sam P." }, { pos: 2, name: "Asher K." }, { pos: 3, name: "Jordan M." }] },
      b: { results: [{ pos: 1, name: "Asher K." }, { pos: 2, name: "Sam P." }] },
      c: { results: [{ pos: 1, name: "Sam P." }, { pos: 2, name: "Asher K." }, { pos: 3, name: "Unnamed driver" }] },
    };
    const html = String(p.eval(`rivalsOf([{ id: 'a' }, { id: 'b' }, { id: 'c' }], new Map(Object.entries(${JSON.stringify(races)})), 'Asher K.')`));
    assert.match(html, /Head to head/);
    assert.match(html, /Sam P\.<\/span> <b>Asher K\. 1 - 2<\/b>/);
    assert.doesNotMatch(html, /Jordan M\./, "met once is not a rivalry");
    assert.doesNotMatch(html, /Unnamed driver/);
  });

  it("adds up the best sectors, and has none without a whole set", async () => {
    const p = await page();
    assert.equal(p.eval("lxTheoretical([3410, 6020])"), 9430);
    assert.equal(p.eval("lxTheoretical([3410])"), null);
    assert.equal(p.eval("lxTheoretical([3410, 0])"), null);
    assert.equal(p.eval("lxTheoretical(null)"), null);
  });
});

describe("Results: the race control log (4 Nov 2026)", () => {
  it("lists what was corrected, by whom it was about, and never a reason", async () => {
    const { html, script } = pageParts(resultsPage("acme-rc", "Acme RC"));
    const p = new FakePage(html, async () => ({ status: 404 }));
    p.run(script);
    await p.settle();
    assert.equal(p.eval("raceControlLog({ results: [] })"), "");
    const log = String(p.eval(`raceControlLog(${JSON.stringify({ corrections: [{ at: "2026-11-04T21:14:00Z", name: "Sam P.", change: "time penalty 0s -> 5s" }, { at: "x", name: "<b>x</b>", change: "<img src=x>" }] })})`));
    assert.match(log, /Race control/);
    assert.match(log, /Sam P\.<\/span>: time penalty 0s -&gt; 5s/);
    assert.doesNotMatch(log, /<img|<b>x/, "the club's typing as text");
  });
});

describe("Results: club health (4 Nov 2026)", () => {
  it("counts race days, drivers, a night's average and first-timers who came back", async () => {
    const { html, script } = pageParts(resultsPage("acme-rc", "Acme RC"));
    const p = new FakePage(html, async () => ({ status: 404 }));
    p.run(script);
    await p.settle();
    // Three nights: drivers 0 and 1 on the first, 1 and 2 on the second, 0 on the third.
    p.eval(`idx = { days: [{ day: '2026-10-01', sessions: [0] }, { day: '2026-10-08', sessions: [1] }, { day: '2026-10-15', sessions: [2] }],
      sessions: [{ id: 'a' }, { id: 'b' }, { id: 'c' }], drivers: [{ n: 'A' }, { n: 'B' }, { n: 'C' }],
      raced: [[0, 2], [0, 1], [1]] }; year = '2026';`);
    const said = String(p.eval("clubHealth()"));
    assert.match(said, /The club in 2026/);
    assert.match(said, /<dt>Race days<\/dt><dd>3<\/dd>/);
    assert.match(said, /<dt>Drivers<\/dt><dd>3<\/dd>/);
    assert.match(said, /<dt>A night, on average<\/dt><dd>1\.7<\/dd>/);
    assert.match(said, /<dt>First-timers who came back<\/dt><dd>2 of 3<\/dd>/);
    assert.equal((said.match(/class="ch-bar"/g) ?? []).length, 3);
  });
});
