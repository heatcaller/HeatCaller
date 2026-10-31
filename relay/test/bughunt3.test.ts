import assert from "node:assert/strict";
import { createContext, runInContext } from "node:vm";
import { after, before, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * Bug hunt 3, relay side: the results index evicting newer years (#19),
 * the home page polling the whole results listing (#8), and the manager
 * page piling up poll chains (#31).
 */

async function publish(harness: Harness, path: string, json: unknown): Promise<number> {
  const answer = await call(harness, "POST", `/api/v1/c/acme-rc/publish/${path}`, { token: ACME_TOKEN, json });
  return answer.status;
}

async function years(harness: Harness): Promise<string[]> {
  const meta = await call(harness, "GET", "/api/v1/c/acme-rc/collection/resultsindex?meta=1");
  return meta.body.items.map((item: { id: string }) => item.id).sort();
}

/** The one inline script a page carries. */
function scriptOf(html: string): string {
  const match = /<script[^>]*>([\s\S]*)<\/script>/.exec(html);
  assert.ok(match, "every page carries its script inline");
  return match[1] as string;
}

describe("the results index's own budget never evicts a newer year (#19)", () => {
  let harness: Harness;

  before(async () => {
    harness = await start(new MemoryStore());
  });

  after(async () => {
    await harness.close();
  });

  it("evicts an older year, not a newer one, when a middle year grows", async () => {
    assert.equal(await publish(harness, "resultsindex/2024", { pad: "a".repeat(500 * 1024) }), 200);
    assert.equal(await publish(harness, "resultsindex/2025", { pad: "b".repeat(700 * 1024) }), 200);
    assert.equal(await publish(harness, "resultsindex/2026", { pad: "c".repeat(700 * 1024) }), 200);
    assert.deepEqual(await years(harness), ["2024", "2025", "2026"]);

    // 2025 is edited and grows past the 2 MiB budget: 2024 makes way.
    assert.equal(await publish(harness, "resultsindex/2025", { pad: "b".repeat(900 * 1024) }), 200);
    assert.deepEqual(await years(harness), ["2025", "2026"]);
  });

  it("refuses an old year rather than evicting the current season", async () => {
    // 2024 republished at a size that only fits by dropping a newer year.
    const refused = await call(harness, "POST", "/api/v1/c/acme-rc/publish/resultsindex/2024", {
      token: ACME_TOKEN,
      json: { pad: "a".repeat(900 * 1024) },
    });

    assert.equal(refused.status, 507);
    assert.deepEqual(await years(harness), ["2025", "2026"], "this season and last are both still there");

    // And this season's index is still the one that was published.
    const current = await call(harness, "GET", "/api/v1/c/acme-rc/collection/resultsindex/2026");
    assert.equal(current.status, 200);
  });
});

describe("the home page's latest results (#8)", () => {
  let harness: Harness;

  before(async () => {
    harness = await start(new MemoryStore());

    for (let i = 0; i < 10; i++) {
      assert.equal(await publish(harness, `results/s${i}`, { label: `Heat ${i}`, pad: "x".repeat(20 * 1024) }), 200);
    }
  });

  after(async () => {
    await harness.close();
  });

  it("gives only the newest N with ?meta=1&limit=N", async () => {
    const all = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results?meta=1");
    const some = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results?meta=1&limit=3");

    assert.equal(some.status, 200);
    assert.equal(some.body.meta, true);
    assert.deepEqual(some.body.items, all.body.items.slice(0, 3));

    const more = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results?meta=1&limit=50");
    assert.deepEqual(more.body.items, all.body.items);
  });

  it("refuses a limit that is not a whole number, or one without meta", async () => {
    for (const query of ["meta=1&limit=0", "meta=1&limit=abc", "meta=1&limit=1.5", "limit=3"]) {
      const answer = await call(harness, "GET", `/api/v1/c/acme-rc/collection/results?${query}`);
      assert.equal(answer.status, 400, query);
    }
  });

  // Round 6 (30 Oct 2026): Home's latest three gave way to the last race
  // night, read from the results index. The promise is the same - no flat
  // listing, and nothing but one small meta read while nothing changes.
  it("never fetches the full listing, and reads the last night again only when the index changes", async () => {
    const page = scriptOf((await call(harness, "GET", "/c/acme-rc")).text);

    assert.ok(!/grab\('\/collection\/results'\)/.test(page), "the home page does not read the flat listing");

    const helper = /let nightSig = null;[\s\S]*?\nasync function lastNight\(\) \{[\s\S]*?\n\}\n/.exec(page)?.[0];
    assert.ok(helper, "lastNight is where the test expects it");

    const index = (label: string) => ({
      shape: 1, year: "2026", drivers: [], raced: [],
      sessions: [
        { id: "s8", day: "2026-09-19", at: "19:00", type: "Race", label: "Heat 8", won: "Sam" },
        { id: "s9", day: "2026-09-19", at: "20:00", type: "Race", label, won: "Jo" },
      ],
      days: [{ day: "2026-09-19", sessions: [0, 1] }],
    });

    assert.equal(await publish(harness, "resultsindex/2026", index("A Main")), 200);

    const asked: string[] = [];
    const context = createContext({
      grab: async (path: string) => {
        asked.push(path);
        const answer = await call(harness, "GET", `/api/v1/c/acme-rc${path}`);
        return answer.status >= 400 ? { missing: true, status: answer.status } : { body: answer.body };
      },
    });

    runInContext(`let nightHeld = null;\n${helper}\nthis.lastNight = lastNight;`, context);
    const lastNight = (context as { lastNight: () => Promise<any> }).lastNight;

    const first = await lastNight();
    assert.equal(first.day, "2026-09-19");
    assert.deepEqual(first.sessions.map((x: { label: string }) => x.label), ["Heat 8", "A Main"]);
    assert.deepEqual(asked, ["/collection/resultsindex?meta=1", "/collection/resultsindex/2026", "/collection/results?ids=s8,s9"]);

    // Nothing new: only the small meta read.
    await lastNight();
    assert.equal(asked.length, 4);
    assert.equal(asked[3], "/collection/resultsindex?meta=1");

    // The index changes: the night is read again.
    assert.equal(await publish(harness, "resultsindex/2026", index("A Main (re-run)")), 200);
    const fresh = await lastNight();
    assert.equal(fresh.sessions[1].label, "A Main (re-run)");
    assert.ok(asked.every((path) => path !== "/collection/results"));
  });
});

describe("the manager page's background refresh (#31)", () => {
  let harness: Harness;
  let script: string;

  before(async () => {
    harness = await start(new MemoryStore());
    script = scriptOf((await call(harness, "GET", "/c/acme-rc/manage")).text);
  });

  after(async () => {
    await harness.close();
  });

  it("keeps one refresh in flight at a time", async () => {
    const chunk = /function poll\(load, everyMs\) \{[\s\S]*?\nfunction afterSignIn\(\) \{[\s\S]*?\n\}\n/.exec(script)?.[0];
    assert.ok(chunk, "poll and afterSignIn are where the test expects them");

    const ticks: Array<() => void> = [];
    const pending: Array<() => void> = [];
    let hellos = 0;

    const context = createContext({
      document: { hidden: false, addEventListener() {} },
      setInterval: (fn: () => void) => {
        ticks.push(fn);
        return 1;
      },
      showApp() {},
      signedIn: () => true,
      loadHello: () => {
        hellos++;
        return new Promise<void>((resolve) => pending.push(resolve));
      },
      refreshActive: () => Promise.resolve(),
      Promise,
    });

    runInContext(`${chunk}\nthis.afterSignIn = afterSignIn;`, context);
    (context as { afterSignIn: () => void }).afterSignIn();

    assert.equal(hellos, 1, "the first load");
    assert.equal(ticks.length, 1);

    // The laptop is away: nine ticks, and no new chain while the first waits.
    for (let i = 0; i < 9; i++) ticks[0]!();
    assert.equal(hellos, 1);

    // Once it answers, the next tick starts the next refresh.
    pending.shift()!();
    await new Promise((resolve) => setImmediate(resolve));
    ticks[0]!();
    assert.equal(hellos, 2);
  });

  it("backs off when the relay answers 429", async () => {
    const chunk = /function awaitAnswer\(reqId, deadline\) \{[\s\S]*?\n\}\n/.exec(script)?.[0];
    assert.ok(chunk, "awaitAnswer is where the test expects it");

    const delays: number[] = [];
    let status = 429;

    const context = createContext({
      api: async () => ({ ok: false, _status: status }),
      pass: () => "p",
      showSignIn() {},
      setTimeout: (fn: () => void, ms: number) => {
        delays.push(ms);
        return 1;
      },
      Date,
      Promise,
    });

    runInContext(`${chunk}\nthis.awaitAnswer = awaitAnswer;`, context);
    const wait = (context as { awaitAnswer: (id: string, deadline: number) => Promise<unknown> }).awaitAnswer;

    void wait("r1", Date.now() + 90000);
    await new Promise((resolve) => setImmediate(resolve));
    status = 200;
    void wait("r2", Date.now() + 90000);
    await new Promise((resolve) => setImmediate(resolve));

    assert.equal(delays.length, 2);
    assert.ok((delays[0] as number) > (delays[1] as number), `429 waits longer (${delays.join(", ")})`);
  });
});
