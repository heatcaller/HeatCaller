import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { liveVideo, raceVideo, streamFrom } from "../src/video.js";
import { ACME_TOKEN, call, start } from "./helpers.js";

/**
 * RACE VIDEO, V1 (14 Nov 2026, plans/race-video-access-plan.md): each race's
 * moment in the club's YouTube stream, worked out on the relay from the
 * laptop's private stream document - never a list of every video for
 * anyone to read.
 */

const SID = "0123456789abcdef0123456789abcdef";
const JUNIOR = "fedcba9876543210fedcba9876543210";
const PIECE_AT = "2026-11-14T23:00:00.000Z";
const STREAM = {
  on: true,
  publicResults: true,
  leadIn: 10,
  pieces: [{ videoId: "AAAAAAAAAAA", startedUtc: PIECE_AT, endedUtc: null, nudge: 0 }],
  hidden: [] as string[],
  juniors: [JUNIOR],
};
const RACE = { sid: SID, startedUtc: "2026-11-14T23:12:00.000Z", endedUtc: "2026-11-14T23:16:00.000Z" };

describe("a race's moment in the stream (V1)", () => {
  it("opens at the grid: green flag less the lead-in, and stops just after the flag", () => {
    const v = raceVideo(streamFrom(STREAM), RACE, true)!;
    assert.deepEqual(v, { id: "AAAAAAAAAAA", t: 710, end: 970 });
  });

  it("moves with the club's nudge, and never opens before the stream began", () => {
    assert.equal(raceVideo(streamFrom({ ...STREAM, pieces: [{ ...STREAM.pieces[0], nudge: -4 }] }), RACE, true)!.t, 706);
    const early = { ...RACE, startedUtc: "2026-11-14T23:00:03.000Z" };
    assert.equal(raceVideo(streamFrom(STREAM), early, true)!.t, 0);
  });

  it("gives one driver's laps, and which is their best", () => {
    const v = raceVideo(streamFrom(STREAM), RACE, false, { startMs: 4000, lapsMs: [9000, 8500, 8800] })!;
    assert.deepEqual(v.laps, [724, 733, 741.5]);
    assert.equal(v.best, 1);
  });

  it("has no video when it's off, the race is hidden, outside a piece, or the piece has no link", () => {
    assert.equal(raceVideo(streamFrom({ ...STREAM, on: false }), RACE, true), undefined);
    assert.equal(raceVideo(streamFrom({ ...STREAM, hidden: [SID] }), RACE, false), undefined);
    assert.equal(raceVideo(streamFrom({ ...STREAM, pieces: [{ ...STREAM.pieces[0], endedUtc: "2026-11-14T23:14:00.000Z" }] }), RACE, true), undefined, "the stream dropped mid-race");
    assert.equal(raceVideo(streamFrom({ ...STREAM, pieces: [{ ...STREAM.pieces[0], videoId: "" }] }), RACE, true), undefined);
    assert.equal(raceVideo(streamFrom({ ...STREAM, pieces: [{ ...STREAM.pieces[0], videoId: "not-an-id!!" }] }), RACE, true), undefined);
  });

  it("keeps juniors' races and own-night-only clubs off public results, but not off the driver's own night", () => {
    const junior = { ...RACE, sid: JUNIOR };
    assert.equal(raceVideo(streamFrom(STREAM), junior, true), undefined);
    assert.ok(raceVideo(streamFrom(STREAM), junior, false));
    assert.equal(raceVideo(streamFrom({ ...STREAM, publicResults: false }), RACE, true), undefined);
    assert.ok(raceVideo(streamFrom({ ...STREAM, publicResults: false }), RACE, false));
  });

  it("is live only while a piece with a link is open", () => {
    assert.equal(liveVideo(streamFrom(STREAM)), "AAAAAAAAAAA");
    assert.equal(liveVideo(streamFrom({ ...STREAM, pieces: [{ ...STREAM.pieces[0], endedUtc: "2026-11-14T23:30:00.000Z" }] })), undefined);
    assert.equal(liveVideo(streamFrom({ ...STREAM, on: false })), undefined);
  });
});

describe("the relay's video routes (V1)", () => {
  async function relay(now: Date) {
    const h = await start(undefined, undefined, () => now);
    await call(h, "POST", "/api/v1/c/acme-rc/publish/stream?seq=1", { token: ACME_TOKEN, json: STREAM });
    await call(h, "POST", `/api/v1/c/acme-rc/publish/results/${SID}?seq=1`, { token: ACME_TOKEN, json: { id: SID, ...RACE, results: [{ pos: 1, name: "Sam P.", startMs: 4000, lapsMs: [9000, 8500] }] } });
    await call(h, "POST", `/api/v1/c/acme-rc/publish/results/${JUNIOR}?seq=1`, { token: ACME_TOKEN, json: { id: JUNIOR, ...RACE, sid: JUNIOR, results: [] } });
    return h;
  }

  it("never serves the stream document itself", async () => {
    const h = await relay(new Date("2026-11-14T23:20:00.000Z"));
    try {
      assert.equal((await call(h, "GET", "/api/v1/c/acme-rc/snapshot/stream")).status, 404);
    } finally {
      await h.close();
    }
  });

  it("answers one public race, with a driver's laps on asking, and 404 for a junior's", async () => {
    const h = await relay(new Date("2026-11-14T23:20:00.000Z"));
    try {
      const race = await call(h, "GET", `/api/v1/c/acme-rc/video/race/${SID}?n=${encodeURIComponent("Sam P.")}`);
      assert.equal(race.status, 200);
      assert.equal(race.body.video.id, "AAAAAAAAAAA");
      assert.equal(race.body.video.t, 710);
      assert.deepEqual(race.body.video.laps, [724, 733]);
      assert.equal((await call(h, "GET", `/api/v1/c/acme-rc/video/race/${JUNIOR}`)).status, 404);
      assert.equal((await call(h, "GET", "/api/v1/c/acme-rc/video/race/nope")).status, 404);
    } finally {
      await h.close();
    }
  });

  it("is live only while the laptop is still sending the race", async () => {
    let now = new Date("2026-11-14T23:20:00.000Z");
    const h = await start(undefined, undefined, () => now);
    try {
      await call(h, "POST", "/api/v1/c/acme-rc/publish/stream?seq=1", { token: ACME_TOKEN, json: STREAM });
      await call(h, "POST", "/api/v1/c/acme-rc/publish/state?seq=1", { token: ACME_TOKEN, json: { state: "Racing" } });
      assert.equal((await call(h, "GET", "/api/v1/c/acme-rc/video/live")).body.live, "AAAAAAAAAAA");
      now = new Date("2026-11-14T23:25:00.000Z");
      assert.equal((await call(h, "GET", "/api/v1/c/acme-rc/video/live")).body.live, null, "five minutes without a frame is not live");
    } finally {
      await h.close();
    }
  });
});
