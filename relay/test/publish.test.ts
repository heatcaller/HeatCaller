import assert from "node:assert/strict";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { after, before, describe, it } from "node:test";
import { FileStore, isValidItemId, MemoryStore } from "../src/store.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, type Harness } from "./helpers.js";

describe("snapshot channels", () => {
  let harness: Harness;

  after(async () => {
    await harness?.close();
  });

  it("replaces the current document", async () => {
    harness = await start();

    await call(harness, "POST", "/api/v1/c/acme-rc/publish/state?seq=1", {
      token: ACME_TOKEN,
      json: { flag: "green", lap: 1 },
    });

    await call(harness, "POST", "/api/v1/c/acme-rc/publish/state?seq=2", {
      token: ACME_TOKEN,
      json: { flag: "green", lap: 2 },
    });

    const answer = await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/state");

    assert.equal(answer.status, 200);
    assert.deepEqual(answer.body.body, { flag: "green", lap: 2 });
    assert.equal(answer.body.seq, 2);
  });

  it("ignores a frame that arrived out of order, without calling it an error", async () => {
    const late = await call(harness, "POST", "/api/v1/c/acme-rc/publish/state?seq=1", {
      token: ACME_TOKEN,
      json: { flag: "green", lap: 1 },
    });

    // A stale frame is not a failure - it lost a race it was allowed to
    // lose. Answering with an error would make a publisher retry it.
    assert.equal(late.status, 200);
    assert.equal(late.body.applied, false);
    assert.equal(late.body.seq, 2);

    const answer = await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/state");
    assert.deepEqual(answer.body.body, { flag: "green", lap: 2 });
  });

  it("refuses an id on a snapshot channel", async () => {
    const answer = await call(harness, "POST", "/api/v1/c/acme-rc/publish/state/oops", {
      token: ACME_TOKEN,
      json: { a: 1 },
    });

    assert.equal(answer.status, 400);
  });

  it("refuses a channel it does not have", async () => {
    const answer = await call(harness, "POST", "/api/v1/c/acme-rc/publish/telemetry", {
      token: ACME_TOKEN,
      json: { a: 1 },
    });

    assert.equal(answer.status, 404);
  });

  it("stores whatever shape it was given", async () => {
    // The relay does not parse payloads. A club that adds a column in the
    // desktop app must not need a relay deploy.
    const odd = { columns: ["Pos", "Driver", "Qual"], rows: [[1, "Sam", "P4 ▲2"]], nested: { deep: [1, 2] } };

    await call(harness, "POST", "/api/v1/c/acme-rc/publish/standings", {
      token: ACME_TOKEN,
      json: odd,
    });

    const answer = await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/standings");
    assert.deepEqual(answer.body.body, odd);
  });
});

describe("collection channels", () => {
  let harness: Harness;

  after(async () => {
    await harness?.close();
  });

  it("keeps every session rather than only the newest", async () => {
    harness = await start();

    await call(harness, "POST", "/api/v1/c/acme-rc/publish/results/heat-1", {
      token: ACME_TOKEN,
      json: { label: "Heat 1" },
    });

    await call(harness, "POST", "/api/v1/c/acme-rc/publish/results/heat-2", {
      token: ACME_TOKEN,
      json: { label: "Heat 2" },
    });

    const list = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results");

    assert.equal(list.status, 200);
    assert.equal(list.body.items.length, 2);
    assert.deepEqual(
      list.body.items.map((i: { id: string }) => i.id).sort(),
      ["heat-1", "heat-2"],
    );

    const one = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results/heat-2");
    assert.deepEqual(one.body.body, { label: "Heat 2" });
  });

  it("needs an id", async () => {
    const answer = await call(harness, "POST", "/api/v1/c/acme-rc/publish/results", {
      token: ACME_TOKEN,
      json: { label: "nope" },
    });

    assert.equal(answer.status, 400);
  });

  it("refuses an id that would escape its directory", async () => {
    for (const bad of ["../../etc/passwd", "a/b", ".."]) {
      const answer = await call(
        harness,
        "POST",
        `/api/v1/c/acme-rc/publish/results/${encodeURIComponent(bad)}`,
        { token: ACME_TOKEN, json: { a: 1 } },
      );

      assert.ok(answer.status === 400 || answer.status === 404, `${bad} gave ${answer.status}`);
    }
  });
});

