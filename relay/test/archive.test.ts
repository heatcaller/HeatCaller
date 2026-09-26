import assert from "node:assert/strict";
import { describe, it, before, after } from "node:test";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * Reading a long archive.
 *
 * A collection is no longer capped at three hundred documents, so a club
 * keeps every result it ever published. That makes the flat listing the
 * wrong thing for a page to fetch, and these are the two reads that
 * replace it: ?meta=1 to see what is there, ?ids= to fetch the handful
 * about to be drawn.
 *
 * What every test here is really checking is that neither of them looks
 * inside a document. The bodies below are deliberately shapeless.
 */

async function publish(harness: Harness, path: string, json: unknown): Promise<number> {
  const answer = await call(harness, "POST", `/api/v1/c/acme-rc/publish/${path}`, { token: ACME_TOKEN, json });
  return answer.status;
}

describe("a collection's meta listing", () => {
  let harness: Harness;

  before(async () => {
    harness = await start(new MemoryStore());

    for (let i = 0; i < 12; i++) {
      assert.equal(await publish(harness, `results/s${String(i).padStart(2, "0")}`, { anything: i, pad: "x".repeat(500) }), 200);
    }
  });

  after(async () => {
    await harness.close();
  });

  it("gives ids, times and sizes, and no bodies at all", async () => {
    const answer = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results?meta=1");

    assert.equal(answer.status, 200);
    assert.equal(answer.body.meta, true);
    assert.equal(answer.body.items.length, 12);

    for (const item of answer.body.items) {
      assert.equal(typeof item.id, "string");
      assert.equal(typeof item.bytes, "number");
      assert.ok(item.bytes > 0);
      assert.ok(!Number.isNaN(Date.parse(item.publishedAt)));
      assert.equal("body" in item, false, "a meta listing carries no bodies");
    }

    // Far smaller than the listing it replaces - that is the whole point.
    const full = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results");
    assert.ok(answer.text.length * 3 < full.text.length, `${answer.text.length} vs ${full.text.length}`);
  });

  it("is newest first", async () => {
    const ids = (await call(harness, "GET", "/api/v1/c/acme-rc/collection/results?meta=1")).body.items.map(
      (item: { id: string }) => item.id,
    );

    assert.equal(ids[0], "s11");
    assert.equal(ids[ids.length - 1], "s00");
  });

  it("is readable across origins, like every other public read", async () => {
    const answer = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results?meta=1");
    assert.equal(answer.headers["access-control-allow-origin"], "*");
  });

  it("notices a publish, rather than serving a held listing that has gone stale", async () => {
    const before = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results?meta=1");
    assert.equal(before.body.items.length, 12);

    // Held now. The bug this guards is forgetListing clearing the full
    // listing's cache entry and not this one, so a result could land and
    // the meta listing go on saying it was not there.
    assert.equal(await publish(harness, "results/s99", { late: true }), 200);

    const after = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results?meta=1");
    assert.equal(after.body.items.length, 13);
    assert.equal(after.body.items[0].id, "s99");
  });

  it("says nothing about a collection that has nothing in it", async () => {
    const empty = await start(new MemoryStore());

    try {
      const answer = await call(empty, "GET", "/api/v1/c/acme-rc/collection/results?meta=1");
      assert.equal(answer.status, 200);
      assert.deepEqual(answer.body.items, []);
    } finally {
      await empty.close();
    }
  });

  it("is still a 404 for a channel that is not a collection", async () => {
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/collection/state?meta=1")).status, 404);
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/collection/nope?meta=1")).status, 404);
  });
});

describe("fetching documents by id", () => {
  let harness: Harness;

  before(async () => {
    harness = await start(new MemoryStore());

    for (let i = 0; i < 10; i++) {
      assert.equal(await publish(harness, `results/s${String(i).padStart(2, "0")}`, { i }), 200);
    }
  });

  after(async () => {
    await harness.close();
  });

  it("gives back just those, in the order asked for", async () => {
    const answer = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results?ids=s03,s01,s07");

    assert.equal(answer.status, 200);
    assert.deepEqual(
      answer.body.items.map((item: { id: string }) => item.id),
      ["s03", "s01", "s07"],
    );
    assert.deepEqual(
      answer.body.items.map((item: { body: { i: number } }) => item.body.i),
      [3, 1, 7],
    );
    assert.equal(answer.headers["access-control-allow-origin"], "*");
  });

  it("leaves out what it does not have rather than failing the request", async () => {
    // A page that asked for ten and drew nine is right: a result deleted
    // between the index and the fetch is exactly this case.
    const answer = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results?ids=s01,nope,s02");

    assert.equal(answer.status, 200);
    assert.deepEqual(
      answer.body.items.map((item: { id: string }) => item.id),
      ["s01", "s02"],
    );
  });

  it("ignores an id no collection could hold, rather than letting it near the disk", async () => {
    const answer = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results?ids=s01,..%2F..%2Fetc%2Fpasswd,s02");

    assert.equal(answer.status, 200);
    assert.deepEqual(
      answer.body.items.map((item: { id: string }) => item.id),
      ["s01", "s02"],
    );
  });

  it("reads a repeated id once", async () => {
    const answer = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results?ids=s01,s01,s01");

    assert.deepEqual(
      answer.body.items.map((item: { id: string }) => item.id),
      ["s01"],
    );
  });

  it("refuses an empty list and one that is too long", async () => {
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/collection/results?ids=")).status, 400);
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/collection/results?ids=,,")).status, 400);

    const many = Array.from({ length: 51 }, (_, i) => `s${i}`).join(",");
    const refused = await call(harness, "GET", `/api/v1/c/acme-rc/collection/results?ids=${many}`);

    assert.equal(refused.status, 400);
    assert.match(refused.body.error, /at most 50/);

    // Fifty exactly is fine.
    const fifty = Array.from({ length: 50 }, (_, i) => `s${i}`).join(",");
    assert.equal((await call(harness, "GET", `/api/v1/c/acme-rc/collection/results?ids=${fifty}`)).status, 200);
  });

  it("is still a 404 for a channel that is not a collection", async () => {
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/collection/state?ids=s01")).status, 404);
  });
});

