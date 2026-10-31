import assert from "node:assert/strict";
import { after, afterEach, before, beforeEach, describe, it } from "node:test";
import { CLUB_FAIL_LIMIT, LOCK_STEPS_MS, PIN_GUARD_DOC, PinGuard, QUIET_RESET_MS } from "../src/pinguard.js";
import { pinHash, racerPinHash } from "../src/server.js";
import { MemoryStore, type ItemSize } from "../src/store.js";
import type { StoredDocument } from "../src/types.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

const SAM = "0123456789abcdef0123";
const BILL = "fedcba9876543210fedc";
const SAM_PIN = "123456";
const MINUTE = 60 * 1000;
const HOUR = 60 * MINUTE;

const wait = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

function checkinDoc(staffPin = "4321") {
  return {
    enabled: true,
    pinSha256: pinHash("acme-rc", staffPin),
    event: "Club Night",
    drivers: [
      { id: SAM, name: "Sam Carter", checkedIn: false, rp: racerPinHash("acme-rc", SAM_PIN) },
      { id: BILL, name: "Bill Travers", checkedIn: false },
    ],
  };
}

/** A result document of about `bytes` bytes, published at minute `n` of the day. */
function result(n: number, bytes: number): StoredDocument {
  return {
    seq: 0,
    publishedAt: new Date(Date.UTC(2026, 8, 20, 0, n)).toISOString(),
    body: { n, pad: "r".repeat(bytes) },
  };
}

/** A memory store whose reads of a collection take a little while, and that counts them. */
class SlowStore extends MemoryStore {
  sizeCalls = 0;
  itemReads: string[] = [];
  delayMs = 20;

  override async itemSizes(club: string, channel: string): Promise<readonly ItemSize[]> {
    this.sizeCalls++;
    await wait(this.delayMs);
    return super.itemSizes(club, channel);
  }

  override async getItem(club: string, channel: string, id: string): Promise<StoredDocument | undefined> {
    this.itemReads.push(club);
    await wait(1);
    return super.getItem(club, channel, id);
  }
}

