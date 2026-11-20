import { createHash } from "node:crypto";
import { SUN_ICON_VERSION } from "./sun.js";
import type { StoredDocument } from "./types.js";

/**
 * How a club's pages look, taken from the "site" document the laptop
 * publishes.
 *
 * This is the one place the relay reads a club's payload rather than
 * passing it along untouched, and it is kept deliberately narrow: hex
 * colours, a line of text, a PNG. Anything else in the document is
 * ignored, and anything malformed falls back to the default look rather
 * than reaching a page - a colour that went straight into a <style>
 * block unchecked would be a way to put anything at all into it.
 */
export interface Look {
  /**
   * The club is letting people listen along from home, so its pages offer
   * the control that plays the announcer - see CHANNELS.listen.
   *
   * Merged in rather than published with the rest of the look, exactly as
   * `checkIn` is: it is a switch the laptop sets on a document of its own,
   * and a page that showed a Listen button for a club that had not turned
   * it on would be offering something that answers nothing.
   */
  readonly listening: boolean;
  /**
   * The club takes messages for the announcer from its own pages (2 Nov
   * 2026), so Live offers the form - see Relay.notesOpenFor. Merged in by
   * Relay.lookFor, as `listening` is, and for the same reason: a form for a
   * club that has not switched it on would send into nothing.
   */
  readonly notes: boolean;
  /**
   * The club lets its own pages vote in the room's vote (2 Nov 2026), so
   * Live asks for the ballot and shows the grid while a vote is open - see
   * Relay.roomBallotFor. Whether a vote is open right now is the page's to
   * ask: it changes every race, and the page is drawn once.
   */
  readonly vote: boolean;
  /** 24-hour times (true), 12-hour (false), or the phone's own locale (undefined) - the laptop's tick on Themes. */
  readonly clock24?: boolean | undefined;
  /** The club's time zone (IANA, "America/New_York"), or undefined for the phone's own (14 Nov 2026, driver phone plan item 15). */
  readonly timeZone?: string | undefined;
  /** The name under the home-screen icon, up to 12 characters, or undefined for the club's name cut short (item 5). */
  readonly shortName?: string | undefined;
  /** A short address the club chose - /c/<alias> redirects here (item 5) - or undefined. */
  readonly alias?: string | undefined;
  /** Light-on-dark or dark-on-light, for color-scheme and a few contrast fixes. */
  readonly light: boolean;
  /** CSS custom properties to override, name to value. Empty for the default look. */
  readonly vars: Readonly<Record<string, string>>;
  /** The page background, for the browser's toolbar colour. */
  readonly themeColor: string;
  /** A line under the club's name on the home page. Plain text. */
  readonly tagline: string;
  /** The club's logo, if it has one. Always a real PNG. */
  readonly logo: Buffer | undefined;
  /**
   * A short fingerprint of the logo's bytes, for the end of its address.
   * The logo is served with an hour's cache, so a club that uploads a new
   * one would otherwise see the old one for up to an hour; a new logo is a
   * new fingerprint, so a new address nothing has cached. Undefined with
   * no logo.
   */
  readonly logoVersion: string | undefined;
  /**
   * A colour (#RRGGBB) to put behind the logo, when the laptop worked out
   * that the logo would all but vanish on this page's background - a black
   * logo on a black page. Undefined for no plate, which is also what an
   * older app that never sends one gets.
   */
  readonly logoPlate: string | undefined;
  /**
   * The same idea for the home-screen icon. Since 30 Oct 2026 (relay
   * branding, Asher: "synthwave colors for the icon regardless of logo")
   * that is HeatCaller's sun for every club, logo or not, so this is the
   * sun's own version (sun.ts) - one address for every club, changed only
   * when the drawing is.
   */
  readonly iconVersion: string;
  /** Whether online check-in is open, which decides whether its tab shows. */
  readonly checkIn: boolean;
  /** Whether a driver may email themselves a sign-in link on Me (14 Nov 2026, signinmail.ts). */
  readonly emailLink: boolean;
  /**
   * Whether drivers may pick their own look for the club's pages on their
   * phones (15 Nov 2026, driver themes; Asher: "club driver can change the
   * look should be on by default"). On unless the club's site document says
   * false, so a club on an older app has it too.
   */
  readonly driverThemes: boolean;
  /**
   * The club's half of the /clubs listing (19 Nov 2026, cross-club index
   * phase 1): "List this club in the HeatCaller directory" on the laptop's
   * Club Website page. Only a literal true; the relay admin's
   * Tenant.listInDirectory is needed as well. See directory.ts.
   */
  readonly directory?: boolean;
  /**
   * Links to the club's own pages elsewhere - Facebook, Instagram, its
   * website - in the order the laptop sent them. Only http(s) addresses
   * with a platform this file knows reach here; see socialFrom.
   */
  readonly social: readonly SocialLink[];
  /**
   * The "New here?" card on Home (30 Oct 2026, relay navigation round 6,
   * asked for by Asher): where the club races, when, and how to join, as
   * typed in the app. Plain text, each line tidied like the tagline; all
   * three empty (an older app, or a club that left them blank) is no card.
   */
  readonly newcomer: Newcomer;
  /**
   * The first-night guide in the club's own words (4 Nov 2026, Asher: "the
   * guide should be able to be edited on laptop under the website nav"):
   * a heading and its words per part, plain text, tidied and capped like
   * the tagline. Empty - an older app, or a club that kept the standard
   * guide - is HeatCaller's own guide.
   */
  readonly guide: readonly GuidePart[];
  /**
   * Club updates (4 Nov 2026, Asher: "a separate optional card to push to
   * the relay for Club Updates. same motion"): news the club sends from
   * the laptop - a heading and words per item, tidied like the guide - for
   * a card on every driver's night page. Empty is no card.
   */
  readonly updates: readonly GuidePart[];
}

