import { createHash, timingSafeEqual } from "node:crypto";
import type { BrainPlan } from "./brain.js";
import type { VoicePlan } from "./voice.js";

/**
 * The club id, and why it is validated in one place and nowhere else.
 *
 * This string is the tenancy boundary. It goes into every storage key, and
 * in the file-backed store it becomes a directory name - so a club id
 * containing a slash or a pair of dots would not merely be untidy, it
 * would read and write another club's data, or escape the data directory
 * altogether.
 *
 * Every entry point runs an id through here before it is allowed near a
 * store. There is exactly one definition of what a club id may contain,
 * and it is this one.
 */
const CLUB_PATTERN = /^[a-z0-9](?:[a-z0-9-]{1,30})[a-z0-9]$/;

export function isValidClubId(value: string): boolean {
  // Length is checked by the pattern, but "--" in the middle is not - and
  // a doubled separator makes two different-looking ids that people will
  // mistake for each other.
  return CLUB_PATTERN.test(value) && !value.includes("--");
}

export interface Tenant {
  /** The id in URLs and storage keys. Lower case, hyphenated. */
  readonly club: string;
  /** What the club calls itself, for the page heading. */
  readonly name: string;
  /** sha-256 of the publish token, hex. The token itself is never stored. */
  readonly tokenSha256: string;
  /**
   * Whether this club's roster may be served publicly.
   *
   * Off by default, and that default is a decision rather than caution:
   * publishing a list of names - some of them children's - on an address
   * anyone can reach is a choice a club should make deliberately. Results
   * and standings do not carry a full roster, so they are unaffected.
   */
  readonly publishRoster: boolean;
  /**
   * What this club may do with the HeatCaller voice - ElevenLabs spoken
   * through this relay, on HeatCaller's account rather than the club's.
   *
   * Absent means not included, which is the right default for a club that
   * was set up before the voice existed: it keeps speaking with its own
   * key or the free Kokoro voice, and nothing is spent on its behalf.
   */
  readonly voice?: VoicePlan;
  /**
   * What this club may do with the commentary brain - the words of an
   * intro, a recap or a mechanic's interview, written through this relay
   * on HeatCaller's account rather than on the club's own laptop.
   *
   * Absent means not included, for the same reason as the voice: it
   * spends money, so it is opted into rather than out of.
   */
  readonly brain?: BrainPlan;
}

export interface TenantConfig {
  readonly tenants: readonly Tenant[];
}

/**
 * Who is allowed to publish what.
 *
 * A token maps to exactly ONE club. There is no token that can write to
 * two, and no way to express one - which is what makes "did this request
 * come from the club it says it did" a single comparison rather than a
 * policy.
 */
export class TenantRegistry {
  private readonly byClub = new Map<string, Tenant>();
  private readonly byTokenHash = new Map<string, Tenant>();

  constructor(tenants: readonly Tenant[]) {
    for (const tenant of tenants) {
      if (!isValidClubId(tenant.club)) {
        throw new Error(
          `Refusing to start: "${tenant.club}" is not a usable club id. ` +
            `Use lower-case letters, digits and single hyphens, 3 to 32 characters.`,
        );
      }

      if (this.byClub.has(tenant.club)) {
        throw new Error(`Refusing to start: the club id "${tenant.club}" is configured twice.`);
      }

      if (!/^[0-9a-f]{64}$/.test(tenant.tokenSha256)) {
        throw new Error(
          `Refusing to start: "${tenant.club}" has no usable token hash. ` +
            `It must be the sha-256 of the publish token, as 64 hex characters.`,
        );
      }

      if (this.byTokenHash.has(tenant.tokenSha256)) {
        throw new Error(
          `Refusing to start: two clubs share a publish token. ` +
            `A token names exactly one club, or it names none.`,
        );
      }

      this.byClub.set(tenant.club, tenant);
      this.byTokenHash.set(tenant.tokenSha256, tenant);
    }
  }

  get count(): number {
    return this.byClub.size;
  }

  club(id: string): Tenant | undefined {
    return this.byClub.get(id);
  }

  /**
   * The tenant a bearer token belongs to, or undefined.
   *
   * The hash is compared with timingSafeEqual rather than ===. A plain
   * comparison returns as soon as two bytes differ, and the time it took
   * is a measurement of how much of the token was right - which is enough,
   * over enough attempts, to find the rest of it a byte at a time.
   */
  resolveToken(token: string): Tenant | undefined {
    if (token.length === 0 || token.length > 512) {
      return undefined;
    }

    const offered = createHash("sha256").update(token, "utf8").digest();

    for (const [hex, tenant] of this.byTokenHash) {
      const known = Buffer.from(hex, "hex");

      if (known.length === offered.length && timingSafeEqual(known, offered)) {
        return tenant;
      }
    }

    return undefined;
  }
}

/** The hash to put in a config file for a given token. Used by the token-minting script. */
export function hashToken(token: string): string {
  return createHash("sha256").update(token, "utf8").digest("hex");
}
