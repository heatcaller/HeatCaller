import assert from "node:assert/strict";
import { createHmac } from "node:crypto";
import { after, before, describe, it } from "node:test";
import { managePinHash } from "../src/server.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * The proof a phone puts on a manager request, and the relay's one job with
 * it: carry it, unchanged, and check nothing.
 *
 * The relay CANNOT check it - the key belongs to the laptop and the phone,
 * and was deliberately never given to the relay, because a proof this relay
 * could verify would be a proof this relay could forge. What is tested here
 * is that it arrives at the laptop exactly as the phone sent it, that a
 * malformed one is dropped rather than half-carried, and that the recipe
 * the manager page signs with is the recipe the laptop verifies with.
 */

const DEVICE = "0123456789abcdef01234567";
const KEY = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

/** What the manager page's sign() does, and what RelayManagerDevices.Sign does. Spelled out here rather than imported: two copies that must agree is the point. */
function sign(club: string, deviceId: string, key: string, reqId: string, sentAt: string, payload: string): string {
  const text = ["hg-manage-mac1", club, deviceId, reqId, sentAt, payload].join("\n");

  return createHmac("sha256", Buffer.from(key, "hex")).update(text, "utf8").digest("hex");
}

describe("the signature recipe", () => {
  // The vector RelayManagerDevices.cs carries in SignedText's own comment.
  // Both ends compute it independently and must land on the same hex, the
  // same arrangement managePinHash has with RelayManage.ManagePinHash.
  it("is HMAC-SHA256 over the scheme, club, device, reqId, time and payload, newline separated", () => {
    assert.equal(
      sign("acme-rc", DEVICE, KEY, "req-1", "2026-09-25T10:00:00.000Z", '{"op":"Hello","args":{}}'),
      "f4d1ecd7e3b432f34b3b3541037b91f4577f0938e78faf4011daa76ead0440a4",
    );
  });

  it("is bound to the club, the device and the request, so no signature can be lifted onto another", () => {
    const base = sign("acme-rc", DEVICE, KEY, "req-1", "2026-09-25T10:00:00.000Z", '{"op":"Hello","args":{}}');

    assert.notEqual(base, sign("brookside", DEVICE, KEY, "req-1", "2026-09-25T10:00:00.000Z", '{"op":"Hello","args":{}}'));
    assert.notEqual(base, sign("acme-rc", "ffffffffffffffffffffffff", KEY, "req-1", "2026-09-25T10:00:00.000Z", '{"op":"Hello","args":{}}'));
    assert.notEqual(base, sign("acme-rc", DEVICE, KEY, "req-2", "2026-09-25T10:00:00.000Z", '{"op":"Hello","args":{}}'));
    assert.notEqual(base, sign("acme-rc", DEVICE, KEY, "req-1", "2026-09-25T10:05:00.000Z", '{"op":"Hello","args":{}}'));
    assert.notEqual(base, sign("acme-rc", DEVICE, KEY, "req-1", "2026-09-25T10:00:00.000Z", '{"op":"DeleteEverything","args":{}}'));
  });
});