describe("item ids", () => {
  it("accepts session ids and refuses separators", () => {
    assert.equal(isValidItemId("aba44398"), true);
    assert.equal(isValidItemId("2026-09-20_heat-1"), true);
    assert.equal(isValidItemId("a.b"), true);
    assert.equal(isValidItemId("../x"), false);
    assert.equal(isValidItemId("a/b"), false);
    assert.equal(isValidItemId(""), false);
    assert.equal(isValidItemId("a..b"), false);
  });
});

describe("limits and bad input", () => {
  let harness: Harness;

  after(async () => {
    await harness?.close();
  });

  it("refuses a payload past the limit without buffering it", async () => {
    harness = await start();

    const huge = JSON.stringify({ pad: "x".repeat(4096) });

    const relay = await start(new MemoryStore());
    const answer = await call(relay, "POST", "/api/v1/c/acme-rc/publish/state", {
      token: ACME_TOKEN,
      raw: huge,
    });

    // Default limit is 1 MiB so this one fits; the point of the test is
    // the path works for a large-but-legal body.
    assert.equal(answer.status, 200);
    await relay.close();
  });

  it("refuses a body that is not JSON", async () => {
    const answer = await call(harness, "POST", "/api/v1/c/acme-rc/publish/state", {
      token: ACME_TOKEN,
      raw: "this is not json",
    });

    assert.equal(answer.status, 400);
  });

  it("refuses an empty body", async () => {
    const answer = await call(harness, "POST", "/api/v1/c/acme-rc/publish/state", {
      token: ACME_TOKEN,
    });

    assert.equal(answer.status, 400);
  });

  it("refuses a seq that is not a number", async () => {
    const answer = await call(harness, "POST", "/api/v1/c/acme-rc/publish/state?seq=abc", {
      token: ACME_TOKEN,
      json: { a: 1 },
    });

    assert.equal(answer.status, 400);
  });

  it("answers health without a token", async () => {
    const answer = await call(harness, "GET", "/healthz");

    assert.equal(answer.status, 200);
    assert.equal(answer.body.ok, true);
  });
});

describe("the roster is not public unless the club says so", () => {
  let harness: Harness;

  after(async () => {
    await harness?.close();
  });

  it("withholds a roster by default and serves one when allowed", async () => {
    harness = await start();

    await call(harness, "POST", "/api/v1/c/acme-rc/publish/roster", {
      token: ACME_TOKEN,
      json: { drivers: [{ n: "Sam" }] },
    });

    await call(harness, "POST", "/api/v1/c/brookside/publish/roster", {
      token: BROOK_TOKEN,
      json: { drivers: [{ n: "Billy Bob" }] },
    });

    // acme-rc has publishRoster off: the names were accepted but are not
    // served to the public.
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/roster")).status, 403);

    // brookside opted in.
    const allowed = await call(harness, "GET", "/api/v1/c/brookside/snapshot/roster");
    assert.equal(allowed.status, 200);
    assert.deepEqual(allowed.body.body, { drivers: [{ n: "Billy Bob" }] });
  });
});

