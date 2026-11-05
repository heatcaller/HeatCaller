import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { nightPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer, type FakeNode } from "./driverexp-rp1-fakepage.js";

/**
 * MY NIGHT LAID OUT TO BE FOUND (28 Oct 2026, asked for by Asher: "My night
 * needs some more organizing. Plan a way to make it easier to find what you
 * want", then "Add in driver score, probably near the report card or
 * progress"). The night at a glance, the races as a list with one open at a
 * time and its lap stats, lap times and lap chart on tabs, a stat to line
 * the races up by, the driver score beside the report card, and a jump bar.
 */

const CODE = "a1b2c3d4e5f60718293a";
const LAPS1 = [9100, 9000, 9050, 9200, 9010, 9300, 9150];
const LAPS2 = [8900, 8950, 8800, 9400, 8850, 8870, 8900];

function race(n: number, extra: Record<string, unknown> = {}): Record<string, unknown> {
  return { sid: String(n).repeat(32), label: `Heat ${n}`, at: `2026-10-22T1${n}:00:00Z`, pos: n, of: 6, laps: 7, ...extra };
}

const NIGHT = {
  driver: "Sam Parker", title: "Club Night", when: "Thursday 22 October 2026", night: "2026-10-22",
  lines: ["Sam raced twice."],
  coach: ["Best lap tonight: 8.800s, from 14 laps.", "Driver rating tonight: 96.0 out of 150 on average over 2 races - best 104.5, in Heat 2.", "Consistency: fine."],
  races: [
    race(1, { bestMs: 9000, lapsMs: LAPS1, rating: 87.5 }),
    race(2, { bestMs: 8800, lapsMs: LAPS2, rating: 104.5, pen: ["+5 s - jump start"] }),
  ],
};

async function open(night: unknown): Promise<FakePage> {
  const answer: Answerer = async (url, body) => {
    if (url.endsWith("/night")) return { status: 200, body: body["part"] === "live" ? { ok: true } : { ok: true, night } };
    return { status: 404 };
  };
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, answer, { hash: `#d=${CODE}` });
  page.run(script);
  await page.settle();
  return page;
}

function rows(page: FakePage): FakeNode[] {
  return page.get("nx").find((n) => n.className.startsWith("rrow"));
}

function byClass(root: FakeNode, cls: string): FakeNode[] {
  return root.find((n) => n.className === cls);
}

describe("My Night: the night at a glance", () => {
  it("shows the best finish and best lap with the race each came in, the races and the laps", async () => {
    const page = await open(NIGHT);
    const text = page.get("ng").textContent;

    assert.match(text, /Best finish1st of 6Heat 1/);
    assert.match(text, /Best lap8\.800Heat 2/);
    assert.match(text, /Races2/);
    assert.match(text, /Laps14/);
  });

  it("sits above the night's facts", async () => {
    const page = await open(NIGHT);
    const html = page.get("nt").html;

    assert.ok(html.indexOf('id="ng"') < html.indexOf("Sam raced twice."));
  });
});

