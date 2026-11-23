import assert from "node:assert/strict";
import { existsSync } from "node:fs";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { FileStore } from "../src/store.js";
import {
  activeOf,
  garageWithoutTakenDown,
  NO_TAKEDOWNS,
  normaliseRef,
  TakedownBook,
  takedownCounts,
  withoutTakenDown,
  youTubeIdOf,
  type TakedownRecord,
} from "../src/takedown.js";
import { argValue } from "../src/takedownadmin.js";
import { hashToken } from "../src/tenancy.js";
import { voiceAdmin, type AdminCommand } from "../src/voiceadmin.js";
import { ACME_TOKEN, BROOK_TOKEN } from "./helpers.js";

/**
 * Copyright takedowns of one item (23 Nov 2026, asked for by Asher): each
 * kind's filter, the running relay's reading of the file, the policy's
 * count, and the four admin commands. takedown-e2e.test.ts does it all on
 * a real relay.
 */

const record = (over: Partial<TakedownRecord>): TakedownRecord => ({
  id: "td-000000000001",
  kind: "logo",
  ref: "",
  notice: "N1",
  state: "active",
  takenDownAt: "2026-11-20T00:00:00.000Z",
  restoredAt: null,
  restoreReason: null,
  ...over,
});

describe("takedowns: the keys", () => {
  it("puts each kind's key in its one stored form", () => {
    assert.deepEqual(normaliseRef("logo", undefined), { ok: true, ref: "" });
    assert.equal(normaliseRef("logo", "something").ok, false);
    assert.deepEqual(normaliseRef("video", "https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=10"), { ok: true, ref: "dQw4w9WgXcQ" });
    assert.deepEqual(normaliseRef("video", "dQw4w9WgXcQ"), { ok: true, ref: "dQw4w9WgXcQ" });
    assert.equal(normaliseRef("video", "nope").ok, false);
    assert.deepEqual(normaliseRef("clip", "0123456789ABCDEF0123456789abcdef.mp3"), { ok: true, ref: "0123456789abcdef0123456789abcdef" });
    assert.equal(normaliseRef("clip", "0123").ok, false);
    assert.deepEqual(normaliseRef("crewsample", "A1B2C3D4E5F6"), { ok: true, ref: "a1b2c3d4e5f6" });
    assert.deepEqual(normaliseRef("garagephoto", "a1b2c3d4e5f6"), { ok: true, ref: "a1b2c3d4e5f6.jpg" });
    assert.deepEqual(normaliseRef("track", "  Tight   Layout "), { ok: true, ref: "Tight Layout" });
    assert.equal(normaliseRef("sponsor", "").ok, false);
  });

  it("reads every shape of YouTube address, and nothing else", () => {
    assert.equal(youTubeIdOf("https://youtu.be/dQw4w9WgXcQ?t=3"), "dQw4w9WgXcQ");
    assert.equal(youTubeIdOf("youtube.com/live/dQw4w9WgXcQ"), "dQw4w9WgXcQ");
    assert.equal(youTubeIdOf("https://m.youtube.com/shorts/dQw4w9WgXcQ"), "dQw4w9WgXcQ");
    assert.equal(youTubeIdOf("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ"), "dQw4w9WgXcQ");
    assert.equal(youTubeIdOf("https://relay.example/c/acme-rc/live"), undefined);
  });

  it("decodes a b64: value, so a name with quotes never meets the shell", () => {
    assert.equal(argValue(`b64:${Buffer.from(`Joe's "Big" Track`).toString("base64url")}`), `Joe's "Big" Track`);
    assert.equal(argValue("plain"), "plain");
    assert.equal(argValue("b64:not base64!"), "");
  });
});

describe("takedowns: what each kind leaves out", () => {
  const all = activeOf([
    record({ kind: "logo" }),
    record({ id: "td-000000000002", kind: "sponsor", ref: "sp1" }),
    record({ id: "td-000000000003", kind: "track", ref: "Tight" }),
    record({ id: "td-000000000004", kind: "badge", ref: "b1" }),
    record({ id: "td-000000000005", kind: "garagephoto", ref: "aaaaaaaaaaaa.jpg" }),
  ]);

  it("logo: the site document loses its logo and plate, and nothing else", () => {
    assert.deepEqual(withoutTakenDown("site", { logo: "data:x", logoPlate: "#000000", tagline: "Hi" }, all), { tagline: "Hi" });
  });

  it("sponsor: the whole sponsor goes, the others stay", () => {
    const body = { sponsors: [{ id: "sp1", name: "A", picture: "data:x" }, { id: "sp2", name: "B", picture: null }] };
    assert.deepEqual(withoutTakenDown("sponsors", body, all), { sponsors: [{ id: "sp2", name: "B", picture: null }] });
  });

  it("track: the picture goes, the layout and its records stay", () => {
    const body = { tracks: [{ name: "Tight", image: "data:x", record: 9.1 }, { name: "Long", image: "data:y" }] };
    assert.deepEqual(withoutTakenDown("tracks", body, all), { tracks: [{ name: "Tight", record: 9.1 }, { name: "Long", image: "data:y" }] });
  });

  it("badge: the artwork goes, the achievement stays", () => {
    assert.deepEqual(withoutTakenDown("badges", { badges: [{ id: "b1", name: "Win", art: "data:x" }] }, all), { badges: [{ id: "b1", name: "Win" }] });
  });

  it("garage photo: out of the car, and not its cover", () => {
    const body = { cars: [{ name: "Buggy", cover: "aaaaaaaaaaaa.jpg", photos: [{ id: "aaaaaaaaaaaa.jpg", thumb: "data:x" }, { id: "bbbbbbbbbbbb.jpg", thumb: "data:y" }] }] };
    assert.deepEqual(garageWithoutTakenDown(body, all), { cars: [{ name: "Buggy", photos: [{ id: "bbbbbbbbbbbb.jpg", thumb: "data:y" }] }] });
  });

  it("passes every other channel, and everything when nothing is down, through untouched", () => {
    const state = { flag: "green", logo: "data:x" };
    assert.equal(withoutTakenDown("state", state, all), state);
    assert.equal(withoutTakenDown("site", state, NO_TAKEDOWNS), state);
  });

  it("forgets a restored one", () => {
    assert.equal(activeOf([record({ state: "restored" })]).any, false);
  });
});

describe("takedowns: the repeat-infringer count", () => {
  const now = new Date("2026-11-23T00:00:00.000Z");

  it("counts notices, not items, and not one countered or over a year old", () => {
    const counts = takedownCounts([
      record({ id: "td-000000000001", notice: "N1" }),
      record({ id: "td-000000000002", kind: "sponsor", ref: "a", notice: "n1 " }),
      record({ id: "td-000000000003", kind: "sponsor", ref: "b", notice: "N2" }),
      record({ id: "td-000000000004", kind: "sponsor", ref: "c", notice: "N3", state: "restored" }),
      record({ id: "td-000000000005", kind: "sponsor", ref: "d", notice: "N4", takenDownAt: "2025-11-01T00:00:00.000Z" }),
    ], now);

    assert.deepEqual(counts, { active: 4, last12Months: 2, repeat: false });
  });

  it("flags three, and only flags", () => {
    const counts = takedownCounts(["N1", "N2", "N3"].map((notice, i) => record({ id: `td-00000000000${i}`, kind: "sponsor", ref: String(i), notice })), now);
    assert.equal(counts.repeat, true);
  });
});

describe("takedowns: the running relay's copy", () => {
  let dataDir: string;

  beforeEach(async () => {
    dataDir = await mkdtemp(join(tmpdir(), "hc-takedown-book-"));
  });

  afterEach(async () => {
    await rm(dataDir, { recursive: true, force: true });
  });

  const write = async (records: TakedownRecord[] | string) => {
    await mkdir(join(dataDir, ".takedowns"), { recursive: true });
    await writeFile(join(dataDir, ".takedowns", "acme-rc.json"), typeof records === "string" ? records : JSON.stringify({ version: 1, club: "acme-rc", takedowns: records }));
  };

  it("looks at the file at most every two seconds, and says when it changed", async () => {
    let clock = 0;
    const book = new TakedownBook(dataDir, { clock: () => clock });

    assert.deepEqual(await book.of("acme-rc"), { active: NO_TAKEDOWNS, changed: false }, "no file: nothing down, nothing to drop");

    await write([record({})]);
    clock += 1_000;
    assert.equal((await book.of("acme-rc")).active.logo, false, "not looked at again inside two seconds");

    clock += 1_500;
    const seen = await book.of("acme-rc");
    assert.equal(seen.active.logo, true);
    assert.equal(seen.changed, true);

    clock += 2_500;
    assert.equal((await book.of("acme-rc")).changed, false, "the same file is no change");
  });

  it("keeps the last good set when the file will not read, rather than putting everything back up", async () => {
    let clock = 0;
    const lines: string[] = [];
    const book = new TakedownBook(dataDir, { clock: () => clock, log: (line) => lines.push(line) });

    await write([record({})]);
    assert.equal((await book.of("acme-rc")).active.logo, true);

    await write("{ half a fi");
    clock += 3_000;
    assert.equal((await book.of("acme-rc")).active.logo, true);
    assert.match(lines.join("\n"), /will not read/);
  });

  it("drops held files but never the records when the club deletes everything", async () => {
    await write([record({})]);
    await mkdir(join(dataDir, ".takedowns", "held", "acme-rc", "clip"), { recursive: true });
    await writeFile(join(dataDir, ".takedowns", "held", "acme-rc", "clip", "x.mp3"), "x");

    await new TakedownBook(dataDir).wipeHeld("acme-rc");

    assert.equal(existsSync(join(dataDir, ".takedowns", "held", "acme-rc")), false);
    assert.equal(existsSync(join(dataDir, ".takedowns", "acme-rc.json")), true);
  });
});

describe("takedowns: the admin commands", () => {
  let dataDir: string;
  let env: NodeJS.ProcessEnv;
  const now = new Date("2026-11-23T12:00:00.000Z");
  const PNG = "data:image/png;base64,iVBORw0KGgo=";

  beforeEach(async () => {
    dataDir = await mkdtemp(join(tmpdir(), "hc-takedown-admin-"));
    env = {
      RELAY_CONFIG_JSON: JSON.stringify({
        dataDir,
        clubs: [
          { club: "acme-rc", name: "Acme RC", tokenSha256: hashToken(ACME_TOKEN) },
          { club: "brookside", name: "Brookside", tokenSha256: hashToken(BROOK_TOKEN) },
        ],
      }),
    };
  });

  afterEach(async () => {
    await rm(dataDir, { recursive: true, force: true });
  });

  const run = async (command: AdminCommand, ...args: string[]) => {
    let out = "";
    let err = "";
    const code = await voiceAdmin(command, args, env, now, (t) => { out += t; }, (t) => { err += t; });
    return { code, out, err, json: out.trim().startsWith("{") ? JSON.parse(out) : undefined };
  };

  it("takedown: answers the frozen shape, once, and the same target again is the same takedown", async () => {
    const first = await run("takedown", "acme-rc", "sponsor", "sp1", "--notice", "DMCA 2026-001", "--json");

    assert.equal(first.code, 0, first.err);
    assert.deepEqual(Object.keys(first.json).sort(), ["already", "club", "held", "id", "kind", "notice", "ok", "ref", "state", "takenDownAt"]);
    assert.match(first.json.id, /^td-[0-9a-f]{12}$/);
    assert.deepEqual({ ...first.json, id: "" }, { ok: true, id: "", club: "acme-rc", kind: "sponsor", ref: "sp1", notice: "DMCA 2026-001", state: "active", takenDownAt: now.toISOString(), already: false, held: null });

    const again = await run("takedown", "acme-rc", "sponsor", "sp1", "--notice", "DMCA 2026-002", "--json");
    assert.equal(again.json.id, first.json.id);
    assert.equal(again.json.already, true);
    assert.equal(again.json.notice, "DMCA 2026-001", "the first notice stays the one on record");

    const audit = (await readFile(join(dataDir, ".admin-audit", "2026-11.jsonl"), "utf8")).trim().split("\n").map((l) => JSON.parse(l));
    assert.equal(audit.length, 1, "one audit entry: the second was nothing new");
    assert.equal(audit[0].action, "takedown");
    assert.equal(audit[0].club, "acme-rc");
  });

  it("takedown: refuses no notice, an unknown kind, a bad ref and a club not in the config, writing nothing", async () => {
    assert.equal((await run("takedown", "acme-rc", "sponsor", "sp1", "--json")).json.ok, false);
    assert.equal((await run("takedown", "acme-rc", "poster", "x", "--notice", "N", "--json")).json.ok, false);
    assert.equal((await run("takedown", "acme-rc", "clip", "nope", "--notice", "N", "--json")).json.ok, false);
    assert.equal((await run("takedown", "nobody-rc", "logo", "--notice", "N", "--json")).json.ok, false);
    assert.equal(existsSync(join(dataDir, ".takedowns", "acme-rc.json")), false);
  });

  it("takedown: takes b64: values for a track's name and the notice", async () => {
    const b64 = (s: string) => `b64:${Buffer.from(s).toString("base64url")}`;
    const done = await run("takedown", "acme-rc", "track", b64(`Joe's "Big" Track`), "--notice", b64("DMCA #7"), "--json");
    assert.equal(done.json.ref, `Joe's "Big" Track`);
    assert.equal(done.json.notice, "DMCA #7");
  });

  it("takedown and restore a clip: held, not deleted, and put back", async () => {
    const id = "0123456789abcdef0123456789abcdef";
    await mkdir(join(dataDir, ".clips", "acme-rc"), { recursive: true });
    await writeFile(join(dataDir, ".clips", "acme-rc", `${id}.mp3`), "ID3audio");
    await writeFile(join(dataDir, ".clips", "acme-rc", `${id}.json`), "{}");

    const down = await run("takedown", "acme-rc", "clip", `${id}.mp3`, "--notice", "N1", "--json");
    assert.equal(down.json.held, true);
    assert.equal(existsSync(join(dataDir, ".clips", "acme-rc", `${id}.mp3`)), false);
    assert.equal(await readFile(join(dataDir, ".takedowns", "held", "acme-rc", "clip", `${id}.mp3`), "utf8"), "ID3audio");
    assert.equal(existsSync(join(dataDir, ".takedowns", "held", "acme-rc", "clip", `${id}.json`)), true);

    const back = await run("takedown-restore", "acme-rc", down.json.id, "--reason", "counter-notice", "--json");
    assert.equal(back.code, 0, back.err);
    assert.deepEqual(Object.keys(back.json).sort(), ["club", "id", "kind", "ok", "putBack", "reason", "ref", "restoredAt", "state"]);
    assert.equal(back.json.putBack, true);
    assert.equal(await readFile(join(dataDir, ".clips", "acme-rc", `${id}.mp3`), "utf8"), "ID3audio");
  });

  it("takedown a crew sample: held, and a takedown of one that is not there still records it", async () => {
    await mkdir(join(dataDir, ".crewsamples", "acme-rc"), { recursive: true });
    await writeFile(join(dataDir, ".crewsamples", "acme-rc", "a1b2c3d4e5f6.mp3"), "ID3x");

    assert.equal((await run("takedown", "acme-rc", "crewsample", "a1b2c3d4e5f6", "--notice", "N", "--json")).json.held, true);
    assert.equal(existsSync(join(dataDir, ".takedowns", "held", "acme-rc", "crewsample", "a1b2c3d4e5f6.mp3")), true);
    assert.equal((await run("takedown", "acme-rc", "crewsample", "ffffffffffff", "--notice", "N", "--json")).json.held, false);
  });

  it("takedown-restore: needs a reason, keeps the record, and says so when it was restored already", async () => {
    const down = await run("takedown", "acme-rc", "logo", "--notice", "N1", "--json");

    assert.equal((await run("takedown-restore", "acme-rc", down.json.id, "--json")).json.ok, false);
    assert.equal((await run("takedown-restore", "acme-rc", "td-ffffffffffff", "--reason", "x", "--json")).json.ok, false);
    assert.equal((await run("takedown-restore", "acme-rc", down.json.id, "--reason", "countered", "--json")).json.putBack, null);
    assert.equal((await run("takedown-restore", "acme-rc", down.json.id, "--reason", "again", "--json")).json.ok, false);

    const list = await run("takedown-list", "acme-rc", "--json");
    assert.equal(list.json.clubs[0].takedowns[0].state, "restored");
    assert.equal(list.json.clubs[0].takedowns[0].restoreReason, "countered");

    const audit = (await readFile(join(dataDir, ".admin-audit", "2026-11.jsonl"), "utf8")).trim().split("\n").map((l) => JSON.parse(l));
    assert.deepEqual(audit.map((a) => a.action), ["takedown", "takedown-restore"]);
  });

  it("takedown-list: every club with takedowns, its counts and the repeat flag, in the frozen shape", async () => {
    for (const [ref, notice] of [["a", "N1"], ["b", "N2"], ["c", "N3"]]) {
      await run("takedown", "acme-rc", "sponsor", ref as string, "--notice", notice as string, "--json");
    }
    await run("takedown", "brookside", "logo", "--notice", "N9", "--json");

    const list = await run("takedown-list", "--json");
    assert.deepEqual(Object.keys(list.json).sort(), ["clubs", "generatedAt", "ok"]);
    assert.deepEqual(list.json.clubs.map((c: { club: string }) => c.club), ["acme-rc", "brookside"]);
    assert.deepEqual(Object.keys(list.json.clubs[0]).sort(), ["active", "club", "last12Months", "repeat", "takedowns"]);
    assert.deepEqual(Object.keys(list.json.clubs[0].takedowns[0]).sort(), ["id", "kind", "notice", "ref", "restoreReason", "restoredAt", "state", "takenDownAt"]);
    assert.equal(list.json.clubs[0].repeat, true);
    assert.equal(list.json.clubs[1].repeat, false);

    const text = await run("takedown-list");
    assert.match(text.out, /REPEAT/);
    assert.match(text.out, /nothing was closed/);
  });

  it("takedown-find: maps a notice's address to what can be taken down", async () => {
    const store = new FileStore(dataDir);
    const at = { seq: 1, publishedAt: now.toISOString() };
    await store.putSnapshot("acme-rc", "site", { ...at, body: { logo: PNG, alias: "acme" } });
    await store.putSnapshot("acme-rc", "sponsors", { ...at, body: { sponsors: [{ id: "sp1", name: "Hobby Hut", picture: PNG }] } });
    await store.putSnapshot("acme-rc", "tracks", { ...at, body: { tracks: [{ name: "Tight", image: PNG }, { name: "Bare" }] } });
    await store.putSnapshot("acme-rc", "stream", { ...at, body: { on: true, pieces: [{ videoId: "dQw4w9WgXcQ", startedUtc: "2026-11-23T10:00:00Z", endedUtc: null }] } });
    await store.putItem("acme-rc", "results", "abcdefabcdefabcdefabcdefabcdef01", { ...at, body: { startedUtc: "2026-11-23T10:05:00Z", endedUtc: "2026-11-23T10:10:00Z" } });

    const find = async (url: string) => (await run("takedown-find", "acme-rc", url, "--json")).json;
    const kinds = (answer: { candidates: { kind: string; ref: string }[] }) => answer.candidates.map((c) => `${c.kind}:${c.ref}`);

    const logo = await find("https://relay.example/c/acme-rc/logo.png?v=abc");
    assert.deepEqual(Object.keys(logo).sort(), ["candidates", "club", "note", "ok", "url"]);
    assert.deepEqual(Object.keys(logo.candidates[0]).sort(), ["kind", "label", "present", "ref", "takedownId"]);
    assert.deepEqual(kinds(logo), ["logo:"]);
    assert.equal(logo.candidates[0].present, true);

    assert.deepEqual(kinds(await find("https://relay.example/c/acme/live")), ["logo:", "sponsor:sp1"], "the club's short address works too");
    assert.deepEqual(kinds(await find("/c/acme-rc")), ["logo:", "sponsor:sp1", "track:Tight"]);
    assert.deepEqual(kinds(await find("/c/acme-rc/tracks")), ["logo:", "track:Tight"]);
    assert.deepEqual(kinds(await find("/c/acme-rc/results/abcdefabcdefabcdefabcdefabcdef01")), ["logo:", "video:dQw4w9WgXcQ"]);
    assert.deepEqual(kinds(await find("https://youtu.be/dQw4w9WgXcQ")), ["video:dQw4w9WgXcQ"]);
    assert.deepEqual(kinds(await find("/c/acme-rc/clip/0123456789abcdef0123456789abcdef.mp3")), ["clip:0123456789abcdef0123456789abcdef"]);
    assert.deepEqual(kinds(await find("/api/v1/c/acme-rc/crew/sample/a1b2c3d4e5f6.mp3")), ["crewsample:a1b2c3d4e5f6"]);
    assert.deepEqual(kinds(await find("/api/v1/c/acme-rc/snapshot/sponsors")), ["sponsor:sp1"]);
    assert.equal((await find("/c/brookside/live")).ok, false, "another club's address is refused");

    // Already down shows which takedown it is.
    const down = await run("takedown", "acme-rc", "logo", "--notice", "N", "--json");
    assert.equal((await find("/c/acme-rc/logo.png")).candidates[0].takedownId, down.json.id);
  });

  it("admin-snapshot: each club's counts, and the capability, with version still 1", async () => {
    await run("takedown", "acme-rc", "logo", "--notice", "N1", "--json");
    const snapshot = (await run("admin-snapshot", "--json")).json;

    assert.equal(snapshot.version, 1);
    assert.ok(snapshot.capabilities.includes("takedowns"));
    assert.deepEqual(snapshot.clubs.find((c: { club: string }) => c.club === "acme-rc").takedowns, { active: 1, last12Months: 1, repeat: false });
    assert.deepEqual(snapshot.clubs.find((c: { club: string }) => c.club === "brookside").takedowns, { active: 0, last12Months: 0, repeat: false });
  });
});