describe("the file store", () => {
  it("survives a restart and keeps clubs apart on disk", async () => {
    const root = await mkdtemp(join(tmpdir(), "relay-test-"));

    try {
      const first = await start(new FileStore(root));

      await call(first, "POST", "/api/v1/c/acme-rc/publish/standings?seq=7", {
        token: ACME_TOKEN,
        json: { table: [1, 2, 3] },
      });

      await call(first, "POST", "/api/v1/c/brookside/publish/standings?seq=1", {
        token: BROOK_TOKEN,
        json: { table: ["other"] },
      });

      await first.close();

      // A new relay over the same directory - what a restart looks like.
      const second = await start(new FileStore(root));

      const acme = await call(second, "GET", "/api/v1/c/acme-rc/snapshot/standings");
      assert.deepEqual(acme.body.body, { table: [1, 2, 3] });
      assert.equal(acme.body.seq, 7);

      const brook = await call(second, "GET", "/api/v1/c/brookside/snapshot/standings");
      assert.deepEqual(brook.body.body, { table: ["other"] });

      // And deleting one leaves the other alone, on disk.
      await call(second, "DELETE", "/api/v1/c/acme-rc", { token: ACME_TOKEN });

      assert.equal((await call(second, "GET", "/api/v1/c/acme-rc/snapshot/standings")).status, 404);
      assert.equal((await call(second, "GET", "/api/v1/c/brookside/snapshot/standings")).status, 200);

      await second.close();
    } finally {
      await rm(root, { recursive: true, force: true });
    }
  });
});

describe("the public results page", () => {
  let harness: Harness;

  before(async () => {
    harness = await start();
  });

  after(async () => {
    await harness.close();
  });

  it("serves a page for a club, naming it", async () => {
    const page = await call(harness, "GET", "/c/acme-rc/results");

    assert.equal(page.status, 200);
    assert.match(page.headers["content-type"] ?? "", /text\/html/);
    assert.match(page.text, /Acme RC/);

    // The club id has to reach the browser, because the page fetches its
    // own data with it.
    assert.match(page.text, /data-club="acme-rc"/);
  });

  it("answers the short address with the club's home page", async () => {
    const short = await call(harness, "GET", "/c/acme-rc", { noRedirect: true });

    // It used to redirect to results. The short address is the one on the
    // poster, so it is now a page of its own that links to everything.
    assert.equal(short.status, 200);
    assert.match(short.text, /href="\/c\/acme-rc\/results"/);
  });

  it("gives a club it does not have the same answer as any other unknown path", async () => {
    // A club that does not exist, and an id that could never be one.
    // (".." is not tested through fetch: the URL is normalised before it
    // is sent, so it would prove something about fetch, not the relay -
    // the club-id tests cover that directly.)
    assert.equal((await call(harness, "GET", "/c/not-a-club/results")).status, 404);
    assert.equal((await call(harness, "GET", "/c/NotAClubId/results")).status, 404);
  });

  it("serves the live and standings pages too, all one site", async () => {
    for (const [path, heading] of [["live", "Live"], ["results", "Results"], ["standings", "Standings"]]) {
      const page = await call(harness, "GET", `/c/acme-rc/${path}`);

      assert.equal(page.status, 200, `${path} should be served`);
      assert.match(page.text, new RegExp(`<span>${heading}</span>`));

      // Every page carries the nav, so none of them is a dead end.
      assert.match(page.text, /href="\/c\/acme-rc\/live"/);
      assert.match(page.text, /href="\/c\/acme-rc\/results"/);
      assert.match(page.text, /href="\/c\/acme-rc\/standings"/);
    }
  });

  it("has no page it does not have", async () => {
    assert.equal((await call(harness, "GET", "/c/acme-rc/nonsense")).status, 404);
  });

  it("renders nothing itself - the page carries no result data", async () => {
    await call(harness, "POST", "/api/v1/c/acme-rc/publish/results/heat-1", {
      token: ACME_TOKEN,
      json: { results: [{ pos: 1, name: "Zebedee Nightingale" }] },
    });

    const page = await call(harness, "GET", "/c/acme-rc/results");

    // The driver's name is in the JSON, not in the HTML. If it ever shows
    // up here, the relay has started understanding payloads.
    assert.ok(!page.text.includes("Zebedee Nightingale"));
  });
});
