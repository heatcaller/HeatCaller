import assert from "node:assert/strict";
import { createContext, runInContext } from "node:vm";
import { describe, it } from "node:test";
import { homePage } from "../src/pages.js";
import { CLOCK_JS } from "../src/clock.js";
import { ME_JS } from "../src/mespaces.js";

/** One function's whole text out of a page, by its braces. */
function fn(html: string, head: string): string {
  const from = html.indexOf(head);
  assert.notEqual(from, -1, head);
  let depth = 0;
  for (let i = html.indexOf("{", from); i < html.length; i++) {
    if (html[i] === "{") depth++;
    if (html[i] === "}") depth--;
    if (depth === 0) return html.slice(from, i + 1);
  }
  throw new Error("unclosed " + head);
}

function card(body: Record<string, unknown>, tabHref: string | null, stored: Map<string, string> = new Map()): string {
  const html = homePage("acme-rc", "Acme RC");
  const escFrom = html.indexOf("const esc = (v) =>");
  const context = createContext({
    BASE: "/c/acme-rc",
    CLUB: "acme-rc",
    localStorage: { getItem: (k: string) => stored.get(k) ?? null, setItem: (k: string, v: string) => stored.set(k, String(v)) },
    document: { querySelector: () => (tabHref === null ? null : { getAttribute: () => tabHref }), documentElement: { getAttribute: () => null } },
  });
  runInContext(CLOCK_JS, context);
  runInContext(html.slice(escFrom, html.indexOf("));", escFrom) + 3), context);
  runInContext(fn(html, "function nextNightHtml(answer) {"), context);
  runInContext(fn(html, "function nextNightComingHref() {"), context);
  runInContext(fn(html, "function nnStored(prefix) {"), context);
  runInContext(fn(html, "function nnWire(at) {"), context);
  return runInContext("nextNightHtml(answer)", Object.assign(context, { answer: { body: { body } } })) as string;
}

/** Home's Dismiss, clicked: what nnWire does with the card's holder. */
function dismiss(stored: Map<string, string>): string {
  const html = homePage("acme-rc", "Acme RC");
  let click: (() => void) | null = null;
  const at = {
    innerHTML: "card",
    querySelector: () => ({ getAttribute: () => "2026-11-18", addEventListener: (_: string, f: () => void) => { click = f; } }),
  };
  const context = createContext({
    CLUB: "acme-rc",
    localStorage: { getItem: (k: string) => stored.get(k) ?? null, setItem: (k: string, v: string) => stored.set(k, String(v)) },
    at,
  });
  runInContext(fn(html, "function nnWire(at) {"), context);
  runInContext("nnWire(at)", context);
  assert.ok(click, "Dismiss is wired");
  (click as () => void)();
  return at.innerHTML;
}

describe("Home's next race night card once Me has them down (16 Nov 2026)", () => {
  it("has Dismiss instead of Coming? for the night they said they are coming to", () => {
    const html = card(night(), null, new Map([["hg-coming-acme-rc", "2026-11-18"]]));
    assert.match(html, /<button class="page wide hp-go nn-gone" type="button" data-day="2026-11-18">Dismiss<\/button>/);
    assert.doesNotMatch(html, /Coming\?/);
  });

  it("still asks for a different night", () => {
    const html = card(night(), null, new Map([["hg-coming-acme-rc", "2026-11-11"]]));
    assert.match(html, /Coming\? Put yourself down/);
    assert.doesNotMatch(html, /Dismiss/);
  });

  it("Dismiss takes the card away now and keeps it away for that night", () => {
    const stored = new Map([["hg-coming-acme-rc", "2026-11-18"]]);
    assert.equal(dismiss(stored), "");
    assert.equal(stored.get("hg-nn-gone-acme-rc"), "2026-11-18");
    assert.equal(card(night(), null, stored), "");
  });

  it("comes back for the next night after the dismissed one", () => {
    const stored = new Map([["hg-nn-gone-acme-rc", "2026-11-18"]]);
    assert.match(card(night({ day: "2026-11-25" }), null, stored), /Coming\? Put yourself down/);
  });

  it("Me notes the day on I'm coming and forgets it on Not coming after all", () => {
    assert.match(ME_JS, /if \(classes\.length > 0\) localStorage\.setItem\('hg-coming-' \+ CLUB, day\);\s*else localStorage\.removeItem\('hg-coming-' \+ CLUB\);/);
  });
});

const night = (over: Record<string, unknown> = {}) => ({
  day: "2026-11-18", when: "Tue 18 Nov", classes: ["Stock"], takingEntries: true,
  until: new Date(Date.now() + 86400000).toISOString(), ...over,
});

describe("Home's next race night card says I'm coming (12 Nov 2026)", () => {
  // A button like See the running order since 13 Nov 2026; the card itself stays still.
  it("has a button to Me's I'm coming card while the club takes entries", () => {
    const html = card(night(), null);
    assert.match(html, /^<div class="hero next" style="cursor:default">/);
    assert.match(html, /<a class="page wide hp-go nn-go" href="\/c\/acme-rc\/mynight#s=tonight&amp;go=coming">Coming\? Put yourself down<\/a>/);
  });

  it("goes by the Me tab's own link when the phone has the driver", () => {
    assert.match(card(night(), "/c/acme-rc/night#d=ABC123"), /href="\/c\/acme-rc\/night#d=ABC123&amp;s=tonight&amp;go=coming"/);
  });

  it("is only the notice when the club isn't taking entries", () => {
    const html = card(night({ takingEntries: false }), null);
    assert.match(html, /^<div class="hero next" style="cursor:default">/);
    assert.doesNotMatch(html, /Coming\?/);
  });

  it("Me brings its I'm coming card into view when sent there", () => {
    assert.match(ME_JS, /meGoComing = \/\[#&\]go=coming\/\.test\(location\.hash \|\| ''\);/);
    assert.match(ME_JS, /if \(meGoComing\) \{\s*meGoComing = false;/);
  });
});
