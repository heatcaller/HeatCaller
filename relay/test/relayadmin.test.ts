import assert from "node:assert/strict";
import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { after, afterEach, beforeEach, describe, it } from "node:test";
import { brainUsagePath } from "../src/brain.js";
import { loadConfig } from "../src/config.js";
import { hashToken, isActive, TenantRegistry } from "../src/tenancy.js";
import { TopUpRequests } from "../src/topuprequests.js";
import { voiceAdmin, type AdminCommand } from "../src/voiceadmin.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, tenants, type Harness } from "./helpers.js";

// What the relay admin app needs from the relay (24 Oct 2026): a club can
// be paused rather than removed, and the app can read every club in one
// command and answer a club's ask for more without giving any.

describe("relay admin: pausing a club", () => {
  describe("the config", () => {
    const base = {
      voice: { monthlyChars: 100, voices: [{ id: "clubhouse", voiceId: "el-voice-clubhouse" }] },
    };

    it("reads active: false as paused, and a club that says nothing as active", async () => {
      const config = await loadConfig("unused", {
        RELAY_CONFIG_JSON: JSON.stringify({
          ...base,
          clubs: [
            { club: "acme-rc", name: "Acme RC", tokenSha256: hashToken(ACME_TOKEN), active: false },
            { club: "brookside", name: "Brookside", tokenSha256: hashToken(BROOK_TOKEN) },
          ],
        }),
      });

      assert.equal(isActive(config.tenants[0]!), false);
      assert.equal(isActive(config.tenants[1]!), true);
      assert.equal("active" in config.tenants[1]!, false, "an active club carries no flag at all");
    });

    it("refuses to start on a hand-typed \"false\" rather than reading it as switched on", async () => {
      await assert.rejects(
        loadConfig("unused", {
          RELAY_CONFIG_JSON: JSON.stringify({
            ...base,
            clubs: [{ club: "acme-rc", tokenSha256: hashToken(ACME_TOKEN), active: "false" }],
          }),
        }),
        /"active" for acme-rc .* must be true or false/,
      );
    });
  });

  describe("the token", () => {
    it("names nobody while the club is paused, though the club is still known by its id", () => {
      const registry = tenants([{ active: false }]);

      assert.equal(registry.resolveToken(ACME_TOKEN), undefined);
      assert.equal(registry.club("acme-rc")?.name, "Acme RC");
      assert.equal(registry.resolveToken(BROOK_TOKEN)?.club, "brookside");
    });

    it("works again the moment the club is switched back on, with the same token", () => {
      assert.equal(tenants([{ active: true }]).resolveToken(ACME_TOKEN)?.club, "acme-rc");
    });
  });

  describe("the routes", () => {
    let harness: Harness;

    after(async () => {
      await harness?.close();
    });

    it("refuses a paused club's publish with a reason the laptop can show", async () => {
      harness = await start(undefined, tenants([{ active: false }]));

      const answer = await call(harness, "POST", "/api/v1/c/acme-rc/publish/state?seq=1", {
        token: ACME_TOKEN,
        json: { flag: "green" },
      });

      assert.equal(answer.status, 403);
      assert.equal(answer.body.paused, true);
      assert.match(answer.body.error, /paused/);
    });

    it("refuses every route under the club, with or without a token", async () => {
      const answer = await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/state");

      assert.equal(answer.status, 403);
      assert.equal(answer.body.paused, true);
    });

    it("shows one paused notice for the club's pages, with its name and no script", async () => {
      for (const path of ["/c/acme-rc", "/c/acme-rc/results", "/c/acme-rc/live"]) {
        const answer = await call(harness, "GET", path);

        assert.equal(answer.status, 403, path);
        assert.match(answer.text, /Acme RC/, path);
        assert.match(answer.text, /paused/, path);
        assert.doesNotMatch(answer.text, /<script/i, path);
      }
    });

    it("escapes the club's name on the paused page", async () => {
      const where = await start(
        undefined,
        new TenantRegistry([
          { club: "acme-rc", name: "<b>Acme</b>", tokenSha256: hashToken(ACME_TOKEN), publishRoster: false, active: false },
        ]),
      );

      try {
        const answer = await call(where, "GET", "/c/acme-rc");

        assert.doesNotMatch(answer.text, /<b>Acme<\/b>/);
        assert.match(answer.text, /&lt;b&gt;Acme&lt;\/b&gt;/);
      } finally {
        await where.close();
      }
    });

    it("leaves every other club exactly as it was", async () => {
      const published = await call(harness, "POST", "/api/v1/c/brookside/publish/state?seq=1", {
        token: BROOK_TOKEN,
        json: { flag: "green" },
      });

      assert.equal(published.status, 200);
      assert.equal((await call(harness, "GET", "/c/brookside")).status, 200);
    });
  });
});

