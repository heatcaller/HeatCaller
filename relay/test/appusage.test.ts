import assert from "node:assert/strict";
import { mkdir, mkdtemp, readdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { after, afterEach, beforeEach, describe, it } from "node:test";
import {
  APP_USAGE_DIR,
  APP_USAGE_MAX_ROWS,
  APP_USAGE_REPORTS_PER_DAY,
  appUsagePath,
  AppUsageLog,
  cleanAppUsage,
  forgetAppUsage,
  pruneAppUsage,
  readAppUsage,
  type AppUsageDay,
} from "../src/appusage.js";
import { MemoryStore } from "../src/store.js";
import { hashToken } from "../src/tenancy.js";
import { voiceAdmin } from "../src/voiceadmin.js";
import { ACME_TOKEN, BROOK_TOKEN, start, tenants, type Harness } from "./helpers.js";

// The laptops' daily usage totals (19 Nov 2026, app usage round 2): page
// and tab names from the laptop's own slugs, plain numbers, nothing else.

const scratch: string[] = [];

async function folder(): Promise<string> {
  const dir = await mkdtemp(join(tmpdir(), "relay-appusage-"));
  scratch.push(dir);
  return dir;
}

after(async () => {
  for (const dir of scratch) {
    await rm(dir, { recursive: true, force: true });
  }
});

const NOW = new Date("2026-11-19T21:00:00.000Z");

function row(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return { page: "live_timing", tab: "", raceDay: true, deskView: false, opens: 3, activeSeconds: 600, shownSeconds: 5400, ...extra };
}

function day(date: string, extra: Record<string, unknown> = {}): Record<string, unknown> {
  return { date, version: "1.4.2", rows: [row()], events: { nav_search_used: 2 }, ...extra };
}

async function lines(dir: string, month: string): Promise<any[]> {
  const text = await readFile(appUsagePath(dir, month), "utf8");
  return text.trim().split("\n").map((line) => JSON.parse(line));
}

describe("app usage: the shape check", () => {
  it("keeps a laptop's report as it sends it", () => {
    assert.deepEqual(cleanAppUsage({ version: 1, days: [day("2026-11-18")] }, NOW), [
      {
        date: "2026-11-18",
        version: "1.4.2",
        rows: [{ page: "live_timing", tab: "", raceDay: true, deskView: false, opens: 3, activeSeconds: 600, shownSeconds: 5400 }],
        events: { nav_search_used: 2 },
      },
    ]);
  });

  it("keeps only the fields a row may have, and rounds the numbers", () => {
    const [kept] = cleanAppUsage({ days: [day("2026-11-18", { rows: [row({ driver: "Billy Smith", activeSeconds: 12.6, shownSeconds: 30.2 })] })] }, NOW);

    assert.deepEqual(kept?.rows, [{ page: "live_timing", tab: "", raceDay: true, deskView: false, opens: 3, activeSeconds: 13, shownSeconds: 30 }]);
    assert.ok(!JSON.stringify(kept).includes("Billy"));
  });

  it("drops a row whose name could hold typed text", () => {
    const rows = [
      row({ page: "Who's Racing" }),
      row({ page: "whos_racing", tab: "Billy Smith" }),
      row({ page: "x".repeat(65) }),
      row({ page: "whos_racing", tab: "unknown_driver" }),
    ];
    const [kept] = cleanAppUsage({ days: [day("2026-11-18", { rows })] }, NOW);

    assert.deepEqual(kept?.rows.map((r) => `${r.page}/${r.tab}`), ["whos_racing/unknown_driver"]);
  });

  it("drops a row with seconds past a day or below nothing, and caps in use at on screen", () => {
    const rows = [
      row({ shownSeconds: 86_401 }),
      row({ activeSeconds: -1 }),
      row({ opens: "3" }),
      row({ activeSeconds: Number.NaN }),
      row({ page: "setup", activeSeconds: 900, shownSeconds: 300 }),
    ];
    const [kept] = cleanAppUsage({ days: [day("2026-11-18", { rows })] }, NOW);

    assert.deepEqual(kept?.rows.map((r) => [r.page, r.activeSeconds, r.shownSeconds]), [["setup", 300, 300]]);
  });

  it("keeps events with good names and whole counts only", () => {
    const events = { nav_search_used: 4, "Billy Smith": 1, warmup_flag: 0, photo_finish: -2, export_csv: 1.4 };
    const [kept] = cleanAppUsage({ days: [day("2026-11-18", { events })] }, NOW);

    assert.deepEqual(kept?.events, { nav_search_used: 4, export_csv: 1 });
  });

  it("drops days outside the last month, days ahead, bad dates and a date sent twice", () => {
    const days = [
      day("2026-10-18"),
      day("2026-10-19"),
      day("2026-11-20"),
      day("2026-11-21"),
      day("2026-02-30"),
      day("yesterday"),
      day("2026-11-18"),
      day("2026-11-18", { version: "9.9.9" }),
    ];

    const kept = cleanAppUsage({ days }, NOW);

    assert.deepEqual(kept.map((d) => d.date), ["2026-10-19", "2026-11-20", "2026-11-18"]);
    assert.equal(kept[2]?.version, "1.4.2");
  });

  it("drops a day with nothing left, and blanks a version that isn't one", () => {
    const kept = cleanAppUsage({ days: [day("2026-11-17", { rows: [row({ page: "" })], events: {} }), day("2026-11-18", { version: "<script>" })] }, NOW);

    assert.deepEqual(kept.map((d) => [d.date, d.version]), [["2026-11-18", ""]]);
  });

  it("takes at most the row limit from a day", () => {
    const rows = Array.from({ length: APP_USAGE_MAX_ROWS + 50 }, (_, i) => row({ page: `page_${i}` }));
    const [kept] = cleanAppUsage({ days: [day("2026-11-18", { rows })] }, NOW);

    assert.equal(kept?.rows.length, APP_USAGE_MAX_ROWS);
  });

  it("answers nothing for a body that isn't a report", () => {
    for (const body of [null, [], "days", { days: "all" }, { rows: [] }]) {
      assert.deepEqual(cleanAppUsage(body, NOW), [], JSON.stringify(body));
    }
  });
});

describe("app usage: the files", () => {
  const days = (): AppUsageDay[] => cleanAppUsage({ days: [day("2026-11-17"), day("2026-11-18")] }, NOW);

  it("appends one line per day, all at the time the report arrived", async () => {
    const dir = await folder();
    const log = new AppUsageLog({ dataDir: dir });

    assert.equal(await log.record("acme-rc", days(), NOW), true);

    const written = await lines(dir, "2026-11");
    assert.deepEqual(written.map((l) => [l.club, l.date, l.at]), [
      ["acme-rc", "2026-11-17", NOW.toISOString()],
      ["acme-rc", "2026-11-18", NOW.toISOString()],
    ]);
    assert.notEqual(written[0].id, written[1].id);
  });

  it("writes nothing for a bad club id or an empty report", async () => {
    const dir = await folder();
    const log = new AppUsageLog({ dataDir: dir });

    assert.equal(await log.record("../etc", days(), NOW), false);
    assert.equal(await log.record("acme-rc", [], NOW), true);
    await assert.rejects(readdir(join(dir, APP_USAGE_DIR)));
  });

  it("says false, and doesn't throw, when the folder can't be made", async () => {
    const dir = await folder();
    await writeFile(join(dir, APP_USAGE_DIR), "in the way");
    const said: string[] = [];

    assert.equal(await new AppUsageLog({ dataDir: dir, log: (line) => said.push(line) }).record("acme-rc", days(), NOW), false);
    assert.equal(said.length, 1);
  });

  it("keeps 13 months of files", async () => {
    const dir = await folder();
    await mkdir(join(dir, APP_USAGE_DIR));

    for (const month of ["2025-10", "2025-11", "2026-11"]) {
      await writeFile(appUsagePath(dir, month), "");
    }

    await writeFile(join(dir, APP_USAGE_DIR, "notes.jsonl"), "");
    await pruneAppUsage(dir, "2026-11");

    assert.deepEqual((await readdir(join(dir, APP_USAGE_DIR))).sort(), ["2025-11.jsonl", "2026-11.jsonl", "notes.jsonl"]);
  });

  it("reads lines after a moment, oldest first, and never splits one report across pages", async () => {
    const dir = await folder();
    const log = new AppUsageLog({ dataDir: dir });

    await log.record("acme-rc", days(), new Date("2026-11-01T10:00:00.000Z"));
    await log.record("brookside", days(), new Date("2026-11-19T10:00:00.000Z"));
    await log.record("acme-rc", days(), new Date("2026-12-01T10:00:00.000Z"));

    const all = await readAppUsage(dir, undefined);
    assert.equal(all.entries.length, 6);
    assert.equal(all.more, false);

    const after = await readAppUsage(dir, "2026-11-01T10:00:00.000Z");
    assert.deepEqual(after.entries.map((e) => e.club), ["brookside", "brookside", "acme-rc", "acme-rc"]);

    const page = await readAppUsage(dir, undefined, 3);
    assert.equal(page.entries.length, 4);
    assert.equal(page.more, true);
  });

  it("skips half a line", async () => {
    const dir = await folder();
    await new AppUsageLog({ dataDir: dir }).record("acme-rc", days(), NOW);
    await writeFile(appUsagePath(dir, "2026-11"), (await readFile(appUsagePath(dir, "2026-11"), "utf8")) + "{\"at\":\"2026-11-1");

    assert.equal((await readAppUsage(dir, undefined)).entries.length, 2);
  });

  it("forgets one club and keeps the rest", async () => {
    const dir = await folder();
    const log = new AppUsageLog({ dataDir: dir });

    await log.record("acme-rc", days(), new Date("2026-10-30T10:00:00.000Z"));
    await log.record("acme-rc", days(), NOW);
    await log.record("brookside", days(), NOW);

    assert.equal(await forgetAppUsage(dir, "acme-rc"), 4);
    assert.deepEqual(await readdir(join(dir, APP_USAGE_DIR)), ["2026-11.jsonl"]);
    assert.deepEqual((await readAppUsage(dir, undefined)).entries.map((e) => e.club), ["brookside", "brookside"]);
    assert.equal(await forgetAppUsage(dir, "acme-rc"), 0);
  });
});

describe("app usage: the relay's route", () => {
  let harness: Harness | undefined;
  let dir = "";
  let clock = NOW;

  beforeEach(async () => {
    dir = await folder();
    clock = NOW;
    harness = await start(new MemoryStore(), tenants(), () => clock, () => {}, undefined, { appUsage: new AppUsageLog({ dataDir: dir }) });
  });

  afterEach(async () => {
    await harness?.close();
    harness = undefined;
  });

  async function post(body: unknown, token: string | null = ACME_TOKEN, club = "acme-rc"): Promise<number> {
    const headers: Record<string, string> = { "content-type": "application/json" };

    if (token !== null) {
      headers["authorization"] = `Bearer ${token}`;
    }

    const response = await fetch(`${harness!.base}/api/v1/c/${club}/app-usage`, {
      method: "POST",
      headers,
      body: typeof body === "string" ? body : JSON.stringify(body),
    });

    await response.arrayBuffer();
    return response.status;
  }

  it("keeps a report sent with the club's token", async () => {
    assert.equal(await post({ version: 1, days: [day("2026-11-18")] }), 204);

    const [line] = await lines(dir, "2026-11");
    assert.equal(line.club, "acme-rc");
    assert.equal(line.date, "2026-11-18");
    assert.equal(line.rows[0].page, "live_timing");
  });

  it("needs the club's own token", async () => {
    assert.equal(await post({ days: [day("2026-11-18")] }, null), 401);
    assert.equal(await post({ days: [day("2026-11-18")] }, BROOK_TOKEN), 403);
    await assert.rejects(readdir(join(dir, APP_USAGE_DIR)));
  });

  it("answers 204 to a report with nothing it can keep, and 400 to one it can't read", async () => {
    assert.equal(await post({ days: [day("2026-01-01")] }), 204);
    assert.equal(await post({ days: [], somethingNew: true }), 204);
    assert.equal(await post("not json"), 400);
    assert.equal(await post([1, 2]), 400);
    await assert.rejects(readdir(join(dir, APP_USAGE_DIR)));
  });

  it("refuses a report past the size limit", async () => {
    const rows = Array.from({ length: 4000 }, (_, i) => row({ page: `page_${i}_${"x".repeat(40)}` }));
    assert.equal(await post({ days: [day("2026-11-18", { rows })] }), 400);
  });

  it("takes a day's worth of reports from a club, then waits", async () => {
    for (let i = 0; i < APP_USAGE_REPORTS_PER_DAY; i += 1) {
      assert.equal(await post({ days: [] }), 204);
    }

    assert.equal(await post({ days: [day("2026-11-18")] }), 429);
    // Another club isn't held up by this one.
    assert.equal(await post({ days: [day("2026-11-18")] }, BROOK_TOKEN, "brookside"), 204);

    clock = new Date(NOW.getTime() + 24 * 3600 * 1000 + 1000);
    assert.equal(await post({ days: [day("2026-11-19")] }), 204);
  });

  it("answers 503 on a relay that doesn't take them, so the laptop keeps them", async () => {
    await harness?.close();
    harness = await start(new MemoryStore(), tenants(), () => clock);

    assert.equal(await post({ days: [day("2026-11-18")] }), 503);
  });
});

describe("app usage: the admin commands", () => {
  async function env(): Promise<{ dir: string; env: Record<string, string> }> {
    const dir = await folder();
    return {
      dir,
      env: {
        RELAY_DATA: dir,
        RELAY_CONFIG_JSON: JSON.stringify({ clubs: [{ club: "acme-rc", tokenSha256: hashToken(ACME_TOKEN) }] }),
      },
    };
  }

  it("prints lines after a moment as JSON", async () => {
    const { dir, env: vars } = await env();
    const log = new AppUsageLog({ dataDir: dir });
    const days = cleanAppUsage({ days: [day("2026-11-18")] }, NOW);

    await log.record("acme-rc", days, new Date("2026-11-01T00:00:00.000Z"));
    await log.record("acme-rc", days, new Date("2026-11-02T00:00:00.000Z"));

    let out = "";
    const code = await voiceAdmin("app-usage", ["--json", "--since", "2026-11-01T12:00:00Z"], vars, NOW, (text) => (out += text), () => {});

    assert.equal(code, 0);
    const page = JSON.parse(out);
    assert.equal(page.version, 1);
    assert.deepEqual(page.entries.map((e: { at: string }) => e.at), ["2026-11-02T00:00:00.000Z"]);
  });

  it("refuses arguments it doesn't know", async () => {
    const { env: vars } = await env();

    for (const args of [["--since", "soon"], ["--limit", "0"], ["--everything"]]) {
      let err = "";
      assert.equal(await voiceAdmin("app-usage", args, vars, NOW, () => {}, (text) => (err += text)), 1, args.join(" "));
      assert.match(err, /Usage: app-usage/);
    }
  });

  it("forgets one club's totals", async () => {
    const { dir, env: vars } = await env();
    await new AppUsageLog({ dataDir: dir }).record("acme-rc", cleanAppUsage({ days: [day("2026-11-18")] }, NOW), NOW);

    let out = "";
    assert.equal(await voiceAdmin("app-usage-forget", ["acme-rc", "--json"], vars, NOW, (text) => (out += text), () => {}), 0);
    assert.deepEqual(JSON.parse(out), { ok: true, club: "acme-rc", removed: 1 });
    assert.equal(await voiceAdmin("app-usage-forget", ["../x"], vars, NOW, () => {}, () => {}), 1);
  });
});
