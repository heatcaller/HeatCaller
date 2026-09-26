import assert from "node:assert/strict";
import { after, before, describe, it } from "node:test";
import { previewCard, previewFor, previewsIn } from "../src/preview.js";
import { MemoryStore, type Store } from "../src/store.js";
import type { StoredDocument } from "../src/types.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * The card a shared result arrives with.
 *
 * Two halves, and the join between them is where the care goes: a narrow
 * reader that takes a handful of named fields off the club's index and
 * ignores everything else, and a route that puts what it read into meta
 * attributes. The reader deliberately does not strip angle brackets - a
 * club may call a heat whatever it likes - so the page's escaping is the
 * only thing between a label and an injection, and the last test here is
 * the one that proves it.
 */

function doc(body: unknown): StoredDocument {
  return { seq: 1, publishedAt: "2026-09-19T12:00:00.000Z", body };
}

const GOOD = doc({
  shape: 1,
  year: "2026",
  sessions: [
    { id: "heat-1", day: "2026-09-19", at: "19:40", type: "Race", label: "A Final", track: "Blue Loop", season: "Winter Series", drivers: 8, won: "Alex Kent" },
    { id: "heat-2", day: "2026-09-19", at: "20:10", type: "Race", label: "B Final", track: "Blue Loop", season: "", drivers: 6 },
  ],
});

