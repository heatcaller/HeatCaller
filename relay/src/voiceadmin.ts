import { readBuildInfo, type BuildInfo } from "./buildinfo.js";
import { lchown, mkdir, readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { BRAIN_DIR, brainTopUpPath, brainUsagePath } from "./brain.js";
import { loadConfig, type RelayConfig } from "./config.js";
import { isActive, isValidClubId } from "./tenancy.js";
import { readUsageLog, USAGE_LOG_PAGE } from "./usagelog.js";
import { APP_USAGE_PAGE, forgetAppUsage, readAppUsage } from "./appusage.js";
import { PAGE_COUNTS } from "./pagecounts.js";
import { readDirectoryVisits } from "./directoryvisits.js";
import { FileStore } from "./store.js";
import { checkKeys } from "./keycheck.js";
import { checkVoiceAccount, type VoiceAccountCheck } from "./voiceaccount.js";
import { autoGiven, type AutoGiven } from "./autotopup.js";
import { appendAudit, readAudit, readAuditArgument } from "./adminaudit.js";
import { readTakedowns, takedownCounts } from "./takedown.js";
import { TAKEDOWN_COMMANDS, takedownAdmin, type TakedownCommand } from "./takedownadmin.js";
import { closeRequests, closeRequestsRefusal, isMeterName, openRequests, type MeterName, type TopUpAsk } from "./topuprequests.js";
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
export const ADMIN_COMMANDS = [
  "voice-usage",
  "voice-topup",
  "brain-usage",
  "brain-topup",
  "topup-requests",
  "topup-decline",
  "admin-snapshot",
  "usage-log",
  "app-usage",
  "app-usage-forget",
  "page-counts",
  "audit-append",
  "audit-log",
  "key-check",
  // Copyright takedowns of one item (23 Nov 2026) - see takedownadmin.ts.
  ...TAKEDOWN_COMMANDS,
] as const;

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
  /** Characters given for this month on top of the plan: voice-topup's and the relay's automatic ones together. */
  readonly topUp: number;
  /** Of topUp, what the relay gave automatically (autotopup.ts). */
  readonly autoTopUp: number;
  /** What the club may spend this month: plan and top-up together. */
  readonly allowance: number;
  /** Spent this month so far. */
  readonly used: number;
  /** Earlier months' totals, by "YYYY-MM", oldest first. */
  readonly history: Readonly<Record<string, number>>;
  /**
   * False when the club's usage file is there but will not read, so `used`
   * and `history` are stand-in zeros and the relay is not spending against
   * it. Absent means known - the same as the brain's own usageFor.
   */
  readonly known?: false;
}

/** What the relay gave a club automatically for one meter. */
function sum(given: readonly AutoGiven[], meter: MeterName): number {
  return given.filter((item) => item.meter === meter).reduce((total, item) => total + item.amount, 0);
}

