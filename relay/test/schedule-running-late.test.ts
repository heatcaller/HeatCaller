import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { schedulePage } from "../src/pages.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";

/**
 * HOW FAR BEHIND THE NIGHT IS RUNNING (4 Nov 2026, from the relay roadmap):
 * the planned time each line was typed with, against when it started.
 */
describe("Schedule: running late", () => {
  async function page(): Promise<FakePage> {
    const { html, script } = pageParts(schedulePage("acme-rc", "Acme RC"));
    const p = new FakePage(html, async () => ({ status: 404 }));
    p.run(script);
    await p.settle();
    return p;
  }

  // A start time as an ISO string, at this phone's local clock on a fixed day.
  const at = (h: number, m: number) => new Date(2026, 10, 4, h, m, 0, 0).toISOString();

  it("reads the club's typed times, 24-hour or with am and pm", async () => {
    const p = await page();
    const read = (t: string) => p.eval(`(() => { const v = clockOn(${JSON.stringify(t)}, ${JSON.stringify(at(12, 0))}); return Number.isFinite(v) ? new Date(v).getHours() * 60 + new Date(v).getMinutes() : -1; })()`);
    assert.equal(read("19:05"), 19 * 60 + 5);
    assert.equal(read("7:05 PM"), 19 * 60 + 5);
    assert.equal(read("12:30am"), 30);
    assert.equal(read("after lunch"), -1);
  });

  it("says how late, by the latest line that started", async () => {
    const p = await page();
    const late = (lines: unknown[]) => p.eval(`lateWords(runningLate(${JSON.stringify(lines)}))`);
    assert.equal(late([{ time: "19:00", startedUtc: at(19, 4) }, { time: "19:10", startedUtc: at(19, 18) }, { time: "19:20" }]), "Running 8 min behind");
    assert.equal(late([{ time: "19:00", startedUtc: at(19, 1) }]), "Running on time");
    assert.equal(late([{ time: "19:00", startedUtc: at(18, 55) }]), "Running 5 min ahead");
    assert.equal(late([{ time: "19:00" }]), "", "nothing started yet");
  });
});

/**
 * QUALIFYING AND THE MAINS (4 Nov 2026, from the relay roadmap): scores as
 * LiveRC writes them, and the score to beat for the main above.
 */
describe("Schedule: qualifying and the mains", () => {
  const cls = { name: "Stock", method: "TotalLaps", consecutive: 3, mainSize: 2 };
  const rows = [
    { pos: 1, name: "Sam P.", main: "A", laps: 18, timeMs: 302103 },
    { pos: 2, name: "Jordan M.", main: "A", laps: 18, timeMs: 305500 },
    { pos: 3, name: "Asher K.", main: "B", laps: 17, timeMs: 299000 },
    { pos: 4, name: "Riley T.", main: "B", laps: 16, timeMs: 301000 },
    { pos: 5, name: "Casey L.", main: "C", laps: 15, timeMs: 300000 },
  ];

  async function page(): Promise<FakePage> {
    const { html, script } = pageParts(schedulePage("acme-rc", "Acme RC"));
    const p = new FakePage(html, async () => ({ status: 404 }));
    p.run(script);
    await p.settle();
    return p;
  }

  it("writes the score as laps and time, or the laps in a row", async () => {
    const p = await page();
    assert.equal(p.eval(`qualScore(${JSON.stringify(cls)}, ${JSON.stringify(rows[0])})`), "18/5:02.103");
    assert.equal(p.eval(`qualScore(${JSON.stringify({ ...cls, method: "BestConsecutiveLaps" })}, ${JSON.stringify({ consecutiveMs: 27512 })})`), "3 in 27.512");
  });

  it("says what it takes to make the main above, and nothing in the A", async () => {
    const p = await page();
    const up = (r: unknown) => p.eval(`qualToMoveUp(${JSON.stringify(cls)}, ${JSON.stringify(rows)}, ${JSON.stringify(r)})`);
    assert.equal(up(rows[0]), "");
    assert.equal(up(rows[2]), "Beat 18/5:05.500 to make the A main");
    assert.equal(up(rows[4]), "Beat 16/5:01.000 to make the B main");
  });
});
