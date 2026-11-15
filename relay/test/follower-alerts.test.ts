import assert from "node:assert/strict";
import { createECDH, randomBytes } from "node:crypto";
import type { Server } from "node:http";
import { afterEach, beforeEach, describe, it } from "node:test";
import { b64url, FOLLOW_CHANNEL, FOLLOW_SUBS_PER_NAME, followIdOf, followNameFrom, followProgrammeAlerts, followResultAlerts, PushService, type PushPoster } from "../src/push.js";
import { Relay } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { homePage } from "../src/pages.js";
import { BROOK_TOKEN, call, tenants, type Harness } from "./helpers.js";

/**
 * FOLLOWER ALERTS (14 Nov 2026, plans/obs-overlay-plan.md 7.1, Asher:
 * "follower alerts-yes"): a phone following a driver by name, with no badge
 * code, told before that driver races and when their result is in.
 */

const API = "/api/v1/c/brookside";

function phone(endpoint: string) {
  const ecdh = createECDH("prime256v1");
  ecdh.generateKeys();
  return { endpoint, keys: { p256dh: b64url(ecdh.getPublicKey()), auth: b64url(randomBytes(16)) } };
}

const line = (id: string, status: string, drivers: string[], extra: Record<string, unknown> = {}) =>
  ({ id, className: "Junior", description: id, status, drivers, ...extra });
const programme = (lines: unknown[], lineups = true) => ({ lineups, lines });

describe("what a follower hears", () => {
  it("says when their race has just become the next one, once", () => {
    const before = programme([line("Heat 1", "running", ["Sam Tr"]), line("Heat 2", "upcoming", ["Mia K"])]);
    const after = programme([line("Heat 1", "done", ["Sam Tr"]), line("Heat 2", "next", ["Mia K"], { estimate: "about 19:40" })]);

    assert.deepEqual(followProgrammeAlerts(before, after, "Mia K", "/c/brookside/"), [
      { title: "Mia K's race is next", body: "Junior - Heat 2, about 19:40", tag: "hc-race", url: "/c/brookside/" },
    ]);
    assert.deepEqual(followProgrammeAlerts(after, after, "Mia K", "/c/brookside/"), [], "already next: nothing again");
    assert.deepEqual(followProgrammeAlerts(undefined, after, "Mia K", "/c/brookside/"), [], "never on the first look");
  });

  it("matches the name in any case, and says nothing without line-ups", () => {
    const before = programme([line("Heat 2", "upcoming", ["Mia K"])]);
    const after = programme([line("Heat 2", "next", ["Mia K"])]);

    assert.equal(followProgrammeAlerts(before, after, "mia k", "/").length, 1);
    assert.equal(followProgrammeAlerts(before, programme([line("Heat 2", "next", ["Mia K"])], false), "Mia K", "/").length, 0);
  });

  it("says where they finished, in plain words, for a new result only", () => {
    const result = { label: "Junior - Heat 2", results: [{ pos: 1, name: "Sam Tr" }, { pos: 3, name: "Mia K" }] };

    assert.deepEqual(followResultAlerts(undefined, result, "Mia K", "/r"), [
      { title: "Mia K finished 3rd", body: "Junior - Heat 2", tag: "hc-result", url: "/r" },
    ]);
    assert.deepEqual(followResultAlerts(result, result, "Mia K", "/r"), [], "a corrected result is not a new one");
    assert.deepEqual(followResultAlerts(undefined, result, "Ollie", "/r"), []);
  });

  it("tidies a name, and keys it without putting the name in a file name", () => {
    assert.equal(followNameFrom("  Mia   K \u0007"), "Mia K");
    assert.equal(followNameFrom("x".repeat(61)), "");
    assert.equal(followNameFrom(42), "");
    assert.match(followIdOf("Mia K"), /^f[0-9a-f]{32}$/);
    assert.equal(followIdOf("Mia K"), followIdOf("mia k"));
  });
});

