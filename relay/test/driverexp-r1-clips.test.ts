import assert from "node:assert/strict";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import {
  CLIP_KEEP_MS,
  CLIP_MAX_BYTES,
  CLIP_MAX_CLUB_BYTES,
  CLIP_MAX_PER_DRIVER,
  clipsToDrop,
  DiskClipVault,
  looksLikeMp3,
  type ClipMeta,
} from "../src/clips.js";
import { ACME_TOKEN, BROOK_TOKEN, call } from "./helpers.js";
import { API, BILL, SAM, setSwitch, startDriver, type DriverHarness } from "./driverexp-r1-helpers.js";

/**
 * DRIVERS' OWN RACE CLIPS (22 Oct 2026, driver experience, R1): the laptop
 * sends the announcer's call of a race as an mp3, one per driver; the
 * driver plays and keeps it from their own night. See clips.ts.
 */

const RACE = "0123456789abcdef0123456789abcdef";

function clipId(n: number): string {
  return n.toString(16).padStart(32, "0");
}

/** An ID3-tagged "mp3" - enough for the sniff. */
function mp3(size = 3000): Buffer {
  const bytes = Buffer.alloc(size, 9);
  bytes.write("ID3", 0, "latin1");
  return bytes;
}

let h: DriverHarness;

beforeEach(async () => {
  h = await startDriver();
});

afterEach(async () => {
  await h.close();
});

async function upload(
  id: string,
  options: { token?: string | null; body?: Buffer; driver?: string; race?: string; label?: string } = {},
): Promise<{ status: number; json: Record<string, unknown> }> {
  const query = new URLSearchParams({ driver: options.driver ?? SAM, label: options.label ?? "Q1 Heat 2 (Stock)" });

  if (options.race !== undefined) {
    query.set("race", options.race);
  }

  const headers: Record<string, string> = { "content-type": "audio/mpeg" };

  if (options.token !== null) {
    headers["authorization"] = `Bearer ${options.token ?? ACME_TOKEN}`;
  }

  const response = await fetch(`${h.base}${API}/clip/${id}?${query.toString()}`, {
    method: "POST",
    headers,
    body: new Uint8Array(options.body ?? mp3()),
  });

  const text = await response.text();
  return { status: response.status, json: JSON.parse(text) as Record<string, unknown> };
}

const night = (id = SAM) => call(h, "POST", `${API}/night`, { json: { id } });

function meta(n: number, driver: string, at: number, bytes = 1000): ClipMeta {
  return { id: clipId(n), driver, label: `Race ${n}`, at: new Date(at).toISOString(), bytes };
}

describe("what passes for an mp3", () => {
  it("takes an ID3 tag or an MPEG frame sync, and nothing else", () => {
    assert.equal(looksLikeMp3(Buffer.from("ID3\u0004\u0000")), true);
    assert.equal(looksLikeMp3(Buffer.from([0xff, 0xfb, 0x90, 0x00])), true);
    assert.equal(looksLikeMp3(Buffer.from([0xff, 0xe3, 0x18, 0x00])), true);
    assert.equal(looksLikeMp3(Buffer.from([0xff, 0xd8, 0xff, 0xe0])), false, "a JPEG");
    assert.equal(looksLikeMp3(Buffer.from("RIFF....WAVE")), false, "a wav");
    assert.equal(looksLikeMp3(Buffer.from("ID")), false);
  });
});

describe("which clips make way", () => {
  const now = Date.parse("2026-10-22T19:00:00Z");

  it("keeps a driver's newest eight, counting the one arriving", () => {
    const held = Array.from({ length: CLIP_MAX_PER_DRIVER }, (_, i) => meta(i + 1, SAM, now - (i + 1) * 60_000));
    const drop = clipsToDrop(held, now, { driver: SAM, bytes: 1000 });

    assert.deepEqual(drop.map((c) => c.id), [clipId(CLIP_MAX_PER_DRIVER)], "only the oldest of Sam's");
    assert.deepEqual(clipsToDrop(held, now), [], "eight is fine with nothing arriving");
    assert.deepEqual(clipsToDrop(held, now, { driver: BILL, bytes: 1000 }), [], "Bill's clip costs Sam nothing");
  });

  it("drops anything past its week", () => {
    const held = [meta(1, SAM, now - CLIP_KEEP_MS - 1), meta(2, SAM, now - 1000)];
    assert.deepEqual(clipsToDrop(held, now).map((c) => c.id), [clipId(1)]);
  });

  it("once the club is full, everything older goes - never the newest", () => {
    const big = CLIP_MAX_CLUB_BYTES / 2;
    const held = [meta(1, SAM, now - 3000, big), meta(2, BILL, now - 2000, big), meta(3, "11111111111111111111", now - 1000, 10)];
    const drop = clipsToDrop(held, now, { driver: "22222222222222222222", bytes: 100 });

    // Newest first: 3 fits, 2 fits, 1 does not - and nothing older than 1 survives.
    assert.deepEqual(drop.map((c) => c.id), [clipId(1)]);
  });
});

