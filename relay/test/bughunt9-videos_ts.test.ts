import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { VIDEO_MAX_CLUB_BYTES, videosToDrop, type VideoMeta } from "../src/videos.js";

/**
 * BUG HUNT 9 (22 Oct 2026): when the club's 1 GB ran out, a big newer video
 * was thrown away while a smaller older one further down was kept.
 */

const MB = 1024 * 1024;
const NOW = Date.parse("2026-10-22T20:00:00Z");

function video(n: number, hoursAgo: number, bytes: number): VideoMeta {
  return {
    id: String(n).padStart(32, "0"),
    race: "0123456789abcdef0123456789abcdef",
    position: 1,
    name: `Driver ${n}`,
    label: "A-Main",
    at: new Date(NOW - hoursAgo * 3600 * 1000).toISOString(),
    bytes,
  };
}

describe("videosToDrop when the bytes run out", () => {
  it("never keeps an older video after dropping a newer one", () => {
    const newest = video(1, 1, VIDEO_MAX_CLUB_BYTES - 20 * MB);
    const bigger = video(2, 48, 30 * MB);
    const smaller = video(3, 120, 10 * MB);

    const drop = videosToDrop([smaller, newest, bigger], NOW).map((v) => v.id);

    assert.deepEqual(drop.sort(), [bigger.id, smaller.id].sort());
  });

  it("keeps everything that fits", () => {
    const held = [video(1, 1, 10 * MB), video(2, 2, 10 * MB), video(3, 3, 10 * MB)];

    assert.deepEqual(videosToDrop(held, NOW), []);
  });

  it("makes room for the incoming upload from the oldest end", () => {
    const a = video(1, 1, 400 * MB);
    const b = video(2, 2, 400 * MB);
    const c = video(3, 3, 100 * MB);

    const drop = videosToDrop([a, b, c], NOW, 300 * MB).map((v) => v.id);

    assert.deepEqual(drop.sort(), [b.id, c.id].sort());
  });
});
