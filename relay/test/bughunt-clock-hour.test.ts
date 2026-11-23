import assert from "node:assert/strict";
import { createContext, runInContext } from "node:vm";
import { describe, it } from "node:test";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { CLOCK_JS } from "../src/clock.js";

/** The clock script run against an <html> carrying the given data-clock, with the given Intl. */
function hourFor(attr: string | null): unknown {
  const context = createContext({
    Intl,
    document: { documentElement: { getAttribute: (name: string) => (name === "data-clock" ? attr : null) } },
  });
  runInContext(CLOCK_JS, context);
  return runInContext("HC_HOUR", context);
}

// 22 Nov 2026, the owner, from the Voice of the Night video: the Me page said
// "about 6 min (06:08 PM)". A 12-hour time has no leading zero.
describe("the hour on the phone pages' clock times", () => {
  it("has no leading zero on a 12-hour clock and keeps one on a 24-hour clock", () => {
    assert.equal(hourFor("12"), "numeric");
    assert.equal(hourFor("24"), "2-digit");

    const at = new Date(2026, 10, 22, 18, 8);
    assert.equal(at.toLocaleTimeString("en-US", { hour12: true, hour: hourFor("12") as "numeric", minute: "2-digit" }), "6:08 PM");
  });

  it("is what every time the Me, Home and Live pages draw asks for", () => {
    const pages = readFileSync(fileURLToPath(new URL("../src/pages.js", import.meta.url)), "utf8");
    assert.equal(/hour12: HC_H12, hour: '2-digit'/.test(pages), false);
    assert.match(pages, /function hm\(ms\) \{\n  return new Date\(ms\)\.toLocaleTimeString\(\[\], \{ hour12: HC_H12, hour: HC_HOUR/);
  });
});
