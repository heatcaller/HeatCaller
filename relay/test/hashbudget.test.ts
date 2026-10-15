import assert from "node:assert/strict";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { after, afterEach, before, beforeEach, describe, it } from "node:test";
import { BrainService, brainUsagePath, readBrainConfig, type BrainConfig, type BrainPlan } from "../src/brain.js";
import { managePage } from "../src/pages.js";
import { CLUB_FAIL_LIMIT, CLUB_FAIL_WINDOW_MS, DAY_FAIL_LIMIT, DAY_FAIL_WINDOW_MS, LOCK_STEPS_MS, PIN_GUARD_DOC, PinGuard } from "../src/pinguard.js";
import {
  managePinHash,
  pinHash,
  racerPinHash,
  signupPinHash,
  SLOW_HASHES_IN_ALL,
  SLOW_HASHES_PER_ADDRESS,
} from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

// Fixes of 14 Oct 2026: slow PIN hashes paid for right PINs (and stale
// passes' PINs, and expired invite codes) were never counted, so one
// address could stall the relay for every club; a guesser pacing under
// the ten-minute limit was never locked out at all; the brain cut every
// recap to 120 words and a refund across midnight on the last of the
// month wrote the old month over the new; and the manager's Edit result
// read a mistyped lap count as 0.

const SALT = "00112233445566778899aabbccddeeff".repeat(2);
const LATER = "2026-10-04T12:00:00Z";
const NOW = new Date("2026-09-20T12:00:00.000Z");
const SAM = "0123456789abcdef0123";
const MINUTE = 60 * 1000;
const HOUR = 60 * MINUTE;

describe("slow PIN hashes are counted, right PINs as well as wrong", () => {
  let harness: Harness;
  let seq = 1;

  before(() => {
    process.env["FLY_APP_NAME"] = "test";
  });

  after(() => {
    delete process.env["FLY_APP_NAME"];
  });

  beforeEach(async () => {
    harness = await start(undefined, undefined, () => NOW);
    seq = 1;
  });

  afterEach(async () => {
    await harness.close();
  });

  const publish = async (channel: string, doc: unknown): Promise<void> => {
    const answer = await call(harness, "POST", `/api/v1/c/acme-rc/publish/${channel}?seq=${seq++}`, { token: ACME_TOKEN, json: doc });
    assert.equal(answer.status, 200, JSON.stringify(answer.body));
  };

  const post = async (path: string, address: string, json: Record<string, unknown>) => {
    const response = await fetch(`${harness.base}/api/v1/c/acme-rc/${path}`, {
      method: "POST",
      headers: { "content-type": "application/json", "fly-client-ip": address },
      body: JSON.stringify(json),
    });

    return { status: response.status, body: (await response.json()) as Record<string, any> };
  };

  it("stops a racer signing in with their own PIN over and over from one address", async () => {
    await publish("checkin", {
      enabled: true,
      pinSha256: pinHash("acme-rc", "4321", SALT),
      pinSalt: SALT,
      event: "Club Night",
      drivers: [{ id: SAM, name: "Sam Carter", className: "Stock", number: "7", checkedIn: false, photo: "", rp: racerPinHash("acme-rc", "482913", SALT) }],
    });

    // Two hashes a sign-in: the staff PIN, then the racer's.
    for (let i = 0; i < SLOW_HASHES_PER_ADDRESS / 2; i++) {
      const answer = await post("checkin/list", "198.51.100.7", { pin: "482913" });
      assert.equal(answer.status, 200, JSON.stringify(answer.body));
    }

    const refused = await post("checkin/list", "198.51.100.7", { pin: "482913" });
    assert.equal(refused.status, 429);
    assert.match(refused.body["error"], /Too many sign-ins right now/);

    // Another address is not held up by it, and a pass costs no hash.
    const other = await post("checkin/list", "198.51.100.8", { pin: "482913" });
    assert.equal(other.status, 200, JSON.stringify(other.body));

    const byPass = await post("checkin/list", "198.51.100.7", { pass: other.body["pass"] });
    assert.equal(byPass.status, 200, JSON.stringify(byPass.body));
  });

  it("counts an expired invite code's hash, though not as a wrong code", async () => {
    await publish("signup", {
      enabled: true,
      event: "Club night",
      classes: ["Stock"],
      pinSalt: SALT,
      invites: [{ h: signupPinHash("acme-rc", "48291357", SALT), x: "2026-09-01T12:00:00Z" }],
    });

    for (let i = 0; i < SLOW_HASHES_PER_ADDRESS; i++) {
      const answer = await post("signup/signin", "203.0.113.9", { pin: "48291357" });
      assert.equal(answer.status, 403, JSON.stringify(answer.body));
      assert.match(answer.body["error"], /expired/);
    }

    const refused = await post("signup/signin", "203.0.113.9", { pin: "48291357" });
    assert.equal(refused.status, 429);
    assert.match(refused.body["error"], /Too many sign-ins right now/);
  });

  it("holds the whole relay to its budget, however many addresses ask", async () => {
    await publish("manage", { enabled: true, pinSha256: managePinHash("acme-rc", "123456", SALT), pinSalt: SALT });

    // One right manager PIN from each of many addresses: under every
    // per-address limit, and still only so many hashes in all.
    for (let i = 0; i < SLOW_HASHES_IN_ALL; i++) {
      const answer = await post("manage/signin", `10.0.${Math.floor(i / 200)}.${i % 200}`, { pin: "123456" });
      assert.equal(answer.status, 200, `sign-in ${i + 1}: ${JSON.stringify(answer.body)}`);
    }

    const refused = await post("manage/signin", "10.9.9.9", { pin: "123456" });
    assert.equal(refused.status, 429);
    assert.match(refused.body["error"], /Too many sign-ins right now/);
  });
});

