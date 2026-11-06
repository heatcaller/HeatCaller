import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, it } from "node:test";
import { askFieldsFrom, askRefOf, DRIVER_ASKS_PER_CLUB, DRIVER_ASKS_PER_DRIVER } from "../src/driverasks.js";
import { ACME_TOKEN, BROOK_TOKEN, call } from "./helpers.js";
import {
  API,
  BILL,
  checkinDoc,
  OFF,
  openEverything,
  publish,
  SAM,
  samPass,
  setSwitch,
  startDriver,
  type DriverHarness,
} from "./driverexp-r1-helpers.js";

/**
 * A DRIVER ASKING THE DESK FROM THEIR PHONE (22 Oct 2026, driver
 * experience, R1): details, a battery pack, a result query. Held on the
 * relay until the laptop collects them; nothing here changes anything.
 */

const PACK = "0123456789abcdef0123456789abcdef";
const RACE = "fedcba9876543210fedcba9876543210";

let h: DriverHarness;

beforeEach(async () => {
  h = await startDriver();
});

afterEach(async () => {
  await h.close();
});

const ask = (json: unknown) => call(h, "POST", `${API}/driver/ask`, { json });
const inbox = (token = ACME_TOKEN) => call(h, "GET", `${API}/driver/inbox`, { token });

describe("checking what a phone sent", () => {
  it("takes the details fields and drops anything else", () => {
    const check = askFieldsFrom(
      "details",
      { announcer: "  Zhock  ", pronouns: "she/her", about: "Loves\nthe hairpin", emailMyNight: true, noPhoto: true, guardian: true, sneaky: "x" },
      false,
    );

    assert.deepEqual(check, {
      ok: true,
      f: { announcer: "Zhock", pronouns: "she/her", about: "Loves the hairpin", emailMyNight: true, noPhoto: true, guardian: true },
    });
  });

  it("only ever turns privacy ON", () => {
    for (const name of ["noPhoto", "noVideo", "initials"]) {
      const check = askFieldsFrom("details", { [name]: false }, true);
      assert.deepEqual(check, { ok: false, status: 400, error: "Ask at the desk to undo this." }, name);
    }
  });

  it("needs the PIN for an email, and a real-looking address", () => {
    assert.deepEqual(askFieldsFrom("details", { email: "sam@example.com" }, false), {
      ok: false,
      status: 403,
      error: "Changing your email needs your personal PIN - ask at the desk.",
    });
    assert.deepEqual(askFieldsFrom("details", { email: "sam@example.com" }, true), { ok: true, f: { email: "sam@example.com" } });
    assert.equal(askFieldsFrom("details", { email: "not an email" }, true).ok, false);
    assert.equal(askFieldsFrom("details", { email: `${"a".repeat(120)}@x.com` }, true).ok, false);
  });

  it("holds each text to its limit", () => {
    assert.equal(askFieldsFrom("details", { announcer: "a".repeat(40) }, false).ok, true);
    assert.equal(askFieldsFrom("details", { announcer: "a".repeat(41) }, false).ok, false);
    assert.equal(askFieldsFrom("details", { pronouns: "a".repeat(25) }, false).ok, false);
    assert.equal(askFieldsFrom("details", { about: "a".repeat(121) }, false).ok, false);
    assert.equal(askFieldsFrom("query", { race: RACE, m: "a".repeat(301) }, false).ok, false);
  });

  it("refuses details with nothing to change, and a guardian tick alone", () => {
    assert.equal(askFieldsFrom("details", {}, false).ok, false);
    assert.equal(askFieldsFrom("details", { guardian: true }, false).ok, false);
  });

  it("checks pack and race ids by shape", () => {
    assert.deepEqual(askFieldsFrom("pack", { pack: PACK.toUpperCase() }, false), { ok: true, f: { pack: PACK } });
    assert.deepEqual(askFieldsFrom("pack", { pack: "none" }, false), { ok: true, f: { pack: "none" } });
    assert.equal(askFieldsFrom("pack", { pack: "Red 2S" }, false).ok, false);
    assert.deepEqual(askFieldsFrom("query", { race: RACE, m: "I did 12 laps" }, false), { ok: true, f: { race: RACE, m: "I did 12 laps" } });
    assert.equal(askFieldsFrom("query", { race: "Q1", m: "x" }, false).ok, false);
    assert.equal(askFieldsFrom("query", { race: RACE, m: "   " }, false).ok, false);
  });

  it("quotes the first six hex, upper case", () => {
    assert.equal(askRefOf("a1b2c3d4e5f6a7b8c9d0e1f2"), "A1B2C3");
  });
});

