import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { nightPage, resultsPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * LAP BY LAP, AS LIVERC SHOWS IT (28 Oct 2026, asked for by Asher: "We need
 * to make sure that all of the data points, including the graph, are
 * available in the relay. Need feature parity here").
 *
 * The heat results gain Qual, Laps/Time, Behind, View laps and the
 * lap-by-lap position graph; a driver's own night gains their lap stats and
 * lap list. The arithmetic is LAPS_JS, shared by both pages, and the
 * figures are checked against LiveRC's own screens where they show them.
 */

// LiveRC's own lap list for the winner of a 3:00 heat: 10.113, 8.896,
// 8.927 shown with pace 18/3:02.034, 19/3:00.586, 20/3:06.240.
const LIVERC_FIRST_THREE = [10113, 8896, 8927];

async function resultsScript(): Promise<FakePage> {
  const answer: Answerer = async () => ({ status: 404 });
  const { html, script } = pageParts(resultsPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, answer);
  page.run(script);
  await page.settle();
  return page;
}

// Through JSON, so an array the page made compares equal to one made here
// (the page's arrays come from the page's own realm).
function call(page: FakePage, expression: string): unknown {
  const value = page.eval(expression);
  return value === undefined ? value : JSON.parse(JSON.stringify(value));
}

describe("lap stats and pace", () => {
  it("projects pace exactly as LiveRC's lap list does", async () => {
    const page = await resultsScript();
    const rows = call(page, `lapRows(${JSON.stringify(LIVERC_FIRST_THREE)}, null, 0, 180)`) as { pace: string }[];

    assert.deepEqual(rows.map((r) => r.pace), ["18/3:02.034", "19/3:00.586", "20/3:06.240"]);
  });

  it("counts the start-lap slot in the time a lap was crossed", async () => {
    const page = await resultsScript();
    // 2.0 s to the line, then a 10.0 s lap: crossed at 12.0 s, so 15 laps of 12.0 s.
    const rows = call(page, "lapRows([10000], [1], 2000, 180)") as { pace: string; pos: number }[];

    assert.equal(rows[0]!.pace, "15/3:00.000");
    assert.equal(rows[0]!.pos, 1);
  });

  it("has no pace without a time limit, and no positions that are out of step", async () => {
    const page = await resultsScript();
    const rows = call(page, "lapRows([9000, 8000, 8500], [2, 1], 0, undefined)") as { pace: string; pos: number }[];

    assert.deepEqual(rows.map((r) => r.pace), ["", "", ""]);
    assert.deepEqual(rows.map((r) => r.pos), [0, 0, 0]);
  });

  it("marks the best lap once and keeps each lap's position on its own row", async () => {
    const page = await resultsScript();
    const rows = call(page, "lapRows([9000, 0, 8000, 8000], [3, 3, 2, 1], 0, 0)") as { n: number; best: boolean; pos: number }[];

    assert.deepEqual(rows.map((r) => r.n), [1, 2, 3]);
    assert.deepEqual(rows.map((r) => r.best), [false, true, false]);
    assert.deepEqual(rows.map((r) => r.pos), [3, 2, 1]);
  });

  it("works out LiveRC's figures, and leaves out what too few laps cannot give", async () => {
    const page = await resultsScript();
    const stats = call(page, "lapStats([10000, 9000, 9500, 8000, 9000, 12000])") as Record<string, number | null>;

    assert.equal(stats["fast"], 8000);
    assert.equal(stats["avg"], 9583.333333333334);
    assert.equal(stats["top5"], (8000 + 9000 + 9000 + 9500 + 10000) / 5);
    assert.equal(stats["top10"], null);
    assert.equal(stats["consec2"], 17000);
    assert.equal(stats["consec3"], 26500);
    // Every lap's distance from the 9.583 average, squared, averaged and rooted.
    assert.ok(Math.abs((stats["sd"] as number) - 1238.84) < 0.01);
    assert.ok(Math.abs((stats["consistency"] as number) - (100 - 1238.84 / 9583.333 * 100)) < 0.001);

    const lines = call(page, "lapStatLines(lapStats([10000, 9000, 9500, 8000, 9000, 12000]))") as string[][];
    assert.deepEqual(lines.map((l) => l[0]), ["Fast lap", "Avg. lap", "Avg. top 5", "Top 2 consec.", "Top 3 consec.", "Std. deviation", "Consistency"]);
    assert.deepEqual(lines[0], ["Fast lap", "8.000"]);
  });

  it("has nothing to say about no laps, or laps that are not numbers", async () => {
    const page = await resultsScript();

    assert.equal(call(page, "lapStats(undefined)"), null);
    assert.equal(call(page, "lapStats(['8000', null, -5])"), null);
    assert.deepEqual(call(page, "lapRows('nope', null, 0, 180)"), []);
  });
});

describe("the heat results, as LiveRC lays them out", () => {
  const heat = {
    id: "s1",
    lengthS: 180,
    results: [
      { pos: 1, name: "Alex M", car: "MR-03", qual: 3, laps: 20, elapsedMs: 190513, fastLapMs: 8762, lapsMs: [10113, 8896], posAt: [1, 1] },
      { pos: 2, name: "Doc RC", qual: 5, laps: 18, elapsedMs: 187155, fastLapMs: 9100, lapsMs: [10500, 9100], posAt: [3, 2] },
      { pos: 3, name: "Ryan T", qual: 6, laps: 18, elapsedMs: 187497, fastLapMs: 9050, lapsMs: [10400, 9300], posAt: [2, 3] },
      { pos: 4, name: "Late Car", laps: 17, elapsedMs: 181343, fastLapMs: 9300, status: "DNF" },
    ],
  };

  it("says how far behind the car in front each driver was", async () => {
    const page = await resultsScript();
    const html = call(page, `rows(${JSON.stringify(heat.results)}, 's1')`) as string;
    const cells = (row: number) => html.split('<tbody class="drv">')[row + 1]!;

    assert.match(cells(0), /20\/3:10\.513/);
    assert.match(cells(1), /2 Laps/);
    assert.match(cells(2), /<td class="n">0\.342<\/td>/);
    // A DNF has no gap to show, and says DNF where the laps and time go.
    assert.match(cells(3), /<td class="n">DNF<\/td><td class="n"><\/td>/);
  });

  it("shows the grid slot under Qual and leaves it empty when it was not known", async () => {
    const page = await resultsScript();
    const html = call(page, `rows(${JSON.stringify(heat.results)}, 's1')`) as string;

    assert.match(html.split('<tbody class="drv">')[1]!, /<td class="n">3<\/td>/);
    assert.match(html.split('<tbody class="drv">')[4]!, /<\/span><\/td><td class="n"><\/td>|Late Car<\/span><\/td><td class="n"><\/td>/);
  });

  it("offers View laps only for a driver whose laps were sent", async () => {
    const page = await resultsScript();
    const html = call(page, `rows(${JSON.stringify(heat.results)}, 's1')`) as string;

    assert.equal(html.split("View laps").length - 1, 3);
    // Shut, it carries no lap list; the list is written when it opens.
    assert.doesNotMatch(html, /class="lxp"/);
  });

  it("writes a driver's lap stats and lap list with pace and position", async () => {
    const page = await resultsScript();
    const html = call(page, `lapPanel(${JSON.stringify(heat.results[0])}, 180)`) as string;

    assert.match(html, /Fast lap/);
    assert.match(html, /<th class="n">Pace<\/th><th class="n">Pos<\/th>/);
    assert.match(html, /18\/3:02\.034/);
    assert.match(html, /<tr class="best"><td class="n">2<\/td><td class="n">8\.896<\/td>/);
    assert.match(html, /1st/);
  });

  it("draws the position graph with a line and a key entry per driver", async () => {
    const page = await resultsScript();
    const svg = call(page, `positionGraph(${JSON.stringify(heat.results)})`) as string;

    assert.match(svg, /Lap-by-lap position graph/);
    assert.equal(svg.split("<polyline").length - 1, 3);
    assert.equal(svg.split("<li>").length - 1, 3);
    // Lap 0 is the grid slot: Alex started 3rd, so their line starts on row 3.
    assert.match(svg, /<li><span class="sw"[^>]*><\/span>Alex M<\/li>/);
  });

  it("escapes a driver's name in the graph's key", async () => {
    const page = await resultsScript();
    const evil = structuredClone(heat.results);
    evil[0]!.name = "<img src=x onerror=alert(1)>";
    const svg = call(page, `positionGraph(${JSON.stringify(evil)})`) as string;

    assert.doesNotMatch(svg, /<img/);
    assert.match(svg, /&lt;img src=x/);
  });

  it("has no graph for a result from before lap positions were sent", async () => {
    const page = await resultsScript();
    const old = [{ pos: 1, name: "A", laps: 10, elapsedMs: 100000 }, { pos: 2, name: "B", laps: 9, elapsedMs: 99000 }];

    assert.equal(call(page, `positionGraph(${JSON.stringify(old)})`), "");
    assert.doesNotMatch(call(page, `rows(${JSON.stringify(old)}, 's0')`) as string, /View laps/);
  });

  it("says the race's length when the laptop sent it", async () => {
    const page = await resultsScript();

    assert.equal(call(page, "lengthWords({ lengthS: 180 })"), "3:00 Timed");
    assert.equal(call(page, "lengthWords({ lapLimit: 20 })"), "20 Laps");
    assert.equal(call(page, "lengthWords({})"), "");
  });
});

describe("a driver's own lap stats on their night", () => {
  const CODE = "a1b2c3d4e5f60718293a";

  async function open(races: unknown[]): Promise<FakePage> {
    const full = {
      ok: true,
      night: { driver: "Sam Parker", title: "Club Night", when: "Thursday 22 October 2026", night: "2026-10-22", lines: ["Sam raced."], races },
    };
    const answer: Answerer = async (url, body) => {
      if (url.endsWith("/night")) return { status: 200, body: body["part"] === "live" ? { ok: true } : full };
      return { status: 404 };
    };
    const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, answer, { hash: `#d=${CODE}` });
    page.run(script);
    await page.settle();
    return page;
  }

  it("shows lap stats and every lap with its pace and position", async () => {
    const page = await open([{ sid: "s1", label: "Heat 1", at: "2026-10-22T18:30:00Z", pos: 1, of: 6, laps: 3, bestMs: 8896,
      lapsMs: LIVERC_FIRST_THREE, posAt: [2, 1, 1], lenS: 180 }]);
    const text = page.get("nx").textContent;

    assert.match(text, /Lap stats/);
    assert.match(text, /Fast lap8\.896/);
    assert.match(text, /Lap times \(3\)/);
    assert.match(text, /18\/3:02\.034/);
    assert.match(text, /2nd/);
    assert.equal(page.get("nx").find((n) => n.className === "best").length, 1);
  });

  it("builds it all as text", async () => {
    const page = await open([{ sid: "s1", label: "Heat 1", at: "2026-10-22T18:30:00Z", pos: 1, of: 6, laps: 3, bestMs: 8896,
      lapsMs: LIVERC_FIRST_THREE, posAt: [2, 1, 1], lenS: 180 }]);

    assert.equal(page.get("nx").find((n) => n.className === "nlx" && n.hadMarkup).length, 0);
  });

  it("says nothing more for a race from before lap data was sent", async () => {
    const page = await open([{ sid: "s1", label: "Heat 1", at: "2026-10-22T18:30:00Z", pos: 1, of: 6, laps: 10, bestMs: 8500 }]);

    assert.doesNotMatch(page.get("nx").textContent, /Lap stats|Lap times/);
  });
});
