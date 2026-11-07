import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { appShellPage, homePage, managePage, schedulePage } from "../src/pages.js";
import { DEFAULT_CONTEXT } from "../src/pages.js";

/**
 * NO PINCH OR DOUBLE-TAP ZOOM (6 Nov 2026, Asher: "don't allow for pinch
 * and zoom on relay"): every kind of relay page carries all three parts.
 */
describe("the relay's pages cannot be pinched or double-tapped into zoom", () => {
  const pages: [string, string][] = [
    ["home", homePage("acme-rc", "Acme RC", DEFAULT_CONTEXT)],
    ["schedule", schedulePage("acme-rc", "Acme RC", DEFAULT_CONTEXT)],
    ["manage", managePage("acme-rc", "Acme RC", DEFAULT_CONTEXT)],
    ["app shell", appShellPage("acme-rc", "Acme RC", DEFAULT_CONTEXT)],
  ];

  for (const [name, html] of pages) {
    it(`${name}: viewport, touch-action and Safari's gesture events`, () => {
      assert.match(html, /<meta name="viewport" content="[^"]*maximum-scale=1, user-scalable=no/);
      assert.equal((html.match(/name="viewport"/g) ?? []).length, 1, "one viewport tag");
      assert.match(html, /html \{ touch-action: pan-x pan-y; \}/);
      assert.match(html, /addEventListener\('gesturestart', \(e\) => e\.preventDefault\(\)\)/);
    });
  }
});