describe("collection listings under load", () => {
  it("lists the newest documents up to 8 MB and says how many were left out", async () => {
    const store = new MemoryStore();

    // Sixty documents of 200 KB: 12 MB in all, too much for one listing.
    for (let i = 0; i < 60; i++) {
      await store.putItem("acme-rc", "results", `s${String(i).padStart(2, "0")}`, result(i, 200 * 1024));
    }

    const harness = await start(store);

    try {
      const answer = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results");
      const ids: string[] = answer.body.items.map((item: { id: string }) => item.id);

      assert.equal(answer.status, 200);
      assert.ok(Buffer.byteLength(answer.text) <= 8 * 1024 * 1024, `listing is ${answer.text.length} bytes`);
      assert.ok(ids.length > 30 && ids.length < 60, `${ids.length} listed`);
      assert.equal(ids[ids.length - 1], "s59", "the newest is always there");
      assert.equal(ids.includes("s00"), false, "the oldest went first");
      assert.equal(answer.body.omitted, 60 - ids.length);
      assert.deepEqual(ids, [...ids].sort(), "still in publishing order");

      // Everything left out of the listing can still be fetched on its own.
      assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/collection/results/s00")).status, 200);
    } finally {
      await harness.close();
    }
  });

  it("leaves a document too big for any listing out, without reading it", async () => {
    const store = new SlowStore();

    await store.putItem("acme-rc", "results", "small", result(1, 100));
    await store.putItem("acme-rc", "results", "huge", result(2, 600 * 1024));

    const harness = await start(store);

    try {
      const answer = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results");

      assert.deepEqual(answer.body.items.map((item: { id: string }) => item.id), ["small"]);
      assert.equal(answer.body.omitted, 1);
      assert.equal(store.itemReads.length, 1, "only the small one was read");
    } finally {
      await harness.close();
    }
  });

  it("is exactly what it always was when nothing is left out", async () => {
    const store = new MemoryStore();
    await store.putItem("acme-rc", "results", "b", result(2, 10));
    await store.putItem("acme-rc", "results", "a", result(1, 10));

    const harness = await start(store);

    try {
      const answer = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results");
      const a = await store.getItem("acme-rc", "results", "a");
      const b = await store.getItem("acme-rc", "results", "b");

      assert.equal(
        answer.text,
        JSON.stringify({
          items: [
            { id: "a", publishedAt: a?.publishedAt, body: a?.body },
            { id: "b", publishedAt: b?.publishedAt, body: b?.body },
          ],
        }),
      );
    } finally {
      await harness.close();
    }
  });

  it("builds a listing once for many requests arriving together", async () => {
    const store = new SlowStore();

    for (let i = 0; i < 5; i++) {
      await store.putItem("acme-rc", "results", `s${i}`, result(i, 1000));
    }

    const harness = await start(store);

    try {
      const answers = await Promise.all(
        Array.from({ length: 10 }, () => call(harness, "GET", "/api/v1/c/acme-rc/collection/results")),
      );

      assert.equal(store.sizeCalls, 1, "one build");
      assert.equal(store.itemReads.length, 5, "each document read once");

      for (const answer of answers) {
        assert.equal(answer.status, 200);
        assert.equal(answer.text, answers[0]?.text);
      }
    } finally {
      await harness.close();
    }
  });

  it("does not hand a request made after a publish the listing from before it", async () => {
    const store = new SlowStore();
    store.delayMs = 80;
    await store.putItem("acme-rc", "results", "old", result(1, 100));

    const harness = await start(store);

    try {
      const early = call(harness, "GET", "/api/v1/c/acme-rc/collection/results");
      await wait(20);

      const published = await call(harness, "POST", "/api/v1/c/acme-rc/publish/results/new", { token: ACME_TOKEN, json: { n: 2 } });
      assert.equal(published.status, 200);

      const late = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results");
      assert.deepEqual(late.body.items.map((item: { id: string }) => item.id).sort(), ["new", "old"]);

      await early;

      // And the build that started before the publish was not kept.
      const again = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results");
      assert.deepEqual(again.body.items.map((item: { id: string }) => item.id).sort(), ["new", "old"]);
    } finally {
      await harness.close();
    }
  });

  it("builds one club's listing at a time", async () => {
    const store = new SlowStore();

    for (let i = 0; i < 4; i++) {
      await store.putItem("acme-rc", "results", `a${i}`, result(i, 100));
      await store.putItem("brookside", "results", `b${i}`, result(i, 100));
    }

    const harness = await start(store);

    try {
      await Promise.all([
        call(harness, "GET", "/api/v1/c/acme-rc/collection/results"),
        call(harness, "GET", "/api/v1/c/brookside/collection/results"),
      ]);

      // All of one club's reads, then all of the other's - never interleaved.
      const reads = store.itemReads.join(",");
      assert.ok(
        reads === "acme-rc,acme-rc,acme-rc,acme-rc,brookside,brookside,brookside,brookside" ||
          reads === "brookside,brookside,brookside,brookside,acme-rc,acme-rc,acme-rc,acme-rc",
        reads,
      );
    } finally {
      await harness.close();
    }
  });

  it("counts the cache's size truthfully when a listing replaces its own", async () => {
    const store = new SlowStore();
    const harness = await start(store);

    try {
      for (let round = 0; round < 5; round++) {
        await call(harness, "POST", `/api/v1/c/acme-rc/publish/results/s${round}`, { token: ACME_TOKEN, json: { x: "y".repeat(1000) } });
        await Promise.all(Array.from({ length: 10 }, () => call(harness, "GET", "/api/v1/c/acme-rc/collection/results")));

        const cache = harness.relay.listingCache;
        assert.equal(cache.counted, cache.held, `round ${round}`);
        assert.equal(cache.entries, 1);
      }
    } finally {
      await harness.close();
    }
  });
});

