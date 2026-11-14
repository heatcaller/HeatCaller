import assert from "node:assert/strict";
import { lstat, mkdir, mkdtemp, readFile, rm, symlink, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { removeLeftoverVideos } from "../src/oldvideos.js";
import { homePage, nightPage, resultsPage } from "../src/pages.js";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * THE PODIUM VIDEOS ARE GONE FROM THE RELAY (28 Oct 2026).
 *
 * Asked for by Asher: "Remove the podium videos for downloading from the
 * relay and just have locally." The laptop still makes them; they stay on
 * it. Here: every route the videos had answers exactly as an address the
 * relay never had does, nothing on the club's pages links to them, and a
 * relay that kept some under an older build clears them at start-up.
 */

const API = "/api/v1/c/acme-rc";
const RACE = "0123456789abcdef0123456789abcdef";
const VID = "11111111111111111111111111111111";
const BILL = "a1b2c3d4e5f60718293a";

function mp4(size = 4000): Buffer {
  const bytes = Buffer.alloc(size, 7);
  bytes.writeUInt32BE(24, 0);
  bytes.write("ftypisom", 4, "latin1");
  return bytes;
}

describe("the old video routes", () => {
  let harness: Harness;

  beforeEach(async () => {
    harness = await start(new MemoryStore());
  });

  afterEach(async () => {
    await harness.close();
  });

  it("answer a phone exactly as a page the relay never had", async () => {
    const never = await call(harness, "GET", "/c/acme-rc/no-such-page");

    for (const path of ["/c/acme-rc/videos", `/c/acme-rc/videos/${RACE}`, `/c/acme-rc/video/${VID}.mp4`, `/c/acme-rc/video/${VID}.mp4?download=1`]) {
      const answer = await call(harness, "GET", path);
      assert.equal(answer.status, never.status, path);
      assert.equal(answer.status, 404, path);
      assert.doesNotMatch(answer.headers["content-type"] ?? "", /video/, path);
    }
  });

  it("answer the laptop's upload and take-down as unknown endpoints, and the relay carries on", async () => {
    const response = await fetch(`${harness.base}${API}/video/${VID}?race=${RACE}&position=1&name=Bill&label=A-Main`, {
      method: "POST",
      headers: { authorization: `Bearer ${ACME_TOKEN}`, "content-type": "video/mp4" },
      body: new Uint8Array(mp4()),
    });
    const upload = (await response.json()) as { ok: boolean; error: string };

    const never = await call(harness, "POST", `${API}/no-such-thing/${VID}`, { token: ACME_TOKEN, raw: "x" });
    assert.equal(response.status, never.status);
    assert.equal(response.status, 404);
    assert.equal(upload.ok, false);
    assert.equal(upload.error, never.body.error);

    const forget = await call(harness, "POST", `${API}/driver/forget-media`, { token: ACME_TOKEN, json: { ids: [BILL] } });
    assert.equal(forget.status, 404);
    assert.equal(forget.body.error, never.body.error);

    // Still answering everything else.
    assert.equal((await call(harness, "GET", "/c/acme-rc")).status, 200);
  });

  it("leave a driver's night with no videos in it", async () => {
    assert.equal((await call(harness, "POST", `${API}/nights/open`, { token: ACME_TOKEN, json: { open: true } })).status, 200);
    assert.equal(
      (await call(harness, "POST", `${API}/publish/nights/${BILL}?seq=1`, { token: ACME_TOKEN, json: { name: "Bill", said: ["A win."] } })).status,
      200,
    );

    const night = await call(harness, "POST", `${API}/night`, { json: { id: BILL } });
    assert.equal(night.status, 200);
    assert.equal("videos" in night.body, false);
  });
});

describe("the club's pages", () => {
  it("have no Videos tab, no podium videos tile and no player for one", () => {
    for (const html of [homePage("acme-rc", "Acme RC"), resultsPage("acme-rc", "Acme RC"), nightPage("acme-rc", "Acme RC")]) {
      assert.doesNotMatch(html, /href="\/c\/acme-rc\/videos/);
      assert.doesNotMatch(html, />Videos</);
      // Race video (14 Nov 2026) has routes of its own under /video/ -
      // night, race, live, check - and none of the podium videos' old ones.
      assert.doesNotMatch(html, /\/video\/(?!night'|race\/|live'|check')/);
      assert.doesNotMatch(html, /Your podium videos/);
    }

    assert.doesNotMatch(homePage("acme-rc", "Acme RC"), /Watch and download the top three/);
  });
});

describe("the leftovers on disk", () => {
  let dir: string;

  beforeEach(async () => {
    dir = await mkdtemp(join(tmpdir(), "relay-oldvideos-"));
  });

  afterEach(async () => {
    await rm(dir, { recursive: true, force: true });
  });

  it("removes dataDir/.videos and everything in it, and nothing else", async () => {
    await mkdir(join(dir, ".videos", "acme-rc"), { recursive: true });
    await mkdir(join(dir, ".videos", "brookside"), { recursive: true });
    await writeFile(join(dir, ".videos", "acme-rc", `${VID}.mp4`), mp4(4000));
    await writeFile(join(dir, ".videos", "acme-rc", `${VID}.json`), "{}");
    await writeFile(join(dir, ".videos", "brookside", `${RACE}.mp4`), mp4(1000));

    // Things that must stay: the clips, and a club's own folder.
    await mkdir(join(dir, ".clips", "acme-rc"), { recursive: true });
    await writeFile(join(dir, ".clips", "acme-rc", "keep.mp3"), "ID3");
    await mkdir(join(dir, "acme-rc"), { recursive: true });
    await writeFile(join(dir, "acme-rc", "results.json"), "{}");

    const said: string[] = [];
    const gone = await removeLeftoverVideos(dir, (line) => said.push(line));

    assert.deepEqual(gone, { files: 3, bytes: 4000 + 2 + 1000 });
    await assert.rejects(lstat(join(dir, ".videos")));
    assert.equal(await readFile(join(dir, ".clips", "acme-rc", "keep.mp3"), "utf8"), "ID3");
    assert.equal(await readFile(join(dir, "acme-rc", "results.json"), "utf8"), "{}");
    assert.equal(said.length, 1);
    assert.match(said[0] ?? "", /removed the old podium videos: 3 file\(s\)/);
  });

  it("does nothing, says nothing and never throws when there is no folder", async () => {
    const said: string[] = [];
    assert.deepEqual(await removeLeftoverVideos(dir, (line) => said.push(line)), { files: 0, bytes: 0 });
    assert.deepEqual(await removeLeftoverVideos(join(dir, "not-there"), (line) => said.push(line)), { files: 0, bytes: 0 });
    assert.deepEqual(said, []);
  });

  it("never follows a link where the folder was", async (t) => {
    const elsewhere = join(dir, "elsewhere");
    await mkdir(elsewhere);
    await writeFile(join(elsewhere, "precious.txt"), "keep me");
    const data = join(dir, "data");
    await mkdir(data);

    // Windows only lets Developer Mode or an admin make a symlink, so there
    // the link is a junction, which any user can make and which lstat sees
    // as a link all the same. A PC that refuses even that skips the test
    // rather than failing a relay that is fine.
    try {
      await symlink(elsewhere, join(data, ".videos"), process.platform === "win32" ? "junction" : undefined);
    } catch (err) {
      const code = (err as NodeJS.ErrnoException).code;
      if (code === "EPERM" || code === "EACCES" || code === "ENOSYS") {
        t.skip(`this PC can't make a link (${code}), so there is nothing to test here`);
        return;
      }
      throw err;
    }

    const said: string[] = [];
    assert.deepEqual(await removeLeftoverVideos(data, (line) => said.push(line)), { files: 0, bytes: 0 });
    assert.equal(await readFile(join(elsewhere, "precious.txt"), "utf8"), "keep me");
    assert.match(said[0] ?? "", /not a folder/);
  });
});
