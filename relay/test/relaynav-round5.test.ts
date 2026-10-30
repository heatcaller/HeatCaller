import assert from "node:assert/strict";
import { after, before, describe, it } from "node:test";
import { badgesPage, homePage, livePage, resultsPage, standingsPage, tracksPage } from "../src/pages.js";
import { call, start, type Harness } from "./helpers.js";

/**
 * RELAY NAVIGATION, ROUND 5 (30 Oct 2026, Asher's pick: "tabs on results").
 * Standings, Tracks and Achievements are tabs under Results, which stays
 * lit on all four; Home's Explore tiles, which repeated the bar, are gone.
 */

/** Results' own row of tabs, by label, with the one marked as the page. */
function subtabs(html: string): { labels: string[]; current: string | undefined } {
  const row = /<nav class="subtabs" aria-label="Results">([\s\S]*?)<\/nav>/.exec(html)?.[1] ?? "";
  const labels = [...row.matchAll(/<a href="[^"]*"[^>]*>([^<]*)<\/a>/g)].map((m) => m[1] as string);
  return { labels, current: /aria-current="page">([^<]*)</.exec(row)?.[1] };
}

/** The label of the bar's lit tab. */
function litTab(html: string): string | undefined {
  const bar = /<nav class="tabs"[\s\S]*?<\/nav>/.exec(html)?.[0] ?? "";
  return /aria-current="page">[\s\S]*?<span>([^<]*)<\/span>/.exec(bar)?.[1];
}

describe("relay navigation, round 5: Standings, Tracks and Achievements under Results", () => {
  const pages: [string, string, string][] = [
    ["results", resultsPage("acme-rc", "Acme RC"), "Sessions"],
    ["standings", standingsPage("acme-rc", "Acme RC"), "Standings"],
    ["tracks", tracksPage("acme-rc", "Acme RC"), "Tracks"],
    ["badges", badgesPage("acme-rc", "Acme RC"), "Achievements"],
  ];

  for (const [name, html, label] of pages) {
    it(`gives ${name} Results' row of tabs, marks ${label}, and lights Results on the bar`, () => {
      assert.deepEqual(subtabs(html), { labels: ["Sessions", "Standings", "Tracks", "Achievements"], current: label });
      assert.equal(litTab(html), "Results");
    });
  }

  it("links each of Results' tabs to its page, at the address it always had", () => {
    const html = resultsPage("acme-rc", "Acme RC");

    for (const path of ["results", "standings", "tracks", "badges"]) {
      assert.match(html, new RegExp(`<nav class="subtabs"[\\s\\S]*?href="/c/acme-rc/${path}"`));
    }
  });

  it("draws no Results row anywhere else", () => {
    for (const html of [homePage("acme-rc", "Acme RC"), livePage("acme-rc", "Acme RC")]) {
      assert.doesNotMatch(html, /class="subtabs"/);
    }
  });
});

describe("relay navigation, round 5: Home without Explore", () => {
  let harness: Harness;

  before(async () => {
    harness = await start();
  });

  after(async () => {
    await harness.close();
  });

  it("has no Explore tiles, and no tile for a page the bar or Results already has", async () => {
    const home = (await call(harness, "GET", "/c/acme-rc")).text;

    assert.doesNotMatch(home, />Explore</);
    assert.doesNotMatch(home, /class="tile"/);
  });

  it("says where results, standings, tracks and achievements are when nothing is on", () => {
    assert.match(homePage("acme-rc", "Acme RC"), /Results, standings, tracks and achievements are all under Results\./);
  });
});