describe("a club-wide PIN guesser who paces themselves", () => {
  let now = 0;
  let store: MemoryStore;
  let lines: string[];
  let guard: PinGuard;

  beforeEach(() => {
    now = Date.UTC(2026, 8, 20, 12);
    store = new MemoryStore();
    lines = [];
    guard = new PinGuard(store, () => now, (line) => lines.push(line));
  });

  /** One under the ten-minute limit, then the window passes - the most anybody can do without ever tripping it. */
  const pacedBatch = async (count = CLUB_FAIL_LIMIT - 1) => {
    for (let i = 0; i < count; i++) {
      await guard.fail("acme-rc");
    }

    now += CLUB_FAIL_WINDOW_MS + 1;
  };

  it("is closed out for the longest step once a day's ceiling is reached, and again after each guess until it ages out", async () => {
    const full = Math.floor((DAY_FAIL_LIMIT - 1) / (CLUB_FAIL_LIMIT - 1));

    for (let b = 0; b < full; b++) {
      await pacedBatch();
    }

    const rest = DAY_FAIL_LIMIT - 1 - full * (CLUB_FAIL_LIMIT - 1);
    await pacedBatch(rest);
    now -= CLUB_FAIL_WINDOW_MS + 1;
    assert.equal(await guard.lockedUntil("acme-rc"), 0, "one under the day's ceiling is still open");

    await guard.fail("acme-rc");
    const longest = LOCK_STEPS_MS[LOCK_STEPS_MS.length - 1] as number;
    assert.equal(await guard.lockedUntil("acme-rc"), now + longest);
    assert.ok(lines.some((line) => line.includes("in a day")));

    // Survives a restart.
    const restarted = new PinGuard(store, () => now, () => {});
    assert.equal(await restarted.lockedUntil("acme-rc"), now + longest);

    // Open again once it has run - and the next wrong PIN closes it again,
    // since the day's guesses are still in the day.
    now += longest;
    assert.equal(await guard.lockedUntil("acme-rc"), 0);
    await guard.fail("acme-rc");
    assert.equal(await guard.lockedUntil("acme-rc"), now + longest);

    // A day later they have aged out: a wrong PIN is just a wrong PIN.
    now += DAY_FAIL_WINDOW_MS + 1;
    await guard.fail("acme-rc");
    assert.equal(await guard.lockedUntil("acme-rc"), 0);
  });

  it("forgets the day's count when reset, as a changed staff PIN does", async () => {
    for (let b = 0; b < 3; b++) {
      await pacedBatch();
    }

    await guard.reset("acme-rc");
    assert.deepEqual((await store.getSnapshot("acme-rc", PIN_GUARD_DOC))?.body, { fails: [], level: 0, lockedUntil: 0 });

    for (let b = 0; b < 3; b++) {
      await pacedBatch();
    }

    assert.equal(await guard.lockedUntil("acme-rc"), 0);
  });
});

