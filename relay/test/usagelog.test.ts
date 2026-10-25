import assert from "node:assert/strict";
import { mkdir, mkdtemp, readdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { after, afterEach, beforeEach, describe, it } from "node:test";
import { anthropicWriter, BrainService, type BrainConfig, type WriteFn } from "../src/brain.js";
import { MemoryStore } from "../src/store.js";
import { hashToken } from "../src/tenancy.js";
import {
  pruneUsageLog,
  readUsageLog,
  USAGE_LOG_DIR,
  usageLogPath,
  UsageLog,
  type UsageEvent,
} from "../src/usagelog.js";
import { VoiceService, type CatalogueVoice, type RenderFn } from "../src/voice.js";
import { voiceAdmin } from "../src/voiceadmin.js";
import { ACME_TOKEN, BROOK_TOKEN, start, tenants, type Harness } from "./helpers.js";

// The usage log (24 Oct 2026, relay admin round 3): one line per voice and
// commentary request, for the admin app's charts - counts and labels,
// never the words.

const scratch: string[] = [];

async function folder(): Promise<string> {
  const dir = await mkdtemp(join(tmpdir(), "relay-usagelog-"));
  scratch.push(dir);
  return dir;
}

after(async () => {
  for (const dir of scratch) {
    await rm(dir, { recursive: true, force: true });
  }
});

function event(at: string, extra: Partial<UsageEvent> = {}): UsageEvent {
  return { at, club: "acme-rc", meter: "voice", kind: "line", units: 10, outcome: "ok", ms: 5, ...extra };
}

async function lines(dir: string, month: string): Promise<any[]> {
  const text = await readFile(usageLogPath(dir, month), "utf8");
  return text.trim().split("\n").map((line) => JSON.parse(line));
}

describe("the usage log: writing", () => {
  it("appends one line per event to the month's file", async () => {
    const dir = await folder();
    const log = new UsageLog({ dataDir: dir });

    await log.record(event("2026-10-24T20:00:00.000Z"));
    await log.record(event("2026-10-24T20:00:01.000Z", { meter: "commentary", units: 1, in: 500, out: 60, model: "haiku" }));
    await log.record(event("2026-11-01T00:00:00.000Z"));

    assert.equal((await lines(dir, "2026-10")).length, 2);
    assert.equal((await lines(dir, "2026-11")).length, 1);

    const { id, ...rest } = (await lines(dir, "2026-10"))[1];
    assert.match(id, /^[0-9a-f]{8}-[0-9a-z]+$/);
    assert.deepEqual(rest, {
      at: "2026-10-24T20:00:01.000Z",
      club: "acme-rc",
      meter: "commentary",
      kind: "line",
      units: 1,
      outcome: "ok",
      ms: 5,
      model: "haiku",
      in: 500,
      out: 60,
    });
  });

  it("gives two otherwise identical events different ids", async () => {
    const dir = await folder();
    const log = new UsageLog({ dataDir: dir });
    const same = event("2026-10-24T20:00:00.000Z", { outcome: "allowance", units: 0, ms: 0 });

    await log.record(same);
    await log.record(same);

    const [a, b] = await lines(dir, "2026-10");
    assert.notEqual(a.id, b.id);
    assert.deepEqual({ ...a, id: "" }, { ...b, id: "" });
  });

  it("keeps only the fields it knows - never words that slipped into an event", async () => {
    const dir = await folder();

    await new UsageLog({ dataDir: dir }).record({
      ...event("2026-10-24T20:00:00.000Z"),
      text: "Dave Smith takes the lead",
      units: -4,
    } as unknown as UsageEvent);

    const [line] = await lines(dir, "2026-10");
    assert.equal("text" in line, false);
    assert.equal(line.units, 0, "a nonsense count is written as none");
  });

  it("writes nothing for a club id that isn't one", async () => {
    const dir = await folder();

    await new UsageLog({ dataDir: dir }).record(event("2026-10-24T20:00:00.000Z", { club: "../../etc" }));

    await assert.rejects(readdir(join(dir, USAGE_LOG_DIR)));
  });

  it("never throws, even when it can't write", async () => {
    const dir = await folder();
    const said: string[] = [];

    // A file where the folder should be: every write fails.
    await writeFile(join(dir, USAGE_LOG_DIR), "in the way");

    await new UsageLog({ dataDir: dir, log: (line) => said.push(line) }).record(event("2026-10-24T20:00:00.000Z"));

    assert.match(said.join("\n"), /couldn't record a voice line for "acme-rc"/);
  });

  it("keeps 24 months and deletes what is older", async () => {
    const dir = await folder();
    await mkdir(join(dir, USAGE_LOG_DIR));

    for (const month of ["2024-10", "2024-11", "2025-06", "2026-10", "notes"]) {
      await writeFile(join(dir, USAGE_LOG_DIR, `${month}.jsonl`), "");
    }

    await pruneUsageLog(dir, "2026-10");

    assert.deepEqual((await readdir(join(dir, USAGE_LOG_DIR))).sort(), ["2024-11.jsonl", "2025-06.jsonl", "2026-10.jsonl", "notes.jsonl"]);
  });
});

describe("the usage log: reading", () => {
  it("gives every event after a moment, oldest first, across months", async () => {
    const dir = await folder();
    const log = new UsageLog({ dataDir: dir });

    await log.record(event("2026-09-30T23:59:59.000Z"));
    await log.record(event("2026-10-02T10:00:00.000Z"));
    await log.record(event("2026-10-01T10:00:00.000Z"));

    const all = await readUsageLog(dir, undefined);
    assert.deepEqual(all.events.map((e) => e.at), ["2026-09-30T23:59:59.000Z", "2026-10-01T10:00:00.000Z", "2026-10-02T10:00:00.000Z"]);
    assert.equal(all.more, false);

    const after = await readUsageLog(dir, "2026-10-01T10:00:00.000Z");
    assert.deepEqual(after.events.map((e) => e.at), ["2026-10-02T10:00:00.000Z"], "strictly after");
  });

  it("pages, and never splits two events that share a moment", async () => {
    const dir = await folder();
    const log = new UsageLog({ dataDir: dir });

    await log.record(event("2026-10-01T00:00:01.000Z"));
    await log.record(event("2026-10-01T00:00:02.000Z"));
    await log.record(event("2026-10-01T00:00:02.000Z", { club: "brookside" }));
    await log.record(event("2026-10-01T00:00:03.000Z"));

    const first = await readUsageLog(dir, undefined, 2);
    assert.equal(first.events.length, 3, "the page runs on to keep the pair together");
    assert.equal(first.more, true);

    const second = await readUsageLog(dir, first.events.at(-1)?.at, 2);
    assert.deepEqual(second.events.map((e) => e.at), ["2026-10-01T00:00:03.000Z"]);
    assert.equal(second.more, false);
  });

  it("skips a half-written line", async () => {
    const dir = await folder();
    await mkdir(join(dir, USAGE_LOG_DIR));
    await writeFile(usageLogPath(dir, "2026-10"), JSON.stringify(event("2026-10-01T00:00:00.000Z")) + "\n{\"at\":\"2026-10-0");

    assert.equal((await readUsageLog(dir, undefined)).events.length, 1);
  });

  it("answers an empty page when nothing has been logged yet", async () => {
    assert.deepEqual(await readUsageLog(await folder(), undefined), { version: 1, events: [], more: false });
  });
});

describe("the usage log: the relay's routes", () => {
  const VOICE: CatalogueVoice = {
    id: "clubhouse",
    name: "Clubhouse",
    what: "Warm",
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

  const BRAIN: BrainConfig = { model: "claude-haiku-test", monthlyLines: 5, maxWords: 60, maxLongWords: 450 };
  const clock = new Date("2026-10-24T20:00:00.000Z");
  let harness: Harness | undefined;
  let dir = "";

  beforeEach(async () => {
    dir = await folder();
  });

  afterEach(async () => {
    await harness?.close();
    harness = undefined;
  });

  async function serve(write: WriteFn): Promise<Harness> {
    const render: RenderFn = async () => Buffer.from("mp3-bytes");
    const voice = new VoiceService({ config: { voices: [VOICE], monthlyChars: 1_000, maxChars: 120 }, dataDir: dir, render, now: () => clock });
    const brain = new BrainService({ config: BRAIN, dataDir: dir, write, now: () => clock });

    harness = await start(
      new MemoryStore(),
      tenants([
        { voice: { enabled: true, monthlyChars: 1_000, voices: [] }, brain: { enabled: true, monthlyLines: 5 } },
        { voice: { enabled: false, monthlyChars: 1_000, voices: [] } },
      ]),
      () => clock,
      () => {},
      voice,
      { brain, usageLog: new UsageLog({ dataDir: dir }) },
    );

    return harness;
  }

  async function post(path: string, body: unknown, token = ACME_TOKEN, club = "acme-rc"): Promise<number> {
    const response = await fetch(`${harness!.base}/api/v1/c/${club}${path}`, {
      method: "POST",
      headers: { authorization: `Bearer ${token}`, "content-type": "application/json" },
      body: JSON.stringify(body),
    });

    await response.arrayBuffer();
    return response.status;
  }

  /** The log is written after the answer goes out, so a moment is given for it to land. */
  async function logged(): Promise<any[]> {
    for (let attempt = 0; attempt < 50; attempt += 1) {
      try {
        return await lines(dir, "2026-10");
      } catch {
        await new Promise((resolve) => setTimeout(resolve, 10));
      }
    }

    return [];
  }

  async function settle(count: number): Promise<any[]> {
    for (let attempt = 0; attempt < 50; attempt += 1) {
      const found = await logged();

      if (found.length >= count) {
        return found;
      }

      await new Promise((resolve) => setTimeout(resolve, 10));
    }

    return logged();
  }

  it("logs a voice line's characters, and a cached repeat as costing nothing", async () => {
    await serve(async () => "x");

    assert.equal(await post("/voice/speak", { voice: "clubhouse", text: "Green flag!", shared: true }), 200);
    assert.equal(await post("/voice/speak", { voice: "clubhouse", text: "Green flag!", shared: true }), 200);

    const [first, second] = await settle(2);
    assert.equal(first.meter, "voice");
    assert.equal(first.kind, "shared");
    assert.equal(first.units, "Green flag!".length);
    assert.equal(first.cached, false);
    assert.equal(first.model, "eleven_flash_v2_5");
    assert.equal(first.voice, "clubhouse");
    assert.equal(second.cached, true);
    assert.equal(second.units, 0);
  });

  it("logs a refused line with why, and never the words", async () => {
    await serve(async () => "x");

    assert.equal(await post("/voice/speak", { voice: "nobody", text: "Dave Smith leads" }), 400);

    const [line] = await settle(1);
    assert.equal(line.outcome, "unknown-voice");
    assert.equal(line.voice, "unknown", "a voice id the relay doesn't have is never kept");
    assert.equal(line.units, 0);
    assert.doesNotMatch(JSON.stringify(line), /Dave/);
  });

  it("logs a commentary line with Anthropic's own token count", async () => {
    await serve(async () => ({ text: "They came to the flag together.", inputTokens: 512, outputTokens: 41 }));

    assert.equal(await post("/brain/write", { system: "Announcer", facts: "Dave won by a second", words: 40 }), 200);

    const [line] = await settle(1);
    assert.deepEqual(
      { meter: line.meter, kind: line.kind, units: line.units, model: line.model, words: line.words, in: line.in, out: line.out, outcome: line.outcome },
      { meter: "commentary", kind: "line", units: 1, model: "claude-haiku-test", words: 40, in: 512, out: 41, outcome: "ok" },
    );
    assert.doesNotMatch(JSON.stringify(line), /Dave|flag together/);
  });

  it("calls a request over the normal length a long piece", async () => {
    await serve(async () => "A long recap.");

    assert.equal(await post("/brain/write", { system: "Announcer", facts: "Round 3", words: 300 }), 200);

    const [line] = await settle(1);
    assert.equal(line.kind, "long");
    assert.equal("in" in line, false, "a writer that gives no count logs none");
  });

  it("logs a commentary line the model couldn't write as costing nothing", async () => {
    await serve(async () => {
      throw new Error("down");
    });

    assert.equal(await post("/brain/write", { system: "Announcer", facts: "Round 3", words: 40 }), 502);

    const [line] = await settle(1);
    assert.equal(line.outcome, "upstream");
    assert.equal(line.units, 0);
  });

  it("logs nothing for a request from the wrong club's token", async () => {
    await serve(async () => "x");

    assert.equal(await post("/voice/speak", { voice: "clubhouse", text: "Hi" }, BROOK_TOKEN), 403);
    await new Promise((resolve) => setTimeout(resolve, 50));

    assert.deepEqual(await readUsageLog(dir, undefined), { version: 1, events: [], more: false });
  });
});

describe("the usage log: Anthropic's count", () => {
  it("is read from the answer's usage block", async () => {
    const real = globalThis.fetch;

    globalThis.fetch = (async () =>
      new Response(JSON.stringify({ content: [{ type: "text", text: "Hello." }], usage: { input_tokens: 321, output_tokens: 12 } }), {
        status: 200,
        headers: { "content-type": "application/json" },
      })) as typeof fetch;

    try {
      assert.deepEqual(await anthropicWriter("key", "model")("s", "u", 50, 0.5), { text: "Hello.", inputTokens: 321, outputTokens: 12 });
    } finally {
      globalThis.fetch = real;
    }
  });

  it("is simply left out when the answer has none", async () => {
    const real = globalThis.fetch;

    globalThis.fetch = (async () =>
      new Response(JSON.stringify({ content: [{ type: "text", text: "Hello." }] }), { status: 200 })) as typeof fetch;

    try {
      assert.deepEqual(await anthropicWriter("key", "model")("s", "u", 50, 0.5), { text: "Hello." });
    } finally {
      globalThis.fetch = real;
    }
  });
});

describe("the usage log: usage-log", () => {
  it("prints the events after --since as JSON", async () => {
    const dir = await folder();
    const log = new UsageLog({ dataDir: dir });

    await log.record(event("2026-10-01T00:00:00.000Z"));
    await log.record(event("2026-10-02T00:00:00.000Z"));

    let out = "";
    const env = {
      RELAY_DATA: dir,
      RELAY_CONFIG_JSON: JSON.stringify({ clubs: [{ club: "acme-rc", tokenSha256: hashToken(ACME_TOKEN) }] }),
    };

    const code = await voiceAdmin("usage-log", ["--json", "--since", "2026-10-01T12:00:00Z"], env, new Date(), (text) => (out += text), () => {});

    assert.equal(code, 0);
    assert.deepEqual(JSON.parse(out).events.map((e: UsageEvent) => e.at), ["2026-10-02T00:00:00.000Z"]);
  });

  it("refuses anything it doesn't understand", async () => {
    let err = "";
    const env = {
      RELAY_DATA: await folder(),
      RELAY_CONFIG_JSON: JSON.stringify({ clubs: [{ club: "acme-rc", tokenSha256: hashToken(ACME_TOKEN) }] }),
    };

    for (const args of [["--since", "yesterday"], ["--limit", "0"], ["--all"]]) {
      err = "";
      assert.equal(await voiceAdmin("usage-log", args, env, new Date(), () => {}, (text) => (err += text)), 1, args.join(" "));
      assert.match(err, /Usage: usage-log/);
    }
  });
});

