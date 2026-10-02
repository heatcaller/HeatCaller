import assert from "node:assert/strict";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import {
  readVoiceConfig,
  readVoicePlan,
  stripAudioTags,
  VoiceService,
  type CatalogueVoice,
  type RenderFn,
  type VoiceConfig,
} from "../src/voice.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, tenants, type Harness } from "./helpers.js";

// The HeatCaller voice: ElevenLabs on HeatCaller's account, spoken through
// the relay. What these tests are really protecting is the money and the
// key - a club must not be able to name a voice it was not given, spend
// past its allowance, or see anything that would let it call ElevenLabs
// itself.

const CATALOGUE: CatalogueVoice[] = [
  {
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
  },
  {
    id: "rostrum",
    name: "Rostrum",
    what: "Big race-day announcer",
    voiceId: "el-voice-rostrum",
    modelId: "eleven_v3",
    stability: 0.4,
    similarity: 0.8,
    style: 0.6,
    speakerBoost: true,
    speed: 1,
    outputFormat: "mp3_44100_128",
  },
];

function config(overrides: Partial<VoiceConfig> = {}): VoiceConfig {
  return { voices: CATALOGUE, monthlyChars: 1_000, maxChars: 120, ...overrides };
}

interface Rendered {
  readonly voice: string;
  readonly text: string;
}

/** A renderer that records what it was asked for, so a test can prove a line never reached ElevenLabs. */
function recorder(audio = "mp3-bytes"): { render: RenderFn; asked: Rendered[] } {
  const asked: Rendered[] = [];

  return {
    asked,
    render: async (voice, text) => {
      asked.push({ voice: voice.voiceId, text });
      return Buffer.from(`${audio}:${voice.id}:${text}`, "utf8");
    },
  };
}