describe("signing up to follow, and the alerts going out", () => {
  let server: Server;
  let base: string;
  let store: MemoryStore;
  let sent: { url: string }[];
  let seq: number;
  const h = () => ({ base }) as unknown as Harness;
  const publish = (path: string, json: unknown) => call(h(), "POST", `${API}/publish/${path}?seq=${seq++}`, { token: BROOK_TOKEN, json });
  const settle = () => new Promise((resolve) => setTimeout(resolve, 60));

  beforeEach(async () => {
    store = new MemoryStore();
    sent = [];
    seq = 1;
    const post: PushPoster = async (url) => {
      sent.push({ url });
      return { status: 201 };
    };
    const relay = new Relay({ store, tenants: tenants(), log: () => {}, push: new PushService({ store, subject: "https://relay.example", post }) });
    server = relay.listen(0, "127.0.0.1");
    await new Promise<void>((resolve) => server.once("listening", resolve));
    const a = server.address();
    base = `http://127.0.0.1:${typeof a === "object" && a ? a.port : 0}`;
  });

  afterEach(async () => {
    await new Promise<void>((resolve) => server.close(() => resolve()));
  });

  it("takes a name the running order has, and refuses one it doesn't", async () => {
    await publish("programme", programme([line("Heat 2", "upcoming", ["Mia K"])]));

    const ok = await call(h(), "POST", `${API}/push/follow/subscribe`, { json: { name: "Mia K", sub: phone("https://fcm.googleapis.com/fcm/send/grandma") } });
    assert.equal(ok.status, 200);

    const no = await call(h(), "POST", `${API}/push/follow/subscribe`, { json: { name: "Somebody Else", sub: phone("https://fcm.googleapis.com/fcm/send/x") } });
    assert.equal(no.status, 404);
    assert.match(no.body.error, /isn't in the club's running order today/);

    const kept = await store.getItem("brookside", FOLLOW_CHANNEL, followIdOf("Mia K"));
    assert.equal((kept?.body as { name: string }).name, "Mia K");
  });

  it("tells a follower their driver's race is next, and their result", async () => {
    await publish("programme", programme([line("Heat 2", "upcoming", ["Mia K"])]));
    await call(h(), "POST", `${API}/push/follow/subscribe`, { json: { name: "Mia K", sub: phone("https://fcm.googleapis.com/fcm/send/grandma") } });

    await publish("programme", programme([line("Heat 2", "next", ["Mia K"])]));
    await settle();
    assert.equal(sent.length, 1, "race next");

    await publish("results/s-heat-2", { label: "Junior - Heat 2", results: [{ pos: 2, name: "Mia K" }] });
    await settle();
    assert.equal(sent.length, 2, "result");
  });

  it("stops when the phone signs off", async () => {
    await publish("programme", programme([line("Heat 2", "upcoming", ["Mia K"])]));
    const p = phone("https://fcm.googleapis.com/fcm/send/grandma");
    await call(h(), "POST", `${API}/push/follow/subscribe`, { json: { name: "Mia K", sub: p } });
    await call(h(), "POST", `${API}/push/follow/unsubscribe`, { json: { name: "Mia K", sub: { endpoint: p.endpoint } } });

    await publish("programme", programme([line("Heat 2", "next", ["Mia K"])]));
    await settle();
    assert.equal(sent.length, 0);
  });

  it(`keeps at most ${FOLLOW_SUBS_PER_NAME} phones on one name`, async () => {
    await publish("programme", programme([line("Heat 2", "upcoming", ["Mia K"])]));

    for (let i = 0; i < FOLLOW_SUBS_PER_NAME + 2; i++) {
      await call(h(), "POST", `${API}/push/follow/subscribe`, { json: { name: "Mia K", sub: phone(`https://fcm.googleapis.com/fcm/send/p${i}`) } });
    }

    const kept = await store.getItem("brookside", FOLLOW_CHANNEL, followIdOf("Mia K"));
    assert.equal((kept?.body as { subs: unknown[] }).subs.length, FOLLOW_SUBS_PER_NAME);
  });
});

describe("the button on Home", () => {
  it("offers Tell me before Mia races on the followed driver's card", () => {
    const html = homePage("brookside", "Brookside");
    assert.match(html, /'Tell me before ' \+ who \+ ' races'/);
    assert.match(html, /folPost\('\/push\/follow\/subscribe', \{ name, sub: sub\.toJSON\(\), kinds: \['race', 'result'\] \}\)/);
    assert.match(html, /Add to Home Screen/);
  });
});