describe("the brain's long pieces", () => {
  const scratch: string[] = [];
  const ON: BrainPlan = { enabled: true, monthlyLines: 5 };

  after(async () => {
    for (const dir of scratch) {
      await rm(dir, { recursive: true, force: true });
    }
  });

  const dataDir = async (): Promise<string> => {
    const dir = await mkdtemp(join(tmpdir(), "relay-brain-long-"));
    scratch.push(dir);
    return dir;
  };

  const budgets = async (config: BrainConfig, words: number): Promise<number> => {
    let asked = 0;
    const brain = new BrainService({
      config,
      dataDir: await dataDir(),
      write: async (_system, _user, maxTokens) => {
        asked = maxTokens;
        return "A line.";
      },
    });

    assert.equal((await brain.write("acme-rc", ON, "system", "facts", words, 0.7)).ok, true);
    return asked;
  };

  it("lets a recap run past maxWords up to maxLongWords, and keeps a normal line capped", async () => {
    const config: BrainConfig = { model: "m", monthlyLines: 5, maxWords: 120, maxLongWords: 450 };

    assert.equal(await budgets(config, 60), 60 * 2 + 40);
    assert.equal(await budgets(config, 120), 120 * 2 + 40);
    assert.equal(await budgets(config, 400), 400 * 2 + 40);
    assert.equal(await budgets(config, 900), 450 * 2 + 40);
  });

  it("reads maxLongWords from the config, with a default", () => {
    assert.equal(readBrainConfig({ model: "m" }, "test").maxLongWords, 450);
    assert.equal(readBrainConfig({ model: "m", maxLongWords: 300 }, "test").maxLongWords, 300);
    assert.throws(() => readBrainConfig({ maxLongWords: 5000 }, "test"));
  });

  it("hands nothing back across a month boundary, so the new month's count is not overwritten", async () => {
    const dir = await dataDir();
    let now = new Date("2026-09-30T23:59:59.000Z");
    const brain = new BrainService({
      config: { model: "m", monthlyLines: 5, maxWords: 120 },
      dataDir: dir,
      now: () => now,
      write: async () => {
        now = new Date("2026-10-01T00:00:01.000Z");
        throw new Error("too slow");
      },
    });

    // One line in September first, so September has a count to keep.
    const first = new BrainService({ config: { model: "m", monthlyLines: 5, maxWords: 120 }, dataDir: dir, now: () => now, write: async () => "Fine." });
    assert.equal((await first.write("acme-rc", ON, "s", "f", 40, 0.7)).ok, true);

    await brain.write("acme-rc", ON, "s", "f", 40, 0.7);

    const saved = JSON.parse(await readFile(brainUsagePath(dir, "acme-rc"), "utf8")) as { month: string; used: number };
    assert.equal(saved.month, "2026-09", "the refund wrote nothing at all");
    assert.equal(saved.used, 2);
  });
});

describe("the relay's manager Edit result", () => {
  const html = managePage("acme-rc", "Acme RC");

  function pageFunction(name: string): string {
    const begin = html.indexOf(`function ${name}(`);
    assert.ok(begin >= 0, `the page has ${name}()`);
    let depth = 0;
    for (let i = html.indexOf("{", begin); i < html.length; i++) {
      if (html[i] === "{") depth++;
      if (html[i] === "}" && --depth === 0) return html.slice(begin, i + 1);
    }
    throw new Error(`${name}() never closes`);
  }

  const run = (answers: (string | null)[]) => {
    const alerts: string[] = [];
    const ops: unknown[] = [];
    const queue = [...answers];
    const fn = new Function(
      "currentResult",
      "prompt",
      "alert",
      "doOp",
      "openResult",
      "refreshResults",
      `${pageFunction("openEditResult")}\nopenEditResult();`,
    );

    fn(
      { id: "s1", rows: [{ id: "d1", name: "Sam", addedLaps: 2, outcome: "" }, { id: "d2", name: "Ann", addedLaps: 0, outcome: "" }] },
      () => (queue.length === 0 ? null : queue.shift()),
      (text: string) => alerts.push(text),
      (op: string, args: unknown) => ops.push({ op, args }),
      () => {},
      () => {},
    );

    return { alerts, ops };
  };

  it("asks again for a lap count that is not a whole number, and sends only the rows that changed", () => {
    const { alerts, ops } = run(["Fixing a miscount", "two", "3", "", "0", ""]);

    assert.equal(alerts.length, 1);
    assert.deepEqual(ops, [
      { op: "result.adjust", args: { sessionId: "s1", reason: "Fixing a miscount", changes: [{ driverId: "d1", addedLaps: 3, outcome: "" }] } },
    ]);
  });

  it("takes a negative count, and sends nothing when nothing changed", () => {
    assert.deepEqual(run(["r", "-1", "", "0", ""]).ops, [
      { op: "result.adjust", args: { sessionId: "s1", reason: "r", changes: [{ driverId: "d1", addedLaps: -1, outcome: "" }] } },
    ]);

    const none = run(["r", "2", "", "0", ""]);
    assert.deepEqual(none.ops, []);
    assert.deepEqual(none.alerts, ["Nothing was changed."]);
  });
});