describe("what a preview may say about a session", () => {
  it("takes what the index says, and no more", () => {
    const session = previewFor(GOOD, "heat-1");

    assert.deepEqual(session, {
      id: "heat-1",
      label: "A Final",
      track: "Blue Loop",
      season: "Winter Series",
      day: "2026-09-19",
      at: "19:40",
      drivers: 8,
      won: "Alex Kent",
      top: [],
    });
  });

  it("has no winner for an index from an app that does not send one", () => {
    assert.equal(previewFor(GOOD, "heat-2")?.won, "");
  });

  it("knows nothing about a session it was not asked about", () => {
    assert.equal(previewFor(GOOD, "heat-9"), undefined);
    assert.equal(previewFor(GOOD, ""), undefined);
  });

  it("refuses a session whose id could never have been stored here", () => {
    const odd = doc({ sessions: [{ id: "../../etc/passwd", label: "A Final" }, { id: "", label: "B" }, { label: "C" }] });

    assert.deepEqual(previewsIn(odd, 10), []);
    assert.equal(previewFor(odd, "../../etc/passwd"), undefined);
  });

  it("cleans the club's own text rather than trusting its shape", () => {
    const messy = doc({
      sessions: [{
        id: "heat-1",
        label: `  A\u0000Final\n\nagain  `,
        track: "x".repeat(300),
        season: 17,
        won: { name: "nobody" },
      }],
    });

    const session = previewFor(messy, "heat-1");

    assert.equal(session?.label, "A Final again");
    assert.equal(session?.track.length, 80);
    assert.equal(session?.season, "");
    assert.equal(session?.won, "");
  });

  it("leaves angle brackets alone, because escaping them is the page's job", () => {
    const sharp = doc({ sessions: [{ id: "heat-1", label: '"><script>alert(1)</script>' }] });

    assert.equal(previewFor(sharp, "heat-1")?.label, '"><script>alert(1)</script>');
  });

  it("takes a day only when it is one", () => {
    const days = ["2026-09-19", "2026-02-30", "2026-13-01", "19/09/2026", "2026-9-1", "", "tomorrow"];
    const got = days.map((day) => previewFor(doc({ sessions: [{ id: "heat-1", day }] }), "heat-1")?.day);

    assert.deepEqual(got, ["2026-09-19", "", "", "", "", "", ""]);
  });

  it("takes a time only when it is one", () => {
    const times = ["19:40", "00:00", "23:59", "24:00", "7:40", "19:60", "19:40:00", 1940];
    const got = times.map((at) => previewFor(doc({ sessions: [{ id: "heat-1", at }] }), "heat-1")?.at);

    assert.deepEqual(got, ["19:40", "00:00", "23:59", "", "", "", "", ""]);
  });

  it("counts drivers only when they are counted", () => {
    const counts = [8, 0, "8", -1, 1.5, 99999, NaN, null, undefined];
    const got = counts.map((drivers) => previewFor(doc({ sessions: [{ id: "heat-1", drivers }] }), "heat-1")?.drivers);

    assert.deepEqual(got, [8, 0, 0, 0, 0, 9999, 0, 0, 0]);
  });

  it("never throws, whatever it is handed", () => {
    const rubbish: unknown[] = [
      undefined, null, 0, "", "a string", [], [1, 2, 3], { sessions: null }, { sessions: "heat-1" },
      { sessions: {} }, { sessions: [null, 1, "x", [], { id: 1 }] }, { sessions: [{ id: "heat-1" }] },
      { SESSIONS: [] }, Object.create(null),
    ];

    for (const body of rubbish) {
      assert.doesNotThrow(() => previewFor(doc(body), "heat-1"));
      assert.doesNotThrow(() => previewsIn(doc(body), 10));
    }

    assert.equal(previewFor(undefined, "heat-1"), undefined);
    assert.deepEqual(previewsIn(undefined, 10), []);
    assert.equal(previewFor(doc([{ id: "heat-1" }]), "heat-1"), undefined);
  });

  it("stops at the limit it is given", () => {
    const many = doc({ sessions: Array.from({ length: 50 }, (_, i) => ({ id: `heat-${i}` })) });

    assert.equal(previewsIn(many, 10).length, 10);
    assert.deepEqual(previewsIn(many, 0), []);
  });

  it("takes the podium the index publishes", () => {
    const podium = doc({
      sessions: [{
        id: "heat-1",
        top: [{ p: 1, n: "Alex Kent", laps: 24 }, { p: 2, n: "Sam Whitfield", laps: 23 }, { p: 3, n: "Jo Ng", laps: 23 }],
      }],
    });

    assert.deepEqual(previewFor(podium, "heat-1")?.top, [
      { p: 1, n: "Alex Kent", laps: 24 },
      { p: 2, n: "Sam Whitfield", laps: 23 },
      { p: 3, n: "Jo Ng", laps: 23 },
    ]);
  });

  it("takes fewer than three, and none at all", () => {
    const two = doc({ sessions: [{ id: "heat-1", top: [{ p: 1, n: "A", laps: 5 }, { p: 2, n: "B", laps: 4 }] }] });

    assert.equal(previewFor(two, "heat-1")?.top.length, 2);
    assert.deepEqual(previewFor(doc({ sessions: [{ id: "heat-1", top: [] }] }), "heat-1")?.top, []);
    assert.deepEqual(previewFor(doc({ sessions: [{ id: "heat-1" }] }), "heat-1")?.top, []);
  });

  it("puts the podium in order whatever order the index sent it in", () => {
    const shuffled = doc({
      sessions: [{ id: "heat-1", top: [{ p: 3, n: "Third" }, { p: 1, n: "First" }, { p: 2, n: "Second" }] }],
    });

    assert.deepEqual(previewFor(shuffled, "heat-1")?.top.map((place) => place.n), ["First", "Second", "Third"]);
  });

  it("stops at three, however many the index sends", () => {
    const many = doc({
      sessions: [{ id: "heat-1", top: Array.from({ length: 40 }, (_, i) => ({ p: (i % 3) + 1, n: `D${i}` })) }],
    });

    // Three at most, and the first claim on each place is the one kept -
    // a document naming two seconds must not draw two of them.
    assert.deepEqual(previewFor(many, "heat-1")?.top.map((place) => place.n), ["D0", "D1", "D2"]);
  });

  it("drops a place that is not one", () => {
    const odd = doc({
      sessions: [{
        id: "heat-1",
        top: [
          { p: 0, n: "Zeroth" }, { p: 4, n: "Fourth" }, { p: -1, n: "Minus" }, { p: 1.5, n: "Half" },
          { p: "1", n: "String" }, { p: 1, n: "" }, { p: 2, n: { name: "x" } }, null, 7, "x", [],
          { p: 3, n: "Real" },
        ],
      }],
    });

    assert.deepEqual(previewFor(odd, "heat-1")?.top, [{ p: 3, n: "Real", laps: 0 }]);
  });

  it("counts laps only when they are counted", () => {
    const laps = [24, 0, "24", -1, 1.5, 99999, NaN, null, undefined];
    const got = laps.map((value) =>
      previewFor(doc({ sessions: [{ id: "heat-1", top: [{ p: 1, n: "A", laps: value }] }] }), "heat-1")?.top[0]?.laps,
    );

    assert.deepEqual(got, [24, 0, 0, 0, 0, 9999, 0, 0, 0]);
  });

  it("cleans a podium name exactly as it cleans every other word a club wrote", () => {
    const messy = doc({
      sessions: [{ id: "heat-1", top: [{ p: 1, n: `  Alex\u0000 Kent\n\n `, laps: 1 }, { p: 2, n: "x".repeat(300) }] }],
    });

    const top = previewFor(messy, "heat-1")?.top;

    assert.equal(top?.[0]?.n, "Alex Kent");
    assert.equal(top?.[1]?.n.length, 80);
  });

  it("never throws on a podium, whatever it is handed", () => {
    const rubbish: unknown[] = [null, 0, "", "top", {}, [null], [[]], [{ p: {} }], [{ n: [] }], Object.create(null)];

    for (const top of rubbish) {
      assert.doesNotThrow(() => previewFor(doc({ sessions: [{ id: "heat-1", top }] }), "heat-1"));
    }
  });

  it("writes the card a chat app draws", () => {
    const card = previewCard(previewFor(GOOD, "heat-1") as never, "acme-rc", "Acme RC");

    assert.equal(card.title, "A Final - Acme RC");
    assert.equal(card.description, "Saturday, September 19, 2026 at 19:40 · Blue Loop · Winter Series · 8 drivers · won by Alex Kent");
    assert.equal(card.path, "/c/acme-rc/results/heat-1");
  });

  it("leaves out what the index did not say rather than writing that it did not say it", () => {
    const bare = previewCard(previewFor(doc({ sessions: [{ id: "heat-1", drivers: 1 }] }), "heat-1") as never, "acme-rc", "Acme RC");

    assert.equal(bare.title, "Results - Acme RC");
    assert.equal(bare.description, "1 driver");
  });
});

