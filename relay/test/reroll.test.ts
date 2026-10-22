import assert from "node:assert/strict";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { VoiceService, type CatalogueVoice, type RenderFn, type VoicePlan } from "../src/voice.js";

/**
 * A FRESH TAKE (22 Oct 2026, known issues). The Re-roll button on Voice
 * Settings asks for a line again past every cache. It is charged, it is
 * kept in the club's own cache even for a shared line, and that club then
 * hears its new take while every other club keeps the shared one.
 */

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

const PLAN: VoicePlan = { enabled: true, monthlyChars: 1000, voices: [] };

describe("re-rolling a take", () => {
  let folder = "";
  let takes = 0;
  const clock = new Date("2026-10-22T12:00:00.000Z");
  const render: RenderFn = async (voice, text) => {
    takes += 1;
    return Buffer.from(`mp3:${voice.id}:${text}:take${takes}`, "utf8");
  };

  beforeEach(async () => {
    folder = await mkdtemp(join(tmpdir(), "relay-reroll-"));
    takes = 0;
  });

  afterEach(async () => {
    await rm(folder, { recursive: true, force: true });
  });

  function service(): VoiceService {
    return new VoiceService({
      config: { voices: [VOICE], monthlyChars: 1000, maxChars: 120 },
      dataDir: folder,
      render,
      now: () => clock,
    });
  }

  it("renders a saved line again, charged, and the club hears the new take after", async () => {
    const voice = service();
    const first = await voice.speak("acme-rc", PLAN, "clubhouse", "Green flag!", true);
    assert.equal(first.ok, true);

    const fresh = await voice.speak("acme-rc", PLAN, "clubhouse", "Green flag!", true, true);
    assert.equal(fresh.ok, true);
    if (!fresh.ok) return;
    assert.equal(fresh.cached, false);
    assert.equal(fresh.audio.toString("utf8").endsWith("take2"), true);
    assert.equal(fresh.usage.used, "Green flag!".length * 2);

    const after = await voice.speak("acme-rc", PLAN, "clubhouse", "Green flag!", true);
    assert.equal(after.ok, true);
    if (!after.ok) return;
    assert.equal(after.cached, true);
    assert.equal(after.audio.toString("utf8").endsWith("take2"), true);
  });

  it("leaves every other club the shared take", async () => {
    const voice = service();
    await voice.speak("acme-rc", PLAN, "clubhouse", "Green flag!", true);
    await voice.speak("acme-rc", PLAN, "clubhouse", "Green flag!", true, true);

    const other = await voice.speak("beta-rc", PLAN, "clubhouse", "Green flag!", true);
    assert.equal(other.ok, true);
    if (!other.ok) return;
    assert.equal(other.cached, true);
    assert.equal(other.audio.toString("utf8").endsWith("take1"), true);
  });
});
