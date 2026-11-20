import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { resultsPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * THE POLE SHOOTOUT AND THE INVITATIONAL ON THE PUBLIC RESULT (19 Nov 2026).
 * Under a shootout run the laptop sends the shootout order so far, and under
 * an Invitational main the standings on points, as `standings` - a title, a
 * line on the scoring and rows of public names. The Results page draws it
 * under the result, escaped; every other result has none and draws nothing.
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

const LINE = { id: "s1", at: "19:30", label: "Invitational Main 2", day: "2026-11-19" };
const FINISHER = { pos: 1, name: "Sam P", laps: 20, elapsedMs: 300000, fastLapMs: 14000 };
const STANDINGS = {
  title: "Invitational standings after main 2 of 5",
  note: "A finish scores 1st = 1 point; the best 4 of 5 count, fewest points wins.",
  rows: [
    { pos: 1, name: "Sam P", value: "3 pts", detail: "Main 1 1st (1 pt), Main 2 2nd (2 pts)" },
    { pos: 2, name: "Max P", value: "3 pts", detail: "Main 1 2nd (2 pts), Main 2 1st (1 pt)" },
  ],
};

describe("Shootout and Invitational standings on the Results page", () => {
  it("draws the standings under the result", async () => {
    const page = await resultsScript();
    const html = call(page, `card(${JSON.stringify(LINE)}, ${JSON.stringify({ results: [FINISHER], standings: STANDINGS })}, '')`);

    const result = html.indexOf("Sam P");
    const table = html.indexOf('<div class="fstand">');

    assert.ok(result > 0);
    assert.ok(table > result, "the standings come after the result");
    assert.match(html.slice(table), /Invitational standings after main 2 of 5/);
    assert.match(html.slice(table), /3 pts/);
    assert.match(html.slice(table), /Max P/);
  });

  it("escapes the names, the title and the detail", async () => {
    const page = await resultsScript();
    const evil = "<img src=x onerror=alert(1)>";
    const html = call(page, `finalsStandings(${JSON.stringify({ standings: { title: evil, note: evil, rows: [{ pos: 1, name: evil, value: evil, detail: evil }] } })})`);

    assert.ok(!html.includes("<img"));
    assert.ok(html.includes("&lt;img"));
  });

  it("draws nothing for a result without standings, or one from an older laptop", async () => {
    const page = await resultsScript();

    assert.equal(call(page, `finalsStandings({ results: [] })`), "");
    assert.equal(call(page, `finalsStandings({ standings: null })`), "");
    assert.equal(call(page, `finalsStandings({ standings: { rows: [] } })`), "");
    assert.equal(call(page, `finalsStandings({ standings: { rows: "Sam" } })`), "");
    assert.equal(call(page, `finalsStandings(null)`), "");
  });
});
