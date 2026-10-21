import { randomBytes } from "node:crypto";
import { lchown, mkdir, readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { monthOf, writeFileAtomically } from "./voice.js";

/**
 * A club asking for more of a monthly allowance - commentary lines or
 * HeatCaller voice characters - from the laptop or the manager's phone.
 *
 * Only a note for whoever runs the relay: nothing here gives anything. The
 * operator reads them with `topup-requests` (voiceadmin.ts) and answers
 * with `brain-topup` or `voice-topup`, which mark the club's open request
 * for that meter done. The relay sends no email; the app and the phone
 * page tell the operator by email themselves.
 *
 * ONE WRITER PER FILE, as the top-ups are. The running relay is the only
 * writer of a club's requests file, and the operator's command is the only
 * writer of its "done" file beside it, which holds the ids it has answered.
 * They run in different processes, so a lock in one cannot hold the other
 * back - and two writers of one file would sooner or later save over each
 * other. Each only reads the other's file.
 */

export type MeterName = "commentary" | "voice";

export const METER_NAMES: readonly MeterName[] = ["commentary", "voice"];

export interface TopUpAsk {
  readonly id: string;
  readonly meter: MeterName;
  readonly askedAt: string;
  readonly note: string;
  readonly status: "open" | "done";
}

/** The most requests a club may make in a month, open and answered together. */
export const TOP_UP_REQUESTS_PER_MONTH = 10;

/** The longest note kept, in characters. */
export const TOP_UP_NOTE_CHARS = 500;

/** Months of requests kept in a club's file. A year is all anyone looks back over. */
const MONTHS_KEPT = 12;

export const REQUESTS_DIR = ".requests";

/** Where a club's requests are kept, by month. Written by the relay only. */
export function topUpRequestsPath(dataDir: string, club: string): string {
  return join(dataDir, REQUESTS_DIR, `${club}.json`);
}

/** Where the ids of a club's answered requests are kept. Written by the operator's commands only. */
export function topUpDonePath(dataDir: string, club: string): string {
  return join(dataDir, REQUESTS_DIR, "done", `${club}.json`);
}

export function isMeterName(value: unknown): value is MeterName {
  return value === "commentary" || value === "voice";
}

/**
 * A note as it is kept: control characters (new lines included) made
 * spaces, trimmed, and no longer than TOP_UP_NOTE_CHARS - counted in
 * characters, not UTF-16 units, so an emoji is never cut in half.
 * Anything that is not a string is no note.
 */
export function cleanNote(value: unknown): string {
  if (typeof value !== "string") {
    return "";
  }

  const flat = value.replace(/[\u0000-\u001f\u007f-\u009f\u2028\u2029]/g, " ").trim();

  return Array.from(flat).slice(0, TOP_UP_NOTE_CHARS).join("").trim();
}

type StoredAsk = { id: string; meter: MeterName; askedAt: string; note: string };

/** A requests file: asks by month. Anything that is not one is left out. */
export function readRequestFile(raw: unknown): Record<string, StoredAsk[]> {
  const months: Record<string, StoredAsk[]> = {};

  if (typeof raw !== "object" || raw === null || Array.isArray(raw)) {
    return months;
  }

  for (const [month, list] of Object.entries(raw as Record<string, unknown>)) {
    if (!/^\d{4}-(0[1-9]|1[0-2])$/.test(month) || !Array.isArray(list)) {
      continue;
    }

    months[month] = list.filter(
      (item): item is StoredAsk =>
        typeof item === "object" &&
        item !== null &&
        typeof (item as StoredAsk).id === "string" &&
        isMeterName((item as StoredAsk).meter) &&
        typeof (item as StoredAsk).askedAt === "string" &&
        typeof (item as StoredAsk).note === "string",
    );
  }

  return months;
}

/** A done file: answered request ids, each with when. */
export function readDoneFile(raw: unknown): Record<string, string> {
  const done: Record<string, string> = {};

  if (typeof raw !== "object" || raw === null || Array.isArray(raw)) {
    return done;
  }

  for (const [id, when] of Object.entries(raw as Record<string, unknown>)) {
    if (typeof when === "string") {
      done[id] = when;
    }
  }

  return done;
}

/** The file's JSON, undefined for no file, "unreadable" for one that is there and will not read. */
async function readJsonFile(path: string): Promise<unknown> {
  let text: string;

  try {
    text = await readFile(path, "utf8");
  } catch (error) {
    return (error as NodeJS.ErrnoException).code === "ENOENT" ? undefined : UNREADABLE;
  }

  try {
    return JSON.parse(text) as unknown;
  } catch {
    return UNREADABLE;
  }
}

const UNREADABLE = Symbol("unreadable");

/** <month>'s requests for a club, newest first, each marked open or done. */
export async function requestsFor(dataDir: string, club: string, month: string): Promise<TopUpAsk[]> {
  const raw = await readJsonFile(topUpRequestsPath(dataDir, club));
  const doneRaw = await readJsonFile(topUpDonePath(dataDir, club));
  const asks = raw === UNREADABLE ? [] : (readRequestFile(raw)[month] ?? []);
  const done = doneRaw === UNREADABLE ? {} : readDoneFile(doneRaw);

  return asks
    .map((ask) => ({ ...ask, status: done[ask.id] === undefined ? ("open" as const) : ("done" as const) }))
    .sort((a, b) => (a.askedAt < b.askedAt ? 1 : a.askedAt > b.askedAt ? -1 : 0));
}

export type AskResult =
  | { readonly ok: true; readonly request: TopUpAsk; readonly already: boolean }
  | { readonly ok: false; readonly status: number; readonly error: string };

/** The relay's side: keeping a club's requests. */
export class TopUpRequests {
  private readonly dataDir: string;
  private readonly now: () => Date;
  private readonly log: (line: string) => void;
  private readonly locks = new Map<string, Promise<void>>();

  constructor(options: { readonly dataDir: string; readonly now?: () => Date; readonly log?: (line: string) => void }) {
    this.dataDir = options.dataDir;
    this.now = options.now ?? (() => new Date());
    this.log = options.log ?? (() => {});
  }

  /** This month's requests, newest first. */
  async list(club: string): Promise<TopUpAsk[]> {
    return requestsFor(this.dataDir, club, monthOf(this.now()));
  }

  /**
   * Records that a club wants more of <meter> this month.
   *
   * An open request for the same meter already this month is handed back
   * as it is ("already") rather than stored twice - a manager tapping Ask
   * twice, or the laptop and the phone both asking, is one request. At most
   * TOP_UP_REQUESTS_PER_MONTH a month, so a script on a stolen token cannot
   * fill the disk; the file is never written over when it will not read.
   */
  async ask(club: string, meter: MeterName, note: unknown): Promise<AskResult> {
    return this.serialise(club, async (): Promise<AskResult> => {
      const now = this.now();
      const month = monthOf(now);
      const path = topUpRequestsPath(this.dataDir, club);
      const raw = await readJsonFile(path);

      if (raw === UNREADABLE || (raw !== undefined && (typeof raw !== "object" || raw === null || Array.isArray(raw)))) {
        this.log(`[relay] "${club}": the top-up requests file will not read; not writing over it.`);
        return { ok: false, status: 503, error: "Top-up requests can't be taken just now. Email support instead." };
      }

      const months = readRequestFile(raw);
      const mine = await requestsFor(this.dataDir, club, month);
      const open = mine.find((ask) => ask.meter === meter && ask.status === "open");

      if (open !== undefined) {
        return { ok: true, request: open, already: true };
      }

      if (mine.length >= TOP_UP_REQUESTS_PER_MONTH) {
        return { ok: false, status: 429, error: "This club has asked for more as often as it can this month. Email support instead." };
      }

      const ask: StoredAsk = {
        id: `r_${randomBytes(12).toString("hex")}`,
        meter,
        askedAt: now.toISOString(),
        note: cleanNote(note),
      };

      months[month] = [...(months[month] ?? []), ask];

      const kept: Record<string, StoredAsk[]> = {};

      for (const key of Object.keys(months).sort().slice(-MONTHS_KEPT)) {
        kept[key] = months[key] as StoredAsk[];
      }

      await mkdir(dirname(path), { recursive: true, mode: 0o700 });
      await writeFileAtomically(path, Buffer.from(JSON.stringify(kept), "utf8"));

      this.log(`[relay] "${club}" asked for more ${meter === "commentary" ? "commentary lines" : "HeatCaller voice characters"} (${ask.id}).`);

      return { ok: true, request: { ...ask, status: "open" }, already: false };
    });
  }

  /**
   * Blanks the notes on every one of a club's requests, for "delete
   * everything".
   *
   * WHY (20 Oct 2026, Bug Hunt 8, #100): a note is free text that can name
   * a driver, and it lives outside the club folder, so the delete's wipe
   * never reached it. The requests themselves stay, so the monthly cap and
   * the operator's queue still hold. The done file is the operator's and
   * is left alone. A file that will not read is not written over, as in
   * ask(); nothing is written when no note needs blanking, so pressing
   * Delete again costs nothing.
   */
  async forgetNotes(club: string): Promise<void> {
    return this.serialise(club, async (): Promise<void> => {
      const path = topUpRequestsPath(this.dataDir, club);
      const raw = await readJsonFile(path);

      if (raw === undefined || raw === UNREADABLE || typeof raw !== "object" || raw === null || Array.isArray(raw)) {
        return;
      }

      const months = readRequestFile(raw);
      const blanked: Record<string, StoredAsk[]> = {};
      let changed = false;

      for (const [month, asks] of Object.entries(months)) {
        blanked[month] = asks.map((ask) => {
          if (ask.note !== "") {
            changed = true;
          }

          return { ...ask, note: "" };
        });
      }

      if (!changed) {
        return;
      }

      await writeFileAtomically(path, Buffer.from(JSON.stringify(blanked), "utf8"));
    });
  }

  private async serialise<T>(club: string, work: () => Promise<T>): Promise<T> {
    const before = this.locks.get(club) ?? Promise.resolve();
    const run = before.then(work, work);
    const settled = run.then(
      () => undefined,
      () => undefined,
    );

    this.locks.set(club, settled);

    try {
      return await run;
    } finally {
      if (this.locks.get(club) === settled) {
        this.locks.delete(club);
      }
    }
  }
}

/**
 * The operator's side, run by brain-topup and voice-topup: marks this
 * club's open requests for <meter> this month done. Returns how many.
 * Writes only the done file - see the note at the top.
 */
export async function closeRequests(
  dataDir: string,
  club: string,
  meter: MeterName,
  now: Date,
  owner: { readonly uid: number; readonly gid: number } | undefined,
): Promise<number> {
  const open = (await requestsFor(dataDir, club, monthOf(now))).filter((ask) => ask.meter === meter && ask.status === "open");

  if (open.length === 0) {
    return 0;
  }

  const path = topUpDonePath(dataDir, club);
  const doneRaw = await readJsonFile(path);
  const done = doneRaw === UNREADABLE ? {} : readDoneFile(doneRaw);

  for (const ask of open) {
    done[ask.id] = now.toISOString();
  }

  // Only ids still in the club's file are worth keeping; the relay drops a
  // month after a year, and this follows it.
  const raw = await readJsonFile(topUpRequestsPath(dataDir, club));
  const known = new Set(Object.values(raw === UNREADABLE ? {} : readRequestFile(raw)).flat().map((ask) => ask.id));
  const kept: Record<string, string> = {};

  for (const [id, when] of Object.entries(done)) {
    if (known.has(id)) {
      kept[id] = when;
    }
  }

  // As root through fly ssh, like voice-topup: every folder this may make,
  // and the file, go to the relay's own user.
  await mkdir(dirname(path), { recursive: true, mode: 0o700 });

  if (owner !== undefined) {
    await lchown(join(dataDir, REQUESTS_DIR), owner.uid, owner.gid);
    await lchown(dirname(path), owner.uid, owner.gid);
  }

  await writeFileAtomically(path, Buffer.from(JSON.stringify(kept), "utf8"), owner);

  return open.length;
}

/** Every club's open requests this month, oldest first, for topup-requests. */
export async function openRequests(
  dataDir: string,
  clubs: readonly string[],
  now: Date,
): Promise<{ club: string; request: TopUpAsk }[]> {
  const all: { club: string; request: TopUpAsk }[] = [];

  for (const club of clubs) {
    for (const ask of await requestsFor(dataDir, club, monthOf(now))) {
      if (ask.status === "open") {
        all.push({ club, request: ask });
      }
    }
  }

  return all.sort((a, b) => (a.request.askedAt < b.request.askedAt ? -1 : a.request.askedAt > b.request.askedAt ? 1 : 0));
}
