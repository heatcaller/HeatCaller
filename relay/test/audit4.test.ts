import assert from "node:assert/strict";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createContext, runInContext } from "node:vm";
import { afterEach, describe, it } from "node:test";
import { DEFAULT_CONTEXT, managePage } from "../src/pages.js";
import { pinHash } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import type { StoredDocument } from "../src/types.js";
import { VoiceService } from "../src/voice.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

// Regressions for the fourth audit: public snapshots read afresh for every
// request, a check-in tap written after the club was deleted or check-in
// closed, methods answered as missing addresses, a voice lock that was never
// let go, and three things in the pages' own scripts.

const NOW = new Date("2026-09-20T12:00:00.000Z");
const SAM = "0123456789abcdef0123";

let seq = 1;
const open: Harness[] = [];
const dirs: string[] = [];

afterEach(async () => {
  while (open.length > 0) {
    await open.pop()!.close();
  }

  while (dirs.length > 0) {
    await rm(dirs.pop()!, { recursive: true, force: true });
  }
});

async function harness(store: MemoryStore = new MemoryStore()): Promise<Harness> {
  const h = await start(store, undefined, () => NOW);
  open.push(h);
  return h;
}

async function publish(h: Harness, channel: string, doc: unknown): Promise<void> {
  const answer = await call(h, "POST", `/api/v1/c/acme-rc/publish/${channel}?seq=${seq++}`, { token: ACME_TOKEN, json: doc });
  assert.equal(answer.status, 200, JSON.stringify(answer.body));
}

const checkinDoc = (overrides: Record<string, unknown> = {}) => ({
  enabled: true,
  pinSha256: pinHash("acme-rc", "4321"),
  event: "Club night",
  drivers: [{ id: SAM, name: "Sam", className: "Stock", number: "7", checkedIn: false, photo: "" }],
  ...overrides,
});

const tick = (ms = 30) => new Promise((resolve) => setTimeout(resolve, ms));

/** A memory store that counts snapshot reads, slowly, and can hold one write or the club delete until let go. */
class GatedStore extends MemoryStore {
  readonly reads = new Map<string, number>();
  readDelayMs = 0;
  held: string | undefined;
  holdDelete = false;
  private release: (() => void) | undefined;
  private reached: (() => void) | undefined;
  readonly arrived = new Promise<void>((resolve) => (this.reached = resolve));

  letGo(): void {
    this.release?.();
  }

  private gate(): Promise<void> {
    this.reached?.();
    return new Promise<void>((resolve) => (this.release = resolve));
  }

  override async getSnapshot(club: string, channel: string): Promise<StoredDocument | undefined> {
    this.reads.set(channel, (this.reads.get(channel) ?? 0) + 1);

    if (this.readDelayMs > 0) {
      await tick(this.readDelayMs);
    }

    return super.getSnapshot(club, channel);
  }

  override async putSnapshot(club: string, channel: string, doc: StoredDocument): Promise<void> {
    if (channel === this.held) {
      this.held = undefined;
      await this.gate();
    }

    return super.putSnapshot(club, channel, doc);
  }

  override async deleteClub(club: string): Promise<void> {
    if (this.holdDelete) {
      this.holdDelete = false;
      await this.gate();
    }

    return super.deleteClub(club);
  }
}

