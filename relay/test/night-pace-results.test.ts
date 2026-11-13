import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { resultsPage } from "../src/pages.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";

/**
 * Night pace on the club's online results (12 Nov 2026, setup plan step 8,
 * build plan 0.1). The laptop puts a "pace" line on a day of the results
 * index; the page shows it under that day's heading, escaped, and a day
 * without one - every index published before this - looks as it always did.
 */

async function results(): Promise<FakePage> {
  const { html, script } = pageParts(resultsPage("acme-rc", "Acme RC"));
  const p = new FakePage(html, async () => ({ status: 404 }));
  p.run(script);
  await p.settle();
  return p;
}

describe("night pace on the results page", () => {
  it("shows a day's pace under its heading", async () => {
    const p = await results();
    p.eval(`idx = { drivers: [], sessions: [{ id: 'a', day: '2026-11-12', at: '19:30', label: 'Q1 Heat 1' }],
      days: [{ day: '2026-11-12', sessions: [0], pace: 'Green to green 6:10 · 31% on track' }] };`);
    const html = String(p.eval(`drawGroups(visibleDays(), new Map(), '')`));
    assert.match(html, /<p class="pace">Night pace: Green to green 6:10 · 31% on track<\/p>/);
  });

  it("leaves a day with no pace as it was", async () => {
    const p = await results();
    p.eval(`idx = { drivers: [], sessions: [{ id: 'a', day: '2026-11-12', at: '19:30', label: 'Q1 Heat 1' }],
      days: [{ day: '2026-11-12', sessions: [0] }] };`);
    const html = String(p.eval(`drawGroups(visibleDays(), new Map(), '')`));
    assert.doesNotMatch(html, /class="pace"/);
  });

  it("escapes what it is sent", async () => {
    const p = await results();
    assert.equal(String(p.eval(`paceLine('<b>fast</b>')`)), '<p class="pace">Night pace: &lt;b&gt;fast&lt;/b&gt;</p>');
    assert.equal(String(p.eval(`paceLine(42)`)), "");
  });
});
