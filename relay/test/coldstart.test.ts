import assert from "node:assert/strict";
import { mkdir, mkdtemp, rename, rm, rmdir, writeFile } from "node:fs/promises";
import { Agent, createServer, request as httpRequest, type IncomingMessage, type ServerResponse } from "node:http";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { after, afterEach, before, beforeEach, describe, it } from "node:test";
import { CLUB_FAIL_LIMIT, PIN_GUARD_DOC, PinGuard } from "../src/pinguard.js";
import { pinHash, racerPinHash, Relay } from "../src/server.js";
import { FileStore, MemoryStore, StoreReadError } from "../src/store.js";
import type { StoredDocument } from "../src/types.js";
import { ACME_TOKEN, call, start, tenants, type Harness } from "./helpers.js";

// What the relay is handed when Fly wakes it: every request that queued
// while the machine started, released together into a relay that has read
// nothing from disk yet. These tests reproduce that by holding requests
// at a front server until all of them have arrived, then passing them to
// the relay at once.

const SAM = "0123456789abcdef0123";
const STAFF_PIN = "004821";
const MINUTE = 60 * 1000;
const wait = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

function checkinDoc(enabled = true) {
  return {
    enabled,
    event: "Club Night",
    pinSha256: pinHash("acme-rc", STAFF_PIN),
    drivers: [{ id: SAM, name: "Sam Carter", checkedIn: false }],
  };
}

function stored(body: unknown): StoredDocument {
  return { seq: 1, publishedAt: new Date(Date.UTC(2026, 8, 20, 11)).toISOString(), body };
}

interface Held {
  readonly path: string;
  readonly method: string;
  readonly body?: unknown;
  readonly headers?: Record<string, string>;
}

/** Sends every request, holds them until the last has arrived, then hands them all to the relay at once. Their statuses, in order. */
async function burst(relay: Relay, requests: readonly Held[]): Promise<number[]> {
  const held: [IncomingMessage, ServerResponse][] = [];

  const front = createServer((req, res) => {
    held.push([req, res]);

    if (held.length === requests.length) {
      for (const [q, s] of held) {
        relay.handle(q, s).catch(() => {
          s.writeHead(500).end();
        });
      }
    }
  });

  front.listen(0, "127.0.0.1");
  await new Promise<void>((resolve) => front.once("listening", resolve));

  const address = front.address();
  assert.ok(address !== null && typeof address !== "string");

  const agent = new Agent({ keepAlive: false, maxSockets: Infinity });

  try {
    return await Promise.all(
      requests.map(
        (r) =>
          new Promise<number>((resolve, reject) => {
            const text = r.body === undefined ? "" : JSON.stringify(r.body);
            const req = httpRequest(
              {
                host: "127.0.0.1",
                port: address.port,
                path: r.path,
                method: r.method,
                agent,
                headers: { "content-length": Buffer.byteLength(text), ...(r.headers ?? {}) },
              },
              (res) => {
                res.resume();
                res.on("end", () => resolve(res.statusCode ?? 0));
              },
            );

            req.on("error", reject);
            req.end(text);
          }),
      ),
    );
  } finally {
    agent.destroy();
    await new Promise<void>((resolve) => front.close(() => resolve()));
  }
}

/** A memory store whose first read of each named snapshot is slow, as a cold disk is, and that counts reads. */
class ColdStore extends MemoryStore {
  readonly reads = new Map<string, number>();
  delayMs = 40;

  override async getSnapshot(club: string, channel: string): Promise<StoredDocument | undefined> {
    this.reads.set(channel, (this.reads.get(channel) ?? 0) + 1);
    await wait(this.delayMs);
    return super.getSnapshot(club, channel);
  }
}

function relayOn(store: MemoryStore | FileStore): Relay {
  return new Relay({ store, tenants: tenants(), now: () => new Date(Date.UTC(2026, 8, 20, 12)), log: () => {} });
}

const listPath = "/api/v1/c/acme-rc/checkin/list";

