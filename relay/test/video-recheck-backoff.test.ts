import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { VIDEO_RECHECK_BACKOFF_MS, VideoCheck } from "../src/video.js";

/**
 * OPEN ITEMS (19 Nov 2026), item 1: a video YouTube refused (401/403/404)
 * was asked about again only a day later, so a stream that was private when
 * it ended and went public that evening stayed hidden all night. Now it is
 * asked again after 2, 5, 15 and 60 minutes, then daily.
 */

const MIN = 60 * 1000;
const DAY = 24 * 60 * MIN;

function checker(answer: (id: string) => Promise<number | undefined>, keep?: ConstructorParameters<typeof VideoCheck>[2]) {
  let now = Date.parse("2026-11-16T23:00:00Z");
  const asked: string[] = [];
  const check = new VideoCheck(async (id) => { asked.push(id); return answer(id); }, () => new Date(now), keep);
  return { check, asked, later: (ms: number) => { now += ms; } };
}

describe("a refused video re-checked the same night", () => {
  it("backs off 2, 5, 15, 60 minutes, then daily", async () => {
    assert.deepEqual(VIDEO_RECHECK_BACKOFF_MS, [2 * MIN, 5 * MIN, 15 * MIN, 60 * MIN]);
    const { check, asked, later } = checker(async () => 403);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(asked.length, 1);

    for (const [i, wait] of [2, 5, 15, 60].entries()) {
      later(wait * MIN - 1000);
      await check.saw("acme-rc", ["AAAAAAAAAAA"]);
      assert.equal(asked.length, i + 1, `not before ${wait} min`);
      later(1000);
      await check.saw("acme-rc", ["AAAAAAAAAAA"]);
      assert.equal(asked.length, i + 2, `asked again after ${wait} min`);
    }

    later(23 * 60 * MIN);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(asked.length, 5, "then once a day");
    later(60 * MIN);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(asked.length, 6);
  });

  it("picks up a private stream that goes public within minutes", async () => {
    let status = 403;
    const { check, later } = checker(async () => status);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(check.dead("acme-rc").size, 1);

    status = 200;
    later(2 * MIN);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(check.dead("acme-rc").size, 0, "playing again two minutes later");
  });

  it("starts the backoff again after a video that played is refused", async () => {
    const answers = [404, 200, 401];
    const { check, asked, later } = checker(async () => answers.shift() ?? 401);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    later(2 * MIN);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(check.dead("acme-rc").size, 0);
    later(DAY);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(check.dead("acme-rc").size, 1);
    later(2 * MIN);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(asked.length, 4, "two minutes after the new refusal");
  });

  it("keeps one ask in flight per video", async () => {
    // The first ask hangs; sightings while it does, even past the backoff, ask nothing more.
    let release: (n: number) => void = () => {};
    const { check, asked, later } = checker(() => new Promise<number>((r) => { release = r; }));
    const first = check.saw("acme-rc", ["AAAAAAAAAAA"]);
    await new Promise((r) => setImmediate(r));
    later(10 * MIN);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(asked.length, 1);
    release(403);
    await first;
    assert.equal(check.dead("acme-rc").size, 1);
  });

  it("keeps the refusals across a restart, and treats an older keep as past its quick re-asks", async () => {
    const saved: { id: string; refused: number }[][] = [];
    const { check, later } = checker(async () => 404, {
      load: async () => undefined,
      save: async (_club, checks) => { saved.push(checks); },
    });
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    later(2 * MIN);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(saved.at(-1)?.[0]?.refused, 2);

    const now = Date.parse("2026-11-16T23:00:00Z");
    const asked: string[] = [];
    const old = new VideoCheck(async (id) => { asked.push(id); return 404; }, () => new Date(now + 10 * MIN), {
      load: async () => [{ id: "AAAAAAAAAAA", firstMs: now, lastMs: now, dead: true }],
      save: async () => {},
    });
    await old.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(asked.length, 0, "an old keep's dead video waits its day");
  });
});