describe("making room in a collection", () => {
  it("keeps the oldest results when writing the new one fails", async () => {
    class FullDisk extends MemoryStore {
      full = false;

      override async putItem(club: string, channel: string, id: string, doc: StoredDocument): Promise<void> {
        if (this.full) {
          throw Object.assign(new Error("ENOSPC: no space left on device"), { code: "ENOSPC" });
        }

        await super.putItem(club, channel, id, doc);
      }
    }

    const store = new FullDisk();

    // Driven by the QUOTA, which is what makes room now: results used to
    // be capped at three hundred documents and are not any more, so a
    // short collection no longer evicts anything however long it gets.
    // Most of the 50 MB is taken by one snapshot put straight into the
    // store - the relay's own limit would never let a publish send it -
    // leaving room for about one result.
    const filler = 50 * 1024 * 1024 - 300 * 1024;
    await store.putSnapshot("acme-rc", "badges", { seq: 0, publishedAt: new Date(Date.UTC(2026, 8, 20)).toISOString(), body: "b".repeat(filler) });

    for (let i = 0; i < 2; i++) {
      await store.putItem("acme-rc", "results", `s${String(i).padStart(3, "0")}`, result(i, 100 * 1024));
    }

    const harness = await start(store);

    try {
      store.full = true;
      const refused = await call(harness, "POST", "/api/v1/c/acme-rc/publish/results/extra", { token: ACME_TOKEN, json: { pad: "n".repeat(100 * 1024) } });
      assert.equal(refused.status, 500);

      const held = await store.itemSizes("acme-rc", "results");
      assert.equal(held.length, 2);
      assert.ok(await store.getItem("acme-rc", "results", "s000"), "the oldest is still there");

      // Once the disk has room, the oldest makes way as it always did.
      store.full = false;
      const accepted = await call(harness, "POST", "/api/v1/c/acme-rc/publish/results/extra", { token: ACME_TOKEN, json: { pad: "n".repeat(100 * 1024) } });
      assert.equal(accepted.status, 200);
      assert.equal(await store.getItem("acme-rc", "results", "s000"), undefined, "the oldest made way");
      assert.ok(await store.getItem("acme-rc", "results", "extra"));
    } finally {
      await harness.close();
    }
  });
});

describe("the club-wide PIN lockout", () => {
  let now = 0;
  let store: MemoryStore;
  let lines: string[];
  let guard: PinGuard;

  const failTimes = async (count: number) => {
    for (let i = 0; i < count; i++) {
      await guard.fail("acme-rc");
    }
  };

  beforeEach(() => {
    now = Date.UTC(2026, 8, 20, 12);
    store = new MemoryStore();
    lines = [];
    guard = new PinGuard(store, () => now, (line) => lines.push(line));
  });

  it("closes for longer each time, up to twelve hours", async () => {
    await failTimes(CLUB_FAIL_LIMIT - 1);
    assert.equal(await guard.lockedUntil("acme-rc"), 0);

    const expected = [10 * MINUTE, 30 * MINUTE, 2 * HOUR, 6 * HOUR, 12 * HOUR, 12 * HOUR];

    await failTimes(1);

    for (const [index, length] of expected.entries()) {
      assert.equal(await guard.lockedUntil("acme-rc"), now + length, `lockout ${index + 1}`);

      now += length;
      assert.equal(await guard.lockedUntil("acme-rc"), 0, "open again once it has run");

      await failTimes(CLUB_FAIL_LIMIT);
    }
  });

  it("forgets the fifty once ten minutes have passed", async () => {
    await failTimes(CLUB_FAIL_LIMIT - 1);
    now += 10 * MINUTE + 1;
    await failTimes(CLUB_FAIL_LIMIT - 1);
    assert.equal(await guard.lockedUntil("acme-rc"), 0);
  });

  it("starts from the short lockout again after a quiet day", async () => {
    await failTimes(CLUB_FAIL_LIMIT);
    now += LOCK_STEPS_MS[0] as number;
    await failTimes(CLUB_FAIL_LIMIT);
    assert.equal(await guard.lockedUntil("acme-rc"), now + 30 * MINUTE);

    now += 30 * MINUTE + QUIET_RESET_MS;
    await failTimes(CLUB_FAIL_LIMIT);
    assert.equal(await guard.lockedUntil("acme-rc"), now + 10 * MINUTE);
  });

  it("survives a restart, and reads the disk once however many ask at once", async () => {
    await failTimes(CLUB_FAIL_LIMIT);
    now += 10 * MINUTE;
    await failTimes(CLUB_FAIL_LIMIT);

    let reads = 0;

    class Counting extends MemoryStore {
      override async getSnapshot(club: string, channel: string): Promise<StoredDocument | undefined> {
        if (channel === PIN_GUARD_DOC) {
          reads++;
        }

        return store.getSnapshot(club, channel);
      }
    }

    const restarted = new PinGuard(new Counting(), () => now, () => {});
    const answers = await Promise.all([restarted.lockedUntil("acme-rc"), restarted.lockedUntil("acme-rc")]);

    assert.deepEqual(answers, [now + 30 * MINUTE, now + 30 * MINUTE]);
    assert.equal(reads, 1);

    // And it remembers the level: the next lockout is the two-hour one.
    now += 30 * MINUTE;
    const again = new PinGuard(store, () => now, () => {});
    for (let i = 0; i < CLUB_FAIL_LIMIT; i++) {
      await again.fail("acme-rc");
    }
    assert.equal(await again.lockedUntil("acme-rc"), now + 2 * HOUR);
  });

  it("never believes a stored lockout longer than the longest real one", async () => {
    await store.putSnapshot("acme-rc", PIN_GUARD_DOC, {
      seq: 0,
      publishedAt: new Date(now).toISOString(),
      body: { fails: ["x", 1e99, now - 1], level: 99, lockedUntil: now + 365 * 24 * HOUR },
    });

    assert.equal(await guard.lockedUntil("acme-rc"), now + 12 * HOUR);
  });

  it("keeps counting in memory when the disk is full, and says so once", async () => {
    class FullDisk extends MemoryStore {
      override async putSnapshot(): Promise<void> {
        throw Object.assign(new Error("ENOSPC: no space left on device"), { code: "ENOSPC" });
      }
    }

    guard = new PinGuard(new FullDisk(), () => now, (line) => lines.push(line));

    await failTimes(CLUB_FAIL_LIMIT);

    assert.equal(await guard.lockedUntil("acme-rc"), now + 10 * MINUTE);
    assert.equal(lines.filter((line) => line.includes("could not save")).length, 1);
  });

  it("starts again when reset", async () => {
    await failTimes(CLUB_FAIL_LIMIT);
    await guard.reset("acme-rc");
    assert.equal(await guard.lockedUntil("acme-rc"), 0);
    assert.deepEqual((await store.getSnapshot("acme-rc", PIN_GUARD_DOC))?.body, { fails: [], level: 0, lockedUntil: 0 });
  });
});