describe("public snapshots", () => {
  it("are read once for a crowd asking together, held until the next publish, and never stale after one", async () => {
    const store = new GatedStore();
    const h = await harness(store);
    await publish(h, "badges", { art: "x".repeat(50_000) });

    store.readDelayMs = 20;
    store.reads.clear();
    const crowd = await Promise.all(Array.from({ length: 30 }, () => call(h, "GET", "/api/v1/c/acme-rc/snapshot/badges")));

    for (const answer of crowd) {
      assert.equal(answer.status, 200);
      assert.equal(answer.body.body.art.length, 50_000);
      assert.equal(answer.headers["access-control-allow-origin"], "*");
      assert.equal(answer.headers["cache-control"], "no-store");
    }

    assert.equal(store.reads.get("badges"), 1, "thirty requests, one read");

    // Held: asking again reads nothing.
    assert.equal((await call(h, "GET", "/api/v1/c/acme-rc/snapshot/badges")).status, 200);
    assert.equal(store.reads.get("badges"), 1);

    // A publish is seen at once.
    await publish(h, "badges", { art: "new" });
    const after = await call(h, "GET", "/api/v1/c/acme-rc/snapshot/badges");
    assert.equal(after.body.body.art, "new");

    // A club that deletes everything is not served its old document.
    assert.equal((await call(h, "DELETE", "/api/v1/c/acme-rc", { token: ACME_TOKEN })).status, 200);
    assert.equal((await call(h, "GET", "/api/v1/c/acme-rc/snapshot/badges")).status, 404);
  });

  it("does not keep a read that a publish overtook", async () => {
    const store = new GatedStore();
    const h = await harness(store);
    await publish(h, "state", { lap: 1 });

    store.readDelayMs = 60;
    const slow = call(h, "GET", "/api/v1/c/acme-rc/snapshot/state");
    await tick(10);
    store.readDelayMs = 0;
    await publish(h, "state", { lap: 2 });

    assert.equal((await slow).status, 200);
    assert.equal((await call(h, "GET", "/api/v1/c/acme-rc/snapshot/state")).body.body.lap, 2);
  });

  it("still answers a private or a withheld channel exactly as before", async () => {
    const h = await harness();
    await publish(h, "checkin", checkinDoc());
    await publish(h, "roster", { names: ["Sam"] });

    assert.equal((await call(h, "GET", "/api/v1/c/acme-rc/snapshot/checkin")).status, 404);
    assert.equal((await call(h, "GET", "/api/v1/c/acme-rc/snapshot/roster")).status, 403);
    assert.equal((await call(h, "GET", "/api/v1/c/acme-rc/snapshot/state")).status, 404);
  });
});

describe("a check-in tap that waited for the club's lock", () => {
  it("is refused, and writes nothing, when the club was deleted meanwhile", async () => {
    const store = new GatedStore();
    const h = await harness(store);
    await publish(h, "checkin", checkinDoc());
    // Held in memory by the relay, so the tap passes its first check.
    assert.equal((await call(h, "POST", "/api/v1/c/acme-rc/checkin/one", { json: { id: SAM } })).status, 200);

    store.holdDelete = true;
    const deleting = call(h, "DELETE", "/api/v1/c/acme-rc", { token: ACME_TOKEN });
    await store.arrived;

    const tap = call(h, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: SAM, wanted: true } });
    await tick();
    store.letGo();

    assert.equal((await deleting).status, 200);
    assert.equal((await tap).status, 409);
    assert.equal(await store.getSnapshot("acme-rc", "inbox"), undefined);
  });

  it("is refused when the desk closed check-in, or took the driver off, while it waited", async () => {
    for (const [closing, status] of [
      [checkinDoc({ enabled: false }), 409],
      [checkinDoc({ drivers: [] }), 404],
    ] as const) {
      const store = new GatedStore();
      const h = await harness(store);
      await publish(h, "checkin", checkinDoc());
      assert.equal((await call(h, "POST", "/api/v1/c/acme-rc/checkin/one", { json: { id: SAM } })).status, 200);

      store.held = "checkin";
      const publishing = call(h, "POST", `/api/v1/c/acme-rc/publish/checkin?seq=${seq++}`, { token: ACME_TOKEN, json: closing });
      await store.arrived;

      const tap = call(h, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: SAM, wanted: true } });
      await tick();
      store.letGo();

      assert.equal((await publishing).status, 200);
      assert.equal((await tap).status, status);

      const inbox = await call(h, "GET", "/api/v1/c/acme-rc/inbox", { token: ACME_TOKEN });
      assert.deepEqual(inbox.body.items, []);
    }
  });

  it("is still queued as ever when nothing changed", async () => {
    const h = await harness();
    await publish(h, "checkin", checkinDoc());

    const tap = await call(h, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: SAM, wanted: true } });
    assert.equal(tap.status, 200);

    const inbox = await call(h, "GET", "/api/v1/c/acme-rc/inbox", { token: ACME_TOKEN });
    assert.equal(inbox.body.items.length, 1);
    assert.equal(inbox.body.items[0].id, SAM);
  });
});

