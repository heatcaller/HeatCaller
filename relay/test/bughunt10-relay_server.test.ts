import assert from "node:assert/strict";
import { afterEach, describe, it } from "node:test";
import { pinHash } from "../src/server.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * BUG HUNT 10, relay/src/server.ts (23 Oct 2026): the staff check-in list
 * handing out every driver's badge id. (Its second find, forget-media
 * missing a podium video still on its way, went with the videos on 28 Oct
 * 2026 - they stay on the laptop now.)
 */

const SAM = "0123456789abcdef0123";
const BILL = "fedcba9876543210fedc";

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
