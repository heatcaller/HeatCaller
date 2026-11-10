import assert from "node:assert/strict";
import { mkdtemp, mkdir, readFile, rm, utimes, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { hashToken } from "../src/tenancy.js";
import {
  readTopUps,
  readUsageRecord,
  VoiceService,
  voiceTopUpPath,
  voiceUsagePath,
  type CatalogueVoice,
  type RenderFn,
  type VoicePlan,
} from "../src/voice.js";
import { voiceAdmin } from "../src/voiceadmin.js";

// Metering a club for the HeatCaller voice, from the side of whoever runs
// the relay: each month kept once it is over, a report of every club, and
// characters given to a club for this month on top of its plan - picked up
// by a running relay on the club's next line.

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

describe("metering the HeatCaller voice", () => {
  let folder = "";
  let clock = new Date("2026-09-20T12:00:00.000Z");

  beforeEach(async () => {
    folder = await mkdtemp(join(tmpdir(), "relay-voiceadmin-"));
    clock = new Date("2026-09-20T12:00:00.000Z");
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

  /** The env a relay machine has: the config as a secret, the data folder from fly.toml. */
  function env(clubs: readonly Record<string, unknown>[] = [
    { club: "acme-rc", name: "Acme RC", tokenSha256: hashToken("acme-token-aaaaaaaa"), voice: { enabled: true, monthlyChars: 100 } },
    { club: "brookside", name: "Brookside", tokenSha256: hashToken("brook-token-bbbbbbbb") },
  ]): NodeJS.ProcessEnv {
    return {
      RELAY_DATA: folder,
      RELAY_CONFIG_JSON: JSON.stringify({
        voice: { monthlyChars: 100, voices: [{ id: "clubhouse", voiceId: "el-voice-clubhouse" }] },
        clubs,
      }),
    };
  }

  async function run(command: "voice-usage" | "voice-topup", args: string[], environment = env()): Promise<{ code: number; out: string; err: string }> {
    let out = "";
    let err = "";
    const code = await voiceAdmin(command, args, environment, clock, (text) => (out += text), (text) => (err += text));
    return { code, out, err };
  }

  describe("the months before this one", () => {
    it("keeps last month's count when the first line of a new month is spoken", async () => {
      const voice = service();
      await voice.speak("acme-rc", PLAN, "clubhouse", "Ten chars.", false);

      clock = new Date("2026-10-02T09:00:00.000Z");
      const next = await voice.speak("acme-rc", PLAN, "clubhouse", "Four", false);

      assert.equal(next.ok, true);
      const saved = JSON.parse(await readFile(voiceUsagePath(folder, "acme-rc"), "utf8")) as Record<string, unknown>;
      assert.equal(saved["month"], "2026-10");
      assert.equal(saved["used"], 4);
      assert.deepEqual(saved["history"], { "2026-09": 10 });
    });

    it("turns the page for a relay that stayed up across the month", async () => {
      const voice = service();
      await voice.speak("acme-rc", PLAN, "clubhouse", "Ten chars.", false);

      // Same service, so the count is in memory; the month changes under it.
      clock = new Date("2026-11-01T00:00:01.000Z");
      await voice.speak("acme-rc", PLAN, "clubhouse", "Twelve chars", false);
      clock = new Date("2026-12-05T00:00:00.000Z");
      await voice.speak("acme-rc", PLAN, "clubhouse", "Six!!!", false);

      const saved = readUsageRecord(JSON.parse(await readFile(voiceUsagePath(folder, "acme-rc"), "utf8")) as unknown, "2026-12");
      assert.equal(saved.used, 6);
      assert.deepEqual(saved.history, { "2026-09": 10, "2026-11": 12 });
    });

    it("reads a usage file from before there was a history", () => {
      assert.deepEqual(readUsageRecord({ month: "2026-09", used: 321 }, "2026-09"), { used: 321, history: {} });
      assert.deepEqual(readUsageRecord({ month: "2026-08", used: 321 }, "2026-09"), { used: 0, history: { "2026-08": 321 } });
    });

    it("ignores anything in the history that is not a past month and a count", () => {
      const read = readUsageRecord(
        { month: "2026-09", used: 5, history: { "2026-08": 40, "2026-13": 9, nonsense: 9, "2026-07": -3, "2026-10": 7, "2026-06": "x" } },
        "2026-09",
      );

      assert.deepEqual(read, { used: 5, history: { "2026-08": 40 } });
    });

    it("keeps three years and no more", () => {
      const history: Record<string, number> = {};

      for (let month = 0; month < 40; month += 1) {
        const when = new Date(Date.UTC(2023, month, 1));
        history[`${when.getUTCFullYear()}-${String(when.getUTCMonth() + 1).padStart(2, "0")}`] = 1;
      }

      const read = readUsageRecord({ month: "2026-09", used: 1, history }, "2026-09");
      const kept = Object.keys(read.history).sort();

      assert.equal(kept.length, 36);
      assert.equal(kept[kept.length - 1], "2026-04");
    });
  });

  describe("voice-topup", () => {
    it("adds characters for this month, which a running relay spends on the club's next line", async () => {
      const voice = service();
      await voice.speak("acme-rc", PLAN, "clubhouse", "x".repeat(100), false);

      const refused = await voice.speak("acme-rc", PLAN, "clubhouse", "One more line", false);
      assert.equal(refused.ok, false);
      assert.equal(refused.ok ? "" : refused.refusal, "allowance");

      const given = await run("voice-topup", ["acme-rc", "50"]);
      assert.equal(given.code, 0, given.err);
      assert.match(given.out, /acme-rc: 50 characters on top of its plan for September 2026\. It may speak 150 this month and has used 100\./);

      const spoken = await voice.speak("acme-rc", PLAN, "clubhouse", "One more line", false);
      assert.equal(spoken.ok, true);
      assert.equal(spoken.ok ? spoken.usage.allowance : 0, 150);
    });

    it("adds to a top-up already given, and takes some back with a negative number - never below none", async () => {
      await run("voice-topup", ["acme-rc", "1,000"]);
      await run("voice-topup", ["acme-rc", "500"]);
      assert.deepEqual(readTopUps(JSON.parse(await readFile(voiceTopUpPath(folder, "acme-rc"), "utf8")) as unknown), { "2026-09": 1500 });

      await run("voice-topup", ["acme-rc", "-200"]);
      assert.deepEqual(readTopUps(JSON.parse(await readFile(voiceTopUpPath(folder, "acme-rc"), "utf8")) as unknown), { "2026-09": 1300 });

      const gone = await run("voice-topup", ["acme-rc", "-99999"]);
      assert.equal(gone.code, 0);
      assert.match(gone.out, /no top-up/);
      assert.deepEqual(readTopUps(JSON.parse(await readFile(voiceTopUpPath(folder, "acme-rc"), "utf8")) as unknown), {});
    });

    it("lasts this month only", async () => {
      const voice = service();
      await run("voice-topup", ["acme-rc", "50"]);

      const now = await voice.catalogue("acme-rc", PLAN);
      assert.equal(now.usage.allowance, 150);

      clock = new Date("2026-10-01T00:00:00.000Z");
      const next = await voice.catalogue("acme-rc", PLAN);
      assert.equal(next.usage.allowance, 100);
    });

    it("refuses a club the config does not have, zero, a typo's worth, and anything that is not a number", async () => {
      assert.match((await run("voice-topup", ["nobody-rc", "50"])).err, /no club "nobody-rc"/);
      assert.equal((await run("voice-topup", ["acme-rc", "0"])).code, 1);
      assert.equal((await run("voice-topup", ["acme-rc", "50000000"])).code, 1);
      assert.equal((await run("voice-topup", ["acme-rc", "lots"])).code, 1);
      assert.equal((await run("voice-topup", ["acme-rc"])).code, 1);
      assert.equal((await run("voice-topup", ["acme-rc", "50", "60"])).code, 1);
    });

    it("outlives the club's delete", async () => {
      const voice = service();
      await run("voice-topup", ["acme-rc", "50"]);
      await voice.forget("acme-rc");

      // A delete and a fresh start must not hand a club a new month's
      // allowance, nor lose a top-up it paid for.
      assert.equal((await voice.catalogue("acme-rc", PLAN)).usage.allowance, 150);
      await readFile(voiceTopUpPath(folder, "acme-rc"), "utf8");
    });

    it("says so when the club does not have the voice switched on", async () => {
      const given = await run("voice-topup", ["brookside", "50"]);

      assert.equal(given.code, 0, given.err);
      assert.match(given.out, /not switched on for brookside, so this does nothing until it is/);
    });

    it("reads a top-up file caught half-written again on the next line, rather than keeping it as none", async () => {
      const voice = service();
      const path = voiceTopUpPath(folder, "acme-rc");
      const when = new Date("2026-09-20T11:00:00.000Z");

      await mkdir(join(folder, ".voice", "topups"), { recursive: true });
      await writeFile(path, "{not json", "utf8");
      await utimes(path, when, when);
      assert.equal((await voice.catalogue("acme-rc", PLAN)).usage.allowance, 100);

      // The same modification time: only a re-read can see it.
      await writeFile(path, JSON.stringify({ "2026-09": 50 }), "utf8");
      await utimes(path, when, when);
      assert.equal((await voice.catalogue("acme-rc", PLAN)).usage.allowance, 150);
    });

    it("reads a top-up file that cannot be read as none, not as no voice", async () => {
      await mkdir(join(folder, ".voice", "topups"), { recursive: true });
      await writeFile(voiceTopUpPath(folder, "acme-rc"), "{not json", "utf8");

      const spoken = await service().speak("acme-rc", PLAN, "clubhouse", "Green flag!", true);
      assert.equal(spoken.ok, true);
      assert.equal(spoken.ok ? spoken.usage.allowance : 0, 100);
    });
  });

  describe("voice-usage", () => {
    it("lists every club with the voice, this month and the three before", async () => {
      const voice = service();
      clock = new Date("2026-08-10T12:00:00.000Z");
      await voice.speak("acme-rc", PLAN, "clubhouse", "Eight ch", false);
      clock = new Date("2026-09-20T12:00:00.000Z");
      await voice.speak("acme-rc", PLAN, "clubhouse", "Twelve chars", false);
      await run("voice-topup", ["acme-rc", "50"]);

      const report = await run("voice-usage", []);

      assert.equal(report.code, 0, report.err);
      assert.match(report.out, /HeatCaller voice - September 2026, so far\./);
      assert.ok(report.out.includes(`Read from ${folder}.`), "says where it read, so a wrong folder is not a quiet month");
      assert.match(report.out, /club\s+voice\s+plan\s+top-up\s+used\s+left\s+%\s+Aug 26\s+Jul 26\s+Jun 26/);
      assert.match(report.out, /acme-rc\s+on\s+100\s+50\s+12\s+138\s+8%\s+8\s+0\s+0/);
      // Brookside has no voice and has never spoken: not listed.
      assert.doesNotMatch(report.out, /brookside/);
      assert.match(report.out, /All clubs: 12 characters this month\./);
    });

    it("still lists a club switched off after it spoke, so what it said is not lost", async () => {
      await service().speak("brookside", PLAN, "clubhouse", "Hello there", false);

      const report = await run("voice-usage", []);
      assert.match(report.out, /brookside\s+off\s+100\s+-\s+11/);
    });

    it("answers --json with every club and its whole history", async () => {
      clock = new Date("2026-08-10T12:00:00.000Z");
      await service().speak("acme-rc", PLAN, "clubhouse", "Eight ch", false);
      clock = new Date("2026-09-20T12:00:00.000Z");

      const report = await run("voice-usage", ["--json"]);
      const parsed = JSON.parse(report.out) as { month: string; clubs: { club: string; used: number; history: Record<string, number> }[] };

      assert.equal(parsed.month, "2026-09");
      assert.deepEqual(
        parsed.clubs.map((club) => [club.club, club.used, club.history]),
        [
          ["acme-rc", 0, { "2026-08": 8 }],
          ["brookside", 0, {}],
        ],
      );
    });

    it("says so when no club has the voice", async () => {
      const report = await run("voice-usage", [], env([{ club: "brookside", name: "Brookside", tokenSha256: hashToken("brook-token-bbbbbbbb") }]));
      assert.match(report.out, /No club has the HeatCaller voice included/);
    });

    it("refuses anything it does not know, and a config it cannot read", async () => {
      assert.equal((await run("voice-usage", ["--all"])).code, 1);
      assert.equal((await run("voice-usage", [], { RELAY_DATA: folder, RELAY_CONFIG_JSON: "{oops" })).code, 1);
    });

    // Bug hunt 15: a usage file that is there but will not read is one the
    // relay refuses to spend against - the report used to show it as
    // "0 used, no history", a zero the operator would bill from.
    it("shows a usage file that will not read as unknown, never as nothing used", async () => {
      await service().speak("acme-rc", PLAN, "clubhouse", "Twelve chars", false);
      await writeFile(voiceUsagePath(folder, "acme-rc"), "{\"month\": \"2026-09\", \"us");

      const report = await run("voice-usage", []);
      assert.equal(report.code, 0, report.err);
      assert.match(report.out, /acme-rc\s+on\s+100\s+-\s+\?\s+\?\s+\?\s+\?\s+\?\s+\?/);
      assert.match(report.out, /All clubs: 0 characters this month, not counting acme-rc\./);
      assert.match(report.out, /acme-rc: its usage file is there but will not read/);

      const json = JSON.parse((await run("voice-usage", ["--json"])).out) as { clubs: { club: string; known?: boolean }[] };
      assert.deepEqual(json.clubs.map((club) => [club.club, club.known]), [["acme-rc", false], ["brookside", undefined]]);

      // Not a usage record at all reads the same way; no file is still a known zero.
      await writeFile(voiceUsagePath(folder, "acme-rc"), "null");
      assert.match((await run("voice-usage", [])).out, /acme-rc\s+on\s+100\s+-\s+\?/);

      const given = await run("voice-topup", ["acme-rc", "50", "--json"]);
      assert.equal(given.code, 0, given.err);
      assert.equal((JSON.parse(given.out) as { known?: boolean }).known, false);
      assert.match((await run("voice-topup", ["acme-rc", "1"])).out, /what it has used is unknown - its usage file will not read/);
    });
  });
});
