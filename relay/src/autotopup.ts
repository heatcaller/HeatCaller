import { readFile, stat } from "node:fs/promises";
import { join } from "node:path";
import type { MeterName } from "./topuprequests.js";

/**
 * Automatic top-ups (relay admin, round D): a club's ask for more answered
 * at once, by the relay, up to a limit set in the club's config - so a
 * club that runs out on a Saturday night isn't waiting for whoever runs the
 * relay to see an email.
 *
 *   "autoTopUp": { "voice": { "amount": 25000, "perMonth": 2 },
 *                  "commentary": { "amount": 200, "perMonth": 1 } }
 *
 * Off unless a club's config says so, meter by meter.
 *
 * ONE WRITER PER FILE, as everything about top-ups is (topuprequests.ts).
 * The operator's top-up file and done file are written only by the
 * operator's commands, so the relay never touches them: what it gives
 * automatically goes in a file of its own beside the requests -
 * .requests/<club>.auto.json, written only by the running relay - and the
 * allowance, the request's done mark and the admin snapshot all add it in.
 */

export interface AutoTopUpRule {
  /** Given each time: characters for the voice, lines for commentary. */
  readonly amount: number;
  /** How many asks a calendar month are answered this way; the rest wait for the operator. */
  readonly perMonth: number;
}

export interface AutoTopUpPlan {
  readonly voice?: AutoTopUpRule;
  readonly commentary?: AutoTopUpRule;
}

/** One automatic top-up, as kept. `id` is the ask it answered. */
export interface AutoGiven {
  readonly id: string;
  readonly meter: MeterName;
  readonly amount: number;
  readonly at: string;
}

/** An ask is answered automatically only once this much of the month's allowance is used. */
export const AUTO_WHEN_USED = 0.9;

/** At most this many a month: the requests themselves are capped at ten (TOP_UP_REQUESTS_PER_MONTH). */
export const AUTO_PER_MONTH_LIMIT = 10;

/** The same ceilings as one operator top-up (voiceadmin.ts TOP_UP_LIMIT, BRAIN_TOP_UP_LIMIT). */
export const AUTO_AMOUNT_LIMIT: Readonly<Record<MeterName, number>> = { voice: 10_000_000, commentary: 1_000_000 };

const MONTH_PATTERN = /^\d{4}-\d{2}$/;

export function autoTopUpPath(dataDir: string, club: string): string {
  return join(dataDir, ".requests", `${club}.auto.json`);
}

/**
 * A club's autoTopUp from the config. Absent is none. Anything that isn't
 * a whole positive amount and a count of 1 to 10 a month refuses to start,
 * as a hand-typed mistake in a plan does: this one gives away money.
 */
export function readAutoTopUp(value: unknown, club: string, source: string): AutoTopUpPlan | undefined {
  if (value === undefined || value === null) {
    return undefined;
  }

  if (typeof value !== "object" || Array.isArray(value)) {
    throw new Error(`"autoTopUp" for ${club} in ${source} must be an object like { "voice": { "amount": 25000, "perMonth": 2 } }.`);
  }

  const plan: { voice?: AutoTopUpRule; commentary?: AutoTopUpRule } = {};

  for (const [key, rule] of Object.entries(value as Record<string, unknown>)) {
    if (key !== "voice" && key !== "commentary") {
      throw new Error(`"autoTopUp" for ${club} in ${source} takes "voice" and "commentary", not "${key}".`);
    }

    if (rule === null || rule === undefined) {
      continue;
    }

    const amount = (rule as Record<string, unknown>)["amount"];
    const perMonth = (rule as Record<string, unknown>)["perMonth"];

    if (
      typeof rule !== "object" ||
      typeof amount !== "number" || !Number.isInteger(amount) || amount < 1 || amount > AUTO_AMOUNT_LIMIT[key] ||
      typeof perMonth !== "number" || !Number.isInteger(perMonth) || perMonth < 1 || perMonth > AUTO_PER_MONTH_LIMIT
    ) {
      throw new Error(
        `"autoTopUp.${key}" for ${club} in ${source} needs a whole "amount" from 1 to ${AUTO_AMOUNT_LIMIT[key]} ` +
          `and a whole "perMonth" from 1 to ${AUTO_PER_MONTH_LIMIT}.`,
      );
    }

    plan[key] = { amount, perMonth };
  }

  return plan.voice === undefined && plan.commentary === undefined ? undefined : plan;
}

/** The auto file: months to what was given. Anything unexpected in it is left out, never trusted. */
export function readAutoFile(raw: unknown): Record<string, AutoGiven[]> {
  const months: Record<string, AutoGiven[]> = {};

  if (typeof raw !== "object" || raw === null || Array.isArray(raw)) {
    return months;
  }

  for (const [month, list] of Object.entries(raw as Record<string, unknown>)) {
    if (!MONTH_PATTERN.test(month) || !Array.isArray(list)) {
      continue;
    }

    months[month] = list.filter((item): item is AutoGiven => {
      const given = item as Record<string, unknown>;
      return (
        typeof item === "object" && item !== null &&
        typeof given["id"] === "string" &&
        (given["meter"] === "voice" || given["meter"] === "commentary") &&
        typeof given["amount"] === "number" && Number.isInteger(given["amount"]) && (given["amount"] as number) > 0 &&
        typeof given["at"] === "string"
      );
    });
  }

  return months;
}

/** What was given automatically to a club in <month>, read fresh. An unreadable file is nothing given. */
export async function autoGiven(dataDir: string, club: string, month: string): Promise<AutoGiven[]> {
  try {
    return readAutoFile(JSON.parse(await readFile(autoTopUpPath(dataDir, club), "utf8")) as unknown)[month] ?? [];
  } catch {
    return [];
  }
}

/**
 * The automatic top-ups' total for one meter and month, for the voice's and
 * the brain's allowance. Kept against the file's modification time, as the
 * operator's top-ups are, so asking on every line costs one stat.
 */
export class AutoTopUps {
  private readonly cache = new Map<string, { readonly mtimeMs: number; readonly months: Record<string, AutoGiven[]> }>();

  constructor(private readonly dataDir: string) {}

  async total(club: string, meter: MeterName, month: string): Promise<number> {
    const path = autoTopUpPath(this.dataDir, club);
    const info = await stat(path).catch(() => undefined);

    if (info === undefined || !info.isFile()) {
      this.cache.delete(club);
      return 0;
    }

    let known = this.cache.get(club);

    if (known === undefined || known.mtimeMs !== info.mtimeMs) {
      try {
        known = { mtimeMs: info.mtimeMs, months: readAutoFile(JSON.parse(await readFile(path, "utf8")) as unknown) };
        this.cache.set(club, known);
      } catch {
        this.cache.delete(club);
        return 0;
      }
    }

    return (known.months[month] ?? []).filter((given) => given.meter === meter).reduce((sum, given) => sum + given.amount, 0);
  }
}
