import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, it } from "node:test";
import { askFieldsFrom, DRIVER_ASK_SETUP_MAX_BODY } from "../src/driverasks.js";
import { photoBytesOf } from "../src/garage.js";
import { ACME_TOKEN, call } from "./helpers.js";
import { API, BILL, OFF, openEverything, SAM, samPass, setSwitch, startDriver, type DriverHarness } from "./driverexp-r1-helpers.js";

/**
 * A DRIVER'S OWN GARAGE FROM THEIR PHONE (5 Nov 2026, asked for by Asher -
 * claude/garage-relay-contract.md): the garage document the laptop
 * publishes, the new kinds of change, and car photos on their way to the
 * laptop.
 */

const PACK = "0123456789abcdef0123456789abcdef";
const CAR = "33333333333333333333333333333333";
const RACE = "fedcba9876543210fedcba9876543210";

/** The smallest thing that passes for a JPEG: its marker and some bytes. */
const JPEG = Buffer.concat([Buffer.from([0xff, 0xd8, 0xff, 0xe0]), Buffer.alloc(60, 7)]).toString("base64");

let h: DriverHarness;

beforeEach(async () => {
  h = await startDriver();
});

afterEach(async () => {
  await h.close();
});

const ask = (json: unknown) => call(h, "POST", `${API}/driver/ask`, { json });
const garage = (json: unknown) => call(h, "POST", `${API}/driver/garage`, { json });
const photo = (json: unknown) => call(h, "POST", `${API}/driver/photo`, { json });
const putGarage = (id: string, body: unknown, token = ACME_TOKEN) =>
  call(h, "POST", `${API}/publish/garage/${id}?seq=${Date.now()}`, { token, json: body });

describe("checking a change from the garage page", () => {
  it("takes a pack added, edited, retired or removed, with its fields cleaned", () => {
    assert.deepEqual(askFieldsFrom("packedit", { op: "add", pack: PACK, label: "  Blue  2 ", chem: "LiPo", notes: "Bought\r\nOctober", x: 1 }, false), {
      ok: true,
      f: { op: "add", pack: PACK, label: "Blue 2", chem: "LiPo", notes: "Bought\nOctober" },
    });
    assert.deepEqual(askFieldsFrom("packedit", { op: "retire", pack: PACK }, false), { ok: true, f: { op: "retire", pack: PACK } });
    assert.equal(askFieldsFrom("packedit", { op: "add", pack: PACK }, false).ok, false, "a pack needs a label");
    assert.equal(askFieldsFrom("packedit", { op: "burn", pack: PACK }, false).ok, false);
    assert.equal(askFieldsFrom("packedit", { op: "edit", pack: "Blue" }, false).ok, false);
    assert.equal(askFieldsFrom("packedit", { op: "edit", pack: PACK, label: "a".repeat(41) }, false).ok, false);
  });

  it("takes the pack a race was run on", () => {
    assert.deepEqual(askFieldsFrom("pack", { pack: PACK, race: RACE }, false), { ok: true, f: { pack: PACK, race: RACE } });
    assert.equal(askFieldsFrom("pack", { pack: PACK, race: "Heat 2" }, false).ok, false);
  });

  it("takes every field of a car, and a photo by its laptop name", () => {
    const check = askFieldsFrom("car", {
      op: "add", car: CAR, name: "MR-03 Red", chassis: "", chassisOther: "Atomic", cls: "Stock", body: "911", number: "27", notes: "New tyres",
    }, false);

    assert.deepEqual(check, {
      ok: true,
      f: { op: "add", car: CAR, name: "MR-03 Red", chassisOther: "Atomic", cls: "Stock", body: "911", number: "27", notes: "New tyres", chassis: "" },
    });
    assert.equal(askFieldsFrom("car", { op: "add", car: CAR }, false).ok, false, "a car needs a name");
    assert.equal(askFieldsFrom("car", { op: "edit", car: CAR, chassis: "Kyosho" }, false).ok, false);
    assert.equal(askFieldsFrom("car", { op: "edit", car: CAR, number: "123456789" }, false).ok, false);
    assert.deepEqual(askFieldsFrom("car", { op: "cover", car: CAR, photo: "a1b2c3d4e5f6.jpg" }, false), {
      ok: true,
      f: { op: "cover", car: CAR, photo: "a1b2c3d4e5f6.jpg" },
    });
    assert.equal(askFieldsFrom("car", { op: "unphoto", car: CAR, photo: "../garage.json" }, false).ok, false);
  });

  it("takes a whole setup sheet, field by field, and the short log on a car", () => {
    assert.deepEqual(askFieldsFrom("setup", { car: CAR, sheet: PACK, race: RACE, "v.front-spring": " Soft ", "v.toe": "", notes: "Loose" }, false), {
      ok: true,
      f: { car: CAR, sheet: PACK, race: RACE, notes: "Loose", "v.front-spring": "Soft" },
    });
    assert.deepEqual(askFieldsFrom("setup", { car: CAR, tires: "Sweep 32" }, false), { ok: true, f: { car: CAR, tires: "Sweep 32" } });
    assert.equal(askFieldsFrom("setup", { car: CAR, sheet: PACK }, false).ok, false, "an empty sheet");
    assert.equal(askFieldsFrom("setup", { car: CAR, sheet: PACK, "v.bad id!": "x" }, false).ok, false);
    assert.equal(askFieldsFrom("setup", { car: CAR, sheet: PACK, "v.x": "a".repeat(81) }, false).ok, false);

    const many: Record<string, string> = { car: CAR, sheet: PACK };
    for (let i = 0; i < 81; i++) many[`v.f${i}`] = "1";
    assert.equal(askFieldsFrom("setup", many, false).ok, false, "more values than a sheet has");
  });

  it("reads a photo's size from its base64 and wants a JPEG", () => {
    assert.deepEqual(photoBytesOf(JPEG), { ok: true, bytes: 64 });
    assert.equal(photoBytesOf(Buffer.alloc(64, 1).toString("base64")).ok, false);
    assert.equal(photoBytesOf("not base64 at all!!").ok, false);
    assert.equal(photoBytesOf("A".repeat(700_000)).ok, false);
  });
});

