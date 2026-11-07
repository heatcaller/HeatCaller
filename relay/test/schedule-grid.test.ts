import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, it } from "node:test";
import { schedulePage } from "../src/pages.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * TAP A RACE FOR ITS GRID (6 Nov 2026, Asher): a race opened on the
 * Schedule's running order shows who starts where and in what car.
 */
describe("Schedule: a race's grid", () => {
  async function page(): Promise<FakePage> {
    const { html, script } = pageParts(schedulePage("acme-rc", "Acme RC"));
    const p = new FakePage(html, async () => ({ status: 404 }));
    p.run(script);
    await p.settle();
    return p;
  }

  const line = {
    id: "a",
    drivers: ["Sam Smith", "Bill Travers"],
    grid: [
      { slot: 1, name: "Sam Smith", car: "Associated B7" },
      { slot: 3, name: "Bill Travers", car: null },
    ],
  };

  it("draws a row per driver with their grid slot and car", async () => {
    const p = await page();
    const html = String(p.eval(`lineupHtml(${JSON.stringify(line)}, ${JSON.stringify(line.drivers)}, '')`));
    assert.match(html, /<table class="sgrid">/);
    assert.match(html, /<th class="pos">Grid<\/th><th>Driver<\/th><th>Car<\/th>/);
    assert.match(html, /<td class="pos">1<\/td><td>.*?Sam Smith.*?<\/td><td class="scar">Associated B7<\/td>/);
    // A slot left by somebody not shown keeps its number; no car is a dash.
    assert.match(html, /<td class="pos">3<\/td><td>.*?Bill Travers.*?<\/td><td class="scar">&ndash;<\/td>/);
  });

  it("leaves the car column out when nobody's car is known", async () => {
    const p = await page();
    const noCars = { ...line, grid: line.grid.map((g) => ({ ...g, car: null })) };
    const html = String(p.eval(`lineupHtml(${JSON.stringify(noCars)}, ${JSON.stringify(line.drivers)}, '')`));
    assert.doesNotMatch(html, /Car<\/th>|class="scar"/);
  });

  it("marks the driver being searched for", async () => {
    const p = await page();
    const html = String(p.eval(`lineupHtml(${JSON.stringify(line)}, ${JSON.stringify(line.drivers)}, 'bill')`));
    assert.match(html, /<tr class="me">.*<mark><span class="nm" data-n="Bill Travers">Bill Travers<\/span><\/mark>/);
    assert.doesNotMatch(html, /<tr class="me"><td class="pos">1</);
  });

  it("keeps the plain list of names from a laptop that sends no grid", async () => {
    const p = await page();
    const html = String(p.eval(`lineupHtml({ id: 'a' }, ${JSON.stringify(line.drivers)}, '')`));
    assert.match(html, /^<div class="lineup">.*Sam Smith.*, .*Bill Travers.*<\/div>$/);
    assert.doesNotMatch(html, /<table/);
  });
});

describe("Schedule: the grid follows the relay's roster switch", () => {
  let harness: Harness;

  beforeEach(async () => {
    harness = await start();
  });

  afterEach(async () => {
    await harness.close();
  });

  it("takes the grid out with the names for a club whose roster is not allowed", async () => {
    const programme = {
      lineups: true,
      lines: [{ id: "a", className: "Stock", status: "next", drivers: ["Sam Smith"], grid: [{ slot: 1, name: "Sam Smith", car: "B7" }] }],
    };
    const published = await call(harness, "POST", "/api/v1/c/acme-rc/publish/programme?seq=1", { token: ACME_TOKEN, json: programme });
    assert.equal(published.status, 200);

    const answer = await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/programme");
    assert.equal(answer.body.body.lines[0].grid, null);
    assert.equal(answer.body.body.lines[0].drivers, null);
    assert.doesNotMatch(answer.text, /Sam Smith|B7/);
  });
});
