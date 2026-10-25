import { randomBytes } from "node:crypto";
import { appendFile, mkdir, readdir, readFile, unlink } from "node:fs/promises";
import { join } from "node:path";
import { isValidClubId } from "./tenancy.js";

/**
 * Every voice and commentary request, one line each (24 Oct 2026, relay
 * admin round 3).
 *
 * The meters in voice.ts and brain.ts keep one number per club per month
 * - enough to hold a club to its allowance, and nothing to look back on.
 * This keeps the rest: when, how much, which voice or model, how it went,
 * how long it took. The relay admin app reads it (usage-log, through fly
 * ssh, like every admin command) and draws its charts from it.
 *
 * What it never keeps: the words. Not a line spoken, not a fact sent for
 * commentary, not a driver's name - only counts and labels. A log that
 * held what was said would be a copy of every club's race nights sitting
 * on HeatCaller's volume, which is not something this needs to be.
 *
 * Appended, one month to a file, never rewritten. A line is a few hundred
 * bytes written with O_APPEND, which the kernel writes whole, so two
 * requests finishing together can't interleave. Kept for 24 months.
 *
 * And it must never cost a club a line: every failure to write is logged
 * and swallowed. The meters are the record that matters for allowances;
 * this is the record for looking back.
 */

/** Where the monthly files live, beside the voice's and the brain's own folders. */
export const USAGE_LOG_DIR = ".usage-log";

/** How many months of files are kept. */
export const USAGE_LOG_MONTHS = 24;

/** The most events one usage-log answer carries; the app asks again from where it stopped. */
export const USAGE_LOG_PAGE = 20_000;

export type UsageMeter = "voice" | "commentary";

/** One request, as the log keeps it. */
export interface UsageEvent {
  /**
   * Unique to this event (set by record). Two requests from one club in
   * the same millisecond can otherwise be identical to the byte - a burst
   * of refusals, say - and the app, which fetches a little overlap each
   * time (see readUsageLog), tells repeats from real events by this.
   */
  readonly id?: string;
  /** When it finished, ISO-8601. */
  readonly at: string;
  readonly club: string;
  readonly meter: UsageMeter;
  /**
   * What it was. Voice: "line" (named for this club), "shared" (kept for
   * every club), "reroll" (the Re-roll button), "preview". Commentary:
   * "line", or "long" for a recap or a preview over the normal length.
   */
  readonly kind: string;
  /**
   * What it cost the club: characters for the voice (0 when it came from
   * the cache), lines for commentary (1 when written, 0 when not).
   */
  readonly units: number;
  /** "ok", or the refusal: "allowance", "busy", "upstream", "not-included", "off", "text", "unknown-voice". */
  readonly outcome: string;
  /** How long the request took, start to answer, in milliseconds. */
  readonly ms: number;
  /** The voice's catalogue id (voice only). */
  readonly voice?: string;
  /** The ElevenLabs model or the Anthropic model. */
  readonly model?: string;
  /** Served from the relay's cache, so nothing was spent with ElevenLabs (voice only). */
  readonly cached?: boolean;
  /** Words asked for (commentary only). */
  readonly words?: number;
  /** Anthropic's input and output tokens, as it reported them (commentary only, when written). */
  readonly in?: number;
  readonly out?: number;
}

/** "2026-10" for a date, in UTC - the month the meters count in. */
function monthOf(when: Date): string {
  return when.toISOString().slice(0, 7);
}

const MONTH_FILE = /^(\d{4}-\d{2})\.jsonl$/;

export function usageLogPath(dataDir: string, month: string): string {
  return join(dataDir, USAGE_LOG_DIR, `${month}.jsonl`);
}

export class UsageLog {
  private readonly dataDir: string;
  private readonly log: (line: string) => void;
  private prunedFor = "";
  /** This process's prefix for event ids, and a count after it: unique without any coordination. */
  private readonly idPrefix = randomBytes(4).toString("hex");
  private idCount = 0;

  constructor(options: { readonly dataDir: string; readonly log?: (line: string) => void }) {
    this.dataDir = options.dataDir;
    this.log = options.log ?? (() => {});
  }

  /** Adds one event. Never throws: a request is never refused because its line couldn't be logged. */
  async record(event: UsageEvent): Promise<void> {
    try {
      if (!isValidClubId(event.club)) {
        return;
      }

      const month = event.at.slice(0, 7);
      const folder = join(this.dataDir, USAGE_LOG_DIR);

      await mkdir(folder, { recursive: true, mode: 0o700 });
      this.idCount += 1;
      const id = `${this.idPrefix}-${this.idCount.toString(36)}`;

      await appendFile(usageLogPath(this.dataDir, month), JSON.stringify({ id, ...clean(event) }) + "\n", { mode: 0o600 });

      if (this.prunedFor !== month) {
        this.prunedFor = month;
        await pruneUsageLog(this.dataDir, month);
      }
    } catch (error) {
      this.log(`[usage-log] couldn't record a ${event.meter} line for "${event.club}": ${String(error)}`);
    }
  }
}