describe("a signed manager request", () => {
  let harness: Harness;
  let seq = 1;

  before(async () => {
    harness = await start();

    const answer = await call(harness, "POST", `/api/v1/c/acme-rc/publish/manage?seq=${seq++}`, {
      token: ACME_TOKEN,
      json: { enabled: true, pinSha256: managePinHash("acme-rc", "654321") },
    });

    assert.equal(answer.status, 200);
  });

  after(async () => {
    await harness.close();
  });

  async function signedIn(): Promise<string> {
    const answer = await call(harness, "POST", "/api/v1/c/acme-rc/manage/signin", { json: { pin: "654321" } });
    assert.equal(answer.status, 200, JSON.stringify(answer.body));
    return answer.body.pass as string;
  }

  async function inboxItem(reqId: string) {
    const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/manage/inbox", { token: ACME_TOKEN });
    assert.equal(inbox.status, 200);
    return (inbox.body.items as { reqId: string }[]).find((item) => item.reqId === reqId) as Record<string, unknown> | undefined;
  }

  it("reaches the laptop with the payload, device, time and signature exactly as sent", async () => {
    const pass = await signedIn();
    const reqId = "signed-through-1";
    const sentAt = "2026-09-20T12:00:01.000Z";
    const payload = '{"op":"Hello","args":{}}';
    const mac = sign("acme-rc", DEVICE, KEY, reqId, sentAt, payload);

    const sent = await call(harness, "POST", "/api/v1/c/acme-rc/manage/request", {
      json: { pass, reqId, op: "Hello", args: {}, payload, deviceId: DEVICE, sentAt, mac },
    });

    assert.equal(sent.status, 200, JSON.stringify(sent.body));

    const item = await inboxItem(reqId);

    assert.ok(item, "the request should be waiting in the inbox");
    assert.equal(item["payload"], payload);
    assert.equal(item["deviceId"], DEVICE);
    assert.equal(item["sentAt"], sentAt);
    assert.equal(item["mac"], mac);
  });

  it("carries the payload byte for byte, spacing and key order included", async () => {
    const pass = await signedIn();
    const reqId = "signed-through-2";
    const sentAt = "2026-09-20T12:00:02.000Z";

    // Deliberately not what the relay would produce if it rebuilt this:
    // different key order, a space after the colon. The laptop verifies
    // against these bytes, so anything that reshapes them breaks the
    // signature - which is exactly what this is here to catch.
    const payload = '{"args": {"name":"Dave"}, "op":"AddDriver"}';
    const mac = sign("acme-rc", DEVICE, KEY, reqId, sentAt, payload);

    const sent = await call(harness, "POST", "/api/v1/c/acme-rc/manage/request", {
      json: { pass, reqId, op: "AddDriver", args: { name: "Dave" }, payload, deviceId: DEVICE, sentAt, mac },
    });

    assert.equal(sent.status, 200, JSON.stringify(sent.body));

    const item = await inboxItem(reqId);

    assert.equal(item?.["payload"], payload);
  });

  it("drops a half-proof rather than passing part of one on", async () => {
    const pass = await signedIn();
    const reqId = "half-proof";

    // A payload with no signature is text nobody vouched for. Carried, the
    // laptop would refuse it with a message about an unenrolled phone,
    // which is not what went wrong; dropped, it is simply an unsigned
    // request and says so.
    const sent = await call(harness, "POST", "/api/v1/c/acme-rc/manage/request", {
      json: { pass, reqId, op: "Hello", args: {}, payload: '{"op":"Hello","args":{}}', deviceId: DEVICE },
    });

    assert.equal(sent.status, 200, JSON.stringify(sent.body));

    const item = await inboxItem(reqId);

    assert.ok(item, "the request itself still goes through");
    assert.equal(item["payload"], undefined);
    assert.equal(item["deviceId"], undefined);
    assert.equal(item["mac"], undefined);
  });

  it("drops proof fields of the wrong shape, so nothing unbounded is stored on the club's quota", async () => {
    const pass = await signedIn();
    const reqId = "bad-shape";

    const sent = await call(harness, "POST", "/api/v1/c/acme-rc/manage/request", {
      json: {
        pass,
        reqId,
        op: "Hello",
        args: {},
        payload: "x".repeat(200 * 1024),
        deviceId: "not-hex",
        sentAt: "2026-09-20T12:00:03.000Z",
        mac: "short",
      },
    });

    // Refused for size by readBody, or taken with the proof dropped -
    // either is correct, and neither stores the oversized field.
    if (sent.status === 200) {
      const item = await inboxItem(reqId);
      assert.equal(item?.["payload"], undefined);
      assert.equal(item?.["mac"], undefined);
    } else {
      assert.ok(sent.status === 400 || sent.status === 413, `unexpected status ${sent.status}`);
    }
  });

  it("still takes an unsigned request, for every club that has not enrolled a phone", async () => {
    const pass = await signedIn();
    const reqId = "unsigned-still-fine";

    const sent = await call(harness, "POST", "/api/v1/c/acme-rc/manage/request", {
      json: { pass, reqId, op: "Hello", args: {} },
    });

    assert.equal(sent.status, 200, JSON.stringify(sent.body));

    const item = await inboxItem(reqId);

    assert.ok(item, "the request should be waiting in the inbox");
    assert.deepEqual(item["body"], { op: "Hello", args: {} });
    assert.equal(item["mac"], undefined);
  });
});