describe("a burst of PIN guesses into a relay that has just woken", () => {
  it("holds one address to its ten, even when the guesses arrive together", async () => {
    const store = new ColdStore();
    await store.putSnapshot("acme-rc", "checkin", stored(checkinDoc()));

    const guesses: Held[] = Array.from({ length: 60 }, (_, i) => ({ path: listPath, method: "POST", body: { pin: String(100000 + i) } }));

    // The right PIN, last of the burst: it must be refused like the rest.
    guesses[59] = { path: listPath, method: "POST", body: { pin: STAFF_PIN } };

    const statuses = await burst(relayOn(store), guesses);

    assert.equal(statuses.filter((s) => s === 403).length, 10, "exactly ten wrong PINs compared");
    assert.equal(statuses.filter((s) => s === 429).length, 50);
    assert.equal(statuses[59], 429, "the right PIN did not get through the burst");
  });

  describe("from many addresses at once", () => {
    before(() => {
      process.env["FLY_APP_NAME"] = "test";
    });

    after(() => {
      delete process.env["FLY_APP_NAME"];
    });

    it("closes the club's list after fifty, however many arrive together", async () => {
      const store = new ColdStore();
      await store.putSnapshot("acme-rc", "checkin", stored(checkinDoc()));

      const guesses: Held[] = Array.from({ length: 80 }, (_, i) => ({
        path: listPath,
        method: "POST",
        body: { pin: String(200000 + i) },
        headers: { "fly-client-ip": `10.2.${Math.floor(i / 5)}.${i % 5}` },
      }));

      guesses[79] = { path: listPath, method: "POST", body: { pin: STAFF_PIN }, headers: { "fly-client-ip": "10.3.0.1" } };

      const relay = relayOn(store);
      const statuses = await burst(relay, guesses);

      assert.equal(statuses.filter((s) => s === 403).length, CLUB_FAIL_LIMIT);
      assert.equal(statuses.filter((s) => s === 429).length, 80 - CLUB_FAIL_LIMIT);
      assert.equal(statuses[79], 429);

      // And the lockout reached the disk, so the next wake-up keeps it.
      const saved = (await store.getSnapshot("acme-rc", PIN_GUARD_DOC))?.body as { lockedUntil: number };
      assert.equal(saved.lockedUntil, Date.UTC(2026, 8, 20, 12) + 10 * MINUTE);
    });
  });
});

describe("a burst of page loads into a relay that has just woken", () => {
  it("reads the check-in and site documents once between them", async () => {
    const store = new ColdStore();
    await store.putSnapshot("acme-rc", "checkin", stored(checkinDoc()));
    await store.putSnapshot("acme-rc", "site", stored({ theme: { colors: { bg: "#123456" } } }));
    store.reads.clear();

    const pages: Held[] = Array.from({ length: 120 }, (_, i) => ({
      path: ["/c/acme-rc/live", "/c/acme-rc/results", "/c/acme-rc/manifest.webmanifest", "/c/acme-rc"][i % 4] as string,
      method: "GET",
    }));

    const statuses = await burst(relayOn(store), pages);

    assert.ok(statuses.every((s) => s === 200));
    assert.equal(store.reads.get("checkin"), 1);
    assert.equal(store.reads.get("site"), 1);
  });

  it("never keeps, or hands a later request, a read that a publish overtook", async () => {
    // The first read of the check-in document is held until released; the
    // publish's own read and any after it are not.
    let release: () => void = () => {};
    const gate = new Promise<void>((resolve) => (release = resolve));
    let checkinReads = 0;

    class Gated extends MemoryStore {
      override async getSnapshot(club: string, channel: string): Promise<StoredDocument | undefined> {
        if (channel === "checkin" && checkinReads++ === 0) {
          const doc = await super.getSnapshot(club, channel);
          await gate;
          return doc;
        }

        return super.getSnapshot(club, channel);
      }
    }

    const store = new Gated();
    await store.putSnapshot("acme-rc", "checkin", stored(checkinDoc(false)));

    const harness = await start(store);

    try {
      // A page load starts the slow read of the closed roster...
      const page = call(harness, "GET", "/c/acme-rc/live");
      await wait(20);

      // ...the laptop opens check-in while it is out...
      const published = await call(harness, "POST", "/api/v1/c/acme-rc/publish/checkin?seq=2", { token: ACME_TOKEN, json: checkinDoc(true) });
      assert.equal(published.status, 200);

      // ...and a phone asking now does not wait for the old read.
      const early = await call(harness, "POST", listPath, { json: { pin: STAFF_PIN } });
      assert.equal(early.status, 200);

      release();
      assert.equal((await page).status, 200);

      // Nor was the old read kept when it finished.
      const later = await call(harness, "POST", listPath, { json: { pin: STAFF_PIN } });
      assert.equal(later.status, 200);
    } finally {
      release();
      await harness.close();
    }
  });
});

