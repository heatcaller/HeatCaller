import { mkdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import { writeFileAtomically } from "./voice.js";

/**
 * HOW OFTEN /clubs IS OPENED (19 Nov 2026, cross-club index phase 2's
 * gate): one number per day, for the whole relay, for the relay admin app.
 *
 * NOTHING ABOUT WHO. No address, no user agent, no cookie, no page script:
 * the count goes up on the server as the page is served, and only the day
 * (the relay's UTC day) and the number are kept. HEAD and anything whose
 * user agent says it is a bot, a crawler, a link preview or a monitor are
 * not counted. Kept 90 days.
 *
 * Relay-wide, not per club, so it lives beside the voice's and the usage
 * log's folders on the volume rather than in any club's folder - which is
 * also why a club's Delete Everything Published does not touch it: it holds
 * nothing of any club's.
 *
 * Counted in memory and written at most every few seconds, so a busy page
 * is one small write, not one per view. Whatever is waiting is written
 * when the relay shuts down (flush).
 */

/** The folder on the volume, beside .usage-log. A dot first, so it can never be a club id. */
export const DIRECTORY_VISITS_DIR = ".directory-visits";

/** How many days are kept. */
export const DIRECTORY_VISITS_DAYS = 90;

/** How long counts wait in memory before they are written. */
export const DIRECTORY_VISITS_FLUSH_MS = 5_000;

const BOT = /bot|crawl|spider|preview|monitor/i;

/** Whether a user agent says it is not a person: bot, crawl, spider, preview or monitor. */
export function looksLikeABot(userAgent: string | undefined): boolean {
  return userAgent !== undefined && BOT.test(userAgent);
}

export function directoryVisitsPath(dataDir: string): string {
  return join(dataDir, DIRECTORY_VISITS_DIR, "visits.json");
}

/** "2026-11-19", the relay's UTC day. */
function dayOf(when: Date): string {
  return when.toISOString().slice(0, 10);
}

/** The first day kept on `when`: today and the 89 before it. */
function oldestKept(when: Date): string {
  const from = new Date(`${dayOf(when)}T00:00:00Z`);
  from.setUTCDate(from.getUTCDate() - (DIRECTORY_VISITS_DAYS - 1));
  return dayOf(from);
}

/** The stored days, anything that is not a day and a positive count left out. */
function daysFrom(value: unknown): Map<string, number> {
  const days = new Map<string, number>();

  if (typeof value === "object" && value !== null && !Array.isArray(value)) {
    for (const [day, views] of Object.entries(value as Record<string, unknown>)) {
      if (/^\d{4}-\d{2}-\d{2}$/.test(day) && typeof views === "number" && Number.isFinite(views) && views > 0) {
        days.set(day, Math.floor(views));
      }
    }
  }

  return days;
}

async function readDays(dataDir: string): Promise<Map<string, number>> {
  try {
    const parsed = JSON.parse(await readFile(directoryVisitsPath(dataDir), "utf8")) as Record<string, unknown>;
    return daysFrom(parsed["days"]);
  } catch {
    // None yet, or unreadable: start from nothing rather than refuse.
    return new Map();
  }
}

/**
 * The last 90 days as the admin snapshot gives them: oldest first, days
 * with no views left out. Never throws.
 */
export async function readDirectoryVisits(dataDir: string, now: Date): Promise<{ day: string; views: number }[]> {
  const oldest = oldestKept(now);
  const today = dayOf(now);

  return [...(await readDays(dataDir))]
    .filter(([day]) => day >= oldest && day <= today)
    .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
    .map(([day, views]) => ({ day, views }));
}

export class DirectoryVisits {
  private readonly dataDir: string;
  private readonly now: () => Date;
  private readonly log: (line: string) => void;
  private readonly flushMs: number;
  /** Views counted since the last write, by day. */
  private pending = new Map<string, number>();
  private timer: NodeJS.Timeout | undefined;
  /** Writes one after another, so two never read the same file and lose each other's counts. */
  private writing: Promise<void> = Promise.resolve();

  constructor(options: {
    readonly dataDir: string;
    readonly now?: () => Date;
    readonly log?: (line: string) => void;
    readonly flushMs?: number;
  }) {
    this.dataDir = options.dataDir;
    this.now = options.now ?? (() => new Date());
    this.log = options.log ?? (() => {});
    this.flushMs = options.flushMs ?? DIRECTORY_VISITS_FLUSH_MS;
  }

  /** One view of /clubs, now. Written within flushMs. */
  count(): void {
    const day = dayOf(this.now());
    this.pending.set(day, (this.pending.get(day) ?? 0) + 1);

    if (this.timer === undefined) {
      this.timer = setTimeout(() => {
        this.timer = undefined;
        void this.flush();
      }, this.flushMs);
      this.timer.unref();
    }
  }

  /** Writes whatever is waiting. Never throws: a count that cannot be written is logged and dropped. */
  flush(): Promise<void> {
    if (this.timer !== undefined) {
      clearTimeout(this.timer);
      this.timer = undefined;
    }

    const adding = this.pending;
    this.pending = new Map();

    this.writing = this.writing.then(async () => {
      if (adding.size === 0) {
        return;
      }

      try {
        const days = await readDays(this.dataDir);

        for (const [day, views] of adding) {
          days.set(day, (days.get(day) ?? 0) + views);
        }

        const oldest = oldestKept(this.now());
        const kept: Record<string, number> = {};

        for (const [day, views] of [...days].sort(([a], [b]) => (a < b ? -1 : 1))) {
          if (day >= oldest) {
            kept[day] = views;
          }
        }

        await mkdir(join(this.dataDir, DIRECTORY_VISITS_DIR), { recursive: true, mode: 0o700 });
        await writeFileAtomically(directoryVisitsPath(this.dataDir), Buffer.from(JSON.stringify({ days: kept }), "utf8"));
      } catch (error) {
        this.log(`[relay] could not save the directory's visit counts (${String(error)}).`);
      }
    });

    return this.writing;
  }
}
