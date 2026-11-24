import assert from "node:assert/strict";
import { afterEach, describe, it } from "node:test";
import { type CatalogueVoice, ElevenLabsLogging, elevenLabsRenderer, elevenLabsStreamer } from "../src/voice.js";

// 24 Nov 2026, Asher: delete each line from ElevenLabs' history. Tried on a
// real key, enable_logging=false is accepted (200) and quietly ignored on a
// non-enterprise plan, so every line - drivers' names in them - stayed in the
// key's history. Every speech answer carries a history-item-id header, and
// each line is now deleted by that exact id, never holding the line up.

const voice = {
  id: "finley",
  voiceId: "fnYMz3F5gMEDGMWcH1ex",
  modelId: "eleven_v4_turbo",
  outputFormat: "mp3_44100_128",
  stability: 0.5,
  similarity: 0.75,
  style: 0,
  speakerBoost: true,
  speed: 1,
} as unknown as CatalogueVoice;

const realFetch = globalThis.fetch;

interface Asked {
  readonly method: string;
  readonly url: string;
  readonly key: string | null;
}

/**
 * Speech answers carry a history-item-id; a DELETE is answered with the
 * next of deleteStatuses (200 when they run out). Nothing reaches the
 * network: every test waits for its deletes (settled) before afterEach
 * puts the real fetch back.
 */
function fakeElevenLabs(options: { historyId?: string | null; deleteStatuses?: number[]; deleteHangs?: boolean } = {}): Asked[] {
  const asked: Asked[] = [];
  const statuses = options.deleteStatuses ?? [];

  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const method = init?.method ?? "GET";
    const key = new Headers(init?.headers).get("xi-api-key");
    asked.push({ method, url: String(input), key });

    if (method === "DELETE") {
      if (options.deleteHangs === true) {
        return new Promise<Response>((_, reject) => {
          init?.signal?.addEventListener("abort", () => reject(new Error("timed out")), { once: true });
        });
      }

      return new Response("{}", { status: statuses.shift() ?? 200 });
    }

    const headers = new Headers();
    const id = options.historyId === undefined ? "hist_ABC123" : options.historyId;
    if (id !== null) headers.set("history-item-id", id);
    return new Response(new Uint8Array([1, 2, 3, 4]), { status: 200, headers });
  }) as typeof fetch;

  return asked;
}

const deletes = (asked: readonly Asked[]): Asked[] => asked.filter((a) => a.method === "DELETE");

afterEach(() => {
  globalThis.fetch = realFetch;
});

