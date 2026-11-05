import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { DEFAULT_CONTEXT, livePage } from "../src/pages.js";

/**
 * THE RACE BOARD FIRST (29 Oct 2026, asked for by Asher: "on the live page
 * of the relay reorder the cards. the race board should be first"). The
 * board sits straight under its flag bar, above Listen, Captions and Lap
 * times in my ear.
 */

/** Where each part starts in the page's body. */
function at(html: string, id: string): number {
  const i = html.indexOf(`id="${id}"`);
  assert.ok(i >= 0, `the page has #${id}`);
  return i;
}

describe("the Live page puts the race board first", () => {
  it("draws the board under the flag bar and above every card, with Listen on", () => {
    const html = livePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, listening: true } });

    assert.match(html, /<div id="bar" class="flagbar">[\s\S]*?<\/div><div id="board"><\/div>/);
    for (const card of ["listen", "laptalk"]) assert.ok(at(html, "board") < at(html, card), card);
    assert.ok(at(html, "listen") < at(html, "laptalk"));
    // Listen shows the words itself, so no Captions card (4 Nov 2026).
    assert.doesNotMatch(html, /id="captions"/);
  });

  it("does the same with Listen off", () => {
    const html = livePage("acme-rc", "Acme RC");

    assert.doesNotMatch(html, /id="listen"/);
    for (const card of ["captions", "laptalk"]) assert.ok(at(html, "board") < at(html, card), card);
  });
});