describe("check-in through a lockout", () => {
  let harness: Harness;
  let store: MemoryStore;
  let now: number;
  let seq: number;

  const clock = () => new Date(now);

  const list = async (json: Record<string, unknown>, address = "10.9.9.9") => {
    const response = await fetch(`${harness.base}/api/v1/c/acme-rc/checkin/list`, {
      method: "POST",
      headers: { "content-type": "application/json", "fly-client-ip": address },
      body: JSON.stringify(json),
    });

    return { status: response.status, body: (await response.json()) as Record<string, any> };
  };

  const publishCheckin = async (doc: unknown) => {
    const answer = await call(harness, "POST", `/api/v1/c/acme-rc/publish/checkin?seq=${seq++}`, { token: ACME_TOKEN, json: doc });
    assert.equal(answer.status, 200);
  };

  /** Fifty wrong PINs, five each from ten addresses, so no one address trips its own limit. */
  const lockTheClub = async () => {
    for (let a = 0; a < 10; a++) {
      for (let i = 0; i < 5; i++) {
        assert.equal((await list({ pin: `99${a}${i}` }, `10.1.${a}.1`)).status, 403);
      }
    }
  };

  before(() => {
    process.env["FLY_APP_NAME"] = "test";
  });

  after(() => {
    delete process.env["FLY_APP_NAME"];
  });

  beforeEach(async () => {
    now = Date.UTC(2026, 8, 20, 12);
    seq = 1;
    store = new MemoryStore();
    harness = await start(store, undefined, clock);
    await publishCheckin(checkinDoc());
  });

  afterEach(async () => {
    await harness.close();
  });

  it("answers a right PIN with a pass that keeps working through the lockout", async () => {
    const staff = await list({ pin: "4321" });
    const racer = await list({ pin: SAM_PIN });

    assert.equal(staff.body.pass, await harness.relay.passOf("staff", "acme-rc", pinHash("acme-rc", "4321")));
    assert.equal(racer.body.pass, await harness.relay.passOf("racer", "acme-rc", racerPinHash("acme-rc", SAM_PIN)));
    assert.notEqual(staff.body.pass, racer.body.pass);

    await lockTheClub();

    const locked = await list({ pin: "4321" });
    assert.equal(locked.status, 429);
    assert.match(locked.body.error, /closed for about 10 minutes/);

    const desk = await list({ pin: "4321", pass: staff.body.pass });
    assert.equal(desk.status, 200);
    assert.equal(desk.body.mode, "staff");
    assert.equal(desk.body.drivers.length, 2);

    const sam = await list({ pin: SAM_PIN, pass: racer.body.pass });
    assert.equal(sam.status, 200);
    assert.equal(sam.body.mode, "racer");
    assert.equal(sam.body.driver.id, SAM);

    // A made-up pass is no way round it.
    assert.equal((await list({ pin: "4321", pass: "0".repeat(64) })).status, 429);
  });

  it("is still locked after a restart", async () => {
    await lockTheClub();
    await harness.close();

    harness = await start(store, undefined, clock);
    assert.equal((await list({ pin: "4321" })).status, 429);

    const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/inbox", { token: ACME_TOKEN });
    assert.equal(inbox.body.pinLockedUntil, new Date(now + 10 * MINUTE).toISOString());
  });

  it("reopens when the laptop publishes a new staff PIN, and not for the same one again", async () => {
    await lockTheClub();

    await publishCheckin(checkinDoc());
    assert.equal((await list({ pin: "4321" })).status, 429, "the same PIN republished changes nothing");

    await publishCheckin(checkinDoc("975310"));
    const reopened = await list({ pin: "975310" });
    assert.equal(reopened.status, 200);
    assert.equal(reopened.body.mode, "staff");

    const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/inbox", { token: ACME_TOKEN });
    assert.equal("pinLockedUntil" in inbox.body, false);
  });

  it("signs a device out when the PIN its pass came from changes", async () => {
    const staff = await list({ pin: "4321" });
    await publishCheckin(checkinDoc("975310"));

    const stale = await list({ pin: "4321", pass: staff.body.pass });
    assert.equal(stale.status, 403);
  });

  it("closes for longer the second time", async () => {
    await lockTheClub();
    now += 10 * MINUTE;
    await lockTheClub();

    const locked = await list({ pin: "4321" });
    assert.equal(locked.status, 429);
    assert.match(locked.body.error, /closed for about 30 minutes/);
  });
});

