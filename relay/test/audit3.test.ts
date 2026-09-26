import assert from "node:assert/strict";
import { chmod, mkdir, mkdtemp, rm, stat, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, describe, it } from "node:test";
import { MANAGE_INBOX_MAX_BYTES, managePinHash, pinHash } from "../src/server.js";
import { FileStore } from "../src/store.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

// Regressions for the third audit: what the relay leaves on disk for other
// accounts to read, and a manager queue the laptop could not read back.

const open: Harness[] = [];
const dirs: string[] = [];

afterEach(async () => {
  while (open.length > 0) {
    await open.pop()!.close();
  }

  while (dirs.length > 0) {
    await rm(dirs.pop()!, { recursive: true, force: true });
  }
});

async function tempDir(): Promise<string> {
  const dir = await mkdtemp(join(tmpdir(), "relay-audit3-"));
  dirs.push(dir);
  return dir;
}

const posix = process.platform !== "win32";

describe("the relay's files are its own", { skip: !posix && "file modes are a POSIX thing" }, () => {
  it("writes the pass key, the check-in roster and every folder owner-only, whatever the umask", async () => {
    const dir = await tempDir();
    const h = await start(new FileStore(dir));
    open.push(h);

    const published = await call(h, "POST", "/api/v1/c/acme-rc/publish/checkin?seq=1", {
      token: ACME_TOKEN,
      json: { enabled: true, pinSha256: pinHash("acme-rc", "4321"), event: "Club night", drivers: [] },
    });
    assert.equal(published.status, 200);

    // The right PIN is answered with a pass, which is what makes the key.
    const signedIn = await call(h, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "4321" } });
    assert.equal(signedIn.status, 200);

    for (const file of ["acme-rc/snapshot/passkey.json", "acme-rc/snapshot/checkin.json"]) {
      const mode = (await stat(join(dir, file))).mode & 0o777;
      assert.equal(mode & 0o077, 0, `${file} is ${mode.toString(8)}`);
    }

    for (const folder of ["acme-rc", "acme-rc/snapshot"]) {
      const mode = (await stat(join(dir, folder))).mode & 0o777;
      assert.equal(mode & 0o077, 0, `${folder} is ${mode.toString(8)}`);
    }
  });

  it("takes other accounts off what an older build left readable, at start-up", async () => {
    const dir = await tempDir();
    const folder = join(dir, "acme-rc", "snapshot");
    const key = join(folder, "passkey.json");

    await mkdir(folder, { recursive: true });
    await writeFile(key, JSON.stringify({ seq: 0, publishedAt: "2026-09-20T12:00:00.000Z", body: { key: "ab".repeat(32) } }));
    await chmod(key, 0o644);
    await chmod(folder, 0o755);

    await new FileStore(dir).sweepTemp();

    assert.equal((await stat(key)).mode & 0o777, 0o600);
    assert.equal((await stat(folder)).mode & 0o777, 0o700);
  });
});

describe("the manager's queue always fits in what the laptop reads", () => {
  it("never answers manage/inbox with more than the laptop's one-mebibyte limit, however big the requests", async () => {
    const h = await start();
    open.push(h);

    const published = await call(h, "POST", "/api/v1/c/acme-rc/publish/manage?seq=1", {
      token: ACME_TOKEN,
      json: { enabled: true, pinSha256: managePinHash("acme-rc", "654321") },
    });
    assert.equal(published.status, 200);

    const signIn = await call(h, "POST", "/api/v1/c/acme-rc/manage/signin", { json: { pin: "654321" } });
    assert.equal(signIn.status, 200);
    const pass = signIn.body.pass as string;

    // As big as one request may be (68 KiB), until the queue is full.
    let refused = false;
    let accepted = 0;

    for (let n = 0; n < 40 && !refused; n++) {
      const sent = await call(h, "POST", "/api/v1/c/acme-rc/manage/request", {
        json: { pass, reqId: `huge-${String(n).padStart(4, "0")}-aaaa`, op: "result.adjust", args: { pad: "x".repeat(67 * 1024 + 700) } },
      });

      if (sent.status === 200) {
        accepted++;
      } else {
        assert.equal(sent.status, 503, JSON.stringify(sent.body));
        refused = true;
      }
    }

    assert.ok(refused, "the queue filled");
    assert.ok(accepted >= 12, `${accepted} accepted`);

    const inbox = await call(h, "GET", "/api/v1/c/acme-rc/manage/inbox", { token: ACME_TOKEN });
    assert.equal(inbox.status, 200);
    assert.equal(inbox.body.items.length, accepted);
    assert.ok(
      Buffer.byteLength(inbox.text) <= MANAGE_INBOX_MAX_BYTES,
      `manage/inbox answered ${Buffer.byteLength(inbox.text)} bytes; the laptop reads at most ${MANAGE_INBOX_MAX_BYTES}`,
    );
  });
});
