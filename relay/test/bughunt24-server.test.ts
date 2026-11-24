import assert from "node:assert/strict";
import { request as httpRequest, type Server } from "node:http";
import { describe, it } from "node:test";
import { Relay } from "../src/server.js";
import { MemoryStore, StoreReadError } from "../src/store.js";
import type { StoredDocument } from "../src/types.js";
import { API, openEverything, startDriver } from "./driverexp-r1-helpers.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, tenants, type Harness } from "./helpers.js";

/**
 * BUG HUNT 24 (23 Nov 2026): the relay server - a new short address, old
 * results under the privacy rules, uploads that hung up early, and
 * "Watch the last race" after a hide.
 */

const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** A memory store whose reads can be held, or made to fail, by the test. */
class TestStore extends MemoryStore {
  hold: Promise<void> | undefined;
  entered: (() => void) | undefined;
  failItem: { channel: string; id: string; corrupt: boolean } | undefined;
  failSnapshot: string | undefined;

  override async getSnapshot(club: string, channel: string): Promise<StoredDocument | undefined> {
    if (this.hold !== undefined) {
      this.entered?.();
      await this.hold;
    }

    if (this.failSnapshot === channel) {
      throw new StoreReadError(`${club}/${channel}.json`, true, new Error("half a file"));
    }

    return super.getSnapshot(club, channel);
  }

  override async getItem(club: string, channel: string, id: string): Promise<StoredDocument | undefined> {
    const fail = this.failItem;

    if (fail !== undefined && fail.channel === channel && fail.id === id) {
      throw new StoreReadError(`${club}/${channel}/${id}.json`, fail.corrupt, new Error("could not read"));
    }

    return super.getItem(club, channel, id);
  }

  /** Holds every snapshot read until released; resolves entered at the first one held. */
  arm(): { entered: Promise<void>; release: () => void } {
    let release!: () => void;
    this.hold = new Promise<void>((resolve) => {
      release = resolve;
    });
    const entered = new Promise<void>((resolve) => {
      this.entered = resolve;
    });

    return {
      entered,
      release: () => {
        this.hold = undefined;
        this.entered = undefined;
        release();
      },
    };
  }
}

describe("bug hunt 24: a club's new short address works as soon as its pages update", () => {
  it("redirects a new alias straight after the check said it was free", async () => {
    const h = await start();
    try {
      const ask = await call(h, "GET", "/api/v1/c/acme-rc/alias?name=acmeracing", { token: ACME_TOKEN });
      assert.equal(ask.body.state, "free");

      await call(h, "POST", "/api/v1/c/acme-rc/publish/site?seq=1", { token: ACME_TOKEN, json: { alias: "acmeracing" } });
      const home = await call(h, "GET", "/c/acmeracing", { noRedirect: true });
      assert.equal(home.status, 302, "not \"No club here\" for a minute");
      assert.equal(home.headers["location"], "/c/acme-rc");
    } finally {
      await h.close();
    }
  });

  it("stops redirecting an old alias the moment the club changes it", async () => {
    const h = await start();
    try {
      await call(h, "POST", "/api/v1/c/acme-rc/publish/site?seq=1", { token: ACME_TOKEN, json: { alias: "acmeracing" } });
      assert.equal((await call(h, "GET", "/c/acmeracing", { noRedirect: true })).status, 302);

      await call(h, "POST", "/api/v1/c/acme-rc/publish/site?seq=2", { token: ACME_TOKEN, json: { alias: "acmetwo" } });
      assert.notEqual((await call(h, "GET", "/c/acmeracing", { noRedirect: true })).status, 302, "the old address no longer sends drivers here");
      const now = await call(h, "GET", "/c/acmetwo", { noRedirect: true });
      assert.equal(now.status, 302);
      assert.equal(now.headers["location"], "/c/acme-rc");
    } finally {
      await h.close();
    }
  });
});

describe("bug hunt 24: unpublishing old results does not lose them when a year's index will not read", () => {
  const OLD = "11111111111111111111111111111111";
  const RECENT = "22222222222222222222222222222222";
  const CLUB = "brookside";
  const PUB = "/api/v1/c/brookside/publish";
  let seq = 1;
  const publish = (h: Harness, path: string, json: unknown) =>
    call(h, "POST", `${PUB}/${path}?seq=${seq++}`, { token: BROOK_TOKEN, json });

  async function withOldYear(store: TestStore): Promise<Harness> {
    const h = await start(store, tenants());
    await publish(h, `results/${OLD}`, { label: "Heat 1", startedUtc: "2023-06-01T19:00:00Z", results: [] });
    await publish(h, `results/${RECENT}`, { label: "Heat 1", startedUtc: "2026-06-01T19:00:00Z", results: [] });
    await publish(h, "resultsindex/2023", { year: "2023", sessions: [{ id: OLD }] });
    await publish(h, "resultsindex/2026", { year: "2026", sessions: [{ id: RECENT }] });
    return h;
  }

  it("takes the old results down, and keeps the index to try again, when its read fails", async () => {
    const store = new TestStore();
    const h = await withOldYear(store);
    try {
      store.failItem = { channel: "resultsindex", id: "2023", corrupt: false };
      await publish(h, "privacy", { juniors: [], keepYears: 2, fromYear: 2025 });
      await wait(60);
      store.failItem = undefined;

      assert.equal(await store.getItem(CLUB, "results", OLD), undefined, "the 2023 result is off the internet");
      assert.ok(await store.getItem(CLUB, "resultsindex", "2023"), "the index is left for the next pass, not thrown away unread");
      assert.ok(await store.getItem(CLUB, "results", RECENT));
      assert.ok(await store.getItem(CLUB, "resultsindex", "2026"));
    } finally {
      await h.close();
    }
  });

  it("takes the old results down, and then the broken index, when the index is not JSON at all", async () => {
    const store = new TestStore();
    const h = await withOldYear(store);
    try {
      store.failItem = { channel: "resultsindex", id: "2023", corrupt: true };
      await publish(h, "privacy", { juniors: [], keepYears: 2, fromYear: 2025 });
      await wait(60);
      store.failItem = undefined;

      assert.equal(await store.getItem(CLUB, "results", OLD), undefined);
      assert.equal(await store.getItem(CLUB, "resultsindex", "2023"), undefined);
      assert.ok(await store.getItem(CLUB, "results", RECENT));
    } finally {
      await h.close();
    }
  });

  it("takes down an old result that no year's index names", async () => {
    const store = new TestStore();
    const h = await start(store, tenants());
    try {
      await publish(h, `results/${OLD}`, { label: "Heat 1", startedUtc: "2023-06-01T19:00:00Z", results: [] });
      await publish(h, "privacy", { juniors: [], keepYears: 2, fromYear: 2025 });
      await wait(60);
      assert.equal(await store.getItem(CLUB, "results", OLD), undefined);
    } finally {
      await h.close();
    }
  });

  it("applies the new rules when the privacy document they replace would not read", async () => {
    const store = new TestStore();
    const h = await withOldYear(store);
    try {
      store.failSnapshot = "privacy";
      const answer = await publish(h, "privacy", { juniors: [], keepYears: 2, fromYear: 2025 });
      store.failSnapshot = undefined;
      assert.equal(answer.status, 200);
      await wait(60);

      assert.equal(await store.getItem(CLUB, "results", OLD), undefined);
      assert.equal(await store.getItem(CLUB, "resultsindex", "2023"), undefined);
      assert.ok(await store.getItem(CLUB, "results", RECENT));
    } finally {
      await h.close();
    }
  });
});

