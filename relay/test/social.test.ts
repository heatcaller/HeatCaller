import assert from "node:assert/strict";
import { after, before, describe, it } from "node:test";
import { socialFrom } from "../src/site.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

describe("club social links: what the relay accepts", () => {
  it("keeps known platforms with http(s) addresses, in the order sent", () => {
    const links = socialFrom([
      { kind: "instagram", url: "https://instagram.com/acmerc" },
      { kind: "facebook", url: "https://www.facebook.com/acmerc" },
      { kind: "website", url: "http://acme-rc.example" },
    ]);

    assert.deepEqual(links.map((l) => l.kind), ["instagram", "facebook", "website"]);
    assert.equal(links[0]?.label, "Instagram");
    assert.equal(links[2]?.url, "http://acme-rc.example/");
  });

  it("drops anything that is not a plain web address or a known platform", () => {
    const links = socialFrom([
      { kind: "facebook", url: "javascript:alert(1)" },
      { kind: "instagram", url: "data:text/html,<b>hi</b>" },
      { kind: "youtube", url: "/relative/path" },
      { kind: "tiktok", url: "https://user:pass@tiktok.com/@acme" },
      { kind: "myspace", url: "https://myspace.com/acme" },
      { kind: "x", url: "https://x.com/" + "a".repeat(400) },
      { kind: "discord", url: "https://discord.gg/ab cd" },
      { kind: "website" },
      "https://acme.example",
      null,
    ]);

    assert.deepEqual(links, []);
  });

  it("takes one link per platform", () => {
    const links = socialFrom([
      { kind: "facebook", url: "https://facebook.com/first" },
      { kind: "facebook", url: "https://facebook.com/second" },
    ]);

    assert.equal(links.length, 1);
    assert.equal(links[0]?.url, "https://facebook.com/first");
  });

  it("gives no links to an app that never sends any", () => {
    assert.deepEqual(socialFrom(undefined), []);
    assert.deepEqual(socialFrom({ facebook: "https://facebook.com/acme" }), []);
  });
});

describe("club social links: on the pages", () => {
  let harness: Harness;

  before(async () => {
    harness = await start();
  });

  after(async () => {
    await harness.close();
  });

  it("shows no links before the club sets any", async () => {
    const answer = await call(harness, "GET", "/c/acme-rc");

    assert.equal(answer.status, 200);
    assert.doesNotMatch(answer.text, /class="social"/);
    assert.doesNotMatch(answer.text, /class="links"/);
  });

  it("puts the club's links on the home page and in every footer", async () => {
    const published = await call(harness, "POST", "/api/v1/c/acme-rc/publish/site?seq=1", {
      token: ACME_TOKEN,
      json: {
        tagline: "Racing every Tuesday",
        social: [
          { kind: "facebook", url: "https://www.facebook.com/acmerc?ref=a&b=\"c\"" },
          { kind: "instagram", url: "javascript:alert(1)" },
        ],
      },
    });

    assert.equal(published.status, 200);

    const home = await call(harness, "GET", "/c/acme-rc");

    // An icon, not the word (5 Nov 2026, Asher: "replace the socials labels
    // with icons"); the name stays as its accessible label and tooltip.
    assert.match(home.text, /<div class="social"><a class="soc" href="https:\/\/www\.facebook\.com\/acmerc\?ref=a&amp;b=%22c%22"[^>]*rel="noopener noreferrer me" aria-label="[^"]+ on Facebook" title="Facebook"><svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">[\s\S]*?<\/svg><\/a><\/div>/);
    assert.doesNotMatch(home.text, />Facebook<\/a>/, "no word on the button");
    assert.doesNotMatch(home.text, /href="javascript:/);
    // No link drawn for it - the page's own script names Instagram (the
    // in-app browser help, 14 Nov 2026), so the check is on the link itself.
    assert.doesNotMatch(home.text, /aria-label="[^"]*Instagram"/);

    const live = await call(harness, "GET", "/c/acme-rc/live");

    assert.match(live.text, /<span class="links"[^>]*><a class="soc" href="https:\/\/www\.facebook\.com\/acmerc[^"]*"[^>]*aria-label="[^"]+ on Facebook"/);
    assert.doesNotMatch(live.text, /class="social"/);
  });
});
