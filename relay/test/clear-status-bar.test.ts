import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { homePage, managePage, myNightPage, resultsPage } from "../src/pages.js";

/**
 * CLEAR OF THE CLOCK (30 Oct 2026, Asher: "all the pages on the relay load
 * scrolled down a bit. when you try to scroll up it just bounces back. hides
 * the title of page"). The pages ask for viewport-fit=cover, which lets a
 * phone draw them under its status bar, so the page itself has to start
 * below it - and so do the bars that stick to the top as you scroll.
 */

/** The page's own body rule, the first in its stylesheet. */
function bodyRule(html: string): string {
  return /\nbody \{([^}]*)\}/.exec(html)?.[1] ?? "";
}

describe("relay pages clear a phone's status bar", () => {
  const club: [string, string][] = [
    ["home", homePage("acme-rc", "Acme RC")],
    ["results", resultsPage("acme-rc", "Acme RC")],
    ["my night", myNightPage("acme-rc", "Acme RC")],
  ];

  for (const [name, html] of [...club, ["manager", managePage("acme-rc", "Acme RC")] as [string, string]]) {
    it(`starts ${name} below the status bar it asks to be drawn under`, () => {
      assert.match(html, /viewport-fit=cover/);
      assert.match(bodyRule(html), /padding:\s*env\(safe-area-inset-top/);
    });
  }

  it("keeps the club pages' side gutter of at least 16px", () => {
    assert.match(bodyRule(homePage("acme-rc", "Acme RC")), /max\(16px, env\(safe-area-inset-right[^)]*\)\) 64px max\(16px, env\(safe-area-inset-left/);
  });

  it("stops My Night's jump bar and the Manager's tabs below the status bar, not under it", () => {
    assert.match(myNightPage("acme-rc", "Acme RC"), /#nj \{ position: sticky; top: env\(safe-area-inset-top, 0px\)/);
    assert.match(managePage("acme-rc", "Acme RC"), /position: sticky; top: env\(safe-area-inset-top, 0px\); z-index: 2;/);
    for (const [, html] of club) assert.doesNotMatch(html, /position: sticky; top: 0[;\s]/);
  });
});