describe("an archive far past the old three-hundred cap", () => {
  let harness: Harness;

  before(async () => {
    harness = await start(new MemoryStore());

    // Two seasons of a busy club, near enough.
    for (let i = 0; i < 800; i++) {
      await publish(harness, `results/s${String(i).padStart(4, "0")}`, { i, pad: "r".repeat(400) });
    }
  });

  after(async () => {
    await harness.close();
  });

  it("keeps every one of them", async () => {
    const meta = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results?meta=1");

    assert.equal(meta.body.items.length, 800);
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/collection/results/s0000")).status, 200);
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/collection/results/s0799")).status, 200);
  });

  it("lets a page walk it a day at a time without ever fetching the whole thing", async () => {
    const meta = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results?meta=1");
    const ids: string[] = meta.body.items.map((item: { id: string }) => item.id);

    // What a page actually does: read the index, take the page it is
    // about to draw, ask for exactly that.
    const page = ids.slice(0, 20);
    const answer = await call(harness, "GET", `/api/v1/c/acme-rc/collection/results?ids=${page.join(",")}`);

    assert.equal(answer.status, 200);
    assert.equal(answer.body.items.length, 20);

    // The two reads together are a small fraction of the flat listing.
    const full = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results");
    assert.ok(
      meta.text.length + answer.text.length < full.text.length / 2,
      `${meta.text.length} + ${answer.text.length} vs ${full.text.length}`,
    );
  });
});

describe("the results index's own budget", () => {
  let harness: Harness;
  let store: MemoryStore;

  before(async () => {
    store = new MemoryStore();
    harness = await start(store);
  });

  after(async () => {
    await harness.close();
  });

  // The index is a small note about where a club's data is, not the data.
  // Counted against the quota it became what made room for results: at
  // quota, publishing THIS year's index deleted LAST year's, and a whole
  // season went unreachable on the page while its results sat there still
  // taking the space.
  it("keeps last season's index when the club is at its quota", async () => {
    for (let i = 0; i < 6; i++) {
      assert.equal(await publish(harness, `results/r${i}`, { pad: "r".repeat(200 * 1024) }), 200);
    }

    // The rest of the 50 MB, put straight into the store.
    await store.putSnapshot("acme-rc", "badges", {
      seq: 0,
      publishedAt: "2026-09-20T12:00:00.000Z",
      body: "b".repeat(50 * 1024 * 1024 - 400 * 1024),
    });

    assert.equal(await publish(harness, "resultsindex/2025", { pad: "i".repeat(60 * 1024) }), 200);
    assert.equal(await publish(harness, "resultsindex/2026", { pad: "i".repeat(20 * 1024) }), 200);

    // A race night grows this year's index. Nothing else may pay for it.
    assert.equal(await publish(harness, "resultsindex/2026", { pad: "i".repeat(64 * 1024) }), 200);

    const years = (await call(harness, "GET", "/api/v1/c/acme-rc/collection/resultsindex?meta=1")).body.items.map(
      (item: { id: string }) => item.id,
    );

    assert.deepEqual(years.sort(), ["2025", "2026"], "last season's index survived");

    const results = (await call(harness, "GET", "/api/v1/c/acme-rc/collection/results?meta=1")).body.items;
    assert.equal(results.length, 6, "and no result was evicted to make room for an index");

    // And the index never took the quota it is exempt from.
    assert.equal(await publish(harness, "results/after", { small: 1 }), 200);
  });

  // A year of twenty heats a night over forty-odd nights passes 256 KB
  // somewhere around nine hundred sessions. At the general ceiling the
  // publish was refused, the laptop dropped it, and that year's index
  // froze at the last version that fitted - while results kept publishing
  // perfectly, so nothing looked wrong.
  it("takes a year far past the general 256 KB document ceiling", async () => {
    assert.equal(await publish(harness, "resultsindex/2027", { pad: "i".repeat(300 * 1024) }), 200);
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/collection/resultsindex/2027")).status, 200);

    // Not unbounded, though: past its own budget the oldest year makes way.
    for (const year of [2028, 2029, 2030, 2031]) {
      assert.equal(await publish(harness, `resultsindex/${year}`, { pad: "i".repeat(500 * 1024) }), 200);
    }

    const years = (await call(harness, "GET", "/api/v1/c/acme-rc/collection/resultsindex?meta=1")).body.items;
    assert.ok(years.length < 8, `its own budget still bounds it (${years.length} years held)`);
  });

  it("still refuses one document bigger than even its own ceiling", async () => {
    const refused = await call(harness, "POST", "/api/v1/c/acme-rc/publish/resultsindex/2032", {
      token: ACME_TOKEN,
      raw: JSON.stringify({ pad: "i".repeat(2 * 1024 * 1024) }),
    });

    assert.equal(refused.status, 413);
    assert.match(refused.body.error, /"resultsindex" document may be at most 1048576 bytes/);
  });
});