/** One part of the first-night guide: a short heading (may be empty) and its words. */
export interface GuidePart {
  readonly heading: string;
  readonly words: string;
}

/** The three lines of Home's "New here?" card. Empty strings for any left blank. */
export interface Newcomer {
  readonly where: string;
  readonly when: string;
  readonly join: string;
}

/** One of the club's pages elsewhere. */
export interface SocialLink {
  /** One of SOCIAL_LABELS' keys. */
  readonly kind: string;
  /** The label shown on the page - chosen here, never sent by the laptop. */
  readonly label: string;
  /** An absolute http(s) address, normalised by the URL parser. */
  readonly url: string;
}

/**
 * The platforms a club can link to, and what each is called on the page.
 * A fixed list rather than whatever label the laptop sends, so the words
 * on the page are always ones this file chose.
 */
export const SOCIAL_LABELS: Readonly<Record<string, string>> = Object.freeze({
  website: "Website",
  facebook: "Facebook",
  instagram: "Instagram",
  youtube: "YouTube",
  tiktok: "TikTok",
  x: "X",
  discord: "Discord",
  whatsapp: "WhatsApp",
});

const MAX_SOCIAL_URL = 300;

/** The document's colour names, and the page variables they set. */
const COLOURS: Readonly<Record<string, string>> = Object.freeze({
  bg: "--bg",
  surface: "--surface",
  rowAlt: "--row-alt",
  primary: "--primary",
  // Text drawn on the primary colour - white on most themes, near-black
  // on a yellow one, where white would all but disappear.
  onPrimary: "--on-primary",
  accent: "--accent",
  text: "--text",
  muted: "--muted",
  border: "--border",
  good: "--good",
  bad: "--bad",
});

const HEX = /^#[0-9a-fA-F]{6}$/;
const PNG_SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
const MAX_LOGO_BYTES = 400 * 1024;
const MAX_TAGLINE = 160;
const MAX_NEWCOMER_LINE = 160;
const MAX_NEWCOMER_JOIN = 300;
const MAX_GUIDE_PARTS = 20;
const MAX_GUIDE_HEADING = 60;
const MAX_GUIDE_WORDS = 600;
const MAX_UPDATES = 10;
const MAX_UPDATE_WORDS = 1000;

