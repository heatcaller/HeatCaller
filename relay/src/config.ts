import { readFile } from "node:fs/promises";
import type { Tenant } from "./tenancy.js";
import { readAutoTopUp, type AutoTopUpPlan } from "./autotopup.js";
import { readBrainConfig, readBrainPlan, type BrainConfig } from "./brain.js";
import { readVoiceConfig, readVoicePlan, type VoiceConfig } from "./voice.js";

export interface RelayConfig {
  readonly port: number;
  readonly dataDir: string;
  readonly maxBodyBytes: number;
  readonly tenants: readonly Tenant[];
  /**
   * The HeatCaller voice catalogue and its defaults. Absent from the config
   * means an empty catalogue, which is a relay that does not offer the
   * voice at all - every club carries on with its own key or the free
   * Kokoro voice.
   */
  readonly voice: VoiceConfig;
  /**
   * The commentary brain's model and defaults. Absent means no model,
   * which is a relay that does not write commentary at all - clubs carry
   * on with Ollama on the race laptop, or with no commentary.
   */
  readonly brain: BrainConfig;
}

interface RawConfig {
  port?: unknown;
  dataDir?: unknown;
  maxBodyBytes?: unknown;
  clubs?: unknown;
  voice?: unknown;
  brain?: unknown;
}

/**
 * Reads the config, and refuses to start rather than starting wrong.
 *
 * Every failure here is a misconfiguration that would otherwise show up as
 * a club whose laptop publishes into nothing, or worse, as two clubs
 * sharing a key. Neither is something to discover during a race day, so
 * the relay would rather not come up at all.
 *
 * Two places the config can come from, tried in this order:
 *
 *   1. The RELAY_CONFIG_JSON environment variable, holding the whole
 *      config as one JSON string. This is how a hosted deployment carries
 *      it - on Fly.io it lives in a secret, which is where a token hash
 *      belongs, and the image stays free of anything club-specific.
 *   2. The file at `path`, which is how it has always worked on a laptop.
 *
 * Either way, PORT and RELAY_DATA from the environment beat what the
 * config says. That rule exists for one specific accident: a club pastes
 * its laptop config - "dataDir": "./data" and all - into the hosting
 * secret, and if the file won, the relay would quietly write inside the
 * container instead of onto the mounted volume, and every deploy would
 * erase the club's published history. The environment is the deployment
 * speaking, and the deployment knows where the disk is.
 */
