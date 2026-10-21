import assert from "node:assert/strict";
import { mkdir, mkdtemp, readdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, describe, it } from "node:test";
import { FileStore } from "../src/store.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * BUG HUNT 8, server.ts (20 Oct 2026).
 *
 * A corrupt voteballot.json that kept the room vote dead for good, and a
 * switch-off clear that skipped any file it could not read.
 */

let harness: Harness | undefined;
const scratch: string[] = [];

afterEach(async () => {
  await harness?.close();
  harness = undefined;

  for (const dir of scratch.splice(0)) {
    await rm(dir, { recursive: true, force: true });
  }
});

async function fileRelay(): Promise<{ live: Harness; root: string }> {
  const root = await mkdtemp(join(tmpdir(), "hc-bh8-"));
  scratch.push(root);
  const live = (harness = await start(new FileStore(root)));
  return { live, root };
}

const VOTE = "/api/v1/c/acme-rc/vote";
const BILL = { id: "t1", name: "Bill Hargrove" };
const SAM = { id: "t2", name: "Sam Teague" };

const open = (live: Harness, race: string) =>
  call(live, "POST", `${VOTE}/open`, { token: ACME_TOKEN, json: { enabled: true, open: true, race, drivers: [BILL, SAM] } });

describe("bug hunt 8: a corrupt vote ballot file", () => {
  it("is replaced by the next vote/open instead of answering 503 for good", async () => {
    const { live, root } = await fileRelay();

    assert.equal((await open(live, "Stock A-Main")).status, 200);

    // A power cut left the ballot short.
    await writeFile(join(root, "acme-rc", "snapshot", "voteballot.json"), '{"seq":1,"pub');

    // Phones see no vote rather than a 503.
    assert.equal((await call(live, "GET", `${VOTE}/ballot`)).status, 404);

    // And the laptop's next open writes a good file over it.
    assert.equal((await open(live, "Stock A-Main")).status, 200);
    assert.equal((await call(live, "GET", `${VOTE}/ballot`)).status, 200);
    assert.equal((await call(live, "POST", `${VOTE}/send`, { json: { d: BILL.id } })).status, 200);
  });
});

describe("bug hunt 8: clearing on close or a new race", () => {
  it("deletes a vote file that will not parse", async () => {
    const { live, root } = await fileRelay();

    assert.equal((await open(live, "Stock A-Main")).status, 200);

    const dir = join(root, "acme-rc", "collection", "votes");
    await mkdir(dir, { recursive: true });
    await writeFile(join(dir, "broken.json"), "{not json");

    // A different race clears every vote held, the unreadable one too.
    assert.equal((await open(live, "Stock B-Main")).status, 200);
    assert.deepEqual((await readdir(dir)).filter((name) => name.endsWith(".json")), []);
  });
});
