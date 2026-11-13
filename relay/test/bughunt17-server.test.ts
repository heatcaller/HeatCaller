import assert from "node:assert/strict";
import { request as httpRequest, type ClientRequest } from "node:http";
import { afterEach, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import type { StoredDocument } from "../src/types.js";
import type { VoiceService } from "../src/voice.js";
import { API, BILL, checkinDoc, openEverything, publish, SAM, setSwitch, startDriver, type DriverHarness } from "./driverexp-r1-helpers.js";
import { ACME_TOKEN, call, start, tenants, type Harness } from "./helpers.js";

/**
 * BUG HUNT 17 (13 Nov 2026), relay/src/server.ts: listen-along read from
 * the disk for every listener, one address holding every photo place, a
 * failed delete leaving the club in memory, the whole folder walked for a
 * channel that never needed it, and an ask that waited on itself.
 */

const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** A store that counts what the relay asks of it, and can be given something to do in the middle of a read. */
class WatchedStore extends MemoryStore {
  readonly itemReads: string[] = [];
  usageReads = 0;
  duringNightRead: (() => Promise<void>) | undefined;

  override async getItem(club: string, channel: string, id: string): Promise<StoredDocument | undefined> {
    this.itemReads.push(`${channel}/${id}`);

    if (channel === "nights" && this.duringNightRead !== undefined) {
      const once = this.duringNightRead;
      this.duringNightRead = undefined;
      await once();
    }

    return super.getItem(club, channel, id);
  }

  override async usage(club: string): Promise<number> {
    this.usageReads++;
    return super.usage(club);
  }
}

describe("bug hunt 17: a stretch of listen-along is read from the disk once, however many are listening", () => {
  let harness: Harness | undefined;

  afterEach(async () => {
    await harness?.close();
    harness = undefined;
  });

  const segment = (n: number) => ({ seq: n, said: "", audio: "A".repeat(2048) });
  const send = (path: string, json: unknown) => call(harness!, "POST", `/api/v1/c/acme-rc/publish/${path}`, { token: ACME_TOKEN, json });
  const read = (path: string) => call(harness!, "GET", `/api/v1/c/acme-rc/collection/${path}`);

  it("holds it, counts one read against the address, and lets go when the next one lands", async () => {
    const store = new WatchedStore();
    harness = await start(store);
    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/listen/open", { token: ACME_TOKEN, json: { open: true } })).status, 200);
    assert.equal((await send("listen/000001", segment(1))).status, 200);

    store.itemReads.length = 0;

    for (let phone = 0; phone < 30; phone++) {
      const got = await read("listen/000001");
      assert.equal(got.status, 200);
      assert.equal(got.body.body.audio, segment(1).audio);
    }

    assert.deepEqual(store.itemReads, ["listen/000001"], "thirty phones, one read of the disk");

    const counted = (harness.relay as unknown as { collectionReads: { hits: Map<string, number[]> } }).collectionReads.hits;
    assert.deepEqual([...counted.values()].map((hits) => hits.length), [1], "and one off the address's allowance");

    // The next stretch replaces what is held, so nothing stale is served.
    assert.equal((await send("listen/000001", { ...segment(1), said: "again" })).status, 200);
    assert.equal((await read("listen/000001")).body.body.said, "again");

    // One that is not there is not remembered as missing.
    assert.equal((await read("listen/000009")).status, 404);
    assert.equal((await send("listen/000009", segment(9))).status, 200);
    assert.equal((await read("listen/000009")).status, 200);
  });

  it("still reads a result each time, as before", async () => {
    const store = new WatchedStore();
    harness = await start(store);
    assert.equal((await send("results/r1?seq=1", { a: 1 })).status, 200);
    store.itemReads.length = 0;

    assert.equal((await read("results/r1")).status, 200);
    assert.equal((await read("results/r1")).status, 200);
    assert.deepEqual(store.itemReads, ["results/r1", "results/r1"]);
  });
});

describe("bug hunt 17: a channel with a budget of its own does not walk the club's folder", () => {
  let harness: Harness | undefined;

  afterEach(async () => {
    await harness?.close();
    harness = undefined;
  });

  it("measures the folder for a result and not for a stretch of listen-along", async () => {
    const store = new WatchedStore();
    harness = await start(store);
    const send = (path: string, json: unknown) => call(harness!, "POST", `/api/v1/c/acme-rc/publish/${path}`, { token: ACME_TOKEN, json });
    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/listen/open", { token: ACME_TOKEN, json: { open: true } })).status, 200);

    store.usageReads = 0;
    assert.equal((await send("listen/000001", { seq: 1, said: "", audio: "AAAA" })).status, 200);
    assert.equal((await send("listen/000002", { seq: 2, said: "", audio: "AAAA" })).status, 200);
    assert.equal(store.usageReads, 0);

    assert.equal((await send("results/r1?seq=1", { a: 1 })).status, 200);
    assert.ok(store.usageReads > 0, "a result is still counted against the club's quota");
  });
});

describe("bug hunt 17: a delete that fails part-way still forgets what was held in memory", () => {
  let harness: Harness | undefined;

  afterEach(async () => {
    await harness?.close();
    harness = undefined;
  });

  it("does not go on serving the club's board and roster after the voice's wipe throws", async () => {
    const voice = { forget: async () => { throw new Error("ENOTEMPTY"); } } as unknown as VoiceService;
    harness = await start(new MemoryStore(), tenants(), undefined, undefined, voice);
    const live = harness;
    const post = (path: string, json: unknown) => call(live, "POST", `/api/v1/c/acme-rc/${path}`, { token: ACME_TOKEN, json });

    assert.equal((await post("publish/state?seq=1", { state: "Racing", standings: [] })).status, 200);
    assert.equal((await post("publish/checkin?seq=2", checkinDoc())).status, 200);
    assert.equal((await call(live, "GET", "/api/v1/c/acme-rc/snapshot/state")).status, 200, "held from here on");
    assert.equal((await call(live, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "123456" } })).status, 200, "the roster too");

    const held = live.relay as unknown as { checkins: Map<string, unknown>; passKeys: Map<string, unknown> };
    assert.ok(held.checkins.has("acme-rc"));

    const deleted = await call(live, "DELETE", "/api/v1/c/acme-rc", { token: ACME_TOKEN });
    assert.notEqual(deleted.status, 200, "the laptop is told the delete did not finish");

    assert.equal(held.checkins.get("acme-rc") ?? undefined, undefined, "no roster, PIN hashes and all, left in memory");
    assert.equal(held.passKeys.has("acme-rc"), false);
    assert.equal((await call(live, "GET", "/api/v1/c/acme-rc/snapshot/state")).status, 404, "and the board is gone, not served from memory");
  });
});

