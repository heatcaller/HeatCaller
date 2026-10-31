import assert from "node:assert/strict";
import { request as httpRequest } from "node:http";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createContext, runInContext } from "node:vm";
import { afterEach, describe, it } from "node:test";
import { BrainService, type BrainConfig, type BrainPlan } from "../src/brain.js";
import { MemoryStore } from "../src/store.js";
import { VoiceService, type CatalogueVoice } from "../src/voice.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, tenants, type Harness } from "./helpers.js";

/**
 * BUG HUNT 7, THE RELAY (13 Oct 2026).
 *
 * The listen-along player, which was silent on iPhones, went silent for
 * the night after a laptop restart, and replayed the backlog after a
 * Stop; a listen line that could land after the club switched listening
 * off; the night-before preview hidden on exactly the night it is for; a
 * cloned announcer any club could use; the brain's cap of two at once
 * that held nothing back; and a room vote carried into the next race.
 */

let harness: Harness | undefined;
const scratch: string[] = [];

afterEach(async () => {
  await harness?.close();
  harness = undefined;

  for (const dir of scratch.splice(0)) {
    await rm(dir, { recursive: true, force: true });
  }
});

/** Lets every promise and fetch the page started settle. */
async function settle(): Promise<void> {
  for (let i = 0; i < 20; i++) {
    await new Promise((done) => setImmediate(done));
  }
}

function scriptOf(page: string): string {
  const match = /<script[^>]*>([\s\S]*)<\/script>/.exec(page);
  assert.ok(match, "the page carries a script");
  return match[1] as string;
}

/**
 * Sends a POST whose body arrives in two halves, with `between` run in the
 * gap - the slow uplink the races below need.
 */
function slowPost(
  live: Harness,
  path: string,
  token: string | undefined,
  json: unknown,
  between: () => Promise<void>,
): Promise<number> {
  const text = JSON.stringify(json);
  const half = Math.floor(text.length / 2);
  const url = new URL(path, live.base);

  return new Promise((resolve, reject) => {
    const headers: Record<string, string | number> = {
      "content-type": "application/json",
      "content-length": Buffer.byteLength(text),
    };

    if (token !== undefined) {
      headers["authorization"] = `Bearer ${token}`;
    }

    const sent = httpRequest(url, { method: "POST", headers }, (response) => {
      response.resume();
      response.on("end", () => resolve(response.statusCode ?? 0));
    });

    sent.on("error", reject);
    sent.write(text.slice(0, half));

    // Long enough for the relay to have passed its checks before the body
    // and be waiting on the rest of it.
    setTimeout(() => {
      between().then(
        () => sent.end(text.slice(half)),
        (error: unknown) => reject(error),
      );
    }, 50);
  });
}

// ------------------------------------------------------------ the player

/** One media element as the page sees it, and every play() it was asked for. */
class FakeAudio {
  static made: FakeAudio[] = [];
  static plays: { element: FakeAudio; src: string; inPress: boolean }[] = [];
  static inPress = false;

  src = "";
  paused = true;
  onended: (() => void) | null = null;
  onerror: (() => void) | null = null;

  constructor() {
    FakeAudio.made.push(this);
  }

  play(): Promise<void> {
    FakeAudio.plays.push({ element: this, src: this.src, inPress: FakeAudio.inPress });
    this.paused = false;
    return Promise.resolve();
  }

  pause(): void {
    this.paused = true;
  }

  addEventListener(): void {}
}

interface Held {
  id: string;
  publishedAt: string;
  audio: string;
}

