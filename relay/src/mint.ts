import { randomBytes } from "node:crypto";
import { existsSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import { hashToken, isValidClubId } from "./tenancy.js";

/** A 64-character hex sha-256, which is the only thing a club entry can use. */
const HEX_64 = /^[0-9a-f]{64}$/i;

const DEFAULTS: Readonly<Record<string, unknown>> = Object.freeze({
  port: 8787,
  dataDir: "./data",
  maxBodyBytes: 1024 * 1024,
});

export interface ClubEntry {
  readonly club: string;
  readonly name: string;
  readonly tokenSha256: string;
  readonly publishRoster: boolean;
}

/**
 * The club's display name, assembled from whatever the shell left behind.
 *
 * On Windows, `npm run mint -- acme-rc "Acme RC"` does not deliver what it
 * appears to deliver. npm re-escapes arguments for cmd.exe, and a quoted
 * string containing a space arrives as ^Acme^ RC^ - or split across two
 * arguments with the quotes eaten. That is npm's behaviour, not the
 * operator's mistake, and the result would otherwise be a club heading
 * full of carets on a page the public can read.
 *
 * So: every remaining argument is joined back into one name, and carets
 * are removed. A caret has no business in a club name, which is what makes
 * stripping them safe rather than merely convenient.
 */
export function displayName(args: readonly string[]): { name: string | undefined; mangled: boolean } {
  const joined = args.join(" ").trim();
  const cleaned = joined.replace(/\^/g, "").replace(/\s+/g, " ").trim();

  return {
    name: cleaned.length > 0 ? cleaned : undefined,
    mangled: joined.includes("^"),
  };
}

export interface MintArgs {
  readonly nameArgs: readonly string[];
  readonly write: boolean;
  readonly configPath: string;
}

export function parseMintArgs(rest: readonly string[]): MintArgs {
  const nameArgs: string[] = [];
  let write = false;
  let configPath = "./relay.config.json";

  for (let i = 0; i < rest.length; i += 1) {
    const arg = rest[i] as string;

    if (arg === "--write" || arg === "-w") {
      write = true;
      continue;
    }

    if (arg.startsWith("--config=")) {
      configPath = arg.slice("--config=".length);
      continue;
    }

    if (arg === "--config") {
      const next = rest[i + 1];

      if (next !== undefined) {
        configPath = next;
        i += 1;
      }

      continue;
    }

    nameArgs.push(arg);
  }

  return { nameArgs, write, configPath };
}

/**
 * Puts a club into a config without disturbing the rest of it.
 *
 * Three things this is careful about, each one a way a hand-edit has
 * already gone wrong:
 *
 * - Other clubs are left exactly as they were. Re-minting one club's token
 *   is not an occasion to rewrite anybody else's entry.
 * - A club that already exists keeps its own publishRoster. A new token is
 *   not consent to start publishing a list of names.
 * - Entries whose hash is not a usable sha-256 are dropped, and named. The
 *   example config ships with a sentence where the hash goes, so a config
 *   copied from it and added to would refuse to start - correctly, but
 *   confusingly, and for a club the operator never meant to keep.
 */
export function withClub(
  existing: unknown,
  entry: ClubEntry,
): { config: Record<string, unknown>; replaced: boolean; dropped: readonly string[] } {
  const config: Record<string, unknown> =
    existing !== null && typeof existing === "object" && !Array.isArray(existing)
      ? { ...(existing as Record<string, unknown>) }
      : {};

  for (const [key, value] of Object.entries(DEFAULTS)) {
    if (config[key] === undefined) {
      config[key] = value;
    }
  }

  const present = Array.isArray(config["clubs"]) ? (config["clubs"] as unknown[]) : [];
  const dropped: string[] = [];
  const clubs: unknown[] = [];
  let replaced = false;

  for (const item of present) {
    const club = item as Record<string, unknown> | null;

    if (club === null || typeof club !== "object" || Array.isArray(club)) {
      continue;
    }

    if (club["club"] === entry.club) {
      replaced = true;
      clubs.push({ ...club, ...entry, publishRoster: club["publishRoster"] === true });
      continue;
    }

    if (typeof club["tokenSha256"] !== "string" || !HEX_64.test(club["tokenSha256"])) {
      dropped.push(typeof club["club"] === "string" ? club["club"] : "(unnamed)");
      continue;
    }

    clubs.push(club);
  }

  if (!replaced) {
    clubs.push({ ...entry });
  }

  config["clubs"] = clubs;

  return { config, replaced, dropped };
}

/** Temp file then rename, so an interrupted write cannot leave half a config. */
function saveConfig(path: string, config: Record<string, unknown>): void {
  const temporary = `${path}.tmp-${process.pid}`;

  writeFileSync(temporary, `${JSON.stringify(config, null, 2)}\n`, "utf8");
  renameSync(temporary, path);
}

/**
 * Prints a new publish token, and either writes the config or prints the
 * block to paste into it.
 *
 * The token is shown ONCE and never stored - the config holds only its
 * sha-256. That is not ceremony: the config file gets copied into backups,
 * pasted into chat windows and read over shoulders, and a file full of
 * live tokens is a file that eventually leaks all of them at once.
 */
export function mint(club: string | undefined, rest: readonly string[]): number {
  if (club === undefined || !isValidClubId(club)) {
    console.error(
      'Usage: node dist/src/index.js mint <club-id> ["Club Name"] [--write]\n\n' +
        "The club id goes in URLs and on disk: lower-case letters, digits and\n" +
        "single hyphens, 3 to 32 characters. For example: acme-rc\n\n" +
        "--write puts the club straight into relay.config.json, leaving any\n" +
        "other clubs alone. Without it, the block to paste in is printed.",
    );

    if (club !== undefined && club.includes("^")) {
      console.error(
        "\nThose carets came from npm on Windows, not from you. Call node\n" +
          "directly and npm never gets to escape anything.",
      );
    }

    return 1;
  }

  const { nameArgs, write, configPath } = parseMintArgs(rest);
  const { name, mangled } = displayName(nameArgs);
  const token = `hg_${randomBytes(24).toString("base64url")}`;

  const entry: ClubEntry = {
    club,
    name: name ?? club,
    tokenSha256: hashToken(token),
    publishRoster: false,
  };

  console.log("");
  console.log(`  Club: ${club}  (${entry.name})`);

  if (mangled) {
    console.log("");
    console.log("  Note: npm on Windows mangled the name into carets on its way here,");
    console.log(`        so it was cleaned up to "${entry.name}". Calling node directly`);
    console.log("        avoids npm's escaping altogether.");
  }

  console.log("");
  console.log("  ----------------------------------------------------------------");
  console.log("  THE TOKEN. This is the one you paste into HeatCaller, and into");
  console.log("  probe.cmd. It starts with hg_. It is shown once and is not stored");
  console.log("  anywhere - not even in the config, which holds only its hash.");
  console.log("");
  console.log(`      ${token}`);
  console.log("  ----------------------------------------------------------------");
  console.log("");

  if (!write) {
    console.log("  The config wants the HASH, not the token. Paste this whole block");
    console.log('  into the "clubs" array in relay.config.json:');
    console.log("");
    console.log(
      JSON.stringify(entry, null, 2)
        .split("\n")
        .map((line) => `    ${line}`)
        .join("\n"),
    );
    console.log("");
    console.log("  Or skip the copying: re-run this with --write and it edits the");
    console.log("  config itself, leaving every other club alone.");
    console.log("");

    return 0;
  }

  let existing: unknown;

  if (existsSync(configPath)) {
    try {
      existing = JSON.parse(readFileSync(configPath, "utf8"));
    } catch (error) {
      // Refuse rather than overwrite. A config that will not parse is
      // usually one somebody is midway through editing.
      console.error(`  ${configPath} is not readable JSON, so it was left untouched.`);
      console.error(`  ${String(error)}`);
      console.error("  Fix it, or delete it and run this again.");
      return 1;
    }
  }

  const { config, replaced, dropped } = withClub(existing, entry);

  try {
    saveConfig(configPath, config);
  } catch (error) {
    console.error(`  Could not write ${configPath}: ${String(error)}`);
    return 1;
  }

  console.log(`  ${replaced ? "Updated" : "Added"} "${club}" in ${configPath}.`);

  if (dropped.length > 0) {
    console.log(
      `  Removed ${dropped.map((id) => `"${id}"`).join(", ")} - no usable token hash, ` +
        "so the relay would have refused to start.",
    );
  }

  console.log("  Restart the relay to pick it up: the config is read once, at start.");
  console.log("");

  return 0;
}
