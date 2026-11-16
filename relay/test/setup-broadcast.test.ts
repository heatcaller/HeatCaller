import assert from "node:assert/strict";
import { createECDH, randomBytes } from "node:crypto";
import type { Server } from "node:http";
import { afterEach, beforeEach, describe, it } from "node:test";
import {
  b64url,
  BROADCAST_TTL_SECONDS,
  PUSH_TTL_MAX_SECONDS,
  PUSH_TTL_SECONDS,
  PushService,
  type PushPoster,
  type PushSub,
} from "../src/push.js";
import { broadcastFrom, PUSH_BROADCASTS_PER_HOUR, Relay } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, BROOK_TOKEN, call, tenants, type Harness } from "./helpers.js";

/**
 * "RACING'S OFF" BY PUSH (12 Nov 2026, setup plan step 7): the club's
 * laptop sends one message to every phone signed up for the club's alerts.
 */

const CLUB = "acme-rc";
const API = `/api/v1/c/${CLUB}`;
const BILL = "fedcba9876543210fedc";
const SAM = "0123456789abcdef0123";
const ZOE = "aaaaaaaaaaaaaaaaaaaa";

function sub(endpoint: string): PushSub {
  const ecdh = createECDH("prime256v1");
  ecdh.generateKeys();
  return { endpoint, p256dh: b64url(ecdh.getPublicKey()), auth: b64url(randomBytes(16)), since: new Date().toISOString() }; // now: a sign-up unused for 180 days is forgotten (15 Nov 2026)
}

interface Posted {
  readonly url: string;
  readonly headers: Record<string, string>;
}

/** A push service that answers by address: anything with "refuse" is refused, "gone" is gone. */
function fakePost(posted: Posted[]): PushPoster {
  return async (url, init) => {
    posted.push({ url, headers: init.headers });
    return { status: url.includes("refuse") ? 400 : url.includes("gone") ? 410 : 201 };
  };
}

const message = { title: "Racing's off", body: "Thursday is cancelled - the track is flooded.", tag: "nextnight", url: "/c/acme-rc/mynight" };

describe("PushService.sendAll", () => {
  it("sends to every signed-up driver's phones and counts the ones that failed", async () => {
    const store = new MemoryStore();
    const posted: Posted[] = [];
    const push = new PushService({ store, subject: "https://relay.example", post: fakePost(posted) });

    await push.subscribe(CLUB, BILL, sub("https://fcm.googleapis.com/fcm/send/bill1"));
    await push.subscribe(CLUB, BILL, sub("https://web.push.apple.com/bill2-refuse"));
    await push.subscribe(CLUB, SAM, sub("https://fcm.googleapis.com/fcm/send/sam-gone"));
    await push.subscribe(CLUB, ZOE, sub("https://updates.push.services.mozilla.com/wpush/v2/zoe"));
    // Another club's phone is never told.
    await push.subscribe("brookside", BILL, sub("https://fcm.googleapis.com/fcm/send/brook"));

    const outcome = await push.sendAll(CLUB, message);

    assert.deepEqual(outcome, { badges: 3, sent: 2, failed: 2 });
    assert.equal(posted.length, 4);
    assert.ok(posted.every((p) => !p.url.includes("brook")));
    assert.equal(await store.getItem(CLUB, "pushsubs", SAM), undefined, "a gone phone is forgotten");
  });

  it("answers zero for a club with nobody signed up", async () => {
    const posted: Posted[] = [];
    const push = new PushService({ store: new MemoryStore(), subject: "https://relay.example", post: fakePost(posted) });

    assert.deepEqual(await push.sendAll(CLUB, message), { badges: 0, sent: 0, failed: 0 });
    assert.equal(posted.length, 0);
  });

  it("keeps going past a driver whose phones could not be read", async () => {
    const store = new MemoryStore();
    const posted: Posted[] = [];
    const push = new PushService({ store, subject: "https://relay.example", post: fakePost(posted) });
    await push.subscribe(CLUB, BILL, sub("https://fcm.googleapis.com/fcm/send/bill1"));
    await push.subscribe(CLUB, SAM, sub("https://fcm.googleapis.com/fcm/send/sam1"));

    const getItem = store.getItem.bind(store);
    store.getItem = async (club, channel, id) => {
      if (channel === "pushsubs" && id === BILL) {
        throw new Error("EMFILE");
      }

      return getItem(club, channel, id);
    };

    assert.deepEqual(await push.sendAll(CLUB, message), { badges: 2, sent: 1, failed: 1 });
  });
});