function listenPage(script: string, held: Held[]) {
  const from = script.indexOf("(function () {\n  const btn = document.getElementById('listenbtn');");

  assert.notEqual(from, -1, "the live page carries the player");

  const listeners: (() => void)[] = [];
  const intervals: (() => void)[] = [];
  const note = { textContent: "" };
  const btn = {
    textContent: "",
    addEventListener: (_type: string, run: () => void) => listeners.push(run),
  };

  FakeAudio.made = [];
  FakeAudio.plays = [];

  const reply = (body: unknown) => ({ ok: true, json: async () => body });

  const context = createContext({
    CLUB: "acme-rc",
    Audio: FakeAudio,
    document: { getElementById: (id: string) => (id === "listenbtn" ? btn : id === "listennote" ? note : null) },
    window: {
      setInterval: (run: () => void) => intervals.push(run),
      clearInterval: () => {
        intervals.length = 0;
      },
    },
    fetch: async (url: string) => {
      if (url.endsWith("/collection/listen?meta=1")) {
        // One document per id, as the relay holds them: a line published
        // again under an id replaces the one before it.
        const latest = new Map(held.map((one) => [one.id, one] as const));

        return reply({
          items: [...latest.values()].map((one) => ({ id: one.id, publishedAt: one.publishedAt, bytes: 1 })),
          meta: true,
        });
      }

      const id = decodeURIComponent(url.slice(url.lastIndexOf("/") + 1));
      const one = [...held].reverse().find((line) => line.id === id);

      return one === undefined ? { ok: false, json: async () => ({}) } : reply({ id, body: { audio: one.audio, said: id } });
    },
  });

  runInContext(script.slice(from), context);

  return {
    note,
    /** The tap, which is the only moment a phone lets audio start. */
    async press(): Promise<void> {
      FakeAudio.inPress = true;

      try {
        listeners.forEach((run) => run());
      } finally {
        FakeAudio.inPress = false;
      }

      await settle();
    },
    async poll(): Promise<void> {
      [...intervals].forEach((run) => run());
      await settle();
    },
    /** The line now playing reaching its end. */
    async ended(): Promise<void> {
      FakeAudio.made[0]?.onended?.();
      await settle();
    },
    /** What was played after the press, as the audio each line carried. */
    played(): string[] {
      return FakeAudio.plays.filter((one) => !one.inPress).map((one) => one.src.slice(one.src.indexOf(",") + 1));
    },
  };
}

describe("bug hunt 7: the listen-along player", () => {
  async function liveScript(): Promise<string> {
    harness = await start(new MemoryStore());

    const opened = await call(harness, "POST", "/api/v1/c/acme-rc/listen/open", { token: ACME_TOKEN, json: { open: true } });
    assert.equal(opened.status, 200);

    return scriptOf((await call(harness, "GET", "/c/acme-rc/live")).text);
  }

  const at = (minutes: number) => new Date(Date.UTC(2026, 9, 13, 19, 0, 0) + minutes * 60_000).toISOString();

  it("starts its one element inside the press, and plays every line through that element", async () => {
    const held: Held[] = [{ id: "L000001", publishedAt: at(0), audio: "one" }];
    const page = listenPage(await liveScript(), held);

    await page.press();

    // iPhone Safari only lets an element make a sound once a tap has
    // started it, and lines arrive from a timer, never from a tap.
    assert.equal(FakeAudio.made.length, 1);
    assert.equal(FakeAudio.plays.length, 1);
    assert.equal(FakeAudio.plays[0]?.inPress, true);

    held.push({ id: "L000002", publishedAt: at(1), audio: "two" });
    await page.poll();
    await page.ended();

    held.push({ id: "L000003", publishedAt: at(2), audio: "three" });
    await page.poll();

    assert.deepEqual(page.played(), ["two", "three"]);
    assert.equal(FakeAudio.made.length, 1, "no new element per line");
    assert.ok(FakeAudio.plays.every((one) => one.element === FakeAudio.made[0]));
  });

  it("plays the laptop's count starting again after a restart, but not a line sent twice", async () => {
    const held: Held[] = [{ id: "L000001", publishedAt: at(0), audio: "first-run-1" }];
    const page = listenPage(await liveScript(), held);

    await page.press();

    held.push({ id: "L000002", publishedAt: at(1), audio: "first-run-2" });
    await page.poll();
    await page.ended();

    // The laptop's answer was lost and it sent the same line again a few
    // seconds later. Heard once already.
    held.push({ id: "L000002", publishedAt: at(1.2), audio: "first-run-2" });
    await page.poll();

    assert.deepEqual(page.played(), ["first-run-2"]);

    // An hour on, the laptop restarts and counts from L000001 again.
    held.push({ id: "L000001", publishedAt: at(60), audio: "second-run-1" });
    await page.poll();
    await page.ended();

    held.push({ id: "L000002", publishedAt: at(61), audio: "second-run-2" });
    await page.poll();

    assert.deepEqual(page.played(), ["first-run-2", "second-run-1", "second-run-2"]);
  });

  it("Stop stops the line playing, and Listen again joins where the evening is", async () => {
    const held: Held[] = [{ id: "L000001", publishedAt: at(0), audio: "one" }];
    const page = listenPage(await liveScript(), held);

    await page.press();

    held.push({ id: "L000002", publishedAt: at(1), audio: "two" });
    await page.poll();

    assert.equal(FakeAudio.made[0]?.paused, false);

    await page.press();

    assert.equal(FakeAudio.made[0]?.paused, true, "the line playing at Stop is stopped");

    // Said while nobody was listening.
    held.push({ id: "L000003", publishedAt: at(2), audio: "three" });
    held.push({ id: "L000004", publishedAt: at(3), audio: "four" });

    await page.press();
    await page.poll();

    assert.deepEqual(page.played(), ["two"], "nothing said while stopped is played back");

    held.push({ id: "L000005", publishedAt: at(4), audio: "five" });
    await page.poll();

    assert.deepEqual(page.played(), ["two", "five"]);
  });
});