describe("the garage document", () => {
  it("is refused while driver requests are closed, and read back only through the ask gate", async () => {
    await openEverything(h, false);
    assert.equal((await putGarage(BILL, { v: 1, driver: "Bill" })).status, 403);

    await setSwitch(h, "driver/open", true);
    assert.equal((await putGarage(BILL, { v: 1, driver: "Bill" })).status, 200);

    const read = await garage({ id: BILL });
    assert.equal(read.status, 200);
    assert.deepEqual(read.body.garage, { v: 1, driver: "Bill" });
    assert.equal(read.body.pin, false);
    assert.equal(read.body.signedIn, false);

    // One answer for a stranger and a code off the list.
    assert.deepEqual((await garage({ id: OFF })).body, { ok: false, error: "Not found" });
    assert.deepEqual((await garage({ id: "nonsense" })).body, { ok: false, error: "Not found" });

    // Never public.
    assert.notEqual((await call(h, "GET", `${API}/snapshot/garage`)).status, 200);
  });

  it("says whether the driver has a PIN and whether the pass is theirs", async () => {
    await openEverything(h);
    await putGarage(SAM, { v: 1 });

    assert.equal((await garage({ id: SAM })).body.pin, true);
    assert.equal((await garage({ id: SAM })).body.signedIn, false);
    assert.equal((await garage({ id: SAM, pass: await samPass(h) })).body.signedIn, true);
  });

  it("goes when requests close", async () => {
    await openEverything(h);
    await putGarage(BILL, { v: 1 });
    await setSwitch(h, "driver/open", false);
    await setSwitch(h, "driver/open", true);

    assert.equal((await garage({ id: BILL })).body.garage, null);
  });
});

