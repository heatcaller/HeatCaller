import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { homePage, livePage, nightPage, resultsPage, schedulePage } from "../src/pages.js";
import { withSharedInline } from "./pagescript.js";

/**
 * A STEADY SWITCH BETWEEN PAGES (1 Nov 2026, Asher on his iPhone's home-screen
 * icon: "it flickers when hitting each tab and the content above it flashes
 * into place"). Every tab is its own page; the club pages ask the browser to
 * keep the old page up until the new one is ready and to carry the bar across.
 */
describe("switching tabs on the club pages", () => {
  const pages: [string, string][] = [
    ["home", withSharedInline(homePage("acme-rc", "Acme RC"))],
    ["live", withSharedInline(livePage("acme-rc", "Acme RC"))],
    ["schedule", withSharedInline(schedulePage("acme-rc", "Acme RC"))],
    ["results", withSharedInline(resultsPage("acme-rc", "Acme RC"))],
    ["a driver's night", withSharedInline(nightPage("acme-rc", "Acme RC"))],
  ];

  for (const [name, html] of pages) {
    it(`keeps the page up and the bar still between pages, from ${name}`, () => {
      assert.match(html, /@view-transition \{ navigation: auto; \}/);
      assert.match(html, /nav\.tabs \{ view-transition-name: hc-tabs; \}/);
      assert.match(html, /::view-transition-group\(hc-tabs\), ::view-transition-old\(hc-tabs\), ::view-transition-new\(hc-tabs\) \{ animation: none; \}/);
      // A name used twice on one page cancels the whole switch, so the bar is the only one.
      assert.equal(html.match(/<nav class="tabs"/g)?.length, 1);
      assert.equal(html.match(/view-transition-name:/g)?.length, 1);
    });
  }

  it("swaps the page at once, with no fade into a page still loading", () => {
    // 1 Nov 2026, Asher: "i see the fade I think but it's not really helping".
    const html = withSharedInline(homePage("acme-rc", "Acme RC"));
    assert.match(html, /::view-transition-new\(root\) \{ animation: none; \}/);
    assert.doesNotMatch(html, /hc-page-in/);
  });
});
