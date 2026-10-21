import assert from "node:assert/strict";
import { mkdtemp, readFile, rm, writeFile, mkdir } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { after, describe, it } from "node:test";
import {
  BrainService,
  BrainUpstreamError,
  brainUsagePath,
  readBrainConfig,
  readBrainPlan,
  type BrainConfig,
  type BrainPlan,
  type WriteFn,
} from "../src/brain.js";

const CONFIG: BrainConfig = { model: "test-model", monthlyLines: 3, maxWords: 60 };
const ON: BrainPlan = { enabled: true, monthlyLines: 3 };
const OFF: BrainPlan = { enabled: false, monthlyLines: 3 };

const SYSTEM = "You are a race announcer.";
const FACTS = "Dave won by 1.2 seconds from Martin.";

const scratch: string[] = [];

async function dataDir(): Promise<string> {
  const dir = await mkdtemp(join(tmpdir(), "relay-brain-"));
  scratch.push(dir);
  return dir;
}

/** A brain whose model always answers the same thing, and counts how often it was asked. */
function fixed(answer: string): { write: WriteFn; calls: () => number } {
  let calls = 0;

  return {
    write: async () => {
      calls += 1;
      return answer;
    },
    calls: () => calls,
  };
}

describe("the commentary brain", () => {
  after(async () => {
    for (const dir of scratch) {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("writes a line, and counts it against the club's month", async () => {
    const model = fixed("  They came to the flag together.  ");
    const brain = new BrainService({ config: CONFIG, dataDir: await dataDir(), write: model.write });

    const result = await brain.write("acme-rc", ON, SYSTEM, FACTS, 40, 0.7);

    assert.equal(result.ok, true);
    assert.equal(result.ok && result.text, "They came to the flag together.");
    assert.equal(result.ok && result.usage.used, 1);
    assert.equal(result.ok && result.usage.allowance, 3);
    assert.equal(model.calls(), 1);
  });

  it("says nothing was written when the relay has no key, and never asks the model", async () => {
    const brain = new BrainService({ config: CONFIG, dataDir: await dataDir() });

    const result = await brain.write("acme-rc", ON, SYSTEM, FACTS, 40, 0.7);

    assert.equal(result.ok, false);
    assert.equal(!result.ok && result.refusal, "off");
    assert.equal(brain.available, false);
  });

  it("refuses a club whose plan does not include commentary, without spending anything", async () => {
    const model = fixed("Not this one.");
    const brain = new BrainService({ config: CONFIG, dataDir: await dataDir(), write: model.write });

    const result = await brain.write("acme-rc", OFF, SYSTEM, FACTS, 40, 0.7);

    assert.equal(!result.ok && result.refusal, "not-included");
    assert.equal(model.calls(), 0);
    // WHY (20 Oct 2026): the refusal used to carry a default 1,000-line
    // allowance the club doesn't have; it must carry no figures at all.
    assert.equal(result.usage, undefined);
  });

  it("stops at the allowance, and keeps saying so", async () => {
    const model = fixed("A line.");
    const brain = new BrainService({ config: CONFIG, dataDir: await dataDir(), write: model.write });

    for (let i = 0; i < 3; i++) {
      assert.equal((await brain.write("acme-rc", ON, SYSTEM, FACTS, 40, 0.7)).ok, true);
    }

    const past = await brain.write("acme-rc", ON, SYSTEM, FACTS, 40, 0.7);

    assert.equal(!past.ok && past.refusal, "allowance");
    // Asked three times, not four: the fourth never reached the model.
    assert.equal(model.calls(), 3);

    const again = await brain.write("acme-rc", ON, SYSTEM, FACTS, 40, 0.7);
    assert.equal(!again.ok && again.refusal, "allowance");
    assert.equal(model.calls(), 3);
  });

  it("hands the line back when the model fails, so a failure costs the club nothing", async () => {
    const dir = await dataDir();
    const brain = new BrainService({
      config: CONFIG,
      dataDir: dir,
      write: async () => {
        throw new BrainUpstreamError("the model answered 500", true);
      },
    });

    const result = await brain.write("acme-rc", ON, SYSTEM, FACTS, 40, 0.7);

    assert.equal(!result.ok && result.refusal, "busy");
    assert.equal(!result.ok && result.usage?.used, 0);

    const saved = JSON.parse(await readFile(brainUsagePath(dir, "acme-rc"), "utf8")) as { used: number };
    assert.equal(saved.used, 0);
  });

  it("calls a 401 broken rather than busy, so the app stops asking", async () => {
    const brain = new BrainService({
      config: CONFIG,
      dataDir: await dataDir(),
      write: async () => {
        throw new BrainUpstreamError("the model answered 401", false);
      },
    });

    const result = await brain.write("acme-rc", ON, SYSTEM, FACTS, 40, 0.7);

    assert.equal(!result.ok && result.refusal, "upstream");
  });

  it("refuses an empty request and one past the prompt cap", async () => {
    const model = fixed("A line.");
    const brain = new BrainService({ config: CONFIG, dataDir: await dataDir(), write: model.write });

    assert.equal(!(await brain.write("acme-rc", ON, SYSTEM, "   ", 40, 0.7)).ok, true);
    assert.equal(!(await brain.write("acme-rc", ON, SYSTEM, "x".repeat(9_000), 40, 0.7)).ok, true);
    assert.equal(model.calls(), 0);
  });

  it("will not spend against a count it cannot read", async () => {
    const dir = await dataDir();
    const path = brainUsagePath(dir, "acme-rc");

    await mkdir(dirname(path), { recursive: true });
    await writeFile(path, "{ not json", "utf8");

    const model = fixed("A line.");
    const brain = new BrainService({ config: CONFIG, dataDir: dir, write: model.write });

    const result = await brain.write("acme-rc", ON, SYSTEM, FACTS, 40, 0.7);

    // Busy, not allowance: a file that will not read must never hand a
    // club a fresh allowance, and must never be written over blindly.
    assert.equal(!result.ok && result.refusal, "busy");
    assert.equal(model.calls(), 0);
    assert.equal(await readFile(path, "utf8"), "{ not json");
  });

  it("keeps two clubs' counts apart", async () => {
    const dir = await dataDir();
    const model = fixed("A line.");
    const brain = new BrainService({ config: CONFIG, dataDir: dir, write: model.write });

    await brain.write("acme-rc", ON, SYSTEM, FACTS, 40, 0.7);
    await brain.write("acme-rc", ON, SYSTEM, FACTS, 40, 0.7);
    await brain.write("brook-rc", ON, SYSTEM, FACTS, 40, 0.7);

    const acme = await brain.catalogue("acme-rc", ON);
    const brook = await brain.catalogue("brook-rc", ON);

    assert.equal(acme.usage.used, 2);
    assert.equal(brook.usage.used, 1);
  });

  it("loses no line when several are written at once", async () => {
    const dir = await dataDir();
    const plenty: BrainPlan = { enabled: true, monthlyLines: 50 };

    const brain = new BrainService({
      config: CONFIG,
      dataDir: dir,
      write: async () => {
        // A real model takes a moment, which is when two counts read the
        // same number and one of them is lost.
        await new Promise((done) => setTimeout(done, 5));
        return "A line.";
      },
    });

    // More than the per-club cap of two in flight, so some are refused as
    // busy - what must not happen is a line written and not counted.
    const all = await Promise.all(
      Array.from({ length: 8 }, () => brain.write("acme-rc", plenty, SYSTEM, FACTS, 40, 0.7)),
    );

    const written = all.filter((result) => result.ok).length;
    const view = await brain.catalogue("acme-rc", plenty);

    assert.equal(view.usage.used, written);
    assert.ok(written > 0, "nothing was written at all");
  });

  it("says a club is on only when the relay can write and the plan includes it", async () => {
    const withKey = new BrainService({ config: CONFIG, dataDir: await dataDir(), write: fixed("x").write });
    const without = new BrainService({ config: CONFIG, dataDir: await dataDir() });

    assert.equal((await withKey.catalogue("acme-rc", ON)).enabled, true);
    assert.equal((await withKey.catalogue("acme-rc", ON)).model, "test-model");
    assert.equal((await withKey.catalogue("acme-rc", OFF)).enabled, false);
    assert.equal((await without.catalogue("acme-rc", ON)).enabled, false);
  });
});

describe("the brain's config", () => {
  it("is switched off entirely when the config says nothing", () => {
    const config = readBrainConfig(undefined, "test");

    assert.equal(config.model, "");
    assert.equal(readBrainPlan(undefined, config, "acme-rc", "test").enabled, false);
  });

  it("takes a model and defaults every club inherits", () => {
    const config = readBrainConfig({ model: "a-model", monthlyLines: 25 }, "test");

    assert.equal(config.model, "a-model");
    assert.equal(config.monthlyLines, 25);
    assert.equal(readBrainPlan({ enabled: true }, config, "acme-rc", "test").monthlyLines, 25);
    assert.equal(readBrainPlan({ enabled: true, monthlyLines: 5 }, config, "acme-rc", "test").monthlyLines, 5);
  });

  it("is opt-in: a club that says nothing does not get it", () => {
    const config = readBrainConfig({ model: "a-model" }, "test");

    assert.equal(readBrainPlan({}, config, "acme-rc", "test").enabled, false);
    assert.equal(readBrainPlan({ enabled: false }, config, "acme-rc", "test").enabled, false);
    assert.equal(readBrainPlan({ enabled: true }, config, "acme-rc", "test").enabled, true);
  });

  it("refuses a config it cannot make sense of rather than starting wrong", () => {
    assert.throws(() => readBrainConfig("nonsense", "test"));
    assert.throws(() => readBrainConfig({ monthlyLines: -1 }, "test"));
    assert.throws(() => readBrainConfig({ maxWords: 0 }, "test"));
    assert.throws(() => readBrainPlan("nonsense", CONFIG, "acme-rc", "test"));
    assert.throws(() => readBrainPlan({ monthlyLines: 1.5 }, CONFIG, "acme-rc", "test"));
  });
});
