import { isActive, type Tenant } from "./tenancy.js";
import type { Look } from "./site.js";
import type { StoredDocument } from "./types.js";

/**
 * THE CLUB DIRECTORY (19 Nov 2026, cross-club index phases 1 and 2 - see
 * plans/cross-club-index-proposal.md, option B).
 *
 * One public page, /clubs, listing the clubs on this relay that asked to be
 * listed: the club's name, a link to its own pages, a "Racing now" badge
 * and its next announced night. NOTHING PER DRIVER: no names, no line-ups,
 * no results - a club's own pages, with its own privacy settings, are one
 * tap away for that.
 *
 * Two keys, like the roster: the club ticks "List this club in the
 * HeatCaller directory" (the site document's `directory`, Look.directory)
 * AND the relay admin sets `"listInDirectory": true` on the club's entry in
 * the relay config (Tenant.listInDirectory). Either one off is not listed.
 * A paused club is not listed. Delete Everything Published takes the site
 * document, so it takes the listing with it.
 *
 * Nothing here is stored: the page is worked out from documents the relay
 * already holds, and sets no cookie and keeps nothing in the browser.
 */

/** How new the last live frame must be for "Racing now" - checked when the page is served. */
export const DIRECTORY_FRESH_MS = 60_000;

/** How long the facts behind the page are held before they are read again. */
export const DIRECTORY_HELD_MS = 5_000;

/** The race states a live frame says a race is on in (the same list Home uses). */
const LIVE_STATES: ReadonlySet<string> = new Set(["Staging", "Countdown", "Racing"]);

const WEEKDAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/** What the relay holds about one listed club, read once and held DIRECTORY_HELD_MS. */
export interface DirectoryFacts {
  readonly club: string;
  readonly name: string;
  /** When the last live frame reached the relay (ms), only if it said a race is on. */
  readonly liveAt: number | undefined;
  /** The class and heat the schedule says is on track, or "" - never who is in it. */
  readonly racing: string;
  /** The next announced night, in words, and when the laptop said to stop offering it. */
  readonly next: { readonly words: string; readonly until: number } | undefined;
}

/** One line of the page, as of the moment it is served. */
export interface DirectoryEntry {
  readonly club: string;
  readonly name: string;
  readonly racingNow: boolean;
  /** Only while racingNow. */
  readonly racing: string;
  /** "" when there is no night to show. */
  readonly next: string;
}

/** Whether a club is listed: both keys turned, and the club not paused. */
export function isListed(tenant: Tenant, look: Look): boolean {
  return isActive(tenant) && tenant.listInDirectory === true && look.directory === true;
}

/** One line of a club's own text: control characters out, spaces collapsed, capped. */
function tidy(value: unknown, max: number): string {
  return typeof value !== "string"
    ? ""
    : value.replace(/[\u0000-\u001f\u007f]/g, " ").replace(/\s+/g, " ").trim().slice(0, max);
}

function record(value: unknown): Record<string, unknown> | undefined {
  return typeof value === "object" && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : undefined;
}

/** "19:00" as the club writes times: 24-hour only when it said so. */
function clockWords(value: string, clock24: boolean | undefined): string {
  const match = /^(\d{1,2}):(\d{2})$/.exec(value);

  if (match === null) {
    return "";
  }

  const hour = Number(match[1]);
  const minute = match[2] as string;

  if (hour > 23) {
    return "";
  }

  if (clock24 === true) {
    return `${String(hour).padStart(2, "0")}:${minute}`;
  }

  return `${hour % 12 === 0 ? 12 : hour % 12}:${minute}${hour < 12 ? "am" : "pm"}`;
}

/**
 * The next night's line - "Thu 21 Nov from 7:00pm EST" - or undefined with
 * none to show. The page lists clubs anywhere, so a time names the club's
 * zone whenever the laptop said it.
 */
