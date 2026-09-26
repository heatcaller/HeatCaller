import { isValidItemId } from "./store.js";
import type { StoredDocument } from "./types.js";

/**
 * What a link to one session is allowed to say about it, for the card a
 * chat app draws when somebody shares it.
 *
 * This is the SECOND place the relay reads a club's payload rather than
 * handing it back untouched, and it is kept as narrow as the first - see
 * site.ts, which takes hex colors, a line of text and a PNG and ignores
 * the rest of the document. Same shape here: a handful of named fields,
 * each checked, everything else ignored, a malformed document worth
 * nothing at all rather than a broken card. Nothing in this file throws.
 *
 * It reads the INDEX, never a result. The index is the club's own list of
 * what was run - a label, a track, a day, how many raced - which is text
 * the club already chose for its own public pages. A result is the thing
 * the relay does not understand and must not start to: the columns in one
 * are the desktop app's business, and a preview built by parsing results
 * would drag every column a club adds into a relay deploy.
 *
 * WHY THE RELAY HAS TO DO THIS AT ALL. A preview card is built by
 * somebody else's server - WhatsApp's, Slack's - fetching the link and
 * reading its <head>. It does not run the page's script, so nothing the
 * browser draws can reach it, and a fragment never leaves the browser in
 * the first place. Either the tags are in the HTML the relay serves, or
 * every shared result looks like every other one.
 */
/**
 * One line of the podium, as the index publishes it.
 *
 * Read for one reason: the card the relay draws (card.ts) needs
 * something to put on it, and the relay is not allowed to open a result
 * to find out who won. So the app publishes its own three names in the
 * index, alongside the winner it already published, and this is the
 * narrowest possible reader for them.
 */
export interface PodiumPlace {
  /** 1, 2 or 3. Never anything else - an index that says otherwise loses the row. */
  readonly p: number;
  /** The driver, as the club publishes names. Never empty; a row without one is dropped. */
  readonly n: string;
  /** Whole laps, or zero for an index that did not say. */
  readonly laps: number;
}

export interface SessionPreview {
  /** The session's own id, already known to be a usable one. */
  readonly id: string;
  /** What the club called it. Empty when the club called it nothing. */
  readonly label: string;
  readonly track: string;
  /** The championship it counted for, or empty for a session raced outside one. */
  readonly season: string;
  /** yyyy-mm-dd, and a real day. Empty when the document did not say. */
  readonly day: string;
  /** HH:mm in the club's own time, or empty. */
  readonly at: string;
  /** How many raced. Zero means "not said", which reads the same as "nobody did". */
  readonly drivers: number;
  /**
   * The winner's name as the club publishes names - shortened if that is
   * what the club chose. Empty for an index from an app that does not
   * send it yet, and then the card simply has no winner on it. It is
   * never worked out from anywhere else: a result is not read here.
   */
  readonly won: string;
  /**
   * First, second and third, in that order, for the picture on the card.
   * Empty for an index from an app that does not send it - and then the
   * card falls back to the club, the session and the day, which is still
   * a card worth looking at.
   */
  readonly top: readonly PodiumPlace[];
}

/** The tags a page puts in its head for one session. Plain text; the page escapes it. */
export interface PagePreview {
  readonly title: string;
  readonly description: string;
  /** The canonical address of this session, for og:url. */
  readonly path: string;
  /**
   * The drawn card for this session (card.ts), as a path with its own
   * version on it. Its presence is what turns the chat app's small
   * square thumbnail into a banner - see shell() in pages.ts - so it is
   * absent for a page that has no card of its own to show.
   *
   * Versioned because a link preview is fetched once and then kept for
   * days by whoever fetched it: a club that corrects a result or changes
   * its colours needs the next share to be a DIFFERENT address, or the
   * old picture is what everybody keeps seeing.
   */
  readonly image?: string;
}

const MAX_TEXT = 80;
const MAX_DRIVERS = 9999;
const MAX_DESCRIPTION = 200;
/** A podium is three. An index that sends thirty is not describing a podium. */
const MAX_PODIUM = 3;
/** Nobody completes ten thousand laps in a club heat, and the card has room for four digits. */
const MAX_LAPS = 9999;

const DAY = /^(\d{4})-(\d{2})-(\d{2})$/;
const AT = /^([01][0-9]|2[0-3]):[0-5][0-9]$/;

const WEEKDAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
const MONTHS = [
  "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December",
];

