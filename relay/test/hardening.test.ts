import assert from "node:assert/strict";
import { request as httpRequest } from "node:http";
import { mkdir, mkdtemp, readdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { after, before, describe, it } from "node:test";
import { loadConfig } from "../src/config.js";
import { addressKey, Limiter, pinHash } from "../src/server.js";
import { FileStore, MemoryStore } from "../src/store.js";
import type { StoredDocument } from "../src/types.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";
import { pageScriptMatch } from "./pagescript.js";

const SAM = "0123456789abcdef0123";
const BILL = "fedcba9876543210fedc";
const NOW = "2026-09-20T12:00:00.000Z";

function checkinDoc(overrides: Record<string, unknown> = {}) {
  return {
    enabled: true,
    pinSha256: pinHash("acme-rc", "4321"),
    event: "Club Night",
    drivers: [
      { id: SAM, name: "Sam Carter", checkedIn: false },
      { id: BILL, name: "Bill Travers", checkedIn: false },
    ],
    ...overrides,
  };
}

async function publish(harness: Harness, path: string, json: unknown): Promise<number> {
  return (await call(harness, "POST", `/api/v1/c/acme-rc/publish/${path}`, { token: ACME_TOKEN, json })).status;
}

/** A memory store that counts what gets written, so a test can say "this did not write". */
class CountingStore extends MemoryStore {
  readonly writes: string[] = [];

  override async putSnapshot(club: string, channel: string, doc: StoredDocument): Promise<void> {
    this.writes.push(channel);
    await super.putSnapshot(club, channel, doc);
  }
}

describe("collection limits", () => {
  let harness: Harness;

  before(async () => {
    harness = await start();
  });

  after(async () => {
    await harness.close();
  });

  it("refuses one result bigger than 256 KB with a 413 that says so", async () => {
    const answer = await call(harness, "POST", "/api/v1/c/acme-rc/publish/results/big", {
      token: ACME_TOKEN,
      raw: JSON.stringify({ pad: "x".repeat(300 * 1024) }),
    });

    assert.equal(answer.status, 413);
    assert.match(answer.body.error, /"results" document may be at most 262144 bytes/);

    // A snapshot of the same size is still fine: the general limit is 1 MiB.
    assert.equal(await publish(harness, "badges", { pad: "x".repeat(300 * 1024) }), 200);
  });

  // Results were capped at three hundred and are not any more: a club's
  // history is the point of publishing it, and a driver looking up last
  // season on their phone has no laptop to fall back on. The quota is
  // what bounds it now - see "the club quota" below, which still holds.
  it("keeps every result past the old three-hundred cap", async () => {
    for (let i = 0; i < 305; i++) {
      assert.equal(await publish(harness, `results/s${String(i).padStart(3, "0")}`, { i }), 200);
    }

    const list = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results");
    const ids: string[] = list.body.items.map((item: { id: string }) => item.id);

    assert.equal(ids.length, 305);
    assert.equal(ids.includes("s000"), true, "the oldest is still there");
    assert.equal(ids.includes("s304"), true);
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/collection/results/s000")).status, 200);
  });

  it("serves a fresh listing after a publish and after a delete, not the cached one", async () => {
    const first = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results");
    const again = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results");
    assert.equal(again.text, first.text, "the same listing twice");

    await publish(harness, "results/s304", { i: "changed" });

    const changed = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results");
    const item = changed.body.items.find((x: { id: string }) => x.id === "s304");
    assert.equal(item.body.i, "changed");
    assert.equal(changed.headers["access-control-allow-origin"], "*");

    assert.equal((await call(harness, "DELETE", "/api/v1/c/acme-rc", { token: ACME_TOKEN })).status, 200);
    assert.deepEqual((await call(harness, "GET", "/api/v1/c/acme-rc/collection/results")).body, { items: [] });
  });
});

describe("the club quota", () => {
  let harness: Harness;
  const store = new MemoryStore();

  before(async () => {
    harness = await start(store);
  });

  after(async () => {
    await harness.close();
  });

  it("forgets the oldest results to make room, and refuses only what cannot fit at all", async () => {
    const result = { pad: "r".repeat(200 * 1024) };

    await publish(harness, "results/old1", result);
    await publish(harness, "results/old2", result);

    // Most of the 50 MB taken by one snapshot, put straight into the store
    // - the relay's own limit would never let a publish send this.
    const filler = 50 * 1024 * 1024 - 300 * 1024;
    await store.putSnapshot("acme-rc", "badges", { seq: 0, publishedAt: NOW, body: "b".repeat(filler) });

    // Only one result fits beside the filler, so both old ones go, oldest first.
    assert.equal(await publish(harness, "results/new1", result), 200);

    const ids = (await call(harness, "GET", "/api/v1/c/acme-rc/collection/results")).body.items.map((i: { id: string }) => i.id);
    assert.deepEqual(ids.sort(), ["new1"]);

    // Filled past the quota by snapshots alone: nothing a result can free.
    await store.putSnapshot("acme-rc", "badges", { seq: 0, publishedAt: NOW, body: "b".repeat(50 * 1024 * 1024) });

    const refused = await call(harness, "POST", "/api/v1/c/acme-rc/publish/results/new2", { token: ACME_TOKEN, json: { a: 1 } });
    assert.equal(refused.status, 507);
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/collection/results/new1")).status, 200, "nothing deleted for a refusal");

    // Live frames are never refused for space.
    assert.equal(await publish(harness, "state", { state: "Racing" }), 200);
  });
});

describe("a long collection on disk", () => {
  let harness: Harness;
  let dir: string;

  before(async () => {
    dir = await mkdtemp(join(tmpdir(), "relay-cap-"));
    harness = await start(new FileStore(dir));
  });

  after(async () => {
    await harness.close();
    await rm(dir, { recursive: true, force: true });
  });

  it("keeps every file, the oldest included", async () => {
    for (let i = 0; i < 302; i++) {
      assert.equal(await publish(harness, `results/d${String(i).padStart(3, "0")}`, { i }), 200);
    }

    const files = await readdir(join(dir, "acme-rc", "collection", "results"));
    assert.equal(files.filter((f) => f.endsWith(".json")).length, 302);
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/collection/results/d000")).status, 200);
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/collection/results/d301")).status, 200);
  });
});

describe("the file store's temp files", () => {
  let dir: string;

  before(async () => {
    dir = await mkdtemp(join(tmpdir(), "relay-tmp-"));
  });

  after(async () => {
    await rm(dir, { recursive: true, force: true });
  });

  it("removes its temp file when the write fails", async () => {
    const store = new FileStore(dir);

    // A directory where the document should go makes the rename fail.
    await mkdir(join(dir, "acme-rc", "snapshot", "state.json"), { recursive: true });

    await assert.rejects(store.putSnapshot("acme-rc", "state", { seq: 1, publishedAt: NOW, body: {} }));

    const left = await readdir(join(dir, "acme-rc", "snapshot"));
    assert.deepEqual(left, ["state.json"], "no .tmp left behind");
  });

  it("sweeps stale temp files and nothing else", async () => {
    const store = new FileStore(dir);
    const folder = join(dir, "brookside", "collection", "results");

    await mkdir(folder, { recursive: true });
    await writeFile(join(folder, "a.json.123.abc.tmp"), "{");
    await writeFile(join(folder, "a.json"), "{}");

    assert.equal(await store.sweepTemp(), 1);
    assert.deepEqual(await readdir(folder), ["a.json"]);
  });

  it("gives two writes at once their own temp files", async () => {
    const store = new FileStore(dir);

    await Promise.all(
      Array.from({ length: 20 }, (_, i) => store.putSnapshot("acme-rc", "roster", { seq: i, publishedAt: NOW, body: { i } })),
    );

    const snap = await store.getSnapshot("acme-rc", "roster");
    assert.equal(typeof snap?.seq, "number");
    assert.equal((await readdir(join(dir, "acme-rc", "snapshot"))).some((f) => f.endsWith(".tmp")), false);
  });
});

describe("tap queue", () => {
  let harness: Harness;
  const store = new CountingStore();

  before(async () => {
    harness = await start(store);
    await publish(harness, "checkin?seq=1", checkinDoc());
  });

  after(async () => {
    await harness.close();
  });

  it("keeps one waiting tap per driver, the newest", async () => {
    await call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: SAM, wanted: true } });
    await call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: BILL, wanted: true } });
    await call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: SAM, wanted: false } });

    const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/inbox", { token: ACME_TOKEN });
    const items = inbox.body.items as { n: number; id: string; wanted: boolean }[];

    assert.equal(items.length, 2);
    const sam = items.find((i) => i.id === SAM);
    const bill = items.find((i) => i.id === BILL);
    assert.equal(sam?.wanted, false);
    assert.ok(sam !== undefined && bill !== undefined && sam.n > bill.n, "the replacement has a newer number");
  });

  it("does not rewrite the roster when a tap changes nothing in it", async () => {
    await call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: BILL, wanted: true } });
    store.writes.length = 0;

    // Bill is already shown as checked in.
    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: BILL, wanted: true } })).status, 200);
    assert.deepEqual(store.writes, ["inbox"]);
  });

  it("allows six taps a minute per driver", async () => {
    // Bill has had three so far in this minute (the clock is fixed).
    const statuses: number[] = [];

    for (let i = 0; i < 4; i++) {
      statuses.push((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: BILL, wanted: i % 2 === 0 } })).status);
    }

    assert.deepEqual(statuses, [200, 200, 200, 429]);
  });

  it("refuses a tap with 503 when the queue is full, rather than dropping one", async () => {
    const items = Array.from({ length: 500 }, (_, i) => ({
      n: i + 1,
      id: `${String(i).padStart(3, "0")}${"b".repeat(17)}`,
      wanted: true,
      atUtc: NOW,
    }));

    await store.putSnapshot("acme-rc", "inbox", { seq: 0, publishedAt: NOW, body: { next: 501, items } });

    const answer = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: SAM, wanted: true } });
    assert.equal(answer.status, 503);

    const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/inbox", { token: ACME_TOKEN });
    assert.equal(inbox.body.items.length, 500);
    assert.equal(inbox.body.items[0].n, 1, "the oldest waiting tap is still there");
  });

  it("survives an inbox with rubbish in it", async () => {
    await store.putSnapshot("acme-rc", "inbox", { seq: 0, publishedAt: NOW, body: { next: 7, items: [null, 3, { n: 1 }] } });

    const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/inbox", { token: ACME_TOKEN });
    assert.equal(inbox.status, 200);
    assert.deepEqual(inbox.body.items, []);
  });
});