// ------------------------------------------------------------ the listen gate

describe("bug hunt 7: a listen line arriving as the club says no", () => {
  it("is refused rather than written after the switch took everything down", async () => {
    const live = (harness = await start(new MemoryStore()));

    assert.equal(
      (await call(live, "POST", "/api/v1/c/acme-rc/listen/open", { token: ACME_TOKEN, json: { open: true } })).status,
      200,
    );

    const status = await slowPost(
      live,
      "/api/v1/c/acme-rc/publish/listen/L000001?seq=1",
      ACME_TOKEN,
      { said: "Green flag", audio: "A".repeat(4096) },
      async () => {
        const closed = await call(live, "POST", "/api/v1/c/acme-rc/listen/open", { token: ACME_TOKEN, json: { open: false } });
        assert.equal(closed.status, 200);
      },
    );

    assert.equal(status, 403);

    const held = await live.store.itemSizes("acme-rc", "listen");
    assert.deepEqual(held, []);
  });
});

// ------------------------------------------------------------ the night before

describe("bug hunt 7: the night-before preview on the home page", () => {
  class Element {
    innerHTML = "";
    textContent = "";
    hidden = false;
    style: Record<string, string> = {};
    dataset: Record<string, string> = { club: "acme-rc" };
    classList = { add() {}, remove() {}, toggle() {}, contains: () => false };
    previousElementSibling = null;
    addEventListener(): void {}
    setAttribute(): void {}
    getAttribute(): null {
      return null;
    }
    appendChild(): void {}
    scrollIntoView(): void {}
    querySelector(): null {
      return null;
    }
    querySelectorAll(): never[] {
      return [];
    }
  }

  async function homeWith(lines: { status: string; className: string; time: string }[]): Promise<Element> {
    harness = await start(new MemoryStore());

    const script = scriptOf((await call(harness, "GET", "/c/acme-rc")).text);
    const byId = new Map<string, Element>();
    const storage = { getItem: () => null, setItem() {}, removeItem() {} };
    const until = new Date(Date.now() + 3600_000).toISOString();

    const answers: Record<string, unknown> = {
      "/snapshot/programme": { body: { lines }, publishedAt: new Date().toISOString(), seq: 1 },
      "/snapshot/preview": {
        body: { title: "Friday Club Night", when: "Friday", text: "Nineteen cars are entered.", until },
        publishedAt: new Date().toISOString(),
        seq: 1,
      },
    };

    const context = createContext({
      document: {
        body: new Element(),
        hidden: false,
        getElementById: (id: string) => {
          if (!byId.has(id)) {
            byId.set(id, new Element());
          }

          return byId.get(id);
        },
        querySelector: () => null,
        querySelectorAll: () => [],
        createElement: () => new Element(),
        addEventListener() {},
      },
      window: {},
      location: { search: "", hash: "", pathname: "/c/acme-rc", origin: "http://relay.test" },
      localStorage: storage,
      sessionStorage: storage,
      navigator: {},
      URLSearchParams,
      setInterval: () => 0,
      setTimeout: () => 0,
      clearTimeout() {},
      fetch: async (url: string) => {
        const path = url.slice("/api/v1/c/acme-rc".length);
        const body = answers[path];

        return body === undefined
          ? { ok: false, status: 404, json: async () => ({}) }
          : { ok: true, status: 200, json: async () => body };
      },
      console,
    });

    runInContext(script, context);
    await settle();

    // The preview is Home's main card since round 6 (30 Oct 2026): it is
    // drawn into "now", where the live or up-next card goes.
    const now = byId.get("now");
    assert.ok(now, "the page drew into its main card");
    return now;
  }

  it("shows the preview the night before, when the day's first round is only 'next'", async () => {
    const preview = await homeWith([
      { status: "next", className: "Stock", time: "10:00" },
      { status: "upcoming", className: "Modified", time: "10:20" },
    ]);

    assert.match(preview.innerHTML, /Nineteen cars are entered/);
  });

  it("still hides it while a round is running", async () => {
    const preview = await homeWith([
      { status: "running", className: "Stock", time: "10:00" },
      { status: "next", className: "Modified", time: "10:20" },
    ]);

    assert.doesNotMatch(preview.innerHTML, /Nineteen cars/);
    assert.match(preview.innerHTML, /On now/);
  });
});