/** Only the fields an event may have, each of the right kind: nothing else is ever written. */
function clean(event: UsageEvent): UsageEvent {
  const whole = (value: unknown): number | undefined =>
    typeof value === "number" && Number.isFinite(value) && value >= 0 ? Math.round(value) : undefined;
  const label = (value: unknown): string | undefined =>
    typeof value === "string" && value.length > 0 ? value.slice(0, 80) : undefined;

  const kept: Record<string, unknown> = {
    at: event.at,
    club: event.club,
    meter: event.meter,
    kind: label(event.kind) ?? "line",
    units: whole(event.units) ?? 0,
    outcome: label(event.outcome) ?? "ok",
    ms: whole(event.ms) ?? 0,
  };

  const optional: Record<string, unknown> = {
    voice: label(event.voice),
    model: label(event.model),
    cached: typeof event.cached === "boolean" ? event.cached : undefined,
    words: whole(event.words),
    in: whole(event.in),
    out: whole(event.out),
  };

  for (const [name, value] of Object.entries(optional)) {
    if (value !== undefined) {
      kept[name] = value;
    }
  }

  return kept as unknown as UsageEvent;
}

/** Deletes month files more than USAGE_LOG_MONTHS before `month`. */
export async function pruneUsageLog(dataDir: string, month: string): Promise<void> {
  const [year, number] = month.split("-").map(Number) as [number, number];
  const oldest = monthOf(new Date(Date.UTC(year, number - 1 - (USAGE_LOG_MONTHS - 1), 1)));

  let names: string[];

  try {
    names = await readdir(join(dataDir, USAGE_LOG_DIR));
  } catch {
    return;
  }

  for (const name of names) {
    const match = MONTH_FILE.exec(name);

    if (match !== null && (match[1] as string) < oldest) {
      await unlink(join(dataDir, USAGE_LOG_DIR, name)).catch(() => {});
    }
  }
}

/** One page of the log, as usage-log --json prints it. */
export interface UsageLogPage {
  readonly version: 1;
  /** Every event after `since`, oldest first, up to the page size. */
  readonly events: readonly UsageEvent[];
  /** True when there are more after the last one: ask again with since = the last event's `at`. */
  readonly more: boolean;
}

/**
 * Events strictly after `since` (all of them when it is undefined), oldest
 * first, at most `limit`. A line that won't read - half written when a
 * disk filled, say - is skipped, not fatal.
 *
 * "Strictly after" is what lets the app ask again from the last `at` it
 * has. Two events in the same millisecond at a page boundary would lose
 * the second, so a page never ends between two events that share an `at`.
 */
export async function readUsageLog(dataDir: string, since: string | undefined, limit = USAGE_LOG_PAGE): Promise<UsageLogPage> {
  let names: string[];

  try {
    names = (await readdir(join(dataDir, USAGE_LOG_DIR))).filter((name) => MONTH_FILE.test(name)).sort();
  } catch {
    return { version: 1, events: [], more: false };
  }

  const fromMonth = since === undefined ? "" : since.slice(0, 7);
  const events: UsageEvent[] = [];

  for (const name of names) {
    if (name.slice(0, 7) < fromMonth) {
      continue;
    }

    const text = await readFile(join(dataDir, USAGE_LOG_DIR, name), "utf8").catch(() => "");
    const month: UsageEvent[] = [];

    for (const line of text.split("\n")) {
      if (line.length === 0) {
        continue;
      }

      try {
        const event = JSON.parse(line) as UsageEvent;

        if (typeof event.at === "string" && (since === undefined || event.at > since)) {
          month.push(event);
        }
      } catch {
        // Half a line. The rest of the month is still good.
      }
    }

    // Appended in finishing order, which is nearly but not quite time
    // order across requests that overlapped.
    month.sort((a, b) => (a.at < b.at ? -1 : a.at > b.at ? 1 : 0));
    events.push(...month);

    if (events.length > limit) {
      break;
    }
  }

  if (events.length <= limit) {
    return { version: 1, events, more: false };
  }

  let end = limit;
  const boundary = events[limit - 1]?.at;

  while (end < events.length && events[end]?.at === boundary) {
    end += 1;
  }

  return { version: 1, events: events.slice(0, end), more: end < events.length };
}
