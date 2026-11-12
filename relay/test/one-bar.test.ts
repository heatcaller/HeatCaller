import assert from "node:assert/strict";
import { after, before, describe, it } from "node:test";
import { appShellPage, homePage, livePage, nightPage, resultsPage, schedulePage } from "../src/pages.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";
import { call, start, type Harness } from "./helpers.js";

/**
 * ONE BAR ON THE HOME-SCREEN ICON (1 Nov 2026, Asher on his iPhone, once the
 * pages above the bar stopped flashing: "the nav buttons continue to flicker
 * when navigating to different pages"). Every tab is its own page, so each
 * tap threw the bar away with the page and drew the next page's bar from
 * scratch. Opened from the icon, the pages now run under one page that holds
 * the bar, which is drawn once and stays; only the page above it changes.
 */

const pages: [string, string][] = [
  ["home", homePage("acme-rc", "Acme RC")],
  ["live", livePage("acme-rc", "Acme RC")],
  ["schedule", schedulePage("acme-rc", "Acme RC")],
  ["results", resultsPage("acme-rc", "Acme RC")],
  ["a driver's night", nightPage("acme-rc", "Acme RC")],
];

describe("the home-screen icon's one bar", () => {
  const shell = appShellPage("acme-rc", "Acme RC");

  it("holds the five tabs with none marked, a stage for the pages, and the screen's insets to hand on", () => {
    assert.match(shell, /<html lang="en" class="hcshell dark">/);
    assert.match(shell, /<div class="hcstage" id="hcstage"><\/div>/);
    assert.match(shell, /<div class="hcprobe" id="hcprobe" aria-hidden="true"><\/div>/);
    const nav = /<nav class="tabs"[\s\S]*?<\/nav>/.exec(shell)?.[0] ?? "";
    assert.equal((nav.match(/<a /g) ?? []).length, 5);
    assert.doesNotMatch(nav, /aria-current/);
    assert.match(shell, /<meta name="robots" content="noindex, nofollow">/);
  });

  it("keeps the bar flush to the bottom of the full screen, as on the pages", () => {
    assert.match(shell, /html\.hcshell nav\.tabs \{ position: fixed; left: 0; right: 0; bottom: auto; top: 100vh; top: 100lvh; transform: translateY\(-100%\);/);
    assert.match(shell, /html\.hcshell \{ height: 100vh; height: 100lvh; overflow: hidden; overscroll-behavior: none; \}/);
    assert.match(shell, /\.hcstage \{ position: fixed; left: 0; top: 0; width: 100%; height: 100vh; height: 100lvh; \}/);
  });

  it("loads the next page behind the one on screen and swaps them once it has loaded", () => {
    assert.match(shell, /f\.className = 'hcnext';/);
    assert.match(shell, /\.hcstage iframe\.hcnext \{ z-index: 0; \}/);
    assert.match(shell, /old\.className = 'hcnext';\n  requestAnimationFrame\(\(\) => requestAnimationFrame\(\(\) => old\.remove\(\)\)\);/);
    assert.match(shell, /window\.hcGo = hcGo;/);
  });

  it("shows only the club's own pages, and the club's home page for any other address", () => {
    assert.match(shell, /function hcInside\(path\)/);
    assert.match(shell, /if \(!hcInside\(path\)\) return;/);
    assert.match(shell, /if \(hcInside\(asked\)\) first = asked;/);
  });

  it("hands a tab's tap to the stage instead of loading a new page", () => {
    assert.match(shell, /if \(SHELL \|\| hcHeld\) \{\s*\/\/[^\n]*\n[^\n]*\n\s*e\.preventDefault\(\);/);
    assert.match(shell, /if \(page === location\.pathname\) \{ ahead = null; hcTop\(\); return; \}/);
  });

  for (const [name, html] of pages) {
    it(`opens ${name} under the bar when opened from the icon, and hides its own bar there`, () => {
      assert.match(html, /location\.replace\('\/c\/' \+ encodeURIComponent\(CLUB\) \+ '\/app#' \+ encodeURIComponent\(location\.pathname \+ location\.search \+ location\.hash\)\);/);
      assert.match(html, /m\('\(display-mode: standalone\)'\)/);
      assert.match(html, /m\('\(max-width: 640px\)'\)/);
      assert.match(html, /html\.framed nav\.tabs \{ display: none; \}/);
      assert.match(html, /html\.framed body \{ padding-bottom: calc\(76px \+ var\(--sa-b\)\); \}/);
    });
  }

  it("leaves a page in a browser tab as it was", () => {
    const { html, script } = pageParts(homePage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async () => ({ status: 404 }), {});
    page.run(script);
    assert.equal(page.eval("location.href"), undefined);
  });
});

describe("the one bar's page on the relay", () => {
  let harness: Harness;
  before(async () => { harness = await start(); });
  after(async () => { await harness.close(); });

  it("is served under the club, may frame the club's pages, and may be framed by nothing else", async () => {
    const answer = await call(harness, "GET", "/c/acme-rc/app");
    assert.equal(answer.status, 200);
    assert.match(answer.text, /class="hcshell/);
    const policy = answer.headers["content-security-policy"] ?? "";
    assert.match(policy, /frame-src 'self'/);
    assert.match(policy, /frame-ancestors 'self'/);
    assert.equal(answer.headers["x-frame-options"], "SAMEORIGIN");
  });
});