export function nextNightWords(body: unknown, clock24: boolean | undefined, timeZone?: string): { words: string; until: number } | undefined {
  const b = record(body);
  const day = typeof b?.["day"] === "string" ? /^(\d{4})-(\d{2})-(\d{2})$/.exec(b["day"]) : null;
  const until = typeof b?.["until"] === "string" ? Date.parse(b["until"]) : NaN;

  if (b === undefined || day === null || !Number.isFinite(until)) {
    return undefined;
  }

  const date = new Date(Date.UTC(Number(day[1]), Number(day[2]) - 1, Number(day[3])));

  if (!Number.isFinite(date.getTime())) {
    return undefined;
  }

  const dayWords = `${WEEKDAYS[date.getUTCDay()]} ${date.getUTCDate()} ${MONTHS[date.getUTCMonth()]}`;

  if (b["off"] === true) {
    return { words: `${dayWords}: called off`, until };
  }

  const clock = clockWords(tidy(b["firstRace"], 5), clock24);
  const zone = clock === "" ? "" : zoneWord(date, timeZone);
  const time = zone === "" ? clock : `${clock} ${zone}`;
  const title = tidy(b["title"], 60);
  const words = [`${dayWords}${time === "" ? "" : ` from ${time}`}`, title].filter((part) => part !== "").join(" · ");
  return { words, until };
}

/** "EST" or "GMT+13" for the zone on that day (noon UTC), or nothing for no zone or one Node does not know. */
function zoneWord(day: Date, timeZone: string | undefined): string {
  if (timeZone === undefined || timeZone === "") {
    return "";
  }

  try {
    const noon = new Date(day.getTime() + 12 * 60 * 60 * 1000);
    const part = new Intl.DateTimeFormat("en-US", { timeZone, timeZoneName: "short" }).formatToParts(noon).find((p) => p.type === "timeZoneName");
    return part?.value ?? "";
  } catch {
    return "";
  }
}

/** The class and heat on track, from the schedule's running line - its class and description only. */
export function racingWords(programme: unknown): string {
  const lines = record(programme)?.["lines"];

  if (!Array.isArray(lines)) {
    return "";
  }

  const running = lines.map(record).find((line) => line?.["status"] === "running");
  return running === undefined
    ? ""
    : [tidy(running["className"], 40), tidy(running["description"], 40)].filter((part) => part !== "").join(" · ");
}

/** What a listed club's documents say, ready to hold. */
export function directoryFacts(
  tenant: Tenant,
  look: Look,
  state: StoredDocument | undefined,
  programme: StoredDocument | undefined,
  nextnight: StoredDocument | undefined,
): DirectoryFacts {
  const frame = record(state?.body);
  const at = state === undefined ? NaN : Date.parse(state.publishedAt);
  const live = frame !== undefined && LIVE_STATES.has(String(frame["state"])) && Number.isFinite(at);

  return {
    club: tenant.club,
    name: tenant.name,
    liveAt: live ? at : undefined,
    racing: live ? racingWords(programme?.body) : "",
    next: nextNightWords(nextnight?.body, look.clock24, look.timeZone),
  };
}

/**
 * The page's line for a club, as of `now`. "Racing now" only from a live
 * frame under DIRECTORY_FRESH_MS old - an older one is dropped, never shown
 * as if it were live (the announcer's trust-over-chatter rule). A night
 * past its `until` is not shown.
 */
export function directoryEntry(facts: DirectoryFacts, now: number): DirectoryEntry {
  const age = facts.liveAt === undefined ? Infinity : now - facts.liveAt;
  const racingNow = age >= 0 && age < DIRECTORY_FRESH_MS;

  return {
    club: facts.club,
    name: facts.name,
    racingNow,
    racing: racingNow ? facts.racing : "",
    next: facts.next !== undefined && now <= facts.next.until ? facts.next.words : "",
  };
}

/** Racing clubs first, then by name. */
export function directoryOrder(a: DirectoryEntry, b: DirectoryEntry): number {
  return a.racingNow !== b.racingNow ? (a.racingNow ? -1 : 1) : a.name.localeCompare(b.name);
}
