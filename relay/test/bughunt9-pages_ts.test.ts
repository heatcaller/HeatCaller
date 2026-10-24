import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { DEFAULT_CONTEXT, resultsPage, videosPage, type PageContext } from "../src/pages.js";

/**
 * Bug hunt 9 (22 Oct 2026): the podium videos page lives at
 * /c/<club>/videos/<race>, so its og:url must say that - never the bare
 * /c/<club>/videos, which is not a route.
 */
describe("videosPage og:url", () => {
  const context: PageContext = { ...DEFAULT_CONTEXT, origin: "https://relay.example" };

  it("names the race's own address", () => {
    const html = videosPage("acme-rc", "Acme RC", context, [], "abc123");
    assert.match(html, /<meta property="og:url" content="https:\/\/relay\.example\/c\/acme-rc\/videos\/abc123">/);
  });

  it("leaves og:url out rather than point at /c/<club>/videos when the race is not known", () => {
    const html = videosPage("acme-rc", "Acme RC", context, []);
    assert.doesNotMatch(html, /og:url/);
    assert.match(html, /og:image/);
  });

  it("encodes and escapes a junk race segment", () => {
    const html = videosPage("acme-rc", "Acme RC", context, [], "a\"b<c");
    assert.doesNotMatch(html, /a"b<c/);
    assert.match(html, /\/c\/acme-rc\/videos\/a%22b%3Cc"/);
  });

  it("still gives other pages their tab address", () => {
    const html = resultsPage("acme-rc", "Acme RC", context);
    assert.match(html, /<meta property="og:url" content="https:\/\/relay\.example\/c\/acme-rc\/results">/);
  });
});
