import assert from "node:assert/strict";
import { createContext, runInContext } from "node:vm";
import { afterEach, beforeEach, describe, it } from "node:test";
import { homePage } from "../src/pages.js";
import { CHANNELS } from "../src/types.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";
import { withSharedInline } from "./pagescript.js";

/**
 * THE NIGHT BEFORE (13 Oct 2026) - a preview of a meeting that has not
 * happened yet, on the page a club already sends people.
 *
 * WHAT THIS FILE IS REALLY ABOUT, AND IT IS NOT THE WORDS
 *
 * It is about a document that goes wrong BY ITSELF, which this relay had
 * never had. Everything else here is replaced by a newer version of
 * itself: a result by a corrected result, a roster by a fresh roster, a
 * frame by the next frame. A preview is the first thing that becomes
 * false through the passage of time alone - left up it tells the public,
 * in the future tense, about a meeting that finished hours ago.
 *
 * So it comes down two ways and it needs both, and the second is the one
 * that matters:
 *
 *   the laptop withdraws it when the meeting starts, which covers the
 *   ordinary evening;
 *
 *   and the document carries its own expiry, which the PAGE honours -
 *   because the case the laptop cannot fix is the laptop never coming
 *   back. Packed up, switched off, taken home. That is exactly when a
 *   preview is at its most stale.
 *
 * The two tests worth reading first are
 * "a preview whose time has passed is not drawn" and
 * "a preview with an unreadable expiry is treated as expired" - the
 * second because the other way round, treating a bad date as "stands
 * forever", is how a preview from March is still on a club's front page
 * in June.
 */

/**
 * One function out of the home page's script, run on its own.
 *
 * The whole script ends by starting a poll, so it cannot simply be
 * evaluated - this takes the one function and nothing else, the way
 * bughunt4 does for the manager's undo line.
 */
function previewHtmlFrom(html: string): (answer: unknown) => string {
  const from = html.indexOf("function previewHtml(answer) {");

  assert.notEqual(from, -1, "the home page carries previewHtml");

  let depth = 0;
  let to = from;

  for (let i = html.indexOf("{", from); i < html.length; i++) {
    if (html[i] === "{") depth++;
    if (html[i] === "}") depth--;

    if (depth === 0) {
      to = i;
      break;
    }
  }

  // THE PAGE'S OWN ESCAPER, not a copy of it. A stub that did not escape
  // would make the escaping test below pass while proving nothing - which
  // is exactly what it did on the first run.
  const escFrom = html.indexOf("const esc = (v) =>");
  const escTo = html.indexOf("));", escFrom);

  assert.notEqual(escFrom, -1, "the page carries its own escaper");
  assert.notEqual(escTo, -1, "the escaper is the shape this expects");

  const context = createContext({});

  runInContext(html.slice(escFrom, escTo + 3), context);
  runInContext(html.slice(from, to + 1), context);

  return (answer: unknown) =>
    runInContext("previewHtml(answer)", Object.assign(context, { answer })) as string;
}

/** What the relay hands the page for a snapshot, wrapped the way grab() returns it. */
function served(body: unknown): unknown {
  return { body: { body, publishedAt: new Date().toISOString(), seq: 1 } };
}

function preview(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    kicker: "Coming up",
    title: "Friday Club Night",
    when: "Friday 17 October, 10:15",
    text: "Nineteen cars are entered across three classes.\n\nThe track record has stood since March.",
    until: new Date(Date.now() + 3600_000).toISOString(),
    ...overrides,
  };
}

describe("the night before: the channel", () => {
  let harness: Harness;

  beforeEach(async () => {
    harness = await start();
  });

  afterEach(async () => {
    await harness.close();
  });

  it("is registered as a snapshot, because a club has one next meeting", () => {
    const spec = CHANNELS["preview"];

    assert.ok(spec, "the preview channel is registered");
    assert.equal(spec.kind, "snapshot");
  });

  it("is public, because the whole point of it is people who are not members yet", () => {
    assert.notEqual(CHANNELS["preview"]?.private, true);
  });

  it("takes a preview and serves it back to anybody", async () => {
    const sent = await call(harness, "POST", "/api/v1/c/acme-rc/publish/preview?seq=1", {
      token: ACME_TOKEN,
      json: preview(),
    });

    assert.equal(sent.status, 200);

    // No token on the way out. A preview nobody can read is a poster in a
    // locked room.
    const got = await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/preview");

    assert.equal(got.status, 200);
    assert.equal(got.body.body.title, "Friday Club Night");
  });

  it("replaces a preview with an empty one rather than needing it dropped", async () => {
    await call(harness, "POST", "/api/v1/c/acme-rc/publish/preview?seq=1", { token: ACME_TOKEN, json: preview() });

    // THE WITHDRAWAL. Empty, not absent - see NightBefore.Withdrawn. There
    // is no route that deletes a snapshot, and there should not be: the
    // last thing sent would stand on the relay for good, because nothing
    // newer would ever replace it.
    const taken = await call(harness, "POST", "/api/v1/c/acme-rc/publish/preview?seq=2", {
      token: ACME_TOKEN,
      json: { text: "" },
    });

    assert.equal(taken.status, 200);

    const got = await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/preview");

    assert.equal(got.status, 200);
    assert.equal(got.body.body.text, "");

    // And the old words are gone from the relay's own store, not merely
    // hidden by the page.
    assert.doesNotMatch(JSON.stringify(await harness.store.getSnapshot("acme-rc", "preview")), /Nineteen cars/);
  });

  it("needs the club's own token to publish one", async () => {
    const sent = await call(harness, "POST", "/api/v1/c/acme-rc/publish/preview?seq=1", { json: preview() });

    assert.equal(sent.status, 401);
  });

  it("answers for a club that has never published one without inventing anything", async () => {
    const got = await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/preview");

    assert.equal(got.status, 404);
  });
});