describe("My Night: the races, one open at a time", () => {
  it("lists every race as a row and opens the latest by itself", async () => {
    const page = await open(NIGHT);
    const list = rows(page);

    assert.equal(list.length, 2);
    assert.equal(list[0]!.className, "rrow");
    assert.equal(list[1]!.className, "rrow open");
    assert.equal(byClass(page.get("nx"), "rc").length, 1);
    assert.match(byClass(page.get("nx"), "rc")[0]!.textContent, /\+5 s - jump start/);
  });

  it("marks a race with a penalty on its row", async () => {
    const page = await open(NIGHT);

    assert.match(rows(page)[1]!.textContent, /Penalty/);
    assert.doesNotMatch(rows(page)[0]!.textContent, /Penalty/);
  });

  it("opens another race in its place, and closes it again", async () => {
    const page = await open(NIGHT);

    byClass(rows(page)[0]!, "rbtn")[0]!.click();
    assert.deepEqual(rows(page).map((r) => r.className), ["rrow open", "rrow"]);
    assert.equal(byClass(rows(page)[0]!, "rbtn")[0]!.getAttribute("aria-expanded"), "true");

    byClass(rows(page)[0]!, "rbtn")[0]!.click();
    assert.deepEqual(rows(page).map((r) => r.className), ["rrow", "rrow"]);
    assert.equal(byClass(page.get("nx"), "rc").length, 0);
  });

  it("keeps the race they opened through a redraw", async () => {
    const page = await open(NIGHT);

    byClass(rows(page)[0]!, "rbtn")[0]!.click();
    page.eval("drawExtras()");

    assert.deepEqual(rows(page).map((r) => r.className), ["rrow open", "rrow"]);
  });

  it("puts lap stats, lap times and lap chart on tabs, stats first", async () => {
    const page = await open(NIGHT);
    const card = byClass(page.get("nx"), "rc")[0]!;
    const tabs = card.find((n) => n.className.startsWith("rtab") && n.tagName === "BUTTON");

    assert.deepEqual(tabs.map((t) => t.textContent), ["Lap stats", "Lap times (7)", "Lap chart"]);
    const panels = card.find((n) => n.getAttribute("role") === "tabpanel");
    assert.deepEqual(panels.map((p) => p.hidden), [false, true, true]);

    tabs[2]!.click();
    assert.deepEqual(panels.map((p) => p.hidden), [true, true, false]);
    assert.equal(tabs[2]!.getAttribute("aria-selected"), "true");
  });

  it("shows four stats first and the rest behind More stats", async () => {
    const page = await open(NIGHT);
    const stats = byClass(page.get("nx"), "nlx")[0]!;
    const first = stats.children[0]!.find((n) => n.tagName === "DT").map((n) => n.textContent);

    assert.deepEqual(first, ["Fast lap", "Avg. lap", "Top 3 consec.", "Consistency"]);
    const more = stats.find((n) => n.tagName === "DETAILS")[0]!;
    assert.match(more.textContent, /^More stats/);
    assert.match(more.textContent, /Avg\. top 5/);
    assert.match(more.textContent, /Std\. deviation/);
  });

  it("shows each race's best lap, the night's best marked, and no stat picker", async () => {
    const page = await open(NIGHT);
    const top = page.get("nx").find((n) => n.className === "rval top").map((n) => n.textContent);

    assert.deepEqual(top, ["8.800"], "best lap, quicker is better");
    // The picker went on 4 Nov 2026: Lap stats already has all four.
    assert.equal(page.get("nx").find((n) => n.className === "pick").length, 0);
    assert.equal(page.get("nx").find((n) => n.className.startsWith("pk")).length, 0);
  });

  it("opens a race from before lap data with its facts and no tabs", async () => {
    const page = await open({ ...NIGHT, races: [race(1, { bestMs: 9000 })] });
    const card = byClass(page.get("nx"), "rc")[0]!;

    assert.match(card.textContent, /1st of 6 · 7 laps · best 9\.000/);
    assert.equal(card.find((n) => n.getAttribute("role") === "tablist").length, 0);
  });
});

describe("My Night: the driver score", () => {
  it("shows the average, the best race and a bar per race, under the report card", async () => {
    const page = await open(NIGHT);
    const nx = page.get("nx");
    const score = page.get("n-score");

    assert.match(score.textContent, /^Driver score96\.0 out of 150/);
    assert.match(score.textContent, /Average of 2 races\. Best 104\.5, in Heat 2\./);
    const fills = score.find((n) => n.className === "sc-fill");
    assert.deepEqual(fills.map((f) => f.style["width"]), ["58%", "70%"]);
    assert.ok(nx.textContent.indexOf("Your report card") < nx.textContent.indexOf("Driver score"));
  });

  it("takes the report card's driver rating line out, as it says the same", async () => {
    const page = await open(NIGHT);
    const card = page.get("n-how").textContent;

    assert.doesNotMatch(card, /Driver rating tonight/);
    assert.match(card, /Best lap tonight/);
    assert.match(card, /Consistency: fine/);
  });

  it("is one number for one race", async () => {
    const page = await open({ ...NIGHT, races: [NIGHT.races[1]] });
    const score = page.get("n-score");

    assert.match(score.textContent, /104\.5 out of 150/);
    assert.equal(score.find((n) => n.className === "scbars").length, 0);
  });

  it("says there is no score yet, and keeps the report card's line, for a night from a laptop that sends no rating", async () => {
    const page = await open({ ...NIGHT, races: [race(1, { bestMs: 9000 })] });

    assert.match(page.get("n-score").textContent, /^Driver scoreNo driver score yet./);
    assert.match(page.get("n-how").textContent, /Driver rating tonight/);
  });

  it("ignores a rating that is not one", async () => {
    const page = await open({ ...NIGHT, races: [race(1, { rating: "high" }), race(2, { rating: 900 })] });

    assert.match(page.get("n-score").textContent, /No driver score yet/);
    assert.doesNotMatch(page.get("n-score").textContent, /out of 150/);
  });
});