function record(value: unknown): Record<string, unknown> | undefined {
  return typeof value === "object" && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : undefined;
}

/**
 * Plain text a card may carry: control characters out, whitespace
 * collapsed, and a hard cap. Angle brackets and quotes are left exactly
 * as the club wrote them - stripping them would quietly rename a club's
 * own session - and every one of these strings goes through the page's
 * escaping on its way into an attribute, which is where they are made
 * harmless. See the test that shares a session called "><script>.
 */
function text(value: unknown): string {
  return typeof value !== "string"
    ? ""
    : cut(
        value
          .replace(/[\u0000-\u001f\u007f]/g, " ")
          .replace(BIDI, "")
          .replace(/\s+/g, " ")
          .trim(),
        MAX_TEXT,
      );
}

/**
 * The invisible characters that reorder text rather than add to it:
 * the embeddings and overrides (U+202A-U+202E), the isolates
 * (U+2066-U+2069) and the two marks (U+200E, U+200F).
 *
 * Taken out rather than kept, because a card's text is a club's label or
 * a driver's name set into somebody else's chat app, beside other text
 * the club did not write. A right-to-left override in a session label
 * would run on into whatever the app puts after it - and "won by" a name
 * that reads backwards is the old trick for making a line say something
 * other than what it contains. A name in Hebrew or Arabic needs none of
 * these to be drawn right; its letters carry their own direction.
 */
const BIDI = /[\u200e\u200f\u202a-\u202e\u2066-\u2069]/g;

/**
 * The first `max` UTF-16 units of a string, one fewer when the cut would
 * land between the two halves of a surrogate pair. A plain slice there
 * leaves half an emoji on the end, which is not a character at all:
 * drawn as a box, and in an attribute an encoding error some apps refuse
 * the whole tag for.
 */
function cut(value: string, max: number): string {
  if (value.length <= max) {
    return value;
  }

  const last = value.charCodeAt(max - 1);

  return value.slice(0, last >= 0xd800 && last <= 0xdbff ? max - 1 : max);
}

/** A calendar day, and a real one: "2026-02-30" is neither. */
function day(value: unknown): string {
  const match = typeof value === "string" ? DAY.exec(value) : null;

  if (match === null) {
    return "";
  }

  const year = Number(match[1]);
  const month = Number(match[2]);
  const date = Number(match[3]);
  const when = new Date(Date.UTC(year, month - 1, date));

  return when.getUTCFullYear() === year && when.getUTCMonth() === month - 1 && when.getUTCDate() === date
    ? (value as string)
    : "";
}

function drivers(value: unknown): number {
  return typeof value === "number" && Number.isInteger(value) && value >= 0 ? Math.min(value, MAX_DRIVERS) : 0;
}

/** A whole lap count, capped. Anything that is not one counts as none, which the card draws as no number at all. */
function laps(value: unknown): number {
  return typeof value === "number" && Number.isInteger(value) && value >= 0 ? Math.min(value, MAX_LAPS) : 0;
}

/**
 * A finishing position, or zero for anything that is not one.
 *
 * Refused rather than capped, unlike every other number here. Capping a
 * lap count is harmless - nine thousand laps and ten thousand laps are
 * both "a number too big to be real" - but capping a POSITION would file
 * a fourth-place driver as third, which is not a clamped value, it is a
 * wrong result with somebody's name on it.
 */
function place(value: unknown): number {
  return typeof value === "number" && Number.isInteger(value) && value >= 1 && value <= MAX_PODIUM ? value : 0;
}

/**
 * The podium, read with the same suspicion as everything else here.
 *
 * At most three rows; each one needs a place of 1, 2 or 3 and a name
 * that survives text(), and anything else is dropped rather than drawn
 * as a gap. Two rows claiming the same place keep the first - the card
 * would otherwise show two silvers - and the rows are sorted, because
 * "first, second, third" is the order the card draws and not something
 * it should have to trust an index to have got right.
 *
 * Laps are allowed to be missing: a session timed by laps has them and
 * one classified by hand may not, and a card with no number beside a
 * name is fine where a card with a wrong one is not.
 */
