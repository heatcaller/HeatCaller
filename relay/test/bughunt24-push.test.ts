import assert from "node:assert/strict";
import { createECDH, randomBytes } from "node:crypto";
import { describe, it } from "node:test";
import { runInNewContext } from "node:vm";
import {
  b64url,
  FOLLOW_CHANNEL,
  FOLLOW_RESULT_FRESH_MS,
  FOLLOW_SUBS_PER_NAME,
  followIdOf,
  followResultAlerts,
  followResultIsFresh,
  PushService,
  serviceWorkerFor,
  type PushSub,
} from "../src/push.js";
import { MemoryStore } from "../src/store.js";

/**
 * BUG HUNT 24 (23 Nov 2026), relay/src/push.ts:
 *  #359 a follower was told "X finished 2nd" for a result sent hours or days late;
 *  #360 ten made-up sign-ups pushed every real follower's phone off a name;
 *  #361 the page open during a deploy had no no-signal copy afterwards;
 *  #362 the worker showed the relay's 502/503 although it had a copy of the page.
 */

const MINUTE = 60 * 1000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

// ------------------------------------------------- #359: a late result is not news

describe("bug hunt 24 #359: a follower is only told of a result while it is news", () => {
  const now = Date.parse("2026-11-23T20:00:00.000Z");
  const ago = (ms: number) => new Date(now - ms).toISOString();
  const result = (times: Record<string, unknown>) => ({ label: "Heat 3", results: [{ pos: 2, name: "Sam P" }], ...times });

  it("tells of a race that has just ended", () => {
    assert.deepEqual(followResultAlerts(undefined, result({ startedUtc: ago(10 * MINUTE), endedUtc: ago(4 * MINUTE) }), "Sam P", "/r", now), [
      { title: "Sam P finished 2nd", body: "Heat 3", tag: "hc-result", url: "/r" },
    ]);
  });

  it("says nothing for a result first sent the next morning, or weeks later", () => {
    assert.deepEqual(followResultAlerts(undefined, result({ startedUtc: ago(12 * HOUR + 6 * MINUTE), endedUtc: ago(12 * HOUR) }), "Sam P", "/r", now), []);
    assert.deepEqual(followResultAlerts(undefined, result({ startedUtc: ago(14 * DAY + 6 * MINUTE), endedUtc: ago(14 * DAY) }), "Sam P", "/r", now), []);
  });

  it("goes by when the race ended, so a long race sent on time is still told", () => {
    assert.equal(followResultAlerts(undefined, result({ startedUtc: ago(3 * HOUR), endedUtc: ago(MINUTE) }), "Sam P", "/r", now).length, 1);
  });

  it("still tells of a result with no readable time, or one from a clock that is ahead", () => {
    assert.equal(followResultAlerts(undefined, result({}), "Sam P", "/r", now).length, 1);
    assert.equal(followResultAlerts(undefined, result({ startedUtc: "0001-01-01T00:00:00", endedUtc: "0001-01-01T00:00:00" }), "Sam P", "/r", now).length, 1);
    assert.equal(followResultAlerts(undefined, result({ endedUtc: "not a time" }), "Sam P", "/r", now).length, 1);
    assert.equal(followResultAlerts(undefined, result({ endedUtc: new Date(now + 20 * MINUTE).toISOString() }), "Sam P", "/r", now).length, 1);
  });

  it("draws the line an hour after the race ended, and wider from the start when no end is given", () => {
    assert.equal(followResultIsFresh({ endedUtc: ago(FOLLOW_RESULT_FRESH_MS - MINUTE) }, now), true);
    assert.equal(followResultIsFresh({ endedUtc: ago(FOLLOW_RESULT_FRESH_MS + MINUTE) }, now), false);
    assert.equal(followResultIsFresh({ startedUtc: ago(2 * HOUR) }, now), true, "no end given: a long race may still be news");
    assert.equal(followResultIsFresh({ startedUtc: ago(2 * DAY) }, now), false);
    assert.equal(followResultIsFresh("nothing", now), true);
  });

  it("sends nothing to a follower's phone for an old result, by the relay's own clock", async () => {
    const push = new PushService({ store: new MemoryStore(), subject: "https://relay.example", now: () => new Date(now) });
    const told: string[] = [];
    const inside = push as unknown as {
      followed: (club: string) => Promise<Map<string, string>>;
      deliver: (club: string, id: string, message: { title?: string }) => Promise<{ tried: number; sent: number }>;
    };
    inside.followed = async () => new Map([["f1", "Sam P"]]);
    inside.deliver = async (_club, _id, message) => {
      told.push(String(message.title));
      return { tried: 1, sent: 1 };
    };

    await push.afterResult("acme-rc", "s1", undefined, result({ startedUtc: ago(2 * DAY + 6 * MINUTE), endedUtc: ago(2 * DAY) }));
    assert.deepEqual(told, [], "two days old: not news");

    await push.afterResult("acme-rc", "s2", undefined, result({ startedUtc: ago(8 * MINUTE), endedUtc: ago(2 * MINUTE) }));
    assert.deepEqual(told, ["Sam P finished 2nd"]);
  });
});

