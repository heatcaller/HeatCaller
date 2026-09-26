import assert from "node:assert/strict";
import { afterEach, describe, it } from "node:test";
import { managePinHash, passFor, pinHash, racerPinHash, signupPinHash } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import type { StoredDocument } from "../src/types.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, type Harness } from "./helpers.js";

// Regressions for the second audit: passes that anybody could work out from
// a PIN, and writes that could land after a club was deleted.

const NOW = new Date("2026-09-20T12:00:00.000Z");
const LATER = "2026-10-04T12:00:00.000Z";
const SAM = "0123456789abcdef0123";
const SAM_PIN = "123456";

let seq = 1;
const open: Harness[] = [];

async function harness(store = new MemoryStore()): Promise<Harness> {
  const h = await start(store, undefined, () => NOW);
  open.push(h);
  return h;
}

afterEach(async () => {
  while (open.length > 0) {
    await open.pop()!.close();
  }
});

async function publish(h: Harness, channel: string, doc: unknown, token = ACME_TOKEN, club = "acme-rc"): Promise<void> {
  const answer = await call(h, "POST", `/api/v1/c/${club}/publish/${channel}?seq=${seq++}`, { token, json: doc });
  assert.equal(answer.status, 200, JSON.stringify(answer.body));
}

const checkinDoc = () => ({
  enabled: true,
  pinSha256: pinHash("acme-rc", "4321"),
  event: "Club night",
  drivers: [{ id: SAM, name: "Sam", className: "Stock", number: "7", checkedIn: false, photo: "", rp: racerPinHash("acme-rc", SAM_PIN) }],
});
const manageDoc = (overrides: Record<string, unknown> = {}) => ({ enabled: true, pinSha256: managePinHash("acme-rc", "654321"), ...overrides });
const signupDoc = () => ({ enabled: true, event: "Club night", classes: ["Stock"], invites: [{ h: signupPinHash("acme-rc", "48291357"), x: LATER }] });
const form = { name: "Sam Rivers", className: "Stock", transponderKind: "unknown", transponder: "", car: "", email: "", guardian: false };