// ------------------------------------------------------------ cloned voices

describe("bug hunt 7: a cloned announcer", () => {
  const STOCK: CatalogueVoice = {
    id: "clubhouse",
    name: "Clubhouse",
    what: "Warm and clear",
    voiceId: "el-voice-clubhouse",
    modelId: "eleven_flash_v2_5",
    stability: 0.5,
    similarity: 0.75,
    style: 0.3,
    speakerBoost: true,
    speed: 1,
    outputFormat: "mp3_44100_128",
    cloned: false,
  };

  const CLONE: CatalogueVoice = { ...STOCK, id: "acme-dave", name: "Dave", voiceId: "el-voice-acme-dave", cloned: true };

  async function serve(): Promise<Harness> {
    const folder = await mkdtemp(join(tmpdir(), "relay-bughunt7-voice-"));
    scratch.push(folder);

    const clock = new Date("2026-10-13T12:00:00.000Z");
    const service = new VoiceService({
      config: { voices: [STOCK, CLONE], monthlyChars: 10_000, maxChars: 120, previewDailyChars: 200, previewMaxChars: 60 },
      dataDir: folder,
      render: async (voice, text) => Buffer.from(`mp3:${voice.id}:${text}`, "utf8"),
      now: () => clock,
    });

    // Acme's operator put its announcer on Acme's list. Brookside has the
    // default plan, with no list at all.
    harness = await start(
      new MemoryStore(),
      tenants([
        { voice: { enabled: true, monthlyChars: 10_000, voices: ["clubhouse", "acme-dave"] } },
        { voice: { enabled: true, monthlyChars: 10_000, voices: [] } },
      ]),
      () => clock,
      () => {},
      service,
    );

    return harness;
  }

  it("is not offered to, or spoken for, a club whose list does not name it", async () => {
    const live = await serve();

    const view = await call(live, "GET", "/api/v1/c/brookside/voice/catalogue", { token: BROOK_TOKEN });
    assert.equal(view.status, 200);
    assert.deepEqual((view.body.voices as { id: string }[]).map((one) => one.id), ["clubhouse"]);

    const said = await call(live, "POST", "/api/v1/c/brookside/voice/speak", {
      token: BROOK_TOKEN,
      json: { voice: "acme-dave", text: "Anything at all", shared: false },
    });

    assert.equal(said.status, 400);

    // The stock voice is still every club's with no list.
    const stock = await call(live, "POST", "/api/v1/c/brookside/voice/speak", {
      token: BROOK_TOKEN,
      json: { voice: "clubhouse", text: "Green flag!", shared: false },
    });

    assert.equal(stock.status, 200);
  });

  it("is still the club's own when its list names it", async () => {
    const live = await serve();

    const said = await call(live, "POST", "/api/v1/c/acme-rc/voice/speak", {
      token: ACME_TOKEN,
      json: { voice: "acme-dave", text: "Green flag!", shared: false },
    });

    assert.equal(said.status, 200);
  });
});

