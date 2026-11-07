import assert from "node:assert/strict";
import { createDecipheriv, createECDH, createPublicKey, hkdfSync, randomBytes, verify } from "node:crypto";
import type { Server } from "node:http";
import { afterEach, beforeEach, describe, it } from "node:test";
import {
  b64url,
  encryptForPush,
  fromB64url,
  liveAlerts,
  nightAlerts,
  pushHostAllowed,
  pushSubFrom,
  PushService,
  vapidHeader,
  type PushPoster,
} from "../src/push.js";
import { Relay } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, call, tenants, type Harness } from "./helpers.js";

/**
 * PUSH ALERTS (6 Nov 2026, Asher: "Build real push alerts") - see push.ts.
 */

const BILL = "fedcba9876543210fedc";
const NOBODY = "aaaaaaaaaaaaaaaaaaaa";
const API = "/api/v1/c/acme-rc";

/** A phone: its own key pair and auth secret, as a browser makes them. */
function phone(endpoint = "https://fcm.googleapis.com/fcm/send/abc123") {
  const ecdh = createECDH("prime256v1");
  ecdh.generateKeys();
  const auth = randomBytes(16);
  return {
    ecdh,
    auth,
    json: { endpoint, expirationTime: null, keys: { p256dh: b64url(ecdh.getPublicKey()), auth: b64url(auth) } },
  };
}

/** What the phone's browser does with a message: RFC 8291, the other way. */
function decrypt(body: Buffer, p: ReturnType<typeof phone>): string {
  const salt = body.subarray(0, 16);
  const idLength = body[20] as number;
  const asPublic = body.subarray(21, 21 + idLength);
  const sealed = body.subarray(21 + idLength);

  const shared = p.ecdh.computeSecret(asPublic);
  const keyInfo = Buffer.concat([Buffer.from("WebPush: info\0", "latin1"), p.ecdh.getPublicKey(), asPublic]);
  const ikm = Buffer.from(hkdfSync("sha256", shared, p.auth, keyInfo, 32));
  const cek = Buffer.from(hkdfSync("sha256", ikm, salt, Buffer.from("Content-Encoding: aes128gcm\0", "latin1"), 16));
  const nonce = Buffer.from(hkdfSync("sha256", ikm, salt, Buffer.from("Content-Encoding: nonce\0", "latin1"), 12));

  const decipher = createDecipheriv("aes-128-gcm", cek, nonce);
  decipher.setAuthTag(sealed.subarray(sealed.length - 16));
  const plain = Buffer.concat([decipher.update(sealed.subarray(0, sealed.length - 16)), decipher.final()]);
  assert.equal(plain[plain.length - 1], 2, "the last record's delimiter");
  return plain.subarray(0, plain.length - 1).toString("utf8");
}

describe("push: the message is for that one phone (RFC 8291)", () => {
  it("encrypts so the phone, and only the phone, reads it back", () => {
    const p = phone();
    const body = encryptForPush(Buffer.from('{"title":"Your race is next"}'), { p256dh: p.json.keys.p256dh, auth: p.json.keys.auth });

    assert.equal(body.readUInt32BE(16), 4096, "record size");
    assert.equal(body[20], 65, "the relay's throwaway public key follows");
    assert.equal(decrypt(body, p), '{"title":"Your race is next"}');

    const other = phone();
    assert.throws(() => decrypt(body, other), "another phone cannot");
  });

  it("is different every time, even for the same words", () => {
    const p = phone();
    const sub = { p256dh: p.json.keys.p256dh, auth: p.json.keys.auth };
    assert.notDeepEqual(encryptForPush(Buffer.from("x"), sub), encryptForPush(Buffer.from("x"), sub));
  });
});

describe("push: the relay's signed note to the push service (RFC 8292)", () => {
  it("is signed with the relay's key, for that service, for twelve hours", async () => {
    const service = new PushService({ store: new MemoryStore(), subject: "https://relay.example" });
    const keys = await service.vapid();
    const now = new Date("2026-11-06T20:00:00Z");
    const header = vapidHeader(keys, "https://fcm.googleapis.com/fcm/send/abc", "https://relay.example", now);

    const m = /^vapid t=([^.]+)\.([^.]+)\.([^,]+), k=(.+)$/.exec(header);
    assert.ok(m);
    const [, head, claims, sig, k] = m as unknown as string[];
    assert.equal(k, await service.publicKey());

    const c = JSON.parse(fromB64url(claims as string).toString("utf8")) as { aud: string; exp: number; sub: string };
    assert.deepEqual(c, { aud: "https://fcm.googleapis.com", exp: Math.floor(now.getTime() / 1000) + 12 * 3600, sub: "https://relay.example" });

    const raw = fromB64url(k as string);
    const publicKey = createPublicKey({ key: { kty: "EC", crv: "P-256", x: b64url(raw.subarray(1, 33)), y: b64url(raw.subarray(33)) }, format: "jwk" });
    assert.ok(verify("sha256", Buffer.from(`${head}.${claims}`), { key: publicKey, dsaEncoding: "ieee-p1363" }, fromB64url(sig as string)));
  });
});

