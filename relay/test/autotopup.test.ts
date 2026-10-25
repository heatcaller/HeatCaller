import assert from "node:assert/strict";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { autoTopUpPath, readAutoTopUp } from "../src/autotopup.js";
import { BrainService, BrainUpstreamError, type BrainConfig, type BrainPlan } from "../src/brain.js";
import { loadConfig } from "../src/config.js";
import { MemoryStore } from "../src/store.js";
import { hashToken } from "../src/tenancy.js";
import { TopUpRequests, requestsFor, topUpDonePath } from "../src/topuprequests.js";
import { UsageLog } from "../src/usagelog.js";
import { VoiceService, voiceTopUpPath, type CatalogueVoice, type RenderFn, type VoicePlan } from "../src/voice.js";
import { voiceAdmin, type AdminCommand } from "../src/voiceadmin.js";
import { ACME_TOKEN, call, start, tenants, type Harness } from "./helpers.js";

// Round D (automatic top-ups), round B (one trip per refresh), round C
// (Anthropic's failures logged as upstream) and round E (the shared audit
// log), from the relay's side.

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
const ACME_BRAIN: BrainPlan = { enabled: true, monthlyLines: 5 };
const ACME_VOICE: VoicePlan = { enabled: true, monthlyChars: 1_000, voices: [] };
const render: RenderFn = async (voice, text) => Buffer.from(`mp3:${voice.id}:${text}`, "utf8");

