import assert from "node:assert/strict";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import { DiskVideoVault, MemoryVideoVault, VIDEO_KEEP_MS, videosToDrop, type VideoMeta } from "../src/videos.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * THE PODIUM'S HIGHLIGHT VIDEOS (21 Oct 2026). The laptop sends the top
 * three's videos; a phone watches and downloads them from the race's page
 * (the Big Screen's QR code) or from the driver's own night. Only 1st to
 * 3rd are ever taken.
 */

const API = "/api/v1/c/acme-rc";
const RACE = "0123456789abcdef0123456789abcdef";
const VID1 = "11111111111111111111111111111111";
const VID2 = "22222222222222222222222222222222";
const VID4 = "44444444444444444444444444444444";
const BILL = "a1b2c3d4e5f60718293a";

/** The smallest thing that passes for an mp4: a box size, "ftyp", a brand - then filler. */
function mp4(size = 4000): Buffer {
  const bytes = Buffer.alloc(size, 7);
  bytes.writeUInt32BE(24, 0);
  bytes.write("ftypisom", 4, "latin1");
  return bytes;
}

let harness: Harness;
let vault: MemoryVideoVault;

beforeEach(async () => {
  vault = new MemoryVideoVault();
  harness = await start(new MemoryStore(), undefined, undefined, undefined, undefined, { videos: vault });
});

afterEach(async () => {
  await harness.close();
});

async function upload(id: string, position: number, options: { token?: string; body?: Buffer; driver?: string; club?: string } = {}) {
  const query = new URLSearchParams({ race: RACE, position: String(position), name: "Bill Hargrove", label: "A-Main" });

  if (options.driver !== undefined) {
    query.set("driver", options.driver);
  }

  const response = await fetch(`${harness.base}/api/v1/c/${options.club ?? "acme-rc"}/video/${id}?${query.toString()}`, {
    method: "POST",
    headers: {
      authorization: `Bearer ${options.token ?? ACME_TOKEN}`,
      "content-type": "video/mp4",
    },
    body: new Uint8Array(options.body ?? mp4()),
  });

  const text = await response.text();
  return { status: response.status, json: JSON.parse(text) as Record<string, unknown> };
}

describe("taking a podium video", () => {
  it("takes 1st, 2nd and 3rd and says where its page is", async () => {
    const answer = await upload(VID1, 1, { driver: BILL });

    assert.equal(answer.status, 200);
    assert.equal(answer.json["page"], `/c/acme-rc/videos/${RACE}`);
    assert.equal((await vault.list("acme-rc")).length, 1);
  });

  it("refuses 4th and anything else off the podium", async () => {
    for (const position of [0, 4, 5, 12, -1]) {
      const answer = await upload(VID4, position);
      assert.equal(answer.status, 400, `position ${position}`);
    }

    assert.equal((await vault.list("acme-rc")).length, 0);
  });

  it("needs the club's own token", async () => {
    assert.equal((await upload(VID1, 1, { token: BROOK_TOKEN })).status, 403);

    const none = await fetch(`${harness.base}${API}/video/${VID1}?race=${RACE}&position=1`, { method: "POST", body: new Uint8Array(mp4()) });
    await none.text();
    assert.equal(none.status, 401);
    assert.equal((await vault.list("acme-rc")).length, 0);
  });

  it("refuses something that is not an mp4", async () => {
    const answer = await upload(VID1, 1, { body: Buffer.from("<html>not a video at all</html>") });
    assert.equal(answer.status, 400);
  });

  it("refuses ids that are not 128 random bits", async () => {
    assert.equal((await upload("abc", 1)).status, 400);
  });
});

describe("watching and keeping one", () => {
  it("lists the race's podium, in order, on its page", async () => {
    await upload(VID2, 2);
    await upload(VID1, 1);

    const page = await call(harness, "GET", `/c/acme-rc/videos/${RACE}`);

    assert.equal(page.status, 200);
    assert.ok(page.text.indexOf(VID1) < page.text.indexOf(VID2), "1st before 2nd");
    assert.match(page.text, /download=1/);
    assert.match(page.text, /A-Main podium/);
  });

  it("says plainly when a race has nothing", async () => {
    const page = await call(harness, "GET", `/c/acme-rc/videos/${RACE}`);
    assert.equal(page.status, 200);
    assert.match(page.text, /kept for a week/);
  });

  it("serves the whole video, and in ranges for a phone's player", async () => {
    const body = mp4(5000);
    await upload(VID1, 1, { body });

    const whole = await fetch(`${harness.base}/c/acme-rc/video/${VID1}.mp4`);
    const all = Buffer.from(await whole.arrayBuffer());
    assert.equal(whole.status, 200);
    assert.equal(whole.headers.get("content-type"), "video/mp4");
    assert.equal(whole.headers.get("accept-ranges"), "bytes");
    assert.ok(all.equals(body));

    const part = await fetch(`${harness.base}/c/acme-rc/video/${VID1}.mp4`, { headers: { range: "bytes=100-199" } });
    const piece = Buffer.from(await part.arrayBuffer());
    assert.equal(part.status, 206);
    assert.equal(part.headers.get("content-range"), "bytes 100-199/5000");
    assert.ok(piece.equals(body.subarray(100, 200)));

    const tail = await fetch(`${harness.base}/c/acme-rc/video/${VID1}.mp4`, { headers: { range: "bytes=-10" } });
    assert.equal(tail.status, 206);
    assert.equal(Buffer.from(await tail.arrayBuffer()).length, 10);

    const past = await fetch(`${harness.base}/c/acme-rc/video/${VID1}.mp4`, { headers: { range: "bytes=9000-" } });
    await past.arrayBuffer();
    assert.equal(past.status, 416);
  });

  it("hands it over as a file with ?download=1", async () => {
    await upload(VID1, 1);

    const answer = await fetch(`${harness.base}/c/acme-rc/video/${VID1}.mp4?download=1`);
    await answer.arrayBuffer();
    assert.match(answer.headers.get("content-disposition") ?? "", /^attachment; filename="A-Main - Bill Hargrove\.mp4"$/);
  });

  it("is not another club's to serve", async () => {
    await upload(VID1, 1);

    const answer = await fetch(`${harness.base}/c/brookside/video/${VID1}.mp4`);
    await answer.text();
    assert.equal(answer.status, 404);
  });
});

describe("a driver's own night", () => {
  it("carries their podium videos and nobody else's", async () => {
    await call(harness, "POST", `${API}/nights/open`, { token: ACME_TOKEN, json: { open: true } });
    await call(harness, "POST", `${API}/publish/nights/${BILL}?seq=1`, { token: ACME_TOKEN, json: { name: "Bill", said: ["A win."] } });

    await upload(VID1, 1, { driver: BILL });
    await upload(VID2, 2, { driver: "0f1e2d3c4b5a69788796" });

    const answer = await call(harness, "POST", `${API}/night`, { json: { id: BILL } });
    const videos = (answer.body as { videos?: { url: string; download: string; position: number }[] }).videos ?? [];

    assert.equal(answer.status, 200);
    assert.equal(videos.length, 1);
    assert.equal(videos[0]?.url, `/c/acme-rc/video/${VID1}.mp4`);
    assert.equal(videos[0]?.position, 1);
  });
});

describe("keeping only what a relay can carry", () => {
  const meta = (id: string, daysAgo: number, bytes = 10): VideoMeta => ({
    id,
    race: RACE,
    position: 1,
    name: "",
    label: "",
    at: new Date(Date.UTC(2026, 9, 21) - daysAgo * 24 * 60 * 60 * 1000).toISOString(),
    bytes,
  });

  it("drops anything older than a week, and never the newest", () => {
    const now = Date.UTC(2026, 9, 21);
    const dropped = videosToDrop([meta(VID1, 8), meta(VID2, 1)], now);

    assert.deepEqual(dropped.map((v) => v.id), [VID1]);
    assert.ok(VIDEO_KEEP_MS > 0);
  });

  it("keeps them on disk and reads them back in ranges", async () => {
    const dir = await mkdtemp(join(tmpdir(), "videos-"));

    try {
      const disk = new DiskVideoVault(dir);
      const body = mp4(3000);
      await disk.put("acme-rc", { ...meta(VID1, 0, body.length) }, body);

      assert.equal((await disk.list("acme-rc")).length, 1);
      assert.ok((await disk.read("acme-rc", VID1, 10, 19))?.equals(body.subarray(10, 20)));

      await disk.removeClub("acme-rc");
      assert.equal((await disk.list("acme-rc")).length, 0);
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });
});
