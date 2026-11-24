import assert from "node:assert/strict";
import { mkdtemp, readdir, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { after, describe, it } from "node:test";
import { APP_USAGE_DIR, AppUsageLog, cleanAppUsage, forgetAppUsage, readAppUsage, type AppUsageDay } from "../src/appusage.js";

// Bug hunt 24 (23 Nov 2026): forgetting one club's usage totals rewrote the
// month file from a copy read once, so a report another club's laptop sent
// in that gap - already answered 204 - was thrown away.

const scratch: string[] = [];

async function folder(): Promise<string> {
  const dir = await mkdtemp(join(tmpdir(), "relay-bughunt24-appusage-"));
  scratch.push(dir);
  return dir;
}

after(async () => {
  for (const dir of scratch) {
    await rm(dir, { recursive: true, force: true });
  }
});

const NOW = new Date("2026-11-19T21:00:00.000Z");

function days(date: string): AppUsageDay[] {
  return cleanAppUsage(
    {
      days: [
        {
          date,
          version: "1.4.2",
          rows: [{ page: "live_timing", tab: "", raceDay: true, deskView: false, opens: 3, activeSeconds: 600, shownSeconds: 5400 }],
          events: { nav_search_used: 2 },
        },
      ],
    },
    NOW,
  );
}

describe("bug hunt 24: forgetting a club keeps a report that arrives meanwhile", () => {
  it("keeps another club's report sent while the month file is rewritten", async () => {
    const dir = await folder();
    const log = new AppUsageLog({ dataDir: dir });

    await log.record("acme-rc", days("2026-11-17"), NOW);
    await log.record("brookside", days("2026-11-17"), NOW);

    const removed = await forgetAppUsage(dir, "acme-rc", async () => {
      await log.record("brookside", days("2026-11-18"), NOW);
    });

    assert.equal(removed, 1);
    const entries = (await readAppUsage(dir, undefined)).entries;
    assert.deepEqual(
      entries.map((e) => `${e.club} ${e.date}`),
      ["brookside 2026-11-17", "brookside 2026-11-18"],
    );
  });

  it("keeps a report that arrives when the forgotten club was the only one in the file", async () => {
    const dir = await folder();
    const log = new AppUsageLog({ dataDir: dir });

    await log.record("acme-rc", days("2026-11-17"), NOW);

    const removed = await forgetAppUsage(dir, "acme-rc", async () => {
      await log.record("brookside", days("2026-11-18"), NOW);
    });

    assert.equal(removed, 1);
    assert.deepEqual(await readdir(join(dir, APP_USAGE_DIR)), ["2026-11.jsonl"]);
    const entries = (await readAppUsage(dir, undefined)).entries;
    assert.deepEqual(
      entries.map((e) => `${e.club} ${e.date}`),
      ["brookside 2026-11-18"],
    );
  });

  it("also forgets a line the forgotten club sends meanwhile", async () => {
    const dir = await folder();
    const log = new AppUsageLog({ dataDir: dir });

    await log.record("acme-rc", days("2026-11-17"), NOW);
    await log.record("brookside", days("2026-11-17"), NOW);

    const removed = await forgetAppUsage(dir, "acme-rc", async () => {
      await log.record("acme-rc", days("2026-11-18"), NOW);
    });

    assert.equal(removed, 2);
    const entries = (await readAppUsage(dir, undefined)).entries;
    assert.deepEqual(
      entries.map((e) => e.club),
      ["brookside"],
    );
  });
});