describe("push: only the push services", () => {
  it("takes Google's, Apple's, Mozilla's and Microsoft's push addresses and nothing else", () => {
    for (const ok of [
      "https://fcm.googleapis.com/fcm/send/x",
      "https://web.push.apple.com/QGx",
      "https://api.push.apple.com/x",
      "https://updates.push.services.mozilla.com/wpush/v2/x",
      "https://wns2-par02p.notify.windows.com/w/?token=x",
    ]) {
      assert.equal(pushHostAllowed(ok), true, ok);
    }

    for (const bad of [
      "http://fcm.googleapis.com/fcm/send/x",
      "https://fcm.googleapis.com:8443/x",
      "https://evil.example/fcm.googleapis.com",
      "https://fcm.googleapis.com.evil.example/x",
      "https://127.0.0.1/x",
      "https://user:pw@fcm.googleapis.com/x",
      "not a url",
    ]) {
      assert.equal(pushHostAllowed(bad), false, bad);
    }
  });

  it("checks the phone's keys are the right shape", () => {
    const p = phone();
    assert.ok(pushSubFrom(p.json, "now"));
    assert.equal(pushSubFrom({ ...p.json, keys: { ...p.json.keys, auth: b64url(randomBytes(8)) } }, "now"), undefined);
    assert.equal(pushSubFrom({ ...p.json, keys: { ...p.json.keys, p256dh: b64url(randomBytes(65)) } }, "now"), undefined);
    assert.equal(pushSubFrom({ ...p.json, endpoint: "https://evil.example/x" }, "now"), undefined);
    assert.equal(pushSubFrom("a sub", "now"), undefined);
  });
});

describe("push: when to say something - the page's own rules", () => {
  const url = "/c/acme-rc/mynight";

  it("their race becoming next, or staged - and never on the first look", () => {
    const far = { next: { race: "Q2 Heat 1", away: 2, slot: 3 } };
    const next = { next: { race: "Q2 Heat 1", away: 0, slot: 3 } };
    const staged = { next: { race: "Q2 Heat 1", away: 0, slot: 3, now: true, staged: true } };
    const green = { next: { race: "Q2 Heat 1", away: 0, slot: 3, now: true, staged: false } };

    assert.deepEqual(liveAlerts(undefined, next, url), [], "nothing to compare with");
    assert.deepEqual(liveAlerts(far, next, url), [{ title: "Your race is next", body: "Q2 Heat 1 · grid slot 3", tag: "hc-race", url }]);
    assert.deepEqual(liveAlerts(next, next, url), [], "unchanged");
    assert.equal(liveAlerts(next, staged, url)[0]?.title, "Your race is staged - to the driver stand");
    assert.deepEqual(liveAlerts(staged, green, url), [], "going green is not a call-up");
    assert.deepEqual(liveAlerts({ next: { race: "A", away: 3 } }, far, url), [], "still further off");
  });

  it("their marshal duty becoming next", () => {
    const before = { marshal: { race: "Q2 Heat 3", post: "Turn 2", away: 1 } };
    const after = { marshal: { race: "Q2 Heat 3", post: "Turn 2", away: 0 } };
    assert.deepEqual(liveAlerts(before, after, url), [{ title: "Marshal duty next", body: "Q2 Heat 3 - Turn 2", tag: "hc-marshal", url }]);
    assert.deepEqual(liveAlerts(after, after, url), []);
  });

  it("a race newly on their night, a demo said so", () => {
    const one = { races: [{ label: "Q1 Heat 1", pos: 2, of: 6 }] };
    const two = { races: [...one.races, { label: "Q2 Heat 1", pos: 1, of: 6 }] };
    assert.deepEqual(nightAlerts(one, two, url), [{ title: "Your result is in", body: "Q2 Heat 1 - 1st of 6", tag: "hc-result", url }]);
    assert.deepEqual(nightAlerts(undefined, one, url)[0]?.body, "Q1 Heat 1 - 2nd of 6", "the first race of the night");
    assert.deepEqual(nightAlerts(two, two, url), [], "a republish of the same night");
    assert.equal(nightAlerts(one, { races: [...one.races, { label: "Q1 Heat 1", pos: 1, of: 4, kind: "demo" }] }, url)[0]?.title, "Your result is in (demo)");
    assert.equal(nightAlerts(one, { races: [...one.races, { label: "Q3", out: "DNF" }] }, url)[0]?.body, "Q3 - DNF");
  });
});