describe("taking a clip", () => {
  beforeEach(async () => {
    await setSwitch(h, "nights/open", true);
  });

  it("takes one and says where it lives", async () => {
    const answer = await upload(clipId(1), { race: RACE });

    assert.equal(answer.status, 200);
    assert.equal(answer.json["clip"], `/c/acme-rc/clip/${clipId(1)}.mp3`);
    const held = await h.clips.list("acme-rc");
    assert.equal(held.length, 1);
    assert.equal(held[0]?.driver, SAM);
    assert.equal(held[0]?.race, RACE);
  });

  it("needs the club's own token", async () => {
    assert.equal((await upload(clipId(1), { token: null })).status, 401);
    assert.equal((await upload(clipId(1), { token: BROOK_TOKEN })).status, 403);
    assert.equal((await h.clips.list("acme-rc")).length, 0);
  });

  it("is refused while the club is not offering nights", async () => {
    await setSwitch(h, "nights/open", false);
    const answer = await upload(clipId(1));

    assert.equal(answer.status, 403);
    assert.equal((await h.clips.list("acme-rc")).length, 0);
  });

  it("refuses something that is not an mp3, and bad ids", async () => {
    assert.equal((await upload(clipId(1), { body: Buffer.from("RIFF0000WAVEfmt ") })).status, 400);
    assert.equal((await upload("abc")).status, 400);
    assert.equal((await upload(clipId(1), { driver: "" })).status, 400, "a clip is always somebody's");
    assert.equal((await upload(clipId(1), { driver: "1234" })).status, 400);
    assert.equal((await upload(clipId(1), { race: "nope" })).status, 400);
    assert.equal((await h.clips.list("acme-rc")).length, 0);
  });

  it("refuses one over four megabytes", async () => {
    const answer = await upload(clipId(1), { body: mp3(CLIP_MAX_BYTES + 1) });
    assert.equal(answer.status, 413);
  });

  it("keeps only a driver's newest eight", async () => {
    for (let i = 1; i <= CLIP_MAX_PER_DRIVER + 1; i++) {
      assert.equal((await upload(clipId(i))).status, 200);
      h.advance(1000);
    }

    const held = await h.clips.list("acme-rc");
    assert.equal(held.length, CLIP_MAX_PER_DRIVER);
    assert.equal(held.some((c) => c.id === clipId(1)), false, "the oldest made way");
  });
});

