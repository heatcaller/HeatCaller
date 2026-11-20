import assert from "node:assert/strict";
import { Script } from "node:vm";
import { after, before, describe, it } from "node:test";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";
import { pausedPage, relayNoticePage } from "../src/pages.js";
import { SUN_ICON_SIZE, sunIcon, sunPixels } from "../src/sun.js";
import { pageScriptMatch } from "./pagescript.js";

// HeatCaller on the club's pages (30 Oct 2026, relay branding): Asher
// picked the footer sign-off, the byline, Live's broadcast bug, the sun on
// shared cards, the "What's HeatCaller?" sheet, HeatCaller's own pages and
// the sun as every club's icon. The mark is always on.

const PAGES = ["/c/acme-rc", "/c/acme-rc/live", "/c/acme-rc/schedule", "/c/acme-rc/results", "/c/acme-rc/standings", "/c/acme-rc/mynight"];

describe("relay branding: HeatCaller on every club page", () => {
  let harness: Harness;

  before(async () => {
    harness = await start();
  });

  after(async () => {
    await harness.close();
  });

  it("signs off every page in the footer, and the mark opens the sheet", async () => {
    for (const path of PAGES) {
      const page = (await call(harness, "GET", path)).text;
      const footer = /<footer>[\s\S]*?<\/footer>/.exec(page)?.[0] ?? "";

      assert.match(footer, /<a class="hcmark" href="https:\/\/heatcaller\.com" aria-label="What's HeatCaller\?" data-hc-about>/, path);
      assert.match(footer, /<span class="hcword">Heat<span class="hcc">Caller<\/span><\/span>/, path);
      assert.match(page, /<div class="hcabout" id="hcabout"[^>]*hidden>/, path);
      assert.match(page, /<a class="go" href="https:\/\/heatcaller\.com" target="_blank" rel="noopener">Bring HeatCaller to your track<\/a>/, path);
    }
  });

  it("names the club on the sheet, escaped", async () => {
    await call(harness, "POST", "/api/v1/c/acme-rc/publish/site?seq=2", { token: ACME_TOKEN, json: {} });
    const page = (await call(harness, "GET", "/c/acme-rc")).text;
    assert.match(page, /<p>Acme RC times its races, calls them out loud and puts every lap on your phone with HeatCaller\.<\/p>/);
  });

  it("puts the byline beside the club's name on every page, under Live's bug", async () => {
    for (const path of PAGES) {
      const page = (await call(harness, "GET", path)).text;
      assert.match(page, /<\/h1>\s*<div class="hcside">(\s*<span class="onair"[\s\S]*?<\/span>)?\s*<div class="hcby"><svg class="hcsun"[\s\S]*?<\/svg><span>on HeatCaller<\/span><\/div>\s*<\/div>\s*<\/div>/, path);
      // No line under the name saying what the page is for (5 Nov 2026,
      // Asher: "remove all top of page tool tips from relay pages").
      assert.doesNotMatch(page.slice(0, page.indexOf("</header>")), /<div class="sub">/, path);
    }
  });

  it("carries the broadcast bug on Live only, hidden until a race runs", async () => {
    const live = (await call(harness, "GET", "/c/acme-rc/live")).text;
    assert.match(live, /<header class="onair-room">/);
    // The word alone since 4 Nov 2026 (Asher: "remove the sun icon from the live bug").
    assert.match(live, /<span class="onair" aria-hidden="true"><b>LIVE<\/b><\/span>/);
    assert.match(live, /onAir\(fresh && \['Staging', 'Countdown', 'Racing'\]\.includes\(String\(state\.state\)\)\)/);

    for (const path of PAGES.filter((p) => p !== "/c/acme-rc/live")) {
      const page = (await call(harness, "GET", path)).text;
      assert.equal(page.includes('class="onair"'), false, path);
      assert.match(page, /<header>/, path);
    }
  });

  // 1 Nov 2026, Asher: "when classic theme is selected the sun should be
  // yellow and orange not pink". Classic wears Synthwave, the default look.
  it("draws the sun as a yellow-to-orange sunset on Synthwave, and in the primary on any other look", async () => {
    const sunset = (page: string) =>
      /<style>\.hcsun\{fill:url\(#hcsunset\) #FFB347\}<\/style>/.test(page)
      && /<body[^>]*><svg class="hcdefs" aria-hidden="true" focusable="false"><linearGradient id="hcsunset"/.test(page);

    const publish = async (seq: number, theme: unknown) => {
      const response = await call(harness, "POST", `/api/v1/c/acme-rc/publish/site?seq=${seq}`, { token: ACME_TOKEN, json: { theme } });
      assert.equal(response.status, 200);
    };

    try {
      await publish(100, {});
      for (const path of PAGES) assert.equal(sunset((await call(harness, "GET", path)).text), true, `default ${path}`);

      await publish(101, { colors: { primary: "#ff2e9a", bg: "#0F0722" } });
      assert.equal(sunset((await call(harness, "GET", "/c/acme-rc")).text), true, "Synthwave sent");

      await publish(102, { colors: { primary: "#FFD400", bg: "#0B0B0C" } });
      for (const path of PAGES) {
        const page = (await call(harness, "GET", path)).text;
        assert.equal(sunset(page), false, `Pit Lane ${path}`);
        assert.equal(page.includes("hcsunset"), false, `Pit Lane ${path}`);
      }
    } finally {
      await publish(103, {});
    }
  });

  it("ships scripts that still parse", async () => {
    for (const path of PAGES) {
      const page = (await call(harness, "GET", path)).text;
      const script = pageScriptMatch(page)?.[1] ?? "";
      assert.doesNotThrow(() => new Script(script), path);
      assert.match(script, /closest\('\[data-hc-about\]'\)/, path);
    }
  });

  it("serves HeatCaller's sun as the icon for every club", async () => {
    const icon = Buffer.from(await (await fetch(`${harness.base}/c/acme-rc/icon.png`)).arrayBuffer());
    assert.deepEqual(icon, sunIcon());
    assert.equal(icon.readUInt32BE(16), SUN_ICON_SIZE);
  });
});

describe("relay branding: HeatCaller's own pages", () => {
  it("draws the notice page in synthwave, with the words escaped", () => {
    const page = relayNoticePage("No club here", "This address <b>is</b> not a club.");
    assert.match(page, /<p class="wordmark hcword">Heat<span class="hcc">Caller<\/span><\/p>/);
    assert.match(page, /<h1>No club here<\/h1>/);
    assert.match(page, /This address &lt;b&gt;is&lt;\/b&gt; not a club\./);
    assert.match(page, /<svg class="sun"/);
    assert.match(page, /<svg class="floor"/);
    assert.match(page, /<a href="https:\/\/heatcaller\.com">heatcaller\.com<\/a>/);
    assert.equal(page.includes("<script"), false);
  });

  it("names a paused club on HeatCaller's own page", () => {
    const page = pausedPage("acme-rc", "Acme <RC>");
    assert.match(page, /<h1>Acme &lt;RC&gt;<\/h1>/);
    assert.match(page, /<title>Acme &lt;RC&gt; - paused<\/title>/);
    assert.match(page, /This club's pages are paused for now\./);
    assert.match(page, /<svg class="sun"/);
    assert.equal(page.includes("<script"), false);
  });
});

describe("relay branding: the sun as pixels", () => {
  it("is opaque edge to edge for an icon, and rounds its corners for a card", () => {
    const icon = sunPixels(64);
    const tile = sunPixels(64, true);

    assert.equal(icon[3], 255, "icon corner is opaque");
    assert.equal(tile[3], 0, "card tile corner is clear");
    const middle = (32 * 64 + 32) * 4 + 3;
    assert.equal(tile[middle], 255, "card tile middle is opaque");
  });
});
