import assert from "node:assert/strict";
import { createContext, runInContext } from "node:vm";
import { describe, it } from "node:test";
import { DEFAULT_CONTEXT, livePage } from "../src/pages.js";
import { pageParts } from "./driverexp-rp1-fakepage.js";

/**
 * THE LISTEN-ALONG PLAYER BETWEEN RACES (26 Oct 2026, asked for by Asher:
 * "can't hear the heat reminders that fire between races by listening
 * through the relay").
 *
 * The player marked a line heard BEFORE fetching it, so a fetch that failed
 * lost that line for good - and between races a reminder is the only line
 * for minutes, so losing it was losing everything. And one sweep runs at a
 * time, so a fetch that never answered stopped the player for good. Run as
 * the live page's own player script, with a clock the test moves.
 */

/** One media element as the page sees it, and every line it was asked to play. */
class FakeAudio {
  static made: FakeAudio[] = [];
  static played: string[] = [];
  src = "";
  onended: (() => void) | null = null;
  onerror: (() => void) | null = null;

  constructor() {
    FakeAudio.made.push(this);
  }

  play(): Promise<void> {
    // Not the twentieth of a second of silence that unlocks the element.
    if (!this.src.includes("//sQx")) {
      FakeAudio.played.push(this.src.slice(this.src.indexOf(",") + 1));
    }

    return Promise.resolve();
  }

  pause(): void {}
}

/** How the relay answers one line's read: as held, a failure, or never. */
type Answer = "ok" | 500 | 404 | "hang";

interface Line {
  id: string;
  audio: string;
  answers: Answer[];
}

async function settle(): Promise<void> {
  for (let i = 0; i < 30; i++) {
    await new Promise((done) => setImmediate(done));
  }
}

function player(held: Line[]) {
  const { script } = pageParts(livePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, listening: true } }));
  const from = script.indexOf("(function () {\n  const btn = document.getElementById('listenbtn');");
  assert.notEqual(from, -1, "the live page carries the player");

  const listeners: (() => void)[] = [];
  const intervals: (() => void)[] = [];
  let clock = 0;
  const timers: { at: number; run: () => void }[] = [];
  const reads: string[] = [];
  const note = { textContent: "" };
  const btn = { textContent: "", addEventListener: (_type: string, run: () => void) => listeners.push(run) };

  FakeAudio.made = [];
  FakeAudio.played = [];

  const reply = (body: unknown) => ({ ok: true, status: 200, json: async () => body });

  const context = createContext({
    CLUB: "acme-rc",
    Audio: FakeAudio,
    AbortController,
    document: { getElementById: (id: string) => (id === "listenbtn" ? btn : id === "listennote" ? note : null) },
    window: {
      setInterval: (run: () => void) => intervals.push(run),
      clearInterval: () => {
        intervals.length = 0;
      },
    },
    setTimeout: (run: () => void, ms: number) => {
      timers.push({ at: clock + ms, run });
      return timers.length;
    },
    clearTimeout: (handle: number) => {
      if (timers[handle - 1] !== undefined) timers[handle - 1] = { at: Infinity, run: () => {} };
    },
    fetch: async (url: string, init?: { signal?: AbortSignal }) => {
      reads.push(url);

      if (url.endsWith("/collection/listen?meta=1")) {
        return reply({ items: held.map((one) => ({ id: one.id, publishedAt: "2026-10-26T19:00:00.000Z", bytes: 1 })), meta: true });
      }

      const id = decodeURIComponent(url.slice(url.lastIndexOf("/") + 1));
      const one = held.find((line) => line.id === id);
      const answer: Answer = one === undefined ? 404 : one.answers.length > 1 ? (one.answers.shift() as Answer) : (one.answers[0] ?? "ok");

      if (answer === "hang") {
        return new Promise((_resolve, reject) => {
          init?.signal?.addEventListener("abort", () => reject(Object.assign(new Error("aborted"), { name: "AbortError" })));
        });
      }

      if (answer !== "ok" || one === undefined) {
        return { ok: false, status: answer === "ok" ? 404 : answer, json: async () => ({}) };
      }

      return reply({ id, body: { audio: one.audio, said: id } });
    },
  });

  runInContext(script.slice(from), context);

  return {
    reads,
    async press(): Promise<void> {
      listeners.forEach((run) => run());
      await settle();
    },
    /** One tick of the player's two-second timer, the clock moved on with it. */
    async poll(): Promise<void> {
      clock += 2000;

      for (const timer of timers.filter((t) => t.at <= clock)) {
        timer.at = Infinity;
        timer.run();
      }

      await settle();
      [...intervals].forEach((run) => run());
      await settle();
    },
    /** The line now playing reaching its end. */
    async ended(): Promise<void> {
      FakeAudio.made[0]?.onended?.();
      await settle();
    },
    played: () => FakeAudio.played,
  };
}

describe("the listen-along player, between races", () => {
  it("a reminder whose fetch failed once is fetched again and played", async () => {
    const held: Line[] = [{ id: "L1", audio: "lap-call", answers: ["ok"] }];
    const page = player(held);

    await page.press();

    held.push({ id: "L2", audio: "heat-3-to-the-drivers-stand", answers: [500, "ok"] });
    await page.poll();
    assert.deepEqual(page.played(), [], "the failed fetch plays nothing yet");

    await page.poll();
    assert.deepEqual(page.played(), ["heat-3-to-the-drivers-stand"]);
  });

  it("lines after a failed one wait for it, so they still play in the order they were said", async () => {
    const held: Line[] = [];
    const page = player(held);

    await page.press();

    held.push({ id: "L1", audio: "five-minutes", answers: [500, "ok"] });
    held.push({ id: "L2", audio: "one-minute", answers: ["ok"] });
    await page.poll();
    assert.deepEqual(page.played(), []);

    await page.poll();
    await page.ended();
    assert.deepEqual(page.played(), ["five-minutes", "one-minute"]);
  });

  it("a line that keeps failing is passed over after three tries, and the next one plays", async () => {
    const held: Line[] = [];
    const page = player(held);

    await page.press();

    held.push({ id: "L1", audio: "broken", answers: [500] });
    held.push({ id: "L2", audio: "heat-4", answers: ["ok"] });

    await page.poll();
    await page.poll();
    await page.poll();
    assert.deepEqual(page.played(), ["heat-4"]);

    // And never asked for again.
    const before = page.reads.filter((url) => url.endsWith("/L1")).length;
    await page.poll();
    assert.equal(page.reads.filter((url) => url.endsWith("/L1")).length, before);
  });

  it("a line that has rolled off the relay is not asked for again", async () => {
    const held: Line[] = [];
    const page = player(held);

    await page.press();

    held.push({ id: "L1", audio: "gone", answers: [404] });
    await page.poll();
    await page.poll();
    assert.equal(page.reads.filter((url) => url.endsWith("/L1")).length, 1);
  });

  it("a fetch that never answers is given up on, and the player carries on", async () => {
    const held: Line[] = [];
    const page = player(held);

    await page.press();

    held.push({ id: "L1", audio: "heat-5", answers: ["hang", "ok"] });

    // Five ticks of waiting on it - one sweep at a time, so nothing else
    // is asked for meanwhile.
    for (let i = 0; i < 5; i++) {
      await page.poll();
    }

    assert.deepEqual(page.played(), []);

    // Ten seconds after it was asked for, it is given up and asked again.
    await page.poll();
    await page.poll();
    assert.deepEqual(page.played(), ["heat-5"]);
  });
});
