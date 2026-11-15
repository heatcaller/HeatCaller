import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { homePage, myNightPage } from "../src/pages.js";

describe("Pages clear the phone's faded top edge (15 Nov 2026)", () => {
  for (const [name, html] of [["Home", homePage("c", "Club")], ["Me", myNightPage("c", "Club")]] as const) {
    it(`${name}: on a phone, keeps the top at least 20px down and covers it with the page's colour`, () => {
      const at = html.indexOf("@media (hover: none) and (pointer: coarse) and (max-width: 640px) {\n  :root { --sa-t: max(env(safe-area-inset-top, 0px), 20px); }");
      assert.ok(at > 0);
      const block = html.slice(at, html.indexOf("\n}\n", at));
      assert.match(block, /body::before \{ content: ""; position: fixed;[^}]*height: var\(--sa-t\);[^}]*background: var\(--bg\);/);
    });
  }
});
