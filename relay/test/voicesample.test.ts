import assert from "node:assert/strict";
import { mkdtemp, readdir, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import {
  dayOf,
  dayResetOf,
  readVoiceConfig,
  VOICE_DEFAULT_PREVIEW_DAILY_CHARS,
  VOICE_DEFAULT_PREVIEW_MAX_CHARS,
  VOICE_SAMPLE_LINE,
  VoiceService,
  type CatalogueVoice,
  type RenderFn,
  type VoiceConfig,
} from "../src/voice.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, tenants, type Harness } from "./helpers.js";

// Hearing a HeatCaller voice before choosing it: the open samples anyone
// can fetch, and the Preview button's own render.
//
// What these tests are protecting is money that nobody is signed in to
// spend. A sample costs HeatCaller one render per voice for all time, so
// the one that matters most is "the second ask is free" - and a preview
// must never, under any circumstance, come out of the club's race-night
// allowance.

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
    cloned: false,
  },
  {
    id: "rostrum",
    name: "Rostrum",
    what: "Big race-day announcer",
    voiceId: "el-voice-rostrum",
    modelId: "eleven_flash_v2_5",
    stability: 0.4,
    similarity: 0.8,
    style: 0.6,
    speakerBoost: true,
    speed: 1,
    outputFormat: "mp3_44100_128",
    cloned: false,
  },
];

/** Small numbers, so a test can reach the end of a day of previews in four lines. */
function config(overrides: Partial<VoiceConfig> = {}): VoiceConfig {
  return {
    voices: CATALOGUE,
    monthlyChars: 1_000,
    maxChars: 120,
    previewDailyChars: 200,
    previewMaxChars: 60,
    ...overrides,
  };
}

interface Rendered {
  readonly voice: string;
  readonly text: string;
}

/** A renderer that records what it was asked for, so a test can prove nothing reached ElevenLabs twice. */
function recorder(): { render: RenderFn; asked: Rendered[] } {
  const asked: Rendered[] = [];

  return {
    asked,
    render: async (voice, text) => {
      asked.push({ voice: voice.voiceId, text });
      return Buffer.from(`mp3-bytes:${voice.id}:${text}`, "utf8");
    },
  };
}

interface Raw {
  readonly status: number;
  readonly headers: Headers;
  readonly bytes: Buffer;
  readonly json: any;
}

