import assert from "node:assert/strict";
import { request as httpRequest } from "node:http";
import { afterEach, describe, it } from "node:test";
import { pinHash } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { MemoryVideoVault } from "../src/videos.js";
import { ACME_TOKEN, call, start, tenants, type Harness } from "./helpers.js";

/**
 * BUG HUNT 10, relay/src/server.ts (23 Oct 2026): the staff check-in list
 * handing out every driver's badge id, and forget-media missing a podium
 * video that was still on its way.
 */

const SAM = "0123456789abcdef0123";
const BILL = "fedcba9876543210fedc";
const RACE = "0123456789abcdef0123456789abcdef";
const VID1 = "11111111111111111111111111111111";
const VID2 = "22222222222222222222222222222222";
const NOW = new Date("2026-10-23T19:00:00.000Z");

let harness: Harness | undefined;

afterEach(async () => {
  await harness?.close();
  harness = undefined;
});

function checkinDoc(pin = "4321") {
  return {
    enabled: true,
    pinSha256: pinHash("acme-rc", pin),
    event: "Club Night",
    drivers: [
      { id: SAM, name: "Sam Carter", className: "Stock", number: "7", checkedIn: false, photo: "" },
      { id: BILL, name: "Bill Travers", className: "GT", number: "", checkedIn: true, photo: "" },
    ],
  };
}

async function publishCheckin(h: Harness, doc: unknown, seq: number): Promise<void> {
  const answer = await call(h, "POST", `/api/v1/c/acme-rc/publish/checkin?seq=${seq}`, { token: ACME_TOKEN, json: doc });
  assert.equal(answer.status, 200);
}

describe("the staff check-in list", () => {
  it("never carries a real badge id, and a tap with its handle still checks the right driver in", async () => {
    harness = await start();
    await publishCheckin(harness, checkinDoc(), 1);

    const list = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "4321" } });
    assert.equal(list.status, 200);
    assert.equal(list.body.mode, "staff");

    const ids = list.body.drivers.map((d: { id: string }) => d.id) as string[];
    assert.equal(ids.length, 2);
    assert.deepEqual(list.body.drivers.map((d: { name: string }) => d.name), ["Sam Carter", "Bill Travers"]);

    for (const id of ids) {
      assert.match(id, /^[0-9a-f]{20}$/);
      assert.notEqual(id, SAM);
      assert.notEqual(id, BILL);
    }

    // The same handles on the next poll, by PIN or by pass.
    const again = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "4321", pass: list.body.pass } });
    assert.deepEqual(again.body.drivers.map((d: { id: string }) => d.id), ids);

    // A handle does not open a driver's own card - that answers the real id.
    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/one", { json: { id: ids[0] } })).status, 404);

    // But a tap with it queues the real badge id for the laptop.
    const tap = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: ids[0], wanted: true } });
    assert.equal(tap.status, 200);

    const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/inbox", { token: ACME_TOKEN });
    assert.equal(inbox.body.items.length, 1);
    assert.equal(inbox.body.items[0].id, SAM);

    const after = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "4321" } });
    assert.equal(after.body.drivers[0].checkedIn, true);

    // A badge's own tap is unchanged.
    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: BILL, wanted: false } })).status, 200);
  });

  it("gives new handles once the staff PIN changes, and the old ones tap nobody", async () => {
    harness = await start();
    await publishCheckin(harness, checkinDoc("4321"), 1);
    const before = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "4321" } });
    const old = before.body.drivers[0].id as string;

    await publishCheckin(harness, checkinDoc("8765"), 2);
    const now = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "8765" } });
    assert.notEqual(now.body.drivers[0].id, old);

    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: old, wanted: true } })).status, 404);
  });
});

function mp4(size = 4000): Buffer {
  const bytes = Buffer.alloc(size, 7);
  bytes.writeUInt32BE(24, 0);
  bytes.write("ftypisom", 4, "latin1");
  return bytes;
}

/** Starts an upload, sends half, and gives back a way to finish it and its status. */
function halfUpload(h: Harness, video: string, driver: string): { sentHalf: Promise<void>; finish: () => void; status: Promise<number> } {
  const body = mp4();
  const url = new URL(`${h.base}/api/v1/c/acme-rc/video/${video}?race=${RACE}&position=1&driver=${driver}&name=Sam&label=A-Main`);
  let half!: () => void;
  const sentHalf = new Promise<void>((resolve) => (half = resolve));
  let go!: () => void;
  const finishing = new Promise<void>((resolve) => (go = resolve));

  const status = new Promise<number>((resolve, reject) => {
    const req = httpRequest(
      url,
      {
        method: "POST",
        headers: { authorization: `Bearer ${ACME_TOKEN}`, "content-type": "video/mp4", "content-length": String(body.length) },
      },
      (res) => {
        res.resume();
        res.on("end", () => resolve(res.statusCode ?? 0));
      },
    );
    req.on("error", reject);
    req.write(body.subarray(0, 2000), () => half());
    void finishing.then(() => req.end(body.subarray(2000)));
  });

  return { sentHalf, finish: () => go(), status };
}

describe("forget-media while a podium video is on its way", () => {
  it("keeps nothing of that driver's upload, keeps another driver's, and takes the next one as ever", async () => {
    const vault = new MemoryVideoVault();
    harness = await start(new MemoryStore(), tenants(), () => NOW, undefined, undefined, { videos: vault });

    const sams = halfUpload(harness, VID1, SAM);
    const bills = halfUpload(harness, VID2, BILL);
    await Promise.all([sams.sentHalf, bills.sentHalf]);
    // Give the relay a moment to start reading both bodies.
    await new Promise((resolve) => setTimeout(resolve, 100));

    const forgot = await call(harness, "POST", "/api/v1/c/acme-rc/driver/forget-media", { token: ACME_TOKEN, json: { ids: [SAM] } });
    assert.deepEqual(forgot.body, { ok: true, forgotten: 0 });

    sams.finish();
    bills.finish();
    assert.equal(await sams.status, 410);
    assert.equal(await bills.status, 200);
    assert.deepEqual((await vault.list("acme-rc")).map((v) => v.driver), [BILL]);

    // An upload that starts after the take-down is the laptop's own new
    // choice and is kept.
    const next = halfUpload(harness, VID1, SAM);
    await next.sentHalf;
    next.finish();
    assert.equal(await next.status, 200);
    assert.equal((await vault.list("acme-rc")).length, 2);
  });
});