describe("the pass key", () => {
  it("is not made by an anonymous page view of a club with no roster, nor after the club deleted everything", async () => {
    const h = await harness();

    for (const path of ["/c/acme-rc", "/c/acme-rc/live", "/c/acme-rc/checkin"]) {
      assert.equal((await call(h, "GET", path)).status, 200);
    }

    assert.equal((await call(h, "POST", "/api/v1/c/acme-rc/manage/signin", { json: { pin: "123456" } })).status, 409);
    assert.equal(await h.store.getSnapshot("acme-rc", "passkey"), undefined);

    // With a roster there is a key, as there must be for a pass...
    await publish(h, "checkin", checkinDoc());
    assert.equal((await call(h, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "4321" } })).status, 200);
    assert.notEqual(await h.store.getSnapshot("acme-rc", "passkey"), undefined);

    // ...and once the club has gone, opening its home page does not bring its folder back.
    assert.equal((await call(h, "DELETE", "/api/v1/c/acme-rc", { token: ACME_TOKEN })).status, 200);
    assert.equal((await call(h, "GET", "/c/acme-rc")).status, 200);
    assert.equal(await h.store.getSnapshot("acme-rc", "passkey"), undefined);
    assert.equal(await h.store.usage("acme-rc"), 0);
  });
});

describe("a method the relay does not take", () => {
  it("is a 405 naming the ones it does, not a missing address", async () => {
    const h = await harness();

    for (const path of ["/api/v1/c/acme-rc/whoami", "/api/v1/c/acme-rc/publish/state", "/c/acme-rc/live", "/nowhere"]) {
      for (const method of ["PUT", "PATCH"]) {
        const answer = await call(h, method, path, { token: ACME_TOKEN, json: {} });
        assert.equal(answer.status, 405, `${method} ${path}`);
        assert.match(answer.headers["allow"] ?? "", /GET/);
      }
    }

    // A method the relay does take, at an address it does not, is still a 404.
    assert.equal((await call(h, "GET", "/api/v1/c/acme-rc/nothing-here")).status, 404);
    // And race control is refused however it is asked, as ever.
    assert.equal((await call(h, "PUT", "/api/v1/c/acme-rc/command/yellow", { token: ACME_TOKEN })).status, 403);
  });
});

describe("the voice's usage lock", () => {
  it("is let go once nothing is waiting on it", async () => {
    const dir = await mkdtemp(join(tmpdir(), "relay-audit4-"));
    dirs.push(dir);

    const voice = new VoiceService({
      config: {
        voices: [
          {
            id: "clubhouse",
            name: "Clubhouse",
            what: "",
            voiceId: "el",
            modelId: "eleven_flash_v2_5",
            stability: 0.5,
            similarity: 0.75,
            style: 0,
            speakerBoost: true,
            speed: 1,
            outputFormat: "",
          },
        ],
        monthlyChars: 1000,
        maxChars: 120,
      },
      dataDir: dir,
      render: async (_voice, text) => Buffer.from(text),
      now: () => NOW,
    });

    const plan = { enabled: true, monthlyChars: 1000, voices: [] };
    const results = await Promise.all([
      voice.speak("acme-rc", plan, "clubhouse", "Green flag", false),
      voice.speak("acme-rc", plan, "clubhouse", "Cars on the grid", false),
    ]);

    assert.ok(results.every((result) => result.ok));
    assert.equal((voice as unknown as { usageLocks: Map<string, unknown> }).usageLocks.size, 0);
  });
});

// ------------------------------------------------------------ the pages' scripts

/**
 * Just enough of a browser to run one page's script: elements by id, found
 * again in whatever markup is written into innerHTML, with their event
 * listeners and a clock the test moves by hand.
 */