const NO_NEWCOMER: Newcomer = Object.freeze({ where: "", when: "", join: "" });

export const DEFAULT_THEME_COLOR = "#0F0722";

export const DEFAULT_LOOK: Look = Object.freeze({
  light: false,
  vars: Object.freeze({}),
  themeColor: DEFAULT_THEME_COLOR,
  tagline: "",
  logo: undefined,
  logoVersion: undefined,
  logoPlate: undefined,
  iconVersion: SUN_ICON_VERSION,
  checkIn: false,
  emailLink: false,
  driverThemes: true,
  listening: false,
  notes: false,
  vote: false,
  social: Object.freeze([]),
  newcomer: NO_NEWCOMER,
  guide: Object.freeze([]),
  updates: Object.freeze([]),
});

function record(value: unknown): Record<string, unknown> | undefined {
  return typeof value === "object" && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : undefined;
}

/** A PNG from a data: URI, or undefined for anything that is not exactly that. */
export function pngFromDataUri(value: unknown): Buffer | undefined {
  if (typeof value !== "string") {
    return undefined;
  }

  const match = /^data:image\/png;base64,([A-Za-z0-9+/]+={0,2})$/.exec(value);

  if (match === null) {
    return undefined;
  }

  const bytes = Buffer.from(match[1] as string, "base64");

  if (bytes.length > MAX_LOGO_BYTES || bytes.length < PNG_SIGNATURE.length) {
    return undefined;
  }

  return bytes.subarray(0, PNG_SIGNATURE.length).equals(PNG_SIGNATURE) ? bytes : undefined;
}

export function lookFrom(site: StoredDocument | undefined, checkin: StoredDocument | undefined): Look {
  const body = record(site?.body);
  const theme = record(body?.["theme"]);
  const colours = record(theme?.["colors"]);

  const vars: Record<string, string> = {};

  if (colours !== undefined) {
    for (const [key, variable] of Object.entries(COLOURS)) {
      const value = colours[key];

      if (typeof value === "string" && HEX.test(value)) {
        vars[variable] = value.toUpperCase();
      }
    }
  }

  // Control characters out, whitespace collapsed, and a hard length cap:
  // it is one line under a heading, and the page escapes it anyway.
  const tagline = oneLine(body?.["tagline"], MAX_TAGLINE);

  const logo = pngFromDataUri(body?.["logo"]);

  // Checked exactly like a theme colour: it goes into the page's <style>.
  // Anything else - a name, rgb(), a missing #, something that is not a
  // string at all - is no plate, not an error. Only meaningful with a logo.
  const rawPlate = body?.["logoPlate"];
  const logoPlate = logo !== undefined && typeof rawPlate === "string" && HEX.test(rawPlate)
    ? rawPlate.toUpperCase()
    : undefined;

  const light = theme?.["light"] === true;
  const rawClock = body?.["clock24"];
  const clock24 = typeof rawClock === "boolean" ? rawClock : undefined;
  const timeZone = timeZoneFrom(body?.["timeZone"]);
  const rawShort = typeof body?.["shortName"] === "string" ? (body["shortName"] as string).replace(/[\u0000-\u001f\u007f]/g, "").trim() : "";
  const shortName = rawShort.length >= 1 && rawShort.length <= 12 ? rawShort : undefined;
  const rawAlias = typeof body?.["alias"] === "string" ? (body["alias"] as string).trim().toLowerCase() : "";
  const alias = /^[a-z0-9]+(-[a-z0-9]+)*$/.test(rawAlias) && rawAlias.length >= 3 && rawAlias.length <= 32 ? rawAlias : undefined;
  const logoVersion = logo === undefined ? undefined : logoVersionOf(logo);

  return {
    light,
    clock24,
    timeZone,
    shortName,
    alias,
    vars,
    themeColor: vars["--bg"] ?? DEFAULT_THEME_COLOR,
    tagline,
    logo,
    logoVersion,
    logoPlate,
    iconVersion: SUN_ICON_VERSION,
    checkIn: record(checkin?.body)?.["enabled"] === true,
    emailLink: record(checkin?.body)?.["enabled"] === true && record(checkin?.body)?.["emailLink"] === true,
    driverThemes: body?.["driverThemes"] !== false,
    directory: body?.["directory"] === true,

    // Merged in by Relay.lookFor from a document of its own, the way
    // checkIn's own flag is above. False here is the honest default for a
    // look built without it.
    listening: false,
    notes: false,
    vote: false,
    social: socialFrom(body?.["social"]),
    newcomer: newcomerFrom(body?.["newcomer"]),
    guide: guideFrom(body?.["guide"]),
    updates: updatesFrom(body?.["updates"]),
  };
}

