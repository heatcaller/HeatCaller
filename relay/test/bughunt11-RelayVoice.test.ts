import assert from "node:assert/strict";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { autoTopUpPath } from "../src/autotopup.js";
import { BrainService, type BrainConfig, type BrainPlan } from "../src/brain.js";
import { MemoryStore } from "../src/store.js";
import { TopUpRequests, requestsFor } from "../src/topuprequests.js";
import { VoiceService, type CatalogueVoice, type RenderFn, type VoicePlan } from "../src/voice.js";
import { ACME_TOKEN, call, start, tenants, type Harness } from "./helpers.js";

// Bug hunt 11 (RelayVoice), 2026-11-05.

const VOICE: CatalogueVoice = {
  id: "clubhouse",
  voiceId: "el-voice-clubhouse",
  name: "Clubhouse",
  what: "Warm and clear",
  modelId: "eleven_multilingual_v2",
  outputFormat: "mp3_44100_128",
  stability: 0.5,
  similarity: 0.75,
  style: 0,
  speakerBoost: true,
  speed: 1,
  cloned: false,
};

const BRAIN_CONFIG: BrainConfig = { model: "test-model", monthlyLines: 5, maxWords: 60 };
const SMALL: BrainPlan = { enabled: true, monthlyLines: 2 };
const ACME_VOICE: VoicePlan = { enabled: true, monthlyChars: 1_000, voices: [] };
const render: RenderFn = async (voice, text) => Buffer.from(`mp3:${voice.id}:${text}`, "utf8");

describe("bug hunt 11: an early ask for more no longer blocks the month's automatic top-up", () => {
  let folder = "";
  let clock = new Date("2026-10-20T12:00:00.000Z");
  let harness: Harness | undefined;

  beforeEach(async () => {
    folder = await mkdtemp(join(tmpdir(), "relay-bh11-"));
    clock = new Date("2026-10-20T12:00:00.000Z");
  });

  afterEach(async () => {
    await harness?.close();
    harness = undefined;
    await rm(folder, { recursive: true, force: true });
  });

  async function serve(brain: BrainService, perMonth = 2): Promise<void> {
    harness = await start(
      new MemoryStore(),
      tenants([{ brain: SMALL, voice: ACME_VOICE, autoTopUp: { commentary: { amount: 10, perMonth } } }]),
      () => clock,
      () => {},
      new VoiceService({ config: { voices: [VOICE], monthlyChars: 1_000, maxChars: 120 }, dataDir: folder, render, now: () => clock }),
      { brain, topUpRequests: new TopUpRequests({ dataDir: folder, now: () => clock }) },
    );
  }

  const ask = (note = "") =>
    call(harness!, "POST", "/api/v1/c/acme-rc/usage/topup-request", { token: ACME_TOKEN, json: { meter: "commentary", note } });

  async function spend(brain: BrainService, count: number): Promise<void> {
    for (let i = 0; i < count; i += 1) {
      assert.equal((await brain.write("acme-rc", SMALL, "sys", "facts", 40, 0.7)).ok, true);
    }
  }

  it("answers the still-open early ask once the club asks again nearly spent", async () => {
    const brain = new BrainService({ config: BRAIN_CONFIG, dataDir: folder, write: async () => "A line.", now: () => clock });
    await serve(brain);

    const early = await ask("asked early");
    assert.equal(early.body.request.status, "open", "below 90%: waits for the operator");
    assert.equal(early.body.autoTopUp, undefined);

    // Asked again while still below 90%: still waits.
    const again = await ask();
    assert.equal(again.body.already, true);
    assert.equal(again.body.autoTopUp, undefined);
    assert.equal(again.body.request.status, "open");

    await spend(brain, 2);
    clock = new Date("2026-10-24T20:00:00.000Z");

    const raceNight = await ask();
    assert.equal(raceNight.status, 200);
    assert.equal(raceNight.body.already, true);
    assert.equal(raceNight.body.request.id, early.body.request.id);
    assert.equal(raceNight.body.autoTopUp, 10, "the club's configured top-up is given at once");
    assert.equal(raceNight.body.request.status, "done");
    assert.equal((await brain.usage("acme-rc", SMALL)).allowance, 12);

    const asks = await requestsFor(folder, "acme-rc", "2026-10");
    assert.equal(asks.length, 1);
    assert.equal(asks[0]?.status, "done");
  });

  it("never gives twice for the same ask, and still keeps perMonth", async () => {
    const brain = new BrainService({ config: BRAIN_CONFIG, dataDir: folder, write: async () => "A line.", now: () => clock });
    await serve(brain, 1);

    await ask();
    await spend(brain, 2);
    assert.equal((await ask()).body.autoTopUp, 10);

    // Spend the new allowance and ask again: a new ask, but perMonth is 1.
    await spend(brain, 10);
    const later = await ask();
    assert.equal(later.body.autoTopUp, undefined);
    assert.equal(later.body.request.status, "open");
    assert.equal((await ask()).body.autoTopUp, undefined, "asked again, still none past perMonth");

    const given = JSON.parse(await readFile(autoTopUpPath(folder, "acme-rc"), "utf8")) as Record<string, unknown[]>;
    assert.equal(given["2026-10"]?.length, 1);
  });
});
