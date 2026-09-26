import assert from "node:assert/strict";
import { createHmac, pbkdf2Sync } from "node:crypto";
import { afterEach, beforeEach, describe, it } from "node:test";
import { managePage } from "../src/pages.js";
import { managePinHash, pinHash, PIN_KDF_ITERATIONS, racerPinHash, signupPinHash } from "../src/server.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

// Salted, slow PIN hashes (26 Sep), and the manager phone signing with a
// key made from its PIN. The vectors below are held by the laptop's tests
// too (HeatCaller.Tests/PinKdfTests.cs), worked out a third time in
// Python - the three must agree byte for byte or nobody signs in.

const SALT = "00112233445566778899aabbccddeeff".repeat(2);
const LATER = "2026-10-04T12:00:00Z";

const SAM = "0123456789abcdef0123";

describe("salted PIN hashes", () => {
  it("are PBKDF2-SHA256 at 100,000 rounds over purpose|club|salt, matching the laptop's vectors", () => {
    assert.equal(PIN_KDF_ITERATIONS, 100_000);
    assert.equal(pinHash("acme-rc", "123456", SALT), "8869916b2ae8b3a8a2f6d494751f95fbfddf32f955a04fbda124da7cc5150132");
    assert.equal(racerPinHash("acme-rc", "482913", SALT), "c42a9f466ad711daaa64bad315d08406f3b7480f2770a96dc7cf6afc66a0da52");
    assert.equal(managePinHash("acme-rc", "123456", SALT), "767e53f17da0020d7119b54479a0d36b7089c7183a2644da8ea99e7048b85fc3");
    assert.equal(signupPinHash("acme-rc", "4829 1375", SALT), "20efd8d81af04b85b5bde3dd4e152a1eb4e794d1956fead993823f9859a5efa9");
  });

  it("keep one kind of PIN from opening another, and one club's from saying anything about another's", () => {
    assert.notEqual(pinHash("acme-rc", "123456", SALT), managePinHash("acme-rc", "123456", SALT));
    assert.notEqual(pinHash("acme-rc", "123456", SALT), racerPinHash("acme-rc", "123456", SALT));
    assert.notEqual(pinHash("acme-rc", "123456", SALT), pinHash("brookside", "123456", SALT));
    assert.notEqual(pinHash("acme-rc", "123456", SALT), pinHash("acme-rc", "123456", "ff".repeat(32)));
  });

  it("fall back to the old plain hash with no salt, so a laptop not yet updated keeps working", () => {
    assert.equal(managePinHash("acme-rc", "654321"), "2652164cb7d8cbe4a0bff22a9c610b100d39e4fa4a537a8ee24de8e78a87e502");
  });
});

describe("the relay with a laptop that publishes a salt", () => {
  let harness: Harness;
  let seq = 1;

  beforeEach(async () => {
    harness = await start();
    seq = 1;
  });

  afterEach(async () => {
    await harness.close();
  });

  const publish = async (channel: string, doc: unknown): Promise<void> => {
    const answer = await call(harness, "POST", `/api/v1/c/acme-rc/publish/${channel}?seq=${seq++}`, { token: ACME_TOKEN, json: doc });
    assert.equal(answer.status, 200, JSON.stringify(answer.body));
  };

  it("opens check-in for the staff PIN and a racer's PIN, hashed with the salt", async () => {
    await publish("checkin", {
      enabled: true,
      pinSha256: pinHash("acme-rc", "4321", SALT),
      pinSalt: SALT,
      event: "Club Night",
      drivers: [{ id: SAM, name: "Sam Carter", className: "Stock", number: "7", checkedIn: false, photo: "", rp: racerPinHash("acme-rc", "482913", SALT) }],
    });

    const staff = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "4321" } });
    const racer = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "482913" } });
    const wrong = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "999999" } });

    assert.equal(staff.status, 200);
    assert.equal(staff.body.mode, "staff");
    assert.equal(racer.status, 200);
    assert.equal(racer.body.mode, "racer");
    assert.equal(wrong.status, 403);
  });

  it("does not open check-in with the old plain hash once the salt is published", async () => {
    // A document that claims a salt but carries a plain hash - the relay
    // must judge it by the salted recipe, not quietly try both.
    await publish("checkin", {
      enabled: true,
      pinSha256: pinHash("acme-rc", "4321"),
      pinSalt: SALT,
      event: "Club Night",
      drivers: [],
    });

    const staff = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "4321" } });
    assert.equal(staff.status, 403);
  });

  it("signs a manager in with the salted hash, and hands back the salt for the phone's key", async () => {
    await publish("manage", { enabled: true, pinSha256: managePinHash("acme-rc", "123456", SALT), pinSalt: SALT });

    const answer = await call(harness, "POST", "/api/v1/c/acme-rc/manage/signin", { json: { pin: "123456" } });

    assert.equal(answer.status, 200);
    assert.match(answer.body.pass, /^[0-9a-f]{64}$/);
    assert.equal(answer.body.salt, SALT);
  });

  it("hands back no salt to a laptop that publishes none", async () => {
    await publish("manage", { enabled: true, pinSha256: managePinHash("acme-rc", "123456") });

    const answer = await call(harness, "POST", "/api/v1/c/acme-rc/manage/signin", { json: { pin: "123456" } });

    assert.equal(answer.status, 200);
    assert.equal("salt" in answer.body, false);
  });

  it("finds an invite by its salted hash, and an older one still by its plain hash", async () => {
    const plainOld = signupPinHash("acme-rc", "11223344");
    const saltedNew = signupPinHash("acme-rc", "55667788", SALT);

    await publish("signup", {
      enabled: true,
      event: "Club night",
      classes: ["Stock"],
      pinSalt: SALT,
      invites: [{ h: plainOld, x: LATER }, { h: saltedNew, x: LATER }],
    });

    const old = await call(harness, "POST", "/api/v1/c/acme-rc/signup/signin", { json: { pin: "1122 3344" } });
    const fresh = await call(harness, "POST", "/api/v1/c/acme-rc/signup/signin", { json: { pin: "5566 7788" } });
    const wrong = await call(harness, "POST", "/api/v1/c/acme-rc/signup/signin", { json: { pin: "9988 7766" } });

    assert.equal(old.status, 200, JSON.stringify(old.body));
    assert.equal(fresh.status, 200, JSON.stringify(fresh.body));
    assert.equal(wrong.status, 403);
  });
});