describe("how long a message waits for a phone that is off", () => {
  it("is ten minutes for a race call, six hours for a broadcast, never past a day", async () => {
    const posted: Posted[] = [];
    const push = new PushService({ store: new MemoryStore(), subject: "https://relay.example", post: fakePost(posted) });
    await push.subscribe(CLUB, BILL, sub("https://fcm.googleapis.com/fcm/send/bill1"));

    assert.equal(PUSH_TTL_SECONDS, 600);
    assert.equal(BROADCAST_TTL_SECONDS, 6 * 3600);

    await push.send(CLUB, BILL, message);
    await push.sendAll(CLUB, message);
    await push.send(CLUB, BILL, message, 7 * 24 * 3600);

    assert.deepEqual(
      posted.map((p) => p.headers["TTL"]),
      ["600", String(6 * 3600), String(PUSH_TTL_MAX_SECONDS)],
    );
  });
});

describe("checking the laptop's message", () => {
  it("takes a title, a body and an optional tag", () => {
    assert.deepEqual(broadcastFrom({ title: " Racing's off ", body: "No racing Thursday." }), {
      title: "Racing's off",
      body: "No racing Thursday.",
      tag: "nextnight",
    });
    assert.deepEqual(broadcastFrom({ title: "Off", body: "x", tag: "night-2026_11" }), { title: "Off", body: "x", tag: "night-2026_11" });
  });

  it("refuses anything else", () => {
    for (const bad of [
      {},
      { body: "x" },
      { title: "", body: "x" },
      { title: "   ", body: "x" },
      { title: "t".repeat(61), body: "x" },
      { title: "t", body: "" },
      { title: "t", body: "b".repeat(241) },
      { title: 5, body: "x" },
      { title: "t", body: ["x"] },
      { title: "t\nx", body: "x" },
      { title: "t", body: "x", tag: "" },
      { title: "t", body: "x", tag: "t".repeat(41) },
      { title: "t", body: "x", tag: "has space" },
      { title: "t", body: "x", tag: 3 },
      { title: "t", body: "x", url: "https://evil.example" },
    ]) {
      assert.equal(typeof broadcastFrom(bad as Record<string, unknown>), "string", JSON.stringify(bad));
    }

    assert.equal(typeof broadcastFrom({ title: "t".repeat(60), body: "b".repeat(240), tag: "t".repeat(40) }), "object");
  });
});

