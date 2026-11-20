import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { icsText, nightsFeedWith } from "../src/ics.js";

/**
 * Bug hunt 22 (19 Nov 2026): nights.ics. The laptop's `until` is 06:00
 * local the morning after the night (NextNight.UntilUtc), not the night's
 * own day - so tonight still looked "to come" all evening.
 */

const NIGHT = (day: string, until: string, extra: Record<string, unknown> = {}) => ({
  day, title: "Club Night", venue: "Kent Hobbies", classes: ["Stock"], firstRace: "19:00",
  off: false, offText: "", until, ...extra,
});

// America/New_York: 06:00 EST is 11:00Z.
const THURSDAY = NIGHT("2026-11-26", "2026-11-27T11:00:00.000Z");
const NEXT_THURSDAY = NIGHT("2026-12-03", "2026-12-04T11:00:00.000Z");

describe("nights.ics keeps tonight (bug hunt 22)", () => {
  const tonight = new Date("2026-11-27T03:00:00.000Z"); // 22:00 EST on the 26th

  it("keeps tonight's night when next week's is announced that evening", () => {
    let feed = nightsFeedWith(undefined, THURSDAY, new Date("2026-11-20T12:00:00.000Z"));
    feed = nightsFeedWith(feed, NEXT_THURSDAY, tonight);
    assert.deepEqual(feed.nights.map((n) => n["day"]), ["2026-11-26", "2026-12-03"]);
  });

  it("keeps tonight's night when the card is taken down that evening", () => {
    const feed = nightsFeedWith(undefined, THURSDAY, new Date("2026-11-20T12:00:00.000Z"));
    const withdrawn = nightsFeedWith(feed, { day: "", until: "1970-01-01T00:00:00.000Z" }, tonight);
    assert.deepEqual(withdrawn.nights.map((n) => n["day"]), ["2026-11-26"]);
  });

  it("still drops a night moved before its day began", () => {
    const before = new Date("2026-11-25T12:00:00.000Z");
    let feed = nightsFeedWith(undefined, THURSDAY, before);
    feed = nightsFeedWith(feed, NIGHT("2026-11-27", "2026-11-28T11:00:00.000Z"), before);
    assert.deepEqual(feed.nights.map((n) => n["day"]), ["2026-11-27"]);
  });
});

describe("icsText escapes a bare CR (bug hunt 22)", () => {
  it("never lets a carriage return through to start a new line", () => {
    assert.doesNotMatch(icsText("Hall\rBEGIN:VEVENT\rSUMMARY:x"), /\r/);
    assert.equal(icsText("a\r\nb\nc\rd"), "a\\nb\\nc\\nd");
  });
});
