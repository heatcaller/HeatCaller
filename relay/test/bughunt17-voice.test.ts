import assert from "node:assert/strict";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { VoiceService, type CatalogueVoice, type RenderFn, type StreamFn, type VoicePlan } from "../src/voice.js";

// Bug hunt 17, voice.ts (13 Nov 2026): #206.

const VOICE: CatalogueVoice = {
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

const PLAN: VoicePlan = { enabled: true, monthlyChars: 1_000, voices: [] };
const render: RenderFn = async (voice, text) => Buffer.from(`mp3:${voice.id}:${text}`, "utf8");

describe("bug hunt 17 - voice.ts", () => {
  let folder = "";

  beforeEach(async () => {
    folder = await mkdtemp(join(tmpdir(), "relay-bh17-voice-"));
  });

  afterEach(async () => {
    await rm(folder, { recursive: true, force: true });
  });

  /** An upstream that sends <first> pieces and then nothing at all, never ending, until it is told to stop. */
  function stalling(first: number, stopped: { count: number }): StreamFn {
    return async (_voice, _text, signal) => {
      signal.addEventListener("abort", () => (stopped.count += 1), { once: true });

      return {
        [Symbol.asyncIterator]() {
          let sent = 0;

          return {
            next(): Promise<IteratorResult<Uint8Array>> {
              if (sent < first) {
                sent += 1;
                return Promise.resolve({ done: false, value: new Uint8Array([1, 2, 3, 4]) });
              }

              return new Promise<IteratorResult<Uint8Array>>(() => {});
            },
          };
        },
      };
    };
  }

  function service(stream: StreamFn): VoiceService {
    return new VoiceService({
      config: { voices: [VOICE], monthlyChars: 1_000, maxChars: 120 },
      dataDir: folder,
      render,
      stream,
      streamIdleMs: 40,
      maxInFlightPerClub: 1,
      maxWaitingPerClub: 0,
    });
  }

  async function drain(stream: AsyncIterable<Uint8Array>): Promise<{ bytes: number; error: unknown }> {
    let bytes = 0;

    try {
      for await (const piece of stream) bytes += piece.length;
    } catch (error) {
      return { bytes, error };
    }

    return { bytes, error: undefined };
  }

  it("#206: a stream that stalls before any audio gives its slot and its characters back", async () => {
    const stopped = { count: 0 };
    const voice = service(stalling(0, stopped));

    const line = await voice.speakStream("acme-rc", PLAN, "clubhouse", "Sam takes the lead.", false);
    assert.ok(line.ok && "stream" in line);

    const read = await drain(line.stream);
    assert.equal(read.bytes, 0);
    assert.ok(read.error instanceof Error, "a stall is a fault, not a line that ended");
    assert.equal(stopped.count, 1, "ElevenLabs is told to stop");
    assert.equal((await voice.usage("acme-rc", PLAN)).used, 0, "nothing reached the club, so nothing is charged");

    // The club's one slot is free again: the next line is not answered "busy".
    const next = await voice.speakStream("acme-rc", PLAN, "clubhouse", "Two laps to go.", false);
    assert.equal(next.ok, true);
    if (next.ok && "stream" in next) await next.cancel();
  });

  it("#206: a stream that stalls part way is cut off and frees its slot", async () => {
    const stopped = { count: 0 };
    const voice = service(stalling(2, stopped));

    const line = await voice.speakStream("acme-rc", PLAN, "clubhouse", "Sam takes the lead.", false);
    assert.ok(line.ok && "stream" in line);

    const read = await drain(line.stream);
    assert.equal(read.bytes, 8);
    assert.ok(read.error instanceof Error);
    assert.equal(stopped.count, 1);

    const next = await voice.speakStream("acme-rc", PLAN, "clubhouse", "Two laps to go.", false);
    assert.equal(next.ok, true);
    if (next.ok && "stream" in next) await next.cancel();
  });

  it("#206: a stream that keeps arriving is played to the end", async () => {
    const stream: StreamFn = async () =>
      (async function* () {
        for (let index = 0; index < 3; index += 1) {
          await new Promise((resolve) => setTimeout(resolve, 5));
          yield new Uint8Array([1, 2, 3, 4]);
        }
      })();

    const voice = service(stream);
    const line = await voice.speakStream("acme-rc", PLAN, "clubhouse", "Sam takes the lead.", false);
    assert.ok(line.ok && "stream" in line);

    const read = await drain(line.stream);
    assert.equal(read.bytes, 12);
    assert.equal(read.error, undefined);
    assert.equal((await voice.usage("acme-rc", PLAN)).used, "Sam takes the lead.".length);
  });
});
