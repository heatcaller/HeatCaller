import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { livePage } from "../src/pages.js";

/**
 * THE FLAG CARD AS A LAP PROGRESS TRACK (4 Nov 2026, Asher picked
 * direction 5 of ten: "build 5 lap progress"). The race's length is the
 * leader's lap plus what the countdown says is left, so the countdown has
 * to be read right as the page ships it - a regex losing its backslashes
 * inside the page's template string once left every race without a bar.
 */

/** One function from the live page's script, as the phone gets it. */
function pageFunction(name: string): (...args: unknown[]) => unknown {
  const html = livePage("acme-rc", "Acme RC");
  const match = new RegExp(`function ${name}\\([^)]*\\) \\{[\\s\\S]*?\\n\\}`).exec(html);
  assert.ok(match, `the page has ${name}()`);
  return new Function(`${match[0]}; return ${name};`)() as (...args: unknown[]) => unknown;
}

describe("the Live flag card's lap progress", () => {
  it("reads the laps left from the countdown", () => {
    const toGo = pageFunction("lbToGo");
    assert.equal(toGo("5 LAPS TO GO"), 5);
    assert.equal(toGo("1 LAP TO GO"), 1);
    assert.equal(toGo("LAST LAP"), 1);
    assert.equal(toGo("12 laps to go"), 12);
  });

  it("has no laps left for a timed race's countdown", () => {
    const toGo = pageFunction("lbToGo");
    assert.equal(toGo("04:32 REMAINING"), null);
    assert.equal(toGo(""), null);
  });

  it("draws a segment a lap with the lap being run lit", () => {
    const bar = pageFunction("lbBar");
    const html = String(bar(7, 12, false));
    assert.equal((html.match(/<i class="/g) ?? []).length, 12);
    assert.equal((html.match(/<i class="on">/g) ?? []).length, 6);
    assert.equal((html.match(/<i class="now">/g) ?? []).length, 1);
    assert.match(html, /<span class="lp-fin"><\/span>/);
  });

  it("draws one track past forty laps, and nothing without a length", () => {
    const bar = pageFunction("lbBar");
    assert.match(String(bar(30, 60, false)), /<span class="lp-track"><b style="width:50%"><\/b><\/span>/);
    assert.equal(bar(5, 0, false), "");
  });
});