describe("My Night: demo races and practice (3 Nov 2026)", () => {
  // A demo race the driver ran, then a practice run, after their two heats.
  const RUNS = {
    ...NIGHT,
    races: [
      ...NIGHT.races,
      { ...race(3, { bestMs: 8500, lapsMs: LAPS2 }), label: "Demo Heat", kind: "demo" },
      { ...race(4, { bestMs: 8400, lapsMs: LAPS1 }), label: "Practice", pos: 0, kind: "practice" },
    ],
  };

  function tick(page: FakePage): FakeNode {
    return page.get("nx").find((n) => n.tagName === "INPUT" && n.type === "checkbox")[0]!;
  }

  it("lists them with a chip, and the latest opens with its lap stats", async () => {
    const page = await open(RUNS);
    const list = rows(page);

    assert.equal(list.length, 4);
    assert.match(list[2]!.textContent, /Demo Heat.*Demo/);
    assert.match(list[3]!.textContent, /Practice.*Practice/);
    assert.doesNotMatch(list[0]!.textContent, /Demo|Practice/);
    assert.equal(list[3]!.className, "rrow open");
    assert.match(byClass(page.get("nx"), "rc")[0]!.textContent, /Lap stats/);
  });

  it("leaves them out of the night at a glance and the driver score", async () => {
    const page = await open(RUNS);
    const glance = page.get("ng").textContent;

    assert.match(glance, /Best lap8\.800Heat 2/);
    assert.match(glance, /Races2/);
    assert.match(page.get("n-score").textContent, /Average of 2 races/);
  });

  it("hides them all with one tick, and shows them again", async () => {
    const page = await open(RUNS);
    const box = tick(page);

    assert.equal(box.checked, false);
    box.checked = true;
    box.fire("change");
    assert.equal(rows(page).length, 2);
    assert.equal(rows(page)[1]!.className, "rrow open", "the latest real race opens");
    assert.equal(tick(page).checked, true);

    tick(page).checked = false;
    tick(page).fire("change");
    assert.equal(rows(page).length, 4);
  });

  it("keeps the tick box, and says why the list is empty, on a night of only demo and practice", async () => {
    const page = await open({ ...NIGHT, races: RUNS.races.slice(2) });
    const box = tick(page);

    box.checked = true;
    box.fire("change");
    assert.equal(rows(page).length, 0);
    assert.ok(tick(page), "still there to untick");
    assert.match(page.get("nx").textContent, /Only demo races and practice so far/);
  });

  it("has no tick box on a night of races only", async () => {
    const page = await open(NIGHT);

    assert.equal(page.get("nx").find((n) => n.tagName === "INPUT").length, 0);
  });
});

describe("My Night: the jump bar", () => {
  it("goes to each part that is there, as buttons rather than links", async () => {
    const page = await open({ ...NIGHT, progress: { layout: "Club Oval", nights: [{ night: "2026-10-15", bestMs: 9000 }, { night: "2026-10-22", bestMs: 8800 }] } });
    const bar = page.get("nj");
    const names = bar.find((n) => n.tagName === "BUTTON").map((n) => n.textContent);

    assert.deepEqual(names, ["Races", "Report card", "Driver score", "Progress", "Rearrange"]);
    assert.equal(bar.find((n) => n.tagName === "A").length, 0, "the address keeps their code");
  });

  it("goes to each car on a night of two", async () => {
    const page = await open({
      ...NIGHT,
      races: [NIGHT.races[0], { ...NIGHT.races[1], car: "c2" }],
      cars: [{ id: "c1", label: "Main car", name: "B6.4" }, { id: "c2", label: "Second car" }],
    });
    const names = page.get("nj").find((n) => n.tagName === "BUTTON").map((n) => n.textContent);

    assert.deepEqual(names, ["Main car · B6.4", "Second car", "Rearrange"]);
  });
});
