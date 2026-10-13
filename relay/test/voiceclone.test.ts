import assert from "node:assert/strict";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import { readVoiceConfig, VoiceService, type CatalogueVoice, type RenderFn, type VoiceConfig } from "../src/voice.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, tenants, type Harness } from "./helpers.js";

/**
 * A CLUB'S OWN ANNOUNCER, CLONED (12 Oct 2026) - see
 * CatalogueVoice.cloned.
 *
 * WHAT THIS FILE IS ABOUT
 *
 * A cloned voice is a named person's likeness. The club holds the
 * recording and the consent; this relay holds a voice id. Two things that
 * are exactly right for a catalogue of stock voices are leaks the moment
 * one of those voices is somebody:
 *
 *   the shared cache, which keeps one render of "Green flag!" for EVERY
 *   club because the words are the same everywhere - and the words being
 *   the same is not the voice being the same;
 *
 *   the sample route, which needs no token at all and is open to the
 *   internet on purpose, because a stock sample is one neutral line in a
 *   voice nobody owns.
 *
 * Both were written before clones existed and both default to the stock
 * behaviour, so this file is the proof that a clone turns them off.
 *
 * The test worth reading first is
 * "a cloned voice is never kept in the cache every club reads".
 */

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

/** Acme's own announcer, who recorded half an hour once. */
const CLONE: CatalogueVoice = {
  ...STOCK,
  id: "acme-dave",
  name: "Dave",
  what: "The club's own announcer",
  voiceId: "el-voice-acme-dave",
  cloned: true,
};

function config(voices: readonly CatalogueVoice[]): VoiceConfig {
  return { voices, monthlyChars: 10_000, maxChars: 120, previewDailyChars: 200, previewMaxChars: 60 };
}

/** Records what reached ElevenLabs, so a test can prove a render did or did not happen again. */
function recorder(): { render: RenderFn; asked: string[] } {
  const asked: string[] = [];

  return {
    asked,
    render: async (voice, text) => {
      asked.push(`${voice.id}:${text}`);
      return Buffer.from(`mp3:${voice.id}:${text}`, "utf8");
    },
  };
}

