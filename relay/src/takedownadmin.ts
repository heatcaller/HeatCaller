import { appendAudit } from "./adminaudit.js";
import type { RelayConfig } from "./config.js";
import { FileStore } from "./store.js";
import { isValidClubId } from "./tenancy.js";
import {
  applyTakedown,
  clubsWithTakedowns,
  findTakedownTargets,
  isTakedownKind,
  readTakedowns,
  restoreTakedown,
  TAKEDOWN_KINDS,
  takedownCounts,
  type Owner,
  type TakedownRecord,
} from "./takedown.js";

/**
 * The four takedown commands (23 Nov 2026, asked for by Asher), run on the
 * relay's machine through fly ssh like the rest of voiceadmin.ts:
 *
 *   takedown-find <club> <url> [--json]
 *   takedown <club> <kind> [<ref>] --notice "<short reference>" [--json]
 *   takedown-restore <club> <takedown-id> --reason "<text>" [--json]
 *   takedown-list [<club>] [--json]
 *
 * See takedown.ts for what each kind does, and relay/README.md for the
 * exact JSON each answers - the relay admin app is built against it.
 *
 * Any value may be sent as b64:<base64url of the UTF-8 text>, as
 * audit-append's entry is, so a track's name or a notice reference with
 * quotes or spaces never meets the machine's shell.
 */

export const TAKEDOWN_COMMANDS = ["takedown-find", "takedown", "takedown-restore", "takedown-list"] as const;

export type TakedownCommand = (typeof TAKEDOWN_COMMANDS)[number];

/** A value as typed, or decoded from b64:... */
export function argValue(raw: string): string {
  if (!raw.startsWith("b64:")) {
    return raw;
  }

  const encoded = raw.slice(4);
  return /^[A-Za-z0-9_-]*$/.test(encoded) ? Buffer.from(encoded, "base64url").toString("utf8") : "";
}

/** The words and the --flag values of a command line, --json apart. */
function parse(args: readonly string[], flags: readonly string[]): { words: string[]; values: Record<string, string>; json: boolean; unknown: string[] } {
  const words: string[] = [];
  const values: Record<string, string> = {};
  const unknown: string[] = [];
  let json = false;

  for (let i = 0; i < args.length; i += 1) {
    const arg = args[i] as string;

    if (arg === "--json") {
      json = true;
    } else if (flags.includes(arg) && args[i + 1] !== undefined) {
      values[arg] = argValue(args[i + 1] as string);
      i += 1;
    } else if (arg.startsWith("--")) {
      unknown.push(arg);
    } else {
      words.push(argValue(arg));
    }
  }

  return { words, values, json, unknown };
}

/** A record as the commands print it - the stored shape, every field present. */
function recordOut(r: TakedownRecord) {
  return {
    id: r.id,
    kind: r.kind,
    ref: r.ref,
    notice: r.notice,
    state: r.state,
    takenDownAt: r.takenDownAt,
    restoredAt: r.restoredAt,
    restoreReason: r.restoreReason,
  };
}