describe("a document that cannot be read for a moment", () => {
  let dir: string;
  let harness: Harness;
  let errors: typeof console.error;

  /** Makes a file unreadable - a folder where it should be, which reading fails with EISDIR - and gives back what puts it right. */
  const breakFile = async (path: string): Promise<() => Promise<void>> => {
    await rename(path, `${path}.bak`);
    await mkdir(path);

    return async () => {
      await rmdir(path);
      await rename(`${path}.bak`, path);
    };
  };

  beforeEach(async () => {
    dir = await mkdtemp(join(tmpdir(), "relay-sticky-"));
    errors = console.error;
    console.error = () => {};
  });

  afterEach(async () => {
    console.error = errors;
    await harness?.close();
    await rm(dir, { recursive: true, force: true });
  });

  it("is answered as try again, and the document is back as soon as the file is", async () => {
    const store = new FileStore(dir);
    await store.putSnapshot("acme-rc", "checkin", stored(checkinDoc()));
    harness = await start(store);

    const mend = await breakFile(join(dir, "acme-rc", "snapshot", "checkin.json"));

    // A page read during the blip, and a sign-in.
    assert.equal((await call(harness, "GET", "/c/acme-rc/live")).status, 200);
    assert.equal((await call(harness, "POST", listPath, { json: { pin: STAFF_PIN } })).status, 503);

    await mend();

    const signIn = await call(harness, "POST", listPath, { json: { pin: STAFF_PIN } });
    assert.equal(signIn.status, 200, "not remembered as nothing published");
    assert.equal(signIn.body.mode, "staff");
  });

  it("keeps the club's look once its site file can be read again", async () => {
    const store = new FileStore(dir);
    await store.putSnapshot("acme-rc", "site", stored({ theme: { colors: { bg: "#123456" } } }));
    harness = await start(store);

    const mend = await breakFile(join(dir, "acme-rc", "snapshot", "site.json"));
    const during = await call(harness, "GET", "/c/acme-rc/manifest.webmanifest");
    assert.equal(during.status, 200);
    assert.notEqual(during.body.theme_color, "#123456");

    await mend();

    const afterwards = await call(harness, "GET", "/c/acme-rc/manifest.webmanifest");
    assert.equal(afterwards.body.theme_color, "#123456");
  });

  it("answers a public snapshot, a result and a listing as try again, and does not keep the listing", async () => {
    const store = new FileStore(dir);
    await store.putSnapshot("acme-rc", "standings", stored({ table: [] }));
    await store.putItem("acme-rc", "results", "s1", stored({ n: 1 }));
    harness = await start(store);

    const mendSnapshot = await breakFile(join(dir, "acme-rc", "snapshot", "standings.json"));
    const mendItem = await breakFile(join(dir, "acme-rc", "collection", "results", "s1.json"));

    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/standings")).status, 503);
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/collection/results/s1")).status, 503);
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/collection/results")).status, 503);

    await mendSnapshot();
    await mendItem();

    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/standings")).status, 200);
    const listing = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results");
    assert.equal(listing.status, 200);
    assert.equal(listing.body.items.length, 1);
  });

  it("leaves a result that is not JSON out of the listing, as before", async () => {
    const store = new FileStore(dir);
    await store.putItem("acme-rc", "results", "s1", stored({ n: 1 }));
    await store.putItem("acme-rc", "results", "s2", stored({ n: 2 }));
    await writeFile(join(dir, "acme-rc", "collection", "results", "s2.json"), "{ not json");
    harness = await start(store);

    const listing = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results");
    assert.equal(listing.status, 200);
    assert.deepEqual(
      listing.body.items.map((item: { id: string }) => item.id),
      ["s1"],
    );
  });

  it("lets the laptop publish over a document that cannot be read", async () => {
    const store = new FileStore(dir);
    await store.putSnapshot("acme-rc", "checkin", stored(checkinDoc()));
    harness = await start(store);

    await writeFile(join(dir, "acme-rc", "snapshot", "checkin.json"), "{ half a file");

    const published = await call(harness, "POST", "/api/v1/c/acme-rc/publish/checkin?seq=5", { token: ACME_TOKEN, json: checkinDoc() });
    assert.equal(published.status, 200);
    assert.equal((await call(harness, "POST", listPath, { json: { pin: STAFF_PIN } })).status, 200);
  });
});

