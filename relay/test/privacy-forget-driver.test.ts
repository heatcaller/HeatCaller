import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import { followIdOf } from "../src/push.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * FORGET ONE DRIVER (15 Nov 2026, children's-privacy plan 2.5): the
 * laptop's "Forget this driver" takes everything the relay holds of that
 * driver - and nothing of anybody else's.
 */

const API = "/api/v1/c/acme-rc";
const MIA = "a1b2c3d4e5f60718293a";
const SAM = "0f1e2d3c4b5a69788796";
const at = "2026-09-20T11:00:00.000Z";
const doc = (body: unknown) => ({ seq: 1, publishedAt: at, body });

describe("driver/forget", () => {
  let h: Harness;
  let store: MemoryStore;

  beforeEach(async () => {
    store = new MemoryStore();
    h = await start(store);

    for (const badge of [MIA, SAM]) {
      await store.putItem("acme-rc", "nights", badge, doc({ driver: badge }));
      await store.putItem("acme-rc", "garage", badge, doc({ cars: [] }));
      await store.putItem("acme-rc", "pushsubs", badge, doc({ subs: [{ endpoint: "https://fcm.googleapis.com/x" + badge, p256dh: "k", auth: "a" }] }));
      await store.putItem("acme-rc", "driverasks", "a" + badge.slice(0, 23), doc({ b: badge, k: "query", v: "badge", f: { query: "hi" }, at }));
      await store.putItem("acme-rc", "driverphotos", "b" + badge.slice(0, 23), doc({ b: badge, car: "c".repeat(32), v: "badge", data: "xx", at }));
      await store.putItem("acme-rc", "familylinks", "f" + badge.slice(0, 23), doc({ d: badge, label: "Grandma", made: at }));
    }

    await store.putItem("acme-rc", "followsubs", followIdOf("Mia Kent"), doc({ name: "Mia Kent", subs: [] }));
    await store.putItem("acme-rc", "followsubs", followIdOf("Sam Roe"), doc({ name: "Sam Roe", subs: [] }));
    await store.putSnapshot("acme-rc", "driverlive", { seq: 7, publishedAt: at, body: { at, drivers: { [MIA]: { next: 1 }, [SAM]: { next: 2 } } } });
    await store.putSnapshot("acme-rc", "driverme", { seq: 3, publishedAt: at, body: { drivers: { [MIA]: { email: "m***@x" }, [SAM]: { email: "s***@x" } } } });
  });

  afterEach(async () => {
    await h.close();
  });

  const forget = (json: unknown, token: string | null = ACME_TOKEN) => call(h, "POST", `${API}/driver/forget`, token === null ? { json } : { token, json });

  it("takes everything held of that one driver, by badge code and by name", async () => {
    const answer = await forget({ badges: [MIA], names: ["Mia Kent"] });
    assert.equal(answer.status, 200);
    assert.equal(answer.body.ok, true);

    for (const channel of ["nights", "garage", "pushsubs"]) {
      assert.equal(await store.getItem("acme-rc", channel, MIA), undefined, channel);
      assert.notEqual(await store.getItem("acme-rc", channel, SAM), undefined, `${channel}: the other driver stays`);
    }

    for (const channel of ["driverasks", "driverphotos", "familylinks"]) {
      const left = await store.listItems("acme-rc", channel);
      assert.deepEqual(left.map((i) => (i.doc.body as Record<string, unknown>)["b"] ?? (i.doc.body as Record<string, unknown>)["d"]), [SAM], channel);
    }

    assert.equal(await store.getItem("acme-rc", "followsubs", followIdOf("Mia Kent")), undefined);
    assert.notEqual(await store.getItem("acme-rc", "followsubs", followIdOf("Sam Roe")), undefined);

    const live = await store.getSnapshot("acme-rc", "driverlive");
    assert.deepEqual(Object.keys((live?.body as { drivers: object }).drivers), [SAM]);
    assert.equal(live?.seq, 7, "the laptop's next live document still wins");
    const me = await store.getSnapshot("acme-rc", "driverme");
    assert.deepEqual(Object.keys((me?.body as { drivers: object }).drivers), [SAM]);
  });

  it("is safe to send again: a driver already gone is still ok", async () => {
    assert.equal((await forget({ badges: [MIA], names: ["Mia Kent"] })).status, 200);
    const again = await forget({ badges: [MIA], names: ["Mia Kent"] });
    assert.equal(again.status, 200);
    assert.equal(again.body.removed, 0);
  });

  it("needs this club's publish token", async () => {
    assert.equal((await forget({ badges: [MIA] }, null)).status, 401);
    assert.equal((await forget({ badges: [MIA] }, BROOK_TOKEN)).status, 403);
    assert.notEqual(await store.getItem("acme-rc", "nights", MIA), undefined, "nothing went");
  });

  it("refuses a call that names nobody, and ignores what isn't a badge code", async () => {
    assert.equal((await forget({ badges: ["../../etc"], names: [] })).status, 400);
    assert.equal((await forget({})).status, 400);
  });
});
