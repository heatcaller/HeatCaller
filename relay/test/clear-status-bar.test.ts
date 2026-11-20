import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { homePage, managePage, myNightPage, resultsPage } from "../src/pages.js";
import { withSharedInline } from "./pagescript.js";

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
    ["home", withSharedInline(homePage("acme-rc", "Acme RC"))],
    ["results", withSharedInline(resultsPage("acme-rc", "Acme RC"))],
    ["my night", withSharedInline(myNightPage("acme-rc", "Acme RC"))],
  ];

  for (const [name, html] of club) {
    it(`starts ${name} below the status bar it asks to be drawn under`, () => {
      assert.match(html, /viewport-fit=cover/);
      // By name since 1 Nov 2026, so a page under the home-screen icon's
      // one bar can be handed the insets of the screen it is really on.
      assert.match(html, /--sa-t: env\(safe-area-inset-top, 0px\);/);
      assert.match(bodyRule(html), /padding:\s*var\(--sa-t\)/);
    });
  }

  it("starts the manager below the status bar it asks to be drawn under", () => {
    const html = withSharedInline(managePage("acme-rc", "Acme RC"));
    assert.match(html, /viewport-fit=cover/);
    assert.match(bodyRule(html), /padding:\s*env\(safe-area-inset-top/);
  });

  it("keeps the club pages' side gutter of at least 16px", () => {
    const html = withSharedInline(homePage("acme-rc", "Acme RC"));
    assert.match(bodyRule(html), /max\(16px, var\(--sa-r\)\) 64px max\(16px, var\(--sa-l\)\)/);
    assert.match(html, /--sa-r: env\(safe-area-inset-right, 0px\);/);
    assert.match(html, /--sa-l: env\(safe-area-inset-left, 0px\);/);
  });

  it("stops My Night's jump bar and the Manager's tabs below the status bar, not under it", () => {
    assert.match(withSharedInline(myNightPage("acme-rc", "Acme RC")), /#nj \{ position: sticky; top: calc\(var\(--stick-t\) \+ var\(--owe-h, 0px\)\)/);
    assert.match(withSharedInline(managePage("acme-rc", "Acme RC")), /position: sticky; top: env\(safe-area-inset-top, 0px\); z-index: 2;/);
    for (const [, html] of club) assert.doesNotMatch(html, /position: sticky; top: 0[;\s]/);
  });
});