describe("POST push/broadcast", () => {
  let server: Server;
  let base: string;
  let push: PushService;
  let posted: Posted[];
  let clock: number;
  const h = () => ({ base }) as unknown as Harness;
  const broadcast = (json: unknown, token: string | null = ACME_TOKEN) =>
    call(h(), "POST", `${API}/push/broadcast`, token === null ? { json } : { token, json });
  const good = { title: "Racing's off", body: "Thursday is cancelled - the track is flooded." };
  // The relay answers before the phones are all sent to: a moment for them.
  const sendsSettle = () => new Promise<void>((resolve) => setTimeout(resolve, 50));

  async function serve(withPush: boolean): Promise<void> {
    const store = new MemoryStore();
    posted = [];
    clock = Date.parse("2026-11-12T18:00:00Z");
    push = new PushService({ store, subject: "https://relay.example", post: fakePost(posted) });
    const relay = new Relay({
      store,
      tenants: tenants(),
      now: () => new Date(clock),
      log: () => {},
      push: withPush ? push : undefined,
    });
    server = relay.listen(0, "127.0.0.1");
    await new Promise<void>((resolve) => server.once("listening", resolve));
    const a = server.address();
    base = `http://127.0.0.1:${typeof a === "object" && a ? a.port : 0}`;
  }

  afterEach(async () => {
    await new Promise<void>((resolve) => server.close(() => resolve()));
  });

  describe("with alerts working", () => {
    beforeEach(async () => {
      await serve(true);
      await push.subscribe(CLUB, BILL, sub("https://fcm.googleapis.com/fcm/send/bill1"));
      await push.subscribe(CLUB, BILL, sub("https://web.push.apple.com/bill2"));
      await push.subscribe(CLUB, SAM, sub("https://fcm.googleapis.com/fcm/send/sam-refuse"));
    });

    it("tells every signed-up phone and answers the counts", async () => {
      const answer = await broadcast(good);
      assert.equal(answer.status, 200);
      // Answered with the phones it is going to, not waiting for each (bug hunt 21 #26).
      assert.deepEqual(answer.body, { ok: true, badges: 2, queued: 3 });
      await sendsSettle();
      assert.equal(posted.length, 3);
      assert.ok(posted.every((p) => p.headers["TTL"] === String(BROADCAST_TTL_SECONDS)));
    });

    it("is the laptop's alone: no token, another club's token, or a junk token is refused", async () => {
      const none = await broadcast(good, null);
      assert.equal(none.status, 401);
      assert.deepEqual(none.body, { ok: false, error: "A publish token is required." });

      const other = await broadcast(good, BROOK_TOKEN);
      assert.equal(other.status, 403);
      assert.deepEqual(other.body, { ok: false, error: "That token does not belong to this club." });

      assert.equal((await broadcast(good, "not-a-token")).status, 401);
      // A phone's badge code is no credential here.
      assert.equal((await broadcast({ ...good, id: BILL }, BILL)).status, 401);
      assert.equal(posted.length, 0);
    });

    it("refuses a bad body, and a refused body costs none of the allowance", async () => {
      for (const bad of [{ title: "", body: "x" }, { title: "t", body: "b".repeat(241) }, { ...good, tag: "a b" }, { ...good, extra: 1 }, [good], "text"]) {
        const answer = await broadcast(bad);
        assert.equal(answer.status, 400, JSON.stringify(bad));
        assert.equal(answer.body.ok, false);
        assert.equal(typeof answer.body.error, "string");
      }

      const raw = await call(h(), "POST", `${API}/push/broadcast`, { token: ACME_TOKEN, raw: "{not json" });
      assert.equal(raw.status, 400);
      assert.deepEqual(raw.body, { ok: false, error: "That could not be read." });
      assert.equal(posted.length, 0);

      for (let i = 0; i < PUSH_BROADCASTS_PER_HOUR; i++) {
        assert.equal((await broadcast(good)).status, 200);
      }
    });

    it("takes three an hour per club", async () => {
      assert.equal(PUSH_BROADCASTS_PER_HOUR, 3);

      for (let i = 0; i < 3; i++) {
        assert.equal((await broadcast(good)).status, 200, `broadcast ${i + 1}`);
      }

      const fourth = await broadcast(good);
      assert.equal(fourth.status, 429);
      assert.equal(fourth.body.ok, false);
      await sendsSettle();
      assert.equal(posted.length, 9, "the fourth told nobody");

      clock += 60 * 60 * 1000 + 1;
      assert.equal((await broadcast(good)).status, 200, "an hour on, it is allowed again");
    });

    it("answers zero counts when nobody is signed up", async () => {
      // As closing drivers' nights does (forgetPushSubs).
      await push.forget(CLUB);
      const answer = await broadcast(good);
      assert.equal(answer.status, 200);
      assert.deepEqual(answer.body, { ok: true, badges: 0, queued: 0 });
    });
  });

  it("answers 503 when this relay does not send alerts", async () => {
    await serve(false);
    const answer = await broadcast(good);
    assert.equal(answer.status, 503);
    assert.equal(answer.body.ok, false);
    assert.match(answer.body.error, /not working/);

    assert.equal((await broadcast(good, null)).status, 401, "the token is still asked for first");
  });
});