describe("a pass cannot be worked out from the PIN", () => {
  it("check-in: the public recipe opens nothing, even for the right PIN, before or after a lockout", async () => {
    const h = await harness();
    await publish(h, "checkin", checkinDoc());

    const forgedStaff = passFor("staff", "acme-rc", pinHash("acme-rc", "4321"));
    const forgedRacer = passFor("racer", "acme-rc", racerPinHash("acme-rc", SAM_PIN));

    // Before any lockout a forged pass falls through to the PIN beside it, and is counted as the guess it is.
    const before = await call(h, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pass: forgedStaff } });
    assert.equal(before.status, 403);

    // Lock this address out, then try the forged passes: they used to open the list here, uncounted.
    for (let i = 0; i < 10; i++) {
      await call(h, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "000000" } });
    }

    for (const pass of [forgedStaff, forgedRacer]) {
      const answer = await call(h, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pass } });
      assert.equal(answer.status, 429, JSON.stringify(answer.body));
      assert.equal(answer.body.drivers, undefined);
      assert.equal(answer.body.driver, undefined);
    }

    // The relay's own pass still carries a signed-in desk through the lockout.
    const real = await h.relay.passOf("staff", "acme-rc", pinHash("acme-rc", "4321"));
    assert.notEqual(real, forgedStaff);
    const through = await call(h, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pass: real } });
    assert.equal(through.status, 200);
    assert.equal(through.body.mode, "staff");
  });

  it("manager: the public recipe opens neither request nor response, and the real pass does", async () => {
    const h = await harness();
    await publish(h, "manage", manageDoc());

    const forged = passFor("manager", "acme-rc", managePinHash("acme-rc", "654321"));

    for (const route of ["request", "response"]) {
      const answer = await call(h, "POST", `/api/v1/c/acme-rc/manage/${route}`, { json: { pass: forged, reqId: "abcdefgh1", op: "hello" } });
      assert.equal(answer.status, 403, route);
    }

    const signin = await call(h, "POST", "/api/v1/c/acme-rc/manage/signin", { json: { pin: "654321" } });
    assert.equal(signin.status, 200);
    assert.notEqual(signin.body.pass, forged);
    assert.equal(signin.body.pass, await h.relay.passOf("manager", "acme-rc", managePinHash("acme-rc", "654321")));

    const poll = await call(h, "POST", "/api/v1/c/acme-rc/manage/response", { json: { pass: signin.body.pass, reqId: "abcdefgh1" } });
    assert.equal(poll.status, 200);
  });

  it("sign-up: the public recipe opens neither status, submit nor signin", async () => {
    const h = await harness();
    await publish(h, "signup", signupDoc());

    const forged = passFor("signup", "acme-rc", signupPinHash("acme-rc", "48291357"));

    assert.equal((await call(h, "POST", "/api/v1/c/acme-rc/signup/status", { json: { pass: forged } })).status, 403);
    assert.equal((await call(h, "POST", "/api/v1/c/acme-rc/signup/submit", { json: { pass: forged, form } })).status, 403);
    assert.equal((await call(h, "POST", "/api/v1/c/acme-rc/signup/signin", { json: { pass: forged } })).status, 403);

    // Nothing was written by any of them.
    const inbox = await call(h, "GET", "/api/v1/c/acme-rc/signup/inbox", { token: ACME_TOKEN });
    assert.deepEqual(inbox.body.items, []);

    // The code still works, and its pass is not the public one.
    const signin = await call(h, "POST", "/api/v1/c/acme-rc/signup/signin", { json: { pin: "4829 1357" } });
    assert.equal(signin.status, 200);
    assert.notEqual(signin.body.pass, forged);
  });

  it("keeps a pass working across a restart, and keeps the key out of every public route", async () => {
    const store = new MemoryStore();
    const first = await harness(store);
    await publish(first, "checkin", checkinDoc());

    const pass = (await call(first, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "4321" } })).body.pass as string;
    await first.close();
    open.splice(open.indexOf(first), 1);

    const second = await harness(store);
    const again = await call(second, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pass } });
    assert.equal(again.status, 200);
    assert.equal(again.body.mode, "staff");

    assert.equal((await call(second, "GET", "/api/v1/c/acme-rc/snapshot/passkey")).status, 404);
    assert.equal((await call(second, "POST", "/api/v1/c/acme-rc/publish/passkey?seq=1", { token: ACME_TOKEN, json: { key: "00".repeat(32) } })).status, 404);
  });

  it("gives each club its own key, and a deleted club a new one", async () => {
    const h = await harness();
    const hash = "ab".repeat(32);

    assert.notEqual(await h.relay.passOf("staff", "acme-rc", hash), await h.relay.passOf("staff", "brookside", hash));

    await publish(h, "checkin", checkinDoc());
    const pass = (await call(h, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "4321" } })).body.pass as string;

    assert.equal((await call(h, "DELETE", "/api/v1/c/acme-rc", { token: ACME_TOKEN })).status, 200);
    await publish(h, "checkin", checkinDoc());

    // Same PIN, published again - but a pass from before the delete opens nothing.
    const stale = await call(h, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pass } });
    assert.equal(stale.status, 403);
    assert.equal((await call(h, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "4321" } })).status, 200);
    void BROOK_TOKEN;
  });
});

/** A MemoryStore whose writes to one document, or whose club delete, wait until let go. */
class GatedStore extends MemoryStore {
  held: string | undefined;
  holdDelete = false;
  private release: (() => void) | undefined;
  private reached: (() => void) | undefined;
  readonly arrived = new Promise<void>((resolve) => (this.reached = resolve));

  letGo(): void {
    this.release?.();
  }

  private gate(): Promise<void> {
    this.reached?.();
    return new Promise<void>((resolve) => (this.release = resolve));
  }

  override async putSnapshot(club: string, channel: string, doc: StoredDocument): Promise<void> {
    if (channel === this.held) {
      this.held = undefined;
      await this.gate();
    }

    return super.putSnapshot(club, channel, doc);
  }