describe("the manager page's PIN key", () => {
  // The page's own derivePinKey and sign(), taken out of the page as served
  // and run here on Node's WebCrypto - the same code a phone runs - so the
  // key and the signature are checked against the laptop's vectors, not a
  // copy of the recipe written for the test.
  const html = managePage("acme-rc", "Acme RC");

  function pageFunction(name: string): string {
    const start = html.indexOf(`function ${name}(`);
    assert.ok(start >= 0, `the page has ${name}()`);
    let depth = 0;
    for (let i = html.indexOf("{", start); i < html.length; i++) {
      if (html[i] === "{") depth++;
      if (html[i] === "}" && --depth === 0) return html.slice(start, i + 1);
    }
    throw new Error(`${name}() never closes`);
  }

  const run = new Function(
    "CLUB",
    "PIN_KDF_ROUNDS",
    `${pageFunction("hexOf")}\n${pageFunction("keyBytes")}\n${pageFunction("derivePinKey")}\n${pageFunction("sign")}\nreturn { derivePinKey, sign };`,
  ) as (club: string, rounds: number) => {
    derivePinKey(pin: string, salt: string): Promise<string>;
    sign(deviceId: string, key: string, reqId: string, sentAt: string, payload: string, scheme?: string): Promise<string>;
  };

  it("uses the laptop's round count", () => {
    assert.match(html, new RegExp(`const PIN_KDF_ROUNDS = ${PIN_KDF_ITERATIONS};`));
  });

  it("makes the key the laptop makes from the same PIN, and signs as the laptop checks", async () => {
    const page = run("acme-rc", PIN_KDF_ITERATIONS);
    const key = await page.derivePinKey("123456", SALT);

    assert.equal(key, "dfc7298c66de999315ed4b72e1219af794b8020f71b9021f49a6d45b61154588");

    const mac = await page.sign(
      "0123456789abcdef01234567",
      key,
      "req-1",
      "2026-09-25T10:00:00.000Z",
      '{"op":"Hello","args":{}}',
      "hg-manage-mac2",
    );

    assert.equal(mac, "9f61d72c1ef12a9eac91e8ee9746f99e351549988ac2fbb9aa5b4ead48e25495");
  });

  it("never makes its key the same way the relay's stored hash is made", () => {
    // What the relay holds is managePinHash; the key is a different
    // purpose over the same PIN and salt, so holding one is not holding
    // the other.
    const key = pbkdf2Sync("123456", `hg-manage-key2|acme-rc|${SALT}`, PIN_KDF_ITERATIONS, 32, "sha256").toString("hex");
    assert.notEqual(key, managePinHash("acme-rc", "123456", SALT));
    assert.equal(
      createHmac("sha256", Buffer.from(key, "hex")).update("x").digest("hex").length,
      64,
    );
  });
});
