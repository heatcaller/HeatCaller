import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { VideoCheck } from "../src/video.js";

/**
 * BUG HUNT 20: a video found dead is asked about again after its week, so
 * one made public later comes back; and a failed read of the kept checks is
 * tried again rather than written over.
 */

const DAY = 24 * 60 * 60 * 1000;

describe("a video made public after its week (bug hunt 20)", () => {
  it("is still asked about once a day while dead, and a 200 brings it back", async () => {
    let now = Date.parse("2026-11-14T23:00:00Z");
    const answers = [401];
    const asked: string[] = [];
    const check = new VideoCheck(async (id) => { asked.push(id); return answers.shift() ?? 401; }, () => new Date(now));
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.deepEqual([...check.dead("acme-rc")], ["AAAAAAAAAAA"]);

    now += 10 * DAY;
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(asked.length, 2, "asked again on day 10");
    assert.equal(check.dead("acme-rc").size, 1);

    // Its quick re-asks after a refusal (open items 19 Nov 2026: 2, 5, 15,
    // 60 min), then once a day.
    for (const minutes of [5, 15, 60]) {
      now += minutes * 60 * 1000;
      await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    }
    assert.equal(asked.length, 5, "the quick re-asks");
    now += 2 * 60 * 60 * 1000;
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(asked.length, 5, "then at most once a day");

    answers.push(200);
    now += DAY;
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(check.dead("acme-rc").size, 0, "public again");

    now += DAY;
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(asked.length, 6, "a playing video past its week is not asked about");
  });
});

describe("a failed read of the kept checks (bug hunt 20)", () => {
  it("is read again, and nothing is saved over the kept checks meanwhile", async () => {
    let fail = true;
    const saved: unknown[] = [];
    const check = new VideoCheck(async () => 200, () => new Date(), {
      load: async () => {
        if (fail) throw new Error("EIO");
        return [{ id: "OLDOLDOLD00", firstMs: 1, lastMs: Date.now(), dead: true }];
      },
      save: async (_club, checks) => { saved.push(checks); },
    });

    await check.ready("acme-rc");
    await check.saw("acme-rc", ["NEWNEWNEW00"]);
    assert.equal(saved.length, 0, "the kept checks are not written over");

    fail = false;
    await check.ready("acme-rc");
    assert.deepEqual([...check.dead("acme-rc")], ["OLDOLDOLD00"], "read back the second time");
    await check.saw("acme-rc", ["NEWNEWNEW00"]);
    assert.equal(saved.length, 1);
    assert.deepEqual((saved[0] as { id: string }[]).map((c) => c.id).sort(), ["NEWNEWNEW00", "OLDOLDOLD00"]);
  });
});
