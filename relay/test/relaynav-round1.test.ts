import assert from "node:assert/strict";
import { Script } from "node:vm";
import { after, before, describe, it } from "node:test";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";
import { managePage, missingPage, nightPage, relayNoticePage, signupPage } from "../src/pages.js";

/** The club's tabs, as the shell draws them. */
function tabsOf(html: string): string {
  const match = /<nav class="tabs"[\s\S]*?<\/nav>/.exec(html);
  assert.ok(match, "the shell draws the club's tabs");
  return match[0];
}

/** The tab marked as the page you are on, by its label. */
function litTab(html: string): string | undefined {
  return /aria-current="page">[\s\S]*?<span>([^<]*)<\/span>/.exec(tabsOf(html))?.[1];
}

describe("relay navigation, round 1: the club's tabs", () => {
  let harness: Harness;

  before(async () => {
    harness = await start();
  });

  after(async () => {
    await harness.close();
  });

  // The same five on every screen since 30 Oct 2026 (round 5): Standings,
  // Achievements and Tracks are Results' own tabs - relaynav-round5.test.ts.
  it("has Home, Live, Schedule, Results and Me, and nothing else", async () => {
    const tabs = tabsOf((await call(harness, "GET", "/c/acme-rc/results")).text);
    const labels = [...tabs.matchAll(/<a href="([^"]*)"( class="([^"]*)")?[^>]*>[\s\S]*?<span>([^<]*)<\/span>/g)]
      .map((m) => ({ href: m[1], label: m[4] }));

    assert.deepEqual(labels.map((t) => t.label), ["Home", "Live", "Schedule", "Results", "Me"]);
    assert.equal(labels.find((t) => t.label === "Me")?.href, "/c/acme-rc/mynight");
  });

  it("shows Me whether or not online check-in is open", async () => {
    assert.match(tabsOf((await call(harness, "GET", "/c/acme-rc")).text), /<span>Me<\/span>/);
  });

  it("lights Me on every page of a driver's own", () => {
    assert.equal(litTab(nightPage("acme-rc", "Acme RC")), "Me");
    assert.equal(litTab(signupPage("acme-rc", "Acme RC")), "Me");
  });

  it("lights Me on check-in and the PIN box, and the page's own tab elsewhere", async () => {
    assert.equal(litTab((await call(harness, "GET", "/c/acme-rc/checkin")).text), "Me");
    assert.equal(litTab((await call(harness, "GET", "/c/acme-rc/mynight")).text), "Me");
    assert.equal(litTab((await call(harness, "GET", "/c/acme-rc/live")).text), "Live");
    assert.equal(litTab((await call(harness, "GET", "/c/acme-rc")).text), "Home");
  });

  it("puts the race-on dot on Live and reads it from the state snapshot, but not on Live itself", async () => {
    const html = (await call(harness, "GET", "/c/acme-rc/schedule")).text;

    assert.match(tabsOf(html), /<span>Live<\/span><i class="dot"/);
    assert.match(html, /grab\('\/snapshot\/state'\)/);
    assert.match(html, /tabs\.classList\.toggle\('racing'/);
    assert.match(html, /live\.hasAttribute\('aria-current'\)\) return;/);
  });

  it("keeps the Following chip above the phone bar", async () => {
    const html = (await call(harness, "GET", "/c/acme-rc")).text;
    assert.match(html, /@media \(max-width: 640px\)[\s\S]*?nav\.tabs \{ position: fixed;[\s\S]*?body \.follow \{ bottom: calc\(68px/);
  });
});

describe("relay navigation, round 1: no dead ends", () => {
  let harness: Harness;

  before(async () => {
    harness = await start();
  });

  after(async () => {
    await harness.close();
  });

  it("answers a page a club does not have with its own tabs, still a 404", async () => {
    for (const path of ["/c/acme-rc/nope", "/c/acme-rc/videos", "/c/acme-rc/results/x/y/z"]) {
      const answer = await call(harness, "GET", path);

      assert.equal(answer.status, 404, path);
      assert.match(answer.headers["content-type"] ?? "", /text\/html/, path);
      assert.match(answer.text, /That page isn(&#39;|')t here/, path);
      assert.match(answer.text, /href="\/c\/acme-rc\/live"/, path);
      assert.match(answer.text, /noindex/, path);
    }
  });

  it("answers a club that does not exist in plain words, naming no other club", async () => {
    const answer = await call(harness, "GET", "/c/nobody-here");

    assert.equal(answer.status, 404);
    assert.match(answer.headers["content-type"] ?? "", /text\/html/);
    assert.match(answer.text, /No club here/);
    assert.doesNotMatch(answer.text, /acme/i);
  });

  it("answers the relay's own address with what it is and where HeatCaller lives", async () => {
    const answer = await call(harness, "GET", "/");

    assert.equal(answer.status, 200);
    assert.match(answer.text, /href="https:\/\/heatcaller\.com"/);
    assert.doesNotMatch(answer.text, /acme/i);
  });

  it("leaves the API's own answers as JSON", async () => {
    const answer = await call(harness, "GET", "/api/v1/c/acme-rc/nope", { token: ACME_TOKEN });
    assert.match(answer.headers["content-type"] ?? "", /json/);
  });

  it("gives the manager page a way back to the club's page", () => {
    assert.match(managePage("acme-rc", "Acme RC"), /<a class="linkbtn" href="\/c\/acme-rc">Club page<\/a>/);
  });

  it("escapes what it is given and ships scripts that parse", () => {
    const html = missingPage("acme-rc", "<b>Acme</b>");
    assert.doesNotMatch(html, /<b>Acme<\/b>/);
    assert.doesNotMatch(relayNoticePage("<i>x</i>", "<i>y</i>"), /<i>/);

    const script = /<script[^>]*>([\s\S]*)<\/script>/.exec(html)?.[1] ?? "";
    assert.doesNotThrow(() => new Script(script));
  });
});