describe("the file store", () => {
  let dir: string;
  let errors: typeof console.error;

  beforeEach(async () => {
    dir = await mkdtemp(join(tmpdir(), "relay-store-"));
    errors = console.error;
    console.error = () => {};
  });

  afterEach(async () => {
    console.error = errors;
    await rm(dir, { recursive: true, force: true });
  });

  it("answers missing with undefined, and throws for there-but-unreadable", async () => {
    const store = new FileStore(dir);
    assert.equal(await store.getSnapshot("acme-rc", "state"), undefined);

    await mkdir(join(dir, "acme-rc", "snapshot", "state.json"), { recursive: true });
    await assert.rejects(store.getSnapshot("acme-rc", "state"), (error: unknown) => error instanceof StoreReadError && !error.corrupt);

    await writeFile(join(dir, "acme-rc", "snapshot", "site.json"), "{ nope");
    await assert.rejects(store.getSnapshot("acme-rc", "site"), (error: unknown) => error instanceof StoreReadError && error.corrupt);
  });
});

describe("the wrong-PIN count when its file cannot be read", () => {
  const now = Date.UTC(2026, 8, 20, 12);
  let lines: string[];

  /** A store whose wrong-PIN file cannot be read while `broken` is set, and that notes every write of it. */
  class Flaky extends MemoryStore {
    broken = true;
    writes = 0;

    override async getSnapshot(club: string, channel: string): Promise<StoredDocument | undefined> {
      if (channel === PIN_GUARD_DOC && this.broken) {
        throw new StoreReadError("pinguard.json", false, new Error("EIO: i/o error, read"));
      }

      return super.getSnapshot(club, channel);
    }

    override async putSnapshot(club: string, channel: string, doc: StoredDocument): Promise<void> {
      if (channel === PIN_GUARD_DOC) {
        this.writes++;
      }

      return super.putSnapshot(club, channel, doc);
    }
  }

  beforeEach(() => {
    lines = [];
  });

  it("keeps counting in memory, never writes over the file, and keeps the lockout the file held", async () => {
    const store = new Flaky();
    const lockedOnDisk = now + 2 * 60 * MINUTE;

    await MemoryStore.prototype.putSnapshot.call(store, "acme-rc", PIN_GUARD_DOC, stored({ fails: [], level: 3, lockedUntil: lockedOnDisk }));

    const guard = new PinGuard(store, () => now, (line) => lines.push(line));

    // While it cannot be read, the count in memory decides...
    for (let i = 0; i < CLUB_FAIL_LIMIT; i++) {
      await guard.fail("acme-rc");
    }

    assert.equal(guard.lockedUntilNow("acme-rc"), now + 10 * MINUTE);
    assert.equal(store.writes, 0, "nothing written over a file that could not be read");
    assert.equal(lines.filter((line) => line.includes("could not read")).length, 1);

    // ...and once it can, the file's longer lockout is back, and kept.
    store.broken = false;
    assert.equal(await guard.lockedUntil("acme-rc"), lockedOnDisk);

    await wait(5);
    const saved = (await store.getSnapshot("acme-rc", PIN_GUARD_DOC))?.body as { level: number; lockedUntil: number };
    assert.equal(saved.level, 3);
    assert.equal(saved.lockedUntil, lockedOnDisk);
  });

  it("lets a staff PIN change made meanwhile win over the file", async () => {
    const store = new Flaky();
    await MemoryStore.prototype.putSnapshot.call(store, "acme-rc", PIN_GUARD_DOC, stored({ fails: [], level: 3, lockedUntil: now + 60 * MINUTE }));

    const guard = new PinGuard(store, () => now, () => {});
    await guard.reset("acme-rc");

    store.broken = false;
    assert.equal(await guard.lockedUntil("acme-rc"), 0);

    await wait(5);
    assert.deepEqual((await store.getSnapshot("acme-rc", PIN_GUARD_DOC))?.body, { fails: [], level: 0, lockedUntil: 0 });
  });

  it("starts from none, and mends the file, when it is not JSON", async () => {
    const store = new MemoryStore();
    let corrupt = true;

    class Corrupt extends MemoryStore {
      override async getSnapshot(club: string, channel: string): Promise<StoredDocument | undefined> {
        if (channel === PIN_GUARD_DOC && corrupt) {
          throw new StoreReadError("pinguard.json", true, new SyntaxError("Unexpected token"));
        }

        return store.getSnapshot(club, channel);
      }

      override async putSnapshot(club: string, channel: string, doc: StoredDocument): Promise<void> {
        corrupt = false;
        return store.putSnapshot(club, channel, doc);
      }
    }

    const guard = new PinGuard(new Corrupt(), () => now, () => {});
    await guard.fail("acme-rc");

    assert.deepEqual((await store.getSnapshot("acme-rc", PIN_GUARD_DOC))?.body, { fails: [now], level: 0, lockedUntil: 0 });
  });
});