export async function loadConfig(path: string, env: NodeJS.ProcessEnv = process.env): Promise<RelayConfig> {
  const inline = env["RELAY_CONFIG_JSON"];
  const fromEnv = typeof inline === "string" && inline.trim().length > 0;
  const source = fromEnv ? "RELAY_CONFIG_JSON" : path;

  let raw: RawConfig;

  if (fromEnv) {
    try {
      raw = JSON.parse(inline) as RawConfig;
    } catch (error) {
      throw new Error(`RELAY_CONFIG_JSON is set but is not readable JSON: ${String(error)}`);
    }
  } else {
    try {
      raw = JSON.parse(await readFile(path, "utf8")) as RawConfig;
    } catch (error) {
      const code = (error as NodeJS.ErrnoException).code;

      if (code === "ENOENT") {
        throw new Error(
          `No config at ${path}. Copy relay.config.example.json to ${path} and add a club - ` +
            `"npm run mint" prints a token and the line to paste in.`,
        );
      }

      throw new Error(`The config at ${path} is not readable JSON: ${String(error)}`);
    }
  }

  if (!Array.isArray(raw.clubs) || raw.clubs.length === 0) {
    throw new Error(`The config in ${source} lists no clubs. A relay with no clubs has nothing to serve.`);
  }

  // Read before the clubs: a club's voice line names catalogue voices, and
  // naming one the catalogue does not have is a misconfiguration worth
  // refusing to start over rather than discovering on a race night.
  const voice = readVoiceConfig(raw.voice, source);
  const brain = readBrainConfig(raw.brain, source);

  const tenants: Tenant[] = raw.clubs.map((entry, index) => {
    const club = entry as Partial<Tenant>;

    if (typeof club.club !== "string" || typeof club.tokenSha256 !== "string") {
      throw new Error(`Club ${index + 1} in ${source} needs at least "club" and "tokenSha256".`);
    }

    // Paused from the relay admin app. Strictly true or false: a string
    // "false" typed by hand would otherwise read as switched on, which is
    // the opposite of what whoever typed it meant.
    const active = (entry as Record<string, unknown>)["active"];

    if (active !== undefined && typeof active !== "boolean") {
      throw new Error(`"active" for ${club.club} in ${source} must be true or false, not ${JSON.stringify(active)}.`);
    }

    return {
      club: club.club,
      name: typeof club.name === "string" && club.name.length > 0 ? club.name : club.club,
      // Absent is active - see the note on Tenant.
      ...(active === false ? { active: false } : {}),
      // Lower-cased because mint and a hand-copied hash may not agree on
      // case, and the registry compares hex as written - an upper-case
      // hash would otherwise refuse to start a relay whose token is fine.
      tokenSha256: club.tokenSha256.toLowerCase(),
      // Opt-in, and the default matters: see the note on Tenant.
      publishRoster: club.publishRoster === true,
      // Opt-in as well, and for a harder reason: this one spends money.
      voice: readVoicePlan((entry as Record<string, unknown>)["voice"], voice, club.club, source),
      // Opt-in as well, and for the same reason as the voice above.
      brain: readBrainPlan((entry as Record<string, unknown>)["brain"], brain, club.club, source),
      ...autoTopUpOf((entry as Record<string, unknown>)["autoTopUp"], club.club, source),
    };
  });

  return {
    port: readPort(env["PORT"] ?? raw.port),
    dataDir: env["RELAY_DATA"] ?? (typeof raw.dataDir === "string" ? raw.dataDir : "./data"),
    maxBodyBytes: readMaxBody(raw.maxBodyBytes, source),
    tenants,
    voice,
    brain,
  };
}

/**
 * A whole number of bytes, from 1 KiB to 16 MiB. Below that nothing the
 * laptop sends would fit; above it one request could hold most of a small
 * machine's memory while it arrives. Absent (or null) means 1 MiB.
 */
function readMaxBody(value: unknown, source: string): number {
  if (value === undefined || value === null) {
    return 1024 * 1024;
  }

  if (typeof value !== "number" || !Number.isInteger(value) || value < 1024 || value > 16 * 1024 * 1024) {
    throw new Error(
      `"maxBodyBytes" in ${source} must be a whole number of bytes from 1024 to 16777216, not ${JSON.stringify(value)}.`,
    );
  }

  return value;
}

function readPort(value: unknown): number {
  const port = Number(value ?? 8787);

  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    throw new Error(`"${String(value)}" is not a usable port.`);
  }

  return port;
}

/**
 * The origin an old relay forwards to, from RELAY_REDIRECT_ORIGIN. Unset or
 * blank means an ordinary relay. Anything set must be a plain https origin
 * (http only for localhost, for trying it out) - a typo here would send
 * every badge in the club somewhere else, so it stops the relay instead.
 */
export function redirectOriginFrom(value: string | undefined): string | undefined {
  if (value === undefined || value.trim() === "") {
    return undefined;
  }

  let url: URL;
  try {
    url = new URL(value.trim());
  } catch {
    throw new Error(`RELAY_REDIRECT_ORIGIN is not an address: ${value}`);
  }

  const local = url.hostname === "localhost" || url.hostname === "127.0.0.1";
  if (url.protocol !== "https:" && !(local && url.protocol === "http:")) {
    throw new Error(`RELAY_REDIRECT_ORIGIN must be an https address: ${value}`);
  }

  if (url.pathname !== "/" || url.search !== "" || url.hash !== "" || url.username !== "" || url.password !== "") {
    throw new Error(`RELAY_REDIRECT_ORIGIN must be just an origin, like https://heatcaller-relay.fly.dev: ${value}`);
  }

  return url.origin;
}

/** A club's automatic top-ups, as a field to spread in - none at all when the config has none. */
function autoTopUpOf(value: unknown, club: string, source: string): { autoTopUp?: AutoTopUpPlan } {
  const plan = readAutoTopUp(value, club, source);
  return plan === undefined ? {} : { autoTopUp: plan };
}
