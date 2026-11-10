import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { nightPage } from "../src/pages.js";

/**
 * BUG HUNT 15, B31: a demo or practice night's field (races[].field, from
 * DriverNightExtras.FieldOf) has its start lap taken off like the driver's
 * own laps. A laptop that sent those rows without startMs left every rival
 * drawn from lap 1 while the driver's own line started at lap 2.
 */
function meLines(): (r: unknown, field: unknown) => Array<{ me: boolean; first: number }> {
  const html = nightPage("acme-rc", "Acme RC");
  const parts = ["lxLaps", "lxHasStart", "num", "sameLaps", "meLines"].map((name) => {
    const match = new RegExp(`function ${name}\\([^)]*\\) \\{[\\s\\S]*?\\n\\}`).exec(html);
    assert.ok(match, `the page has ${name}()`);
    return match[0];
  });
  return new Function(`const ME_COLOURS = ['a', 'b', 'c']; ${parts.join("\n")}; return meLines;`)() as never;
}

describe("the driver's lap chart lines on a demo night", () => {
  const own = { pos: 1, startMs: 1400, lapsMs: [9000, 8800, 8900], posAt: [1, 1, 1] };
  const rival = { pos: 2, name: "Robo Racer", lapsMs: [9100, 8700, 9000], posAt: [2, 2, 2] };

  it("starts a rival without startMs at the driver's own first lap", () => {
    const r = { ...own, field: [{ pos: 1, name: "Sam", lapsMs: own.lapsMs }, rival] };
    const lines = meLines()(r, r.field);
    assert.equal(lines.length, 2);
    assert.deepEqual(lines.map((l) => l.first), [2, 2]);
  });

  it("uses a rival's own startMs when it is sent", () => {
    const r = { ...own, field: [{ pos: 1, name: "Sam", lapsMs: own.lapsMs, startMs: 1400 }, { ...rival, startMs: 1500 }] };
    assert.deepEqual(meLines()(r, r.field).map((l) => l.first), [2, 2]);
  });

  it("leaves a public result's row without startMs at lap 1", () => {
    const field = [{ pos: 1, name: "Sam", lapsMs: own.lapsMs }, rival];
    assert.deepEqual(meLines()(own, field).map((l) => l.first), [2, 1]);
  });
});