describe("automatic top-ups", () => {
  let folder = "";
  let clock = new Date("2026-10-20T12:00:00.000Z");
  let harness: Harness | undefined;

  beforeEach(async () => {
    folder = await mkdtemp(join(tmpdir(), "relay-auto-"));
    clock = new Date("2026-10-20T12:00:00.000Z");
  });

  afterEach(async () => {
    await harness?.close();
    harness = undefined;
    await rm(folder, { recursive: true, force: true });
  });

  const voice = () => new VoiceService({ config: { voices: [VOICE], monthlyChars: 1_000, maxChars: 120 }, dataDir: folder, render, now: () => clock });
  const brain = () => new BrainService({ config: BRAIN_CONFIG, dataDir: folder, write: async () => "A line.", now: () => clock });

  async function serve(acme: Record<string, unknown> = {}, services = { voice: voice(), brain: brain() }): Promise<Harness> {
    harness = await start(
      new MemoryStore(),
      tenants([{ brain: ACME_BRAIN, voice: ACME_VOICE, ...acme }]),
      () => clock,
      () => {},
      services.voice,
      { brain: services.brain, topUpRequests: new TopUpRequests({ dataDir: folder, now: () => clock }) },
    );
    return harness;
  }

  const ask = (meter: "voice" | "commentary", note = "") =>
    call(harness!, "POST", "/api/v1/c/acme-rc/usage/topup-request", { token: ACME_TOKEN, json: { meter, note } });

  describe("the config", () => {
    it("reads a rule per meter, and none at all when absent", () => {
      assert.deepEqual(readAutoTopUp({ voice: { amount: 25000, perMonth: 2 } }, "acme-rc", "test"), { voice: { amount: 25000, perMonth: 2 } });
      assert.equal(readAutoTopUp(undefined, "acme-rc", "test"), undefined);
      assert.equal(readAutoTopUp({}, "acme-rc", "test"), undefined);
    });

    it("refuses to start on a rule that would give away too much or makes no sense", () => {
      for (const bad of [
        { voice: { amount: 0, perMonth: 1 } },
        { voice: { amount: 25000.5, perMonth: 1 } },
        { voice: { amount: 20_000_000, perMonth: 1 } },
        { voice: { amount: 100, perMonth: 11 } },
        { voice: { amount: 100 } },
        { lines: { amount: 100, perMonth: 1 } },
        "yes",
      ]) {
        assert.throws(() => readAutoTopUp(bad, "acme-rc", "test"), /autoTopUp/, JSON.stringify(bad));
      }
    });

    it("comes through loadConfig onto the club", async () => {
      const config = await loadConfig("unused", {
        RELAY_CONFIG_JSON: JSON.stringify({
          clubs: [
            { club: "acme-rc", tokenSha256: hashToken(ACME_TOKEN), autoTopUp: { commentary: { amount: 100, perMonth: 1 } } },
            { club: "brookside", tokenSha256: hashToken("x") },
          ],
        }),
      });

      assert.deepEqual(config.tenants[0]?.autoTopUp, { commentary: { amount: 100, perMonth: 1 } });
      assert.equal("autoTopUp" in config.tenants[1]!, false);
    });
  });

  describe("an ask for more", () => {
    // Commentary, whose lines are easy to spend here: an ask is answered
    // automatically only once 90% of the month's allowance is used.
    const SMALL: BrainPlan = { enabled: true, monthlyLines: 2 };

    async function spend(lines: BrainService, count: number, plan: BrainPlan = SMALL): Promise<void> {
      for (let i = 0; i < count; i += 1) {
        assert.equal((await lines.write("acme-rc", plan, "sys", "facts", 40, 0.7)).ok, true);
      }
    }

    it("is answered at once when the club has nearly run out, and the allowance grows on its very next line", async () => {
      const services = { voice: voice(), brain: brain() };
      await serve({ brain: SMALL, autoTopUp: { commentary: { amount: 10, perMonth: 2 } } }, services);
      await spend(services.brain, 2);

      const answer = await ask("commentary", "Saturday's a big one");

      assert.equal(answer.status, 200);
      assert.equal(answer.body.autoTopUp, 10);
      assert.equal(answer.body.request.status, "done");
      assert.equal((await services.brain.usage("acme-rc", SMALL)).allowance, 12);

      const asks = await requestsFor(folder, "acme-rc", "2026-10");
      assert.equal(asks[0]?.status, "done", "the ask shows as answered, to the laptop and the admin app");
    });

    it("waits for the operator while the club still has plenty left, so taps can't take the month's top-ups at once", async () => {
      await serve({ autoTopUp: { voice: { amount: 500, perMonth: 5 } } });

      const answer = await ask("voice");

      assert.equal(answer.body.autoTopUp, undefined);
      assert.equal(answer.body.request.status, "open");
    });

    it("stops after the month's limit, and starts again next month", async () => {
      const services = { voice: voice(), brain: brain() };
      await serve({ brain: SMALL, autoTopUp: { commentary: { amount: 1, perMonth: 2 } } }, services);

      await spend(services.brain, 2);
      assert.equal((await ask("commentary")).body.autoTopUp, 1);
      await spend(services.brain, 1);
      assert.equal((await ask("commentary")).body.autoTopUp, 1);
      await spend(services.brain, 1);

      const third = await ask("commentary");
      assert.equal(third.body.autoTopUp, undefined, "two a month");
      assert.equal(third.body.request.status, "open");

      clock = new Date("2026-11-02T12:00:00.000Z");
      await spend(services.brain, 2);
      assert.equal((await ask("commentary")).body.autoTopUp, 1);
    });

    it("gives nothing for a meter without a rule, or not in the plan", async () => {
      const services = { voice: voice(), brain: brain() };
      await serve({ brain: SMALL, autoTopUp: { voice: { amount: 500, perMonth: 2 } } }, services);
      await spend(services.brain, 2);
      assert.equal((await ask("commentary")).body.autoTopUp, undefined, "no rule for commentary");
      await harness!.close();

      await serve({ brain: { ...SMALL, enabled: false }, autoTopUp: { commentary: { amount: 5, perMonth: 2 } } });
      assert.equal((await ask("commentary")).body.autoTopUp, undefined, "commentary not in the plan");
    });

    it("never gives twice for one ask, and records an ask made before midnight in its own month", async () => {
      const requests = new TopUpRequests({ dataDir: folder, now: () => clock });
      clock = new Date("2026-11-01T00:00:00.500Z");

      const first = await requests.autoTopUp("acme-rc", "voice", "r_one", { amount: 500, perMonth: 9 }, "2026-10-31T23:59:59.900Z");
      const again = await requests.autoTopUp("acme-rc", "voice", "r_one", { amount: 500, perMonth: 9 }, "2026-10-31T23:59:59.900Z");

      assert.equal(first?.amount, 500);
      assert.equal(again, undefined, "one ask, one top-up");

      const file = JSON.parse(await readFile(autoTopUpPath(folder, "acme-rc"), "utf8"));
      assert.equal(file["2026-10"].length, 1);
      assert.equal(file["2026-11"], undefined);
    });

    it("never touches the operator's own top-up file", async () => {
      const services = { voice: voice(), brain: brain() };
      await serve({ brain: SMALL, autoTopUp: { commentary: { amount: 5, perMonth: 2 } } }, services);
      await spend(services.brain, 2);
      assert.equal((await ask("commentary")).body.autoTopUp, 5);

      await assert.rejects(readFile(voiceTopUpPath(folder, "acme-rc"), "utf8"), /ENOENT/);
      await assert.rejects(readFile(topUpDonePath(folder, "acme-rc"), "utf8"), /ENOENT/);
    });
  });

  describe("the admin commands", () => {
    function env(): NodeJS.ProcessEnv {
      return {
        RELAY_DATA: folder,
        RELAY_CONFIG_JSON: JSON.stringify({
          voice: { monthlyChars: 100, voices: [{ id: "clubhouse", voiceId: "el-voice-clubhouse" }] },
          clubs: [{ club: "acme-rc", name: "Acme RC", tokenSha256: hashToken(ACME_TOKEN), voice: { enabled: true, monthlyChars: 100 }, autoTopUp: { voice: { amount: 50, perMonth: 2 } } }],
        }),
      };
    }

    async function run(command: AdminCommand, args: string[]): Promise<{ code: number; out: string; err: string }> {
      let out = "";
      let err = "";
      const offline: typeof fetch = async () => {
        throw new Error("offline");
      };
      const code = await voiceAdmin(command, args, env(), clock, (text) => (out += text), (text) => (err += text), offline);
      return { code, out, err };
    }

    it("count the relay's automatic top-ups in the allowance and list them", async () => {
      const requests = new TopUpRequests({ dataDir: folder, now: () => clock });
      const asked = await requests.ask("acme-rc", "voice", "");
      assert.equal(asked.ok, true);
      await requests.autoTopUp("acme-rc", "voice", asked.ok ? asked.request.id : "", { amount: 50, perMonth: 2 });

      const snapshot = JSON.parse((await run("admin-snapshot", ["--json"])).out);
      const acme = snapshot.clubs[0];

      assert.equal(acme.voice.topUp, 50);
      assert.equal(acme.voice.autoTopUp, 50);
      assert.equal(acme.voice.allowance, 150);
      assert.equal(snapshot.requests.length, 0, "the ask is answered");
      assert.deepEqual(snapshot.autoTopUps.map((given: { club: string; amount: number }) => [given.club, given.amount]), [["acme-rc", 50]]);

      // A manual top-up's answer counts them too.
      const topped = JSON.parse((await run("voice-topup", ["acme-rc", "25", "--json"])).out);
      assert.equal(topped.allowance, 175);
    });

    it("hand back the usage log's first page in the same answer when asked (one trip per refresh)", async () => {
      const log = new UsageLog({ dataDir: folder });
      await log.record({ at: "2026-10-20T11:00:00.000Z", club: "acme-rc", meter: "voice", kind: "line", units: 10, outcome: "ok", ms: 5 });
      await log.record({ at: "2026-10-20T11:30:00.000Z", club: "acme-rc", meter: "voice", kind: "line", units: 20, outcome: "ok", ms: 5 });

      const all = JSON.parse((await run("admin-snapshot", ["--json", "--usage-since", "all"])).out);
      assert.equal(all.usageLog.events.length, 2);

      const after = JSON.parse((await run("admin-snapshot", ["--json", "--usage-since", "2026-10-20T11:15:00Z"])).out);
      assert.deepEqual(after.usageLog.events.map((e: { units: number }) => e.units), [20]);

      const without = JSON.parse((await run("admin-snapshot", ["--json"])).out);
      assert.equal("usageLog" in without, false);

      const bad = await run("admin-snapshot", ["--usage-since", "yesterday"]);
      assert.equal(bad.code, 1);
    });

    it("keep the shared audit log: an entry in, newest first out, nothing unchecked written", async () => {
      const entry = (action: string, at: string) =>
        Buffer.from(JSON.stringify({ at, action, club: "acme-rc", description: "line one\nline two", succeeded: true, by: "asher@example.com" })).toString("base64url");

      assert.equal((await run("audit-append", [entry("Top-up", "2026-10-20T10:00:00Z")])).code, 0);
      assert.equal((await run("audit-append", [entry("Pause", "2026-10-20T11:00:00Z")])).code, 0);

      const log = JSON.parse((await run("audit-log", ["--json"])).out);
      assert.deepEqual(log.entries.map((e: { action: string }) => e.action), ["Pause", "Top-up"]);
      assert.equal(log.entries[0].description, "line one line two", "one entry is always one line");

      for (const bad of [
        "not base64 !",
        Buffer.from("not json").toString("base64url"),
        Buffer.from(JSON.stringify({ at: "2026-10-20T10:00:00Z", action: "X", club: "../etc", description: "", succeeded: true })).toString("base64url"),
        Buffer.from(JSON.stringify({ action: "X", description: "", succeeded: true })).toString("base64url"),
      ]) {
        assert.equal((await run("audit-append", [bad])).code, 1, bad);
      }

      assert.equal(JSON.parse((await run("audit-log", ["--json", "--limit", "1"])).out).entries.length, 1);
    });
  });

  describe("the usage log", () => {
    it("calls Anthropic's own 429 upstream, though the club is told busy", async () => {
      const failing = new BrainService({
        config: BRAIN_CONFIG,
        dataDir: folder,
        write: async () => {
          throw new BrainUpstreamError("the model answered 529: overloaded", true);
        },
        now: () => clock,
      });
      const lines: string[] = [];
      const usageLog = new UsageLog({ dataDir: folder });
      const recorded: string[] = [];
      const original = usageLog.record.bind(usageLog);
      usageLog.record = async (event) => {
        recorded.push(event.outcome);
        return original(event);
      };

      harness = await start(new MemoryStore(), tenants([{ brain: ACME_BRAIN }]), () => clock, (line) => lines.push(line), undefined, { brain: failing, usageLog });

      const answer = await call(harness, "POST", "/api/v1/c/acme-rc/brain/write", {
        token: ACME_TOKEN,
        json: { system: "You are the announcer.", facts: "Lap 3.", words: 30 },
      });

      assert.equal(answer.body.refusal, "busy", "the club still hears busy, and tries again");
      assert.deepEqual(recorded, ["upstream"]);
    });
  });
});