function podium(value: unknown): PodiumPlace[] {
  if (!Array.isArray(value)) {
    return [];
  }

  const out: PodiumPlace[] = [];
  const taken = new Set<number>();

  for (const entry of value) {
    if (out.length >= MAX_PODIUM) {
      break;
    }

    const line = record(entry);
    const position = place(line?.["p"]);
    const name = text(line?.["n"]);

    if (position === 0 || name === "" || taken.has(position)) {
      continue;
    }

    taken.add(position);
    out.push({ p: position, n: name, laps: laps(line?.["laps"]) });
  }

  return out.sort((a, b) => a.p - b.p);
}

/** One entry of the index's sessions array, or undefined for anything that is not one. */
function sessionFrom(entry: unknown): SessionPreview | undefined {
  const line = record(entry);
  const id = line?.["id"];

  // No usable id, no preview: the id is what the link names, and one this
  // relay would never store is one no link of its own could point at.
  if (typeof id !== "string" || !isValidItemId(id)) {
    return undefined;
  }

  return {
    id,
    label: text(line?.["label"]),
    track: text(line?.["track"]),
    season: text(line?.["season"]),
    day: day(line?.["day"]),
    at: typeof line?.["at"] === "string" && AT.test(line["at"] as string) ? (line["at"] as string) : "",
    drivers: drivers(line?.["drivers"]),
    won: text(line?.["won"]),
    top: podium(line?.["top"]),
  };
}

/**
 * Every session one year's index names, up to `limit`.
 *
 * The relay holds these rather than the document they came from: a club's
 * links are read by crawlers in bursts, and validating once per publish
 * beats validating once per link. The single-session reader below is the
 * same walk for a caller that has one document and one id in hand.
 */
export function previewsIn(index: StoredDocument | undefined, limit: number): SessionPreview[] {
  const sessions = record(index?.body)?.["sessions"];
  const out: SessionPreview[] = [];

  if (!Array.isArray(sessions)) {
    return out;
  }

  for (const entry of sessions) {
    if (out.length >= limit) {
      break;
    }

    const session = sessionFrom(entry);

    if (session !== undefined) {
      out.push(session);
    }
  }

  return out;
}

/** One session out of one year's index, or undefined - a wrong id and a wrong document read the same. */
export function previewFor(index: StoredDocument | undefined, sessionId: string): SessionPreview | undefined {
  const sessions = record(index?.body)?.["sessions"];

  if (!Array.isArray(sessions) || typeof sessionId !== "string" || sessionId === "") {
    return undefined;
  }

  for (const entry of sessions) {
    const session = sessionFrom(entry);

    if (session !== undefined && session.id === sessionId) {
      return session;
    }
  }

  return undefined;
}

/**
 * "Saturday, September 19, 2026".
 *
 * Written out here rather than left to toLocaleDateString, which on a
 * server answers in the machine's locale and not the reader's: a card
 * that says different things depending on which host drew it is worse
 * than one that says it in the same English as the rest of these pages.
 *
 * Exported because the card (card.ts) writes the same day in the same
 * words: a picture and the line of text beside it disagreeing about
 * which day a race was is the sort of thing nobody can unsee.
 */
export function wordyDay(iso: string): string {
  const match = DAY.exec(iso);

  if (match === null) {
    return "";
  }

  const when = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])));

  return `${WEEKDAYS[when.getUTCDay()]}, ${MONTHS[when.getUTCMonth()]} ${when.getUTCDate()}, ${when.getUTCFullYear()}`;
}

/**
 * The card itself: what it was called and whose it is, then when, where,
 * how many raced and who won. Everything the document did not say is
 * left out rather than written as "unknown" - a card with a gap in it
 * looks like a mistake by the club.
 *
 * Plain text, deliberately. It is the page's job to escape it into the
 * attributes it ends up in, and doing it in one place is what makes that
 * checkable.
 */
export function previewCard(session: SessionPreview, club: string, clubName: string, image?: string): PagePreview {
  const when = wordyDay(session.day);

  const parts = [
    when === "" ? "" : session.at === "" ? when : `${when} at ${session.at}`,
    session.track,
    session.season,
    session.drivers > 0 ? `${session.drivers} driver${session.drivers === 1 ? "" : "s"}` : "",
    session.won === "" ? "" : `won by ${session.won}`,
  ].filter((part) => part !== "");

  return {
    title: `${session.label === "" ? "Results" : session.label} - ${clubName}`,
    description: cut(parts.join(" · "), MAX_DESCRIPTION),
    path: `/c/${club}/results/${session.id}`,
    ...(image === undefined ? {} : { image }),
  };
}
