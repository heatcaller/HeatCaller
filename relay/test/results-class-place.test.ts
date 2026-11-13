import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { resultsPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * CLASS PLACES ON THE PUBLIC RESULT (13 Nov 2026, mixed-class heats). In a
 * heat two classes shared, the laptop sends each row's class (`cls`) and
 * place in it (`clsPos`); the Results page says "2nd in Stock" beside the
 * name, and a driver's own card puts it before the overall place. A row
 * without them - every other race - is drawn as it always was.
 */

async function resultsScript(): Promise<FakePage> {
  const answer: Answerer = async () => ({ status: 404 });
  const { html, script } = pageParts(resultsPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, answer);
  page.run(script);
  await page.settle();
  return page;
}

function call(page: FakePage, expression: string): string {
  return String(page.eval(expression));
}

const LINE = { id: "s1", at: "19:30", label: "Heat 1", day: "2026-11-13" };
const SHARED = {
  results: [
    { pos: 1, name: "Mo D", laps: 21, elapsedMs: 300000, fastLapMs: 13000, cls: "Mod", clsPos: 1 },
    { pos: 2, name: "Sam P", laps: 20, elapsedMs: 300000, fastLapMs: 14000, cls: "Stock", clsPos: 1 },
    { pos: 3, name: "Bea K", laps: 20, elapsedMs: 301000, fastLapMs: 14100, cls: "Stock", clsPos: 2 },
  ],
};

describe("Class places on the Results page", () => {
  it("says each driver's place in their own class beside their name", async () => {
    const page = await resultsScript();
    const html = call(page, `card(${JSON.stringify(LINE)}, ${JSON.stringify(SHARED)}, '')`);

    assert.match(html, /Bea K.*2nd in Stock/s);
    assert.match(html, /Sam P.*1st in Stock/s);
    assert.match(html, /Mo D.*1st in Mod/s);
  });

  it("puts a driver's class place before the overall one on their own card", async () => {
    const page = await resultsScript();
    const html = call(page, `card(${JSON.stringify(LINE)}, ${JSON.stringify(SHARED)}, 'Bea K')`);

    assert.match(html, /<span class="winner">2nd in Stock &middot; 3rd of 3<\/span>/);
  });

  it("draws nothing extra for a row from a one-class race", async () => {
    const page = await resultsScript();

    assert.equal(call(page, `clsPlace({ pos: 1, name: "Sam P" })`), "");
    assert.equal(call(page, `clsPlace({ cls: "Stock", clsPos: 0 })`), "");
    assert.equal(call(page, `clsPlace({ cls: "", clsPos: 2 })`), "");
    assert.equal(call(page, `clsPlace(null)`), "");
  });

  it("escapes a class name", async () => {
    const page = await resultsScript();
    const html = call(page, `card(${JSON.stringify(LINE)}, ${JSON.stringify({ results: [{ pos: 1, name: "Sam P", laps: 1, elapsedMs: 1, cls: "<b>x</b>", clsPos: 1 }] })}, '')`);

    assert.ok(!html.includes("<b>x</b>"));
    assert.ok(html.includes("&lt;b&gt;"));
  });
});