describe("hearing and keeping one", () => {
  beforeEach(async () => {
    await setSwitch(h, "nights/open", true);
  });

  it("serves it whole, in ranges, and as a download", async () => {
    const body = mp3(5000);
    assert.equal((await upload(clipId(1), { body })).status, 200);

    const whole = await fetch(`${h.base}/c/acme-rc/clip/${clipId(1)}.mp3`);
    assert.equal(whole.status, 200);
    assert.equal(whole.headers.get("content-type"), "audio/mpeg");
    assert.equal(whole.headers.get("accept-ranges"), "bytes");
    assert.equal(Buffer.from(await whole.arrayBuffer()).equals(body), true);

    const part = await fetch(`${h.base}/c/acme-rc/clip/${clipId(1)}.mp3`, { headers: { range: "bytes=100-199" } });
    assert.equal(part.status, 206);
    assert.equal(part.headers.get("content-range"), "bytes 100-199/5000");
    assert.equal(Buffer.from(await part.arrayBuffer()).equals(body.subarray(100, 200)), true);

    const bad = await fetch(`${h.base}/c/acme-rc/clip/${clipId(1)}.mp3`, { headers: { range: "bytes=9000-" } });
    await bad.arrayBuffer();
    assert.equal(bad.status, 416);

    const keep = await fetch(`${h.base}/c/acme-rc/clip/${clipId(1)}.mp3?download=1`);
    await keep.arrayBuffer();
    assert.match(keep.headers.get("content-disposition") ?? "", /^attachment; filename="Q1 Heat 2 \(Stock\)\.mp3"$/);
  });

  it("answers 404 for one it does not hold", async () => {
    const answer = await call(h, "GET", `/c/acme-rc/clip/${clipId(7)}.mp3`);
    assert.equal(answer.status, 404);
    assert.equal((await call(h, "GET", `/c/acme-rc/clip/nope.mp3`)).status, 404);
  });

  it("is listed on that driver's night only", async () => {
    await h.store.putItem("acme-rc", "nights", SAM, { seq: 1, publishedAt: "2026-10-22T19:00:00Z", body: { name: "Sam" } });
    await h.store.putItem("acme-rc", "nights", BILL, { seq: 1, publishedAt: "2026-10-22T19:00:00Z", body: { name: "Bill" } });
    assert.equal((await upload(clipId(1), { race: RACE })).status, 200);

    const sam = await night(SAM);
    assert.equal(sam.status, 200);
    assert.deepEqual(sam.body.clips, [
      {
        url: `/c/acme-rc/clip/${clipId(1)}.mp3`,
        download: `/c/acme-rc/clip/${clipId(1)}.mp3?download=1`,
        label: "Q1 Heat 2 (Stock)",
        at: "2026-10-22T19:00:00.000Z",
        race: RACE,
      },
    ]);

    const bill = await night(BILL);
    assert.equal(bill.status, 200);
    assert.equal(bill.body.clips, undefined, "left out when there are none");
  });
});

describe("taking clips down", () => {
  beforeEach(async () => {
    await setSwitch(h, "nights/open", true);
    assert.equal((await upload(clipId(1))).status, 200);
  });

  it("closing nights takes every clip", async () => {
    await setSwitch(h, "nights/open", false);
    assert.equal((await h.clips.list("acme-rc")).length, 0);
    assert.equal((await call(h, "GET", `/c/acme-rc/clip/${clipId(1)}.mp3`)).status, 404);
  });

  it("the club's delete takes every clip", async () => {
    const answer = await call(h, "DELETE", API, { token: ACME_TOKEN });
    assert.equal(answer.status, 200);
    assert.equal((await h.clips.list("acme-rc")).length, 0);
  });

  it("the sweep drops a clip past its week and nothing newer", async () => {
    h.advance(CLIP_KEEP_MS - 60_000);
    assert.equal((await upload(clipId(2))).status, 200);
    h.advance(120_000);

    assert.equal(await h.relay.sweepClips(), 1);
    assert.deepEqual((await h.clips.list("acme-rc")).map((c) => c.id), [clipId(2)]);
  });

  it("an upload under way when nights close keeps nothing", async () => {
    // Hold the clips lock so the upload waits inside it, close nights, then let it go.
    let release: () => void = () => {};
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const relay = h.relay as unknown as { withLock: (key: string, work: () => Promise<void>) => Promise<void> };
    const locked = relay.withLock("acme-rc|clips", () => held);

    const pending = upload(clipId(3));
    await new Promise((resolve) => setTimeout(resolve, 50));
    const closing = setSwitch(h, "nights/open", false);
    await new Promise((resolve) => setTimeout(resolve, 50));
    release();
    await locked;
    await closing;

    assert.equal((await pending).status, 410);
    assert.equal((await h.clips.list("acme-rc")).length, 0);
  });
});

describe("the clips on disk", () => {
  it("round-trips a clip and reads ranges", async () => {
    const dir = await mkdtemp(join(tmpdir(), "hc-clips-"));

    try {
      const vault = new DiskClipVault(dir);
      const bytes = mp3(2000);
      const m: ClipMeta = { id: clipId(1), driver: SAM, label: "A-Main", race: RACE, at: "2026-10-22T19:00:00.000Z", bytes: bytes.length };

      await vault.put("acme-rc", m, bytes);
      assert.deepEqual(await vault.list("acme-rc"), [m]);
      assert.deepEqual(await vault.listClubs(), ["acme-rc"]);
      assert.equal((await vault.read("acme-rc", m.id, 10, 19))?.equals(bytes.subarray(10, 20)), true);

      await vault.removeClub("acme-rc");
      assert.deepEqual(await vault.list("acme-rc"), []);
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });
});