describe("who can ask", () => {
  it("answers the same 404 for a closed club, a stranger and a code off the list", async () => {
    const want = { ok: false, error: "Not found" };
    const pack = { kind: "pack", f: { pack: PACK } };

    // Nothing open at all.
    assert.deepEqual((await ask({ id: BILL, ...pack })).body, want);

    // Nights and check-in, but not asks.
    await openEverything(h, false);
    assert.deepEqual((await ask({ id: BILL, ...pack })).body, want);

    await setSwitch(h, "driver/open", true);
    assert.equal((await ask({ id: BILL, ...pack })).status, 200, "now it is open");

    for (const id of [OFF, "nope", "", 12]) {
      const answer = await ask({ id, ...pack });
      assert.equal(answer.status, 404, String(id));
      assert.deepEqual(answer.body, want);
    }

    // Check-in closed.
    await publish(h, "checkin", checkinDoc({ enabled: false }));
    assert.deepEqual((await ask({ id: BILL, ...pack })).body, want);
    await publish(h, "checkin", checkinDoc());

    // Nights closed.
    await setSwitch(h, "nights/open", false);
    assert.deepEqual((await ask({ id: BILL, ...pack })).body, want);
  });

  it("needs a driver's personal PIN for their details when they have one", async () => {
    await openEverything(h);

    const without = await ask({ id: SAM, kind: "details", f: { pronouns: "she/her" } });
    assert.equal(without.status, 403);
    assert.equal(without.body.error, "Sign in with your personal PIN first.");

    const wrong = await ask({ id: SAM, pass: "0".repeat(64), kind: "details", f: { pronouns: "she/her" } });
    assert.equal(wrong.status, 403);

    const pass = await samPass(h);
    const right = await ask({ id: SAM, pass, kind: "details", f: { pronouns: "she/her", email: "sam@example.com" } });
    assert.equal(right.status, 200);
    assert.match(right.body.ref, /^[0-9A-F]{6}$/);
    h.advance(1000);

    // A pack is the garage's, and the garage needs no PIN (5 Nov 2026,
    // Asher: "remove the pin requirement for the garage").
    assert.equal((await ask({ id: SAM, kind: "pack", f: { pack: PACK } })).status, 200);

    const items = (await inbox()).body.items;
    assert.equal(items[0].v, "pin");
    assert.equal(items[0].f.email, "sam@example.com");
    assert.equal(items[1].v, "badge", "sent without the pass, on the badge alone");
  });

  it("lets a driver with no PIN change details by badge - but not their email", async () => {
    await openEverything(h);

    assert.equal((await ask({ id: BILL, kind: "details", f: { announcer: "Billy T" } })).status, 200);

    const email = await ask({ id: BILL, kind: "details", f: { email: "bill@example.com" } });
    assert.equal(email.status, 403);
    assert.equal(email.body.error, "Changing your email needs your personal PIN - ask at the desk.");

    // Sam's pass does not count as Bill's PIN.
    const pass = await samPass(h);
    assert.equal((await ask({ id: BILL, pass, kind: "details", f: { email: "bill@example.com" } })).status, 403);
  });

  it("refuses privacy being turned off from a phone", async () => {
    await openEverything(h);
    const answer = await ask({ id: BILL, kind: "details", f: { noPhoto: false } });

    assert.equal(answer.status, 400);
    assert.equal(answer.body.error, "Ask at the desk to undo this.");
  });

  it("refuses a body over two kilobytes and an unknown kind", async () => {
    await openEverything(h);
    assert.equal((await ask({ id: BILL, kind: "query", f: { race: RACE, m: "x".repeat(3000) } })).status, 400);
    assert.equal((await ask({ id: BILL, kind: "rename", f: {} })).status, 400);
  });
});

