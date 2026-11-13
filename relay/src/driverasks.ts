/**
 * A DRIVER ASKING THE DESK FOR SOMETHING, FROM THEIR OWN PHONE (22 Oct
 * 2026, driver experience).
 *
 * From the driver's night page and their garage page:
 *
 *   details   their announcer name, pronouns, an "about" line for the
 *             commentary, their email, whether to email them their
 *             night, and three online-privacy ticks;
 *   query     "something's wrong with this result" on one race;
 *   pack      the battery pack in their car now, or the one they ran a
 *             race on;
 *   packedit  one of their own packs added, edited, retired or removed;
 *   car       one of their own cars, every field, photos, racing tonight;
 *   setup     a setup sheet, or the short five-part log.
 *
 * NOTHING HERE CHANGES ANYTHING. The relay holds each ask until the laptop
 * collects it (driver/inbox, then driver/collected - the notes shape). The
 * desk approves or declines details and queries; the laptop applies the
 * other four by itself (5 Nov 2026, DRIVER_WRITE_KINDS). The answer goes
 * back to the phone inside the driver's private live document, not through
 * any route here.
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

/**
 * A setup log entry from the driver's own night (4 Nov 2026, from the relay
 * roadmap Asher asked to have built): the setup sheet's five short parts and
 * a note, the laptop's legacy SetupEntry fields, which file straight into
 * the Garage's setup log and the technical lead's brief.
 */
export const ASK_SETUP_PARTS: readonly string[] = ["tires", "gearing", "springs", "ride", "motor"];
export const ASK_MAX_SETUP_PART = 80;
export const ASK_MAX_SETUP_NOTES = 300;

/** A pack id or a race (session) id: a Guid "N", as the laptop makes them. */
export const ASK_OBJECT_ID = /^[0-9a-f]{32}$/;

/*
 * THE DRIVER'S OWN GARAGE, FROM THE PHONE (5 Nov 2026, asked for by Asher:
 * "what they input on the relay should update 1:1 on the battery section of
 * the app ... build all of it"). See claude/garage-relay-contract.md.
 *
 * pack, packedit, car and setup are WRITES the laptop applies by itself -
 * no desk in between - so they carry their own, roomier limits (a driver
 * swapping packs between every round, logging a sheet and adding a car
 * would run through the desk kinds' six an hour in one evening) and, for a
 * driver who has a personal PIN, need it (server.ts driverAsk).
 *
 * "coming" joined them on 12 Nov 2026 (setup plan C1: "I'm coming to the
 * next race night, in these classes"). It is a write, not a desk ask,
 * because there is nothing for the desk to decide - a driver putting
 * themselves down for next week is the club's whole point in asking, and
 * the desk would only be clicking yes to every one. The laptop files it
 * on its entry list and says so in its answer ("You're down for Tue 18
 * Nov"); a second one for the same day replaces the first.
 *
 * "post", "swap" and "cover" joined them the same day (setup plan step 8):
 * a marshal's "I'm at my post", "ask someone to cover my post" and the
 * asked driver's yes or no. The laptop files them as they come - Race
 * Control takes the first, the second only asks - and a yes to covering
 * waits there for the desk while the club wants to OK swaps, so the relay
 * has nothing to tell apart: all three are writes, with the writes' room.
 */
export const DRIVER_WRITE_KINDS: readonly DriverAskKind[] = ["pack", "packedit", "car", "setup", "coming", "post", "swap", "cover"];

/** Whether a kind is one the laptop applies by itself. */
export function isWriteKind(kind: string): boolean {
  return (DRIVER_WRITE_KINDS as readonly string[]).includes(kind);
}

/** A setup sheet with every value filled is the biggest ask there is. */
export const DRIVER_ASK_SETUP_MAX_BODY = 12 * 1024;

/** Writes held for one badge at once, beside the desk kinds' five. */
export const DRIVER_WRITES_PER_DRIVER = 30;

/** Pack, car and sheet text limits, matching the laptop's (DriverAsk). */
export const ASK_MAX_PACK_LABEL = 40;
export const ASK_MAX_PACK_CHEM = 16;
export const ASK_MAX_NOTES = 300;
export const ASK_MAX_CAR_NAME = 60;
export const ASK_MAX_CAR_CLASS = 40;
export const ASK_MAX_CAR_BODY = 60;
export const ASK_MAX_CAR_NUMBER = 8;
export const ASK_MAX_SHEET_VALUE = 80;
export const ASK_MAX_SHEET_VALUES = 80;