describe("bug hunt 17: an ask checked inside the club's lock never asks for the pass key there", () => {
  let h: DriverHarness | undefined;

  afterEach(async () => {
    await h?.close();
    h = undefined;
  });

  it("takes an I'm coming that the club's first check-in roster landed in front of, and the club can still write", async () => {
    const store = new WatchedStore();
    h = await startDriver({ store });
    const live = h;

    await setSwitch(live, "nights/open", true);
    await setSwitch(live, "driver/open", true);
    assert.equal((await publish(live, "nextnight", {
      day: "2026-11-18", when: "Tue 18 Nov", classes: ["Stock"], takingEntries: true, off: false, until: "2026-11-19T11:00:00.000Z",
    })).status, 200);
    assert.equal((await call(live, "POST", `${API}/publish/nights/${SAM}?seq=1`, { token: ACME_TOKEN, json: { name: "Sam Parker", races: [] } })).status, 200);

    // No roster and no pass key yet. The roster lands after the ask has
    // been let in (the night read is the last thing checked before the
    // lock) and before it gets the lock.
    store.duringNightRead = async () => {
      assert.equal((await publish(live, "checkin", checkinDoc())).status, 200);
    };

    const asked = call(live, "POST", `${API}/driver/ask`, { json: { id: SAM, kind: "coming", f: { day: "2026-11-18", classes: "Stock" } } });
    const answer = await Promise.race([asked, wait(5000).then(() => undefined)]);

    assert.ok(answer !== undefined, "the ask waited on itself inside the club's lock");
    assert.equal(answer.status, 200);

    // And nothing is stuck behind it.
    const after = await Promise.race([publish(live, "state", { state: "Racing", standings: [] }), wait(5000).then(() => undefined)]);
    assert.equal(after?.status, 200);
  });
});

describe("bug hunt 17: one address cannot hold every car-photo place", () => {
  let h: DriverHarness | undefined;

  afterEach(async () => {
    delete process.env["FLY_APP_NAME"];
    await h?.close();
    h = undefined;
  });

  const CAR = "33333333333333333333333333333333";
  const JPEG = Buffer.concat([Buffer.from([0xff, 0xd8, 0xff, 0xe0]), Buffer.alloc(60, 7)]).toString("base64");

  /** A photo body from one address, sent a little at a time and never finished until told. */
  function trickle(base: string, address: string): { req: ClientRequest; status: Promise<number> } {
    const url = new URL(`${base}${API}/driver/photo`);
    let req!: ClientRequest;
    const status = new Promise<number>((resolve) => {
      req = httpRequest({
        host: url.hostname, port: url.port, path: url.pathname, method: "POST",
        headers: { "content-type": "application/json", "fly-client-ip": address },
      }, (res) => {
        res.resume();
        resolve(res.statusCode ?? 0);
      });
      req.on("error", () => resolve(0));
    });
    req.write(`{"id":"${BILL}","car":"${CAR}","data":"`);
    return { req, status };
  }

  it("is held to two at once, and everybody else's photos still go in", async () => {
    h = await startDriver();
    await openEverything(h);
    process.env["FLY_APP_NAME"] = "test";

    const relay = h.relay as unknown as { photoUploadsInFlight: number; photoUploadsFrom: Map<string, number> };
    const slow = Array.from({ length: 6 }, () => trickle(h!.base, "10.17.0.1"));
    await wait(200);

    assert.equal(relay.photoUploadsInFlight, 2, "two of the six places, not all of them");

    // A driver somewhere else is not told the relay is busy.
    assert.equal((await call(h, "POST", `${API}/driver/photo`, { json: { id: BILL, car: CAR, data: JPEG } })).status, 200);

    for (const one of slow) {
      one.req.destroy();
    }

    await wait(200);
    assert.equal(relay.photoUploadsInFlight, 0);
    assert.equal(relay.photoUploadsFrom.size, 0, "and nothing is remembered about an address with none in flight");
  });
});
