import assert from "node:assert/strict";
import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { readBuildInfo, sourceHash } from "../src/buildinfo.js";

// v1.3 of the relay admin app: which source a build was made from, so the
// app can say when the relay on Fly is behind the relay folder.

describe("build info", () => {
  let dir: string;

  beforeEach(async () => {
    dir = await mkdtemp(join(tmpdir(), "relay-buildinfo-"));
  });

  afterEach(async () => {
    await rm(dir, { recursive: true, force: true });
  });

  it("hashes .ts files by path and text, the same whatever the line endings", async () => {
    await mkdir(join(dir, "a", "sub"), { recursive: true });
    await mkdir(join(dir, "b", "sub"), { recursive: true });
    await writeFile(join(dir, "a", "index.ts"), "one\ntwo\n");
    await writeFile(join(dir, "a", "sub", "x.ts"), "x\n");
    await writeFile(join(dir, "a", "notes.md"), "not source");
    await writeFile(join(dir, "b", "index.ts"), "one\r\ntwo\r\n");
    await writeFile(join(dir, "b", "sub", "x.ts"), "x\r\n");

    const a = await sourceHash(join(dir, "a"));
    assert.equal(a, await sourceHash(join(dir, "b")));
    assert.match(a, /^[0-9a-f]{64}$/);

    // The admin app's own test (BuildInfoTests) pins this same value for these two files.
    assert.equal(a, "ed2bfc8bf35a5b68582e110134ec23eb93bc5c7f1b1451826fb1c69e4cb572b8");

    await writeFile(join(dir, "b", "sub", "x.ts"), "y\n");
    assert.notEqual(a, await sourceHash(join(dir, "b")));
  });

  it("reads build-info.json, and nothing from a missing or broken one", async () => {
    const path = join(dir, "build-info.json");
    assert.equal(await readBuildInfo(path), null);

    await writeFile(path, "{ half");
    assert.equal(await readBuildInfo(path), null);

    await writeFile(path, JSON.stringify({ version: "0.1.0", sourceHash: "f".repeat(64), builtAt: "2026-10-25T13:00:00.000Z" }));
    assert.deepEqual(await readBuildInfo(path), { version: "0.1.0", sourceHash: "f".repeat(64), builtAt: "2026-10-25T13:00:00.000Z" });

    await writeFile(path, JSON.stringify({ version: "0.1.0", sourceHash: "not a hash", builtAt: "x" }));
    assert.equal(await readBuildInfo(path), null);
  });
});
