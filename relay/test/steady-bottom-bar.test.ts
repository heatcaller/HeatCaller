import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { homePage, livePage, nightPage, resultsPage, schedulePage } from "../src/pages.js";

/**
 * A BAR THAT STAYS PUT (31 Oct 2026, Asher on iPhone Chrome: "the buttons
 * at the bottom of the relay jump up and down when selecting them"). An
 * iPhone browser tucks its toolbars away while the window scrolls and
 * brings them back on a tap near the bottom of the screen, lifting a bar
 * fixed there. On a phone the club's pages scroll inside the body instead,
 * so the window never scrolls and the toolbars never move.
 */

/** The phone-width block of the club pages' stylesheet. */
function phoneRules(html: string): string {
  const at = html.indexOf("@media (max-width: 640px) {");
  assert.ok(at >= 0, "the page has its phone-width rules");
  return html.slice(at, html.indexOf("\n}", at));
}

describe("the club pages' bottom bar on a phone", () => {
  const pages: [string, string][] = [
    ["home", homePage("acme-rc", "Acme RC")],
    ["live", livePage("acme-rc", "Acme RC")],
    ["schedule", schedulePage("acme-rc", "Acme RC")],
    ["results", resultsPage("acme-rc", "Acme RC")],
    ["a driver's night", nightPage("acme-rc", "Acme RC")],
  ];

  for (const [name, html] of pages) {
    it(`gives the browser nothing of its own to scroll on ${name}`, () => {
      const phone = phoneRules(html);
      assert.match(phone, /html \{ height: 100%; overflow: hidden; overscroll-behavior: none; \}/);
      assert.match(phone, /body \{ height: 100%; overflow-y: auto;[^}]*padding-bottom: calc\(76px \+ env\(safe-area-inset-bottom\)\); \}/);
      assert.match(phone, /nav\.tabs \{ position: fixed; left: 0; right: 0; bottom: 0;/);
    });
  }

  it("leaves a laptop's page scrolling the window as before", () => {
    const html = homePage("acme-rc", "Acme RC");
    const outside = html.replace(phoneRules(html), "");
    assert.doesNotMatch(outside, /html \{[^}]*overflow: hidden/);
    assert.doesNotMatch(outside, /\nbody \{[^}]*overflow/);
  });

  it("keeps nothing in the pages' scripts that reads or moves the window's own scroll", () => {
    for (const [, html] of pages) {
      assert.doesNotMatch(html, /window\.scroll(To|By|Y)\b|pageYOffset|scrollingElement/);
    }
  });

  it("keeps a home-screen icon's bar flush to the bottom without letting the window scroll", () => {
    // 1 Nov 2026, Asher from his iPhone's home screen: "It now has a chin under the tabs",
    // then "band is gone but the jumping is back".
    for (const [, html] of pages) {
      const at = html.indexOf("@media (max-width: 640px) and (display-mode: standalone), (max-width: 640px) and (display-mode: fullscreen) {");
      assert.ok(at > html.indexOf("@media (max-width: 640px) {"), "after the phone rules, so it wins");
      const block = html.slice(at, html.indexOf("\n}", at));
      assert.match(block, /html \{ height: 100vh; height: 100lvh; \}/);
      assert.match(block, /nav\.tabs \{ bottom: auto; top: 100vh; top: 100lvh; transform: translateY\(-100%\); \}/);
      // The window never scrolls here either: no rule in it undoes the phone rules' overflow.
      assert.doesNotMatch(block, /overflow/);
      assert.doesNotMatch(block, /height: auto/);
    }
  });
});
