/**
 * A DRIVER ASKING THE DESK FOR SOMETHING, FROM THEIR OWN PHONE (22 Oct
 * 2026, driver experience).
 *
 * Three kinds, all from the driver's night page and all only REQUESTS:
 *
 *   details   their announcer name, pronouns, an "about" line for the
 *             commentary, their email, whether to email them their
 *             night, and three online-privacy ticks;
 *   pack      which of their battery packs they are running now;
 *   query     "something's wrong with this result" on one race.
 *
 * NOTHING HERE CHANGES ANYTHING. The relay holds each ask until the laptop
 * collects it (driver/inbox, then driver/collected - the notes shape), and
 * the desk approves or declines every one. The answer goes back to the
 * phone inside the driver's private live document, not through any route
 * here.
 *
 * This file is the pure half: the names, the limits and the checking of
 * what a phone sent. The routes are in server.ts (driverAsk and friends).
 */

/** Where the relay holds asks. Not a channel - nobody publishes to it - like the notes. */
export const DRIVER_ASKS_CHANNEL = "driverasks";

/** The flag the laptop sets to say its club takes these at all (driver/open). */
export const DRIVER_ASKS_OPEN_DOC = "driveraskopen";

/** One ask's whole request body. The biggest real one is a details change, a few hundred bytes. */
export const DRIVER_ASK_MAX_BODY = 2 * 1024;

/** And what one ask may come to as stored. */
export const DRIVER_ASK_MAX_STORED = 2 * 1024;

/** Asks held for one badge at once - the desk answers in minutes, so five waiting is a lot. */
export const DRIVER_ASKS_PER_DRIVER = 5;

/** Asks held for one club at once. A ceiling on what phones can leave in a club's folder. */
export const DRIVER_ASKS_PER_CLUB = 200;

/** The ids in one driver/collected batch - room for the whole held set and no more. */
export const DRIVER_ASKS_COLLECT_MAX_BODY = 16 * 1024;

/** Text limits, matching the laptop's own (Pronouns.MaxChars, DriverProfile.AboutLineMaxChars). */
export const ASK_MAX_ANNOUNCER = 40;
export const ASK_MAX_PRONOUNS = 24;
export const ASK_MAX_ABOUT = 120;
export const ASK_MAX_EMAIL = 120;
export const ASK_MAX_QUERY = 300;

/** A pack id or a race (session) id: a Guid "N", as the laptop makes them. */
export const ASK_OBJECT_ID = /^[0-9a-f]{32}$/;