describe("check-in documents that are not objects", () => {
  let harness: Harness;

  before(async () => {
    harness = await start();
  });

  after(async () => {
    await harness.close();
  });

  for (const body of [null, [], 7, "text"]) {
    it(`treats a check-in body of ${JSON.stringify(body)} as closed, not as a crash`, async () => {
      await call(harness, "POST", "/api/v1/c/acme-rc/publish/checkin", { token: ACME_TOKEN, raw: JSON.stringify(body) });

      assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/one", { json: { id: SAM } })).status, 409);
      assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: SAM, wanted: true } })).status, 409);
    });
  }

  it("refuses a request body that is null or an array", async () => {
    await publish(harness, "checkin", checkinDoc());

    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/one", { raw: "null" })).status, 400);
    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/one", { raw: "[]" })).status, 400);
    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/inbox/ack", { token: ACME_TOKEN, raw: "null" })).status, 400);
  });
});

describe("check-in reads and taps count separately", () => {
  let harness: Harness;

  before(async () => {
    harness = await start();
    await publish(harness, "checkin", checkinDoc());
  });

  after(async () => {
    await harness.close();
  });

  it("lets a paddock's polling go on without using up the taps, up to 600 reads a minute", async () => {
    for (let i = 0; i < 300; i++) {
      assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "4321" } })).status, 200);
    }

    for (let i = 0; i < 300; i++) {
      assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/one", { json: { id: SAM } })).status, 200);
    }

    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/one", { json: { id: SAM } })).status, 429);
    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: SAM, wanted: true } })).status, 200);
  });
});