/** A setup sheet field's id, as ChassisTemplate makes them. */
export const ASK_FIELD_ID = /^[A-Za-z0-9_-]{1,48}$/;

/** A car photo's id on the laptop: PhotoStore's leaf file name. */
export const ASK_PHOTO_ID = /^[0-9a-f]{12}\.jpg$/;

const PACK_OPS = ["add", "edit", "retire", "unretire", "remove"];
const CAR_OPS = ["add", "edit", "retire", "unretire", "tonight", "cover", "unphoto"];

/**
 * A note that may run over lines: control characters other than the line
 * break out, each line's spaces tidied, at most two breaks in a row.
 */
export function cleanAskNotes(value: unknown, max: number): string {
  if (typeof value !== "string") {
    return "";
  }

  return value
    .replace(/\r\n?/g, "\n")
    .replace(/[\u0000-\u0009\u000b-\u001f\u007f]/g, " ")
    .split("\n")
    .map((line) => line.replace(/\s+/g, " ").trim())
    .join("\n")
    .replace(/\n{3,}/g, "\n\n")
    .trim()
    .slice(0, max);
}

/** Plain enough to be an address and nothing else - the desk mails a notice to the OLD one before it changes. */
const EMAIL = /^[^\s@<>()",;:\\[\]]+@[^\s@<>()",;:\\[\]]+\.[^\s@<>()",;:\\[\]]+$/;

// "late" and "skip" (11 Nov 2026, race-night build plan, Phase 1): "I need 5
// more minutes" and "my car's broken - skip me", both for the desk to approve.
// "coming" (12 Nov 2026, setup plan C1): "I'm coming to the next race night,
// in these classes" - applied by the laptop by itself (DRIVER_WRITE_KINDS).
export type DriverAskKind = "details" | "pack" | "query" | "setup" | "packedit" | "car" | "late" | "skip" | "coming" | "post" | "swap" | "cover";

export const DRIVER_ASK_KINDS: readonly DriverAskKind[] = ["details", "pack", "query", "setup", "packedit", "car", "late", "skip", "coming", "post", "swap", "cover"];

/** The longest note a "late" or "skip" can carry. */
export const ASK_MAX_RACE_NOTE = 120;

/** A "coming" ask's night: the next night snapshot's own day, yyyy-mm-dd. */
export const ASK_NIGHT_DAY = /^\d{4}-\d{2}-\d{2}$/;

/** A "swap" ask's driver asked: the laptop's cover key (MarshalSwaps.KeyOf), twelve hex. */
export const ASK_COVER_KEY = /^[0-9a-f]{12}$/;

/** A "cover" ask's offer: the relay's id for the swap ask it answers (randomBytes(12)). */
export const ASK_OFFER_ID = /^[0-9a-f]{24}$/;

/** A "coming" ask's classes, comma-separated on one line. */
export const ASK_MAX_COMING_CLASSES = 200;

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

    // With a race, the pack they ran in it (5 Nov 2026); without, the
    // pack in the car now, as it always was.
    if (input["race"] !== undefined) {
      const race = typeof input["race"] === "string" ? input["race"].trim().toLowerCase() : "";

      if (!ASK_OBJECT_ID.test(race)) {
        return refuse(400, "Which race was it?");
      }

      return { ok: true, f: { pack, race } };
    }

    return { ok: true, f: { pack } };
  }

  if (kind === "packedit") {
    return packEditFrom(input);
  }

  if (kind === "car") {
    return carFrom(input);
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

  // More time, or skip my race: a short note, nothing else. Which race is the
  // laptop's to know - the next one the driver is in.
  if (kind === "late" || kind === "skip") {
    const m = cleanAskText(input["m"], ASK_MAX_RACE_NOTE + 1);

    if (m.length > ASK_MAX_RACE_NOTE) {
      return refuse(400, `Keep it under ${ASK_MAX_RACE_NOTE} characters.`);
    }

    return { ok: true, f: m.length > 0 ? { m } : {} };
  }

  // I'm coming to the next race night (12 Nov 2026, setup plan C1): which
  // night, as the nextnight snapshot names it, and the classes ticked. The
  // day is checked to the letter because the laptop matches it against the
  // night it announced - a phone holding last week's snapshot must not be
  // put down for a night that is not the one on offer, and the laptop can
  // only tell that from a day it can read. The classes are one tidied line
  // the laptop splits on commas and matches against its own class names;
  // anything it does not know it leaves out and says so. Empty is allowed
  // and means "not coming after all" - the way a driver takes it back.
  if (kind === "coming") {
    const day = typeof input["day"] === "string" ? input["day"].trim() : "";

    if (!ASK_NIGHT_DAY.test(day)) {
      return refuse(400, "Which race night is it?");
    }

    if (input["classes"] !== undefined && typeof input["classes"] !== "string") {
      return refuse(400, "That request could not be read.");
    }

    const raw = cleanAskText(input["classes"], ASK_MAX_COMING_CLASSES + 1);

    if (raw.length > ASK_MAX_COMING_CLASSES) {
      return refuse(400, `Keep your classes under ${ASK_MAX_COMING_CLASSES} characters.`);
    }

    // Each name trimmed and empties dropped, so "Stock, ,Mod," reads "Stock,Mod".
    const classes = raw.split(",").map((one) => one.trim()).filter((one) => one !== "").join(",");

    return { ok: true, f: { day, classes } };
  }

  // Marshals from their phones (12 Nov 2026, setup plan step 8). The round
  // is a round id as the driver's live entry named it; the driver asked is
  // a cover key from the same entry (a hash, never a badge code); the offer
  // is the relay's own id for the swap ask, as the asked driver's entry
  // named it. Whether any of it is true is the laptop's to check.
  if (kind === "post" || kind === "swap") {
    const round = typeof input["round"] === "string" ? input["round"].trim().toLowerCase() : "";

    if (!ASK_OBJECT_ID.test(round)) {
      return refuse(400, "Which race is it?");
    }

    if (kind === "post") {
      return { ok: true, f: { round } };
    }

    const to = typeof input["to"] === "string" ? input["to"].trim().toLowerCase() : "";

    if (!ASK_COVER_KEY.test(to)) {
      return refuse(400, "Pick who you are asking.");
    }

    return { ok: true, f: { round, to } };
  }

  if (kind === "cover") {
    const offer = typeof input["offer"] === "string" ? input["offer"].trim().toLowerCase() : "";
    const answer = input["answer"];

    if (!ASK_OFFER_ID.test(offer) || (answer !== "yes" && answer !== "no")) {
      return refuse(400, "That request could not be read.");
    }

    return { ok: true, f: { offer, answer } };
  }

  if (kind === "setup" && (input["car"] !== undefined || input["sheet"] !== undefined)) {
    return sheetFrom(input);
  }

  if (kind === "setup") {
    const out: Record<string, string> = {};

    const limits: [string, number][] = ASK_SETUP_PARTS.map((n): [string, number] => [n, ASK_MAX_SETUP_PART]);
    limits.push(["notes", ASK_MAX_SETUP_NOTES]);

    for (const [name, max] of limits) {
      const value = input[name];

      if (value === undefined) {
        continue;
      }

      if (typeof value !== "string") {
        return refuse(400, "That request could not be read.");
      }

      const cleaned = cleanAskText(value, max + 1);

      if (cleaned.length > max) {
        return refuse(400, `Keep each part under ${max} characters.`);
      }

      if (cleaned.length > 0) {
        out[name] = cleaned;
      }
    }

    if (Object.keys(out).length === 0) {
      return refuse(400, "Say what you changed.");
    }

    return { ok: true, f: out };
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

/** An optional one-line text field: absent stays absent, a string is cleaned and checked, anything else is unreadable. */
function textInto(out: Record<string, string>, input: Record<string, unknown>, name: string, max: number, words: string): AskCheck | undefined {
  const value = input[name];

  if (value === undefined) {
    return undefined;
  }

  if (typeof value !== "string") {
    return refuse(400, "That request could not be read.");
  }

  const cleaned = name === "notes" ? cleanAskNotes(value, max + 1) : cleanAskText(value, max + 1);

  if (cleaned.length > max) {
    return refuse(400, `Keep the ${words} under ${max} characters.`);
  }

  out[name] = cleaned;
  return undefined;
}

/** An id the phone must send: lower-case 32 hex. */
function idFrom(input: Record<string, unknown>, name: string): string {
  const value = input[name];
  const id = typeof value === "string" ? value.trim().toLowerCase() : "";

  return ASK_OBJECT_ID.test(id) ? id : "";
}

/** One of their own packs: added, edited, retired, back in use or removed. */
function packEditFrom(input: Record<string, unknown>): AskCheck {
  const op = typeof input["op"] === "string" ? input["op"] : "";
  const pack = idFrom(input, "pack");

  if (!PACK_OPS.includes(op) || pack === "") {
    return refuse(400, "That request could not be read.");
  }

  const out: Record<string, string> = { op, pack };

  if (op === "add" || op === "edit") {
    for (const [name, max, words] of [
      ["label", ASK_MAX_PACK_LABEL, "label"],
      ["chem", ASK_MAX_PACK_CHEM, "type"],
      ["notes", ASK_MAX_NOTES, "notes"],
    ] as const) {
      const bad = textInto(out, input, name, max, words);

      if (bad !== undefined) {
        return bad;
      }
    }

    if (op === "add" && (out["label"] ?? "") === "") {
      return refuse(400, "Give the pack a label - what is written on it.");
    }
  }

  return { ok: true, f: out };
}

/** One of their own cars, every field the Garage has that a driver can change. */
function carFrom(input: Record<string, unknown>): AskCheck {
  const op = typeof input["op"] === "string" ? input["op"] : "";
  const car = idFrom(input, "car");

  if (!CAR_OPS.includes(op) || car === "") {
    return refuse(400, "That request could not be read.");
  }

  const out: Record<string, string> = { op, car };

  if (op === "add" || op === "edit") {
    for (const [name, max, words] of [
      ["name", ASK_MAX_CAR_NAME, "name"],
      ["chassisOther", ASK_MAX_CAR_NAME, "chassis name"],
      ["cls", ASK_MAX_CAR_CLASS, "class"],
      ["body", ASK_MAX_CAR_BODY, "bodyshell"],
      ["number", ASK_MAX_CAR_NUMBER, "race number"],
      ["notes", ASK_MAX_NOTES, "notes"],
    ] as const) {
      const bad = textInto(out, input, name, max, words);

      if (bad !== undefined) {
        return bad;
      }
    }

    if (input["chassis"] !== undefined) {
      const chassis = typeof input["chassis"] === "string" ? input["chassis"].trim().toLowerCase() : "-";

      if (chassis !== "" && !ASK_OBJECT_ID.test(chassis)) {
        return refuse(400, "Pick the chassis from the list.");
      }

      out["chassis"] = chassis;
    }

    if (op === "add" && (out["name"] ?? "") === "") {
      return refuse(400, "Give the car a name.");
    }
  }

  if (op === "cover" || op === "unphoto") {
    const photo = typeof input["photo"] === "string" ? input["photo"].trim().toLowerCase() : "";

    if (!ASK_PHOTO_ID.test(photo)) {
      return refuse(400, "Which photo?");
    }

    out["photo"] = photo;
  }

  return { ok: true, f: out };
}

/**
 * A whole setup sheet from the phone: the car, its sheet, the race it was
 * run in, a note and one "v.<field id>" per value. The relay checks only
 * the shape - the laptop holds the sheet and has the last word on which
 * fields and options are real.
 */
function sheetFrom(input: Record<string, unknown>): AskCheck {
  const car = idFrom(input, "car");

  if (car === "") {
    return refuse(400, "Which car is this setup for?");
  }

  const out: Record<string, string> = { car };

  for (const name of ["sheet", "race", "entry"]) {
    if (input[name] === undefined || input[name] === "") {
      continue;
    }

    const id = idFrom(input, name);

    if (id === "") {
      return refuse(400, "That request could not be read.");
    }

    out[name] = id;
  }

  const bad = textInto(out, input, "notes", ASK_MAX_NOTES, "notes");

  if (bad !== undefined) {
    return bad;
  }

  let values = 0;

  for (const [name, value] of Object.entries(input)) {
    if (!name.startsWith("v.")) {
      continue;
    }

    if (!ASK_FIELD_ID.test(name.slice(2)) || typeof value !== "string") {
      return refuse(400, "That request could not be read.");
    }

    const cleaned = cleanAskText(value, ASK_MAX_SHEET_VALUE + 1);

    if (cleaned.length > ASK_MAX_SHEET_VALUE) {
      return refuse(400, `Keep each value under ${ASK_MAX_SHEET_VALUE} characters.`);
    }

    if (cleaned === "") {
      continue;
    }

    values += 1;

    if (values > ASK_MAX_SHEET_VALUES) {
      return refuse(400, "That sheet has too many values.");
    }

    out[name] = cleaned;
  }

  // A car whose chassis has no sheet: the short log's five parts, on that car.
  if (out["sheet"] === undefined) {
    for (const name of ASK_SETUP_PARTS) {
      const bad = textInto(out, input, name, ASK_MAX_SETUP_PART, "part");

      if (bad !== undefined) {
        return bad;
      }

      if (out[name] === "") {
        delete out[name];
      } else if (out[name] !== undefined) {
        values += 1;
      }
    }
  }

  if (values === 0 && (out["notes"] ?? "") === "") {
    return refuse(400, "Fill in at least one part of the sheet.");
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