/** A line of a club's own text: control characters out, whitespace collapsed, capped. Anything not a string is empty. */
function oneLine(value: unknown, max: number): string {
  return typeof value !== "string"
    ? ""
    : value
      .replace(/[\u0000-\u001f\u007f]/g, " ")
      .replace(/\s+/g, " ")
      .trim()
      .slice(0, max);
}

/**
 * A part's words with their own line breaks (7 Nov 2026, Asher: "honor the
 * line spacing for both the newcomer card and club notes that's on the PC
 * app editor"): each line as oneLine tidies it, blank lines dropped - a
 * blank line is where the app starts a new part - and the whole capped.
 * Pages draw it as text, so a break is only ever a break.
 */
function lines(value: unknown, max: number): string {
  return typeof value !== "string"
    ? ""
    : value
      .replace(/\r\n?/g, "\n")
      .split("\n")
      .map((line) => oneLine(line, max))
      .filter((line) => line !== "")
      .join("\n")
      .slice(0, max)
      .trim();
}

/** The site document's "newcomer" object of { where, when, join }, each a line of plain text. */
export function newcomerFrom(value: unknown): Newcomer {
  const raw = record(value);

  if (raw === undefined) {
    return NO_NEWCOMER;
  }

  const newcomer = {
    where: oneLine(raw["where"], MAX_NEWCOMER_LINE),
    when: oneLine(raw["when"], MAX_NEWCOMER_LINE),
    join: oneLine(raw["join"], MAX_NEWCOMER_JOIN),
  };

  return newcomer.where === "" && newcomer.when === "" && newcomer.join === "" ? NO_NEWCOMER : newcomer;
}

/**
 * The site document's "guide" array of { heading, words }. Each part is
 * one line of plain text, tidied and capped; a part with no words is
 * dropped, and past twenty parts the rest are. Anything that is not an
 * array is no guide of the club's own.
 */
export function guideFrom(value: unknown): readonly GuidePart[] {
  return partsFrom(value, MAX_GUIDE_PARTS, MAX_GUIDE_WORDS);
}

/** The site document's "updates" array of { heading, words }, read exactly as the guide is, with room for ten longer items. */
export function updatesFrom(value: unknown): readonly GuidePart[] {
  return partsFrom(value, MAX_UPDATES, MAX_UPDATE_WORDS);
}

function partsFrom(value: unknown, most: number, maxWords: number): readonly GuidePart[] {
  if (!Array.isArray(value)) {
    return [];
  }

  const parts: GuidePart[] = [];

  for (const item of value) {
    const raw = record(item);
    const words = lines(raw?.["words"], maxWords);

    if (words !== "") {
      parts.push({ heading: oneLine(raw?.["heading"], MAX_GUIDE_HEADING), words });
    }

    if (parts.length === most) {
      break;
    }
  }

  return parts;
}

/**
 * The club's links, from the site document's "social" array of
 * { kind, url }. Kept as narrow as the rest of this file: a kind must be
 * one SOCIAL_LABELS knows, the address must parse as absolute http or
 * https (no javascript:, no data:, nothing relative), carry no user name
 * or password, and stay short. One link per kind - the first wins - so a
 * doubled entry cannot fill the footer. Anything else is dropped quietly;
 * an older app that never sends the array gets no links.
 */