describe("the HeatCaller voice", () => {
  let folder = "";
  let harness: Harness | undefined;
  let clock = new Date("2026-09-20T12:00:00.000Z");

  beforeEach(async () => {
    folder = await mkdtemp(join(tmpdir(), "relay-voice-"));
    clock = new Date("2026-09-20T12:00:00.000Z");
  });

  afterEach(async () => {
    await harness?.close();
    harness = undefined;
    await rm(folder, { recursive: true, force: true });
  });

  async function serve(
    options: {
      render?: RenderFn;
      voiceConfig?: VoiceConfig;
      acme?: { enabled: boolean; monthlyChars?: number; voices?: string[] };
      brook?: { enabled: boolean; monthlyChars?: number; voices?: string[] };
      maxInFlight?: number;
      maxWaiting?: number;
    } = {},
  ): Promise<Harness> {
    const voiceConfig = options.voiceConfig ?? config();

    const service = new VoiceService({
      config: voiceConfig,
      dataDir: folder,
      render: options.render,
      now: () => clock,
      ...(options.maxInFlight === undefined ? {} : { maxInFlight: options.maxInFlight }),
      ...(options.maxWaiting === undefined ? {} : { maxWaiting: options.maxWaiting }),
    });

    const acme = options.acme ?? { enabled: true };
    const brook = options.brook ?? { enabled: false };

    harness = await start(
      new MemoryStore(),
      tenants([
        { voice: { enabled: acme.enabled, monthlyChars: acme.monthlyChars ?? 1_000, voices: acme.voices ?? [] } },
        { voice: { enabled: brook.enabled, monthlyChars: brook.monthlyChars ?? 1_000, voices: brook.voices ?? [] } },
      ]),
      () => clock,
      () => {},
      service,
    );

    return harness;
  }

  /** The raw answer, because the audio is bytes and `call` turns everything into text. */
  async function speak(
    where: Harness,
    body: unknown,
    token = ACME_TOKEN,
    club = "acme-rc",
  ): Promise<{ status: number; headers: Headers; bytes: Buffer; json: any }> {
    const response = await fetch(`${where.base}/api/v1/c/${club}/voice/speak`, {
      method: "POST",
      headers: { authorization: `Bearer ${token}`, "content-type": "application/json" },
      body: JSON.stringify(body),
    });

    const bytes = Buffer.from(await response.arrayBuffer());
    let json: unknown = undefined;

    try {
      json = JSON.parse(bytes.toString("utf8"));
    } catch {
      json = undefined;
    }

    return { status: response.status, headers: response.headers, bytes, json };
  }

  describe("the catalogue", () => {
    it("needs the club's own token", async () => {
      const where = await serve();

      assert.equal((await call(where, "GET", "/api/v1/c/acme-rc/voice/catalogue")).status, 401);
      assert.equal((await call(where, "GET", "/api/v1/c/acme-rc/voice/catalogue", { token: BROOK_TOKEN })).status, 403);
    });

    it("lists the voices with a revision, and never the ElevenLabs voice id", async () => {
      const where = await serve({ render: recorder().render });
      const answer = await call(where, "GET", "/api/v1/c/acme-rc/voice/catalogue", { token: ACME_TOKEN });

      assert.equal(answer.status, 200);
      assert.equal(answer.body.enabled, true);
      assert.deepEqual(
        answer.body.voices.map((voice: any) => voice.id),
        ["clubhouse", "rostrum"],
      );
      assert.match(answer.body.voices[0].rev, /^[0-9a-f]{12}$/);
      assert.ok(!answer.text.includes("el-voice-"), "the ElevenLabs voice id must never be served");
      assert.equal(answer.body.usage.allowance, 1_000);
      assert.equal(answer.body.usage.used, 0);
      assert.equal(answer.body.usage.resetsAt, "2026-10-01T00:00:00.000Z");
    });

    it("says enabled:false for a club whose plan does not include it", async () => {
      const where = await serve({ render: recorder().render });
      const answer = await call(where, "GET", "/api/v1/c/brookside/voice/catalogue", { token: BROOK_TOKEN });

      assert.equal(answer.status, 200);
      assert.equal(answer.body.enabled, false);
    });

    it("offers only the voices a club is given", async () => {
      const where = await serve({ render: recorder().render, acme: { enabled: true, voices: ["rostrum"] } });
      const answer = await call(where, "GET", "/api/v1/c/acme-rc/voice/catalogue", { token: ACME_TOKEN });

      assert.deepEqual(
        answer.body.voices.map((voice: any) => voice.id),
        ["rostrum"],
      );
    });

    it("says enabled:false, rather than failing, when the relay has no key", async () => {
      const where = await serve();
      const answer = await call(where, "GET", "/api/v1/c/acme-rc/voice/catalogue", { token: ACME_TOKEN });

      assert.equal(answer.status, 200);
      assert.equal(answer.body.enabled, false);
    });
  });

  describe("speaking a line", () => {
    it("renders, answers audio, and counts what it sent", async () => {
      const { render, asked } = recorder();
      const where = await serve({ render });

      const answer = await speak(where, { voice: "clubhouse", text: "Green flag!", shared: true });

      assert.equal(answer.status, 200);
      assert.equal(answer.headers.get("content-type"), "audio/mpeg");
      assert.equal(answer.headers.get("x-voice-cache"), "miss");
      assert.equal(answer.headers.get("x-voice-used"), "11");
      assert.equal(answer.headers.get("x-voice-allowance"), "1000");
      assert.equal(answer.bytes.toString("utf8"), "mp3-bytes:clubhouse:Green flag!");
      assert.deepEqual(asked, [{ voice: "el-voice-clubhouse", text: "Green flag!" }]);
    });

    it("serves the second ask from the cache, free", async () => {
      const { render, asked } = recorder();
      const where = await serve({ render });

      await speak(where, { voice: "clubhouse", text: "Green flag!", shared: true });
      const again = await speak(where, { voice: "clubhouse", text: "Green flag!", shared: true });

      assert.equal(again.status, 200);
      assert.equal(again.headers.get("x-voice-cache"), "hit");
      assert.equal(again.headers.get("x-voice-used"), "11", "a cache hit costs nothing");
      assert.equal(asked.length, 1, "ElevenLabs must be asked once");
    });

    it("shares a line with no name in it across clubs, and keeps a named line to its own club", async () => {
      const { render, asked } = recorder();
      const where = await serve({ render, brook: { enabled: true } });

      await speak(where, { voice: "clubhouse", text: "Green flag!", shared: true });
      const shared = await speak(where, { voice: "clubhouse", text: "Green flag!", shared: true }, BROOK_TOKEN, "brookside");

      assert.equal(shared.headers.get("x-voice-cache"), "hit");
      assert.equal(asked.length, 1);

      await speak(where, { voice: "clubhouse", text: "Sam Carter leads", shared: false });
      const named = await speak(
        where,
        { voice: "clubhouse", text: "Sam Carter leads", shared: false },
        BROOK_TOKEN,
        "brookside",
      );

      assert.equal(named.headers.get("x-voice-cache"), "miss", "a line with a name is never shared between clubs");
      assert.equal(asked.length, 3);
    });

    it("takes audio tags out for a model that would read them aloud, and leaves them for v3", async () => {
      const { render, asked } = recorder();
      const where = await serve({ render });

      await speak(where, { voice: "clubhouse", text: "[excited] Green flag!", shared: true });
      await speak(where, { voice: "rostrum", text: "[excited] Green flag!", shared: true });

      assert.equal(asked[0]?.text, "Green flag!");
      assert.equal(asked[1]?.text, "[excited] Green flag!");
    });

    it("refuses a voice the club was not given, without asking ElevenLabs", async () => {
      const { render, asked } = recorder();
      const where = await serve({ render, acme: { enabled: true, voices: ["clubhouse"] } });

      const answer = await speak(where, { voice: "rostrum", text: "Green flag!", shared: true });

      assert.equal(answer.status, 400);
      assert.equal(answer.json.refusal, "unknown-voice");
      assert.equal(asked.length, 0);
    });

    it("refuses a club whose plan does not include the voice", async () => {
      const { render, asked } = recorder();
      const where = await serve({ render });

      const answer = await speak(where, { voice: "clubhouse", text: "Green flag!", shared: true }, BROOK_TOKEN, "brookside");

      assert.equal(answer.status, 403);
      assert.equal(answer.json.refusal, "not-included");
      assert.equal(asked.length, 0);
    });

    it("refuses an empty line and one over the cap", async () => {
      const { render, asked } = recorder();
      const where = await serve({ render });

      assert.equal((await speak(where, { voice: "clubhouse", text: "   ", shared: true })).status, 400);
      assert.equal((await speak(where, { voice: "clubhouse", text: "x".repeat(121), shared: true })).status, 400);
      assert.equal(asked.length, 0);
    });

    it("refuses everything when the relay has no key", async () => {
      const where = await serve();
      const answer = await speak(where, { voice: "clubhouse", text: "Green flag!", shared: true });

      assert.equal(answer.status, 503);
      assert.equal(answer.json.refusal, "off");
    });

    it("needs the club's own token", async () => {
      const where = await serve({ render: recorder().render });

      const response = await fetch(`${where.base}/api/v1/c/acme-rc/voice/speak`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ voice: "clubhouse", text: "Green flag!" }),
      });

      assert.equal(response.status, 401);

      const wrong = await speak(where, { voice: "clubhouse", text: "Green flag!" }, BROOK_TOKEN);
      assert.equal(wrong.status, 403);
    });

    it("says so, and stops spending, once the allowance is gone", async () => {
      const { render, asked } = recorder();
      const where = await serve({ render, acme: { enabled: true, monthlyChars: 20 } });

      assert.equal((await speak(where, { voice: "clubhouse", text: "0123456789", shared: false })).status, 200);
      assert.equal((await speak(where, { voice: "clubhouse", text: "abcdefghij", shared: false })).status, 200);

      const over = await speak(where, { voice: "clubhouse", text: "one line too many", shared: false });

      assert.equal(over.status, 402);
      assert.equal(over.json.refusal, "allowance");
      assert.equal(over.json.used, 20);
      assert.equal(over.json.allowance, 20);
      assert.equal(over.json.resetsAt, "2026-10-01T00:00:00.000Z");
      assert.equal(asked.length, 2, "nothing is sent once the allowance is gone");
    });

    it("still serves a saved line when the allowance is gone", async () => {
      const { render } = recorder();
      const where = await serve({ render, acme: { enabled: true, monthlyChars: 11 } });

      assert.equal((await speak(where, { voice: "clubhouse", text: "Green flag!", shared: true })).status, 200);

      const again = await speak(where, { voice: "clubhouse", text: "Green flag!", shared: true });

      assert.equal(again.status, 200, "a cached line costs nothing, so it is still served");
      assert.equal(again.headers.get("x-voice-cache"), "hit");
    });

    it("starts the count again next month", async () => {
      const { render } = recorder();
      const where = await serve({ render, acme: { enabled: true, monthlyChars: 20 } });

      await speak(where, { voice: "clubhouse", text: "0123456789", shared: false });
      await speak(where, { voice: "clubhouse", text: "abcdefghij", shared: false });
      assert.equal((await speak(where, { voice: "clubhouse", text: "over", shared: false })).status, 402);

      clock = new Date("2026-10-01T00:00:01.000Z");

      const fresh = await speak(where, { voice: "clubhouse", text: "over", shared: false });

      assert.equal(fresh.status, 200);
      assert.equal(fresh.headers.get("x-voice-used"), "4");
      assert.equal(fresh.headers.get("x-voice-resets"), "2026-11-01T00:00:00.000Z");
    });

    it("keeps the month's count across a restart", async () => {
      const { render } = recorder();
      let where = await serve({ render, acme: { enabled: true, monthlyChars: 100 } });

      await speak(where, { voice: "clubhouse", text: "0123456789", shared: false });
      await harness?.close();
      harness = undefined;

      const kept = JSON.parse(await readFile(join(folder, ".voice", "usage", "acme-rc.json"), "utf8"));
      // With the months before this one, of which there are none yet - see
      // voiceadmin.test.ts.
      assert.deepEqual(kept, { month: "2026-09", used: 10, history: {} });

      where = await serve({ render, acme: { enabled: true, monthlyChars: 100 } });
      const answer = await speak(where, { voice: "clubhouse", text: "abcdefghij", shared: false });

      assert.equal(answer.headers.get("x-voice-used"), "20", "a deploy is not a fresh allowance");
    });

    it("answers a failure at ElevenLabs without spending the characters", async () => {
      const where = await serve({
        render: async () => {
          throw new Error("no answer");
        },
      });

      const answer = await speak(where, { voice: "clubhouse", text: "Green flag!", shared: true });

      assert.equal(answer.status, 502);
      assert.equal(answer.json.refusal, "upstream");

      const catalogue = await call(where, "GET", "/api/v1/c/acme-rc/voice/catalogue", { token: ACME_TOKEN });
      assert.equal(catalogue.body.usage.used, 0);
    });

    it("never lets an ElevenLabs message reach the club", async () => {
      const where = await serve({
        render: async () => {
          throw new Error("xi-api-key sk-secret-1234 is out of credit");
        },
      });

      const answer = await speak(where, { voice: "clubhouse", text: "Green flag!", shared: true });

      assert.ok(!answer.bytes.toString("utf8").includes("sk-secret"));
    });

    it("never lets two lines in flight at once spend past the allowance", async () => {
      let release: (() => void) | undefined;
      const held = new Promise<void>((resolve) => {
        release = resolve;
      });

      const { asked } = recorder();
      const where = await serve({
        acme: { enabled: true, monthlyChars: 20 },
        render: async (voice, text) => {
          asked.push({ voice: voice.voiceId, text });
          await held;
          return Buffer.from(text, "utf8");
        },
      });

      // Both are in flight before either has finished: the allowance has to
      // be taken when the line is accepted, not when it comes back.
      const first = speak(where, { voice: "clubhouse", text: "123456789012345", shared: false });
      await new Promise((resolve) => setTimeout(resolve, 30));
      const second = await speak(where, { voice: "clubhouse", text: "123456789012345", shared: false });

      assert.equal(second.status, 402);
      assert.equal(second.json.refusal, "allowance");

      release?.();
      assert.equal((await first).status, 200);
      assert.equal(asked.length, 1);
    });

    it("gives the characters back when the render fails", async () => {
      let fail = true;
      const where = await serve({
        acme: { enabled: true, monthlyChars: 30 },
        render: async (_voice, text) => {
          if (fail) {
            throw new Error("no answer");
          }

          return Buffer.from(text, "utf8");
        },
      });

      assert.equal((await speak(where, { voice: "clubhouse", text: "0123456789", shared: false })).status, 502);

      fail = false;

      const worked = await speak(where, { voice: "clubhouse", text: "0123456789", shared: false });

      assert.equal(worked.status, 200);
      assert.equal(worked.headers.get("x-voice-used"), "10", "the failed line gave its characters back");
    });

    it("answers busy rather than queueing for ever", async () => {
      let release: (() => void) | undefined;
      const held = new Promise<void>((resolve) => {
        release = resolve;
      });

      const where = await serve({
        maxInFlight: 1,
        maxWaiting: 0,
        render: async (_voice, text) => {
          await held;
          return Buffer.from(text, "utf8");
        },
      });

      const first = speak(where, { voice: "clubhouse", text: "one", shared: false });

      // Long enough for the first to have taken the only slot.
      await new Promise((resolve) => setTimeout(resolve, 30));

      const second = await speak(where, { voice: "clubhouse", text: "two", shared: false });

      assert.equal(second.status, 503);
      assert.equal(second.json.refusal, "busy");

      release?.();
      assert.equal((await first).status, 200);
    });
  });

  describe("the config", () => {
    it("reads a catalogue and gives every voice a default model", () => {
      const read = readVoiceConfig(
        { voices: [{ id: "clubhouse", name: "Clubhouse", voiceId: "el-1" }], monthlyChars: 500, maxChars: 200 },
        "test",
      );

      assert.equal(read.voices.length, 1);
      assert.equal(read.voices[0]?.modelId, "eleven_flash_v2_5");
      assert.equal(read.monthlyChars, 500);
      assert.equal(read.maxChars, 200);
    });

    it("refuses a voice with no id, a bad id, no ElevenLabs voice, or a repeated id", () => {
      assert.throws(() => readVoiceConfig({ voices: [{ name: "x", voiceId: "el-1" }] }, "test"), /id/);
      assert.throws(() => readVoiceConfig({ voices: [{ id: "Loud Voice", voiceId: "el-1" }] }, "test"), /id/);
      assert.throws(() => readVoiceConfig({ voices: [{ id: "clubhouse" }] }, "test"), /voiceId/);
      assert.throws(
        () =>
          readVoiceConfig(
            {
              voices: [
                { id: "clubhouse", voiceId: "el-1" },
                { id: "clubhouse", voiceId: "el-2" },
              ],
            },
            "test",
          ),
        /twice/,
      );
    });

    it("gives a club with no voice line no voice at all", () => {
      const plan = readVoicePlan(undefined, config(), "acme-rc", "test");

      assert.equal(plan.enabled, false);
    });

    it("refuses a club that names a voice the catalogue does not have", () => {
      assert.throws(() => readVoicePlan({ enabled: true, voices: ["nope"] }, config(), "acme-rc", "test"), /nope/);
    });

    it("takes the relay's monthly default when a club does not set one", () => {
      const plan = readVoicePlan({ enabled: true }, config({ monthlyChars: 4_242 }), "acme-rc", "test");

      assert.equal(plan.monthlyChars, 4_242);
      assert.equal(plan.enabled, true);
    });
  });

  describe("stripAudioTags", () => {
    it("takes tags out and tidies the spaces", () => {
      assert.equal(stripAudioTags("[excited] Green flag!"), "Green flag!");
      assert.equal(stripAudioTags("Go [shouting] now"), "Go now");
      assert.equal(stripAudioTags("[a][b]"), "");
    });
  });
});
