import assert from "node:assert/strict";
import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, describe, it } from "node:test";
import { APP_USAGE_DIR, appUsagePath, forgetAppUsage } from "../src/appusage.js";
import { handAppUsageToRelay } from "../src/voiceadmin.js";

/**
 * BUG HUNT 24, #368: app-usage-forget runs as root through fly ssh console
 * and rewrites this month's file in place, which left it root-only, so the
 * running relay (its own user) could record no club's app usage until it
 * restarted. The rewritten files now go back to the relay's user.
 */
describe("app-usage-forget hands the rewritten month file back to the relay's user", () => {
  const folders: string[] = [];

  afterEach(async () => {
    for (const folder of folders.splice(0)) {
      await rm(folder, { recursive: true, force: true });
    }
  });

  async function folder(): Promise<string> {
    const dir = await mkdtemp(join(tmpdir(), "relay-bh24-voiceadmin-"));
    folders.push(dir);
    return dir;
  }

  it("gives the month file a club was forgotten from to the relay's user", async () => {
    const dir = await folder();
    const month = appUsagePath(dir, "2026-11");
    await mkdir(join(dir, APP_USAGE_DIR), { recursive: true });
    await writeFile(month, `${JSON.stringify({ club: "acme-rc" })}\n${JSON.stringify({ club: "brookside" })}\n`);

    assert.equal(await forgetAppUsage(dir, "acme-rc"), 1);

    const calls: Array<[string, number, number]> = [];
    await handAppUsageToRelay(dir, { uid: 1000, gid: 1001 }, async (path, uid, gid) => {
      calls.push([path, uid, gid]);
    });

    assert.deepEqual(calls, [[month, 1000, 1001]]);
  });

  it("does nothing when no club has sent usage yet", async () => {
    const dir = await folder();
    const calls: string[] = [];

    await handAppUsageToRelay(dir, { uid: 1000, gid: 1000 }, async (path) => {
      calls.push(path);
    });

    assert.deepEqual(calls, []);
  });
});
