import assert from "node:assert/strict";
import { request as httpRequest } from "node:http";
import { afterEach, describe, it } from "node:test";
import { CLIP_MAX_BYTES } from "../src/clips.js";
import { ACME_TOKEN } from "./helpers.js";
import { API, SAM, setSwitch, startDriver, type DriverHarness } from "./driverexp-r1-helpers.js";

/**
 * BUG HUNT 9, server.ts (22 Oct 2026): an upload cut off by the 30-second
 * request ceiling. It was found on the podium video; since 28 Oct 2026 the
 * podium videos stay on the laptop (asked for by Asher) and the one upload
 * left that lifts the ceiling is a driver's clip, so the same two checks
 * are made on a clip. The video sweep and the delete-while-uploading race
 * went with the videos; the clips' own are in driverexp-r1-clips.test.ts.
 */

const CLIP = "11111111111111111111111111111111";

function mp3(size = 4000): Buffer {
  const bytes = Buffer.alloc(size, 9);
  bytes.write("ID3", 0, "latin1");
  return bytes;
}

let harness: DriverHarness | undefined;

afterEach(async () => {
  await harness?.close();
  harness = undefined;
});

/** Sends a clip body in pieces, with a pause between each, and gives back the status. */
function sendInPieces(h: DriverHarness, pieces: readonly Buffer[], pauseMs: number, length?: number): Promise<number> {
  const url = new URL(`${h.base}${API}/clip/${CLIP}?driver=${SAM}&label=A-Main`);
  const headers: Record<string, string> = { authorization: `Bearer ${ACME_TOKEN}`, "content-type": "audio/mpeg" };

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

describe("an upload arriving slowly", () => {
  it("is kept when its body comes in pieces with pauses between them", async () => {
    harness = await startDriver();
    await setSwitch(harness, "nights/open", true);
    const body = mp3(9000);

    const status = await sendInPieces(harness, [body.subarray(0, 3000), body.subarray(3000, 6000), body.subarray(6000)], 200, body.length);

    assert.equal(status, 200);
    assert.equal((await harness.clips.list("acme-rc")).length, 1);
  });

  it("still answers 413, not 400, for a body that runs past the limit without saying its length", async () => {
    harness = await startDriver();
    await setSwitch(harness, "nights/open", true);
    const piece = mp3(1024 * 1024);
    const pieces = Array.from({ length: Math.ceil(CLIP_MAX_BYTES / piece.length) + 1 }, () => piece);

    const status = await sendInPieces(harness, pieces, 0);

    assert.equal(status, 413);
    assert.equal((await harness.clips.list("acme-rc")).length, 0);
  });
});