describe("the night before: what the page draws", () => {
  let draw: (answer: unknown) => string;

  beforeEach(() => {
    draw = previewHtmlFrom(withSharedInline(homePage("acme-rc", "Acme RC")));
  });

  it("draws the preview it is given", () => {
    const html = draw(served(preview()));

    assert.match(html, /Friday Club Night/);
    assert.match(html, /Friday 17 October, 10:15/);
    assert.match(html, /Nineteen cars are entered/);
    assert.match(html, /The track record has stood/);
  });

  it("makes a paragraph of each piece rather than one wall of text", () => {
    const html = draw(served(preview()));

    assert.equal(html.match(/<p class="note">/g)?.length, 2);
  });

  it("draws nothing at all for a club that has never published one", () => {
    assert.equal(draw({ missing: true, status: 404 }), "");
    assert.equal(draw(null), "");
  });

  it("draws nothing for a withdrawn preview", () => {
    // The laptop's own take-down: empty text, not an absent document.
    assert.equal(draw(served(preview({ text: "" }))), "");
    assert.equal(draw(served(preview({ text: "   \n  " }))), "");
  });

  it("a preview whose time has passed is not drawn", () => {
    // THE CASE THE LAPTOP CANNOT FIX: the club packed up and went home,
    // so nothing ever withdrew it. Without this the preview sits on the
    // front page describing last Friday in the future tense.
    const over = new Date(Date.now() - 1000).toISOString();

    assert.equal(draw(served(preview({ until: over }))), "");
  });

  it("a preview with an unreadable expiry is treated as expired", () => {
    // Deliberately this way round. "Stands forever when the date cannot
    // be read" is how a preview from March is still up in June, and an
    // older laptop that sends no expiry at all lands here too.
    assert.equal(draw(served(preview({ until: "soon" }))), "");
    assert.equal(draw(served(preview({ until: "" }))), "");
    assert.equal(draw(served(preview({ until: undefined }))), "");
  });

  it("stands right up to its expiry", () => {
    // The boundary in the direction that costs something: an hour early
    // and a club's Friday preview is gone on Friday morning.
    assert.notEqual(draw(served(preview({ until: new Date(Date.now() + 2000).toISOString() }))), "");
  });

  it("escapes everything a club wrote, title and all", () => {
    const html = draw(served(preview({
      title: "<script>alert(1)</script>",
      text: "Bring a <b>spare</b> battery",
      kicker: "\"quoted\"",
      when: "<img src=x>",
    })));

    assert.doesNotMatch(html, /<script>alert/);
    assert.doesNotMatch(html, /<b>spare<\/b>/);
    assert.doesNotMatch(html, /<img src=x>/);

    // And the words themselves still came through.
    assert.match(html, /spare/);
  });

  it("stands up to a document with nothing in it", () => {
    // A hand-edited file, a half-written publish, an older laptop. None of
    // these may take the club's front page down with them.
    assert.equal(draw(served({})), "");
    assert.equal(draw(served(null)), "");
    assert.equal(draw({ body: {} }), "");
  });

  it("does not need a title or a when to draw the words", () => {
    const html = draw(served({ text: "Just the words.", until: new Date(Date.now() + 3600_000).toISOString() }));

    assert.match(html, /Just the words\./);

    // A sensible stand-in rather than the word "undefined" on a club's
    // front page.
    assert.match(html, /Next race day/);
  });
});

describe("the night before: the home page's own slot", () => {
  let harness: Harness;

  beforeEach(async () => {
    harness = await start();
  });

  afterEach(async () => {
    await harness.close();
  });

  // Round 6 (30 Oct 2026): the preview is Home's main card, drawn into
  // "now" in place of "No racing right now", rather than a slot of its own.
  it("fetches the snapshot into Home's main card", async () => {
    const html = (await call(harness, "GET", "/c/acme-rc")).text;

    assert.match(html, /id="now"/);
    assert.match(html, /\/snapshot\/preview/);
    assert.match(html, /else if \(coming\) \{\s*now\.innerHTML = coming;/);
  });

  it("has no heading of its own, so a club with no preview has no empty one", async () => {
    const html = (await call(harness, "GET", "/c/acme-rc")).text;

    assert.doesNotMatch(html, /The night before/);
    assert.doesNotMatch(html, /id="preview"/);
  });

  it("still carries exactly one script block", async () => {
    // The rule the whole site keeps - one inline script under one nonce,
    // and since 19 Nov 2026 the shared app.js before it, under the same.
    const html = (await call(harness, "GET", "/c/acme-rc")).text;
    const tags = html.match(/<script[^>]*>/g) ?? [];
    const nonce = /<script nonce="([^"]+)">/.exec(html)?.[1] ?? "";

    assert.equal(tags.length, 2);
    assert.match(tags[0] ?? "", /^<script src="\/c\/acme-rc\/app\.js\?v=[0-9a-f]{12}" nonce="/);
    assert.ok(nonce !== "" && tags.every((t) => t.includes(`nonce="${nonce}"`)));
  });
});
