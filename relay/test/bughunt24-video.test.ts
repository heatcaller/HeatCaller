import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  hiddenAfter, mergedStreamBody, raceVideo, STREAM_MAX_PIECES, STREAM_NIGHTS_KEPT, STREAM_OPEN_PIECE_MAX_MS, streamFrom,
  VIDEO_NO_ANSWER_RETRY_MS, VideoCheck,
} from "../src/video.js";

/**
 * BUG HUNT 24 (23 Nov 2026), race video on the relay:
 *  - #64: a piece left open on an earlier night claimed every later race
 *    with no video of its own.
 *  - #366: a video YouTube didn't answer about was not asked again for a day.
 *  - #367: a race hidden and unticked after the night's pieces were let go
 *    stayed hidden for good.
 */

const MIN = 60 * 1000;
const NIGHT1 = "11111111111111111111111111111111";
const NIGHT2 = "22222222222222222222222222222222";
const NIGHT3 = "33333333333333333333333333333333";

const piece = (videoId: string, startedUtc: string, endedUtc: string | null, hidden = false) => ({ videoId, startedUtc, endedUtc, nudge: 0, hidden });
const doc = (pieces: unknown[], hidden: string[] = []) => ({ on: true, publicResults: true, leadIn: 10, pieces, hidden, juniors: [] });
const race = (sid: string, green: string) => ({ sid, startedUtc: green, endedUtc: new Date(Date.parse(green) + 4 * MIN).toISOString() });

/** Races worked out of the latest document and the kept nights, as the relay does. */
const merged = (latest: unknown, nights: unknown[]) =>
  streamFrom(mergedStreamBody(latest, nights, []), new Set(), STREAM_MAX_PIECES * (STREAM_NIGHTS_KEPT + 1));

describe("bug hunt 24 #64: a piece left open on an earlier night", () => {
  const night1 = doc([piece("AAAAAAAAAAA", "2026-11-07T23:00:00.000Z", null)]);

  it("gives no video to a later night with no stream", () => {
    const stream = merged(doc([]), [night1]);
    assert.equal(raceVideo(stream, race(NIGHT2, "2026-11-14T23:10:00.000Z"), false), undefined);
    assert.equal(raceVideo(stream, race(NIGHT2, "2026-11-14T23:10:00.000Z"), true), undefined);
  });

  it("gives no video while tonight's link isn't in yet, or tonight's piece is hidden, and keeps its own night's", () => {
    const unlinked = doc([piece("", "2026-11-14T23:00:00.000Z", null)]);
    assert.equal(raceVideo(merged(unlinked, [unlinked, night1]), race(NIGHT2, "2026-11-14T23:10:00.000Z"), false), undefined);
    const hidden = doc([piece("BBBBBBBBBBB", "2026-11-14T23:00:00.000Z", null, true)]);
    assert.equal(raceVideo(merged(hidden, [hidden, night1]), race(NIGHT2, "2026-11-14T23:10:00.000Z"), false), undefined);
    assert.equal(raceVideo(merged(unlinked, [unlinked, night1]), race(NIGHT1, "2026-11-07T23:10:00.000Z"), false)?.id, "AAAAAAAAAAA");
  });

  it("does not take a closed later night's race from the older open piece", () => {
    const night2 = doc([piece("BBBBBBBBBBB", "2026-11-14T23:00:00.000Z", "2026-11-14T23:59:00.000Z")]);
    const night3 = doc([piece("CCCCCCCCCCC", "2026-11-21T23:00:00.000Z", null)]);
    const v = raceVideo(merged(night3, [night1, night2, night3]), race(NIGHT2, "2026-11-14T23:10:00.000Z"), false);
    assert.equal(v?.id, "BBBBBBBBBBB");
    assert.equal(v?.t, 590);
    assert.equal(raceVideo(merged(night3, [night1, night2, night3]), race(NIGHT3, "2026-11-21T23:10:00.000Z"), false)?.id, "CCCCCCCCCCC");
  });

  it("still counts tonight's open piece for its own night, and no longer", () => {
    const stream = streamFrom(night1);
    assert.equal(raceVideo(stream, race(NIGHT1, "2026-11-08T01:00:00.000Z"), false)?.id, "AAAAAAAAAAA", "two hours in");
    const late = new Date(Date.parse("2026-11-07T23:00:00.000Z") + STREAM_OPEN_PIECE_MAX_MS + MIN).toISOString();
    assert.equal(raceVideo(stream, race(NIGHT1, late), false), undefined, "past its own night");
  });
});

describe("bug hunt 24 #366: YouTube not answering", () => {
  function checker(answers: (number | undefined)[]) {
    let now = Date.parse("2026-11-14T23:00:00Z");
    const asked: string[] = [];
    const check = new VideoCheck(async (id) => { asked.push(id); return answers.shift(); }, () => new Date(now));
    return { check, asked, later: (ms: number) => { now += ms; } };
  }

  it("asks again within minutes when the first ask got no answer", async () => {
    const { check, asked, later } = checker([undefined, 404]);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(asked.length, 1);
    assert.equal(check.dead("acme-rc").size, 0, "no answer still changes nothing");
    later(MIN);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(asked.length, 1, "not straight away");
    later(VIDEO_NO_ANSWER_RETRY_MS);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(asked.length, 2, "asked again a few minutes on, not a day");
    assert.deepEqual([...check.dead("acme-rc")], ["AAAAAAAAAAA"]);
  });

  it("treats a server error the same, and a video that plays stays playing", async () => {
    const { check, asked, later } = checker([500, 200]);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    later(VIDEO_NO_ANSWER_RETRY_MS);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(asked.length, 2);
    assert.equal(check.dead("acme-rc").size, 0);
    later(VIDEO_NO_ANSWER_RETRY_MS);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(asked.length, 2, "once answered, back to the daily check");
  });
});

describe("bug hunt 24 #367: unticked after the night's pieces were let go", () => {
  it("shows the race again", () => {
    const none = doc([]);
    const noneHid = doc([], [NIGHT1]);
    const kept = hiddenAfter([], none, noneHid);
    assert.deepEqual(kept, [NIGHT1]);
    assert.deepEqual(hiddenAfter(kept, noneHid, none), [], "unticked with no pieces held: shown again");
  });

  it("still keeps a race hidden when the laptop lets its night go, or a new night starts", () => {
    const night1 = doc([piece("AAAAAAAAAAA", "2026-11-07T23:00:00.000Z", null)], [NIGHT1]);
    assert.deepEqual(hiddenAfter([NIGHT1], night1, doc([])), [NIGHT1], "the let-go is not an unhide");
    const night2 = doc([piece("BBBBBBBBBBB", "2026-11-14T23:00:00.000Z", null)]);
    assert.deepEqual(hiddenAfter([NIGHT1], night1, night2), [NIGHT1], "a new night is not an unhide");
  });
});
