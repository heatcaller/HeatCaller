import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { resultsPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * DNS ON THE PUBLIC RESULT (12 Nov 2026). A driver skipped from a race and
 * scored DNS (the laptop's SkipDns) has no result row; the laptop sends their
 * public name in the result's `dns` list and the Results page draws them
 * under the finishers, greyed, escaped, with no position, laps or times.
 */

async function resultsScript(): Promise<FakePage> {
  const answer: Answerer = async () => ({ status: 404 });
  const { html, script } = pageParts(resultsPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, answer);
  page.run(script);
  await page.settle();
  return page;
}

function call(page: FakePage, expression: string): string {
  return String(page.eval(expression));
}

const LINE = { id: "s1", at: "19:30", label: "A Main", day: "2026-11-12" };
const FINISHER = { pos: 1, name: "Sam P", laps: 20, elapsedMs: 300000, fastLapMs: 14000 };

describe("DNS drivers on the Results page", () => {
  it("draws them under the finishers, marked DNS and greyed", async () => {
    const page = await resultsScript();
    const html = call(page, `card(${JSON.stringify(LINE)}, ${JSON.stringify({ results: [FINISHER], dns: ["Max P"] })}, '')`);

    const finisher = html.indexOf("Sam P");
    const dns = html.indexOf('<tbody class="dns">');

    assert.ok(finisher > 0);
    assert.ok(dns > finisher, "the DNS rows come after the finishers");
    assert.match(html.slice(dns), /<td class="pos">DNS<\/td>/);
    assert.match(html.slice(dns), /Max P/);
    assert.match(html.slice(dns), /Did not start/);
  });

  it("escapes a DNS name", async () => {
    const page = await resultsScript();
    const html = call(page, `dnsRows(${JSON.stringify({ dns: ["<img src=x onerror=alert(1)>"] })})`);

    assert.ok(!html.includes("<img"));
    assert.ok(html.includes("&lt;img"));
  });

  it("draws nothing for a result with no DNS, or one from an older laptop", async () => {
    const page = await resultsScript();

    assert.equal(call(page, `dnsRows({ results: [] })`), "");
    assert.equal(call(page, `dnsRows({ dns: [] })`), "");
    assert.equal(call(page, `dnsRows({ dns: "Max P" })`), "");
    assert.equal(call(page, `dnsRows({ dns: [null, 3, "  "] })`), "");
    assert.equal(call(page, `dnsRows(null)`), "");
  });

  it("greys the DNS rows in the page's style", () => {
    const { html } = pageParts(resultsPage("acme-rc", "Acme RC"));

    assert.match(html, /table\.res tbody\.dns td \{ color: var\(--muted\); \}/);
  });
});
