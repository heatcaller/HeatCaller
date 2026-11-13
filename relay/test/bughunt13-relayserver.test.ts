import assert from "node:assert/strict";
import { randomBytes, createECDH } from "node:crypto";
import { mkdtemp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { request as httpRequest, type ClientRequest, type Server } from "node:http";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { b64url, PUSH_CHANNEL, PushService, type PushPoster, type PushSub } from "../src/push.js";
import { Relay } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { API, BILL, openEverything, setSwitch, startDriver, type DriverHarness } from "./driverexp-r1-helpers.js";
import { ACME_TOKEN, call, tenants, type Harness } from "./helpers.js";

/**
 * BUG HUNT 13 (7 Nov 2026): the relay server's car photo route, the push
 * key, push sign-ups written over, and sign-ups left behind.
 */

const CAR = "33333333333333333333333333333333";
const JPEG = Buffer.concat([Buffer.from([0xff, 0xd8, 0xff, 0xe0]), Buffer.alloc(60, 7)]).toString("base64");
const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

function sub(endpoint: string): PushSub {
  const ecdh = createECDH("prime256v1");
  ecdh.generateKeys();
  return { endpoint, p256dh: b64url(ecdh.getPublicKey()), auth: b64url(randomBytes(16)), since: "2026-11-07T19:00:00Z" };
}

/** A photo body sent a little at a time - chunked, no Content-Length - and never finished until told. */
function trickle(base: string, address?: string): { req: ClientRequest; status: Promise<number> } {
  const url = new URL(`${base}${API}/driver/photo`);
  let req!: ClientRequest;
  const from: Record<string, string> = address === undefined ? {} : { "fly-client-ip": address };
  const status = new Promise<number>((resolve) => {
    req = httpRequest({ host: url.hostname, port: url.port, path: url.pathname, method: "POST", headers: { "content-type": "application/json", ...from } }, (res) => {
      res.resume();
      resolve(res.statusCode ?? 0);
    });
    req.on("error", () => resolve(0));
  });
  req.write(`{"id":"${BILL}","car":"${CAR}","data":"`);
  return { req, status };
}

describe("bug hunt 13: a car photo does not lift the 30-second ceiling for just anybody", () => {
  let h: DriverHarness;

  beforeEach(async () => {
    h = await startDriver();
  });

  afterEach(async () => {
    await h.close();
  });

  it("is refused before its body is read when the club is not taking asks", async () => {
    await openEverything(h, false);

    const held = trickle(h.base);
    await wait(100);
    assert.equal((h.relay as unknown as { photoUploadsInFlight: number }).photoUploadsInFlight, 0, "never read as an upload");
    held.req.end("x\"}");
    assert.equal(await held.status, 404);
  });

  it("reads only a few at once across the relay, and frees the place when one stops", async () => {
    await openEverything(h);

    // Six different addresses (Bug Hunt 17): one address is now held to
    // two of the six places - see bughunt17-server.test.ts.
    process.env["FLY_APP_NAME"] = "test";

    try {
      const held = Array.from({ length: 6 }, (_, i) => trickle(h.base, `10.13.0.${i + 1}`));
      await wait(200);
      assert.equal((h.relay as unknown as { photoUploadsInFlight: number }).photoUploadsInFlight, 6);

      const busy = await call(h, "POST", `${API}/driver/photo`, { json: { id: BILL, car: CAR, data: JPEG } });
      assert.equal(busy.status, 429);

      for (const one of held) {
        one.req.destroy();
      }

      await wait(200);
      assert.equal((h.relay as unknown as { photoUploadsInFlight: number }).photoUploadsInFlight, 0);
      assert.equal((await call(h, "POST", `${API}/driver/photo`, { json: { id: BILL, car: CAR, data: JPEG } })).status, 200);
    } finally {
      delete process.env["FLY_APP_NAME"];
    }
  });
});

describe("bug hunt 13: the relay's push key is never made over a key that would not read", () => {
  let dir: string;

  beforeEach(async () => {
    dir = await mkdtemp(join(tmpdir(), "hc-vapid-"));
  });

  afterEach(async () => {
    await rm(dir, { recursive: true, force: true });
  });

  it("makes one only when there is none, and keeps it", async () => {
    const keyFile = join(dir, ".push", "vapid.json");
    const first = await new PushService({ store: new MemoryStore(), keyFile, subject: "https://relay.example" }).publicKey();
    const again = await new PushService({ store: new MemoryStore(), keyFile, subject: "https://relay.example" }).publicKey();
    assert.equal(again, first);
  });

  it("refuses, and leaves alone, a key file that will not read or is not a key - then reads it once it can", async () => {
    const keyFile = join(dir, "vapid.json");
    const good = join(dir, "good.json");
    const goodKey = await new PushService({ store: new MemoryStore(), keyFile: good, subject: "https://relay.example" }).publicKey();

    // Unreadable: a folder where the file should be (EISDIR).
    await mkdir(keyFile);
    const push = new PushService({ store: new MemoryStore(), keyFile, subject: "https://relay.example" });
    await assert.rejects(push.publicKey());

    // Not a key: left exactly as it was.
    await rm(keyFile, { recursive: true });
    await writeFile(keyFile, '{"x":"half');
    await assert.rejects(push.publicKey());
    assert.equal(await readFile(keyFile, "utf8"), '{"x":"half');

    // Readable again: the same service tries again rather than keeping its failure.
    await writeFile(keyFile, await readFile(good, "utf8"));
    assert.equal(await push.publicKey(), goodKey);
  });
});

describe("bug hunt 13: push sign-ups are not written over by an alert being sent", () => {
  const club = "acme-rc";
  const OLD = "https://fcm.googleapis.com/fcm/send/old";
  const NEW = "https://web.push.apple.com/new";
  const KEPT = "https://updates.push.services.mozilla.com/kept";

  /** A push service that answers only when let go: gone (410) for OLD, accepted for the rest. */
  function gated() {
    let release!: () => void;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    let posting!: () => void;
    const started = new Promise<void>((resolve) => {
      posting = resolve;
    });
    const post: PushPoster = async (url) => {
      posting();
      await gate;
      return { status: url === OLD ? 410 : 201 };
    };
    return { post, release, started };
  }

  const message = { title: "Your race is next", body: "Q1", tag: "hc-race", url: "/c/acme-rc/mynight" };

  it("keeps a phone that signed up while a gone one was being found out", async () => {
    const store = new MemoryStore();
    const g = gated();
    const push = new PushService({ store, subject: "https://relay.example", post: g.post });

    await push.subscribe(club, BILL, sub(OLD));
    const sending = push.send(club, BILL, message);
    await g.started;

    assert.equal(await push.subscribe(club, BILL, sub(NEW)), "ok");
    g.release();
    await sending;

    const kept = await store.getItem(club, PUSH_CHANNEL, BILL);
    const endpoints = ((kept?.body as { subs: PushSub[] }).subs ?? []).map((s) => s.endpoint);
    assert.deepEqual(endpoints, [NEW]);
  });

  it("puts nothing back once the club's sign-ups are forgotten", async () => {
    const store = new MemoryStore();
    const g = gated();
    const push = new PushService({ store, subject: "https://relay.example", post: g.post });

    await push.subscribe(club, BILL, sub(KEPT));
    await push.subscribe(club, BILL, sub(OLD));
    const sending = push.send(club, BILL, message);
    await g.started;

    await push.forget(club, () => store.deleteClub(club));
    g.release();
    await sending;

    assert.equal(await store.getItem(club, PUSH_CHANNEL, BILL), undefined);
  });
});

describe("bug hunt 13: closing drivers' nights forgets the phones signed up for alerts", () => {
  let server: Server;
  let base: string;
  let store: MemoryStore;
  const h = () => ({ base }) as unknown as Harness;
  const phoneJson = (endpoint: string) => {
    const s = sub(endpoint);
    return { endpoint: s.endpoint, expirationTime: null, keys: { p256dh: s.p256dh, auth: s.auth } };
  };

  beforeEach(async () => {
    store = new MemoryStore();
    const post: PushPoster = async () => ({ status: 201 });
    const relay = new Relay({ store, tenants: tenants(), log: () => {}, push: new PushService({ store, subject: "https://relay.example", post }) });
    server = relay.listen(0, "127.0.0.1");
    await new Promise<void>((resolve) => server.once("listening", resolve));
    const a = server.address();
    base = `http://127.0.0.1:${typeof a === "object" && a ? a.port : 0}`;

    assert.equal((await call(h(), "POST", `${API}/nights/open`, { token: ACME_TOKEN, json: { open: true } })).status, 200);
    const live = { at: "2026-10-22T19:00:00Z", drivers: { [BILL]: { next: { race: "Q2 Heat 1", away: 2 } } } };
    assert.equal((await call(h(), "POST", `${API}/publish/driverlive?seq=1`, { token: ACME_TOKEN, json: live })).status, 200);
  });

  afterEach(async () => {
    await new Promise<void>((resolve) => server.close(() => resolve()));
  });

  it("takes every sign-up off when the switch goes off, and takes none while it is off", async () => {
    const up = await call(h(), "POST", `${API}/push/subscribe`, { json: { id: BILL, sub: phoneJson("https://fcm.googleapis.com/fcm/send/abc") } });
    assert.equal(up.status, 200);
    assert.equal((await store.listItems("acme-rc", PUSH_CHANNEL)).length, 1);

    await setSwitch({ base } as unknown as DriverHarness, "nights/open", false);
    assert.equal((await store.listItems("acme-rc", PUSH_CHANNEL)).length, 0);

    const closed = await call(h(), "POST", `${API}/push/subscribe`, { json: { id: BILL, sub: phoneJson("https://fcm.googleapis.com/fcm/send/def") } });
    assert.equal(closed.status, 404);
    assert.equal((await store.listItems("acme-rc", PUSH_CHANNEL)).length, 0);
  });

  it("does not count phones signed up for alerts against the club's results quota", async () => {
    // Most of the 50 MB taken by sign-ups, put straight into the store -
    // their own ceiling would never let phones send this much.
    await store.putItem("acme-rc", PUSH_CHANNEL, BILL, { seq: 0, publishedAt: "2026-11-07T19:00:00Z", body: { subs: [], pad: "p".repeat(50 * 1024 * 1024) } });

    const result = await call(h(), "POST", `${API}/publish/results/r1?seq=2`, { token: ACME_TOKEN, json: { a: 1 } });
    assert.equal(result.status, 200);
  });
});