/** A store that says how many times a club's index documents were read. */
class CountingStore extends MemoryStore {
  public reads = 0;

  override async getItem(club: string, channel: string, id: string): Promise<StoredDocument | undefined> {
    if (channel === "resultsindex") {
      this.reads++;
    }

    return super.getItem(club, channel, id);
  }
}

async function publish(harness: Harness, path: string, json: unknown): Promise<number> {
  const answer = await call(harness, "POST", `/api/v1/c/acme-rc/publish/${path}`, { token: ACME_TOKEN, json });
  return answer.status;
}

function index(sessions: unknown[]): unknown {
  return { shape: 1, year: "2026", abbreviated: false, drivers: [], raced: [], sessions, days: [] };
}

describe("a session's own address", () => {
  let harness: Harness;
  let store: CountingStore;

  before(async () => {
    store = new CountingStore();
    harness = await start(store as Store);

    assert.equal(await publish(harness, "resultsindex/2026", index([
      { id: "heat-1", day: "2026-09-19", at: "19:40", label: "A Final", track: "Blue Loop", season: "Winter Series", drivers: 8, won: "Alex Kent" },
      { id: "sharp", day: "2026-09-19", label: '"><script>alert(1)</script>', track: "Blue Loop", drivers: 2 },
    ])), 200);
  });

  after(async () => {
    await harness.close();
  });

  it("serves the results page with that session's own card on it", async () => {
    const answer = await call(harness, "GET", "/c/acme-rc/results/heat-1");

    assert.equal(answer.status, 200);
    assert.match(answer.headers["content-type"] ?? "", /text\/html/);

    assert.match(answer.text, /<meta property="og:title" content="A Final - Acme RC">/);
    assert.match(answer.text, /<meta property="og:description" content="Saturday, September 19, 2026 at 19:40 \u00b7 Blue Loop \u00b7 Winter Series \u00b7 8 drivers \u00b7 won by Alex Kent">/);
    assert.match(answer.text, /<meta property="og:url" content="http:\/\/127\.0\.0\.1:\d+\/c\/acme-rc\/results\/heat-1">/);
    // The big card, not the small one: the session has a picture of its
    // own now, and og:image points at it rather than at the club's icon.
    assert.match(answer.text, /<meta name="twitter:card" content="summary_large_image">/);
    assert.match(answer.text, /<meta property="og:image" content="http:\/\/[^"]+\/c\/acme-rc\/results\/heat-1\/card\.png\?v=[0-9a-f]{12}">/);
    assert.match(answer.text, /<meta property="og:image:width" content="1200">/);
    assert.match(answer.text, /<meta property="og:image:height" content="630">/);
    assert.match(answer.text, /<meta name="twitter:image" content="http:\/\/[^"]+\/c\/acme-rc\/results\/heat-1\/card\.png\?v=[0-9a-f]{12}">/);
    assert.match(answer.text, /<title>A Final - Acme RC<\/title>/);

    // It is still the results page, tabs and script and all.
    assert.match(answer.text, /data-club="acme-rc"/);
    assert.match(answer.text, /href="\/c\/acme-rc\/standings"/);
  });

  it("is the ordinary page for a session the index does not name", async () => {
    for (const path of ["/c/acme-rc/results/heat-9", "/c/acme-rc/results/not.a.session-9_x"]) {
      const answer = await call(harness, "GET", path);

      assert.equal(answer.status, 200, path);
      assert.match(answer.text, /<meta property="og:title" content="Acme RC &mdash; Results">/, path);
      assert.equal(answer.text.includes("twitter:card"), false, path);
      assert.match(answer.text, /data-club="acme-rc"/, path);
    }
  });

  it("never sees an id that could be a path", async () => {
    // Refused by the request parser, before any of this: a segment that
    // decodes into a separator is not a session id anybody meant. Plain
    // ".." - encoded or not - never reaches the relay at all, since both
    // fetch and the URL parser resolve it away first, so it is not what
    // is tested here.
    for (const path of ["/c/acme-rc/results/%2e%2e%2fsite", "/c/acme-rc/results/x%2Fy", "/c/acme-rc/results/a%5Cb"]) {
      assert.equal((await call(harness, "GET", path)).status, 400, path);
    }
  });

  it("is the ordinary page for a club that publishes no index at all", async () => {
    const answer = await call(harness, "GET", "/c/brookside/results/heat-1");

    assert.equal(answer.status, 200);
    assert.match(answer.text, /<meta property="og:title" content="Brookside &mdash; Results">/);
  });

  it("puts a session named after a script tag into the page inert", async () => {
    const answer = await call(harness, "GET", "/c/acme-rc/results/sharp");

    // The one that matters: nothing that could run, anywhere on the page.
    assert.equal(answer.text.includes("<script>alert(1)"), false);
    assert.equal(/<script(?![^>]*nonce=)/.test(answer.text), false);

    // And it is still the club's own words, escaped rather than thrown away.
    assert.match(answer.text, /<meta property="og:title" content="&quot;&gt;&lt;script&gt;alert\(1\)&lt;\/script&gt; - Acme RC">/);
    assert.match(answer.text, /<title>&quot;&gt;&lt;script&gt;alert\(1\)&lt;\/script&gt; - Acme RC<\/title>/);
  });

  it("reads the index once for a crawler asking for fifty links at once", async () => {
    // Published again first, so nothing is held: fifty links arriving at
    // a relay that has just been written to is the case this is about,
    // and the one where fifty reads would otherwise happen.
    assert.equal(await publish(harness, "resultsindex/2026", index([
      { id: "heat-1", day: "2026-09-19", at: "19:40", label: "A Final", track: "Blue Loop", season: "Winter Series", drivers: 8, won: "Alex Kent" },
    ])), 200);

    store.reads = 0;

    const links = Array.from({ length: 50 }, (_, i) => call(harness, "GET", `/c/acme-rc/results/heat-${i}`));
    const answers = await Promise.all(links);

    for (const answer of answers) {
      assert.equal(answer.status, 200);
    }

    assert.equal(store.reads, 1, `${store.reads} reads for fifty links`);

    // Held, not re-read, until the club publishes again.
    await call(harness, "GET", "/c/acme-rc/results/heat-1");
    assert.equal(store.reads, 1);
  });

  it("notices a publish rather than serving a card that has gone stale", async () => {
    assert.equal(await publish(harness, "resultsindex/2026", index([
      { id: "heat-1", day: "2026-09-19", label: "A Main", track: "Red Sweeper", drivers: 4 },
    ])), 200);

    const answer = await call(harness, "GET", "/c/acme-rc/results/heat-1");

    assert.match(answer.text, /<meta property="og:title" content="A Main - Acme RC">/);
    assert.match(answer.text, /Red Sweeper/);
  });

  it("never reads a result to draw a card", async () => {
    // The card above is drawn without one of these existing at all.
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/collection/results/heat-1")).status, 404);
    assert.match((await call(harness, "GET", "/c/acme-rc/results/heat-1")).text, /og:title" content="A Main/);
  });
});