export function socialFrom(value: unknown): readonly SocialLink[] {
  if (!Array.isArray(value)) {
    return [];
  }

  const links: SocialLink[] = [];
  const seen = new Set<string>();
  const seenUrls = new Set<string>();

  for (const item of value) {
    const entry = record(item);
    const kind = entry?.["kind"];
    const raw = entry?.["url"];

    if (typeof kind !== "string" || typeof raw !== "string" || seen.has(kind)) {
      continue;
    }

    if (!Object.prototype.hasOwnProperty.call(SOCIAL_LABELS, kind)) {
      continue;
    }

    const url = safeLink(raw);

    if (url === undefined) {
      continue;
    }

    // EACH LINK GOES WHERE IT SAYS, ONCE (14 Nov 2026, driver phone plan
    // item 23). Upstate Mini Z had six icons - Instagram, YouTube, TikTok,
    // X, Discord and Facebook - all to the same Facebook page, so a driver
    // tapping "Instagram" got Facebook and a screen reader read the same
    // link six times. A link whose address isn't its service's own is
    // dropped, and an address already shown is shown once.
    if (!socialHostFits(kind, url) || seenUrls.has(url.toLowerCase())) {
      continue;
    }

    seenUrls.add(url.toLowerCase());
    seen.add(kind);
    links.push({ kind, label: SOCIAL_LABELS[kind] as string, url });
  }

  return links;
}

/** A real IANA time zone this runtime knows, or undefined. */
export function timeZoneFrom(value: unknown): string | undefined {
  if (typeof value !== "string" || value.length < 3 || value.length > 64 || !/^[A-Za-z0-9_+\-/]+$/.test(value)) return undefined;
  try {
    new Intl.DateTimeFormat("en-US", { timeZone: value });
    return value;
  } catch {
    return undefined;
  }
}

/** Each service's own hosts (and their subdomains); a website may be anywhere. */
const SOCIAL_HOSTS: Readonly<Record<string, readonly string[]>> = Object.freeze({
  facebook: ["facebook.com", "fb.com", "fb.me"],
  instagram: ["instagram.com", "instagr.am"],
  youtube: ["youtube.com", "youtu.be"],
  tiktok: ["tiktok.com"],
  x: ["x.com", "twitter.com"],
  discord: ["discord.gg", "discord.com", "discordapp.com"],
  whatsapp: ["wa.me", "whatsapp.com"],
});

/** Whether a link's address is on its service's own host. */
export function socialHostFits(kind: string, url: string): boolean {
  const hosts = SOCIAL_HOSTS[kind];
  if (hosts === undefined) return true;
  let host = "";
  try { host = new URL(url).hostname.toLowerCase(); } catch { return false; }
  return hosts.some((h) => host === h || host.endsWith(`.${h}`));
}

/** An absolute http(s) address fit for an href, or undefined. */
export function safeLink(raw: string): string | undefined {
  const trimmed = raw.trim();

  if (trimmed.length === 0 || trimmed.length > MAX_SOCIAL_URL || /[\u0000-\u001f\u007f\s]/.test(trimmed)) {
    return undefined;
  }

  let parsed: URL;

  try {
    parsed = new URL(trimmed);
  } catch {
    return undefined;
  }

  if ((parsed.protocol !== "https:" && parsed.protocol !== "http:") || parsed.username !== "" || parsed.password !== "") {
    return undefined;
  }

  const url = parsed.href;
  return url.length > MAX_SOCIAL_URL ? undefined : url;
}

/**
 * Twelve hex digits of the logo's SHA-256. Worked out once per site
 * document (the look is held until the club publishes again), and only
 * ever hex, so it is safe in an address without escaping.
 */
export function logoVersionOf(logo: Buffer): string {
  return createHash("sha256").update(logo).digest("hex").slice(0, 12);
}