  override async deleteClub(club: string): Promise<void> {
    if (this.holdDelete) {
      this.holdDelete = false;
      await this.gate();
    }

    return super.deleteClub(club);
  }
}

const tick = (ms = 30) => new Promise((resolve) => setTimeout(resolve, ms));

describe("deleting a club while other writes are under way", () => {
  for (const [doc, route, pin, guard] of [
    ["signup", "signup/signin", "11111111", "signupguard"],
    ["manage", "manage/signin", "111111", "managepinguard"],
  ] as const) {
    it(`leaves no ${guard} file behind from a wrong guess saved during the delete`, async () => {
      const store = new GatedStore();
      const h = await harness(store);
      await publish(h, doc, doc === "signup" ? signupDoc() : manageDoc());

      store.held = guard;
      const guess = call(h, "POST", `/api/v1/c/acme-rc/${route}`, { json: { pin } });
      await store.arrived;

      const deleting = call(h, "DELETE", "/api/v1/c/acme-rc", { token: ACME_TOKEN });
      await tick();
      store.letGo();

      assert.equal((await guess).status, 403);
      assert.equal((await deleting).status, 200);
      await tick();

      assert.equal(await store.getSnapshot("acme-rc", guard), undefined);
    });
  }

  it("refuses a sign-up that waited for the lock behind the club's delete, and writes nothing", async () => {
    const store = new GatedStore();
    const h = await harness(store);
    await publish(h, "signup", signupDoc());
    const pass = (await call(h, "POST", "/api/v1/c/acme-rc/signup/signin", { json: { pin: "48291357" } })).body.pass as string;

    store.holdDelete = true;
    const deleting = call(h, "DELETE", "/api/v1/c/acme-rc", { token: ACME_TOKEN });
    await store.arrived;

    const submit = call(h, "POST", "/api/v1/c/acme-rc/signup/submit", { json: { pass, form } });
    await tick();
    store.letGo();

    assert.equal((await deleting).status, 200);
    assert.equal((await submit).status, 409);
    assert.equal(await store.getSnapshot("acme-rc", "signupbox"), undefined);
  });

  it("refuses a manager request that waited for the lock behind the club's delete, and writes nothing", async () => {
    const store = new GatedStore();
    const h = await harness(store);
    await publish(h, "manage", manageDoc());
    const pass = (await call(h, "POST", "/api/v1/c/acme-rc/manage/signin", { json: { pin: "654321" } })).body.pass as string;

    store.holdDelete = true;
    const deleting = call(h, "DELETE", "/api/v1/c/acme-rc", { token: ACME_TOKEN });
    await store.arrived;

    const request = call(h, "POST", "/api/v1/c/acme-rc/manage/request", { json: { pass, reqId: "abcdefgh1", op: "hello" } });
    await tick();
    store.letGo();

    assert.equal((await deleting).status, 200);
    assert.equal((await request).status, 403);
    assert.equal(await store.getSnapshot("acme-rc", "managequeue"), undefined);
  });

  it("refuses a sign-up for an invite the desk cancelled while it waited for the lock", async () => {
    const store = new GatedStore();
    const h = await harness(store);
    await publish(h, "signup", signupDoc());
    const pass = (await call(h, "POST", "/api/v1/c/acme-rc/signup/signin", { json: { pin: "48291357" } })).body.pass as string;

    // The desk's publish takes the lock and is held mid-write; the submit queues behind it.
    store.held = "signup";
    const cancelling = call(h, "POST", `/api/v1/c/acme-rc/publish/signup?seq=${seq++}`, {
      token: ACME_TOKEN,
      json: { ...signupDoc(), invites: [{ h: signupPinHash("acme-rc", "99999999"), x: LATER }] },
    });
    await store.arrived;

    const submit = call(h, "POST", "/api/v1/c/acme-rc/signup/submit", { json: { pass, form } });
    await tick();
    store.letGo();

    assert.equal((await cancelling).status, 200);
    assert.equal((await submit).status, 403);
    const inbox = await call(h, "GET", "/api/v1/c/acme-rc/signup/inbox", { token: ACME_TOKEN });
    assert.deepEqual(inbox.body.items, []);
  });
});