// ------------------------------------------------- #360: a full name refuses, never evicts

describe("bug hunt 24 #360: a new phone on a full name is refused, and the phones there keep their alerts", () => {
  function sub(endpoint: string, kinds?: readonly string[]): PushSub {
    const ecdh = createECDH("prime256v1");
    ecdh.generateKeys();
    const base = { endpoint, p256dh: b64url(ecdh.getPublicKey()), auth: b64url(randomBytes(16)), since: new Date().toISOString() };
    return kinds === undefined ? base : { ...base, kinds };
  }

  async function full(): Promise<{ store: MemoryStore; push: PushService; endpoints: string[] }> {
    const store = new MemoryStore();
    const push = new PushService({ store, subject: "https://relay.example" });
    const endpoints: string[] = [];

    for (let i = 0; i < FOLLOW_SUBS_PER_NAME; i++) {
      const endpoint = `https://fcm.googleapis.com/fcm/send/real${i}`;
      endpoints.push(endpoint);
      assert.equal(await push.followSubscribe("brookside", "Sam Tr", sub(endpoint)), "ok");
    }

    return { store, push, endpoints };
  }

  async function kept(store: MemoryStore): Promise<{ endpoint: string; kinds?: string[] }[]> {
    const doc = await store.getItem("brookside", FOLLOW_CHANNEL, followIdOf("Sam Tr"));
    return (doc?.body as { subs: { endpoint: string; kinds?: string[] }[] }).subs;
  }

  it("refuses the eleventh and every made-up phone after it", async () => {
    const { store, push, endpoints } = await full();

    for (let i = 0; i < FOLLOW_SUBS_PER_NAME; i++) {
      assert.equal(await push.followSubscribe("brookside", "Sam Tr", sub(`https://fcm.googleapis.com/fcm/send/made-up${i}`)), "full");
    }

    assert.deepEqual((await kept(store)).map((s) => s.endpoint), endpoints, "the first ten are all still there, in order");
  });

  it("still lets a phone already on the list change what it hears", async () => {
    const { store, push, endpoints } = await full();
    const first = endpoints[0] as string;

    assert.equal(await push.followSubscribe("brookside", "Sam Tr", sub(first, ["result"])), "ok");

    const now = await kept(store);
    assert.equal(now.length, FOLLOW_SUBS_PER_NAME);
    assert.deepEqual(now.find((s) => s.endpoint === first)?.kinds, ["result"]);
  });

  it("takes a new phone again once one has signed off, and on another name meanwhile", async () => {
    const { push, endpoints } = await full();

    assert.equal(await push.followSubscribe("brookside", "Ollie", sub("https://fcm.googleapis.com/fcm/send/aunt")), "ok", "another name is not full");
    await push.followUnsubscribe("brookside", "Sam Tr", endpoints[3] as string);
    assert.equal(await push.followSubscribe("brookside", "Sam Tr", sub("https://fcm.googleapis.com/fcm/send/grandma")), "ok");
  });
});

// ------------------------------------------------- the worker, in a sandbox

/** An answer as the worker sees one: ok only for a 2xx, as in a browser. */
class FakeResponse {
  readonly type = "basic";
  readonly redirected = false;
  constructor(readonly body: string, readonly init: { status: number } = { status: 200 }) {}
  get status(): number { return this.init.status; }
  get ok(): boolean { return this.init.status >= 200 && this.init.status < 300; }
  clone(): FakeResponse { return new FakeResponse(this.body, this.init); }
}

/**
 * The club's worker (build b1) with a cache and a network the test controls,
 * after driver-phone-a.test.ts: the network can answer any status, and the
 * phone can have windows open.
 */