describe("hearing a HeatCaller voice before choosing it", () => {
  let folder = "";
  let harness: Harness | undefined;
  let clock = new Date("2026-09-20T12:00:00.000Z");

  beforeEach(async () => {
    folder = await mkdtemp(join(tmpdir(), "relay-voicesample-"));
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
      /** A relay that does not offer the HeatCaller voice at all - no catalogue, no service. */
      noService?: boolean;
    } = {},
  ): Promise<Harness> {
    const voiceConfig = options.voiceConfig ?? config();

    const service =
      options.noService === true
        ? undefined
        : new VoiceService({
            config: voiceConfig,
            dataDir: folder,
            render: options.render,
            now: () => clock,
          });

    const acme = options.acme ?? { enabled: true };

    harness = await start(
      new MemoryStore(),
      tenants([
        { voice: { enabled: acme.enabled, monthlyChars: acme.monthlyChars ?? 1_000, voices: acme.voices ?? [] } },
        { voice: { enabled: false, monthlyChars: 1_000, voices: [] } },
      ]),
      () => clock,
      () => {},
      service,
    );

    return harness;
  }

  function readRaw(response: Response, bytes: Buffer): Raw {
    let json: unknown = undefined;

    try {
      json = JSON.parse(bytes.toString("utf8"));
    } catch {
      json = undefined;
    }

    return { status: response.status, headers: response.headers, bytes, json };
  }

  /** The raw answer, because a sample is bytes and `call` turns everything into text. */
  async function get(where: Harness, path: string): Promise<Raw> {
    const response = await fetch(`${where.base}${path}`);

    return readRaw(response, Buffer.from(await response.arrayBuffer()));
  }

  /**
   * The Preview button's request. `token: null` sends no Authorization
   * header at all - null rather than undefined, because undefined is what
   * a default parameter is for and passing it would quietly send the
   * token, which is exactly the thing the 401 test is checking is absent.
   */
  async function preview(where: Harness, body: unknown, token: string | null = ACME_TOKEN): Promise<Raw> {
    const response = await fetch(`${where.base}/voice/preview`, {
      method: "POST",
      headers: {
        "content-type": "application/json",
        ...(token === null ? {} : { authorization: `Bearer ${token}` }),
      },
      body: JSON.stringify(body),
    });

    return readRaw(response, Buffer.from(await response.arrayBuffer()));
  }

  describe("the sample list", () => {
    it("lists every voice with a sample, takes no token, and never names the ElevenLabs voice", async () => {
      const where = await serve({ render: recorder().render });
      const answer = await call(where, "GET", "/voice/samples");

      assert.equal(answer.status, 200);
      assert.deepEqual(answer.body, {
        samples: [
          { id: "clubhouse", name: "Clubhouse", url: "/voice/sample/clubhouse" },
          { id: "rostrum", name: "Rostrum", url: "/voice/sample/rostrum" },
        ],
      });
      assert.ok(!answer.text.includes("el-voice-"), "the ElevenLabs voice id must never be served");
    });

    it("lists nothing, rather than failing, when this relay has no voice at all", async () => {
      const where = await serve({ noService: true });
      const answer = await call(where, "GET", "/voice/samples");

      assert.equal(answer.status, 200);
      assert.deepEqual(answer.body, { samples: [] });
    });

    it("still lists a sample already on the volume after the key has gone", async () => {
      const first = await serve({ render: recorder().render });

      assert.equal((await get(first, "/voice/sample/clubhouse")).status, 200);

      await harness?.close();
      harness = undefined;

      // The same data folder, no renderer: the key is gone, the sample is
      // not, and audio already paid for is still worth hearing.
      const after = await serve();
      const answer = await call(after, "GET", "/voice/samples");

      assert.deepEqual(
        answer.body.samples.map((sample: any) => sample.id),
        ["clubhouse"],
      );
    });
  });

  describe("one voice's sample", () => {
    it("renders the one fixed line once, and serves every ask after that free", async () => {
      const { render, asked } = recorder();
      const where = await serve({ render });

      const first = await get(where, "/voice/sample/clubhouse");

      assert.equal(first.status, 200);
      assert.equal(first.headers.get("content-type"), "audio/mpeg");
      assert.equal(first.headers.get("x-voice-cache"), "miss");
      assert.equal(first.bytes.toString("utf8"), `mp3-bytes:clubhouse:${VOICE_SAMPLE_LINE}`);
      assert.deepEqual(asked, [{ voice: "el-voice-clubhouse", text: VOICE_SAMPLE_LINE }]);

      const again = await get(where, "/voice/sample/clubhouse");

      assert.equal(again.status, 200);
      assert.equal(again.headers.get("x-voice-cache"), "hit");
      assert.equal(again.bytes.toString("utf8"), first.bytes.toString("utf8"));
      assert.equal(asked.length, 1, "a sample is rendered once, ever - that is what makes these routes safe to open");
    });

    it("says it may be cached for a long time, and lets another page play it", async () => {
      const where = await serve({ render: recorder().render });
      const answer = await get(where, "/voice/sample/clubhouse");

      assert.match(answer.headers.get("cache-control") ?? "", /^public, max-age=\d{7,}$/);
      assert.equal(answer.headers.get("access-control-allow-origin"), "*");
    });

    it("keeps it on the volume, so a deploy does not pay for it again", async () => {
      const { render, asked } = recorder();
      let where = await serve({ render });

      assert.equal((await get(where, "/voice/sample/clubhouse")).status, 200);

      const kept = await readdir(join(folder, ".voice", "samples"));

      assert.equal(kept.length, 1);
      assert.ok(kept[0]?.startsWith("clubhouse-"), `unexpected sample file: ${String(kept[0])}`);
      assert.ok(kept[0]?.endsWith(".mp3"), `unexpected sample file: ${String(kept[0])}`);

      await harness?.close();
      harness = undefined;

      where = await serve({ render });
      const after = await get(where, "/voice/sample/clubhouse");

      assert.equal(after.status, 200);
      assert.equal(after.headers.get("x-voice-cache"), "hit");
      assert.equal(after.bytes.toString("utf8"), `mp3-bytes:clubhouse:${VOICE_SAMPLE_LINE}`);
      assert.equal(asked.length, 1, "a deploy is not a fresh bill for the samples");
    });

    it("answers 404 and a reason for a voice there is no sample of", async () => {
      const { render, asked } = recorder();
      const where = await serve({ render });

      const answer = await get(where, "/voice/sample/nope");

      assert.equal(answer.status, 404);
      assert.equal(answer.headers.get("content-type"), "application/json; charset=utf-8");
      assert.equal(answer.json.ok, false);
      assert.ok(typeof answer.json.error === "string" && answer.json.error.length > 0);
      assert.equal(asked.length, 0, "an unknown voice must not reach ElevenLabs");
    });

    it("answers 404 when this relay has no voice at all", async () => {
      const where = await serve({ noService: true });
      const answer = await get(where, "/voice/sample/clubhouse");

      assert.equal(answer.status, 404);
      assert.equal(answer.json.ok, false);
    });

    it("renders once however many ask for the same voice at the same moment", async () => {
      let release: (() => void) | undefined;
      const held = new Promise<void>((resolve) => {
        release = resolve;
      });

      const asked: string[] = [];
      const where = await serve({
        render: async (voice, text) => {
          asked.push(voice.voiceId);
          await held;
          return Buffer.from(`mp3-bytes:${voice.id}:${text}`, "utf8");
        },
      });

      const all = [
        get(where, "/voice/sample/clubhouse"),
        get(where, "/voice/sample/clubhouse"),
        get(where, "/voice/sample/clubhouse"),
      ];

      // Long enough for all three to have reached the service and found
      // the same render already under way.
      await new Promise((resolve) => setTimeout(resolve, 30));
      release?.();

      for (const answer of await Promise.all(all)) {
        assert.equal(answer.status, 200);
        assert.equal(answer.bytes.toString("utf8"), `mp3-bytes:clubhouse:${VOICE_SAMPLE_LINE}`);
      }

      assert.equal(asked.length, 1, "two first visitors must not both pay for the same sample");
    });

    it("does not ask again straight away when ElevenLabs refuses, and does once the minute is up", async () => {
      let fail = true;
      const asked: string[] = [];

      const where = await serve({
        render: async (voice, text) => {
          asked.push(voice.voiceId);

          if (fail) {
            throw new Error("no answer");
          }

          return Buffer.from(`mp3-bytes:${voice.id}:${text}`, "utf8");
        },
      });

      assert.equal((await get(where, "/voice/sample/clubhouse")).status, 503);
      assert.equal((await get(where, "/voice/sample/clubhouse")).status, 503);
      assert.equal(asked.length, 1, "ElevenLabs being down must not be asked once per visitor");

      fail = false;
      clock = new Date("2026-09-20T12:01:30.000Z");

      const worked = await get(where, "/voice/sample/clubhouse");

      assert.equal(worked.status, 200);
      assert.equal(worked.bytes.toString("utf8"), `mp3-bytes:clubhouse:${VOICE_SAMPLE_LINE}`);
      assert.equal(asked.length, 2, "and a render that gave up must not be left in the way of the next one");
    });

    it("says what each voice costs nothing twice over: a sample is not a club's allowance", async () => {
      const { render } = recorder();
      const where = await serve({ render });

      assert.equal((await get(where, "/voice/sample/clubhouse")).status, 200);
      assert.equal((await get(where, "/voice/sample/rostrum")).status, 200);

      const catalogue = await call(where, "GET", "/api/v1/c/acme-rc/voice/catalogue", { token: ACME_TOKEN });

      assert.equal(catalogue.body.usage.used, 0, "nobody was signed in, so no club pays");
    });

    it("walls off one address that will not stop, across both open routes", async () => {
      const where = await serve({ render: recorder().render });

      // The first renders; the other 59 are served from memory. Sixty is
      // the minute's allowance for the address, whichever of the two open
      // routes it spends it on.
      for (let asked = 0; asked < 60; asked++) {
        assert.equal((await get(where, "/voice/sample/clubhouse")).status, 200, `ask ${asked + 1} should be served`);
      }

      const over = await get(where, "/voice/sample/clubhouse");

      assert.equal(over.status, 429);
      assert.equal(over.json.ok, false);

      const list = await call(where, "GET", "/voice/samples");

      assert.equal(list.status, 429, "the two open routes share one budget per address");
    });
  });

  describe("the Preview button", () => {
    it("speaks the club's own words and charges the day, not the month", async () => {
      const { render, asked } = recorder();
      const where = await serve({ render });

      const answer = await preview(where, { voiceId: "clubhouse", text: "Testing the Clubhouse voice." });

      assert.equal(answer.status, 200);
      assert.equal(answer.headers.get("content-type"), "audio/mpeg");
      assert.equal(answer.headers.get("x-voice-cache"), "miss");
      assert.equal(answer.headers.get("x-voice-preview-used"), "28");
      assert.equal(answer.headers.get("x-voice-preview-allowance"), "200");
      assert.equal(answer.headers.get("x-voice-preview-resets"), "2026-09-21T00:00:00.000Z");
      assert.equal(answer.bytes.toString("utf8"), "mp3-bytes:clubhouse:Testing the Clubhouse voice.");
      assert.deepEqual(asked, [{ voice: "el-voice-clubhouse", text: "Testing the Clubhouse voice." }]);

      const catalogue = await call(where, "GET", "/api/v1/c/acme-rc/voice/catalogue", { token: ACME_TOKEN });

      assert.equal(catalogue.body.usage.used, 0, "an audition must never cost a club its race night");
      assert.equal(catalogue.body.usage.allowance, 1_000);
    });

    it("takes nobody without a token", async () => {
      const { render, asked } = recorder();
      const where = await serve({ render });

      assert.equal((await preview(where, { voiceId: "clubhouse", text: "Hello there." }, null)).status, 401);
      assert.equal((await preview(where, { voiceId: "clubhouse", text: "Hello there." }, "not-a-token")).status, 401);
      assert.equal(asked.length, 0, "an unauthenticated caller must not be able to spend anything");
    });

    it("refuses a voice the club was not given, and a club with no voice at all", async () => {
      const { render, asked } = recorder();
      const where = await serve({ render, acme: { enabled: true, voices: ["clubhouse"] } });

      const notGiven = await preview(where, { voiceId: "rostrum", text: "Hello there." });

      assert.equal(notGiven.status, 400);
      assert.equal(notGiven.json.refusal, "unknown-voice");

      // Brookside's token is a real token - its club simply has no voice.
      const notIncluded = await preview(where, { voiceId: "clubhouse", text: "Hello there." }, BROOK_TOKEN);

      assert.equal(notIncluded.status, 403);
      assert.equal(notIncluded.json.refusal, "not-included");
      assert.equal(asked.length, 0);
    });

    it("caps the text hard - a preview is a sentence, not a recap", async () => {
      const { render, asked } = recorder();
      const where = await serve({ render });

      const long = await preview(where, { voiceId: "clubhouse", text: "x".repeat(61) });

      assert.equal(long.status, 400);
      assert.equal(long.json.refusal, "text");
      assert.match(long.json.error, /60/);

      const empty = await preview(where, { voiceId: "clubhouse", text: "   " });

      assert.equal(empty.status, 400);
      assert.equal(empty.json.refusal, "text");
      assert.equal(asked.length, 0, "nothing over the cap reaches ElevenLabs");
    });

    it("never lets the preview cap speak past what the voice speaks at all", async () => {
      const { render, asked } = recorder();
      // maxChars below previewMaxChars: the shorter of the two has to win.
      const where = await serve({ render, voiceConfig: config({ maxChars: 40, previewMaxChars: 200 }) });

      const answer = await preview(where, { voiceId: "clubhouse", text: "y".repeat(41) });

      assert.equal(answer.status, 400);
      assert.match(answer.json.error, /40/);
      assert.equal(asked.length, 0);
    });

    it("says so in words once the day's previews are gone, and starts again tomorrow", async () => {
      const { render, asked } = recorder();
      const where = await serve({ render });

      // Two hundred characters a day: four lines of fifty, and no more.
      // Each line is different, so none of them is served from the cache.
      for (const line of ["a", "b", "c", "d"]) {
        const answer = await preview(where, { voiceId: "clubhouse", text: line.repeat(50) });

        assert.equal(answer.status, 200, `the ${line} line should fit the day's allowance`);
      }

      const over = await preview(where, { voiceId: "clubhouse", text: "e".repeat(50) });

      assert.equal(over.status, 429);
      assert.equal(over.json.refusal, "allowance");
      assert.equal(over.json.used, 200);
      assert.equal(over.json.allowance, 200);
      assert.equal(over.json.resetsAt, "2026-09-21T00:00:00.000Z");
      assert.match(over.json.error, /previews/i);
      assert.match(over.json.error, /tomorrow/i);
      assert.equal(asked.length, 4, "nothing is sent once the day's previews are gone");

      clock = new Date("2026-09-21T09:00:00.000Z");

      const fresh = await preview(where, { voiceId: "clubhouse", text: "e".repeat(50) });

      assert.equal(fresh.status, 200);
      assert.equal(fresh.headers.get("x-voice-preview-used"), "50");
      assert.equal(fresh.headers.get("x-voice-preview-resets"), "2026-09-22T00:00:00.000Z");

      const catalogue = await call(where, "GET", "/api/v1/c/acme-rc/voice/catalogue", { token: ACME_TOKEN });

      assert.equal(catalogue.body.usage.used, 0, "a whole day of auditioning cost the month nothing");
    });

    it("serves the same preview again free", async () => {
      const { render, asked } = recorder();
      const where = await serve({ render });

      assert.equal((await preview(where, { voiceId: "clubhouse", text: "Hello there." })).status, 200);

      const again = await preview(where, { voiceId: "clubhouse", text: "Hello there." });

      assert.equal(again.status, 200);
      assert.equal(again.headers.get("x-voice-cache"), "hit");
      assert.equal(again.headers.get("x-voice-preview-used"), "12", "a preview heard twice is paid for once");
      assert.equal(asked.length, 1);
    });

    it("gives the day's characters back when ElevenLabs fails", async () => {
      let fail = true;
      const where = await serve({
        render: async (_voice, text) => {
          if (fail) {
            throw new Error("no answer");
          }

          return Buffer.from(text, "utf8");
        },
      });

      const failed = await preview(where, { voiceId: "clubhouse", text: "Hello there." });

      assert.equal(failed.status, 502);
      assert.equal(failed.json.refusal, "upstream");

      fail = false;

      const worked = await preview(where, { voiceId: "clubhouse", text: "Hello there." });

      assert.equal(worked.status, 200);
      assert.equal(worked.headers.get("x-voice-preview-used"), "12", "the failed preview gave its characters back");
    });

    it("refuses everything when this relay has no key", async () => {
      const where = await serve();
      const answer = await preview(where, { voiceId: "clubhouse", text: "Hello there." });

      assert.equal(answer.status, 503);
      assert.equal(answer.json.refusal, "off");
    });

    it("keeps the day's count across a restart", async () => {
      const { render } = recorder();
      let where = await serve({ render });

      assert.equal((await preview(where, { voiceId: "clubhouse", text: "Hello there." })).status, 200);

      await harness?.close();
      harness = undefined;

      assert.deepEqual(JSON.parse(await readFile(join(folder, ".voice", "preview", "acme-rc.json"), "utf8")), {
        day: "2026-09-20",
        used: 12,
      });

      where = await serve({ render });
      const answer = await preview(where, { voiceId: "clubhouse", text: "Different words here." });

      assert.equal(answer.status, 200);
      assert.equal(answer.headers.get("x-voice-preview-used"), "33", "a deploy is not a fresh day of previews");
    });

    it("can be switched off entirely without taking the samples with it", async () => {
      const { render, asked } = recorder();
      const where = await serve({ render, voiceConfig: config({ previewDailyChars: 0 }) });

      const answer = await preview(where, { voiceId: "clubhouse", text: "Hello there." });

      assert.equal(answer.status, 429);
      assert.equal(answer.json.refusal, "allowance");
      assert.equal(answer.json.allowance, 0);
      assert.equal(asked.length, 0);

      assert.equal(
        (await get(where, "/voice/sample/clubhouse")).status,
        200,
        "the samples are not charged to a club, so switching previews off leaves them alone",
      );
    });

    it("is not reachable by GET, and the sample routes not by POST", async () => {
      const where = await serve({ render: recorder().render });

      assert.equal((await call(where, "GET", "/voice/preview", { token: ACME_TOKEN })).status, 404);
      assert.equal((await call(where, "POST", "/voice/samples", { token: ACME_TOKEN, json: {} })).status, 404);
    });
  });

  describe("the config", () => {
    it("has a small daily allowance and a hard cap by default", () => {
      const read = readVoiceConfig({ voices: [{ id: "clubhouse", voiceId: "el-1" }] }, "test");

      assert.equal(read.previewDailyChars, VOICE_DEFAULT_PREVIEW_DAILY_CHARS);
      assert.equal(read.previewMaxChars, VOICE_DEFAULT_PREVIEW_MAX_CHARS);
      assert.ok(
        VOICE_DEFAULT_PREVIEW_DAILY_CHARS < read.monthlyChars,
        "a day of auditioning must be a small fraction of a month of racing",
      );
    });

    it("reads both, and refuses a number that is not one", () => {
      const read = readVoiceConfig({ voices: [], previewDailyChars: 500, previewMaxChars: 100 }, "test");

      assert.equal(read.previewDailyChars, 500);
      assert.equal(read.previewMaxChars, 100);

      assert.throws(() => readVoiceConfig({ voices: [], previewMaxChars: 5 }, "test"), /previewMaxChars/);
      assert.throws(() => readVoiceConfig({ voices: [], previewDailyChars: -1 }, "test"), /previewDailyChars/);
      assert.throws(() => readVoiceConfig({ voices: [], previewDailyChars: "lots" }, "test"), /previewDailyChars/);
    });

    it("gives the voice with no config block the same defaults", () => {
      const read = readVoiceConfig(undefined, "test");

      assert.equal(read.previewDailyChars, VOICE_DEFAULT_PREVIEW_DAILY_CHARS);
      assert.equal(read.previewMaxChars, VOICE_DEFAULT_PREVIEW_MAX_CHARS);
    });
  });

  describe("the day a preview is charged to", () => {
    it("is the UTC day, and comes back at the first moment of tomorrow", () => {
      assert.equal(dayOf(new Date("2026-09-20T23:59:59.000Z")), "2026-09-20");
      assert.equal(dayResetOf(new Date("2026-09-20T23:59:59.000Z")), "2026-09-21T00:00:00.000Z");
    });

    it("rolls over the end of a month and the end of a year", () => {
      assert.equal(dayResetOf(new Date("2026-09-30T12:00:00.000Z")), "2026-10-01T00:00:00.000Z");
      assert.equal(dayResetOf(new Date("2026-12-31T12:00:00.000Z")), "2027-01-01T00:00:00.000Z");
      assert.equal(dayOf(new Date("2027-01-01T00:00:00.000Z")), "2027-01-01");
    });
  });
});
