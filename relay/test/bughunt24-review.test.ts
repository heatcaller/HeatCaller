import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { privacyRulesFrom, resultSweptAway, resultTakenDown } from "../src/privacy.js";
import { MemoryStore } from "../src/store.js";
import { VIDEO_NO_ANSWER_BACKOFF_MS, VIDEO_NO_ANSWER_RETRY_MS, VideoCheck } from "../src/video.js";
import { BROOK_TOKEN, call, start, tenants, type Harness } from "./helpers.js";

/**
 * BUG HUNT 24 REVIEW (24 Nov 2026): two of bug hunt 24's relay fixes went
 * too far.
 *  - The sweep that takes down old years deleted a result from an older
 *    laptop (no "year") by its UTC start, so a New Year's morning race east
 *    of UTC went for good although its year was kept.
 *  - A video YouTube did not answer about was asked again every two minutes
 *    for good; the wait now grows, and a real answer starts it again.
 */

const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

describe("bug hunt 24 review: the old-years sweep and a result with no year", () => {
  const rules = privacyRulesFrom({ juniors: [], keepYears: 2, fromYear: 2025 });

  it("keeps a no-year result whose local start can be in a kept year", () => {
    // 22:00 UTC on 31 Dec is New Year's morning in New Zealand.
    assert.equal(resultSweptAway(rules, { startedUtc: "2024-12-31T22:00:00Z" }), false);
    assert.equal(resultSweptAway(rules, { startedUtc: "2024-12-31T10:00:00Z" }), false, "10:00 UTC + 14 h is midnight, 1 Jan");
  });

  it("still sweeps a no-year result that is old wherever it was raced", () => {
    assert.equal(resultSweptAway(rules, { startedUtc: "2024-12-31T09:00:00Z" }), true, "23:00 on 31 Dec even at UTC+14");
    assert.equal(resultSweptAway(rules, { startedUtc: "2023-06-01T19:00:00Z" }), true);
  });

  it("judges a result with its own year by that year, as before", () => {
    assert.equal(resultSweptAway(rules, { startedUtc: "2024-12-31T22:00:00Z", year: "2024" }), true);
    assert.equal(resultSweptAway(rules, { startedUtc: "2025-01-01T01:00:00Z", year: 2024 }), true);
    assert.equal(resultSweptAway(rules, { startedUtc: "2024-12-31T09:00:00Z", year: "2025" }), false);
    assert.equal(resultSweptAway(rules, { startedUtc: "0001-01-01T00:00:00Z" }), false, "no start is unknown, not old");
  });

  it("leaves the publish refusal on the UTC year for an older laptop", () => {
    assert.equal(resultTakenDown(rules, { startedUtc: "2024-12-31T22:00:00Z" }), true);
  });

  it("does not delete a New Year's morning result from the store", async () => {
    const NEW_YEAR = "33333333333333333333333333333333";
    const OLD = "11111111111111111111111111111111";
    const store = new MemoryStore();
    const h: Harness = await start(store, tenants());
    let seq = 1;
    const publish = (path: string, json: unknown) =>
      call(h, "POST", `/api/v1/c/brookside/publish/${path}?seq=${seq++}`, { token: BROOK_TOKEN, json });

    try {
      await publish(`results/${NEW_YEAR}`, { label: "Heat 1", startedUtc: "2024-12-31T22:00:00Z", results: [] });
      await publish(`results/${OLD}`, { label: "Heat 1", startedUtc: "2023-06-01T19:00:00Z", results: [] });
      await publish("privacy", { juniors: [], keepYears: 2, fromYear: 2025 });
      await wait(60);

      assert.ok(await store.getItem("brookside", "results", NEW_YEAR), "New Year's morning, east of UTC, is in a kept year");
      assert.equal(await store.getItem("brookside", "results", OLD), undefined, "a truly old one still goes");
    } finally {
      await h.close();
    }
  });
});

describe("bug hunt 24 review: YouTube not answering, again and again", () => {
  const MIN = 60 * 1000;

  function checker(answers: (number | undefined)[]) {
    let now = Date.parse("2026-11-14T23:00:00Z");
    const asked: string[] = [];
    const check = new VideoCheck(async (id) => { asked.push(id); return answers.shift(); }, () => new Date(now));
    return { check, asked, later: (ms: number) => { now += ms; } };
  }

  it("waits 2, 5, 15 and 60 minutes, then a day", () => {
    assert.deepEqual(VIDEO_NO_ANSWER_BACKOFF_MS, [2, 5, 15, 60].map((m) => m * MIN));
    assert.equal(VIDEO_NO_ANSWER_BACKOFF_MS[0], VIDEO_NO_ANSWER_RETRY_MS);
  });

  it("asks less often each time YouTube does not answer", async () => {
    const { check, asked, later } = checker([undefined, 429, 503, undefined, undefined]);
    const see = () => check.saw("acme-rc", ["AAAAAAAAAAA"]);

    await see();
    assert.equal(asked.length, 1);

    // Second ask two minutes on; the third needs five more, not two.
    later(2 * MIN);
    await see();
    assert.equal(asked.length, 2);
    later(2 * MIN);
    await see();
    assert.equal(asked.length, 2, "not every two minutes for good");
    later(3 * MIN);
    await see();
    assert.equal(asked.length, 3);

    // Then fifteen, then an hour.
    later(14 * MIN);
    await see();
    assert.equal(asked.length, 3);
    later(MIN);
    await see();
    assert.equal(asked.length, 4);
    later(59 * MIN);
    await see();
    assert.equal(asked.length, 4);
    later(MIN);
    await see();
    assert.equal(asked.length, 5);

    // Five in a row: back to the day it used to wait.
    later(2 * 60 * MIN);
    await see();
    assert.equal(asked.length, 5, "capped at the old day");
    assert.equal(check.dead("acme-rc").size, 0, "no answer still changes nothing");
  });

  it("starts the wait again after a real answer", async () => {
    const { check, asked, later } = checker([undefined, undefined, 404, undefined, undefined]);
    const see = () => check.saw("acme-rc", ["AAAAAAAAAAA"]);

    await see();
    later(2 * MIN);
    await see();
    later(5 * MIN);
    await see(); // 404: dead, next ask after the 2-minute refusal wait
    assert.equal(asked.length, 3);
    assert.deepEqual([...check.dead("acme-rc")], ["AAAAAAAAAAA"]);

    later(2 * MIN);
    await see(); // no answer: the first in a new run
    assert.equal(asked.length, 4);
    assert.deepEqual([...check.dead("acme-rc")], ["AAAAAAAAAAA"], "a dead video stays dead on no answer");

    // Two minutes again, not the fifteen a third no-answer in a row would wait.
    later(2 * MIN);
    await see();
    assert.equal(asked.length, 5);
  });
});
