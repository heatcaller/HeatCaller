import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { appShellPage, homePage, nightPage } from "../src/pages.js";

/**
 * A SWIPE ALONG THE BOTTOM BAR (5 Nov 2026, Asher: "add ability to swipe
 * left and right on the bottom tabs to change screens"): on every club page
 * and on the home-screen icon's bar, a sideways swipe on the bar is a tap on
 * the next tab along; Me's own space swipe leaves the bar alone.
 */

const scriptOf = (page: string): string => {
  const m = /<script[^>]*>([\s\S]*)<\/script>/.exec(page);
  assert.ok(m, "the page has a script");
  return m[1] as string;
};

describe("swiping the bottom tabs", () => {
  for (const [name, page] of [
    ["a club page", homePage("acme-rc", "Acme RC")],
    ["the home-screen icon's bar", appShellPage("acme-rc", "Acme RC")],
  ] as const) {
    it(`listens for a swipe on the bar on ${name}, and the script parses`, () => {
      const code = scriptOf(page);
      assert.doesNotThrow(() => new Function(code));
      assert.match(code, /A SWIPE ALONG THE BAR/);
      assert.match(code, /tabs\.addEventListener\('touchend'/);
      // The bar follows the finger: a swipe right goes to the tab on the
      // right ("the swiping is backwards"), and no wrapping past either end.
      assert.match(code, /all\[at \+ \(dx > 0 \? 1 : -1\)\]/);
      assert.match(code, /if \(!to\) return;/);
    });
  }

  it("keeps Me's sideways space swipe off the bar", () => {
    assert.match(nightPage("acme-rc", "Acme RC"), /'input, textarea, select, nav\.tabs, /);
  });
});