// ------------------------------------------------------------ the brain

describe("bug hunt 7: the brain's two at once", () => {
  const CONFIG: BrainConfig = { model: "test-model", monthlyLines: 50, maxWords: 60 };

  async function brainDir(): Promise<string> {
    const dir = await mkdtemp(join(tmpdir(), "relay-bughunt7-brain-"));
    scratch.push(dir);
    return dir;
  }

  it("holds a burst to two in flight and turns the rest away as busy", async () => {
    let running = 0;
    let most = 0;

    const brain = new BrainService({
      config: CONFIG,
      dataDir: await brainDir(),
      write: async () => {
        running += 1;
        most = Math.max(most, running);
        await new Promise((done) => setTimeout(done, 30));
        running -= 1;
        return "A line.";
      },
    });

    const plenty: BrainPlan = { enabled: true, monthlyLines: 50 };
    const all = await Promise.all(
      Array.from({ length: 8 }, () => brain.write("acme-rc", plenty, "You are a race announcer.", "Dave won.", 40, 0.7)),
    );

    assert.equal(most, 2);
    assert.equal(all.filter((one) => one.ok).length, 2);
    assert.equal(all.filter((one) => !one.ok && one.refusal === "busy").length, 6);

    // And the slots came back: the next one is written.
    const after = await brain.write("acme-rc", plenty, "You are a race announcer.", "Dave won.", 40, 0.7);
    assert.equal(after.ok, true);
  });

  it("gives the slot back when the month is spent", async () => {
    const brain = new BrainService({ config: CONFIG, dataDir: await brainDir(), write: async () => "A line." });
    const one: BrainPlan = { enabled: true, monthlyLines: 1 };

    assert.equal((await brain.write("acme-rc", one, "S", "F", 40, 0.7)).ok, true);

    // Three refusals for the allowance, never one for being busy - a slot
    // a spent request kept would have filled the cap by the second.
    for (let i = 0; i < 3; i++) {
      const refused = await brain.write("acme-rc", one, "S", "F", 40, 0.7);
      assert.equal(refused.ok, false);
      assert.equal(refused.ok ? "" : refused.refusal, "allowance");
    }
  });
});

// ------------------------------------------------------------ the room's vote

describe("bug hunt 7: a vote cast as the ballot moves on", () => {
  const VOTE = "/api/v1/c/acme-rc/vote";
  const BILL = { id: "t1", name: "Bill Hargrove" };
  const SAM = { id: "t2", name: "Sam Teague" };

  it("is not carried into the next race under the old race's key", async () => {
    const live = (harness = await start(new MemoryStore()));

    const open = (race: string) =>
      call(live, "POST", `${VOTE}/open`, { token: ACME_TOKEN, json: { enabled: true, open: true, race, drivers: [BILL, SAM] } });

    assert.equal((await open("Stock A-Main")).status, 200);

    const status = await slowPost(live, `${VOTE}/send`, undefined, { d: BILL.id }, async () => {
      assert.equal((await open("Stock B-Main")).status, 200);
    });

    assert.equal(status, 409);

    const inbox = await call(live, "GET", `${VOTE}/inbox`, { token: ACME_TOKEN });
    assert.deepEqual(inbox.body.items, []);

    // The same phone voting in the new race is one vote there.
    assert.equal((await call(live, "POST", `${VOTE}/send`, { json: { d: SAM.id } })).status, 200);
    assert.equal((await call(live, "GET", `${VOTE}/inbox`, { token: ACME_TOKEN })).body.items.length, 1);
  });
});
