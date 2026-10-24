import assert from "node:assert/strict";
import { request as httpRequest } from "node:http";
import { afterEach, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import { MemoryVideoVault, VIDEO_KEEP_MS, VIDEO_MAX_BYTES, type VideoMeta } from "../src/videos.js";
import { ACME_TOKEN, call, start, tenants, type Harness } from "./helpers.js";

/**
 * BUG HUNT 9, server.ts (22 Oct 2026): podium videos cut off by the
 * 30-second request ceiling, videos past their week never deleted for a
 * club that stopped uploading, and an upload in flight writing a video
 * back after the club deleted everything.
 */

const RACE = "0123456789abcdef0123456789abcdef";
const VID1 = "11111111111111111111111111111111";
const NOW = new Date("2026-09-20T12:00:00.000Z");

function mp4(size = 4000): Buffer {
  const bytes = Buffer.alloc(size, 7);
  bytes.writeUInt32BE(24, 0);
  bytes.write("ftypisom", 4, "latin1");
  return bytes;
}

function meta(id: string, at: Date): VideoMeta {
  return { id, race: RACE, position: 1, name: "Bill", label: "A-Main", at: at.toISOString(), bytes: 10 };
}

let harness: Harness | undefined;

afterEach(async () => {
  await harness?.close();
  harness = undefined;
});

/** Sends a video body in pieces, with a pause between each, and gives back the status. */
function sendInPieces(h: Harness, pieces: readonly Buffer[], pauseMs: number, length?: number): Promise<number> {
  const url = new URL(`${h.base}/api/v1/c/acme-rc/video/${VID1}?race=${RACE}&position=1&name=Bill&label=A-Main`);
  const headers: Record<string, string> = { authorization: `Bearer ${ACME_TOKEN}`, "content-type": "video/mp4" };

  if (length !== undefined) {
    headers["content-length"] = String(length);
  }

  return new Promise<number>((resolve, reject) => {
    const req = httpRequest(url, { method: "POST", headers }, (res) => {
      res.resume();
      res.on("end", () => resolve(res.statusCode ?? 0));
    });
    req.on("error", reject);

    void (async () => {
      for (const piece of pieces) {
        await new Promise<void>((done) => req.write(piece, () => done()));
        await new Promise((done) => setTimeout(done, pauseMs));
      }

      req.end();
    })();
  });
}

describe("a podium video arriving slowly", () => {
  it("is kept when its body comes in pieces with pauses between them", async () => {
    const vault = new MemoryVideoVault();
    harness = await start(new MemoryStore(), tenants(), () => NOW, undefined, undefined, { videos: vault });
    const body = mp4(9000);

    const status = await sendInPieces(harness, [body.subarray(0, 3000), body.subarray(3000, 6000), body.subarray(6000)], 200, body.length);

    assert.equal(status, 200);
    assert.equal((await vault.list("acme-rc")).length, 1);
  });

  it("still answers 413, not 400, for a body that runs past the limit without saying its length", async () => {
    const vault = new MemoryVideoVault();
    harness = await start(new MemoryStore(), tenants(), () => NOW, undefined, undefined, { videos: vault });
    const piece = mp4(8 * 1024 * 1024);
    const pieces = Array.from({ length: Math.ceil(VIDEO_MAX_BYTES / piece.length) + 1 }, () => piece);

    const status = await sendInPieces(harness, pieces, 0);

    assert.equal(status, 413);
    assert.equal((await vault.list("acme-rc")).length, 0);
  });
});

describe("the video sweep", () => {
  it("deletes videos past their week for a club that never uploads again, and keeps the rest", async () => {
    const vault = new MemoryVideoVault();
    const old = new Date(NOW.getTime() - VIDEO_KEEP_MS - 60_000);
    await vault.put("acme-rc", meta("a".repeat(32), old), Buffer.from("x"));
    await vault.put("acme-rc", meta("b".repeat(32), NOW), Buffer.from("x"));

    harness = await start(new MemoryStore(), tenants(), () => NOW, undefined, undefined, { videos: vault });

    assert.equal(await harness.relay.sweepVideos(["acme-rc"]), 1);
    assert.deepEqual((await vault.list("acme-rc")).map((v) => v.id), ["b".repeat(32)]);
  });

  it("does nothing without a vault, or with nothing to name the clubs", async () => {
    harness = await start(new MemoryStore(), tenants(), () => NOW);
    assert.equal(await harness.relay.sweepVideos(["acme-rc"]), 0);
  });
});

describe("a delete while a video is on its way", () => {
  it("keeps nothing of an upload that finishes after the club deleted everything", async () => {
    const vault = new MemoryVideoVault();
    harness = await start(new MemoryStore(), tenants(), () => NOW, undefined, undefined, { videos: vault });
    const body = mp4();
    const url = new URL(`${harness.base}/api/v1/c/acme-rc/video/${VID1}?race=${RACE}&position=1&name=Bill&label=A-Main`);

    let firstHalfSent!: () => void;
    const sentHalf = new Promise<void>((resolve) => (firstHalfSent = resolve));
    let deleteDone!: () => void;
    const afterDelete = new Promise<void>((resolve) => (deleteDone = resolve));

    const status = new Promise<number>((resolve, reject) => {
      const req = httpRequest(
        url,
        {
          method: "POST",
          headers: { authorization: `Bearer ${ACME_TOKEN}`, "content-type": "video/mp4", "content-length": String(body.length) },
        },
        (res) => {
          res.resume();
          res.on("end", () => resolve(res.statusCode ?? 0));
        },
      );
      req.on("error", reject);
      req.write(body.subarray(0, 2000), () => firstHalfSent());
      // The rest goes once the delete has finished.
      void afterDelete.then(() => req.end(body.subarray(2000)));
    });

    await sentHalf;
    // Give the relay a moment to start reading the body.
    await new Promise((resolve) => setTimeout(resolve, 100));
    const deleted = await call(harness, "DELETE", "/api/v1/c/acme-rc", { token: ACME_TOKEN });
    deleteDone();
    assert.equal(deleted.status, 200);

    assert.equal(await status, 410);
    assert.equal((await vault.list("acme-rc")).length, 0);
  });
});
