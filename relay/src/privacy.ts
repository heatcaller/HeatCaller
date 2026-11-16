/**
 * THE CLUB'S PRIVACY RULES THE RELAY ENFORCES ITSELF (15 Nov 2026,
 * children's privacy fixes 1.2, 2.6 and 4.3).
 *
 * The laptop publishes one small private snapshot, "privacy":
 *
 *   juniors   - every way a junior's (under 18) name can appear on the
 *               club's public pages: "Mia K", "Mia Ke", ... "Mia Kent",
 *               and their initials when they are shown by initials. The
 *               laptop works these out (RelayPrivacyDocument), because
 *               which of them a page shows depends on who else raced.
 *   keepYears - "Unpublish results older than N years": 0 keeps them all.
 *   fromYear  - the first calendar year still kept, worked out on the
 *               laptop, so the document changes (and is sent again) when
 *               the year turns. 0 with keepYears 0.
 *
 * Private: nobody else is told which names are juniors. The relay uses it
 * to refuse following a junior by name (push.ts follower alerts), to keep
 * search engines off pages that name one (noindex), and to take old
 * results down. It never reads anything else of the club's with it.
 *
 * A document that is missing is "no juniors, keep everything" - a laptop
 * from before this sends none. One that will not read is treated as if
 * every name were a junior's (see PrivacyRules.unknown): failing closed
 * costs a follower alert or a search listing, never a child's name.
 */

/** The private snapshot the laptop publishes its rules in. See types.ts CHANNELS.privacy. */
export const PRIVACY_CHANNEL = "privacy";

/** The most name forms taken from one document. A club with a few dozen juniors needs a few hundred. */
export const PRIVACY_MAX_NAMES = 4000;

export interface PrivacyRules {
  /** Every junior name form, lower case, single-spaced. */
  readonly juniors: ReadonlySet<string>;
  /** Matches any junior name as whole words in a piece of text; undefined with no juniors. */
  readonly pattern: RegExp | undefined;
  /** 0 keeps every year. */
  readonly keepYears: number;
  /** The first calendar year kept; 0 keeps every year. */
  readonly fromYear: number;
  /** The document could not be read: treat every name as a junior's, take nothing down. */
  readonly unknown: boolean;
}

export const NO_PRIVACY_RULES: PrivacyRules = Object.freeze({
  juniors: new Set<string>(),
  pattern: undefined,
  keepYears: 0,
  fromYear: 0,
  unknown: false,
});

export const UNKNOWN_PRIVACY_RULES: PrivacyRules = Object.freeze({
  juniors: new Set<string>(),
  pattern: undefined,
  keepYears: 0,
  fromYear: 0,
  unknown: true,
});

/** A name as the rules compare it: one space between words, lower case. */
export function privacyNameKey(name: string): string {
  return name.replace(/[\u0000-\u001f\u007f]/g, " ").trim().replace(/\s+/g, " ").toLowerCase();
}

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/** The rules in a privacy document's body. Never throws. */
export function privacyRulesFrom(body: unknown): PrivacyRules {
  if (typeof body !== "object" || body === null || Array.isArray(body)) {
    return NO_PRIVACY_RULES;
  }

  const v = body as Record<string, unknown>;
  const list = Array.isArray(v["juniors"]) ? v["juniors"] : [];
  const juniors = new Set<string>();

  for (const n of list) {
    if (juniors.size >= PRIVACY_MAX_NAMES) break;
    if (typeof n !== "string") continue;
    const key = privacyNameKey(n);
    // A letter or digit at least, and no longer than a followed name can be.
    if (key.length >= 1 && key.length <= 60 && /[\p{L}\p{N}]/u.test(key)) juniors.add(key);
  }

  const whole = (x: unknown): number => (typeof x === "number" && Number.isInteger(x) && x >= 0 && x <= 9999 ? x : 0);
  const keepYears = Math.min(whole(v["keepYears"]), 100);
  const fromYear = keepYears === 0 ? 0 : whole(v["fromYear"]);

  // Longest first, so "Mia Kent" is matched whole before "Mia K" can be.
  // Whole words only, so "Mia K" is not found inside "Mia Kowalski" - but
  // "Mia K" is found in "Mia K's lap": a page that might name a junior is
  // kept out of search results, which is the safe way round.
  const alternatives = [...juniors].sort((a, b) => b.length - a.length).map((n) => escapeRegExp(n).replace(/ /g, "\\s+"));
  const pattern = alternatives.length === 0
    ? undefined
    : new RegExp(`(?<![\\p{L}\\p{N}])(?:${alternatives.join("|")})(?![\\p{L}\\p{N}])`, "iu");

  return { juniors, pattern, keepYears, fromYear, unknown: false };
}

/** Whether a published name is one of a junior's. Every name is, while the rules are unknown. */
export function isJuniorName(rules: PrivacyRules, name: string): boolean {
  return rules.unknown || rules.juniors.has(privacyNameKey(name));
}

/** Whether a document (any JSON) names a junior anywhere in it, as whole words. */
export function namesAJunior(rules: PrivacyRules, value: unknown): boolean {
  if (rules.pattern === undefined || value === undefined || value === null) {
    return false;
  }

  let text: string;

  try {
    text = typeof value === "string" ? value : JSON.stringify(value);
  } catch {
    return false;
  }

  rules.pattern.lastIndex = 0;
  return rules.pattern.test(text);
}

/** Whether a results-index year (its id, "2024") is older than the rules keep. */
export function yearTakenDown(rules: PrivacyRules, year: string): boolean {
  if (rules.unknown || rules.fromYear === 0 || !/^\d{4}$/.test(year)) {
    return false;
  }

  return Number(year) < rules.fromYear;
}

/** Whether a result document (with its startedUtc) is from a year older than the rules keep. */
export function resultTakenDown(rules: PrivacyRules, body: unknown): boolean {
  if (rules.unknown || rules.fromYear === 0 || typeof body !== "object" || body === null) {
    return false;
  }

  const started = (body as Record<string, unknown>)["startedUtc"];
  const at = typeof started === "string" ? Date.parse(started) : NaN;

  // A session saved with no start says so with year 1; it is not old, it is unknown.
  if (!Number.isFinite(at) || new Date(at).getUTCFullYear() < 1990) {
    return false;
  }

  return new Date(at).getUTCFullYear() < rules.fromYear;
}

/**
 * robots.txt (15 Nov 2026, children's privacy 4.3): search engines may
 * crawl the clubs' pages, but not the private ones - a driver's own night,
 * the PIN box, sign-up, family links, the manager, the app shell and the
 * garage - which already say noindex themselves, nor the API.
 */
export const ROBOTS_TXT = [
  "User-agent: *",
  "Allow: /",
  "Disallow: /api/",
  "Disallow: /c/*/night",
  "Disallow: /c/*/mynight",
  "Disallow: /c/*/signup",
  "Disallow: /c/*/family",
  "Disallow: /c/*/manage",
  "Disallow: /c/*/app",
  "Disallow: /c/*/garage",
  "Disallow: /c/*/clip/",
  "",
].join("\n");