describe("a club's own announcer, cloned", () => {
  let folder = "";
  let harness: Harness | undefined;
  const clock = new Date("2026-10-12T12:00:00.000Z");

  beforeEach(async () => {
    folder = await mkdtemp(join(tmpdir(), "relay-voiceclone-"));
  });

  afterEach(async () => {
    await harness?.close();
    harness = undefined;
    await rm(folder, { recursive: true, force: true });
  });

  /**
   * Two clubs, each with the voice list given - empty, which is every
   * stock voice, unless a test names one. A clone is only ever used by a
   * club whose list names it.
   */
  async function serve(
    voices: readonly CatalogueVoice[],
    render?: RenderFn,
    lists: readonly [readonly string[], readonly string[]] = [[], []],
  ): Promise<Harness> {
    const service = new VoiceService({
      config: config(voices),
      dataDir: folder,
      render,
      now: () => clock,
    });

    harness = await start(
      new MemoryStore(),
      tenants([
        { voice: { enabled: true, monthlyChars: 10_000, voices: lists[0] } },
        { voice: { enabled: true, monthlyChars: 10_000, voices: lists[1] } },
      ]),
      () => clock,
      () => {},
      service,
    );

    return harness;
  }

  // ----------------------------------------------------------------
  //  The shared cache
  // ----------------------------------------------------------------

  /** THE ONE. See the file's own summary. */
  it("a cloned voice is never kept in the cache every club reads", async () => {
    const { render, asked } = recorder();

    // Both clubs have it on their list, so the only thing that could keep
    // Brook from Acme's render is the cache itself.
    const live = await serve([CLONE], render, [["acme-dave"], ["acme-dave"]]);

    // Acme speaks a line its laptop says is shared - "Green flag!" has no
    // name in it and is the same words at every club.
    const first = await call(live, "POST", "/api/v1/c/acme-rc/voice/speak", {
      token: ACME_TOKEN,
      json: { voice: "acme-dave", text: "Green flag!", shared: true },
    });

    assert.equal(first.status, 200);
    assert.deepEqual(asked, ["acme-dave:Green flag!"]);

    // Brook asks for the identical words in the identical voice. If the
    // render had gone into the shared cache, Brook would be handed Acme's
    // announcer saying it - for free, which is how nobody would notice.
    const second = await call(live, "POST", "/api/v1/c/brookside/voice/speak", {
      token: BROOK_TOKEN,
      json: { voice: "acme-dave", text: "Green flag!", shared: true },
    });

    assert.equal(second.status, 200);

    // It was rendered AGAIN, for Brook, rather than served out of a cache
    // the two clubs share.
    assert.deepEqual(asked, ["acme-dave:Green flag!", "acme-dave:Green flag!"]);
  });

  it("a stock voice is still kept for every club, which is the saving this protects", async () => {
    const { render, asked } = recorder();
    const live = await serve([STOCK], render);

    await call(live, "POST", "/api/v1/c/acme-rc/voice/speak", {
      token: ACME_TOKEN,
      json: { voice: "clubhouse", text: "Green flag!", shared: true },
    });

    const second = await call(live, "POST", "/api/v1/c/brookside/voice/speak", {
      token: BROOK_TOKEN,
      json: { voice: "clubhouse", text: "Green flag!", shared: true },
    });

    assert.equal(second.status, 200);

    // Rendered once for both. This is the behaviour a clone gives up, and
    // the club whose voice it is pays for giving it up.
    assert.deepEqual(asked, ["clubhouse:Green flag!"]);
  });

  it("a club's own cache still works for a cloned voice", async () => {
    const { render, asked } = recorder();
    const live = await serve([CLONE], render, [["acme-dave"], []]);

    for (let at = 0; at < 2; at++) {
      const said = await call(live, "POST", "/api/v1/c/acme-rc/voice/speak", {
        token: ACME_TOKEN,
        json: { voice: "acme-dave", text: "Green flag!", shared: true },
      });

      assert.equal(said.status, 200);
    }

    // Only the cross-club saving is lost - the same club asking twice is
    // still free.
    assert.deepEqual(asked, ["acme-dave:Green flag!"]);
  });

  // ----------------------------------------------------------------
  //  The public sample
  // ----------------------------------------------------------------

  it("a cloned voice is not in the list anybody can fetch", async () => {
    const { render } = recorder();
    const live = await serve([STOCK, CLONE], render);

    // No token at all, which is what this route is for.
    const listed = await call(live, "GET", "/voice/samples", {});

    assert.equal(listed.status, 200);

    const ids = (listed.body.samples as { id: string }[]).map((one) => one.id);

    assert.deepEqual(ids, ["clubhouse"]);
  });

  it("a cloned voice's sample is refused the way a voice that does not exist is", async () => {
    const { render } = recorder();
    const live = await serve([STOCK, CLONE], render);

    const clone = await call(live, "GET", "/voice/sample/acme-dave", {});
    const nobody = await call(live, "GET", "/voice/sample/no-such-voice", {});

    // THE SAME ANSWER, and that is the point: a refusal worded
    // differently from a miss would tell anybody who asked which of this
    // relay's voices are clones of somebody.
    assert.equal(clone.status, 404);
    assert.equal(nobody.status, 404);
    assert.equal(clone.body.error, nobody.body.error);
  });

  it("a stock voice's sample is still served to anybody", async () => {
    const { render } = recorder();
    const live = await serve([STOCK, CLONE], render);

    const said = await call(live, "GET", "/voice/sample/clubhouse", {});

    assert.equal(said.status, 200);
    assert.equal(said.headers["content-type"], "audio/mpeg");
  });

  // ----------------------------------------------------------------
  //  What the club itself is told
  // ----------------------------------------------------------------

  it("the club whose voice it is can see which of its voices is a clone", async () => {
    const { render } = recorder();
    const live = await serve([STOCK, CLONE], render, [["clubhouse", "acme-dave"], []]);

    const view = await call(live, "GET", "/api/v1/c/acme-rc/voice/catalogue", { token: ACME_TOKEN });

    assert.equal(view.status, 200);

    const voices = view.body.voices as { id: string; cloned: boolean }[];

    assert.equal(voices.find((one) => one.id === "clubhouse")?.cloned, false);
    assert.equal(voices.find((one) => one.id === "acme-dave")?.cloned, true);
  });

  // ----------------------------------------------------------------
  //  The config
  // ----------------------------------------------------------------

  it("a catalogue written before clones existed is unchanged", () => {
    const read = readVoiceConfig(
      { voices: [{ id: "clubhouse", name: "Clubhouse", voiceId: "el-1" }] },
      "the test",
    );

    assert.equal(read.voices[0]?.cloned, false);
  });

  it("a voice is a clone only when the catalogue says so in as many words", () => {
    const read = readVoiceConfig(
      {
        voices: [
          { id: "one", voiceId: "el-1", cloned: true },
          { id: "two", voiceId: "el-2", cloned: false },

          // Anything that is not the boolean true is not a clone. An
          // opt-in that a truthy string could switch on is an opt-in
          // somebody turns on by accident.
          { id: "three", voiceId: "el-3", cloned: "yes" },
          { id: "four", voiceId: "el-4", cloned: 1 },
        ],
      },
      "the test",
    );

    assert.deepEqual(
      read.voices.map((one) => one.cloned),
      [true, false, false, false],
    );
  });
});
