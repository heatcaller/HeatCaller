import assert from "node:assert/strict";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { hashToken } from "../src/tenancy.js";
import { closeRequests, topUpDonePath, TopUpRequests } from "../src/topuprequests.js";
import { readTopUps, voiceTopUpPath } from "../src/voice.js";
import { voiceAdmin, type AdminCommand } from "../src/voiceadmin.js";

// 6 Nov 2026, Bug hunt 11: the operator's commands must never write over a
// top-up file or an answered-requests ("done") file that is there but will
// not read. Writing over the first lost every top-up given before; writing
// over the second opened again every request answered before.

describe("Bug hunt 11: top-up files that will not read", () => {
  let folder = "";
  const clock = new Date("2026-11-06T12:00:00.000Z");

  beforeEach(async () => {
    folder = await mkdtemp(join(tmpdir(), "relay-bh11-topup-"));
  });

  afterEach(async () => {
    await rm(folder, { recursive: true, force: true });
  });

  function env(): NodeJS.ProcessEnv {
    return {
      RELAY_DATA: folder,
      RELAY_CONFIG_JSON: JSON.stringify({
        voice: { monthlyChars: 100, voices: [{ id: "clubhouse", voiceId: "el-voice-clubhouse" }] },
        clubs: [{ club: "acme-rc", name: "Acme RC", tokenSha256: hashToken("acme-token-aaaaaaaa"), voice: { enabled: true, monthlyChars: 100 } }],
      }),
    };
  }

  async function run(command: AdminCommand, args: string[]): Promise<{ code: number; out: string; err: string }> {
    let out = "";
    let err = "";
    const code = await voiceAdmin(command, args, env(), clock, (text) => (out += text), (text) => (err += text));
    return { code, out, err };
  }

  async function put(path: string, text: string): Promise<void> {
    await mkdir(dirname(path), { recursive: true });
    await writeFile(path, text, "utf8");
  }

  function requests(): TopUpRequests {
    return new TopUpRequests({ dataDir: folder, now: () => clock });
  }

  describe("voice-topup", () => {
    it("leaves a top-up file that will not read as it is, and says so", async () => {
      const path = voiceTopUpPath(folder, "acme-rc");
      const broken = '{"2026-11": 50000, "2026-10": 2000';
      await put(path, broken);

      const result = await run("voice-topup", ["acme-rc", "10000"]);

      assert.equal(result.code, 1);
      assert.equal(result.out, "");
      assert.match(result.err, /will not read/);
      assert.ok(result.err.includes(path), result.err);
      assert.equal(await readFile(path, "utf8"), broken);
    });

    it("leaves a top-up file that is JSON but not one alone too", async () => {
      const path = voiceTopUpPath(folder, "acme-rc");
      await put(path, "[1, 2, 3]");

      const result = await run("voice-topup", ["acme-rc", "10000"]);

      assert.equal(result.code, 1);
      assert.equal(await readFile(path, "utf8"), "[1, 2, 3]");
    });

    it("still starts a top-up file when there is none, and adds to one that reads", async () => {
      const path = voiceTopUpPath(folder, "acme-rc");

      assert.equal((await run("voice-topup", ["acme-rc", "500"])).code, 0);
      assert.deepEqual(readTopUps(JSON.parse(await readFile(path, "utf8")) as unknown), { "2026-11": 500 });

      await put(path, JSON.stringify({ "2026-11": 50000, "2026-10": 2000 }));
      assert.equal((await run("voice-topup", ["acme-rc", "10000"])).code, 0);
      assert.deepEqual(readTopUps(JSON.parse(await readFile(path, "utf8")) as unknown), { "2026-11": 60000, "2026-10": 2000 });
    });

    it("gives nothing, and leaves the done file alone, when the ask it answers cannot be marked done", async () => {
      const store = requests();
      const ask = await store.ask("acme-rc", "voice", "Big meeting");
      assert.equal(ask.ok, true);

      const donePath = topUpDonePath(folder, "acme-rc");
      await put(donePath, "{not json");

      const result = await run("voice-topup", ["acme-rc", "10000"]);

      assert.equal(result.code, 1);
      assert.ok(result.err.includes(donePath), result.err);
      assert.match(result.err, /No top-up was given/);
      assert.equal(await readFile(donePath, "utf8"), "{not json");
      await assert.rejects(readFile(voiceTopUpPath(folder, "acme-rc"), "utf8"), { code: "ENOENT" });
    });

    it("still marks the ask done when there is no done file yet", async () => {
      const store = requests();
      await store.ask("acme-rc", "voice", "");

      const result = await run("voice-topup", ["acme-rc", "10000"]);

      assert.equal(result.code, 0, result.err);
      assert.match(result.out, /Marked acme-rc's request for more characters done/);
      assert.equal((await store.list("acme-rc"))[0]?.status, "done");
    });
  });

  describe("closing requests", () => {
    it("throws, writing nothing, when the done file will not read", async () => {
      const store = requests();
      await store.ask("acme-rc", "voice", "");

      const donePath = topUpDonePath(folder, "acme-rc");
      await put(donePath, '{"r_earlier": "2026-11-01T10:00:00.000Z"');

      await assert.rejects(closeRequests(folder, "acme-rc", "voice", clock, undefined), (error: Error) => error.message.includes(donePath));
      assert.equal(await readFile(donePath, "utf8"), '{"r_earlier": "2026-11-01T10:00:00.000Z"');
    });

    it("topup-decline reports it and fails, rather than claiming the ask answered", async () => {
      const store = requests();
      await store.ask("acme-rc", "voice", "");

      const donePath = topUpDonePath(folder, "acme-rc");
      await put(donePath, "{oops");

      const result = await run("topup-decline", ["acme-rc", "voice"]);

      assert.equal(result.code, 1);
      assert.equal(result.out, "");
      assert.ok(result.err.includes(donePath), result.err);
      assert.equal(await readFile(donePath, "utf8"), "{oops");
    });

    it("still closes asks when there is no done file, keeping those answered earlier", async () => {
      const store = requests();
      await store.ask("acme-rc", "voice", "");
      assert.equal(await closeRequests(folder, "acme-rc", "voice", clock, undefined), 1);

      await store.ask("acme-rc", "voice", "again");
      const declined = await run("topup-decline", ["acme-rc", "voice"]);
      assert.equal(declined.code, 0, declined.err);

      const done = JSON.parse(await readFile(topUpDonePath(folder, "acme-rc"), "utf8")) as Record<string, string>;
      assert.equal(Object.keys(done).length, 2);
      assert.ok((await store.list("acme-rc")).every((ask) => ask.status === "done"));
    });
  });
});