describe("club look and icon", () => {
  it("reads the site document once, until the club publishes a new one", async () => {
    let siteReads = 0;
    let checkinReads = 0;

    class Counting extends MemoryStore {
      override async getSnapshot(club: string, channel: string): Promise<StoredDocument | undefined> {
        siteReads += channel === "site" ? 1 : 0;
        checkinReads += channel === "checkin" ? 1 : 0;
        return super.getSnapshot(club, channel);
      }
    }

    const harness = await start(new Counting());

    try {
      const first = await fetch(`${harness.base}/c/acme-rc/icon.png`).then((r) => r.arrayBuffer());

      for (const path of ["/c/acme-rc/icon.png", "/c/acme-rc/manifest.webmanifest", "/c/acme-rc", "/c/acme-rc/results"]) {
        assert.equal((await call(harness, "GET", path)).status, 200, path);
      }

      assert.equal(siteReads, 1);
      assert.equal(checkinReads, 1);

      const published = await call(harness, "POST", "/api/v1/c/acme-rc/publish/site", {
        token: ACME_TOKEN,
        json: { theme: { colors: { primary: "#00AA00", bg: "#FFFFFF" } } },
      });
      assert.equal(published.status, 200);

      // The publish reads the old document itself; the next page then reads the new one once.
      const readsBefore = siteReads;
      await call(harness, "GET", "/c/acme-rc/results");
      await call(harness, "GET", "/c/acme-rc/results");
      assert.equal(siteReads, readsBefore + 1);

      // The icon is HeatCaller's sun whatever the club's colours (30 Oct
      // 2026, relay branding), so it reads no look at all.
      const second = await fetch(`${harness.base}/c/acme-rc/icon.png`).then((r) => r.arrayBuffer());
      assert.equal(siteReads, readsBefore + 1);
      assert.deepEqual(Buffer.from(second), Buffer.from(first), "the same sun in any colours");

      // Home's check-in line follows the check-in document straight away
      // (a tile until 30 Oct 2026, relay navigation round 5).
      assert.doesNotMatch((await call(harness, "GET", "/c/acme-rc")).text, /Check in: tap Me and type your PIN/);
      await call(harness, "POST", "/api/v1/c/acme-rc/publish/checkin", { token: ACME_TOKEN, json: checkinDoc() });
      assert.match((await call(harness, "GET", "/c/acme-rc")).text, /Check in: tap Me and type your PIN/);
    } finally {
      await harness.close();
    }
  });
});
