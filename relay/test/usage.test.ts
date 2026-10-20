import assert from "node:assert/strict";
import { mkdir, mkdtemp, readFile, rm, stat, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { Script } from "node:vm";
import { afterEach, beforeEach, describe, it } from "node:test";
import { BrainService, brainTopUpPath, brainUsagePath, type BrainConfig, type BrainPlan } from "../src/brain.js";
import { managePinHash } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { hashToken } from "../src/tenancy.js";
import {
  cleanNote,
  closeRequests,
  topUpDonePath,
  topUpRequestsPath,
  TopUpRequests,
  TOP_UP_REQUESTS_PER_MONTH,
} from "../src/topuprequests.js";
import { VoiceService, voiceTopUpPath, type CatalogueVoice, type RenderFn, type VoicePlan } from "../src/voice.js";
import { voiceAdmin, type AdminCommand } from "../src/voiceadmin.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, tenants, type Harness } from "./helpers.js";

// How much of each monthly allowance a club has used - AI commentary lines
// and HeatCaller voice characters - read by the laptop with its token and
// by the manager's phone with its pass, without spending anything; asking
// for more; and the operator's commands that give more and close the ask.

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

const BRAIN_CONFIG: BrainConfig = { model: "test-model", monthlyLines: 5, maxWords: 60 };
const ACME_BRAIN: BrainPlan = { enabled: true, monthlyLines: 5 };
const ACME_VOICE: VoicePlan = { enabled: true, monthlyChars: 1_000, voices: [] };

const render: RenderFn = async (voice, text) => Buffer.from(`mp3:${voice.id}:${text}`, "utf8");

describe("usage meters", () => {
  let folder = "";
  let clock = new Date("2026-09-20T12:00:00.000Z");
  let harness: Harness | undefined;

  beforeEach(async () => {
    folder = await mkdtemp(join(tmpdir(), "relay-usage-"));
    clock = new Date("2026-09-20T12:00:00.000Z");
  });

  afterEach(async () => {
    await harness?.close();
    harness = undefined;
    await rm(folder, { recursive: true, force: true });
  });

  function brain(): BrainService {
    return new BrainService({ config: BRAIN_CONFIG, dataDir: folder, write: async () => "A line.", now: () => clock });
  }

  function voice(): VoiceService {
    return new VoiceService({ config: { voices: [VOICE], monthlyChars: 1_000, maxChars: 120 }, dataDir: folder, render, now: () => clock });
  }

  function requests(): TopUpRequests {
    return new TopUpRequests({ dataDir: folder, now: () => clock });
  }

  /** acme-rc has both meters; brookside has neither. */
  async function serve(
    services: { brain?: BrainService; voice?: VoiceService; topUpRequests?: TopUpRequests } = {
      brain: brain(),
      voice: voice(),
      topUpRequests: requests(),
    },
  ): Promise<Harness> {
    harness = await start(
      new MemoryStore(),
      tenants([{ brain: ACME_BRAIN, voice: ACME_VOICE }]),
      () => clock,
      () => {},
      services.voice,
      { brain: services.brain, topUpRequests: services.topUpRequests },
    );
    return harness;
  }

  async function writeJson(path: string, value: unknown): Promise<void> {
    await mkdir(dirname(path), { recursive: true });
    await writeFile(path, JSON.stringify(value), "utf8");
  }

  // --- GET usage --------------------------------------------------------------

  describe("GET usage", () => {
    it("says both meters are off, with nothing to show, on a relay with neither service", async () => {
      const relay = await serve({});
      const answer = await call(relay, "GET", "/api/v1/c/acme-rc/usage", { token: ACME_TOKEN });

      assert.equal(answer.status, 200);
      assert.deepEqual(answer.body, {
        ok: true,
        commentary: { enabled: false, used: 0, allowance: 0, topUp: 0, resetsAt: null },
        voice: { enabled: false, used: 0, allowance: 0, topUp: 0, resetsAt: null },
        requests: [],
      });
    });

    it("reports what each meter has used against its plan, and when it starts again", async () => {
      const lines = brain();
      const speech = voice();
      await lines.write("acme-rc", ACME_BRAIN, "sys", "facts", 40, 0.7);
      await lines.write("acme-rc", ACME_BRAIN, "sys", "facts", 40, 0.7);
      await speech.speak("acme-rc", ACME_VOICE, "clubhouse", "Ten chars.", false);

      const relay = await serve({ brain: lines, voice: speech, topUpRequests: requests() });
      const answer = await call(relay, "GET", "/api/v1/c/acme-rc/usage", { token: ACME_TOKEN });

      assert.equal(answer.status, 200);
      assert.deepEqual(answer.body.commentary, { enabled: true, used: 2, allowance: 5, topUp: 0, resetsAt: "2026-10-01T00:00:00.000Z" });
      assert.deepEqual(answer.body.voice, { enabled: true, used: 10, allowance: 1_000, topUp: 0, resetsAt: "2026-10-01T00:00:00.000Z" });
      assert.deepEqual(answer.body.requests, []);
    });

    it("shows a meter the club's plan does not include as off, with no allowance", async () => {
      const relay = await serve();
      const answer = await call(relay, "GET", "/api/v1/c/brookside/usage", { token: BROOK_TOKEN });

      assert.equal(answer.status, 200);
      assert.equal(answer.body.commentary.enabled, false);
      assert.equal(answer.body.commentary.allowance, 0);
      assert.equal(answer.body.commentary.topUp, 0);
      assert.equal(answer.body.voice.enabled, false);
      assert.equal(answer.body.voice.allowance, 0);
      assert.equal(answer.body.voice.resetsAt, "2026-10-01T00:00:00.000Z");
    });

    it("counts each meter's top-up into its allowance", async () => {
      await writeJson(brainTopUpPath(folder, "acme-rc"), { "2026-09": 20, "2026-08": 99 });
      await writeJson(voiceTopUpPath(folder, "acme-rc"), { "2026-09": 500 });

      const relay = await serve();
      const answer = await call(relay, "GET", "/api/v1/c/acme-rc/usage", { token: ACME_TOKEN });

      assert.equal(answer.body.commentary.topUp, 20);
      assert.equal(answer.body.commentary.allowance, 25);
      assert.equal(answer.body.voice.topUp, 500);
      assert.equal(answer.body.voice.allowance, 1_500);
    });

    it("spends nothing, and writes no count, however often it is asked", async () => {
      const relay = await serve();

      for (let i = 0; i < 5; i++) {
        const answer = await call(relay, "GET", "/api/v1/c/acme-rc/usage", { token: ACME_TOKEN });
        assert.equal(answer.body.commentary.used, 0);
        assert.equal(answer.body.voice.used, 0);
      }

      await assert.rejects(stat(brainUsagePath(folder, "acme-rc")));
      await assert.rejects(stat(join(folder, ".voice", "usage", "acme-rc.json")));
    });

    it("answers only the club's own token", async () => {
      const relay = await serve();

      const none = await call(relay, "GET", "/api/v1/c/acme-rc/usage");
      assert.equal(none.status, 401);
      assert.equal(none.body.error, "A publish token is required.");

      const wrong = await call(relay, "GET", "/api/v1/c/acme-rc/usage", { token: BROOK_TOKEN });
      assert.equal(wrong.status, 403);
      assert.equal(wrong.body.error, "That token does not belong to this club.");

      const unknown = await call(relay, "GET", "/api/v1/c/nobody-rc/usage", { token: ACME_TOKEN });
      assert.equal(unknown.status, 404);
    });
  });

  // --- brain top-ups ---------------------------------------------------------------

  describe("a brain top-up", () => {
    it("lets a club that has used its lines write again on its very next line", async () => {
      const lines = new BrainService({
        config: { ...BRAIN_CONFIG, monthlyLines: 1 },
        dataDir: folder,
        write: async () => "A line.",
        now: () => clock,
      });
      const plan: BrainPlan = { enabled: true, monthlyLines: 1 };

      assert.equal((await lines.write("acme-rc", plan, "sys", "facts", 40, 0.7)).ok, true);

      const refused = await lines.write("acme-rc", plan, "sys", "facts", 40, 0.7);
      assert.equal(refused.ok, false);
      assert.equal(!refused.ok && refused.refusal, "allowance");
      assert.equal(!refused.ok && refused.usage?.allowance, 1);

      await writeJson(brainTopUpPath(folder, "acme-rc"), { "2026-09": 2 });

      const again = await lines.write("acme-rc", plan, "sys", "facts", 40, 0.7);
      assert.equal(again.ok, true);
      assert.equal(again.ok && again.usage.used, 2);
      assert.equal(again.ok && again.usage.allowance, 3);

      const catalogue = await lines.catalogue("acme-rc", plan);
      assert.equal(catalogue.usage.allowance, 3);

      const usage = await lines.usage("acme-rc", plan);
      assert.deepEqual(usage, { enabled: true, used: 2, allowance: 3, topUp: 2, resetsAt: "2026-10-01T00:00:00.000Z" });
    });

    it("is this month's only", async () => {
      await writeJson(brainTopUpPath(folder, "acme-rc"), { "2026-09": 40 });
      const lines = brain();

      assert.equal((await lines.usage("acme-rc", ACME_BRAIN)).allowance, 45);

      clock = new Date("2026-10-01T00:00:01.000Z");
      assert.equal((await lines.usage("acme-rc", ACME_BRAIN)).allowance, 5);
    });

    it("is read as no top-up, not no commentary, when the file will not read", async () => {
      await mkdir(dirname(brainTopUpPath(folder, "acme-rc")), { recursive: true });
      await writeFile(brainTopUpPath(folder, "acme-rc"), "{ not json", "utf8");

      const lines = brain();
      assert.equal((await lines.usage("acme-rc", ACME_BRAIN)).allowance, 5);
      assert.equal((await lines.write("acme-rc", ACME_BRAIN, "sys", "facts", 40, 0.7)).ok, true);
    });
  });

  // --- asking for more ------------------------------------------------------------

  describe("POST usage/topup-request", () => {
    it("records an ask, and hands back the same open ask rather than storing it twice", async () => {
      const relay = await serve();

      const first = await call(relay, "POST", "/api/v1/c/acme-rc/usage/topup-request", {
        token: ACME_TOKEN,
        json: { meter: "commentary", note: "Busy weekend" },
      });

      assert.equal(first.status, 200);
      assert.equal(first.body.ok, true);
      assert.equal(first.body.already, false);
      assert.match(first.body.request.id, /^r_[0-9a-f]{24}$/);
      assert.deepEqual(
        { ...first.body.request, id: "" },
        { id: "", meter: "commentary", askedAt: "2026-09-20T12:00:00.000Z", note: "Busy weekend", status: "open" },
      );

      const again = await call(relay, "POST", "/api/v1/c/acme-rc/usage/topup-request", {
        token: ACME_TOKEN,
        json: { meter: "commentary", note: "Asking again" },
      });

      assert.equal(again.status, 200);
      assert.equal(again.body.already, true);
      assert.deepEqual(again.body.request, first.body.request);

      clock = new Date("2026-09-20T13:00:00.000Z");
      const other = await call(relay, "POST", "/api/v1/c/acme-rc/usage/topup-request", { token: ACME_TOKEN, json: { meter: "voice" } });

      assert.equal(other.body.already, false);
      assert.equal(other.body.request.note, "");

      const usage = await call(relay, "GET", "/api/v1/c/acme-rc/usage", { token: ACME_TOKEN });
      assert.deepEqual(
        usage.body.requests.map((ask: { meter: string; status: string }) => [ask.meter, ask.status]),
        [
          ["voice", "open"],
          ["commentary", "open"],
        ],
      );

      // Kept on disk under the month, one entry each.
      const saved = JSON.parse(await readFile(topUpRequestsPath(folder, "acme-rc"), "utf8")) as Record<string, unknown[]>;
      assert.equal(saved["2026-09"]?.length, 2);
    });

    it("keeps a note to 500 characters, on one line, trimmed", async () => {
      const relay = await serve();

      const answer = await call(relay, "POST", "/api/v1/c/acme-rc/usage/topup-request", {
        token: ACME_TOKEN,
        json: { meter: "voice", note: `  Two\nlines\u0007 and a bell. ${"x".repeat(600)}` },
      });

      assert.equal(answer.status, 200);
      assert.equal(answer.body.request.note.length, 500);
      assert.ok(answer.body.request.note.startsWith("Two lines  and a bell. xxx"));
      assert.equal(/[\u0000-\u001f]/.test(answer.body.request.note), false);

      assert.equal(cleanNote(42), "");
      assert.equal(cleanNote("  \t "), "");
      assert.equal(Array.from(cleanNote("😀".repeat(600))).length, 500);
    });

    it("refuses a meter it does not know, and a body it cannot read", async () => {
      const relay = await serve();

      for (const meter of ["brain", "", 7, null, "elevenlabs"]) {
        const answer = await call(relay, "POST", "/api/v1/c/acme-rc/usage/topup-request", { token: ACME_TOKEN, json: { meter } });
        assert.equal(answer.status, 400, String(meter));
      }

      const unreadable = await call(relay, "POST", "/api/v1/c/acme-rc/usage/topup-request", { token: ACME_TOKEN, raw: "not json" });
      assert.equal(unreadable.status, 400);

      await assert.rejects(stat(topUpRequestsPath(folder, "acme-rc")));
    });

    it("answers only the club's own token", async () => {
      const relay = await serve();

      const none = await call(relay, "POST", "/api/v1/c/acme-rc/usage/topup-request", { json: { meter: "voice" } });
      assert.equal(none.status, 401);

      const wrong = await call(relay, "POST", "/api/v1/c/acme-rc/usage/topup-request", { token: BROOK_TOKEN, json: { meter: "voice" } });
      assert.equal(wrong.status, 403);

      await assert.rejects(stat(topUpRequestsPath(folder, "acme-rc")));
    });

    it("takes at most ten a month, and ten more the month after", async () => {
      const relay = await serve();

      for (let i = 0; i < TOP_UP_REQUESTS_PER_MONTH; i++) {
        const answer = await call(relay, "POST", "/api/v1/c/acme-rc/usage/topup-request", { token: ACME_TOKEN, json: { meter: "voice" } });
        assert.equal(answer.status, 200);
        assert.equal(answer.body.already, false);
        // Answered, so the next is a new ask rather than the same one.
        assert.equal(await closeRequests(folder, "acme-rc", "voice", clock, undefined), 1);
      }

      const over = await call(relay, "POST", "/api/v1/c/acme-rc/usage/topup-request", { token: ACME_TOKEN, json: { meter: "voice" } });
      assert.equal(over.status, 429);
      assert.equal(over.body.ok, false);

      clock = new Date("2026-10-02T09:00:00.000Z");
      const next = await call(relay, "POST", "/api/v1/c/acme-rc/usage/topup-request", { token: ACME_TOKEN, json: { meter: "voice" } });
      assert.equal(next.status, 200);

      const usage = await call(relay, "GET", "/api/v1/c/acme-rc/usage", { token: ACME_TOKEN });
      assert.equal(usage.body.requests.length, 1, "only this month's are listed");
    });

    it("hands back an open ask even when the month's asks are all used", async () => {
      const store = requests();

      for (let i = 0; i < TOP_UP_REQUESTS_PER_MONTH - 1; i++) {
        await store.ask("acme-rc", "voice", "");
        await closeRequests(folder, "acme-rc", "voice", clock, undefined);
      }

      const last = await store.ask("acme-rc", "commentary", "");
      assert.equal(last.ok, true);

      const same = await store.ask("acme-rc", "commentary", "");
      assert.equal(same.ok && same.already, true);

      const refused = await store.ask("acme-rc", "voice", "");
      assert.equal(refused.ok, false);
      assert.equal(!refused.ok && refused.status, 429);
    });

    it("stores two asks made at once as one", async () => {
      const store = requests();
      const [a, b] = await Promise.all([store.ask("acme-rc", "voice", ""), store.ask("acme-rc", "voice", "")]);

      assert.equal(a.ok && b.ok, true);
      assert.equal(a.ok && b.ok && a.request.id === b.request.id, true);
      assert.equal((await store.list("acme-rc")).length, 1);
    });

    it("never writes over a requests file that will not read", async () => {
      await mkdir(dirname(topUpRequestsPath(folder, "acme-rc")), { recursive: true });
      await writeFile(topUpRequestsPath(folder, "acme-rc"), "{ broken", "utf8");

      const relay = await serve();
      const answer = await call(relay, "POST", "/api/v1/c/acme-rc/usage/topup-request", { token: ACME_TOKEN, json: { meter: "voice" } });

      assert.equal(answer.status, 503);
      assert.equal(await readFile(topUpRequestsPath(folder, "acme-rc"), "utf8"), "{ broken");
    });

    it("says asking is unavailable on a relay that keeps no requests", async () => {
      const relay = await serve({ brain: brain(), voice: voice() });
      const answer = await call(relay, "POST", "/api/v1/c/acme-rc/usage/topup-request", { token: ACME_TOKEN, json: { meter: "voice" } });

      assert.equal(answer.status, 503);
    });
  });

  // --- the manager's phone ----------------------------------------------------------

  describe("the manager's phone", () => {
    async function signedIn(relay: Harness): Promise<string> {
      const published = await call(relay, "POST", "/api/v1/c/acme-rc/publish/manage?seq=1", {
        token: ACME_TOKEN,
        json: { enabled: true, pinSha256: managePinHash("acme-rc", "654321") },
      });
      assert.equal(published.status, 200);

      const answer = await call(relay, "POST", "/api/v1/c/acme-rc/manage/signin", { json: { pin: "654321" } });
      assert.equal(answer.status, 200);
      return answer.body.pass as string;
    }

    it("reads the same usage as the laptop, with its pass", async () => {
      await writeJson(brainTopUpPath(folder, "acme-rc"), { "2026-09": 10 });
      const relay = await serve();
      const pass = await signedIn(relay);

      const phone = await call(relay, "POST", "/api/v1/c/acme-rc/manage/usage", { json: { pass } });
      const laptop = await call(relay, "GET", "/api/v1/c/acme-rc/usage", { token: ACME_TOKEN });

      assert.equal(phone.status, 200);
      assert.deepEqual(phone.body, laptop.body);
      assert.equal(phone.body.commentary.allowance, 15);
    });

    it("asks for more, and the laptop sees the same ask", async () => {
      const relay = await serve();
      const pass = await signedIn(relay);

      const asked = await call(relay, "POST", "/api/v1/c/acme-rc/manage/topup-request", { json: { pass, meter: "commentary", note: "From the phone" } });
      assert.equal(asked.status, 200);
      assert.equal(asked.body.already, false);

      const fromLaptop = await call(relay, "POST", "/api/v1/c/acme-rc/usage/topup-request", { token: ACME_TOKEN, json: { meter: "commentary" } });
      assert.equal(fromLaptop.body.already, true);
      assert.equal(fromLaptop.body.request.id, asked.body.request.id);

      const bad = await call(relay, "POST", "/api/v1/c/acme-rc/manage/topup-request", { json: { pass, meter: "lots" } });
      assert.equal(bad.status, 400);
    });

    it("is refused without the right pass", async () => {
      const relay = await serve();
      await signedIn(relay);

      for (const body of [{}, { pass: "0".repeat(64) }, { pass: "not a pass" }]) {
        assert.equal((await call(relay, "POST", "/api/v1/c/acme-rc/manage/usage", { json: body })).status, 403);
        assert.equal((await call(relay, "POST", "/api/v1/c/acme-rc/manage/topup-request", { json: { ...body, meter: "voice" } })).status, 403);
      }

      await assert.rejects(stat(topUpRequestsPath(folder, "acme-rc")));
    });

    it("is refused once the desk has turned the manager off", async () => {
      const relay = await serve();
      const pass = await signedIn(relay);

      await call(relay, "POST", "/api/v1/c/acme-rc/publish/manage?seq=2", {
        token: ACME_TOKEN,
        json: { enabled: false, pinSha256: managePinHash("acme-rc", "654321") },
      });

      assert.equal((await call(relay, "POST", "/api/v1/c/acme-rc/manage/usage", { json: { pass } })).status, 403);
    });

    it("has a This month block on the Today tab, and a script that still parses", async () => {
      const relay = await serve();
      const page = await call(relay, "GET", "/c/acme-rc/manage");
      const script = /<script[^>]*>([\s\S]*)<\/script>/.exec(page.text)?.[1] ?? "";

      assert.match(page.text, /id="tab-today"[\s\S]*id="usageBox"[\s\S]*id="tab-entries"/);
      assert.match(page.text, /This month/);
      assert.match(script, /api\('usage'/);
      assert.match(script, /api\('topup-request'/);
      assert.match(script, /support@heatcaller\.com/);
      assert.doesNotThrow(() => new Script(script));
    });
  });

  // --- the operator's commands ------------------------------------------------------

  describe("the operator's commands", () => {
    function env(): NodeJS.ProcessEnv {
      return {
        RELAY_DATA: folder,
        RELAY_CONFIG_JSON: JSON.stringify({
          voice: { monthlyChars: 1000, voices: [{ id: "clubhouse", voiceId: "el-voice-clubhouse" }] },
          brain: { model: "test-model", monthlyLines: 5 },
          clubs: [
            {
              club: "acme-rc",
              name: "Acme RC",
              tokenSha256: hashToken(ACME_TOKEN),
              voice: { enabled: true, monthlyChars: 1000 },
              brain: { enabled: true, monthlyLines: 5 },
            },
            { club: "brookside", name: "Brookside", tokenSha256: hashToken(BROOK_TOKEN) },
          ],
        }),
      };
    }

    async function run(command: AdminCommand, args: string[]): Promise<{ code: number; out: string; err: string }> {
      let out = "";
      let err = "";
      const code = await voiceAdmin(command, args, env(), clock, (text) => (out += text), (text) => (err += text));
      return { code, out, err };
    }

    it("brain-usage lists every club with commentary, and what it has used", async () => {
      const lines = brain();
      await lines.write("acme-rc", ACME_BRAIN, "sys", "facts", 40, 0.7);
      await lines.write("acme-rc", ACME_BRAIN, "sys", "facts", 40, 0.7);

      const report = await run("brain-usage", []);
      assert.equal(report.code, 0, report.err);
      assert.match(report.out, /AI commentary - September 2026/);
      assert.match(report.out, /acme-rc\s+on\s+5\s+-\s+2\s+3\s+40%/);
      assert.doesNotMatch(report.out, /brookside/);
      assert.match(report.out, /All clubs: 2 lines this month/);

      const json = await run("brain-usage", ["--json"]);
      const parsed = JSON.parse(json.out) as { clubs: { club: string; used: number; allowance: number }[] };
      assert.equal(parsed.clubs.find((club) => club.club === "acme-rc")?.used, 2);

      assert.equal((await run("brain-usage", ["--verbose"])).code, 1);
    });

    it("brain-topup gives lines for this month, and the relay counts them at once", async () => {
      const lines = brain();
      assert.equal((await lines.usage("acme-rc", ACME_BRAIN)).allowance, 5);

      const given = await run("brain-topup", ["acme-rc", "100"]);
      assert.equal(given.code, 0, given.err);
      assert.match(given.out, /acme-rc: 100 lines on top of its plan for September 2026/);
      assert.deepEqual(JSON.parse(await readFile(brainTopUpPath(folder, "acme-rc"), "utf8")), { "2026-09": 100 });
      assert.equal((await lines.usage("acme-rc", ACME_BRAIN)).allowance, 105);

      const back = await run("brain-topup", ["acme-rc", "-30"]);
      assert.equal(back.code, 0);
      assert.equal((await lines.usage("acme-rc", ACME_BRAIN)).allowance, 75);

      const off = await run("brain-topup", ["brookside", "10"]);
      assert.equal(off.code, 0);
      assert.match(off.out, /AI commentary is not switched on for brookside/);

      assert.equal((await run("brain-topup", ["acme-rc"])).code, 1);
      assert.equal((await run("brain-topup", ["acme-rc", "0"])).code, 1);
      assert.equal((await run("brain-topup", ["acme-rc", "1000001"])).code, 1);
      assert.equal((await run("brain-topup", ["nobody-rc", "10"])).code, 1);
    });

    it("topup-requests lists the open asks of every club, with the note", async () => {
      clock = new Date("2026-09-20T12:00:00.000Z");
      await requests().ask("acme-rc", "commentary", "Busy weekend");
      clock = new Date("2026-09-21T08:30:00.000Z");
      await requests().ask("brookside", "voice", "");

      const listed = await run("topup-requests", []);
      assert.equal(listed.code, 0, listed.err);
      const rows = listed.out.split("\n").filter((line) => /acme-rc|brookside/.test(line));
      assert.equal(rows.length, 2);
      assert.match(rows[0] ?? "", /acme-rc\s+commentary\s+2026-09-20 12:00:00 UTC\s+"Busy weekend"/);
      assert.match(rows[1] ?? "", /brookside\s+voice\s+2026-09-21 08:30:00 UTC/);

      const json = JSON.parse((await run("topup-requests", ["--json"])).out) as { open: { club: string }[] };
      assert.deepEqual(json.open.map((row) => row.club), ["acme-rc", "brookside"]);
    });

    it("a top-up marks the club's open ask for that meter done, and only that one", async () => {
      const relay = await serve();
      await call(relay, "POST", "/api/v1/c/acme-rc/usage/topup-request", { token: ACME_TOKEN, json: { meter: "commentary" } });
      await call(relay, "POST", "/api/v1/c/acme-rc/usage/topup-request", { token: ACME_TOKEN, json: { meter: "voice" } });

      // Taking some back answers nobody's ask.
      await run("brain-topup", ["acme-rc", "50"]);
      await run("brain-topup", ["acme-rc", "-10"]);
      const given = await run("brain-topup", ["acme-rc", "10"]);
      assert.equal(given.out.includes("request"), false, "the ask was already done");

      let usage = await call(relay, "GET", "/api/v1/c/acme-rc/usage", { token: ACME_TOKEN });
      const status = (meter: string) => usage.body.requests.find((ask: { meter: string }) => ask.meter === meter)?.status;
      assert.equal(status("commentary"), "done");
      assert.equal(status("voice"), "open");
      assert.equal(usage.body.commentary.allowance, 55);

      const left = await run("topup-requests", []);
      assert.doesNotMatch(left.out, /commentary/);
      assert.match(left.out, /acme-rc\s+voice/);

      const voiceGiven = await run("voice-topup", ["acme-rc", "5000"]);
      assert.equal(voiceGiven.code, 0, voiceGiven.err);
      assert.match(voiceGiven.out, /Marked acme-rc's request for more characters done/);

      usage = await call(relay, "GET", "/api/v1/c/acme-rc/usage", { token: ACME_TOKEN });
      assert.equal(status("voice"), "done");
      assert.equal(usage.body.voice.allowance, 6000);
      assert.match((await run("topup-requests", [])).out, /None\./);

      // Answered, so asking again is a new ask - and the done file was
      // written by the command alone, never the requests file.
      const again = await call(relay, "POST", "/api/v1/c/acme-rc/usage/topup-request", { token: ACME_TOKEN, json: { meter: "voice" } });
      assert.equal(again.body.already, false);
      const done = JSON.parse(await readFile(topUpDonePath(folder, "acme-rc"), "utf8")) as Record<string, string>;
      assert.equal(Object.keys(done).length, 2);
    });
  });
});
