import assert from "node:assert/strict";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import { STREAM_FORMAT, VoiceService, VoiceUpstreamError, type CatalogueVoice, type RenderFn, type StreamFn, type VoiceConfig } from "../src/voice.js";
import { ACME_TOKEN, start, tenants, type Harness } from "./helpers.js";

// The HeatCaller voice streamed to the laptop as it is made (11 Nov 2026,
// voice speed plan): the club's race calls start in about a fifth of a
// second instead of after the whole line. What these protect is the same
// as for a whole line - the allowance, the slots, the voices a club may use
// - and that a stream that fails or carries nothing costs the club nothing.

const VOICE: CatalogueVoice = {
  id: "clubhouse",
  name: "Clubhouse",
  what: "Warm and clear",
  voiceId: "el-voice-clubhouse",
  modelId: "eleven_v4_turbo",
  stability: 0.5,
  similarity: 0.75,
  style: 0.3,
  speakerBoost: true,
  speed: 1,
  outputFormat: "",
  cloned: false,
};

function config(): VoiceConfig {
  return { voices: [VOICE], monthlyChars: 1_000, maxChars: 120 };
}

const render: RenderFn = async (voice, text) => Buffer.from(`mp3:${voice.id}:${text}`, "utf8");

/** A stream that sends these pieces, and records what it was asked for. */
function streaming(pieces: readonly string[]): { stream: StreamFn; asked: string[]; stopped: () => boolean } {
  const asked: string[] = [];
  let stopped = false;

  return {
    asked,
    stopped: () => stopped,
    stream: async (_voice, text, signal) => {
      asked.push(text);
      signal.addEventListener("abort", () => {
        stopped = true;
      });

      return (async function* () {
        for (const piece of pieces) {
          yield Buffer.from(piece, "utf8");
        }
      })();
    },
  };
}

describe("the HeatCaller voice, streamed", () => {
  let folder = "";
  let harness: Harness | undefined;
  const clock = new Date("2026-11-11T12:00:00.000Z");

  beforeEach(async () => {
    folder = await mkdtemp(join(tmpdir(), "relay-voicestream-"));
  });

  afterEach(async () => {
    await harness?.close();
    harness = undefined;
    await rm(folder, { recursive: true, force: true });
  });

  async function serve(stream?: StreamFn): Promise<Harness> {
    const service = new VoiceService({ config: config(), dataDir: folder, render, stream, now: () => clock });

    harness = await start(
      new MemoryStore(),
      tenants([{ voice: { enabled: true, monthlyChars: 1_000, voices: [] } }, { voice: { enabled: false, monthlyChars: 1_000, voices: [] } }]),
      () => clock,
      () => {},
      service,
    );

    return harness;
  }

  async function speak(where: Harness, body: unknown): Promise<{ status: number; headers: Headers; bytes: Buffer }> {
    const response = await fetch(`${where.base}/api/v1/c/acme-rc/voice/speak`, {
      method: "POST",
      headers: { authorization: `Bearer ${ACME_TOKEN}`, "content-type": "application/json" },
      body: JSON.stringify(body),
    });

    return { status: response.status, headers: response.headers, bytes: Buffer.from(await response.arrayBuffer()) };
  }

  it("sends the line as it comes, as raw PCM, and counts it once", async () => {
    const fake = streaming(["one ", "two ", "three"]);
    const where = await serve(fake.stream);
    const line = "Dee Ploy, six eight seven!";

    const answer = await speak(where, { voice: "clubhouse", text: line, shared: false, stream: true });

    assert.equal(answer.status, 200);
    assert.equal(answer.headers.get("x-voice-format"), STREAM_FORMAT);
    assert.equal(answer.headers.get("content-type"), "audio/pcm");
    assert.equal(answer.bytes.toString("utf8"), "one two three");
    assert.equal(answer.headers.get("x-voice-used"), String(line.length));
    assert.deepEqual(fake.asked, [line]);
  });

  it("gives the characters back when ElevenLabs refuses before any audio", async () => {
    const where = await serve(async () => {
      throw new VoiceUpstreamError(500, "no");
    });

    const failed = await speak(where, { voice: "clubhouse", text: "Dee Ploy, six eight seven!", shared: false, stream: true });
    assert.equal(failed.status, 502);

    // A whole line after it is charged only for itself.
    const next = await speak(where, { voice: "clubhouse", text: "Al!", shared: false });
    assert.equal(next.status, 200);
    assert.equal(next.headers.get("x-voice-used"), "3");
  });

  it("gives the characters back when nothing at all arrives", async () => {
    const where = await serve(streaming([]).stream);

    await speak(where, { voice: "clubhouse", text: "Dee Ploy, six eight seven!", shared: false, stream: true });

    const next = await speak(where, { voice: "clubhouse", text: "Al!", shared: false });
    assert.equal(next.headers.get("x-voice-used"), "3");
  });

  it("answers a line every club shares whole, so it can be kept for all of them", async () => {
    const fake = streaming(["pcm"]);
    const where = await serve(fake.stream);

    const answer = await speak(where, { voice: "clubhouse", text: "Green flag!", shared: true, stream: true });

    assert.equal(answer.status, 200);
    assert.equal(answer.headers.get("x-voice-format"), null);
    assert.equal(answer.bytes.toString("utf8"), "mp3:clubhouse:Green flag!");
    assert.deepEqual(fake.asked, []);
  });

  it("answers whole from the cache, free, for a line already said", async () => {
    const fake = streaming(["pcm"]);
    const where = await serve(fake.stream);
    const line = "Dee Ploy, six eight seven!";

    await speak(where, { voice: "clubhouse", text: line, shared: false });
    const again = await speak(where, { voice: "clubhouse", text: line, shared: false, stream: true });

    assert.equal(again.headers.get("x-voice-cache"), "hit");
    assert.equal(again.headers.get("x-voice-format"), null);
    assert.deepEqual(fake.asked, []);
  });

  it("answers whole on a relay with no stream configured - an older laptop's request, a newer one's, the same", async () => {
    const where = await serve(undefined);

    const answer = await speak(where, { voice: "clubhouse", text: "Dee Ploy, six eight seven!", shared: false, stream: true });

    assert.equal(answer.status, 200);
    assert.equal(answer.headers.get("x-voice-format"), null);
    assert.equal(answer.bytes.toString("utf8"), "mp3:clubhouse:Dee Ploy, six eight seven!");
  });

  it("is held to the same checks as a whole line", async () => {
    const fake = streaming(["pcm"]);
    const where = await serve(fake.stream);

    const unknown = await speak(where, { voice: "not-ours", text: "Hello", shared: false, stream: true });
    const long = await speak(where, { voice: "clubhouse", text: "x".repeat(500), shared: false, stream: true });

    assert.equal(unknown.status, 400);
    assert.equal(long.status, 400);
    assert.deepEqual(fake.asked, []);
  });

  it("gives back the slot and stops ElevenLabs once the line is sent", async () => {
    const fake = streaming(["a", "b"]);
    const where = await serve(fake.stream);

    // More lines than the club's two slots, one after another: each gives its slot back.
    for (let i = 0; i < 4; i++) {
      const answer = await speak(where, { voice: "clubhouse", text: `Line ${i}`, shared: false, stream: true });
      assert.equal(answer.status, 200);
    }

    assert.equal(fake.stopped(), true);
  });
});