describe("addresses for rate limits", () => {
  it("keys IPv6 by its /64 and unwraps IPv4-mapped addresses", () => {
    assert.equal(addressKey("::ffff:192.0.2.7"), "192.0.2.7");
    assert.equal(addressKey("192.0.2.7"), "192.0.2.7");
    assert.equal(addressKey("2001:db8:1:2:aaaa:bbbb:cccc:dddd"), "2001:db8:1:2::/64");
    assert.equal(addressKey("2001:DB8:1:2::1"), "2001:db8:1:2::/64");
    assert.equal(addressKey("2001:0db8:0001:0002::1%eth0"), "2001:db8:1:2::/64");
    assert.equal(addressKey("2001:db8::1"), "2001:db8:0:0::/64");
    assert.equal(addressKey("::1"), "0:0:0:0::/64");
    assert.equal(addressKey("fe80::1:2:3:4"), "fe80:0:0:0::/64");
    assert.equal(addressKey("64:ff9b::192.0.2.1"), "64:ff9b:0:0::/64");
    assert.equal(addressKey("unknown"), "unknown");
  });
});

describe("PIN limits per /64 on Fly", () => {
  let harness: Harness;

  before(async () => {
    process.env["FLY_APP_NAME"] = "test";
    harness = await start();
    await publish(harness, "checkin", checkinDoc());
  });

  after(async () => {
    delete process.env["FLY_APP_NAME"];
    await harness.close();
  });

  it("counts every address in one /64 as one", async () => {
    for (let i = 0; i < 10; i++) {
      const answer = await fetch(`${harness.base}/api/v1/c/acme-rc/checkin/list`, {
        method: "POST",
        headers: { "content-type": "application/json", "fly-client-ip": `2001:db8:5:6::${i + 1}` },
        body: JSON.stringify({ pin: `9${i}` }),
      });
      await answer.arrayBuffer();
      assert.equal(answer.status, 403);
    }

    const blocked = await fetch(`${harness.base}/api/v1/c/acme-rc/checkin/list`, {
      method: "POST",
      headers: { "content-type": "application/json", "fly-client-ip": "2001:db8:5:6:ffff::1" },
      body: JSON.stringify({ pin: "4321" }),
    });
    await blocked.arrayBuffer();
    assert.equal(blocked.status, 429);
  });
});