function browser(html: string, fetchImpl: (url: string, init?: { body?: string }) => Promise<unknown>) {
  const byId = new Map<string, FakeElement>();
  const timers: { at: number; run: () => void }[] = [];
  const intervals: (() => void)[] = [];
  const store = new Map<string, string>();
  const errors: unknown[] = [];
  let clock = 0;

  class FakeElement {
    value = "";
    hidden = false;
    checked = false;
    disabled = false;
    className = "";
    textContent = "";
    tagName = "DIV";
    style: Record<string, string> = {};
    dataset: Record<string, string> = {};
    options: unknown[] = [];
    classList = { add() {}, remove() {}, contains: () => false };
    parent: FakeElement | undefined;
    readonly listeners = new Map<string, ((event: unknown) => void)[]>();
    private html = "";

    constructor(readonly id: string) {}

    addEventListener(type: string, listener: (event: unknown) => void): void {
      this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
    }

    fire(type: string): void {
      for (const listener of this.listeners.get(type) ?? []) {
        listener({ preventDefault() {}, target: this });
      }
    }

    focus(): void {}
    select(): void {}
    scrollIntoView(): void {}
    readonly children: FakeElement[] = [];

    appendChild(child: FakeElement): void {
      this.children.push(child);
    }

    /** What is drawn in it: its markup and every child's, for a test to read. */
    drawn(): string {
      return this.html + this.children.map((child) => child.drawn()).join("");
    }

    setAttribute(): void {}
    getAttribute(): null {
      return null;
    }

    get innerHTML(): string {
      return this.html;
    }

    set innerHTML(value: string) {
      this.html = value;
      this.children.length = 0;

      for (const [id, element] of [...byId]) {
        if (element.parent === this) {
          byId.delete(id);
        }
      }

      for (const match of String(value).matchAll(/id="([^"]+)"/g)) {
        const child = new FakeElement(match[1] as string);
        child.parent = this;
        byId.set(child.id, child);
      }
    }
  }

  for (const match of html.matchAll(/id="([^"]+)"/g)) {
    byId.set(match[1] as string, new FakeElement(match[1] as string));
  }

  const storage = {
    getItem: (key: string) => store.get(key) ?? null,
    setItem: (key: string, value: string) => store.set(key, String(value)),
    removeItem: (key: string) => store.delete(key),
  };

  const context = createContext({
    document: {
      body: { dataset: { club: "acme-rc" }, appendChild() {}, classList: { add() {}, remove() {} } },
      head: { appendChild() {} },
      hidden: false,
      activeElement: null,
      getElementById: (id: string) => byId.get(id) ?? null,
      querySelector: () => null,
      querySelectorAll: () => [],
      createElement: () => new FakeElement(""),
      addEventListener() {},
    },
    window: {},
    URLSearchParams,
    location: { search: "", hash: "", pathname: "/c/acme-rc/", origin: "http://relay.test" },
    Element: FakeElement,
    fetch: fetchImpl,
    setInterval: (run: () => void) => intervals.push(run),
    setTimeout: (run: () => void, ms = 0) => timers.push({ at: clock + ms, run }),
    clearTimeout: (handle: number) => {
      if (handle > 0 && timers[handle - 1] !== undefined) {
        timers[handle - 1] = { at: Infinity, run: () => {} };
      }
    },
    localStorage: storage,
    sessionStorage: storage,
    navigator: {},
    confirm: () => true,
    prompt: () => null,
    crypto: undefined,
    console,
  });

  return {
    byId,
    storage,
    errors,
    run(script: string): void {
      runInContext(script, context);
    },
    /** Every interval once, and everything that settles from it. */
    async poll(): Promise<void> {
      for (const run of intervals) {
        try {
          run();
        } catch (error) {
          errors.push(error);
        }
      }

      await tick(10);
    },
    async advance(ms: number): Promise<void> {
      clock += ms;

      for (const timer of timers) {
        if (timer.at <= clock) {
          const run = timer.run;
          timer.at = Infinity;
          run();
        }
      }

      await tick(10);
    },
  };
}

function pageParts(text: string): { html: string; script: string } {
  const match = /<script[^>]*>([\s\S]*)<\/script>/.exec(text);
  assert.ok(match);
  return { html: text.replace(match[0], ""), script: match[1] as string };
}