describe("changes the laptop applies by itself", () => {
  it("need the personal PIN of a driver who has one", async () => {
    await openEverything(h);

    for (const [kind, f] of [
      ["pack", { pack: PACK }],
      ["packedit", { op: "add", pack: PACK, label: "Blue" }],
      ["car", { op: "add", car: CAR, name: "Red" }],
      ["setup", { car: CAR, tires: "Soft" }],
    ] as const) {
      const without = await ask({ id: SAM, kind, f });
      assert.equal(without.status, 403, kind);
      assert.equal(without.body.error, "Sign in with your personal PIN first.");
    }

    const pass = await samPass(h);
    assert.equal((await ask({ id: SAM, pass, kind: "car", f: { op: "add", car: CAR, name: "Red" } })).status, 200);

    // A driver with no PIN uses the badge, as before.
    assert.equal((await ask({ id: BILL, kind: "packedit", f: { op: "add", pack: PACK, label: "Blue" } })).status, 200);
  });

  it("take a whole sheet past the other kinds' two kilobytes, and no further", async () => {
    await openEverything(h);

    const f: Record<string, string> = { car: CAR, sheet: PACK };
    for (let i = 0; i < 80; i++) f[`v.field-${i}`] = "x".repeat(60);

    assert.equal((await ask({ id: BILL, kind: "setup", f })).status, 200);

    const big = { car: CAR, sheet: PACK, notes: "x".repeat(DRIVER_ASK_SETUP_MAX_BODY) };
    assert.equal((await ask({ id: BILL, kind: "setup", f: big })).status, 400);

    // A pack with a padded body is still held to two.
    assert.equal((await ask({ id: BILL, kind: "pack", f: { pack: PACK }, pad: "x".repeat(3000) })).status, 400);
  });
});

describe("car photos on their way to the laptop", () => {
  it("are held for the laptop, listed without their pictures, fetched one at a time and confirmed away", async () => {
    await openEverything(h);

    const sent = await photo({ id: BILL, car: CAR, data: JPEG });
    assert.equal(sent.status, 200);
    assert.match(sent.body.ref, /^[0-9A-F]{6}$/);

    const list = await call(h, "GET", `${API}/driver/photos`, { token: ACME_TOKEN });
    assert.equal(list.status, 200);
    assert.equal(list.body.items.length, 1);
    assert.equal(list.body.items[0].b, BILL);
    assert.equal(list.body.items[0].car, CAR);
    assert.equal(list.body.items[0].data, undefined);

    const id = list.body.items[0].id;
    const one = await call(h, "GET", `${API}/driver/photo/${id}`, { token: ACME_TOKEN });
    assert.equal(one.body.data, JPEG);

    assert.equal((await call(h, "POST", `${API}/driver/photos/collected`, { token: ACME_TOKEN, json: { ids: [id] } })).body.cleared, 1);
    assert.equal((await call(h, "GET", `${API}/driver/photos`, { token: ACME_TOKEN })).body.items.length, 0);
  });

  it("need the laptop's token to read", async () => {
    await openEverything(h);
    assert.equal((await call(h, "GET", `${API}/driver/photos`)).status, 401);
  });

  it("refuse what is not a JPEG, a car that is not an id, and a stranger", async () => {
    await openEverything(h);

    assert.equal((await photo({ id: BILL, car: CAR, data: Buffer.alloc(64, 1).toString("base64") })).status, 400);
    assert.equal((await photo({ id: BILL, car: "red", data: JPEG })).status, 400);
    assert.equal((await photo({ id: OFF, car: CAR, data: JPEG })).status, 404);
  });

  it("need the PIN of a driver who has one, and hold six per driver", async () => {
    await openEverything(h);

    assert.equal((await photo({ id: SAM, car: CAR, data: JPEG })).status, 403);

    for (let i = 0; i < 6; i++) {
      assert.equal((await photo({ id: BILL, car: CAR, data: JPEG })).status, 200);
    }

    assert.equal((await photo({ id: BILL, car: CAR, data: JPEG })).status, 429);
  });

  it("go when requests close", async () => {
    await openEverything(h);
    await photo({ id: BILL, car: CAR, data: JPEG });
    await setSwitch(h, "driver/open", false);

    assert.equal((await call(h, "GET", `${API}/driver/photos`, { token: ACME_TOKEN })).body.items.length, 0);
  });
});
