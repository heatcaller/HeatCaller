import { createHash } from "node:crypto";
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
   * The same idea for the home-screen icon, which is the logo when there is
   * one (then this is logoVersion) and otherwise a chequered flag drawn in
   * the look's colours (then a fingerprint of exactly the colours that
   * decide its pixels - see flagColours). A new logo or a recoloured flag
   * is a new icon address, so no phone or link preview keeps the old one.
   */
  readonly iconVersion: string;
  /** Whether online check-in is open, which decides whether its tab shows. */
  readonly checkIn: boolean;
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
  iconVersion: flagVersionOf(flagColours({ light: false, vars: {} })),
  checkIn: false,
  listening: false,
  social: Object.freeze([]),
  newcomer: NO_NEWCOMER,
});

function record(value: unknown): Record<string, unknown> | undefined {
  return typeof value === "object" && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : undefined;
}

/** A PNG's pixel width, read from its header. The signature has already been checked by whoever holds one. */
export function pngWidth(png: Buffer): number {
  return png.length >= 24 ? png.readUInt32BE(16) : 0;
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
  const logoVersion = logo === undefined ? undefined : logoVersionOf(logo);

  return {
    light,
    vars,
    themeColor: vars["--bg"] ?? DEFAULT_THEME_COLOR,
    tagline,
    logo,
    logoVersion,
    logoPlate,
    iconVersion: logoVersion ?? flagVersionOf(flagColours({ light, vars })),
    checkIn: record(checkin?.body)?.["enabled"] === true,

    // Merged in by Relay.lookFor from a document of its own, the way
    // checkIn's own flag is above. False here is the honest default for a
    // look built without it.
    listening: false,
    social: socialFrom(body?.["social"]),
    newcomer: newcomerFrom(body?.["newcomer"]),
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

    seen.add(kind);
    links.push({ kind, label: SOCIAL_LABELS[kind] as string, url });
  }

  return links;
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

/**
 * The two colours the chequered-flag icon is drawn in, for a club with no
 * logo: the flag's background is the primary colour, and the dark squares
 * come from the darkest colour the look has - its background on a dark
 * look, its text on a light one. Kept here, next to the version, so the
 * icon that is drawn and the address it is given can never disagree.
 * Literal fallbacks (not the constants above) because DEFAULT_LOOK calls
 * this while the module is still loading.
 */
export function flagColours(look: Pick<Look, "light" | "vars">): readonly [background: string, dark: string] {
  const primary = look.vars["--primary"] ?? "#FF2E9A";
  const dark = look.light ? (look.vars["--text"] ?? "#11151C") : (look.vars["--bg"] ?? "#0F0722");
  return [primary, dark];
}

/** Twelve hex digits fingerprinting the flag icon's inputs: its size and its two colours. */
export function flagVersionOf([background, dark]: readonly [string, string]): string {
  return createHash("sha256").update(`flag|512|${background}|${dark}`).digest("hex").slice(0, 12);
}
