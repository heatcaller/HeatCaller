import assert from "node:assert/strict";
import { mkdir, mkdtemp, rm, unlink } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { VoiceService, voiceUsagePath, type CatalogueVoice, type RenderFn, type VoicePlan } from "../src/voice.js";

/**
 * BUG HUNT 8, voice.ts (20 Oct 2026), n101.
 *
 * A usage file that is there but cannot be read used to come back as
 * "0 used" - in the catalogue and in a spoken line's busy refusal - and the
 * app took it as the club's real count. Now the refusal carries no figures
 * and the catalogue says known:false with no allowance (0 of 0): not room,
 * so the app's spent state holds, and not "used up", which would set off
 * the app's used-up email and notice for a club that still has room.
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

const PLAN: VoicePlan = { enabled: true, monthlyChars: 100, voices: [] };
const render: RenderFn = async (voice, text) => Buffer.from(`mp3:${voice.id}:${text}`, "utf8");

describe("bug hunt 8 - voice.ts", () => {
  let folder = "";
  const clock = new Date("2026-09-20T12:00:00.000Z");

  beforeEach(async () => {
    folder = await mkdtemp(join(tmpdir(), "relay-bh8-voice-"));
  });

  afterEach(async () => {
    await rm(folder, { recursive: true, force: true });
  });

  function service(): VoiceService {
    return new VoiceService({
      config: { voices: [VOICE], monthlyChars: 100, maxChars: 120 },
      dataDir: folder,
      render,
      now: () => clock,
    });
  }

  /** A folder where the usage file should be: reading it fails with EISDIR, as an unreadable file would. */
  async function breakUsageFile(): Promise<void> {
    await mkdir(voiceUsagePath(folder, "acme-rc"), { recursive: true });
  }

  it("refuses a line as busy with no usage figures when the usage file can't be read", async () => {
    await breakUsageFile();
    const result = await service().speak("acme-rc", PLAN, "clubhouse", "Heat two to the rostrum.", true);

    assert.equal(result.ok, false);
    if (result.ok) return;
    assert.equal(result.refusal, "busy");
    assert.equal(result.usage, undefined);
  });

  it("marks the catalogue's figure unknown, with no allowance - neither room nor used up", async () => {
    await breakUsageFile();
    const view = await service().catalogue("acme-rc", PLAN);

    assert.equal(view.usage.known, false);
    assert.equal(view.usage.used, 0);
    assert.equal(view.usage.allowance, 0);
    assert.equal(view.usage.resetsAt, "2026-10-01T00:00:00.000Z");
    assert.equal(view.enabled, true, "Voice Settings still opens with the club's voices");
    assert.equal(view.voices.length, 1);
  });

  it("gives a line the relay had saved the unknown 0 of 0, not 0 of the plan", async () => {
    const first = service();
    assert.equal((await first.speak("acme-rc", PLAN, "clubhouse", "Heat two.", true)).ok, true);

    // A restart (the count no longer in memory) with the usage file now unreadable.
    await unlink(voiceUsagePath(folder, "acme-rc"));
    await breakUsageFile();

    const again = await service().speak("acme-rc", PLAN, "clubhouse", "Heat two.", true);
    assert.equal(again.ok, true);
    if (!again.ok) return;
    assert.equal(again.cached, true);
    assert.equal(again.usage.known, false);
    assert.equal(again.usage.used, 0);
    assert.equal(again.usage.allowance, 0);
  });

  it("leaves a readable count as it was, with no known flag", async () => {
    const voice = service();
    const spoken = await voice.speak("acme-rc", PLAN, "clubhouse", "Heat two.", true);
    assert.equal(spoken.ok, true);

    const view = await voice.catalogue("acme-rc", PLAN);
    assert.equal(view.usage.used, "Heat two.".length);
    assert.equal(view.usage.known, undefined);
  });
});