describe("bug hunt 24: an upload that hung up before its body was read gives its place back", () => {
  /** Sends only the headers of an upload, then hangs up while the relay is still reading its switches. */
  async function hangUpEarly(base: string, store: TestStore, path: string, headers: Record<string, string>): Promise<void> {
    const gate = store.arm();
    const url = new URL(`${base}${path}`);
    const req = httpRequest({
      host: url.hostname,
      port: url.port,
      path: `${url.pathname}${url.search}`,
      method: "POST",
      headers: { "content-length": "100", ...headers },
    });
    req.on("error", () => {});
    req.flushHeaders();

    await gate.entered;
    req.destroy();
    await wait(200);
    gate.release();
    await wait(200);
  }

  it("frees the car-photo place", async () => {
    const store = new TestStore();
    const h = await startDriver({ store });
    try {
      await openEverything(h);
      await hangUpEarly(h.base, store, `${API}/driver/photo`, { "content-type": "application/json" });

      const relay = h.relay as unknown as { photoUploadsInFlight: number; photoUploadsFrom: Map<string, number> };
      assert.equal(relay.photoUploadsInFlight, 0, "the place is given back, not held until a restart");
      assert.equal(relay.photoUploadsFrom.size, 0);
    } finally {
      await h.close();
    }
  });

  it("frees the clip place", async () => {
    const store = new TestStore();
    const h = await startDriver({ store });
    try {
      await openEverything(h);
      const clip = "c".repeat(32);
      const driver = "d".repeat(20);
      await hangUpEarly(h.base, store, `${API}/clip/${clip}?driver=${driver}`, { authorization: `Bearer ${ACME_TOKEN}`, "content-type": "audio/mpeg" });

      assert.equal((h.relay as unknown as { clipUploadsInFlight: number }).clipUploadsInFlight, 0);
    } finally {
      await h.close();
    }
  });
});

describe("bug hunt 24: \"Watch the last race\" goes the moment the club hides that race", () => {
  const SID = "11111111111111111111111111111111";
  const stream = (extra: Record<string, unknown> = {}) => ({
    on: true, publicResults: true, leadIn: 10, pieces: [{ videoId: "AAAAAAAAAAA", startedUtc: "2026-11-14T23:00:00.000Z", endedUtc: null, nudge: 0 }], hidden: [], juniors: [], ...extra,
  });

  it("answers no last race after a hide, without waiting a minute", async () => {
    const store = new MemoryStore();
    const relay = new Relay({ store, tenants: tenants(), now: () => new Date("2026-11-14T23:00:00.000Z"), log: () => {} });
    const server: Server = relay.listen(0, "127.0.0.1");
    await new Promise<void>((resolve) => server.once("listening", resolve));
    const h = { base: `http://127.0.0.1:${(server.address() as { port: number }).port}`, store, relay, close: async () => {} };
    let seq = 1;
    const publish = (path: string, json: unknown) => call(h, "POST", `${API}/publish/${path}?seq=${seq++}`, { token: ACME_TOKEN, json });

    try {
      await publish(`results/${SID}`, { id: SID, label: "Q1 Heat 2", startedUtc: "2026-11-14T23:10:00.000Z", endedUtc: "2026-11-14T23:14:00.000Z", results: [] });
      await publish("stream", stream());
      const before = await call(h, "GET", `${API}/video/live`);
      assert.equal(before.body.last.video.id, "AAAAAAAAAAA", "the last race is offered, and now kept");

      await publish("stream", stream({ hidden: [SID] }));
      assert.equal((await call(h, "GET", `${API}/video/live`)).body.last, null, "hidden straight away");

      await publish("stream", stream({ hidden: [] }));
      assert.equal((await call(h, "GET", `${API}/video/live`)).body.last.video.id, "AAAAAAAAAAA", "and back straight away when shown again");
    } finally {
      await new Promise<void>((resolve) => server.close(() => resolve()));
    }
  });
});