describe("relay admin: the admin commands", () => {
  let folder = "";
  const clock = new Date("2026-10-24T12:00:00.000Z");

  beforeEach(async () => {
    folder = await mkdtemp(join(tmpdir(), "relay-admin-"));
  });

  afterEach(async () => {
    await rm(folder, { recursive: true, force: true });
  });

  function env(extra: NodeJS.ProcessEnv = {}): NodeJS.ProcessEnv {
    return {
      RELAY_DATA: folder,
      RELAY_CONFIG_JSON: JSON.stringify({
        voice: { monthlyChars: 100, voices: [{ id: "clubhouse", voiceId: "el-voice-clubhouse" }] },
        brain: { monthlyLines: 50 },
        clubs: [
          { club: "acme-rc", name: "Acme RC", tokenSha256: hashToken(ACME_TOKEN), voice: { enabled: true, monthlyChars: 100 }, brain: { enabled: true, monthlyLines: 40 } },
          { club: "brookside", name: "Brookside", tokenSha256: hashToken(BROOK_TOKEN), publishRoster: true, active: false },
        ],
      }),
      ...extra,
    };
  }

  // No test reaches ElevenLabs: with a key set, admin-snapshot asks this instead.
  const offline: typeof fetch = async () => {
    throw new Error("no network in the tests");
  };

  async function run(command: AdminCommand, args: string[], environment = env(), fetchImpl = offline): Promise<{ code: number; out: string; err: string }> {
    let out = "";
    let err = "";
    const code = await voiceAdmin(command, args, environment, clock, (text) => (out += text), (text) => (err += text), fetchImpl);
    return { code, out, err };
  }

  async function ask(club: string, meter: "voice" | "commentary", note: string): Promise<void> {
    const requests = new TopUpRequests({ dataDir: folder, now: () => clock });
    const result = await requests.ask(club, meter, note);
    assert.equal(result.ok, true);
  }

  describe("admin-snapshot", () => {
    it("gives every club, both meters and the open asks in one JSON answer", async () => {
      await ask("acme-rc", "voice", "Big race on Saturday");
      assert.equal((await run("voice-topup", ["acme-rc", "25"])).code, 0);
      await ask("acme-rc", "commentary", "More recaps please");

      const { code, out } = await run("admin-snapshot", ["--json"]);
      const snapshot = JSON.parse(out);

      assert.equal(code, 0);
      assert.equal(snapshot.version, 1);
      assert.equal(snapshot.month, "2026-10");
      assert.equal(snapshot.clubs.length, 2);

      const acme = snapshot.clubs[0];
      assert.equal(acme.club, "acme-rc");
      assert.equal(acme.active, true);
      assert.deepEqual(
        { enabled: acme.voice.enabled, plan: acme.voice.plan, topUp: acme.voice.topUp, allowance: acme.voice.allowance },
        { enabled: true, plan: 100, topUp: 25, allowance: 125 },
      );
      assert.deepEqual(
        { enabled: acme.commentary.enabled, plan: acme.commentary.plan },
        { enabled: true, plan: 40 },
      );

      const brook = snapshot.clubs[1];
      assert.equal(brook.active, false);
      assert.equal(brook.publishRoster, true);
      assert.equal(brook.voice.enabled, false);

      // The voice ask was answered by the top-up; the commentary one is open.
      assert.deepEqual(
        snapshot.requests.map((request: { club: string; meter: string; note: string }) => [request.club, request.meter, request.note]),
        [["acme-rc", "commentary", "More recaps please"]],
      );
    });

    // Bug hunt 15: a usage file that will not read is known: false, not a quiet month of zeros.
    it("marks a meter whose usage file will not read as unknown", async () => {
      await mkdir(dirname(brainUsagePath(folder, "acme-rc")), { recursive: true });
      await writeFile(brainUsagePath(folder, "acme-rc"), "{ broken");

      const snapshot = JSON.parse((await run("admin-snapshot", ["--json"])).out);
      assert.equal(snapshot.clubs[0].commentary.known, false);
      assert.equal(snapshot.clubs[0].voice.known, undefined, "no voice file at all is a known zero");
      assert.equal(snapshot.clubs[1].commentary.known, undefined);

      const report = await run("brain-usage", []);
      assert.match(report.out, /acme-rc\s+on\s+40\s+-\s+\?/);
    });

    it("answers JSON even without --json, since it is read by the app", async () => {
      const { code, out } = await run("admin-snapshot", []);

      assert.equal(code, 0);
      assert.equal(JSON.parse(out).version, 1);
    });

    it("says whether each key is set and never what it is", async () => {
      const { out } = await run("admin-snapshot", ["--json"], env({ RELAY_VOICE_KEY: "sk_secret_voice", RELAY_BRAIN_KEY: "" }));
      const snapshot = JSON.parse(out);

      assert.equal(snapshot.relay.voice.keySet, true);
      assert.equal(snapshot.relay.commentary.keySet, false);
      assert.doesNotMatch(out, /sk_secret_voice/);
    });

    it("says there is no ElevenLabs account to read when the voice key isn't set", async () => {
      const snapshot = JSON.parse((await run("admin-snapshot", ["--json"])).out);
      assert.deepEqual(snapshot.voiceAccount, { state: "no-key" });
    });

    it("reads the relay's ElevenLabs account with its own key, and passes on only the counts", async () => {
      const asked: { url: string; key: string | null }[] = [];
      const elevenLabs: typeof fetch = async (url, init) => {
        asked.push({ url: String(url), key: new Headers(init?.headers).get("xi-api-key") });
        return Response.json({
          tier: "creator",
          status: "active",
          character_count: 61_000,
          character_limit: 100_000,
          next_character_count_reset_unix: 1_793_491_200,
          max_credit_limit_extension: 0,
          first_name: "Asher",
          next_invoice: { amount_due_cents: 2200 },
        });
      };

      const { out } = await run("admin-snapshot", ["--json"], env({ RELAY_VOICE_KEY: "sk_secret_voice" }), elevenLabs);
      const snapshot = JSON.parse(out);

      assert.deepEqual(asked, [{ url: "https://api.elevenlabs.io/v1/user/subscription", key: "sk_secret_voice" }]);
      assert.deepEqual(snapshot.voiceAccount, {
        state: "ok",
        account: { tier: "creator", status: "active", used: 61_000, limit: 100_000, resetsAt: "2026-11-01T00:00:00.000Z", extension: 0 },
      });
      assert.doesNotMatch(out, /sk_secret_voice|Asher|amount_due/);
    });

    it("still answers, with the clubs, when ElevenLabs can't be reached", async () => {
      const { code, out } = await run("admin-snapshot", ["--json"], env({ RELAY_VOICE_KEY: "sk_secret_voice" }));
      const snapshot = JSON.parse(out);

      assert.equal(code, 0);
      assert.deepEqual(snapshot.voiceAccount, { state: "failed" });
      assert.equal(snapshot.clubs.length, 2);
    });

    it("refuses anything but --json", async () => {
      const { code, err } = await run("admin-snapshot", ["--all"]);

      assert.equal(code, 1);
      assert.match(err, /nothing but --json/);
    });
  });

  describe("topup-decline", () => {
    it("answers the open asks for that meter without giving anything", async () => {
      await ask("acme-rc", "voice", "Please");
      await ask("acme-rc", "commentary", "Also please");

      const { code, out } = await run("topup-decline", ["acme-rc", "voice", "--json"]);

      assert.equal(code, 0);
      assert.deepEqual(JSON.parse(out), { ok: true, club: "acme-rc", meter: "voice", closed: 1 });

      const snapshot = JSON.parse((await run("admin-snapshot", [])).out);
      assert.equal(snapshot.clubs[0].voice.topUp, 0, "nothing was given");
      assert.deepEqual(
        snapshot.requests.map((request: { meter: string }) => request.meter),
        ["commentary"],
        "only the voice ask was answered",
      );
    });

    it("says so when there is nothing to answer", async () => {
      const { code, out } = await run("topup-decline", ["acme-rc", "commentary"]);

      assert.equal(code, 0);
      assert.match(out, /no open request/);
    });

    it("refuses a club the relay does not have, and a meter that does not exist", async () => {
      assert.match((await run("topup-decline", ["nobody-rc", "voice"])).err, /no club "nobody-rc"/);
      assert.match((await run("topup-decline", ["acme-rc", "brain"])).err, /Usage: topup-decline/);
    });
  });

  describe("--json on the top-ups", () => {
    it("gives the result the app reads instead of the sentence", async () => {
      const { code, out } = await run("brain-topup", ["acme-rc", "10", "--json"]);
      const result = JSON.parse(out);

      assert.equal(code, 0);
      assert.equal(result.ok, true);
      assert.equal(result.club, "acme-rc");
      assert.equal(result.meter, "commentary");
      assert.equal(result.topUp, 10);
      assert.equal(result.allowance, 50);
    });

    it("still prints the sentence without it, as the .cmd scripts expect", async () => {
      const { out } = await run("voice-topup", ["acme-rc", "5"]);

      assert.match(out, /^acme-rc: 5 characters on top of its plan/);
    });
  });
});

