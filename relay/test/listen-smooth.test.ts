import assert from "node:assert/strict";
import { createContext, runInContext } from "node:vm";
import { afterEach, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";
import { FakeAudioWorld } from "./listen-fake-audio.js";

/**
 * LISTEN NOW WITHOUT THE HICCUPS (11 Nov 2026, asked for by Asher: the
 * stream on the club's page was "a little choppy").
 *
 * Two causes, both in the page's player (LISTEN_JS in pages.ts), and both
 * measured rather than guessed. The laptop's encoder hands over a stretch
 * every 3.004 seconds, steady to a few milliseconds, so the laptop was not
 * one of them.
 *
 *   - Every stretch was decoded at the player's context's rate. On a phone
 *     whose context is not at the stream's 44.1 kHz, each stretch was then
 *     resampled on its own, and the joins stopped meeting: in Chromium,
 *     the laptop's own segments decoded at 48 kHz had an error of up to
 *     0.04 at every join against 0.002 anywhere else, a tick every three
 *     seconds. Decoded offline at the stream's own rate it is 0.001.
 *
 *   - The player kept a third of a second in hand, and looked for the next
 *     stretch every two seconds. A stretch was found anywhere up to two
 *     seconds after it landed, so the sound ran dry and started again
 *     whenever one was found late. Now it looks every second, starts a
 *     second ahead, and grows that cushion each time it still runs dry.
 */

let harness: Harness | undefined;

afterEach(async () => {
  await harness?.close();
  harness = undefined;
});

async function settle(): Promise<void> {
  for (let i = 0; i < 20; i++) {
    await new Promise((done) => setImmediate(done));
  }
}

async function liveScript(): Promise<string> {
  harness = await start(new MemoryStore());

  const opened = await call(harness, "POST", "/api/v1/c/acme-rc/listen/open", { token: ACME_TOKEN, json: { open: true } });
  assert.equal(opened.status, 200);

  const page = (await call(harness, "GET", "/c/acme-rc/live")).text;
  const match = /<script[^>]*>([\s\S]*)<\/script>/.exec(page);
  assert.ok(match, "the page carries a script");
  return match[1] as string;
}

interface Held {
  id: string;
  publishedAt: string;
  audio: string;
  mix?: { run: string; n: number };
}

const at = (seconds: number) => new Date(Date.UTC(2026, 10, 11, 19, 0, 0) + seconds * 1000).toISOString();
const mix = (n: number, run = "R1"): Held => ({ id: `L${run}-${String(n).padStart(6, "0")}`, publishedAt: at(n * 3), audio: `mix-${n}`, mix: { run, n } });
const line = (n: number): Held => ({ id: `L0-${String(n).padStart(6, "0")}`, publishedAt: at(n), audio: `line-${n}` });

/** Two seconds of a stretch's own, in a three-second decode. */
const MIX_FRAMES = Math.round((2 * 44100) / 1152);

function listenPage(script: string, held: Held[], options: { offline?: boolean } = {}) {
  const from = script.indexOf("(function () {\n  const btn = document.getElementById('listenbtn');");
  assert.notEqual(from, -1, "the live page carries the player");

  const listeners: (() => void)[] = [];
  const intervals: { run: () => void; ms: number }[] = [];
  const note = { textContent: "" };
  const btn = { textContent: "", addEventListener: (_type: string, run: () => void) => listeners.push(run) };
  const sound = new FakeAudioWorld();
  const { AudioContext, Audio, OfflineAudioContext, atob } = sound.globals();
  sound.bufferLength = 3 * 44100;

  const reply = (body: unknown) => ({ ok: true, json: async () => body });

  const context = createContext({
    CLUB: "acme-rc",
    Audio,
    atob,
    document: { hidden: false, getElementById: (id: string) => (id === "listenbtn" ? btn : id === "listennote" ? note : null), addEventListener() {} },
    window: {
      AudioContext,
      ...(options.offline === true ? { OfflineAudioContext } : {}),
      setInterval: (run: () => void, ms: number) => intervals.push({ run, ms }),
      clearInterval: () => {
        intervals.length = 0;
      },
      addEventListener() {},
    },
    setTimeout: (run: () => void, ms: number) => setTimeout(run, ms >= 5000 ? ms : 0),
    clearTimeout,
    Date,
    fetch: async (url: string) => {
      if (url.endsWith("/collection/listen?meta=1")) {
        return reply({ items: held.map((one) => ({ id: one.id, publishedAt: one.publishedAt, bytes: 1 })), meta: true });
      }

      const id = decodeURIComponent(url.slice(url.lastIndexOf("/") + 1));
      const one = held.find((each) => each.id === id);

      if (one === undefined) {
        return { ok: false, status: 404, json: async () => ({}) };
      }

      const body = one.mix === undefined
        ? { audio: one.audio, said: id }
        : { kind: "mix", audio: one.audio, said: "", lead: 4, frames: MIX_FRAMES, spf: 1152, rate: 44100, run: one.mix.run, n: one.mix.n, captions: [] };

      return reply({ id, body });
    },
  });

  runInContext(script.slice(from), context);

  return {
    sound,
    intervals,
    async press(): Promise<void> {
      sound.inPress = true;

      try {
        listeners.forEach((run) => run());
      } finally {
        sound.inPress = false;
      }

      await settle();
    },
    async poll(): Promise<void> {
      [...intervals].forEach((one) => one.run());
      await settle();
    },
  };
}

describe("Listen Now plays the stream smoothly (11 Nov 2026)", () => {
  it("decodes the stream at its own rate when the phone's context runs at another", async () => {
    const held: Held[] = [mix(1)];
    const page = listenPage(await liveScript(), held, { offline: true });
    page.sound.contextRate = 48000;

    await page.press();

    assert.deepEqual(page.sound.decodedOn, [{ tag: "mix-1", on: 44100 }]);
    assert.deepEqual(page.sound.played(), ["mix-1"]);
  });

  it("decodes a line, which joins nothing, on the player's own context as it always did", async () => {
    const held: Held[] = [line(1)];
    const page = listenPage(await liveScript(), held, { offline: true });
    page.sound.contextRate = 48000;

    await page.press();
    held.push(line(2));
    await page.poll();

    assert.deepEqual(page.sound.decodedOn, [{ tag: "line-2", on: "context" }]);
  });

  it("decodes on the player's context when that already runs at the stream's rate, or when there is no offline context", async () => {
    const same = listenPage(await liveScript(), [mix(1)], { offline: true });
    same.sound.contextRate = 44100;
    await same.press();
    assert.deepEqual(same.sound.decodedOn, [{ tag: "mix-1", on: "context" }]);

    await harness?.close();
    const none = listenPage(await liveScript(), [mix(1)]);
    none.sound.contextRate = 48000;
    await none.press();
    assert.deepEqual(none.sound.decodedOn, [{ tag: "mix-1", on: "context" }]);
  });

  it("asks the relay what is new every second while listening", async () => {
    const page = listenPage(await liveScript(), [mix(1)]);

    await page.press();

    assert.deepEqual(page.intervals.map((one) => one.ms), [1000]);
  });

  it("starts the stream a second ahead, so the next stretch has a second to turn up", async () => {
    const held: Held[] = [mix(1)];
    const page = listenPage(await liveScript(), held);
    page.sound.now = 10;

    await page.press();

    const [one] = page.sound.starts;
    assert.ok(one !== undefined);
    assert.ok(Math.abs(one.at - 11) < 1e-9, `started at ${one.at}`);
  });

  it("keeps half a second more in hand each time the next stretch still comes too late, up to three seconds", async () => {
    const held: Held[] = [mix(1)];
    const page = listenPage(await liveScript(), held);

    await page.press();

    // Each stretch lands after the one before has run out.
    const aheads: number[] = [];

    for (let n = 2; n <= 7; n++) {
      const before = page.sound.starts[page.sound.starts.length - 1];
      assert.ok(before !== undefined);
      page.sound.now = before.at + before.length + 0.5;
      held.push(mix(n));
      await page.poll();

      const started = page.sound.starts[page.sound.starts.length - 1];
      assert.ok(started !== undefined);
      aheads.push(Math.round((started.at - page.sound.now) * 10) / 10);
    }

    assert.deepEqual(aheads, [1.5, 2, 2.5, 3, 3, 3]);
  });

  it("does not grow the cushion for a stretch after a silence, which was never sent", async () => {
    const held: Held[] = [mix(1)];
    const page = listenPage(await liveScript(), held);

    await page.press();
    page.sound.now = 20;
    held.push(mix(5));
    await page.poll();

    const started = page.sound.starts[1];
    assert.ok(started !== undefined);
    assert.ok(Math.abs(started.at - 21) < 1e-9, `started at ${started.at}`);
  });

  it("a stretch that arrives in time still joins straight on, to the sample", async () => {
    const held: Held[] = [mix(1)];
    const page = listenPage(await liveScript(), held);

    await page.press();
    page.sound.now = 1.5;
    held.push(mix(2));
    await page.poll();

    const [one, two] = page.sound.starts;
    assert.ok(one !== undefined && two !== undefined);
    assert.ok(Math.abs(two.at - (one.at + one.length)) < 1e-9);
  });

  it("Stop and Listen again start the cushion at a second again", async () => {
    const held: Held[] = [mix(1)];
    const page = listenPage(await liveScript(), held);

    await page.press();
    const first = page.sound.starts[0];
    assert.ok(first !== undefined);
    page.sound.now = first.at + first.length + 0.5;
    held.push(mix(2));
    await page.poll();

    await page.press(); // Stop
    page.sound.now = 40;
    held.push(mix(3));
    await page.press(); // Listen

    const again = page.sound.starts[page.sound.starts.length - 1];
    assert.ok(again !== undefined);
    assert.equal(again.tag, "mix-3");
    assert.ok(Math.abs(again.at - 41) < 1e-9, `started at ${again.at}`);
  });
});
