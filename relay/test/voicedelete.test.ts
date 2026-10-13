import assert from "node:assert/strict";
import { mkdir, mkdtemp, readFile, rm, stat, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { describe, it } from "node:test";
import { FileStore } from "../src/store.js";
import { hashToken, TenantRegistry, type Tenant } from "../src/tenancy.js";
import { VOICE_DIR, VoiceService, voiceTopUpPath, type CatalogueVoice, type VoicePlan } from "../src/voice.js";
import { call, start } from "./helpers.js";

/**
 * A club deleting everything it published used to wipe its voice count
 * for the month, the months before it and the operator's top-up. The
 * tenant lives in the relay's config, so the same token worked again at
 * once and the next line was counted from nothing: a fresh full allowance
 * on every delete, on HeatCaller's ElevenLabs account, and no record left
 * of what had been spent. The club's own spoken lines - driver names -
 * must still go.
 */

const VOICE: CatalogueVoice = {
  id: "clubhouse",
  name: "Clubhouse",
  what: "",
  voiceId: "el-voice",
  modelId: "eleven_flash_v2_5",
  stability: 0.5,
  similarity: 0.75,
  style: 0,
  speakerBoost: true,
  speed: 1,
  outputFormat: "",
  cloned: false,
};

const PLAN: VoicePlan = { enabled: true, monthlyChars: 20, voices: [] };
const TOKEN = "acme-rc-token-0-aaaaaaaaaaaa";

function registry(): TenantRegistry {
  const tenant: Tenant = { club: "acme-rc", name: "Acme RC", tokenSha256: hashToken(TOKEN), publishRoster: false, voice: PLAN };
  return new TenantRegistry([tenant]);
}

async function exists(path: string): Promise<boolean> {
  return stat(path).then(
    () => true,
    () => false,
  );
}

describe("deleting a club and the voice's allowance", () => {
  it("keeps the month's count, its history and the top-up, and forgets only the club's own lines", async () => {
    const dir = await mkdtemp(join(tmpdir(), "relay-voicedelete-"));
    const now = () => new Date("2026-09-20T12:00:00.000Z");
    const voice = new VoiceService({
      config: { voices: [VOICE], monthlyChars: 20, maxChars: 600 },
      dataDir: dir,
      now,
      render: async (_voice, text) => Buffer.from(`AUDIO:${text}`),
    });
    const harness = await start(new FileStore(dir), registry(), now, undefined, voice);

    try {
      const topUp = voiceTopUpPath(dir, "acme-rc");
      await mkdir(dirname(topUp), { recursive: true });
      await writeFile(topUp, JSON.stringify({ "2026-09": 5 }));

      // 10 + 10 of 25 (20 on the plan, 5 topped up).
      assert.equal((await voice.speak("acme-rc", PLAN, "clubhouse", "0123456789", false)).ok, true);
      assert.equal((await voice.speak("acme-rc", PLAN, "clubhouse", "abcdefghij", false)).ok, true);

      const deleted = await call(harness, "DELETE", "/api/v1/c/acme-rc", { token: TOKEN });
      assert.equal(deleted.status, 200);

      assert.equal(await exists(join(dir, VOICE_DIR, "clubs", "acme-rc")), false, "a driver's name, spoken, was left on disk");
      assert.equal(JSON.parse(await readFile(join(dir, VOICE_DIR, "usage", "acme-rc.json"), "utf8")).used, 20);
      assert.equal(await exists(topUp), true, "the operator's top-up went with the club's delete");

      const catalogue = await voice.catalogue("acme-rc", PLAN);
      assert.equal(catalogue.usage.used, 20, "a delete handed the club a fresh allowance");
      assert.equal(catalogue.usage.allowance, 25);

      // 20 used of 25: a further 10 is over, delete or no delete.
      assert.equal((await voice.speak("acme-rc", PLAN, "clubhouse", "klmnopqrst", false)).ok, false);
    } finally {
      await harness.close();
      await rm(dir, { recursive: true, force: true });
    }
  });
});
