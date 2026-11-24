import assert from "node:assert/strict";
import { afterEach, describe, it } from "node:test";
import {
  type CatalogueVoice,
  ElevenLabsLogging,
  elevenLabsRenderer,
  elevenLabsStreamer,
  noLoggingFromEnv,
  withNoLogging,
} from "../src/voice.js";

// 15 Nov 2026, children's privacy (plan 2.3): ElevenLabs is asked not to keep
// the relay's lines (enable_logging=false), and a plan that turns that down
// still gets its lines - asked again without it, once, and remembered.
// (24 Nov 2026: tried on a real non-enterprise key, the flag is accepted and
// ignored rather than turned down, so each line is also deleted from the
// history - see elevenlabs-history-delete.test.ts. The fallback is kept for a
// plan that does refuse it.)

const voice = {
  id: "clubhouse",
  voiceId: "abc123",
  modelId: "eleven_v4_turbo",
  outputFormat: "mp3_44100_128",
  stability: 0.5,
  similarity: 0.75,
  style: 0,
  speakerBoost: true,
  speed: 1,
} as unknown as CatalogueVoice;

const realFetch = globalThis.fetch;

/** Answers each request with the next status in turn, and keeps the URLs asked. */
function fakeFetch(statuses: number[]): string[] {
  const urls: string[] = [];

  globalThis.fetch = (async (input: string | URL | Request) => {
    urls.push(String(input));
    const status = statuses.shift() ?? 200;
    return new Response(status === 200 ? new Uint8Array([1, 2, 3, 4]) : "nope", { status });
  }) as typeof fetch;

  return urls;
}

afterEach(() => {
  globalThis.fetch = realFetch;
});

describe("ElevenLabs: don't keep our lines", () => {
  it("is on unless RELAY_VOICE_NO_LOGGING plainly says off", () => {
    assert.equal(noLoggingFromEnv(undefined), true);
    assert.equal(noLoggingFromEnv(""), true);
    assert.equal(noLoggingFromEnv("1"), true);
    assert.equal(noLoggingFromEnv("0"), false);
    assert.equal(noLoggingFromEnv(" Off "), false);
    assert.equal(noLoggingFromEnv("false"), false);
  });

  it("puts the flag after any query already there", () => {
    assert.equal(withNoLogging("https://x/v1/tts/a", true), "https://x/v1/tts/a?enable_logging=false");
    assert.equal(withNoLogging("https://x/v1/tts/a?output_format=mp3", true), "https://x/v1/tts/a?output_format=mp3&enable_logging=false");
    assert.equal(withNoLogging("https://x/v1/tts/a", false), "https://x/v1/tts/a");
  });

  it("sends enable_logging=false on a whole line and on a stream", async () => {
    const urls = fakeFetch([200, 200]);

    await elevenLabsRenderer("key")(voice, "Green flag!");
    await elevenLabsStreamer("key")(voice, "Green flag!", new AbortController().signal);

    assert.equal(urls.length, 2);
    assert.match(urls[0] ?? "", /\?output_format=mp3_44100_128&enable_logging=false$/);
    assert.match(urls[1] ?? "", /\/stream\?output_format=[a-z0-9_]+&enable_logging=false$/);
  });

  it("leaves it off when the relay is told not to ask", async () => {
    const urls = fakeFetch([200]);

    await elevenLabsRenderer("key", undefined, new ElevenLabsLogging(false))(voice, "Green flag!");

    assert.equal(urls.length, 1);
    assert.doesNotMatch(urls[0] ?? "", /enable_logging/);
  });

  it("asks once more without it when a plan turns it down, then remembers", async () => {
    const said: string[] = [];
    const logging = new ElevenLabsLogging(true, (line) => said.push(line));
    const urls = fakeFetch([403, 200, 200, 200]);

    const render = elevenLabsRenderer("key", undefined, logging);
    const stream = elevenLabsStreamer("key", undefined, logging);

    const audio = await render(voice, "Green flag!");
    assert.equal(audio.length, 4);

    // The second line, and the streamer sharing the choice, ask without it straight away.
    await render(voice, "Yellow flag!");
    await stream(voice, "Checkered flag!", new AbortController().signal);

    assert.equal(urls.length, 4);
    assert.match(urls[0] ?? "", /enable_logging=false/);
    assert.doesNotMatch(urls[1] ?? "", /enable_logging/);
    assert.doesNotMatch(urls[2] ?? "", /enable_logging/);
    assert.doesNotMatch(urls[3] ?? "", /enable_logging/);
    assert.equal(logging.asking, false);
    assert.equal(said.length, 1);
    assert.match(said[0] ?? "", /enable_logging=false, 403/);
  });

  it("does not forget the flag over a request that fails both ways", async () => {
    const logging = new ElevenLabsLogging(true);
    const urls = fakeFetch([400, 400]);

    await assert.rejects(elevenLabsRenderer("key", undefined, logging)(voice, "Green flag!"));

    assert.equal(urls.length, 2);
    assert.equal(logging.asking, true);
  });

  it("does not ask twice over a refusal that is not about the flag", async () => {
    const logging = new ElevenLabsLogging(true);
    const urls = fakeFetch([401]);

    await assert.rejects(elevenLabsRenderer("key", undefined, logging)(voice, "Green flag!"));

    assert.equal(urls.length, 1);
    assert.equal(logging.asking, true);
  });
});
