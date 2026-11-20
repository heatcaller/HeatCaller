import assert from "node:assert/strict";
import { afterEach, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import { CHANNELS } from "../src/types.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * LISTENING ALONG FROM HOME (12 Oct 2026) - the announcer's last few
 * lines, as audio, on the page the club already sends people.
 *
 * WHAT THIS FILE IS REALLY ABOUT, AND IT IS NOT THE AUDIO
 *
 * It is about a channel that ROLLS, which this relay had never had. Every
 * collection until now either keeps everything a club publishes
 * (`results`) or is keyed by something ordered and sparse
 * (`resultsindex`, one document per calendar year). A live feed is
 * neither, and the machinery that handles the club quota turned out to
 * assume one of the two in a way that would have failed silently:
 *
 *   `ownBudget`'s eviction only ever considered items whose id sorted
 *   BELOW the one arriving - correct for "the oldest YEAR makes way" and
 *   useless for a running count, where it would have found nothing it was
 *   allowed to drop and refused the INCOMING line instead. Listening
 *   would have worked for the first eight megabytes of an evening and
 *   then stopped, with nothing anywhere saying why;
 *
 *   and `maxItems` was never reached for such a channel at all, because
 *   the budget branch returns first.
 *
 * So `ChannelSpec.rollOldest` says which kind a channel is, and the tests
 * below prove both kinds behave. The two that matter most are
 * "a rolling channel drops its oldest line rather than refusing a new
 * one" and "a night of commentary cannot push a club's results off the
 * relay".
 */

/** One line of commentary, as the laptop would send it: the words, and the audio as base64. */
function line(at: number, seconds = 1): { seq: number; said: string; audio: string } {
  return {
    seq: at,
    said: `Line ${at}`,

    // ~12KB a second at the bitrate the app renders at, base64'd.
    audio: "A".repeat(seconds * 12 * 1024 * 2),
  };
}

/** The id the laptop mints: a running count, zero-padded so it sorts. */
function id(at: number): string {
  return String(at).padStart(6, "0");
}

describe("listening along from home", () => {
  let harness: Harness | undefined;

  afterEach(async () => {
    await harness?.close();
    harness = undefined;
  });

  /**
   * A club that has turned listening on.
   *
   * Opened here because every test but the switch's own needs it open -
   * and the fact that it has to be is itself the feature: lines are
   * REFUSED until a club says yes, not merely not sent. See
   * "a club that has not said yes has its announcer refused".
   */
  async function serve(): Promise<Harness> {
    harness = await start(new MemoryStore());

    const opened = await call(harness, "POST", "/api/v1/c/acme-rc/listen/open", {
      token: ACME_TOKEN,
      json: { open: true },
    });

    assert.equal(opened.status, 200);
    assert.equal(opened.body.open, true);

    return harness;
  }

  /** A club that has not been asked, which is every club by default. */
  async function serveClosed(): Promise<Harness> {
    harness = await start(new MemoryStore());
    return harness;
  }

  async function send(live: Harness, path: string, json: unknown): Promise<number> {
    const answer = await call(live, "POST", `/api/v1/c/acme-rc/publish/${path}`, { token: ACME_TOKEN, json });
    return answer.status;
  }

  async function heldIds(live: Harness): Promise<string[]> {
    const listing = await call(live, "GET", "/api/v1/c/acme-rc/collection/listen?meta=1");

    assert.equal(listing.status, 200);

    return (listing.body.items as { id: string }[]).map((one) => one.id);
  }

  // ----------------------------------------------------------------
  //  The channel itself
  // ----------------------------------------------------------------

  it("is a rolling collection with a budget of its own", () => {
    const spec = CHANNELS["listen"];

    assert.ok(spec !== undefined);
    assert.equal(spec.kind, "collection");
    assert.equal(spec.rollOldest, true);

    // Off the club's quota. See the channel's own note: counted against
    // it, a night of talking would push a club's saved results off the
    // relay to make room.
    assert.ok((spec.ownBudget ?? 0) > 0);
    assert.ok((spec.maxItems ?? 0) > 0);

    // Public - the page that plays it is the one the club already sends
    // people.
    assert.notEqual(spec.private, true);
  });

  it("takes a line and gives it back", async () => {
    const live = await serve();

    assert.equal(await send(live, `listen/${id(1)}`, line(1)), 200);

    const got = await call(live, "GET", "/api/v1/c/acme-rc/collection/listen/000001");

    assert.equal(got.status, 200);
    assert.equal(got.body.body.said, "Line 1");
  });

  it("needs a token to publish and none to listen", async () => {
    const live = await serve();

    const nope = await call(live, "POST", `/api/v1/c/acme-rc/publish/listen/${id(1)}`, { json: line(1) });

    assert.equal(nope.status, 401);

    assert.equal(await send(live, `listen/${id(1)}`, line(1)), 200);

    // No token: this is the whole point of the feature.
    const anybody = await call(live, "GET", "/api/v1/c/acme-rc/collection/listen?meta=1");

    assert.equal(anybody.status, 200);
  });

  // ----------------------------------------------------------------
  //  It rolls
  // ----------------------------------------------------------------

  /** THE ONE. See the file's own summary. */
  it("a rolling channel drops its oldest line rather than refusing a new one", async () => {
    const live = await serve();
    const most = CHANNELS["listen"]?.maxItems ?? 0;

    assert.ok(most > 0);

    // Fill it, then keep talking well past the limit.
    for (let at = 1; at <= most + 5; at++) {
      assert.equal(await send(live, `listen/${id(at)}`, line(at)), 200, `line ${at} was refused`);
    }

    const held = await heldIds(live);

    assert.equal(held.length, most);

    // The newest are what is left, and the first five are gone.
    assert.ok(held.includes(id(most + 5)));
    assert.ok(!held.includes(id(1)));
    assert.ok(!held.includes(id(5)));
  });

  /**
   * The failure this flag exists to stop. Before `rollOldest`, eviction
   * considered only ids sorting BELOW the one arriving - so a running
   * count that ran past the budget found nothing it could drop and the
   * arriving line was refused with a 507.
   */
  it("keeps accepting lines once its byte budget is full", async () => {
    const live = await serve();

    // Ten seconds of audio each, so the eight-megabyte budget is reached
    // in well under the forty-line count limit.
    for (let at = 1; at <= 40; at++) {
      const status = await send(live, `listen/${id(at)}`, line(at, 10));

      assert.equal(status, 200, `line ${at} was refused with ${status}`);
    }

    const held = await heldIds(live);

    // Something was dropped - the budget did its job - and the newest
    // line is still there, which is the half that used to fail.
    assert.ok(held.length < 40);
    assert.ok(held.includes(id(40)));
  });

  it("a line too big for the channel is refused on its own", async () => {
    const live = await serve();

    // Past maxItemBytes. The club is told, rather than the channel
    // quietly keeping nothing but this one document.
    const huge = await send(live, `listen/${id(1)}`, line(1, 60));

    assert.notEqual(huge, 200);
  });

  // ----------------------------------------------------------------
  //  It cannot cost the club its results
  // ----------------------------------------------------------------

  /** THE OTHER ONE. */
  it("a night of commentary cannot push a club's results off the relay", async () => {
    const live = await serve();

    // A club with results on the relay.
    for (let at = 0; at < 6; at++) {
      assert.equal(await send(live, `results/s${String(at).padStart(2, "0")}`, { race: at, pad: "r".repeat(2000) }), 200);
    }

    // Then an evening of talking, enough to fill the listen budget over
    // and over.
    for (let at = 1; at <= 60; at++) {
      assert.equal(await send(live, `listen/${id(at)}`, line(at, 8)), 200);
    }

    const results = await call(live, "GET", "/api/v1/c/acme-rc/collection/results?meta=1");

    assert.equal(results.status, 200);

    // Every result still there. This is the whole reason the channel has
    // a budget of its own.
    assert.equal((results.body.items as unknown[]).length, 6);
  });

  // ----------------------------------------------------------------
  //  And the year-keyed archive is untouched
  // ----------------------------------------------------------------

  /**
   * `resultsindex` keeps its own behaviour - the oldest YEAR makes way,
   * and a newer year is never pushed out by an older one. Its comment
   * records what ordering by write time cost, so a change meant for a
   * rolling feed must not reach it.
   */
  it("the results index still evicts by year and never evicts a newer one", async () => {
    const live = await serve();

    assert.equal(CHANNELS["resultsindex"]?.rollOldest, undefined);

    // Nine hundred kilobytes a year against a two-megabyte budget, so the
    // third one has to evict something.
    const year = { pad: "i".repeat(900 * 1024) };

    assert.equal(await send(live, "resultsindex/2024", year), 200);
    assert.equal(await send(live, "resultsindex/2025", year), 200);

    // This one overflows, and the OLDEST YEAR makes way - 2024.
    assert.equal(await send(live, "resultsindex/2026", year), 200);

    // Republishing 2024, which the laptop does whenever an old session is
    // edited, now has nothing it is allowed to evict: 2025 and 2026 both
    // sort above it. So the INCOMING document is refused, which is the
    // channel's own stated contract - a newer year is never pushed out by
    // an older one.
    assert.equal(await send(live, "resultsindex/2024", year), 507);

    const listing = await call(live, "GET", "/api/v1/c/acme-rc/collection/resultsindex?meta=1");
    const years = (listing.body.items as { id: string }[]).map((one) => one.id).sort();

    // The current season survived, which is the whole point of it.
    assert.deepEqual(years, ["2025", "2026"]);
  });

  // ----------------------------------------------------------------
  //  The switch, which is off until a club says otherwise
  // ----------------------------------------------------------------

  /**
   * The thing the plan asked for by name: a room's announcer saying
   * something unfortunate about a ten-year-old's driving is a different
   * matter in a hall and on the internet, so a club that has never been
   * asked has not agreed.
   */
  it("a club that has not said yes has its announcer refused", async () => {
    const live = await serveClosed();

    const status = await send(live, `listen/${id(1)}`, line(1));

    assert.equal(status, 403);

    // And nothing was kept.
    assert.deepEqual(await heldIds(live), []);
  });

  /**
   * Refused rather than trusted to the laptop not to send. The laptop has
   * its own switch, but a switch on one machine is not a guarantee: an
   * older build, a restored backup, a second laptop set up with the same
   * token.
   */
  it("the refusal is the relay's own, not the laptop's good manners", async () => {
    const live = await serveClosed();

    // A perfectly well-formed line, correctly signed, from a club that
    // simply has not turned this on.
    const answer = await call(live, "POST", `/api/v1/c/acme-rc/publish/listen/${id(1)}`, {
      token: ACME_TOKEN,
      json: line(1),
    });

    assert.equal(answer.status, 403);
    assert.match(answer.body.error, /not letting people listen along/);
  });

  it("switching it off takes down what is already there", async () => {
    const live = await serve();

    for (let at = 1; at <= 5; at++) {
      assert.equal(await send(live, `listen/${id(at)}`, line(at)), 200);
    }

    assert.equal((await heldIds(live)).length, 5);

    const closed = await call(live, "POST", "/api/v1/c/acme-rc/listen/open", {
      token: ACME_TOKEN,
      json: { open: false },
    });

    assert.equal(closed.status, 200);
    assert.equal(closed.body.open, false);

    // STOPPING SENDING IS NOT ENOUGH. Left alone, the last few minutes of
    // an evening would sit on a public address for as long as the club's
    // budget held them - so closing the switch is what takes them down.
    assert.deepEqual(await heldIds(live), []);
  });

  it("the switch needs the club's own token", async () => {
    const live = await serveClosed();

    const noToken = await call(live, "POST", "/api/v1/c/acme-rc/listen/open", { json: { open: true } });
    const wrongClub = await call(live, "POST", "/api/v1/c/acme-rc/listen/open", {
      token: BROOK_TOKEN,
      json: { open: true },
    });

    assert.equal(noToken.status, 401);
    assert.equal(wrongClub.status, 403);

    // Neither opened it.
    assert.equal(await send(live, `listen/${id(1)}`, line(1)), 403);
  });

  it("anything but the literal true is not a yes", async () => {
    const live = await serveClosed();

    for (const notYes of [{ open: "yes" }, { open: 1 }, {}, { open: null }]) {
      const answer = await call(live, "POST", "/api/v1/c/acme-rc/listen/open", {
        token: ACME_TOKEN,
        json: notYes,
      });

      assert.equal(answer.status, 200);
      assert.equal(answer.body.open, false, `${JSON.stringify(notYes)} was taken for a yes`);
    }

    assert.equal(await send(live, `listen/${id(1)}`, line(1)), 403);
  });

  // ----------------------------------------------------------------
  //  The page that plays it
  // ----------------------------------------------------------------

  it("the live page offers nothing to listen to until the club turns it on", async () => {
    const live = await serveClosed();

    const page = await call(live, "GET", "/c/acme-rc/live", {});

    assert.equal(page.status, 200);

    // Not drawn and disabled - not drawn at all. A Listen button for a
    // club that has not turned it on answers nothing.
    assert.doesNotMatch(page.text, /listenbtn/);
    assert.doesNotMatch(page.text, /Listen live/);
  });

  it("the live page offers the announcer once the club has", async () => {
    const live = await serve();

    const page = await call(live, "GET", "/c/acme-rc/live", {});

    assert.equal(page.status, 200);
    assert.match(page.text, /listenbtn/);
    assert.match(page.text, /Listen live/);

    // A button, not autoplay: browsers refuse to start audio unasked, and
    // a page that starts talking at whoever opens it is a page people
    // close.
    assert.match(page.text, /Press Listen Live to hear the broadcast./);
  });

  /**
   * Without media-src, audio falls back to default-src 'none' and the
   * browser blocks every note of it - so the player would have been
   * silent with nothing anywhere saying why.
   *
   * The `data:` is there because the relay must not decode a club's
   * payload to find the bytes; see the policy's own note.
   */
  it("the pages allow audio at all", async () => {
    const live = await serve();

    const page = await call(live, "GET", "/c/acme-rc/live", {});
    const policy = page.headers["content-security-policy"] ?? "";

    assert.match(policy, /media-src 'self' data:/);

    // And nothing else was loosened on the way.
    assert.match(policy, /default-src 'none'/);
    assert.match(policy, /script-src 'nonce-/);
  });

  it("the player's script rides the page's own nonce", async () => {
    const live = await serve();

    const page = await call(live, "GET", "/c/acme-rc/live", {});

    // The player is appended to the live page's own script rather than
    // added as a block of its own, so there is still nothing on the page
    // an injected tag could run from: the shared app.js (19 Nov 2026) and
    // the page's own script, both under the one nonce.
    const tags = page.text.match(/<script[^>]*>/g) ?? [];
    const nonce = /<script nonce="([^"]+)">/.exec(page.text)?.[1] ?? "";
    assert.equal(tags.length, 2);
    assert.match(tags[0] ?? "", /^<script src="\/c\/acme-rc\/app\.js\?v=[0-9a-f]{12}" nonce="/);
    assert.ok(nonce !== "" && tags.every((t) => t.includes(`nonce="${nonce}"`)));
  });

  it("one club turning it on does not turn it on for another", async () => {
    const live = await serve();

    const brook = await call(live, "POST", `/api/v1/c/brookside/publish/listen/${id(1)}`, {
      token: BROOK_TOKEN,
      json: line(1),
    });

    assert.equal(brook.status, 403);
  });
});
