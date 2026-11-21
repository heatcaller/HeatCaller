import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { nightsFeedWith } from "../src/ics.js";

/**
 * Bug hunt 23 (21 Nov 2026): nights.ics. Since bug hunt 22 a night counted
 * as begun from 06:00 on its own day, so one moved on the day itself -
 * rained off in the afternoon, on tomorrow instead - stayed in every
 * calendar beside its new date. It has begun when its first race is due.
 */

const NIGHT = (day: string, until: string, extra: Record<string, unknown> = {}) => ({
  day, title: "Club Night", venue: "Kent Hobbies", classes: ["Stock"], firstRace: "19:00",
  off: false, offText: "", until, ...extra,
});

// America/New_York: 06:00 EST is 11:00Z, so 19:00 EST is 00:00Z the next day.
const THURSDAY = NIGHT("2026-11-26", "2026-11-27T11:00:00.000Z");
const FRIDAY = NIGHT("2026-11-27", "2026-11-28T11:00:00.000Z");
const ANNOUNCED = new Date("2026-11-20T12:00:00.000Z");

describe("nights.ics drops a night moved on its own day (bug hunt 23)", () => {
  it("drops tonight when it is moved that afternoon, before its first race", () => {
    let feed = nightsFeedWith(undefined, THURSDAY, ANNOUNCED);
    feed = nightsFeedWith(feed, FRIDAY, new Date("2026-11-26T20:00:00.000Z")); // 15:00 EST
    assert.deepEqual(feed.nights.map((n) => n["day"]), ["2026-11-27"]);
  });

  it("keeps tonight once its first race is due", () => {
    let feed = nightsFeedWith(undefined, THURSDAY, ANNOUNCED);
    feed = nightsFeedWith(feed, NIGHT("2026-12-03", "2026-12-04T11:00:00.000Z"), new Date("2026-11-27T00:00:00.000Z")); // 19:00 EST
    assert.deepEqual(feed.nights.map((n) => n["day"]), ["2026-11-26", "2026-12-03"]);
  });

  it("with no first race time, the day begins at 06:00 as before", () => {
    let feed = nightsFeedWith(undefined, NIGHT("2026-11-26", "2026-11-27T11:00:00.000Z", { firstRace: "" }), ANNOUNCED);
    feed = nightsFeedWith(feed, FRIDAY, new Date("2026-11-26T20:00:00.000Z"));
    assert.deepEqual(feed.nights.map((n) => n["day"]), ["2026-11-26", "2026-11-27"]);
  });

  it("a first race after midnight is on the night's own morning", () => {
    const late = NIGHT("2026-11-26", "2026-11-27T11:00:00.000Z", { firstRace: "00:30" });
    let feed = nightsFeedWith(undefined, late, ANNOUNCED);
    feed = nightsFeedWith(feed, FRIDAY, new Date("2026-11-27T04:00:00.000Z")); // 23:00 EST, before 00:30
    assert.deepEqual(feed.nights.map((n) => n["day"]), ["2026-11-27"]);
  });
});
