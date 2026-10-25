import { appendFile, mkdir, readdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import { isValidClubId } from "./tenancy.js";

/**
 * The relay admin app's audit log, kept on the relay as well as on the PC
 * (round E), so that with more than one admin each sees every action, not
 * only their own.
 *
 * Written only by the admin commands, over `fly ssh console` - never by
 * the running relay and never from the internet: there is still no route
 * for it. One line per action, a month to a file, in dataDir/.admin-audit.
 *
 * The app sends each entry base64url-encoded as one argument, so nothing in
 * an entry is ever read by the machine's shell; here it is decoded, checked
 * field by field, and only what passes is written.
 */

export const ADMIN_AUDIT_DIR = ".admin-audit";

/** One entry is small; this bounds the argument the app may send. */
export const ADMIN_AUDIT_MAX_ARG = 6_000;

export interface AdminAuditEntry {
  readonly at: string;
  readonly action: string;
  readonly club?: string;
  readonly description: string;
  readonly succeeded: boolean;
  readonly before?: string;
  readonly after?: string;
  /** The Fly account that did it, as flyctl named it on the admin's PC. */
  readonly by?: string;
}

function text(value: unknown, max: number): string | undefined {
  // Control characters out, so one entry is always one line.
  return typeof value === "string" ? value.replace(/[\u0000-\u001f\u007f]/g, " ").slice(0, max) : undefined;
}

/** The entry the app sent, or undefined when it isn't one. */
export function readAuditArgument(argument: string): AdminAuditEntry | undefined {
  if (argument.length === 0 || argument.length > ADMIN_AUDIT_MAX_ARG || !/^[A-Za-z0-9_-]+$/.test(argument)) {
    return undefined;
  }

  let raw: unknown;

  try {
    raw = JSON.parse(Buffer.from(argument, "base64url").toString("utf8")) as unknown;
  } catch {
    return undefined;
  }

  if (typeof raw !== "object" || raw === null || Array.isArray(raw)) {
    return undefined;
  }

  const record = raw as Record<string, unknown>;
  const at = text(record["at"], 40);
  const action = text(record["action"], 40);
  const description = text(record["description"], 500);
  const club = record["club"];

  if (at === undefined || Number.isNaN(Date.parse(at)) || action === undefined || action.length === 0 || description === undefined || typeof record["succeeded"] !== "boolean") {
    return undefined;
  }

  if (club !== undefined && club !== null && (typeof club !== "string" || !isValidClubId(club))) {
    return undefined;
  }

  const before = text(record["before"], 500);
  const after = text(record["after"], 500);
  const by = text(record["by"], 100);

  return {
    at: new Date(Date.parse(at)).toISOString(),
    action,
    ...(typeof club === "string" ? { club } : {}),
    description,
    succeeded: record["succeeded"] as boolean,
    ...(before === undefined ? {} : { before }),
    ...(after === undefined ? {} : { after }),
    ...(by === undefined ? {} : { by }),
  };
}

export async function appendAudit(dataDir: string, entry: AdminAuditEntry): Promise<void> {
  const folder = join(dataDir, ADMIN_AUDIT_DIR);
  await mkdir(folder, { recursive: true, mode: 0o700 });
  await appendFile(join(folder, `${entry.at.slice(0, 7)}.jsonl`), JSON.stringify(entry) + "\n", { encoding: "utf8", mode: 0o600 });
}

/** The newest <limit> entries, newest first. A line that won't read is skipped. */
export async function readAudit(dataDir: string, limit: number): Promise<AdminAuditEntry[]> {
  const folder = join(dataDir, ADMIN_AUDIT_DIR);
  const files = (await readdir(folder).catch(() => [] as string[])).filter((name) => /^\d{4}-\d{2}\.jsonl$/.test(name)).sort().reverse();
  const entries: AdminAuditEntry[] = [];

  for (const file of files) {
    const lines = (await readFile(join(folder, file), "utf8").catch(() => "")).split("\n").filter((line) => line.length > 0).reverse();

    for (const line of lines) {
      try {
        entries.push(JSON.parse(line) as AdminAuditEntry);
      } catch {
        // Half a line; the rest is good.
      }

      if (entries.length >= limit) {
        return entries;
      }
    }
  }

  return entries;
}
