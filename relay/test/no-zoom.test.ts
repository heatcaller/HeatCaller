import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { appShellPage, homePage, managePage, schedulePage } from "../src/pages.js";
import { DEFAULT_CONTEXT } from "../src/pages.js";
import { withSharedInline } from "./pagescript.js";

/**
 * PINCH-ZOOM IS BACK (14 Nov 2026, driver phone plan item 6, Asher: "you
 * can add pinch to zoom and high contrast"). From 6 Nov the pages turned
 * zoom off ("don't allow for pinch and zoom on relay"); now every kind of
 * relay page allows it, and fixes what the no-zoom rule was for: no text box
 * under 16px to jump-zoom into, and no double-tap zoom on buttons. (The Fit to
 * screen button came up on its own and went on 15 Nov 2026.)
 */
describe("the relay's pages can be pinched to zoom", () => {
  const pages: [string, string][] = [
    ["home", withSharedInline(homePage("acme-rc", "Acme RC", DEFAULT_CONTEXT))],
    ["schedule", withSharedInline(schedulePage("acme-rc", "Acme RC", DEFAULT_CONTEXT))],
    ["manage", withSharedInline(managePage("acme-rc", "Acme RC", DEFAULT_CONTEXT))],
    ["app shell", withSharedInline(appShellPage("acme-rc", "Acme RC", DEFAULT_CONTEXT))],
  ];

  for (const [name, html] of pages) {
    it(`${name}: a viewport that allows zoom, and none of the old blocks`, () => {
      assert.match(html, /<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">/);
      assert.equal((html.match(/name="viewport"/g) ?? []).length, 1, "one viewport tag");
      assert.doesNotMatch(html, /user-scalable=no|maximum-scale=1"/);
      assert.doesNotMatch(html, /touch-action: pan-x pan-y/);
      assert.doesNotMatch(html, /addEventListener\('gesturestart', \(e\) => e\.preventDefault\(\)\)/);
      assert.match(html, /touch-action: manipulation/, "no double-tap zoom on buttons");
      assert.match(html, /function hcBigEnough\(el\)/, "no box under 16px to jump-zoom into");
      assert.doesNotMatch(html, /hcfit|textContent = 'Fit to screen'/, "no Fit to screen button (15 Nov 2026)");
    });
  }
});
