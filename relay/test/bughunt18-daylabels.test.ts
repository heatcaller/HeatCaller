import assert from "node:assert/strict";
import { after, before, describe, it } from "node:test";
import { createContext, runInContext } from "node:vm";
import { CLOCK_JS } from "../src/clock.js";
import { nightPage, resultsPage } from "../src/pages.js";

/**
 * BUG HUNT 18, #23: CLOCK_JS draws every date in the club's time zone, but
 * the results archive (dayDate) and the driver's night list (shortDay) built
 * a calendar day as the phone's midnight. A phone ahead of the club - a
 * parent in London, a club in New York - then labelled 16 Sep "Tuesday 15
 * September".
 */
function fn(html: string, name: string): string {
  const match = new RegExp(`function ${name}\\([^)]*\\) \\{[\\s\\S]*?\\n\\}`).exec(html);
  assert.ok(match, `the page has ${name}()`);
  return match[0];
}

/** The page's helpers run under CLOCK_JS on an <html> saying the club is in `tz`. */
function inClubZone(tz: string, source: string): { [name: string]: (iso: string) => string } & { dayLabel: (iso: string) => string; daySay: (iso: string) => string; shortDay: (iso: string) => string } {
  const context = createContext({
    document: { documentElement: { getAttribute: (name: string) => (name === "data-tz" ? tz : null) } },
  });
  runInContext(CLOCK_JS + "\n" + source, context);
  return context as never;
}

describe("a race night's day label when the phone is ahead of the club", () => {
  let was: string | undefined;

  before(() => {
    was = process.env.TZ;
    process.env.TZ = "Europe/London";
  });

  after(() => {
    if (was === undefined) delete process.env.TZ;
    else process.env.TZ = was;
  });

  it("files the results archive under the day the club raced", () => {
    const html = resultsPage("acme-rc", "Acme RC");
    const page = inClubZone("America/New_York", ["dayDate", "dayLabel", "daySay"].map((n) => fn(html, n)).join("\n"));

    const label = page.dayLabel("2026-09-16");
    assert.match(label, /Wednesday/);
    assert.match(label, /16/);
    assert.match(page.daySay("2026-09-16"), /Wednesday/);
  });

  it("shows the driver's night list on the right day", () => {
    const html = nightPage("acme-rc", "Acme RC");
    const page = inClubZone("America/Chicago", fn(html, "shortDay"));

    assert.match(page.shortDay("2026-10-22"), /22/);
  });
});
