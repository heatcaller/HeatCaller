import { lchown, mkdir, readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { BRAIN_DIR, brainTopUpPath, brainUsagePath } from "./brain.js";
import { loadConfig, type RelayConfig } from "./config.js";
import { closeRequests, openRequests, type MeterName } from "./topuprequests.js";
import {
  monthOf,
  readTopUps,
  readUsageRecord,
  resetOf,
  VOICE_DIR,
  voiceTopUpPath,
  voiceUsagePath,
  writeFileAtomically,
} from "./voice.js";

/**
 * The HeatCaller voice, from the side of whoever runs the relay: what
 * every club has spoken this month and in the months before, and
 * characters given to a club on top of its plan.
 *
 * Commands, not web addresses. Everything else on this relay names one
 * club in its path and answers only that club's token, and these would be
 * the first routes to read every club at once - so they are not routes at
 * all. They run on the relay's own machine, through `fly ssh console`
 * (voice-usage.cmd and voice-topup.cmd do that from the laptop), where
 * reaching them already takes the Fly account that can redeploy the relay.
 * Nothing new can be guessed, leaked or brute-forced from the internet.
 *
 * Read and written beside what the relay keeps (VOICE_DIR): the usage
 * files are only ever read here - the running relay is their one writer -
 * and the top-up files only ever written here, the relay reading them
 * fresh on a club's next line. So neither side can overwrite the other.
 *
 * The commentary brain's lines are metered the same way (brain-usage,
 * brain-topup), in their own files under BRAIN_DIR; and a club's asks for
 * more are listed with topup-requests - a top-up for that meter marks the
 * club's open ask done (see topuprequests.ts).
 */

/** Every command this file answers, as typed after `node dist/src/index.js`. */
export const ADMIN_COMMANDS = ["voice-usage", "voice-topup", "brain-usage", "brain-topup", "topup-requests"] as const;

export type AdminCommand = (typeof ADMIN_COMMANDS)[number];

/** What one meter is called and counted in, for the report's wording. */
const METERS = {
  voice: { title: "HeatCaller voice", unit: "characters", column: "voice", topUp: "voice-topup", usage: "voice-usage" },
  commentary: { title: "AI commentary", unit: "lines", column: "brain", topUp: "brain-topup", usage: "brain-usage" },
} as const;

/** One club's standing, as the report shows it. */
export interface VoiceStanding {
  readonly club: string;
  readonly name: string;
  /** The HeatCaller voice is included in this club's plan. */
  readonly enabled: boolean;
  /** Characters a month from the config (the club's own line, or the relay's default). */
  readonly plan: number;
  /** Characters given for this month on top of the plan (voice-topup). */
  readonly topUp: number;
  /** What the club may spend this month: plan and top-up together. */
  readonly allowance: number;
  /** Spent this month so far. */
  readonly used: number;
  /** Earlier months' totals, by "YYYY-MM", oldest first. */
  readonly history: Readonly<Record<string, number>>;
}

/** Every club in the config, with where it stands with the brain this month - the same shape as the voice's. */
export async function brainStandings(config: RelayConfig, now: Date): Promise<VoiceStanding[]> {
  const month = monthOf(now);
  const standings: VoiceStanding[] = [];

  for (const tenant of config.tenants) {
    const plan = tenant.brain?.monthlyLines ?? config.brain.monthlyLines;
    const usage = readUsageRecord(await readJson(brainUsagePath(config.dataDir, tenant.club)), month);
    const topUp = readTopUps(await readJson(brainTopUpPath(config.dataDir, tenant.club)))[month] ?? 0;

    standings.push({
      club: tenant.club,
      name: tenant.name,
      enabled: tenant.brain?.enabled === true,
      plan,
      topUp,
      allowance: plan + topUp,
      used: usage.used,
      history: usage.history,
    });
  }

  return standings;
}

/** Every club in the config, with where it stands this month. */
export async function voiceStandings(config: RelayConfig, now: Date): Promise<VoiceStanding[]> {
  const month = monthOf(now);
  const standings: VoiceStanding[] = [];

  for (const tenant of config.tenants) {
    const plan = tenant.voice?.monthlyChars ?? config.voice.monthlyChars;
    const usage = readUsageRecord(await readJson(voiceUsagePath(config.dataDir, tenant.club)), month);
    const topUp = readTopUps(await readJson(voiceTopUpPath(config.dataDir, tenant.club)))[month] ?? 0;

    standings.push({
      club: tenant.club,
      name: tenant.name,
      enabled: tenant.voice?.enabled === true,
      plan,
      topUp,
      allowance: plan + topUp,
      used: usage.used,
      history: usage.history,
    });
  }

  return standings;
}

/**
 * The report, as a table to read in a terminal: this month for every
 * club, and the three months before it.
 *
 * Clubs the voice is not included for are listed only when they have
 * spent something, now or earlier - a club switched off mid-month still
 * owes for what it said.
 */
export function formatStandings(
  standings: readonly VoiceStanding[],
  now: Date,
  dataDir?: string,
  meter: MeterName = "voice",
): string {
  const words = METERS[meter];
  const month = monthOf(now);
  const earlier = previousMonths(month, 3);
  const shown = standings.filter(
    (standing) => standing.enabled || standing.used > 0 || Object.keys(standing.history).length > 0,
  );

  const lines: string[] = [];
  const reset = new Date(resetOf(now));

  lines.push(
    `${words.title} - ${monthName(month)}, so far. Counts go back to zero on ${reset.toUTCString().replace(" GMT", " UTC")}.`,
  );

  // Said, because a report read from the wrong folder is all zeros and
  // looks exactly like a quiet month.
  if (dataDir !== undefined) {
    lines.push(`Read from ${dataDir}.`);
  }

  lines.push("");

  if (shown.length === 0) {
    lines.push(
      meter === "voice"
        ? "No club has the HeatCaller voice included, and none has spoken with it."
        : "No club has AI commentary included, and none has used it.",
    );
    return lines.join("\n") + "\n";
  }

  const header = ["club", words.column, "plan", "top-up", "used", "left", "%", ...earlier.map(shortMonth)];
  const rows = shown.map((standing) => [
    standing.club,
    standing.enabled ? "on" : "off",
    grouped(standing.plan),
    standing.topUp > 0 ? grouped(standing.topUp) : "-",
    grouped(standing.used),
    grouped(Math.max(0, standing.allowance - standing.used)),
    standing.allowance > 0 ? `${Math.floor((standing.used * 100) / standing.allowance)}%` : "-",
    ...earlier.map((past) => grouped(standing.history[past] ?? 0)),
  ]);

  const widths = header.map((title, column) => Math.max(title.length, ...rows.map((row) => (row[column] ?? "").length)));
  const pad = (cells: readonly string[]): string =>
    cells
      .map((cell, column) => (column === 0 || column === 1 ? cell.padEnd(widths[column] ?? 0) : cell.padStart(widths[column] ?? 0)))
      .join("  ")
      .trimEnd();

  lines.push(pad(header));

  for (const row of rows) {
    lines.push(pad(row));
  }

  const used = shown.reduce((sum, standing) => sum + standing.used, 0);
  lines.push("");
  lines.push(`All clubs: ${grouped(used)} ${words.unit} this month.`);

  return lines.join("\n") + "\n";
}

export type TopUpResult =
  | {
      readonly ok: true;
      readonly month: string;
      readonly topUp: number;
      readonly allowance: number;
      readonly used: number;
      /** The meter is not included for this club, so the top-up does nothing until it is. */
      readonly voiceOff: boolean;
      /** The club's open asks for more of this meter this month, now marked done. */
      readonly closed: number;
    }
  | { readonly ok: false; readonly error: string };

/** The most one top-up may add or take back - a typo with three extra zeros is refused, not billed. */
export const TOP_UP_LIMIT = 10_000_000;

/** The same for commentary lines: a plan may be at most a million a month (readBrainPlan). */
export const BRAIN_TOP_UP_LIMIT = 1_000_000;

/**
 * Adds <characters> to <club>'s allowance for this month only, or takes
 * them back with a negative number (never below no top-up at all).
 *
 * This month only, on purpose: a top-up is for a club that ran out on a
 * busy weekend. A club that needs more every month wants a bigger plan -
 * its "monthlyChars" in the config.
 *
 * The relay picks it up on the club's next line, without a restart. A
 * laptop that ran out is speaking again within half an hour (it tries
 * again every 30 minutes).
 */
export async function topUp(
  config: RelayConfig,
  club: string,
  characters: number,
  now: Date,
  owner: { readonly uid: number; readonly gid: number } | undefined,
  meter: MeterName = "voice",
): Promise<TopUpResult> {
  const tenant = config.tenants.find((known) => known.club === club);
  const limit = meter === "voice" ? TOP_UP_LIMIT : BRAIN_TOP_UP_LIMIT;

  if (tenant === undefined) {
    return { ok: false, error: `There is no club "${club}" in this relay's config.` };
  }

  if (!Number.isInteger(characters) || characters === 0 || Math.abs(characters) > limit) {
    return {
      ok: false,
      error: `Give a whole number of ${METERS[meter].unit}, other than 0, up to ${grouped(limit)} either way.`,
    };
  }

  const month = monthOf(now);
  const path = meter === "voice" ? voiceTopUpPath(config.dataDir, club) : brainTopUpPath(config.dataDir, club);
  const months: Record<string, number> = { ...readTopUps(await readJson(path)) };
  const given = Math.max(0, (months[month] ?? 0) + characters);

  if (given > 0) {
    months[month] = given;
  } else {
    delete months[month];
  }

  // Run through `fly ssh console`, this is root - and the relay itself is
  // not (dropPrivileges). A file only root could read would be a top-up the
  // relay never saw; a folder only root could write would be worse - the
  // first top-up, given before any club had spoken, made the voice's own
  // folder, and the relay could then keep no count and no line until it
  // was restarted. So every folder this may have made, and the file -
  // before it is in place - go to the relay's user.
  const voiceRoot = join(config.dataDir, meter === "voice" ? VOICE_DIR : BRAIN_DIR);

  await mkdir(dirname(path), { recursive: true, mode: 0o700 });

  if (owner !== undefined) {
    await lchown(voiceRoot, owner.uid, owner.gid);
    await lchown(dirname(path), owner.uid, owner.gid);
  }

  await writeFileAtomically(path, Buffer.from(JSON.stringify(months), "utf8"), owner);

  const plan = meter === "voice"
    ? (tenant.voice?.monthlyChars ?? config.voice.monthlyChars)
    : (tenant.brain?.monthlyLines ?? config.brain.monthlyLines);
  const usagePath = meter === "voice" ? voiceUsagePath(config.dataDir, club) : brainUsagePath(config.dataDir, club);
  const usage = readUsageRecord(await readJson(usagePath), month);
  const on = meter === "voice" ? tenant.voice?.enabled === true : tenant.brain?.enabled === true;

  // Giving more answers the club's ask for more; taking some back does not.
  const closed = characters > 0 ? await closeRequests(config.dataDir, club, meter, now, owner) : 0;

  return { ok: true, month, topUp: given, allowance: plan + given, used: usage.used, voiceOff: !on, closed };
}

/**
 * `voice-usage [--json]`, `voice-topup <club> <characters>`,
 * `brain-usage [--json]`, `brain-topup <club> <lines>` and
 * `topup-requests [--json]`, as run on the relay's machine. Prints to
 * standard output, errors to standard error, and returns the exit code.
 */
export async function voiceAdmin(
  command: AdminCommand,
  args: readonly string[],
  env: NodeJS.ProcessEnv = process.env,
  now: Date = new Date(),
  out: (text: string) => void = (text) => process.stdout.write(text),
  err: (text: string) => void = (text) => process.stderr.write(text),
): Promise<number> {
  let config: RelayConfig;

  try {
    config = await loadConfig(env["RELAY_CONFIG"] ?? "./relay.config.json", env);
  } catch (error) {
    err(`${error instanceof Error ? error.message : String(error)}\n`);
    return 1;
  }

  // On the relay's machine this is root, and the relay keeps its data in
  // RELAY_DATA. Without it the config's own dataDir is used - "./data" in
  // a laptop's config - which on the machine is a folder the relay never
  // reads: a report of zeros, and a top-up nobody sees.
  const owner = ownerFrom(env);

  if (owner === "bad") {
    err("RELAY_UID and RELAY_GID must be whole numbers - the relay's own user, 1000 unless they say otherwise.\n");
    return 1;
  }

  if (owner !== undefined && (env["RELAY_DATA"] ?? "").trim().length === 0) {
    err(`RELAY_DATA is not set, so this cannot tell where the relay keeps its data. Run it through ${command}.cmd, or set RELAY_DATA.\n`);
    return 1;
  }

  if (command === "voice-usage" || command === "brain-usage" || command === "topup-requests") {
    const unknown = args.filter((arg) => arg !== "--json");

    if (unknown.length > 0) {
      err(`${command} takes nothing but --json, not ${unknown.join(" ")}.\n`);
      return 1;
    }

    if (command === "topup-requests") {
      const open = await openRequests(config.dataDir, config.tenants.map((tenant) => tenant.club), now);

      out(args.includes("--json") ? JSON.stringify({ month: monthOf(now), dataDir: config.dataDir, open }, null, 2) + "\n" : formatRequests(open, now, config.dataDir));
      return 0;
    }

    const meter: MeterName = command === "voice-usage" ? "voice" : "commentary";
    const standings = meter === "voice" ? await voiceStandings(config, now) : await brainStandings(config, now);

    out(
      args.includes("--json")
        ? JSON.stringify({ month: monthOf(now), resetsAt: resetOf(now), dataDir: config.dataDir, clubs: standings }, null, 2) + "\n"
        : formatStandings(standings, now, config.dataDir, meter),
    );
    return 0;
  }

  const meter: MeterName = command === "voice-topup" ? "voice" : "commentary";
  const words = METERS[meter];
  const [club, amount, ...extra] = args;

  if (club === undefined || amount === undefined || extra.length > 0 || !/^[+-]?\d{1,9}$/.test(amount.replace(/[,_]/g, ""))) {
    err(
      meter === "voice"
        ? "Usage: voice-topup <club> <characters>   e.g. voice-topup acme-rc 50000   (a negative number takes some back)\n"
        : "Usage: brain-topup <club> <lines>   e.g. brain-topup acme-rc 100   (a negative number takes some back)\n",
    );
    return 1;
  }

  const result = await topUp(config, club, Number(amount.replace(/[,_]/g, "")), now, owner, meter);

  if (!result.ok) {
    err(`${result.error}\n`);
    return 1;
  }

  out(
    `${club}: ${result.topUp > 0 ? `${grouped(result.topUp)} ${words.unit} on top of its plan` : "no top-up"} for ${monthName(result.month)}. ` +
      `It may ${meter === "voice" ? "speak" : "have"} ${grouped(result.allowance)} this month and has used ${grouped(result.used)}.\n` +
      (result.voiceOff
        ? meter === "voice"
          ? `The HeatCaller voice is not switched on for ${club}, so this does nothing until it is ("voice": { "enabled": true } in the config).\n`
          : `AI commentary is not switched on for ${club}, so this does nothing until it is ("brain": { "enabled": true } in the config).\n`
        : "") +
      (result.closed > 0 ? `Marked ${club}'s request for more ${words.unit} done.\n` : ""),
  );
  return 0;
}

/** topup-requests as a list to read: who asked for what, when, and what they said. */
export function formatRequests(
  open: readonly { club: string; request: { meter: MeterName; askedAt: string; note: string } }[],
  now: Date,
  dataDir?: string,
): string {
  const lines: string[] = [`Top-up requests still open - ${monthName(monthOf(now))}.`];

  if (dataDir !== undefined) {
    lines.push(`Read from ${dataDir}.`);
  }

  lines.push("");

  if (open.length === 0) {
    lines.push("None. Nobody has asked for more this month, or every ask has had its top-up.");
    return lines.join("\n") + "\n";
  }

  for (const { club, request } of open) {
    const when = request.askedAt.replace("T", " ").replace(/\.\d+Z$/, " UTC");
    lines.push(`${club}  ${request.meter}  ${when}${request.note === "" ? "" : `  "${request.note}"`}`);
  }

  lines.push("");
  lines.push(`Answer with brain-topup <club> <lines> or voice-topup <club> <characters>; either marks the club's request done.`);

  return lines.join("\n") + "\n";
}

/**
 * The relay's own user, when this is running as root - see topUp.
 * Undefined when it is not root, or not on a system with users; "bad"
 * when RELAY_UID or RELAY_GID is not a user id, which is refused before
 * anything is written.
 */
function ownerFrom(env: NodeJS.ProcessEnv): { uid: number; gid: number } | "bad" | undefined {
  if (typeof process.getuid !== "function" || process.getuid() !== 0) {
    return undefined;
  }

  // The same defaults, and the same variables, as dropPrivileges in index.ts.
  const uid = Number(env["RELAY_UID"] ?? 1000);
  const gid = Number(env["RELAY_GID"] ?? 1000);

  return Number.isInteger(uid) && uid >= 0 && Number.isInteger(gid) && gid >= 0 ? { uid, gid } : "bad";
}

async function readJson(path: string): Promise<unknown> {
  try {
    return JSON.parse(await readFile(path, "utf8")) as unknown;
  } catch {
    // No file - a club that has never spoken, or never been topped up.
    return undefined;
  }
}

/** "2026-10" and the <count> months before it, newest first, without it: ["2026-09", "2026-08", ...]. */
function previousMonths(month: string, count: number): string[] {
  const [year, number] = month.split("-").map(Number) as [number, number];
  const months: string[] = [];

  for (let back = 1; back <= count; back += 1) {
    const when = new Date(Date.UTC(year, number - 1 - back, 1));
    months.push(monthOf(when));
  }

  return months;
}

const MONTH_NAMES = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

/** "2026-10" -> "October 2026". */
function monthName(month: string): string {
  const [year, number] = month.split("-").map(Number) as [number, number];
  return `${MONTH_NAMES[number - 1] ?? month} ${year}`;
}

/** "2026-09" -> "Sep 26". */
function shortMonth(month: string): string {
  const [year, number] = month.split("-").map(Number) as [number, number];
  return `${(MONTH_NAMES[number - 1] ?? "").slice(0, 3)} ${String(year).slice(2)}`;
}

/** 61200 -> "61,200". */
function grouped(value: number): string {
  return value.toLocaleString("en-US");
}