export async function takedownAdmin(
  command: TakedownCommand,
  args: readonly string[],
  config: RelayConfig,
  now: Date,
  owner: Owner,
  out: (text: string) => void,
  err: (text: string) => void,
): Promise<number> {
  const configured = (club: string) => config.tenants.some((t) => t.club === club);
  const fail = (json: boolean, error: string, usage?: string): number => {
    if (json) out(JSON.stringify({ ok: false, error }) + "\n");
    else err(`${error}\n${usage === undefined ? "" : `${usage}\n`}`);
    return 1;
  };

  if (command === "takedown-find") {
    const usage = "Usage: takedown-find <club> <url> [--json]";
    const { words, json, unknown } = parse(args, []);
    const [club, url] = words;

    if (club === undefined || url === undefined || words.length !== 2 || unknown.length > 0) return fail(json, "takedown-find takes a club and one address.", usage);
    if (!configured(club)) return fail(json, `There is no club "${club}" in this relay's config.`);

    const found = await findTakedownTargets({ dataDir: config.dataDir, store: new FileStore(config.dataDir), club, url });

    if (!found.ok) return fail(json, found.error);

    if (json) {
      out(JSON.stringify({ ok: true, club, url, candidates: found.candidates, note: found.note }, null, 2) + "\n");
    } else {
      const lines = [`What ${url} could be, for ${club}:`, ""];
      for (const c of found.candidates) {
        lines.push(`  ${c.kind}${c.ref === "" ? "" : ` ${JSON.stringify(c.ref)}`}  - ${c.label}${c.present ? "" : " (not published now)"}${c.takedownId === null ? "" : ` - already down as ${c.takedownId}`}`);
      }
      if (found.candidates.length === 0) lines.push("  (nothing)");
      lines.push("", found.note);
      out(lines.join("\n") + "\n");
    }

    return 0;
  }

  if (command === "takedown") {
    const usage = `Usage: takedown <club> <${TAKEDOWN_KINDS.join("|")}> [<ref>] --notice "<short reference>" [--json]`;
    const { words, values, json, unknown } = parse(args, ["--notice"]);
    const [club, kind, ref, ...extra] = words;

    if (club === undefined || kind === undefined || extra.length > 0 || unknown.length > 0) return fail(json, "takedown takes a club, a kind, the item's ref and --notice.", usage);
    if (!configured(club)) return fail(json, `There is no club "${club}" in this relay's config.`);
    if (!isTakedownKind(kind)) return fail(json, `"${kind}" is not something that can be taken down. Kinds: ${TAKEDOWN_KINDS.join(", ")}.`, usage);

    const result = await applyTakedown({ dataDir: config.dataDir, club, kind, ref, notice: values["--notice"] ?? "", now, owner });

    if (!result.ok) return fail(json, result.error, usage);

    if (!result.already) {
      await appendAudit(config.dataDir, {
        at: now.toISOString(),
        action: "takedown",
        club,
        description: `Took down ${kind}${result.ref === "" ? "" : ` ${result.ref}`} (${result.id}) for notice ${result.notice}.`,
        succeeded: true,
        after: result.id,
      });
    }

    out(json
      ? JSON.stringify(result, null, 2) + "\n"
      : `${result.already ? "Already down" : "Taken down"}: ${kind}${result.ref === "" ? "" : ` ${result.ref}`} for ${club}, as ${result.id}.` +
        `${result.held === false ? " (No file was there to hold - the address answers 404 all the same.)" : ""}` +
        " The relay stops serving it within a few seconds.\n");
    return 0;
  }

  if (command === "takedown-restore") {
    const usage = `Usage: takedown-restore <club> <takedown-id> --reason "<text>" [--json]`;
    const { words, values, json, unknown } = parse(args, ["--reason"]);
    const [club, id, ...extra] = words;

    if (club === undefined || id === undefined || extra.length > 0 || unknown.length > 0) return fail(json, "takedown-restore takes a club, a takedown id and --reason.", usage);
    if (!isValidClubId(club)) return fail(json, `"${club}" is not a club id.`);

    const result = await restoreTakedown({ dataDir: config.dataDir, club, id, reason: values["--reason"] ?? "", now, owner });

    if (!result.ok) return fail(json, result.error, usage);

    await appendAudit(config.dataDir, {
      at: now.toISOString(),
      action: "takedown-restore",
      club,
      description: `Restored ${result.kind}${result.ref === "" ? "" : ` ${result.ref}`} (${result.id}): ${result.reason}`,
      succeeded: true,
      before: result.id,
    });

    out(json
      ? JSON.stringify(result, null, 2) + "\n"
      : `Restored ${result.id} (${result.kind}${result.ref === "" ? "" : ` ${result.ref}`}) for ${club}.` +
        `${result.putBack === false ? " Its held file was not there to put back." : ""} The relay serves it again within a few seconds.\n`);
    return 0;
  }

  // takedown-list [<club>]
  const { words, json, unknown } = parse(args, []);

  if (words.length > 1 || unknown.length > 0 || (words[0] !== undefined && !isValidClubId(words[0]))) {
    return fail(json, "takedown-list takes one club at most.", "Usage: takedown-list [<club>] [--json]");
  }

  const clubs = words[0] !== undefined ? [words[0]] : await clubsWithTakedowns(config.dataDir);
  const listed = [];

  for (const club of clubs) {
    try {
      const file = await readTakedowns(config.dataDir, club);
      listed.push({ club, ...takedownCounts(file.takedowns, now), takedowns: file.takedowns.map(recordOut) });
    } catch (error) {
      return fail(json, `${club}'s takedown file will not read: ${String(error)}`);
    }
  }

  if (json) {
    out(JSON.stringify({ ok: true, generatedAt: now.toISOString(), clubs: listed }, null, 2) + "\n");
    return 0;
  }

  const lines: string[] = [];

  if (listed.length === 0 || listed.every((c) => c.takedowns.length === 0)) {
    lines.push("No takedowns.");
  }

  for (const c of listed) {
    if (c.takedowns.length === 0) continue;
    lines.push(`${c.club}: ${c.active} down now, ${c.last12Months} notice(s) not countered in the last 12 months${c.repeat ? " - REPEAT: 3 or more, review under the repeat-infringer rule (nothing was closed)" : ""}`);
    for (const t of c.takedowns) {
      lines.push(`  ${t.id}  ${t.state.padEnd(8)}  ${t.kind}${t.ref === "" ? "" : ` ${JSON.stringify(t.ref)}`}  notice ${t.notice}  down ${t.takenDownAt}${t.restoredAt === null ? "" : `  restored ${t.restoredAt} (${t.restoreReason ?? ""})`}`);
    }
  }

  out(lines.join("\n") + "\n");
  return 0;
}
