import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, it } from "node:test";
import { cookiesPage, homePage, livePage, myNightPage, resultsPage } from "../src/pages.js";
import { COOKIE_POLICY_HTML, COOKIE_POLICY_VERSION } from "../src/cookies.js";

/**
 * THE COOKIE POLICY ON THE CLUB'S PAGES (15 Nov 2026): every page's footer
 * links to /c/<club>/cookies, and that page names everything the pages keep
 * in a browser. Part 3 of the terms the club accepts points to it.
 */

const SRC = resolve(process.cwd(), "src");
const TERMS = resolve(process.cwd(), "..", "HeatCaller", "HeatCaller", "Assets", "Legal", "HeatCaller-Terms.md");

/** The page code that runs in the browser. */
const PAGE_CODE = ["pages.ts", "mespaces.ts", "garagepage.ts", "push.ts"]
  .map((f) => readFileSync(resolve(SRC, f), "utf8")).join("\n");

/**
 * hc-/hg- names in the page code that are not kept in the browser: element
 * ids and classes, events, alert tags and signature labels. A new name that
 * is kept in the browser must be listed on the policy page, not here.
 */
const NOT_STORAGE = new Set([
  "hc-banner", "hc-marshal", "hc-marshal-soon", "hc-owe", "hc-owe-bar", "hc-owing", "hc-race", "hc-result", "hc-soon",
  "hc-video", "hg-garage", "hg-manage-mac1", "hg-manage-mac2",
  // The offline copies' store, named after the build: listed as "hc-pages-" on its own.
  "hc-pages-",
]);

/** The policy as a reader sees it. */
function plain(html: string): string {
  return html.replace(/<[^>]+>/g, " ").replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&amp;/g, "&").replace(/\s+/g, " ").trim();
}

describe("the cookie policy page", () => {
  it("names everything the pages keep in a browser", () => {
    const policy = plain(COOKIE_POLICY_HTML);
    const names = new Set([...PAGE_CODE.matchAll(/['"`](h[cg]-[a-z0-9-]*[a-z0-9:-])['"`]/g)].map((m) => m[1] as string));

    assert.ok(names.size >= 40, "the page code was read");
    for (const name of names) {
      if (NOT_STORAGE.has(name)) continue;
      // A name the code finishes with the club ("hg-me-" + CLUB) is listed as hg-me-<club>.
      const listed = /[-:]$/.test(name) ? `${name}<club>` : name;
      assert.ok(new RegExp(`(^|[\\s(])${listed.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}`).test(policy),
        `${name} is kept by the pages but not named on the cookie policy page`);
    }
    assert.ok(policy.includes("hc-pages-"), "the offline copies' store is named");
  });

  it("names the one cookie, and the pages set no other", () => {
    const sets = [...PAGE_CODE.matchAll(/document\.cookie = [^;]*?'([a-z-]+)=/g)].map((m) => m[1]);
    assert.ok(sets.length > 0);
    assert.deepEqual([...new Set(sets)], ["hg-theme"]);
    assert.match(plain(COOKIE_POLICY_HTML), /The cookie hg-theme :/);
  });

  it("says who it is from, its version, and how to reach us", () => {
    const policy = plain(COOKIE_POLICY_HTML);
    assert.ok(policy.startsWith(COOKIE_POLICY_VERSION));
    assert.match(policy, /HeatCaller LLC, a New York company/);
    assert.match(policy, /support@heatcaller\.com/);
    assert.match(policy, /What changed .*Version 2, 15 November 2026/);
  });

  it("is what Part 3 of the terms points to", () => {
    const terms = readFileSync(TERMS, "utf8").replace(/\r\n/g, "\n");
    const part3 = terms.slice(terms.indexOf("# Part 3"));
    assert.match(part3, /\/c\/<your club>\/cookies/);
  });

  it("is a club page under the club's look, kept out of search", () => {
    const html = cookiesPage("acme-rc", "Acme RC");

    assert.match(html, /<title>[^<]*Cookies/);
    assert.match(html, /<meta name="robots" content="noindex, nofollow">/);
    assert.ok(html.includes(COOKIE_POLICY_HTML));
  });

  it("is linked from every page's footer", () => {
    for (const html of [homePage("acme-rc", "Acme RC"), livePage("acme-rc", "Acme RC"), resultsPage("acme-rc", "Acme RC"),
      myNightPage("acme-rc", "Acme RC"), cookiesPage("acme-rc", "Acme RC")]) {
      const footer = html.slice(html.indexOf("<footer>"), html.indexOf("</footer>"));
      assert.match(footer, /<a class="hcpolicy" href="\/c\/acme-rc\/cookies">Cookies<\/a>/);
    }
  });
});