describe("the check-in page's handling of refusals", () => {
  let harness: Harness;
  let script = "";
  let loadList = "";

  before(async () => {
    harness = await start();
    const page = await call(harness, "GET", "/c/acme-rc/checkin");
    script = pageScriptMatch(page.text)?.[1] ?? "";
    loadList = /async function loadList\(\) \{[\s\S]*?\n\}/.exec(script)?.[0] ?? "";
  });

  after(async () => {
    await harness.close();
  });

  it("forgets the PIN only on a 403", () => {
    assert.ok(loadList.length > 0);
    assert.equal(loadList.split("setPin('')").length - 1, 1, "one place clears the PIN");
    assert.match(loadList, /if \(answer\.status === 403\) \{\s*setPin\(''\);/);
    assert.match(loadList, /if \(answer\.status !== 200\) \{ busy\(answer\); return; \}/);
  });

  it("says to wait on a 429 and polls every ten seconds", () => {
    assert.match(script, /answer\.status === 429\s*\?\s*'Too many attempts/);
    assert.match(script, /poll\(BADGE \? loadOne : loadList, 10000\);/);
  });
});

describe("request edges", () => {
  let harness: Harness;

  before(async () => {
    harness = await start();
  });

  after(async () => {
    await harness.close();
  });

  it("answers an oversized body with 413, not a reset, when its length is declared", async () => {
    const answer = await call(harness, "POST", "/api/v1/c/acme-rc/publish/state", {
      token: ACME_TOKEN,
      raw: "x".repeat(3 * 1024 * 1024),
    });

    assert.equal(answer.status, 413);
    assert.equal(answer.headers["connection"], "close");
    assert.match(answer.body.error, /larger than this relay accepts/);
  });

  it("answers an oversized chunked body with 413 too", async () => {
    const url = new URL(harness.base);

    const { status, connection } = await new Promise<{ status: number; connection: string }>((resolve, reject) => {
      let answered = false;
      let stopped = false;

      const req = httpRequest(
        {
          host: url.hostname,
          port: url.port,
          method: "POST",
          path: "/api/v1/c/acme-rc/publish/state",
          headers: { authorization: `Bearer ${ACME_TOKEN}` },
        },
        (res) => {
          answered = true;
          res.resume();
          res.on("end", () => resolve({ status: res.statusCode ?? 0, connection: String(res.headers["connection"]) }));
        },
      );

      // The relay answers 413 and closes the connection while this side is
      // still sending. Linux lets the rest of the upload fall on the floor
      // quietly; Windows fails the next write (ECANCELED / ECONNRESET /
      // EPIPE). That is the refusal working, not a fault - so a write error
      // only fails the test if no answer arrives shortly after it.
      req.on("error", (error: NodeJS.ErrnoException) => {
        stopped = true;

        if (answered) {
          return;
        }

        if (["ECANCELED", "ECONNRESET", "EPIPE"].includes(error.code ?? "")) {
          setTimeout(() => {
            if (!answered) {
              reject(error);
            }
          }, 2000);
          return;
        }

        reject(error);
      });

      let sent = 0;
      const pump = (): void => {
        while (!stopped && sent < 3 * 1024 * 1024) {
          sent += 16 * 1024;

          if (!req.write("y".repeat(16 * 1024))) {
            req.once("drain", pump);
            return;
          }
        }

        req.end();
      };

      pump();
    });

    assert.equal(status, 413);
    assert.equal(connection, "close");

    // And the relay is still answering.
    assert.equal((await call(harness, "GET", "/healthz")).status, 200);
  });

  it("answers a check-in body past its small limit with 400 rather than a reset", async () => {
    const answer = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { raw: JSON.stringify({ pin: "x".repeat(8000) }) });
    assert.equal(answer.status, 400);
  });

  it("answers a path that does not decode with 400, not 500", async () => {
    for (const path of ["/c/%E0%A4%A", "/api/v1/c/acme-rc/snapshot/%ZZ", "/%"]) {
      assert.equal((await call(harness, "GET", path)).status, 400, path);
    }
  });

  it("answers HEAD like GET, without the body", async () => {
    for (const path of ["/healthz", "/c/acme-rc", "/c/acme-rc/results", "/api/v1/c/acme-rc/collection/results"]) {
      const answer = await call(harness, "HEAD", path);

      assert.equal(answer.status, 200, path);
      assert.equal(answer.text, "", path);
      assert.ok(Number(answer.headers["content-length"]) > 0, path);
    }

    assert.equal((await call(harness, "HEAD", "/nope")).status, 404);
  });

  it("answers OPTIONS with 204 and what is allowed", async () => {
    const answer = await call(harness, "OPTIONS", "/c/acme-rc");

    assert.equal(answer.status, 204);
    assert.match(answer.headers["allow"] ?? "", /GET, HEAD, POST, DELETE, OPTIONS/);
    assert.equal(answer.headers["x-content-type-options"], "nosniff");
  });
});

describe("the limiter's memory", () => {
  it("drops expired keys first and never forgets a live one wholesale", () => {
    let now = 0;
    const limiter = new Limiter(2, 1000, () => now, 10);

    limiter.hit("guesser");
    limiter.hit("guesser");
    assert.equal(limiter.blocked("guesser"), true);

    // Old keys whose window has passed...
    for (let i = 0; i < 8; i++) {
      limiter.hit(`old${i}`);
    }

    now = 1500;
    limiter.hit("guesser");
    limiter.hit("guesser");

    // ...then a flood of fresh ones pushing it over the cap.
    for (let i = 0; i < 5; i++) {
      limiter.hit(`new${i}`);
    }

    assert.ok(limiter.size <= 10);
    assert.equal(limiter.blocked("guesser"), true, "the guesser is still counted");
  });

  it("evicts the least recently used when nothing has expired", () => {
    const limiter = new Limiter(1, 60_000, () => 0, 10);

    for (let i = 0; i < 11; i++) {
      limiter.hit(`k${i}`);
    }

    assert.equal(limiter.size, 9);
    assert.equal(limiter.blocked("k0"), false, "the oldest went");
    assert.equal(limiter.blocked("k10"), true, "the newest stayed");
  });
});

describe("config limits", () => {
  const club = { club: "acme-rc", name: "Acme RC", tokenSha256: "A".repeat(64) };
  const load = (extra: Record<string, unknown>) =>
    loadConfig("./nowhere.json", { RELAY_CONFIG_JSON: JSON.stringify({ clubs: [club], ...extra }) });

  it("lower-cases a token hash", async () => {
    assert.equal((await load({})).tenants[0]?.tokenSha256, "a".repeat(64));
  });

  it("takes maxBodyBytes only as a whole number from 1 KiB to 16 MiB", async () => {
    assert.equal((await load({})).maxBodyBytes, 1024 * 1024);
    assert.equal((await load({ maxBodyBytes: 2048 })).maxBodyBytes, 2048);

    for (const bad of [0, 1023, 16 * 1024 * 1024 + 1, 1.5, "1048576", -1]) {
      await assert.rejects(load({ maxBodyBytes: bad }), /maxBodyBytes/, String(bad));
    }
  });
});