/** Plain enough to be an address and nothing else - the desk mails a notice to the OLD one before it changes. */
const EMAIL = /^[^\s@<>()",;:\\[\]]+@[^\s@<>()",;:\\[\]]+\.[^\s@<>()",;:\\[\]]+$/;

export type DriverAskKind = "details" | "pack" | "query";

export const DRIVER_ASK_KINDS: readonly DriverAskKind[] = ["details", "pack", "query"];

/** Which proof came with the ask: the personal PIN's pass, or the badge code alone. */
export type DriverAskProof = "pin" | "badge";

/** What is stored for one ask - and exactly what driver/inbox hands the laptop, beside its id. */
export interface DriverAskBody {
  /** The badge id. */
  readonly b: string;
  readonly k: DriverAskKind;
  readonly v: DriverAskProof;
  readonly f: Readonly<Record<string, string | boolean>>;
  /** When the relay took it, ISO 8601. */
  readonly at: string;
}

/** What the phone is told to quote at the desk: the first six hex of the ask's id, upper case. */
export function askRefOf(id: string): string {
  return id.slice(0, 6).toUpperCase();
}

/**
 * One line of text: control characters out, runs of white space one space,
 * trimmed, no longer than max - server.ts's cleanText, repeated here so this
 * file stays pure and testable on its own.
 */
export function cleanAskText(value: unknown, max: number): string {
  if (typeof value !== "string") {
    return "";
  }

  return value.replace(/[\u0000-\u001f\u007f]/g, " ").replace(/\s+/g, " ").trim().slice(0, max);
}

export type AskCheck =
  | { readonly ok: true; readonly f: Readonly<Record<string, string | boolean>> }
  | { readonly ok: false; readonly status: number; readonly error: string };

const refuse = (status: number, error: string): AskCheck => ({ ok: false, status, error });

/**
 * What a phone sent as `f`, checked for its kind - or the status and words
 * to refuse it with. Fields this does not know are dropped, never passed on.
 *
 * `withPin` is whether the ask came with a pass that matched this driver's
 * own personal PIN. Only that can change an email address: the badge code
 * is a bearer credential printed on a lanyard, and an email is where the
 * desk sends the PIN itself.
 */
export function askFieldsFrom(kind: string, f: unknown, withPin: boolean): AskCheck {
  if (typeof f !== "object" || f === null || Array.isArray(f)) {
    return refuse(400, "That request could not be read.");
  }

  const input = f as Record<string, unknown>;

  if (kind === "pack") {
    const pack = typeof input["pack"] === "string" ? input["pack"].trim().toLowerCase() : "";

    if (pack !== "none" && !ASK_OBJECT_ID.test(pack)) {
      return refuse(400, "Pick one of your packs.");
    }

    return { ok: true, f: { pack } };
  }

  if (kind === "query") {
    const race = typeof input["race"] === "string" ? input["race"].trim().toLowerCase() : "";
    const m = cleanAskText(input["m"], ASK_MAX_QUERY + 1);

    if (!ASK_OBJECT_ID.test(race)) {
      return refuse(400, "Which race is it about?");
    }

    if (m.length === 0) {
      return refuse(400, "Say what looks wrong.");
    }

    if (m.length > ASK_MAX_QUERY) {
      return refuse(400, `Keep it under ${ASK_MAX_QUERY} characters.`);
    }

    return { ok: true, f: { race, m } };
  }

  if (kind !== "details") {
    return refuse(400, "That request could not be read.");
  }

  const out: Record<string, string | boolean> = {};

  // Text fields: kept when sent as a string, even an empty one - an empty
  // string is the driver asking to clear it, which the desk still approves.
  const texts: readonly [string, number][] = [
    ["announcer", ASK_MAX_ANNOUNCER],
    ["pronouns", ASK_MAX_PRONOUNS],
    ["about", ASK_MAX_ABOUT],
  ];

  for (const [name, max] of texts) {
    const value = input[name];

    if (value === undefined) {
      continue;
    }

    if (typeof value !== "string") {
      return refuse(400, "That request could not be read.");
    }

    const cleaned = cleanAskText(value, max + 1);

    if (cleaned.length > max) {
      return refuse(400, `Keep that under ${max} characters.`);
    }

    out[name] = cleaned;
  }

  if (input["email"] !== undefined) {
    if (!withPin) {
      return refuse(403, "Changing your email needs your personal PIN - ask at the desk.");
    }

    if (typeof input["email"] !== "string") {
      return refuse(400, "That request could not be read.");
    }

    const email = cleanAskText(input["email"], ASK_MAX_EMAIL + 1);

    if (email.length > ASK_MAX_EMAIL || (email.length > 0 && !EMAIL.test(email))) {
      return refuse(400, "That email address does not look right.");
    }

    out["email"] = email;
  }

  if (input["emailMyNight"] !== undefined) {
    if (typeof input["emailMyNight"] !== "boolean") {
      return refuse(400, "That request could not be read.");
    }

    out["emailMyNight"] = input["emailMyNight"];
  }

  // PRIVACY CAN ONLY BE TURNED ON FROM A PHONE. Turning one off again is a
  // desk-only edit: a badge code passed around a group chat must not be
  // able to put a child's photo back online.
  for (const name of ["noPhoto", "noVideo", "initials"]) {
    const value = input[name];

    if (value === undefined) {
      continue;
    }

    if (value !== true) {
      return refuse(400, "Ask at the desk to undo this.");
    }

    out[name] = true;
  }

  if (Object.keys(out).length === 0) {
    return refuse(400, "There is nothing to change.");
  }

  // Recorded, not judged: "I'm a parent or guardian making this change".
  if (input["guardian"] === true) {
    out["guardian"] = true;
  }

  return { ok: true, f: out };
}

/** A stored ask read back, field by field - or undefined for anything that is not one. */
export function asDriverAsk(value: unknown): DriverAskBody | undefined {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return undefined;
  }

  const v = value as Record<string, unknown>;
  const b = v["b"];
  const k = v["k"];
  const proof = v["v"];
  const f = v["f"];
  const at = v["at"];

  if (
    typeof b !== "string" || !/^[0-9a-f]{20}$/.test(b)
    || typeof k !== "string" || !(DRIVER_ASK_KINDS as readonly string[]).includes(k)
    || (proof !== "pin" && proof !== "badge")
    || typeof f !== "object" || f === null || Array.isArray(f)
    || typeof at !== "string"
  ) {
    return undefined;
  }

  const fields: Record<string, string | boolean> = {};

  for (const [name, field] of Object.entries(f as Record<string, unknown>)) {
    if (typeof field === "string" || typeof field === "boolean") {
      fields[name] = field;
    }
  }

  return { b, k: k as DriverAskKind, v: proof, f: fields, at };
}