describe("the check-in page", () => {
  it("draws the list again after a failed poll, when nobody was on it yet", async () => {
    const h = await harness();
    const { html, script } = pageParts((await call(h, "GET", "/c/acme-rc/checkin")).text);

    let offline = false;
    let drivers: unknown[] = [];

    const page = browser(html, async () => {
      if (offline) {
        throw new Error("offline");
      }

      const body = { ok: true, mode: "staff", event: "Club night", drivers, pass: "a".repeat(64) };
      return { status: 200, ok: true, json: async () => body };
    });

    const unhandled = (error: unknown) => page.errors.push(error);
    process.on("unhandledRejection", unhandled);

    try {
      page.storage.setItem("hg-pin-acme-rc", "4321");
      page.run(script);
      await page.poll();
      assert.match(page.byId.get("rows")?.innerHTML ?? "", /Nobody is on the check-in list yet/);

      offline = true;
      await page.poll();
      assert.match(page.byId.get("ci")?.innerHTML ?? "", /Could not reach the relay/);

      offline = false;
      drivers = [{ id: SAM, name: "Sam", className: "Stock", number: "7", checkedIn: false, photo: "" }];
      await page.poll();

      assert.deepEqual(page.errors, []);
      assert.match(page.byId.get("rows")?.innerHTML ?? "", /Sam/);
    } finally {
      process.off("unhandledRejection", unhandled);
    }
  });
});

describe("the manager page", () => {
  it("searches once the typing stops, and draws only the newest search", async () => {
    const h = await harness();
    const { html, script } = pageParts((await call(h, "GET", "/c/acme-rc/manage")).text);

    const asked: string[] = [];
    const queryOf = new Map<string, string>();
    let releaseOld: (() => void) | undefined;

    const page = browser(html, async (url, init) => {
      const body = JSON.parse(init?.body ?? "{}") as { reqId?: string; op?: string; args?: { query?: string } };
      const reply = (value: unknown) => ({ status: 200, ok: true, json: async () => value });

      if (url.endsWith("/manage/request")) {
        if (body.op === "drivers.search") {
          asked.push(body.args?.query ?? "");
          queryOf.set(body.reqId ?? "", body.args?.query ?? "");
        }

        return reply({ ok: true });
      }

      const query = queryOf.get(body.reqId ?? "");
      const answer = reply({ ok: true, ready: true, response: { ok: true, data: { drivers: query === undefined ? [] : [{ id: "d", name: query }] } } });

      // The first search's answer is slow, and arrives after the second's.
      if (query === "Old") {
        return new Promise((resolve) => (releaseOld = () => resolve(answer)));
      }

      return answer;
    });

    page.storage.setItem("hg-manage-acme-rc", "b".repeat(64));
    page.run(script);
    await page.advance(0);

    const search = page.byId.get("entrySearch")!;
    const list = page.byId.get("entryList")!;

    // Typed quickly: nothing is asked until the pause, and then once.
    for (const typed of ["O", "Ol", "Old"]) {
      search.value = typed;
      search.fire("input");
      await page.advance(100);
    }

    assert.deepEqual(asked, []);
    await page.advance(400);
    assert.deepEqual(asked, ["Old"]);

    search.value = "New";
    search.fire("input");
    await page.advance(400);
    assert.deepEqual(asked, ["Old", "New"]);
    assert.match(list.drawn(), /New/);

    // The older search answering last must not replace the newer one.
    releaseOld?.();
    await page.advance(0);
    assert.match(list.drawn(), /New/);
    assert.doesNotMatch(list.drawn(), /Old/);
  });

  it("gives a picked row its tint on browsers without color-mix", () => {
    const html = managePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, nonce: "n" });
    const root = /:root \{[\s\S]*?\n\}/.exec(html)?.[0] ?? "";

    assert.doesNotMatch(root, /--on-tint: color-mix/);
    assert.match(root, /--on-tint: var\(--row-alt\);/);
    assert.match(html, /@supports \(color: color-mix\(in srgb, red 50%, blue\)\) \{\s*:root \{ --on-tint: color-mix/);
  });
});