describe("push: through the relay", () => {
  let server: Server;
  let base: string;
  let store: MemoryStore;
  let sent: { url: string; headers: Record<string, string>; body: Buffer }[];
  let answer: number;
  let seq: number;

  // call() only reads base from the harness it is handed.
  const h = () => ({ base }) as unknown as Harness;
  const publish = (path: string, json: unknown) => call(h(), "POST", `${API}/publish/${path}?seq=${seq++}`, { token: ACME_TOKEN, json });
  const live = (entry: unknown) => publish("driverlive", { at: "2026-10-22T19:00:00Z", drivers: { [BILL]: entry } });
  // The send is not waited for by the publish; give it a moment.
  const settle = () => new Promise((resolve) => setTimeout(resolve, 50));

  beforeEach(async () => {
    store = new MemoryStore();
    sent = [];
    answer = 201;
    seq = 1;
    const post: PushPoster = async (url, init) => {
      sent.push({ url, headers: init.headers, body: Buffer.from(init.body) });
      return { status: answer };
    };
    const relay = new Relay({ store, tenants: tenants(), log: () => {}, push: new PushService({ store, subject: "https://relay.example", post }) });
    server = relay.listen(0, "127.0.0.1");
    await new Promise<void>((resolve) => server.once("listening", resolve));
    const a = server.address();
    base = `http://127.0.0.1:${typeof a === "object" && a ? a.port : 0}`;

    assert.equal((await call(h(), "POST", `${API}/nights/open`, { token: ACME_TOKEN, json: { open: true } })).status, 200);
    assert.equal((await live({ next: { race: "Q2 Heat 1 (Stock)", away: 2, slot: 3 } })).status, 200);
  });

  afterEach(async () => {
    await new Promise<void>((resolve) => server.close(() => resolve()));
  });

  it("hands out the key and the service worker", async () => {
    const key = await call(h(), "GET", `${API}/push/key`);
    assert.equal(key.status, 200);
    assert.equal(fromB64url(key.body.key).length, 65);

    const worker = await call(h(), "GET", "/c/acme-rc/sw.js");
    assert.equal(worker.status, 200);
    assert.match(worker.headers["content-type"] ?? "", /javascript/);
    assert.match(worker.text, /showNotification/);

    const page = await call(h(), "GET", "/c/acme-rc/night");
    assert.match(page.headers["content-security-policy"] ?? "", /worker-src 'self'/);
  });

  it("signs a driver's phone up and tells it when their race is next - with the page closed", async () => {
    const p = phone();
    const up = await call(h(), "POST", `${API}/push/subscribe`, { json: { id: BILL, sub: p.json } });
    assert.equal(up.status, 200);

    await live({ next: { race: "Q2 Heat 1 (Stock)", away: 0, slot: 3 } });
    await settle();

    assert.equal(sent.length, 1);
    assert.equal(sent[0]!.url, p.json.endpoint);
    assert.equal(sent[0]!.headers["Content-Encoding"], "aes128gcm");
    assert.match(sent[0]!.headers["Authorization"] ?? "", /^vapid t=.+, k=.+$/);
    const message = JSON.parse(decrypt(sent[0]!.body, p)) as Record<string, string>;
    assert.deepEqual(message, { title: "Your race is next", body: "Q2 Heat 1 (Stock) · grid slot 3", tag: "hc-race", url: "/c/acme-rc/mynight" });
    assert.doesNotMatch(JSON.stringify(message), new RegExp(BILL), "never the badge code");

    // The same again is nothing new.
    await live({ next: { race: "Q2 Heat 1 (Stock)", away: 0, slot: 3 } });
    await settle();
    assert.equal(sent.length, 1);
  });

  it("tells them their result is in when a race is filed on their night - a demo too", async () => {
    const p = phone();
    await call(h(), "POST", `${API}/push/subscribe`, { json: { id: BILL, sub: p.json } });

    await publish(`nights/${BILL}`, { races: [{ label: "Q1 Heat 1", pos: 2, of: 4, kind: "demo" }] });
    await settle();

    assert.equal(sent.length, 1);
    assert.equal((JSON.parse(decrypt(sent[0]!.body, p)) as { title: string }).title, "Your result is in (demo)");
  });

  it("refuses a code that is not a driver tonight, and a push address that is not a push service", async () => {
    const p = phone();
    assert.equal((await call(h(), "POST", `${API}/push/subscribe`, { json: { id: NOBODY, sub: p.json } })).status, 404);
    assert.equal((await call(h(), "POST", `${API}/push/subscribe`, { json: { id: "nope", sub: p.json } })).status, 404);

    const evil = phone("https://evil.example/hook");
    assert.equal((await call(h(), "POST", `${API}/push/subscribe`, { json: { id: BILL, sub: evil.json } })).status, 400);

    await live({ next: { race: "Q2 Heat 1 (Stock)", away: 0 } });
    await settle();
    assert.equal(sent.length, 0);
  });

  it("stops when the phone signs off, and forgets a phone the push service says is gone", async () => {
    const a = phone("https://fcm.googleapis.com/fcm/send/aaa");
    const b = phone("https://web.push.apple.com/bbb");
    await call(h(), "POST", `${API}/push/subscribe`, { json: { id: BILL, sub: a.json } });
    await call(h(), "POST", `${API}/push/subscribe`, { json: { id: BILL, sub: b.json } });

    assert.equal((await call(h(), "POST", `${API}/push/unsubscribe`, { json: { id: BILL, sub: { endpoint: a.json.endpoint } } })).status, 200);

    answer = 410;
    await live({ next: { race: "Q2 Heat 1 (Stock)", away: 0 } });
    await settle();
    assert.deepEqual(sent.map((s) => s.url), [b.json.endpoint]);
    assert.equal(await store.getItem("acme-rc", "pushsubs", BILL), undefined, "gone, so forgotten");
  });
});
