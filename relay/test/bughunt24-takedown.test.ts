import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { FileStore } from "../src/store.js";
import { activeOf, applyTakedown, readTakedowns, restoreTakedown, withoutTakenDown, type TakedownRecord } from "../src/takedown.js";

/**
 * BUG HUNT 24 (23 Nov 2026): a track picture taken down after a copyright
 * notice was held down by the track's exact name only. The tracks document
 * has no id, so renaming the layout on the laptop - even a letter's case -
 * published the same picture under a name no record matched, and it was
 * served again while takedown-list still said it was down.
 */

const PIC = "data:image/png;base64,iVBORw0KGgpNQVJLLVRSQUNLLVBJQw==";
const OTHER_PIC = "data:image/png;base64,iVBORw0KGgpPVEhFUi1QSUM=";
const now = new Date("2026-11-23T12:00:00.000Z");

describe("bug hunt 24: a taken-down track picture stays down when the layout is renamed", () => {
  let dataDir: string;

  beforeEach(async () => {
    dataDir = await mkdtemp(join(tmpdir(), "hc-bh24-takedown-"));
  });

  afterEach(async () => {
    await rm(dataDir, { recursive: true, force: true });
  });

  const publishTracks = (tracks: unknown[]) =>
    new FileStore(dataDir).putSnapshot("acme-rc", "tracks", { seq: 1, publishedAt: now.toISOString(), body: { tracks } });

  const takeDown = (ref: string) =>
    applyTakedown({ dataDir, club: "acme-rc", kind: "track", ref, notice: "DMCA 2026-024", now, owner: undefined });

  const active = async () => activeOf((await readTakedowns(dataDir, "acme-rc")).takedowns);

  it("records the picture's hash and keeps it down under a new name, while a different picture is served", async () => {
    await publishTracks([{ name: "Tight Layout", image: PIC }]);
    const down = await takeDown("Tight Layout");
    assert.equal(down.ok, true);

    const stored = (await readTakedowns(dataDir, "acme-rc")).takedowns[0];
    assert.equal(stored?.imageHash, createHash("sha256").update(PIC, "utf8").digest("hex"));

    const renamed = {
      tracks: [
        { name: "Short Course", image: PIC, record: 9.1 },
        { name: "Long Course", image: OTHER_PIC },
      ],
    };
    assert.deepEqual(withoutTakenDown("tracks", renamed, await active()), {
      tracks: [
        { name: "Short Course", record: 9.1 },
        { name: "Long Course", image: OTHER_PIC },
      ],
    });
  });

  it("keeps it down after a case-only rename, even for a record with no picture hash", async () => {
    const old: TakedownRecord = {
      id: "td-000000000024",
      kind: "track",
      ref: "Tight Layout",
      notice: "N1",
      state: "active",
      takenDownAt: "2026-11-23T00:00:00.000Z",
      restoredAt: null,
      restoreReason: null,
    };
    const body = { tracks: [{ name: "Tight layout", image: PIC }] };
    assert.deepEqual(withoutTakenDown("tracks", body, activeOf([old])), { tracks: [{ name: "Tight layout" }] });
  });

  it("running takedown again on an old record without a hash adds the picture's hash", async () => {
    // Taken down while nothing was published: no hash to record.
    const first = await takeDown("Tight Layout");
    assert.equal(first.ok, true);
    assert.equal((await readTakedowns(dataDir, "acme-rc")).takedowns[0]?.imageHash, undefined);

    await publishTracks([{ name: "Tight Layout", image: PIC }]);
    const again = await takeDown("Tight Layout");
    assert.equal(again.ok && again.already, true);

    const records = (await readTakedowns(dataDir, "acme-rc")).takedowns;
    assert.equal(records.length, 1);
    assert.equal(records[0]?.imageHash, createHash("sha256").update(PIC, "utf8").digest("hex"));
    assert.deepEqual(withoutTakenDown("tracks", { tracks: [{ name: "Renamed", image: PIC }] }, await active()), { tracks: [{ name: "Renamed" }] });
  });

  it("a restore brings the picture back under the new name", async () => {
    await publishTracks([{ name: "Tight Layout", image: PIC }]);
    const down = await takeDown("Tight Layout");
    if (!down.ok) throw new Error(down.error);

    const back = await restoreTakedown({ dataDir, club: "acme-rc", id: down.id, reason: "counter-notice", now, owner: undefined });
    assert.equal(back.ok, true);

    const body = { tracks: [{ name: "Short Course", image: PIC }] };
    assert.equal(withoutTakenDown("tracks", body, await active()), body);
  });
});