function worker(windows: readonly string[] = []) {
  const scope = "https://relay.test/c/acme-rc/";
  const listeners = new Map<string, (e: unknown) => void>();
  const stores = new Map<string, Map<string, FakeResponse>>();
  const net = { up: true, status: 200, asked: [] as string[] };
  const caches = {
    open: async (name: string) => {
      if (!stores.has(name)) stores.set(name, new Map());
      const m = stores.get(name) as Map<string, FakeResponse>;
      return {
        match: async (key: string) => m.get(key),
        put: async (key: string, res: FakeResponse) => { m.set(key, res); },
      };
    },
    keys: async () => [...stores.keys()],
    delete: async (name: string) => stores.delete(name),
  };
  const fetch = async (req: string | { url: string }) => {
    const url = typeof req === "string" ? req : req.url;
    net.asked.push(url);
    if (!net.up) throw new TypeError("Failed to fetch");
    return new FakeResponse((net.status === 200 ? "fresh " : "error " + net.status + " ") + url, { status: net.status });
  };
  const self = {
    addEventListener: (name: string, fn: (e: unknown) => void) => listeners.set(name, fn),
    skipWaiting: () => undefined,
    registration: { scope, showNotification: async () => undefined },
    clients: { claim: async () => undefined, matchAll: async () => windows.map((url) => ({ url })), openWindow: async () => null },
  };
  runInNewContext(serviceWorkerFor("b1"), { self, URL, Promise, caches, fetch, Response: FakeResponse, setTimeout, clearTimeout });

  /** What the worker answers for a page. */
  async function ask(url: string): Promise<{ status: number; body: string }> {
    let answered: Promise<FakeResponse> | null = null;
    (listeners.get("fetch") as (e: unknown) => void)({ request: { url, method: "GET" }, respondWith: (p: Promise<FakeResponse>) => { answered = p; } });
    assert.ok(answered !== null, "the worker answers this page");
    const res = await (answered as Promise<FakeResponse>);
    return { status: res.status, body: res.body };
  }

  /** The new worker taking over. */
  async function activate(): Promise<void> {
    let done: Promise<unknown> = Promise.resolve();
    (listeners.get("activate") as (e: unknown) => void)({ waitUntil: (p: Promise<unknown>) => { done = p; } });
    await done;
  }

  return { ask, activate, net, stores };
}

const LIVE = "https://relay.test/c/acme-rc/live";

// ------------------------------------------------- #362: a 5xx is answered from the kept copy

describe("bug hunt 24 #362: the worker shows its kept copy when the relay answers 502 or 503", () => {
  it("answers a kept page from its copy while the relay is restarting", async () => {
    const w = worker();
    assert.deepEqual(await w.ask(LIVE), { status: 200, body: "fresh " + LIVE });

    for (const status of [503, 502, 500]) {
      w.net.status = status;
      assert.deepEqual(await w.ask(LIVE + "?x=1"), { status: 200, body: "fresh " + LIVE }, `a ${status} is answered from the copy`);
    }

    assert.equal(w.stores.get("hc-pages-b1")?.get(LIVE)?.body, "fresh " + LIVE, "and the error never replaces the copy");
  });

  it("passes a 503 through for a page with no copy on this phone", async () => {
    const w = worker();
    w.net.status = 503;
    const got = await w.ask("https://relay.test/c/acme-rc/schedule");
    assert.equal(got.status, 503);
    assert.match(got.body, /^error 503 /, "the relay's own answer, as before");
    assert.equal(w.stores.get("hc-pages-b1")?.size ?? 0, 0, "nothing is kept of it");
  });

  it("still shows a 403 and a 404 over a kept copy", async () => {
    const w = worker();
    await w.ask(LIVE);

    for (const status of [403, 404]) {
      w.net.status = status;
      assert.equal((await w.ask(LIVE)).status, status, `a ${status} is the relay's real answer`);
    }
  });
});

// ------------------------------------------------- #361: the open page survives a deploy

describe("bug hunt 24 #361: the page open while a new build takes over keeps a no-signal copy", () => {
  it("fetches the open pages again into the new build's cache, and throws the old build's away", async () => {
    const w = worker([LIVE + "?from=tab", "https://relay.test/c/acme-rc/night", "https://elsewhere.test/c/acme-rc/live"]);
    // What the old build's worker kept seconds ago, when it answered the page.
    w.stores.set("hc-pages-old", new Map([[LIVE, new FakeResponse("kept by the old worker")]]));

    await w.activate();

    assert.equal(w.stores.has("hc-pages-old"), false, "the old build's pages are still thrown away");
    assert.deepEqual(w.net.asked, [LIVE], "only the club's own open page, without its query - never a driver's own night");

    w.net.up = false;
    assert.deepEqual(await w.ask(LIVE), { status: 200, body: "fresh " + LIVE }, "with no signal, the page opens from the new build's copy");
  });

  it("takes over all the same when the open page cannot be fetched", async () => {
    const down = worker([LIVE]);
    down.stores.set("hc-pages-old", new Map());
    down.net.up = false;
    await down.activate();
    assert.equal(down.stores.has("hc-pages-old"), false);
    assert.equal((await down.ask(LIVE)).status, 503, "no copy: the worker's own No signal page");

    const busy = worker([LIVE]);
    busy.net.status = 503;
    await busy.activate();
    assert.equal(busy.stores.get("hc-pages-b1")?.get(LIVE), undefined, "an error page is never kept as the copy");
  });

  it("does nothing more when no page is open", async () => {
    const w = worker();
    w.stores.set("hc-pages-old", new Map());
    await w.activate();
    assert.deepEqual(w.net.asked, []);
    assert.equal(w.stores.has("hc-pages-old"), false);
  });
});