describe("how much can be asked", () => {
  beforeEach(async () => {
    await openEverything(h);
  });

  it("holds at most five per badge for the desk, and thirty changes beside them", async () => {
    for (let i = 0; i < DRIVER_ASKS_PER_DRIVER; i++) {
      assert.equal((await ask({ id: BILL, kind: "details", f: { announcer: `Billy ${i}` } })).status, 200);
    }

    const sixth = await ask({ id: BILL, kind: "details", f: { announcer: "Billy six" } });
    assert.equal(sixth.status, 429);
    assert.match(sixth.body.error, /5 waiting already/);

    // Pack changes have their own room (5 Nov 2026).
    for (let i = 0; i < 30; i++) {
      assert.equal((await ask({ id: BILL, kind: "pack", f: { pack: PACK } })).status, 200);
    }

    const more = await ask({ id: BILL, kind: "pack", f: { pack: PACK } });
    assert.equal(more.status, 429);
    assert.match(more.body.error, /not picked up your last changes/);
  });

  it("takes six an hour from a badge and three queries", async () => {
    for (let i = 0; i < 3; i++) {
      assert.equal((await ask({ id: BILL, kind: "query", f: { race: RACE, m: `Lap ${i}` } })).status, 200);
    }

    assert.equal((await ask({ id: BILL, kind: "query", f: { race: RACE, m: "again" } })).status, 429);

    // Collected, so nothing is held - and the hourly limit still holds.
    const ids = (await inbox()).body.items.map((item: { id: string }) => item.id);
    await call(h, "POST", `${API}/driver/collected`, { token: ACME_TOKEN, json: { ids } });

    for (let i = 0; i < 3; i++) {
      assert.equal((await ask({ id: BILL, kind: "details", f: { announcer: `Billy ${i}` } })).status, 200);
    }

    assert.equal((await ask({ id: BILL, kind: "details", f: { announcer: "Billy seven" } })).status, 429, "the seventh in an hour");

    // Changes the laptop applies by itself are counted apart (5 Nov 2026).
    assert.equal((await ask({ id: BILL, kind: "pack", f: { pack: PACK } })).status, 200);

    h.advance(61 * 60 * 1000);
    assert.equal((await ask({ id: BILL, kind: "details", f: { announcer: "Billy again" } })).status, 200);
  });

  it("takes three hundred from one address in ten minutes", async () => {
    for (let i = 0; i < 300; i++) {
      await ask({ id: OFF, kind: "pack", f: { pack: PACK } });
    }

    assert.equal((await ask({ id: BILL, kind: "pack", f: { pack: PACK } })).status, 429);
  });

  it("holds at most two hundred for the club", async () => {
    for (let i = 0; i < DRIVER_ASKS_PER_CLUB; i++) {
      await h.store.putItem("acme-rc", "driverasks", i.toString(16).padStart(24, "0"), {
        seq: i,
        publishedAt: "2026-10-22T18:00:00Z",
        body: { b: "11111111111111111111", k: "pack", v: "badge", f: { pack: "none" }, at: "2026-10-22T18:00:00Z" },
      });
    }

    assert.equal((await ask({ id: BILL, kind: "pack", f: { pack: PACK } })).status, 503);
  });
});