/** Every club in the config, with where it stands with the brain this month - the same shape as the voice's. */
export async function brainStandings(config: RelayConfig, now: Date): Promise<VoiceStanding[]> {
  const month = monthOf(now);
  const standings: VoiceStanding[] = [];

  for (const tenant of config.tenants) {
    const plan = tenant.brain?.monthlyLines ?? config.brain.monthlyLines;
    const usage = await readUsage(brainUsagePath(config.dataDir, tenant.club), month);
    const auto = sum(await autoGiven(config.dataDir, tenant.club, month), "commentary");
    const topUp = (readTopUps(await readJson(brainTopUpPath(config.dataDir, tenant.club)))[month] ?? 0) + auto;

    standings.push({
      club: tenant.club,
      name: tenant.name,
      enabled: tenant.brain?.enabled === true,
      plan,
      topUp,
      autoTopUp: auto,
      allowance: plan + topUp,
      used: usage.used,
      history: usage.history,
      ...(usage.known ? {} : { known: false as const }),
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
    const usage = await readUsage(voiceUsagePath(config.dataDir, tenant.club), month);
    const auto = sum(await autoGiven(config.dataDir, tenant.club, month), "voice");
    const topUp = (readTopUps(await readJson(voiceTopUpPath(config.dataDir, tenant.club)))[month] ?? 0) + auto;

    standings.push({
      club: tenant.club,
      name: tenant.name,
      enabled: tenant.voice?.enabled === true,
      plan,
      topUp,
      autoTopUp: auto,
      allowance: plan + topUp,
      used: usage.used,
      history: usage.history,
      ...(usage.known ? {} : { known: false as const }),
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
    (standing) =>
      standing.enabled || standing.known === false || standing.used > 0 || Object.keys(standing.history).length > 0,
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
  // A usage file that will not read is "?", never 0: the relay is not
  // spending against it, and a zero would be billed from (Bug hunt 15).
  const rows = shown.map((standing) => [
    standing.club,
    standing.enabled ? "on" : "off",
    grouped(standing.plan),
    standing.topUp > 0 ? grouped(standing.topUp) : "-",
    ...(standing.known === false
      ? ["?", "?", "?", ...earlier.map(() => "?")]
      : [
          grouped(standing.used),
          grouped(Math.max(0, standing.allowance - standing.used)),
          standing.allowance > 0 ? `${Math.floor((standing.used * 100) / standing.allowance)}%` : "-",
          ...earlier.map((past) => grouped(standing.history[past] ?? 0)),
        ]),
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
  const unknown = shown.filter((standing) => standing.known === false).map((standing) => standing.club);
  lines.push("");
  lines.push(`All clubs: ${grouped(used)} ${words.unit} this month${unknown.length > 0 ? `, not counting ${unknown.join(", ")}` : ""}.`);

  for (const club of unknown) {
    lines.push(
      `? ${club}: its usage file is there but will not read, so what it has used is unknown and the relay is not ` +
        `spending against it. Mend the file, or move it aside, before billing or topping up.`,
    );
  }

  return lines.join("\n") + "\n";
}

export type TopUpResult =
  | {
      readonly ok: true;
      readonly month: string;
      readonly topUp: number;
      readonly allowance: number;
      readonly used: number;
      /** False when the club's usage file will not read, so `used` is a stand-in 0. Absent means known. */
      readonly known?: false;
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
  const raw = await readJsonStrict(path);

  // WHY (6 Nov 2026, Bug hunt 11): a top-up file that is there but will
  // not read (a crash mid-write, a disk error) used to count as no file,
  // and was saved over with just this top-up - "voice-topup acme-rc 10000"
  // left {"2026-11": 10000}, losing the 50,000 given earlier this month and
  // every month before. It is left as it is now, and the operator told.
  if (raw === UNREADABLE || (raw !== undefined && (typeof raw !== "object" || raw === null || Array.isArray(raw)))) {
    return {
      ok: false,
      error:
        `The top-up file ${path} will not read, so it was left as it is and nothing was given - writing over it ` +
        `would lose every top-up in it. Mend it, or move it aside, and run this again.`,
    };
  }

  // Giving more answers the club's ask for more (below). When that ask
  // cannot be marked done - its done file will not read - nothing is given
  // either, so the two never disagree (6 Nov 2026, Bug hunt 11).
  if (characters > 0) {
    const refusal = await closeRequestsRefusal(config.dataDir, club, meter, now);

    if (refusal !== undefined) {
      return { ok: false, error: `${refusal} No top-up was given.` };
    }
  }

  const months: Record<string, number> = { ...readTopUps(raw) };
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
  const usage = await readUsage(usagePath, month);
  const on = meter === "voice" ? tenant.voice?.enabled === true : tenant.brain?.enabled === true;

  // Giving more answers the club's ask for more; taking some back does not.
  let closed = 0;

  try {
    closed = characters > 0 ? await closeRequests(config.dataDir, club, meter, now, owner) : 0;
  } catch (error) {
    // Only if the done file broke between the check above and here.
    return {
      ok: false,
      error: `The top-up was given, but the club's request could not be marked done: ${error instanceof Error ? error.message : String(error)}`,
    };
  }

  // The relay's automatic top-ups count towards what the club may use, though they live in its own file.
  const auto = sum(await autoGiven(config.dataDir, club, month), meter);

  return {
    ok: true,
    month,
    topUp: given + auto,
    allowance: plan + given + auto,
    used: usage.used,
    ...(usage.known ? {} : { known: false as const }),
    voiceOff: !on,
    closed,
  };
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
  fetchImpl: typeof fetch = fetch,
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

  if ((TAKEDOWN_COMMANDS as readonly string[]).includes(command)) {
    // 23 Nov 2026, asked for by Asher: one item down after a copyright
    // notice, and back after a counter-notice - see takedownadmin.ts.
    return takedownAdmin(command as TakedownCommand, args, config, now, owner, out, err);
  }

  if (command === "admin-snapshot") {
    // --usage-since <ISO time | all> (round B): the first page of the usage
    // log after that moment comes back in the same answer, so the admin
    // app's refresh is one trip to the machine rather than two.
    let usageSince: string | null | undefined;
    const unknown: string[] = [];

    for (let i = 0; i < args.length; i += 1) {
      const arg = args[i] as string;

      if (arg === "--json") {
        continue;
      }

      if (arg === "--usage-since" && args[i + 1] === "all") {
        usageSince = null;
        i += 1;
        continue;
      }

      if (arg === "--usage-since" && args[i + 1] !== undefined && !Number.isNaN(Date.parse(args[i + 1] as string))) {
        usageSince = new Date(Date.parse(args[i + 1] as string)).toISOString();
        i += 1;
        continue;
      }

      unknown.push(arg);
    }

    if (unknown.length > 0) {
      err(`admin-snapshot takes nothing but --json and --usage-since <time|all>, not ${unknown.join(" ")}.\n`);
      return 1;
    }

    // JSON only, with or without the flag: this one is read by the relay
    // admin app, not by a person, and a table of every meter for every
    // club would not fit a terminal anyway.
    const snapshot = await adminSnapshot(config, now, env, fetchImpl);
    const usageLog = usageSince === undefined ? undefined : await readUsageLog(config.dataDir, usageSince ?? undefined, USAGE_LOG_PAGE);

    out(JSON.stringify(usageLog === undefined ? snapshot : { ...snapshot, usageLog }, null, 2) + "\n");
    return 0;
  }

  if (command === "audit-append") {
    // audit-append <base64url JSON>: one entry for the shared audit log
    // (adminaudit.ts), from the relay admin app.
    const entry = args.length === 1 ? readAuditArgument(args[0] as string) : undefined;

    if (entry === undefined) {
      err("Usage: audit-append <entry> - the entry as the relay admin app sends it.\n");
      return 1;
    }

    await appendAudit(config.dataDir, entry);
    out(JSON.stringify({ ok: true }) + "\n");
    return 0;
  }

  if (command === "key-check") {
    // key-check [--json] (v1.3.2): whether RELAY_VOICE_KEY and RELAY_BRAIN_KEY
    // work, tried on this machine where they are (keycheck.ts). Never prints them.
    if (args.some((arg) => arg !== "--json")) {
      err("Usage: key-check [--json]\n");
      return 1;
    }

    out(JSON.stringify(await checkKeys(env, config.brain.model, fetchImpl)) + "\n");
    return 0;
  }

  if (command === "audit-log") {
    // audit-log [--json] [--limit <n>]: the shared audit log, newest first.
    const at = args.indexOf("--limit");
    const limit = at >= 0 && /^\d{1,5}$/.test(args[at + 1] ?? "") ? Math.max(1, Math.min(Number(args[at + 1]), 5000)) : 1000;
    const unknown = args.filter((arg, i) => arg !== "--json" && !(at >= 0 && (i === at || i === at + 1)));

    if (unknown.length > 0) {
      err("Usage: audit-log [--json] [--limit 1000]\n");
      return 1;
    }

    out(JSON.stringify({ version: 1, entries: await readAudit(config.dataDir, limit) }) + "\n");
    return 0;
  }

  if (command === "usage-log") {
    // usage-log [--json] [--since <ISO time>] [--limit <n>]: the usage log's
    // events after a moment, oldest first, as JSON - read by the relay
    // admin app, which asks again from the last one it has. See usagelog.ts.
    let since: string | undefined;
    let limit = USAGE_LOG_PAGE;

    for (let i = 0; i < args.length; i += 1) {
      const arg = args[i] as string;

      if (arg === "--json") {
        continue;
      }

      if (arg === "--since" && args[i + 1] !== undefined && !Number.isNaN(Date.parse(args[i + 1] as string))) {
        since = new Date(Date.parse(args[i + 1] as string)).toISOString();
        i += 1;
        continue;
      }

      if (arg === "--limit" && /^\d{1,6}$/.test(args[i + 1] ?? "") && Number(args[i + 1]) > 0) {
        limit = Math.min(Number(args[i + 1]), USAGE_LOG_PAGE);
        i += 1;
        continue;
      }

      err("Usage: usage-log [--json] [--since 2026-10-01T00:00:00Z] [--limit 5000]\n");
      return 1;
    }

    out(JSON.stringify(await readUsageLog(config.dataDir, since, limit)) + "\n");
    return 0;
  }

  if (command === "app-usage") {
    // app-usage [--json] [--since <ISO time>] [--limit <n>]: the laptops'
    // daily usage totals that arrived after a moment, oldest first, as JSON -
    // read by the relay admin app's Product page, which asks again from the
    // last one it has. See appusage.ts.
    let since: string | undefined;
    let limit = APP_USAGE_PAGE;

    for (let i = 0; i < args.length; i += 1) {
      const arg = args[i] as string;

      if (arg === "--json") {
        continue;
      }

      if (arg === "--since" && args[i + 1] !== undefined && !Number.isNaN(Date.parse(args[i + 1] as string))) {
        since = new Date(Date.parse(args[i + 1] as string)).toISOString();
        i += 1;
        continue;
      }

      if (arg === "--limit" && /^\d{1,6}$/.test(args[i + 1] ?? "") && Number(args[i + 1]) > 0) {
        limit = Math.min(Number(args[i + 1]), APP_USAGE_PAGE);
        i += 1;
        continue;
      }

      err("Usage: app-usage [--json] [--since 2026-11-01T00:00:00Z] [--limit 1000]\n");
      return 1;
    }

    out(JSON.stringify(await readAppUsage(config.dataDir, since, limit)) + "\n");
    return 0;
  }

  if (command === "page-counts") {
    // page-counts [--json]: every club's anonymous page counts (pagecounts.ts)
    // by day, as JSON - read by the relay admin app. Read only.
    if (args.some((arg) => arg !== "--json")) {
      err("Usage: page-counts [--json]\n");
      return 1;
    }

    const store = new FileStore(config.dataDir);
    const clubs: Record<string, unknown> = {};

    for (const tenant of config.tenants) {
      try {
        const held = await store.getSnapshot(tenant.club, PAGE_COUNTS);
        const days = typeof held?.body === "object" && held.body !== null ? (held.body as Record<string, unknown>)["days"] : undefined;

        if (typeof days === "object" && days !== null) {
          clubs[tenant.club] = days;
        }
      } catch {
        // A club whose counts won't read is left out, not fatal.
      }
    }

    out(JSON.stringify({ version: 1, clubs }) + "\n");
    return 0;
  }

  if (command === "app-usage-forget") {
    // app-usage-forget <club> [--json]: deletes every usage total the club
    // sent - for a club that has left, or asks. See appusage.ts.
    const json = args.includes("--json");
    const rest = args.filter((arg) => arg !== "--json");
    const club = rest[0];

    if (rest.length !== 1 || club === undefined || !isValidClubId(club)) {
      err("Usage: app-usage-forget <club> [--json]\n");
      return 1;
    }

    const removed = await forgetAppUsage(config.dataDir, club);
    out(json ? JSON.stringify({ ok: true, club, removed }) + "\n" : `Removed ${removed} day(s) of usage totals for ${club}.\n`);
    return 0;
  }

  if (command === "topup-decline") {
    const json = args.includes("--json");
    const [club, meter, ...extra] = args.filter((arg) => arg !== "--json");

    if (club === undefined || meter === undefined || extra.length > 0 || !isMeterName(meter)) {
      err("Usage: topup-decline <club> <voice|commentary> [--json]   e.g. topup-decline acme-rc voice\n");
      return 1;
    }

    const result = await declineRequests(config, club, meter, now, owner);

    if (!result.ok) {
      err(`${result.error}\n`);
      return 1;
    }

    out(
      json
        ? JSON.stringify({ ok: true, club, meter, closed: result.closed }) + "\n"
        : result.closed > 0
          ? `Answered ${club}'s ${result.closed === 1 ? "request" : `${result.closed} requests`} for more ${METERS[meter].unit} without a top-up.\n`
          : `${club} has no open request for more ${METERS[meter].unit} this month.\n`,
    );
    return 0;
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
  // --json for the relay admin app, which reads the result rather than the sentence.
  const json = args.includes("--json");
  const [club, amount, ...extra] = args.filter((arg) => arg !== "--json");

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

  if (json) {
    out(JSON.stringify({ club, meter, ...result }) + "\n");
    return 0;
  }

  out(
    `${club}: ${result.topUp > 0 ? `${grouped(result.topUp)} ${words.unit} on top of its plan` : "no top-up"} for ${monthName(result.month)}. ` +
      `It may ${meter === "voice" ? "speak" : "have"} ${grouped(result.allowance)} this month and ` +
      (result.known === false
        ? `what it has used is unknown - its usage file will not read, so the relay is not spending against it. Mend it, or move it aside.\n`
        : `has used ${grouped(result.used)}.\n`) +
      (result.voiceOff
        ? meter === "voice"
          ? `The HeatCaller voice is not switched on for ${club}, so this does nothing until it is ("voice": { "enabled": true } in the config).\n`
          : `AI commentary is not switched on for ${club}, so this does nothing until it is ("brain": { "enabled": true } in the config).\n`
        : "") +
      (result.closed > 0 ? `Marked ${club}'s request for more ${words.unit} done.\n` : ""),
  );
  return 0;
}

/** One meter of one club, as admin-snapshot gives it. */
export interface MeterStanding {
  readonly enabled: boolean;
  readonly plan: number;
  readonly topUp: number;
  readonly autoTopUp: number;
  readonly allowance: number;
  readonly used: number;
  readonly history: Readonly<Record<string, number>>;
  /** False when the usage file will not read, so `used` and `history` are stand-in zeros. Absent means known. */
  readonly known?: false;
}

/** One club, as admin-snapshot gives it. */
export interface ClubSnapshot {
  readonly club: string;
  readonly name: string;
  readonly active: boolean;
  readonly publishRoster: boolean;
  /** The relay admin's half of the /clubs listing (19 Nov 2026). An added field, so `version` stays 1. */
  readonly listInDirectory: boolean;
  readonly voice: MeterStanding;
  readonly commentary: MeterStanding;
  /**
   * Copyright takedowns (23 Nov 2026, takedown.ts): items down now, notices
   * not countered in the last twelve months, and whether that is three or
   * more - a flag for the admin only; nothing closes a club. An added
   * field, so `version` stays 1. Null when the club's takedown file will
   * not read - never a stand-in zero.
   */
  readonly takedowns: { readonly active: number; readonly last12Months: number; readonly repeat: boolean } | null;
}

/**
 * Everything the relay admin app shows on its Clubs page, in one answer
 * (24 Oct 2026).
 *
 * One command rather than three because each one is a `fly ssh console`
 * from the app, and each of those is a few seconds of connecting before
 * any of it runs. Read-only: nothing here writes, so a refresh can never
 * change anything on the relay.
 *
 * Whether each key is SET is said, never what it is - the app needs to
 * know whether a club switched on for the voice can actually be heard, and
 * nothing about the key itself.
 *
 * `voiceAccount` (round 5) is the relay's own ElevenLabs account - credits
 * used and held - read from ElevenLabs with the relay's key, here on the
 * machine where the key lives (voiceaccount.ts). Counts only. It is the one
 * part of the answer that leaves the machine to find out, so it has a short
 * timeout and an answer for every way it can fail; the rest is never held up.
 *
 * `version` is the shape of this answer. It goes up when a field changes
 * meaning or goes away; added fields leave it alone, and the app ignores
 * what it does not know.
 */
export async function adminSnapshot(
  config: RelayConfig,
  now: Date,
  env: NodeJS.ProcessEnv = process.env,
  fetchImpl: typeof fetch = fetch,
  buildInfo: () => Promise<BuildInfo | null> = readBuildInfo,
): Promise<{
  readonly version: 1;
  readonly generatedAt: string;
  readonly month: string;
  readonly resetsAt: string;
  readonly dataDir: string;
  readonly relay: {
    readonly voice: { readonly voices: number; readonly monthlyChars: number; readonly keySet: boolean };
    readonly commentary: { readonly model: string; readonly monthlyLines: number; readonly keySet: boolean };
    /** Which source this build was made from (buildinfo.ts); null for a build made without `npm run build`. */
    readonly build: BuildInfo | null;
  };
  readonly clubs: readonly ClubSnapshot[];
  readonly requests: readonly ({ readonly club: string } & TopUpAsk)[];
  /** This month's asks the relay answered by itself (autotopup.ts), oldest first. */
  readonly capabilities: readonly string[];
  readonly autoTopUps: readonly ({ readonly club: string } & AutoGiven)[];
  readonly voiceAccount: VoiceAccountCheck;
  /** The /clubs page's views a day, oldest first, last 90 days, days with none left out. */
  readonly directoryVisits: readonly { readonly day: string; readonly views: number }[];
}> {
  // Started first and awaited last, so ElevenLabs answers while the files are read.
  const account = checkVoiceAccount(env["RELAY_VOICE_KEY"] ?? "", fetchImpl);
  const voice = await voiceStandings(config, now);
  const commentary = await brainStandings(config, now);
  const meter = (standing: VoiceStanding | undefined): MeterStanding => ({
    enabled: standing?.enabled ?? false,
    plan: standing?.plan ?? 0,
    topUp: standing?.topUp ?? 0,
    autoTopUp: standing?.autoTopUp ?? 0,
    allowance: standing?.allowance ?? 0,
    used: standing?.used ?? 0,
    history: standing?.history ?? {},
    ...(standing?.known === false ? { known: false as const } : {}),
  });

  const takedowns = new Map<string, ClubSnapshot["takedowns"]>();

  for (const tenant of config.tenants) {
    takedowns.set(tenant.club, await readTakedowns(config.dataDir, tenant.club).then((file) => takedownCounts(file.takedowns, now), () => null));
  }

  const clubs = config.tenants.map((tenant) => ({
    club: tenant.club,
    name: tenant.name,
    active: isActive(tenant),
    publishRoster: tenant.publishRoster,
    listInDirectory: tenant.listInDirectory === true,
    voice: meter(voice.find((standing) => standing.club === tenant.club)),
    commentary: meter(commentary.find((standing) => standing.club === tenant.club)),
    takedowns: takedowns.get(tenant.club) ?? null,
  }));

  const open = await openRequests(config.dataDir, config.tenants.map((tenant) => tenant.club), now);

  return {
    version: 1,
    generatedAt: now.toISOString(),
    month: monthOf(now),
    resetsAt: resetOf(now),
    dataDir: config.dataDir,
    relay: {
      voice: {
        voices: config.voice.voices.length,
        monthlyChars: config.voice.monthlyChars,
        keySet: (env["RELAY_VOICE_KEY"] ?? "").trim().length > 0,
      },
      commentary: {
        model: config.brain.model,
        monthlyLines: config.brain.monthlyLines,
        keySet: (env["RELAY_BRAIN_KEY"] ?? "").trim().length > 0,
      },
      build: await buildInfo(),
    },
    clubs,
    requests: open.map(({ club, request }) => ({ club, ...request })),
    // What this build can do, so the admin app never sends a command an
    // older relay doesn't know (round E). Added to, never changed.
    capabilities: ["usage-since", "auto-top-up", "audit", "build-info", "key-check", "takedowns"],
    autoTopUps: (await Promise.all(config.tenants.map(async (tenant) =>
      (await autoGiven(config.dataDir, tenant.club, monthOf(now))).map((given) => ({ club: tenant.club, ...given }))))).flat()
      .sort((a, b) => (a.at < b.at ? -1 : a.at > b.at ? 1 : 0)),
    voiceAccount: await account,
    // The /clubs page's views a day, relay-wide (19 Nov 2026,
    // directoryvisits.ts): oldest first, the last 90 days, days with none
    // left out. Added, so version stays 1.
    directoryVisits: await readDirectoryVisits(config.dataDir, now),
  };
}

/**
 * `topup-decline`: answers a club's open asks for more of one meter
 * without giving any (24 Oct 2026, relay admin).
 *
 * The ask is marked done exactly as a top-up marks it, so the club's app
 * stops showing it as waiting. Why it was declined is the relay admin
 * app's to record - nothing the club wrote is changed here.
 */
export async function declineRequests(
  config: RelayConfig,
  club: string,
  meter: MeterName,
  now: Date,
  owner: { readonly uid: number; readonly gid: number } | undefined,
): Promise<{ readonly ok: true; readonly closed: number } | { readonly ok: false; readonly error: string }> {
  if (!config.tenants.some((known) => known.club === club)) {
    return { ok: false, error: `There is no club "${club}" in this relay's config.` };
  }

  // 6 Nov 2026, Bug hunt 11: a done file that will not read is left alone
  // (closeRequests throws), and topup-decline says so rather than crashing.
  try {
    return { ok: true, closed: await closeRequests(config.dataDir, club, meter, now, owner) };
  } catch (error) {
    return { ok: false, error: error instanceof Error ? error.message : String(error) };
  }
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

const UNREADABLE = Symbol("unreadable");

/**
 * A club's usage this month, for a report. A file that is there but will
 * not read - or is not a usage record at all - is known: false, never a
 * zero: the relay will not spend against it (voice.ts readUsageFile), and
 * "0 used, no history" would be billed from (Bug hunt 15).
 */
async function readUsage(
  path: string,
  month: string,
): Promise<{ used: number; history: Readonly<Record<string, number>>; known: boolean }> {
  const raw = await readJsonStrict(path);

  if (raw === UNREADABLE || (raw !== undefined && (typeof raw !== "object" || raw === null || Array.isArray(raw)))) {
    return { used: 0, history: {}, known: false };
  }

  return { ...readUsageRecord(raw, month), known: true };
}

/**
 * readJson for a file about to be written over: undefined only when there
 * is no file, UNREADABLE when it is there and will not read (6 Nov 2026,
 * Bug hunt 11 - see topUp).
 */
async function readJsonStrict(path: string): Promise<unknown> {
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