describe("deleting a club", () => {
  const now = Date.UTC(2026, 8, 20, 12);

  it("forgets its wrong-PIN count, and a save under way cannot put the file back", async () => {
    let slow = false;

    class SlowWrites extends MemoryStore {
      override async putSnapshot(club: string, channel: string, doc: StoredDocument): Promise<void> {
        if (channel === PIN_GUARD_DOC && slow) {
          await wait(40);
        }

        return super.putSnapshot(club, channel, doc);
      }
    }

    const store = new SlowWrites();
    const guard = new PinGuard(store, () => now, () => {});

    for (let i = 0; i < CLUB_FAIL_LIMIT - 1; i++) {
      await guard.fail("acme-rc");
    }

    slow = true;
    const saving = guard.fail("acme-rc");
    const queued = guard.fail("acme-rc");

    await guard.forget("acme-rc", () => store.deleteClub("acme-rc"));
    await Promise.all([saving, queued]);
    await wait(60);

    assert.equal(await store.getSnapshot("acme-rc", PIN_GUARD_DOC), undefined, "no file after the delete");
    assert.equal(await guard.lockedUntil("acme-rc"), 0, "nothing in memory either");
  });

  describe("over http", () => {
    before(() => {
      process.env["FLY_APP_NAME"] = "test";
    });

    after(() => {
      delete process.env["FLY_APP_NAME"];
    });

    // A club with racer PINs and no staff PIN: publishing its roster again
    // after the delete changes no staff PIN, so nothing but the delete
    // itself can have cleared the lockout.
    it("reopens a locked PIN list for the club that publishes again", async () => {
      const RACER_PIN = "5566";
      const racersOnly = {
        enabled: true,
        event: "Club Night",
        drivers: [{ id: SAM, name: "Sam Carter", checkedIn: false, rp: racerPinHash("acme-rc", RACER_PIN) }],
      };
      const harness = await start(new MemoryStore(), undefined, () => new Date(now));

      const guess = async (pin: string, address: string) =>
        (
          await fetch(`${harness.base}${listPath}`, {
            method: "POST",
            headers: { "content-type": "application/json", "fly-client-ip": address },
            body: JSON.stringify({ pin }),
          })
        ).status;

      try {
        assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/publish/checkin?seq=1", { token: ACME_TOKEN, json: racersOnly })).status, 200);

        for (let i = 0; i < CLUB_FAIL_LIMIT; i++) {
          assert.equal(await guess(String(300000 + i), `10.4.${Math.floor(i / 5)}.1`), 403);
        }

        assert.equal(await guess(RACER_PIN, "10.5.0.1"), 429);

        assert.equal((await call(harness, "DELETE", "/api/v1/c/acme-rc", { token: ACME_TOKEN })).status, 200);
        assert.equal(await harness.store.getSnapshot("acme-rc", PIN_GUARD_DOC), undefined);

        assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/publish/checkin?seq=1", { token: ACME_TOKEN, json: racersOnly })).status, 200);
        assert.equal(await guess(RACER_PIN, "10.5.0.1"), 200);
      } finally {
        await harness.close();
      }
    });
  });
});