describe("ElevenLabs: each line deleted from the key's history", () => {
  it("deletes a whole line by the id its own answer carried, with the same key", async () => {
    const asked = fakeElevenLabs();
    const logging = new ElevenLabsLogging(true);

    const audio = await elevenLabsRenderer("the-key", undefined, logging)(voice, "Green flag!");
    assert.equal(audio.length, 4, "the line is handed back whatever the delete does");
    await logging.settled();

    assert.deepEqual(deletes(asked), [{ method: "DELETE", url: "https://api.elevenlabs.io/v1/history/hist_ABC123", key: "the-key" }]);
  });

  it("deletes a streamed line when the stream has been read to the end", async () => {
    const asked = fakeElevenLabs();
    const logging = new ElevenLabsLogging(true);

    const stream = await elevenLabsStreamer("the-key", undefined, logging)(voice, "Green flag!", new AbortController().signal);
    assert.equal(deletes(asked).length, 0, "not while the line is still to be read");

    let bytes = 0;
    for await (const piece of stream) bytes += piece.length;
    assert.equal(bytes, 4);
    await logging.settled();

    assert.equal(deletes(asked).length, 1);
    assert.equal(deletes(asked)[0]?.url, "https://api.elevenlabs.io/v1/history/hist_ABC123");
  });

  it("deletes a streamed line that is stopped and never read, once", async () => {
    const asked = fakeElevenLabs();
    const logging = new ElevenLabsLogging(true);
    const stop = new AbortController();

    const stream = await elevenLabsStreamer("the-key", undefined, logging)(voice, "Green flag!", stop.signal);
    stop.abort();
    // A reader that then gives up too must not delete it twice.
    await stream[Symbol.asyncIterator]().return?.(undefined);
    await logging.settled();

    assert.equal(deletes(asked).length, 1);
  });

  it("deletes a streamed line cut off part way, once", async () => {
    const asked = fakeElevenLabs();
    const logging = new ElevenLabsLogging(true);
    const stop = new AbortController();

    const stream = await elevenLabsStreamer("the-key", undefined, logging)(voice, "Green flag!", stop.signal);
    for await (const _ of stream) break;
    stop.abort();
    await logging.settled();

    assert.equal(deletes(asked).length, 1);
  });

  it("deletes nothing when the relay is told not to ask (RELAY_VOICE_NO_LOGGING=0)", async () => {
    const asked = fakeElevenLabs();
    const logging = new ElevenLabsLogging(false);

    await elevenLabsRenderer("the-key", undefined, logging)(voice, "Green flag!");
    await logging.settled();

    assert.equal(deletes(asked).length, 0);
  });

  it("sends nothing for an answer with no id, or an id that isn't one", async () => {
    for (const historyId of [null, "../voices/abc", "a b"]) {
      const asked = fakeElevenLabs({ historyId });
      const logging = new ElevenLabsLogging(true);

      await elevenLabsRenderer("the-key", undefined, logging)(voice, "Green flag!");
      await logging.settled();

      assert.equal(deletes(asked).length, 0, String(historyId));
    }
  });

  it("takes a 404 as already gone, and says nothing", async () => {
    const said: string[] = [];
    const asked = fakeElevenLabs({ deleteStatuses: [404, 200] });
    const logging = new ElevenLabsLogging(true, (line) => said.push(line));
    const render = elevenLabsRenderer("the-key", undefined, logging);

    await render(voice, "Green flag!");
    await logging.settled();
    await render(voice, "Yellow flag!");
    await logging.settled();

    assert.equal(deletes(asked).length, 2);
    assert.deepEqual(said, []);
    assert.equal(logging.deleting, true);
  });

  it("remembers a key without the Speech history permission, says so once, and stops asking", async () => {
    const said: string[] = [];
    const asked = fakeElevenLabs({ deleteStatuses: [403] });
    const logging = new ElevenLabsLogging(true, (line) => said.push(line));
    const render = elevenLabsRenderer("the-key", undefined, logging);
    const stream = elevenLabsStreamer("the-key", undefined, logging);

    await render(voice, "Green flag!");
    await logging.settled();
    assert.equal(logging.deleting, false);

    // The next line, and the streamer sharing the choice, don't try again.
    await render(voice, "Yellow flag!");
    for await (const _ of await stream(voice, "Checkered flag!", new AbortController().signal)) {
      // read to the end
    }
    await logging.settled();

    assert.equal(deletes(asked).length, 1);
    assert.equal(said.length, 1);
    assert.match(said[0] ?? "", /can't remove lines from its history/);
    assert.match(said[0] ?? "", /Speech history permission/);
    // Still asking not to keep them, on the next request as before.
    assert.equal(logging.asking, true);
    assert.match(asked.filter((a) => a.method === "POST").at(-1)?.url ?? "", /enable_logging=false/);
  });

  it("never holds a line up or fails it over a delete that hangs or breaks", async () => {
    const said: string[] = [];
    const asked = fakeElevenLabs({ deleteHangs: true });
    const logging = new ElevenLabsLogging(true, (line) => said.push(line), 50);

    const audio = await elevenLabsRenderer("the-key", undefined, logging)(voice, "Green flag!");
    assert.equal(audio.length, 4);
    await logging.settled();

    assert.equal(deletes(asked).length, 1);
    assert.equal(said.length, 1, "a failed delete is said once");
    assert.equal(logging.deleting, true, "and the next line is still tried");

    fakeElevenLabs({ deleteStatuses: [500] });
    await elevenLabsRenderer("the-key", undefined, logging)(voice, "Yellow flag!");
    await logging.settled();
    assert.equal(said.length, 1, "not said again");
  });

  it("still deletes when the plan turned the flag down and the line went without it", async () => {
    // The fallback kept for a plan that does refuse enable_logging=false.
    const asked: Asked[] = [];
    let posts = 0;
    globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
      const method = init?.method ?? "GET";
      asked.push({ method, url: String(input), key: null });
      if (method === "DELETE") return new Response("{}", { status: 200 });
      posts++;
      if (posts === 1) return new Response("no", { status: 403 });
      return new Response(new Uint8Array([1]), { status: 200, headers: { "history-item-id": "hist_2" } });
    }) as typeof fetch;

    const logging = new ElevenLabsLogging(true);
    await elevenLabsRenderer("the-key", undefined, logging)(voice, "Green flag!");
    await logging.settled();

    assert.equal(logging.asking, false);
    assert.deepEqual(deletes(asked).map((a) => a.url), ["https://api.elevenlabs.io/v1/history/hist_2"]);
  });
});
