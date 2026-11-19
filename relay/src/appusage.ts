import { randomBytes } from "node:crypto";
import { appendFile, mkdir, readdir, readFile, rename, unlink, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { isValidClubId } from "./tenancy.js";

/**
 * Which parts of the desktop app clubs use, and for how long (19 Nov 2026,
 * app usage round 2 - plans/app-usage-analytics-plan.md).
 *
 * A club whose box "Send HeatCaller anonymous usage totals" is ticked sends
 * its laptop's daily totals here with its publish token: per page and tab,
 * how often it was opened and the seconds it was in use and on screen,
 * plus counts of a few named events. The relay admin app reads them back
 * (app-usage, through fly ssh, like usage-log) for its Product page.
 *
 * What it never keeps: anything about a person. The laptop already writes
 * a page or tab whose caption holds a driver's name as "unknown_driver";
 * this checks the shape again, so only names from [a-z0-9_.] and plain
 * numbers can ever land on the volume. No address, no time of each click,
 * no order of clicks - totals for a day, nothing finer.
 *
 * And it never tells a newer laptop off: a row, an event or a day that
 * doesn't fit is dropped, and the report is still answered 204.
 *
 * One line per club per day reported, appended to the month it arrived in,
 * kept 13 months (one season against the last). Never rewritten, except
 * by forgetAppUsage when a club's data is removed.
 */

/** Where the monthly files live, beside the usage log's. */
export const APP_USAGE_DIR = ".app-usage";

/** How many months of files are kept. */
export const APP_USAGE_MONTHS = 13;

/** The most lines one app-usage answer carries; the app asks again from where it stopped. */
export const APP_USAGE_PAGE = 5_000;

/** The biggest report taken: a few days of a busy laptop, well under this. */
export const APP_USAGE_MAX_BODY = 512 * 1024;

/** Reports one club may send in 24 hours: a brake on a laptop stuck in a loop. */
export const APP_USAGE_REPORTS_PER_DAY = 40;

/** A day older than this is dropped: the laptop holds 31 days at most. */
export const APP_USAGE_DAYS_BACK = 31;

/** The most days, rows a day and events a day taken from one report. */
export const APP_USAGE_MAX_DAYS = 31;
export const APP_USAGE_MAX_ROWS = 400;
export const APP_USAGE_MAX_EVENTS = 60;

/** Seconds in a day: no row can hold more. */
const DAY_SECONDS = 86_400;

/** A page, tab or event name: the laptop's own slugs, nothing typed. */
const NAME = /^[a-z0-9_.]{1,64}$/;

/** An app version: "1.4.2", "1.4.2-beta.1", "1.4.2+abc". */
const VERSION = /^[0-9A-Za-z.+-]{1,32}$/;

const DATE = /^\d{4}-\d{2}-\d{2}$/;

/** One page and tab on one day, under one context. */
export interface AppUsageRow {
  readonly page: string;
  /** "" when the page has no tabs. */
  readonly tab: string;
  /** A race day was open. */
  readonly raceDay: boolean;
  /** The laptop was in Desk View. */
  readonly deskView: boolean;
  readonly opens: number;
  /** On screen with the mouse or keyboard used in the last 2 minutes. */
  readonly activeSeconds: number;
  /** On screen, HeatCaller the window in front. Never less than activeSeconds. */
  readonly shownSeconds: number;
}

/** One laptop's day, as the relay keeps it. */
export interface AppUsageDay {
  /** The laptop's own date, YYYY-MM-DD. */
  readonly date: string;
  /** The HeatCaller version that day ("" when it didn't say). */
  readonly version: string;
  readonly rows: readonly AppUsageRow[];
  readonly events: Readonly<Record<string, number>>;
}

/** One line of the file: a day, who sent it and when it arrived. */
export interface AppUsageEntry extends AppUsageDay {
  /**
   * Unique to this line. A laptop that never heard the 204 sends the day
   * again, so the app keeps the newest line for a club and date; and it
   * fetches a little overlap each time, telling repeats apart by this.
   */
  readonly id: string;
  /** When it arrived, ISO-8601. */
  readonly at: string;
  readonly club: string;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/** A whole number of seconds from 0 to a day, or undefined. */
function seconds(value: unknown): number | undefined {
  return typeof value === "number" && Number.isFinite(value) && value >= 0 && value <= DAY_SECONDS ? Math.round(value) : undefined;
}

/** A whole count from 0 to `most`, or undefined. */
function count(value: unknown, most: number): number | undefined {
  return typeof value === "number" && Number.isFinite(value) && value >= 0 && value <= most ? Math.round(value) : undefined;
}

/** "2026-11-19" for a real date, or undefined. */
function realDate(value: unknown): string | undefined {
  if (typeof value !== "string" || !DATE.test(value)) {
    return undefined;
  }

  const parsed = new Date(`${value}T00:00:00.000Z`);
  return !Number.isNaN(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value ? value : undefined;
}

function cleanRow(value: unknown): AppUsageRow | undefined {
  if (!isRecord(value)) {
    return undefined;
  }

  const page = value["page"];
  const tab = value["tab"] ?? "";
  const opens = count(value["opens"], 100_000);
  const active = seconds(value["activeSeconds"]);
  const shown = seconds(value["shownSeconds"]);

  if (typeof page !== "string" || !NAME.test(page) || typeof tab !== "string" || (tab !== "" && !NAME.test(tab))) {
    return undefined;
  }

  if (opens === undefined || active === undefined || shown === undefined) {
    return undefined;
  }

  return {
    page,
    tab,
    raceDay: value["raceDay"] === true,
    deskView: value["deskView"] === true,
    opens,
    activeSeconds: Math.min(active, shown),
    shownSeconds: shown,
  };
}

/**
 * The days of a report worth keeping, each cut to the shape above. Days
 * outside the last APP_USAGE_DAYS_BACK (or more than a day ahead, for a
 * laptop whose clock is ahead of UTC) are dropped, as is a day with
 * nothing left in it. A date sent twice in one report keeps the first.
 */
export function cleanAppUsage(body: unknown, now: Date): AppUsageDay[] {
  if (!isRecord(body) || !Array.isArray(body["days"])) {
    return [];
  }

  const today = now.getTime();
  const oldest = new Date(today - APP_USAGE_DAYS_BACK * DAY_SECONDS * 1000).toISOString().slice(0, 10);
  const newest = new Date(today + DAY_SECONDS * 1000).toISOString().slice(0, 10);
  const days: AppUsageDay[] = [];
  const seen = new Set<string>();

  for (const value of body["days"].slice(0, APP_USAGE_MAX_DAYS)) {
    if (!isRecord(value)) {
      continue;
    }

    const date = realDate(value["date"]);

    if (date === undefined || date < oldest || date > newest || seen.has(date)) {
      continue;
    }

    const version = typeof value["version"] === "string" && VERSION.test(value["version"]) ? value["version"] : "";
    const rows = Array.isArray(value["rows"])
      ? value["rows"]
          .slice(0, APP_USAGE_MAX_ROWS)
          .map(cleanRow)
          .filter((row): row is AppUsageRow => row !== undefined)
      : [];

    const events: Record<string, number> = {};

    if (isRecord(value["events"])) {
      for (const [name, times] of Object.entries(value["events"]).slice(0, APP_USAGE_MAX_EVENTS)) {
        const whole = count(times, 1_000_000);

        if (NAME.test(name) && whole !== undefined && whole > 0) {
          events[name] = whole;
        }
      }
    }

    if (rows.length === 0 && Object.keys(events).length === 0) {
      continue;
    }

    seen.add(date);
    days.push({ date, version, rows, events });
  }

  return days;
}

const MONTH_FILE = /^(\d{4}-\d{2})\.jsonl$/;

export function appUsagePath(dataDir: string, month: string): string {
  return join(dataDir, APP_USAGE_DIR, `${month}.jsonl`);
}

export class AppUsageLog {
  private readonly dataDir: string;
  private readonly log: (line: string) => void;
  private prunedFor = "";
  /** This process's prefix for line ids, and a count after it: unique without any coordination. */
  private readonly idPrefix = randomBytes(4).toString("hex");
  private idCount = 0;

  constructor(options: { readonly dataDir: string; readonly log?: (line: string) => void }) {
    this.dataDir = options.dataDir;
    this.log = options.log ?? (() => {});
  }

  /**
   * Adds one line per day (already cleaned), all at `at`. False when they
   * couldn't be written, so the laptop is told to try again and keeps them.
   */
  async record(club: string, days: readonly AppUsageDay[], at: Date): Promise<boolean> {
    if (!isValidClubId(club)) {
      return false;
    }

    if (days.length === 0) {
      return true;
    }

    try {
      const when = at.toISOString();
      const month = when.slice(0, 7);
      const text = days
        .map((day) => {
          this.idCount += 1;
          const id = `${this.idPrefix}-${this.idCount.toString(36)}`;
          return JSON.stringify({ id, at: when, club, ...day }) + "\n";
        })
        .join("");

      await mkdir(join(this.dataDir, APP_USAGE_DIR), { recursive: true, mode: 0o700 });
      await appendFile(appUsagePath(this.dataDir, month), text, { mode: 0o600 });

      if (this.prunedFor !== month) {
        this.prunedFor = month;
        await pruneAppUsage(this.dataDir, month);
      }

      return true;
    } catch (error) {
      this.log(`[app-usage] couldn't record ${days.length} day(s) for "${club}": ${String(error)}`);
      return false;
    }
  }
}

async function monthFiles(dataDir: string): Promise<string[]> {
  try {
    return (await readdir(join(dataDir, APP_USAGE_DIR))).filter((name) => MONTH_FILE.test(name)).sort();
  } catch {
    return [];
  }
}

/** Deletes month files more than APP_USAGE_MONTHS before `month`. */
export async function pruneAppUsage(dataDir: string, month: string): Promise<void> {
  const [year, number] = month.split("-").map(Number) as [number, number];
  const oldest = new Date(Date.UTC(year, number - 1 - (APP_USAGE_MONTHS - 1), 1)).toISOString().slice(0, 7);

  for (const name of await monthFiles(dataDir)) {
    if (name.slice(0, 7) < oldest) {
      await unlink(join(dataDir, APP_USAGE_DIR, name)).catch(() => {});
    }
  }
}

/**
 * Removes every line a club sent, from every month: for a club that has
 * left, or that asks for its usage totals to be deleted. The number of
 * lines removed.
 */
export async function forgetAppUsage(dataDir: string, club: string): Promise<number> {
  let removed = 0;

  for (const name of await monthFiles(dataDir)) {
    const path = join(dataDir, APP_USAGE_DIR, name);
    const lines = (await readFile(path, "utf8")).split("\n").filter((line) => line.length > 0);
    const kept = lines.filter((line) => {
      try {
        return (JSON.parse(line) as { club?: unknown }).club !== club;
      } catch {
        return true;
      }
    });

    if (kept.length === lines.length) {
      continue;
    }

    removed += lines.length - kept.length;

    if (kept.length === 0) {
      await unlink(path);
    } else {
      await writeFile(`${path}.tmp`, kept.join("\n") + "\n", { mode: 0o600 });
      await rename(`${path}.tmp`, path);
    }
  }

  return removed;
}

/** One page of lines, as app-usage --json prints it. */
export interface AppUsagePage {
  readonly version: 1;
  /** Every line after `since`, oldest first, up to the page size. */
  readonly entries: readonly AppUsageEntry[];
  /** True when there are more after the last one: ask again with since = the last line's `at`. */
  readonly more: boolean;
}

/**
 * Lines strictly after `since` (all of them when it is undefined), oldest
 * first, at most `limit` - except that a page never ends between two lines
 * that share an `at` (one report's days all do), so asking again from the
 * last `at` loses none. A line that won't read is skipped.
 */
export async function readAppUsage(dataDir: string, since: string | undefined, limit = APP_USAGE_PAGE): Promise<AppUsagePage> {
  const fromMonth = since === undefined ? "" : since.slice(0, 7);
  const entries: AppUsageEntry[] = [];
  const names = (await monthFiles(dataDir)).filter((name) => name.slice(0, 7) >= fromMonth);
  /** A later month wasn't read: there is more even if this page takes every line read. */
  let unread = false;

  for (const [index, name] of names.entries()) {
    if (entries.length > limit) {
      unread = index < names.length;
      break;
    }

    const text = await readFile(join(dataDir, APP_USAGE_DIR, name), "utf8").catch(() => "");
    const month: AppUsageEntry[] = [];

    for (const line of text.split("\n")) {
      if (line.length === 0) {
        continue;
      }

      try {
        const entry = JSON.parse(line) as AppUsageEntry;

        if (typeof entry.at === "string" && (since === undefined || entry.at > since)) {
          month.push(entry);
        }
      } catch {
        // Half a line. The rest of the month is still good.
      }
    }

    month.sort((a, b) => (a.at < b.at ? -1 : a.at > b.at ? 1 : 0));
    entries.push(...month);
  }

  if (entries.length <= limit) {
    return { version: 1, entries, more: unread };
  }

  let end = limit;
  const boundary = entries[limit - 1]?.at;

  while (end < entries.length && entries[end]?.at === boundary) {
    end += 1;
  }

  return { version: 1, entries: entries.slice(0, end), more: end < entries.length || unread };
}