describe("relay admin: check-config", () => {
  // Run as the app runs it, as its own process: index.ts starts a relay
  // when it is imported with no command, so it is never imported here.
  //
  // WHY (24 Oct 2026): fileURLToPath, not the URL's pathname. On Windows
  // the pathname is "/C:/Users/...", which node cannot open, so the child
  // printed nothing and all three tests failed there only.
  const index = fileURLToPath(new URL("../src/index.js", import.meta.url));
  let folder = "";

  beforeEach(async () => {
    folder = await mkdtemp(join(tmpdir(), "relay-check-"));
  });

  afterEach(async () => {
    await rm(folder, { recursive: true, force: true });
  });

  async function check(config: unknown, env: NodeJS.ProcessEnv = {}): Promise<{ code: number; answer: any }> {
    const { execFile } = await import("node:child_process");
    const { writeFile } = await import("node:fs/promises");
    const path = join(folder, "relay.config.json");

    await writeFile(path, JSON.stringify(config), "utf8");

    return new Promise((resolve, reject) => {
      // SystemRoot too: some of what node does at start needs it on Windows.
      // Nothing else of this machine's environment, so the config secret is
      // never read from it.
      const base = { PATH: process.env["PATH"], SystemRoot: process.env["SystemRoot"] };

      execFile(process.execPath, [index, "check-config", path], { env: { ...base, ...env } }, (error, stdout, stderr) => {
        // An empty answer says why, rather than failing as bad JSON.
        if (stdout.trim() === "") {
          reject(new Error(`check-config printed nothing: ${String(stderr).trim()}`));
          return;
        }

        resolve({ code: error === null ? 0 : (error.code as number), answer: JSON.parse(stdout) });
      });
    });
  }

  it("passes a config the relay would start on, counting the paused clubs out of active", async () => {
    const { code, answer } = await check({
      clubs: [
        { club: "acme-rc", tokenSha256: hashToken(ACME_TOKEN), active: false },
        { club: "brookside", tokenSha256: hashToken(BROOK_TOKEN) },
      ],
    });

    assert.equal(code, 0);
    assert.deepEqual(answer, { ok: true, clubs: 2, active: 1 });
  });

  it("fails one the relay would refuse, with the relay's own reason", async () => {
    const { code, answer } = await check({
      clubs: [
        { club: "acme-rc", tokenSha256: hashToken(ACME_TOKEN) },
        { club: "brookside", tokenSha256: hashToken(ACME_TOKEN) },
      ],
    });

    assert.equal(code, 1);
    assert.equal(answer.ok, false);
    assert.match(answer.error, /share a publish token/);
  });

  it("reads the file it is given, never the config secret", async () => {
    const { answer } = await check(
      { clubs: [{ club: "acme-rc", tokenSha256: hashToken(ACME_TOKEN) }] },
      { RELAY_CONFIG_JSON: JSON.stringify({ clubs: [] }) },
    );

    assert.deepEqual(answer, { ok: true, clubs: 1, active: 1 });
  });
});
