import assert from "node:assert/strict";
import { afterEach, describe, it } from "node:test";
import { pinHash } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

// Sign-up's anonymous routes must not make a club's pass key when there is
// nothing to make a pass for - the same rule check-in and the manager's
// sign-in already follow. Making the key writes passkey.json, which brought
// a deleted club's folder back from the first stranger to POST {}.

const NOW = new Date("2026-09-20T12:00:00.000Z");
const open: Harness[] = [];

afterEach(async () => {
  while (open.length > 0) {
    await open.pop()!.close();
  }
});

async function harness(): Promise<Harness> {
  const h = await start(new MemoryStore(), undefined, () => NOW);
  open.push(h);
  return h;
}

const PATHS = ["/api/v1/c/acme-rc/signup/signin", "/api/v1/c/acme-rc/signup/status"];

describe("sign-up and the pass key", () => {
  it("is not made by an anonymous sign-up request for a club that has never used sign-up", async () => {
    const h = await harness();

    for (const path of PATHS) {
      assert.equal((await call(h, "POST", path, { json: {} })).status, 409);
      assert.equal((await call(h, "POST", path, { json: { pin: "12345678", pass: "x" } })).status, 409);
    }

    assert.equal(await h.store.getSnapshot("acme-rc", "passkey"), undefined);
    assert.equal(await h.store.usage("acme-rc"), 0);
  });

  it("does not bring a deleted club's folder back", async () => {
    const h = await harness();
    const doc = {
      enabled: true,
      pinSha256: pinHash("acme-rc", "4321"),
      event: "Club night",
      drivers: [{ id: "0123456789abcdef0123", name: "Sam", className: "Stock", number: "7", checkedIn: false, photo: "" }],
    };

    assert.equal((await call(h, "POST", "/api/v1/c/acme-rc/publish/checkin?seq=1", { token: ACME_TOKEN, json: doc })).status, 200);
    assert.equal((await call(h, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "4321" } })).status, 200);
    assert.notEqual(await h.store.getSnapshot("acme-rc", "passkey"), undefined);

    assert.equal((await call(h, "DELETE", "/api/v1/c/acme-rc", { token: ACME_TOKEN })).status, 200);

    for (const path of PATHS) {
      assert.equal((await call(h, "POST", path, { json: {} })).status, 409);
    }

    assert.equal(await h.store.getSnapshot("acme-rc", "passkey"), undefined);
    assert.equal(await h.store.usage("acme-rc"), 0);
  });
});