describe("the laptop's side", () => {
  beforeEach(async () => {
    await openEverything(h);
  });

  it("collects every ask oldest first, and confirms them away", async () => {
    await ask({ id: BILL, kind: "pack", f: { pack: PACK } });
    h.advance(1000);
    const second = await ask({ id: BILL, kind: "query", f: { race: RACE, m: "I was 2nd" } });

    const answer = await inbox();
    assert.equal(answer.status, 200);
    const items = answer.body.items as { id: string; b: string; k: string; v: string; f: Record<string, unknown>; at: string }[];
    assert.equal(items.length, 2);
    assert.deepEqual(
      items.map((item) => [item.b, item.k, item.v]),
      [[BILL, "pack", "badge"], [BILL, "query", "badge"]],
    );
    assert.equal(items[1]?.id.slice(0, 6).toUpperCase(), second.body.ref);
    assert.deepEqual(items[1]?.f, { race: RACE, m: "I was 2nd" });
    assert.equal(items[0]?.at, "2026-10-22T19:00:00.000Z");

    const cleared = await call(h, "POST", `${API}/driver/collected`, { token: ACME_TOKEN, json: { ids: [items[0]?.id, "junk", 7] } });
    assert.deepEqual(cleared.body, { ok: true, cleared: 1 });
    assert.equal((await inbox()).body.items.length, 1);
  });

  it("needs the club's own token for every laptop route", async () => {
    assert.equal((await call(h, "GET", `${API}/driver/inbox`)).status, 401);
    assert.equal((await inbox(BROOK_TOKEN)).status, 403);
    assert.equal((await call(h, "POST", `${API}/driver/collected`, { json: { ids: [] } })).status, 401);
    assert.equal((await call(h, "POST", `${API}/driver/open`, { token: BROOK_TOKEN, json: { open: true } })).status, 403);
  });

  it("closing requests deletes every ask held", async () => {
    await ask({ id: BILL, kind: "pack", f: { pack: PACK } });
    await setSwitch(h, "driver/open", false);
    assert.deepEqual((await inbox()).body.items, []);
  });

  it("the club's delete takes them too", async () => {
    await ask({ id: BILL, kind: "pack", f: { pack: PACK } });
    assert.equal((await call(h, "DELETE", API, { token: ACME_TOKEN })).status, 200);
    assert.deepEqual((await inbox()).body.items, []);
  });

  it("an ask waiting for the lock when requests close is not kept", async () => {
    let release: () => void = () => {};
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const relay = h.relay as unknown as { withLock: (key: string, work: () => Promise<void>) => Promise<void> };
    const locked = relay.withLock("acme-rc", () => held);

    const pending = ask({ id: BILL, kind: "pack", f: { pack: PACK } });
    await new Promise((resolve) => setTimeout(resolve, 50));
    // The switch written straight to the store, as the close's own write is, before its lock.
    await h.store.putSnapshot("acme-rc", "driveraskopen", { seq: 1, publishedAt: "2026-10-22T19:00:00Z", body: { open: false } });
    release();
    await locked;

    assert.equal((await pending).status, 404);
    assert.deepEqual(await h.store.listItems("acme-rc", "driverasks"), []);
  });
});

/**
 * A SETUP CHANGE FROM THE PHONE (4 Nov 2026, from the relay roadmap): the
 * sheet's five short parts and a note, for the laptop to file into the
 * Garage's setup log by itself.
 */
describe("a setup change from the driver's night", () => {
  it("keeps the parts it knows, cleaned and short, and drops the rest", () => {
    assert.deepEqual(
      askFieldsFrom("setup", { tires: "  Soft  rears ", springs: "blue", notes: "Went\u0007 up a tooth", hack: "x", motor: "" }, false),
      { ok: true, f: { tires: "Soft rears", springs: "blue", notes: "Went up a tooth" } });
  });

  it("refuses one that says nothing, or runs long, or is not text", () => {
    assert.equal(askFieldsFrom("setup", {}, false).ok, false);
    assert.equal(askFieldsFrom("setup", { tires: "   " }, false).ok, false);
    assert.equal(askFieldsFrom("setup", { tires: "x".repeat(81) }, false).ok, false);
    assert.equal(askFieldsFrom("setup", { notes: "x".repeat(301) }, false).ok, false);
    assert.equal(askFieldsFrom("setup", { gearing: 21 }, false).ok, false);
  });
});
