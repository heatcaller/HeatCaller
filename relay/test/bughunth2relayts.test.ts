import assert from "node:assert/strict";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { describe, it } from "node:test";
import { FileStore, MemoryStore } from "../src/store.js";
import { VOICE_DIR, VoiceService, type CatalogueVoice, type VoicePlan } from "../src/voice.js";
import { ACME_TOKEN, call, start } from "./helpers.js";

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

/**
 * A usage file that is there but cannot be read used to be taken as "no
 * characters spent", and the next line wrote its own count - and an empty
 * history - over it: the month's count and every earlier month gone, and
 * a fresh allowance handed out on HeatCaller's ElevenLabs account.
 */
describe("a voice usage file that cannot be read", () => {
  it("is never spent against or written over, and counts on from it once it reads", async () => {
    const dir = await mkdtemp(join(tmpdir(), "relay-bughunth2-voice-"));
    const logged: string[] = [];
    let rendered = 0;
    const voice = new VoiceService({
      config: { voices: [VOICE], monthlyChars: 20, maxChars: 600 },
      dataDir: dir,
      now: () => new Date("2026-09-20T12:00:00.000Z"),
      log: (line) => logged.push(line),
      render: async (_voice, text) => {
        rendered++;
        return Buffer.from(`AUDIO:${text}`);
      },
    });

    try {
      const usageFile = join(dir, VOICE_DIR, "usage", "acme-rc.json");
      const truncated = '{"month":"2026-09","used":15,"history":{"2026-08":7';
      await mkdir(join(dir, VOICE_DIR, "usage"), { recursive: true });
      await writeFile(usageFile, truncated);

      // Voice Settings still opens. The figure is marked unknown - 0 of 0,
      // known:false - so the app does not take it for the club's count
      // (20 Oct 2026, Bug Hunt 8, #101).
      const unknown = (await voice.catalogue("acme-rc", PLAN)).usage;
      assert.equal(unknown.used, 0);
      assert.equal(unknown.allowance, 0);
      assert.equal(unknown.known, false);

      const refused = await voice.speak("acme-rc", PLAN, "clubhouse", "abc", false);
      assert.equal(refused.ok, false);
      assert.equal(refused.ok ? "" : refused.refusal, "busy");
      assert.equal(rendered, 0, "a line was rendered against a count nobody could read");
      assert.equal(await readFile(usageFile, "utf8"), truncated, "the unreadable usage file was written over");
      assert.ok(logged.some((line) => line.includes("usage file")));

      // A file that parses but is not a usage record is held the same way.
      await writeFile(usageFile, "null");
      assert.equal((await voice.speak("acme-rc", PLAN, "clubhouse", "abc", false)).ok, false);
      assert.equal(await readFile(usageFile, "utf8"), "null");

      // Mended: the next line is counted on top of what the file holds,
      // and the history it holds is kept.
      await writeFile(usageFile, JSON.stringify({ month: "2026-09", used: 15, history: { "2026-08": 7 } }));
      const spoken = await voice.speak("acme-rc", PLAN, "clubhouse", "abc", false);
      assert.equal(spoken.ok, true);

      const after = JSON.parse(await readFile(usageFile, "utf8"));
      assert.equal(after.used, 18);
      assert.deepEqual(after.history, { "2026-08": 7 });

      // And the allowance still holds: 18 + 3 is past 20.
      const over = await voice.speak("acme-rc", PLAN, "clubhouse", "xyz", false);
      assert.equal(over.ok ? "" : over.refusal, "allowance");
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("still treats no file at all as nothing spent", async () => {
    const dir = await mkdtemp(join(tmpdir(), "relay-bughunth2-voice-"));
    const voice = new VoiceService({
      config: { voices: [VOICE], monthlyChars: 20, maxChars: 600 },
      dataDir: dir,
      now: () => new Date("2026-09-20T12:00:00.000Z"),
      render: async (_voice, text) => Buffer.from(`AUDIO:${text}`),
    });

    try {
      assert.equal((await voice.speak("acme-rc", PLAN, "clubhouse", "abc", false)).ok, true);
      assert.equal(JSON.parse(await readFile(join(dir, VOICE_DIR, "usage", "acme-rc.json"), "utf8")).used, 3);
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });
});

/**
 * A check-in inbox, manager queue or sign-up box that will never parse
 * used to throw on every read - and every write reads first - so it was
 * never replaced, and the feature stayed broken until someone deleted
 * the file on the server.
 */
describe("a corrupt queue document", () => {
  it("reads as empty and is replaced by the next write", async () => {
    const dir = await mkdtemp(join(tmpdir(), "relay-bughunth2-inbox-"));
    const store = new FileStore(dir);
    const harness = await start(store);

    try {
      await mkdir(join(dir, "acme-rc", "snapshot"), { recursive: true });
      await writeFile(join(dir, "acme-rc", "snapshot", "inbox.json"), '{"seq":0,"body":{"next":4,"it');

      const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/inbox", { token: ACME_TOKEN });
      assert.equal(inbox.status, 200);
      assert.deepEqual(inbox.body.items, []);

      const ack = await call(harness, "POST", "/api/v1/c/acme-rc/inbox/ack", { token: ACME_TOKEN, json: { upTo: 0 } });
      assert.equal(ack.status, 200);

      // Mended on disk, not just read around.
      const mended = await store.getSnapshot("acme-rc", "inbox");
      assert.ok(mended !== undefined);
    } finally {
      await harness.close();
      await rm(dir, { recursive: true, force: true });
    }
  });
});

/**
 * The results index has a budget of its own, and past it "the oldest
 * year makes way". It was the year WRITTEN longest ago that went, and the
 * laptop rewrites an old year whenever one of its sessions changes - so a
 * recent season's index could be deleted while a far older one stayed.
 */
describe("the results index's own budget", () => {
  it("evicts the earliest year, not the one written longest ago", async () => {
    let clock = Date.parse("2026-09-20T12:00:00.000Z");
    const harness = await start(new MemoryStore(), undefined, () => new Date((clock += 60_000)));
    const publish = async (year: number) =>
      (
        await call(harness, "POST", `/api/v1/c/acme-rc/publish/resultsindex/${year}`, {
          token: ACME_TOKEN,
          json: { year, pad: "i".repeat(500 * 1024) },
        })
      ).status;

    try {
      assert.equal(await publish(2019), 200);
      assert.equal(await publish(2020), 200);
      assert.equal(await publish(2021), 200);

      // A stray 2019 heat deleted at the desk: 2019 is now the newest written.
      assert.equal(await publish(2019), 200);
      assert.equal(await publish(2022), 200);

      // Over the two-megabyte budget: one year must make way.
      assert.equal(await publish(2023), 200);

      const years = (await call(harness, "GET", "/api/v1/c/acme-rc/collection/resultsindex?meta=1")).body.items
        .map((item: { id: string }) => item.id)
        .sort();

      assert.deepEqual(years, ["2020", "2021", "2022", "2023"]);
    } finally {
      await harness.close();
    }
  });
});
