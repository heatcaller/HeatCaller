import assert from "node:assert/strict";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { VoiceService, voiceUsagePath, type CatalogueVoice, type RenderFn, type VoicePlan } from "../src/voice.js";

// Bug hunt 15, voice.ts (9 Nov 2026): B30 and B39.

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

describe("bug hunt 15 - voice.ts", () => {
  let folder = "";
  let clock = new Date("2026-10-20T12:00:00.000Z");
  let renders = 0;
  const render: RenderFn = async (voice, text) => {
    renders += 1;
    return Buffer.from(`mp3:${voice.id}:${text}`, "utf8");
  };

  beforeEach(async () => {
    folder = await mkdtemp(join(tmpdir(), "relay-bh15-voice-"));
    clock = new Date("2026-10-20T12:00:00.000Z");
    renders = 0;
  });

  afterEach(async () => {
    await rm(folder, { recursive: true, force: true });
  });

  function service(): VoiceService {
    return new VoiceService({
      config: { voices: [VOICE], monthlyChars: 1_000, maxChars: 120 },
      dataDir: folder,
      render,
      now: () => clock,
    });
  }

  it("B39: a line first heard as a preview is still counted when speak says it", async () => {
    const voice = service();
    const line = "Heat three, Stock Buggy, to the line.";

    const previewed = await voice.preview("acme-rc", PLAN, "clubhouse", line);
    assert.equal(previewed.ok, true);
    assert.equal(renders, 1);

    const spoken = await voice.speak("acme-rc", PLAN, "clubhouse", line, false);
    assert.equal(spoken.ok, true);
    assert.equal(spoken.ok && spoken.cached, false, "speak must not be served the preview's audio");
    assert.equal(renders, 2);
    assert.equal((await voice.usage("acme-rc", PLAN)).used, line.length, "the race-night line is counted");

    // The other way round stays free: a line speak paid for previews free.
    const again = await voice.preview("acme-rc", PLAN, "clubhouse", line);
    assert.equal(again.ok && again.cached, true);
    assert.equal(renders, 2);
  });

  it("B30: a read across the month's end keeps a count still being written as last month's history", async () => {
    const voice = service();
    const path = voiceUsagePath(folder, "acme-rc");

    // The file as it was before the last reserve of October landed, and the
    // count that reserve left in memory while its write was still going.
    await mkdir(dirname(path), { recursive: true });
    await writeFile(path, JSON.stringify({ month: "2026-10", used: 100, history: {} }), "utf8");
    (voice as unknown as { usageCache: Map<string, unknown> }).usageCache.set("acme-rc", {
      month: "2026-10",
      used: 150,
      history: {},
    });

    clock = new Date("2026-11-01T00:00:00.010Z");

    // An unlocked read in November sees the old file.
    assert.equal((await voice.usage("acme-rc", PLAN)).used, 0);

    // That October reserve's write lands; then November's first line.
    await writeFile(path, JSON.stringify({ month: "2026-10", used: 150, history: {} }), "utf8");
    assert.equal((await voice.speak("acme-rc", PLAN, "clubhouse", "Heat one.", false)).ok, true);

    const saved = JSON.parse(await readFile(path, "utf8")) as { month: string; used: number; history: Record<string, number> };
    assert.equal(saved.month, "2026-11");
    assert.equal(saved.used, "Heat one.".length);
    assert.equal(saved.history["2026-10"], 150, "October's last line is still billed");
  });
});
