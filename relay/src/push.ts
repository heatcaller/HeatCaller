import { createECDH, createHash, createPrivateKey, generateKeyPairSync, hkdfSync, randomBytes, sign, createCipheriv, type KeyObject } from "node:crypto";
import { mkdir, readFile, rename, writeFile } from "node:fs/promises";
import { dirname } from "node:path";
import { StoreReadError, type Store } from "./store.js";

/**
 * PUSH ALERTS (6 Nov 2026, Asher: "Build real push alerts", after "They
 * don't work by the way. I'm not getting alerts").
 *
 * WHY THIS EXISTS. The Me page's alerts only ever worked while the page was
 * open and on screen: it stops asking the relay anything once it is out of
 * sight, a phone in a pocket runs no page at all, and an iPhone cannot buzz
 * for a web page. A driver walking the pits never saw one. Web Push is the
 * phone's own way to be told: the phone's browser hands the page a private
 * address at its push service (Google's, Apple's, Mozilla's or
 * Microsoft's), the relay posts an encrypted message there, and the phone
 * shows it - locked, in a pocket, with the page closed. On an iPhone only
 * from the club's page added to the Home Screen; that is Apple's rule.
 *
 * WHAT IS SENT, AND WHEN. The same four moments the page already beeped
 * for, worked out here from what the laptop publishes, by the same rules:
 *   - their race has just become the next one, or has just been staged
 *     (the page's worthABuzz) - from driverlive;
 *   - their marshal duty has just become the next race - from driverlive;
 *   - their race's estimated start has just come inside five minutes
 *     (11 Nov 2026, Auto start) - from driverlive, once per race;
 *   - their marshal duty's estimated start has just come inside five
 *     minutes (12 Nov 2026, setup plan step 8) - the same, once per race;
 *   - a race of theirs has just been filed on their night - from nights.
 *     A demo race is filed there too, marked as a demo, so a demo is a way
 *     to try alerts out.
 * Never on the first look: a driver with no entry before has nothing to
 * compare with, so a relay restart or a laptop that has just come online
 * does not set every phone off.
 *
 * WHAT IT KNOWS. Per club, per badge code: each phone's push address and
 * the two keys the browser gave for it - nothing else, no name. A badge
 * code is the credential for a driver's night already; signing up needs
 * it, exactly as reading the night does. The message itself is encrypted
 * for that one phone (RFC 8291), so the push service carries words it
 * cannot read, and it holds no badge code: a tap opens the club's "my
 * night" address, which finds the driver this phone remembers.
 *
 * ONLY THE PUSH SERVICES. A push address is a URL the phone supplies, so
 * the relay would otherwise post to anywhere somebody typed. Only https on
 * the known push services' hosts is taken (pushHostAllowed).
 *
 * No dependencies, as the rest of the relay: node:crypto does the ECDH,
 * HKDF, AES-128-GCM and ES256 that Web Push is made of.
 */

/** Where a club's push sign-ups are kept in the store: one item per badge code. */
export const PUSH_CHANNEL = "pushsubs";

/**
 * FOLLOWER ALERTS (14 Nov 2026, stream overlay plan 7.1, Asher: "follower
 * alerts-yes"). A phone following a driver BY NAME - the family at home,
 * with no badge code - can be told before that driver races and when their
 * result is in. Kept apart from the drivers' own sign-ups: one item per
 * followed name (FOLLOW_CHANNEL), keyed by a hash of the name so no name
 * sits in a file name, holding the name and the phones.
 *
 * WHAT THIS CHANGES. The follow feature was built so that nothing about who
 * follows whom left the phone. Asked, Asher chose alerts: the relay now
 * keeps, per club, which phones (push addresses, never people) want alerts
 * for which public name. Nothing else: no badge code, no account. Built
 * only from what the club's public pages already show - the running order
 * and published results - so an alert never says more than the Schedule
 * and Results pages do.
 */
export const FOLLOW_CHANNEL = "followsubs";

/** Phones following one name. A relative's phone never pushes the driver's own (PUSH_SUBS_PER_DRIVER) off. */
export const FOLLOW_SUBS_PER_NAME = 10;

/** Follower phones per club, every name together - a ceiling, not a plan. */
export const FOLLOW_SUBS_PER_CLUB = 2000;

/** The kinds a follower can have: before their race, and their result. */
export const FOLLOW_KINDS: readonly string[] = ["race", "result"];

/** A followed name, tidied: one space between words, at most 60 characters, no control characters; "" for none. */
export function followNameFrom(value: unknown): string {
  const v = typeof value === "string" ? value.replace(/[\u0000-\u001f\u007f]/g, " ").trim().replace(/\s+/g, " ") : "";
  return v.length >= 1 && v.length <= 60 ? v : "";
}

/** The store id for a followed name: "f" and 32 hex digits of a hash of it, any case the same. */
export function followIdOf(name: string): string {
  return "f" + createHash("sha256").update(name.toLowerCase(), "utf8").digest("hex").slice(0, 32);
}

const FOLLOW_ID = /^f[0-9a-f]{32}$/;

/** The first race still to run (or running) that this name is in, with how many open races come before it. */
function followLineOf(programme: unknown, name: string): { line: Record<string, unknown>; away: number } | undefined {
  const lines = isObject(programme) && Array.isArray(programme["lines"]) ? programme["lines"] : [];
  const lower = name.toLowerCase();
  let away = 0;

  for (const l of lines) {
    if (!isObject(l)) continue;
    const open = l["status"] === "running" || l["status"] === "next" || l["status"] === "upcoming";
    if (!open) continue;
    const drivers = Array.isArray(l["drivers"]) ? l["drivers"] : [];
    if (drivers.some((d) => typeof d === "string" && d.toLowerCase() === lower)) {
      return { line: l, away };
    }
    away++;
  }

  return undefined;
}

/** The race's name as the Schedule page writes it: "Junior - Heat 2". */
function lineTitle(l: Record<string, unknown>): string {
  return [l["className"], l["description"]].filter((x) => typeof x === "string" && x.trim() !== "").join(" - ") || "their race";
}

/**
 * What a follower of <name> should hear now the running order has changed:
 * their race has just become the next one. Once per race - a line that was
 * already "next" before says nothing again. Never on the first look.
 */
export function followProgrammeAlerts(before: unknown, after: unknown, name: string, url: string): PushMessage[] {
  if (!isObject(before) || !isObject(after) || after["lineups"] !== true) {
    return [];
  }

  const now = followLineOf(after, name);

  if (now === undefined || now.line["status"] !== "next") {
    return [];
  }

  const was = followLineOf(before, name);

  if (was !== undefined && was.line["status"] === "next" && was.line["id"] === now.line["id"]) {
    return [];
  }

  const estimate = typeof now.line["estimate"] === "string" && /^about /.test(now.line["estimate"]) ? ", " + now.line["estimate"] : "";
  return [{ title: name + "'s race is next", body: lineTitle(now.line) + estimate, tag: "hc-race", url }];
}

/** "1st", "2nd", "3rd", "11th". */
function ordinalWords(n: number): string {
  const t = n % 100;
  const e = n % 10;
  return n + (t >= 11 && t <= 13 ? "th" : e === 1 ? "st" : e === 2 ? "nd" : e === 3 ? "rd" : "th");
}

/**
 * What a follower of <name> should hear for a result just published: where
 * they finished, in plain words. Only for a result that is new - one
 * republished (a correction) says nothing again.
 */
export function followResultAlerts(before: unknown, after: unknown, name: string, url: string): PushMessage[] {
  if (before !== undefined || !isObject(after) || !Array.isArray(after["results"])) {
    return [];
  }

  const lower = name.toLowerCase();
  const row = after["results"].find((r) => isObject(r) && typeof r["name"] === "string" && r["name"].toLowerCase() === lower);

  if (!isObject(row)) {
    return [];
  }

  const pos = Number(row["pos"]);
  const what = [after["label"], after["type"]].find((x) => typeof x === "string" && x.trim() !== "") as string | undefined;
  const where = Number.isInteger(pos) && pos > 0 ? "finished " + ordinalWords(pos) : "has a result in";

  return [{ title: name + " " + where, body: what ? what : "See the result", tag: "hc-result", url }];
}

/** Phones per driver. A new one past this pushes out the oldest. */
export const PUSH_SUBS_PER_DRIVER = 5;

/** Sign-ups per club, all drivers together - a ceiling, not a plan. */
export const PUSH_SUBS_PER_CLUB = 2000;

/** How long a message waits at the push service for a phone that is off. A race call is stale in ten minutes. */
export const PUSH_TTL_SECONDS = 600;

/**
 * How long a club-wide message waits for a phone that is off (12 Nov 2026,
 * setup plan step 7: "racing's off" by push). Unlike a race call, "the
 * next race night is off" is still worth hearing hours later - a driver
 * whose phone was off at tea-time must still learn of it before setting out.
 */
export const BROADCAST_TTL_SECONDS = 6 * 3600;

/** The longest any message may wait at a push service: a day. */
export const PUSH_TTL_MAX_SECONDS = 24 * 3600;

const SEND_TIMEOUT_MS = 10000;

/** One phone's sign-up, as the browser gave it. */
export interface PushSub {
  readonly endpoint: string;
  /** The phone's public key, base64url - 65 bytes, uncompressed P-256. */
  readonly p256dh: string;
  /** The phone's auth secret, base64url - 16 bytes. */
  readonly auth: string;
  readonly since: string;
  /**
   * Whose alerts these are, put before each title ("Ella: Your race is
   * next") - only when the phone has more than one driver on it (14 Nov
   * 2026, driver phone plan item 8). A first name, never the badge code.
   */
  readonly label?: string;
  /**
   * Which kinds of alert this phone wants (14 Nov 2026, driver phone plan
   * item 9): any of "race", "marshal", "result", "news". Undefined is
   * all of them, as every phone signed up before this had.
   */
  readonly kinds?: readonly string[];
}

/** The kinds of alert a phone can switch on and off (see PushSub.kinds). */
export const ALERT_KINDS: readonly string[] = ["race", "marshal", "result", "news", "video"];

/** Which kind an alert is, by its tag. */
export function alertKindOf(tag: string): string {
  if (tag === "hc-race" || tag === "hc-soon") return "race";
  if (tag.startsWith("hc-marshal")) return "marshal";
  if (tag === "hc-result") return "result";
  if (tag === "hc-video") return "video";
  return "news";
}

/** A phone's choice of kinds, checked: known kinds only, each once; undefined when it sent none. */
export function pushKindsFrom(value: unknown): string[] | undefined {
  if (!Array.isArray(value)) return undefined;
  return [...new Set(value.filter((k): k is string => typeof k === "string" && ALERT_KINDS.includes(k)))];
}

/** A phone's label for a driver's alerts (see PushSub.label): a short name, or "". */
export function pushLabelFrom(value: unknown): string {
  const v = typeof value === "string" ? value.trim().replace(/\s+/g, " ") : "";
  return v.length >= 1 && v.length <= 20 && /^[\p{L}\p{M}\p{N}'. -]+$/u.test(v) ? v : "";
}

/** What a phone is told. */
export interface PushMessage {
  readonly title: string;
  readonly body: string;
  /** A later message with the same tag replaces this one on the phone. */
  readonly tag: string;
  /** Where a tap goes. Never carries a badge code. */
  readonly url: string;
}

/** What posting to a push service came back with. */
export interface PushAnswer {
  readonly status: number;
  /** What the push service said back, cut short - Apple puts its reason here ({"reason":"..."}). */
  readonly reason?: string;
}

export type PushPoster = (url: string, init: { method: string; headers: Record<string, string>; body: Uint8Array }) => Promise<PushAnswer>;

export interface PushOptions {
  readonly store: Store;
  /** Where the relay's own key pair is kept. Absent: made fresh for this run (tests). */
  readonly keyFile?: string | undefined;
  /** The VAPID "sub": who runs this relay, as an https URL or mailto. */
  readonly subject: string;
  readonly now?: () => Date;
  readonly log?: (line: string) => void;
  /** Swappable for tests; fetch otherwise. */
  readonly post?: PushPoster;
}

// --------------------------------------------------------------- encoding

export function b64url(bytes: Uint8Array): string {
  return Buffer.from(bytes).toString("base64url");
}

export function fromB64url(text: string): Buffer {
  return Buffer.from(text, "base64url");
}

/**
 * Whether a push address is one of the push services, over https on the
 * standard port. Anything else is refused at sign-up, so the relay never
 * posts to an address of somebody's choosing.
 */
export function pushHostAllowed(endpoint: string): boolean {
  let url: URL;
  try {
    url = new URL(endpoint);
  } catch {
    return false;
  }

  if (url.protocol !== "https:" || url.port !== "" || url.username !== "" || url.password !== "") {
    return false;
  }

  const host = url.hostname.toLowerCase();
  const exact = ["fcm.googleapis.com", "android.googleapis.com", "updates.push.services.mozilla.com", "push.services.mozilla.com", "web.push.apple.com"];
  return exact.includes(host) || host.endsWith(".push.apple.com") || host.endsWith(".notify.windows.com");
}

/** A sign-up as the browser's PushSubscription.toJSON() gives it, checked; undefined when it is not one. */
export function pushSubFrom(value: unknown, since: string): PushSub | undefined {
  if (typeof value !== "object" || value === null) {
    return undefined;
  }

  const v = value as Record<string, unknown>;
  const keys = typeof v["keys"] === "object" && v["keys"] !== null ? (v["keys"] as Record<string, unknown>) : {};
  const endpoint = typeof v["endpoint"] === "string" ? v["endpoint"] : "";
  const p256dh = typeof keys["p256dh"] === "string" ? keys["p256dh"] : "";
  const auth = typeof keys["auth"] === "string" ? keys["auth"] : "";

  if (endpoint.length === 0 || endpoint.length > 1024 || !pushHostAllowed(endpoint)) {
    return undefined;
  }

  if (!/^[A-Za-z0-9_-]+=*$/.test(p256dh) || !/^[A-Za-z0-9_-]+=*$/.test(auth)) {
    return undefined;
  }

  const key = fromB64url(p256dh);
  const secret = fromB64url(auth);

  if (key.length !== 65 || key[0] !== 0x04 || secret.length !== 16) {
    return undefined;
  }

  return { endpoint, p256dh: b64url(key), auth: b64url(secret), since };
}

// --------------------------------------------------------------- RFC 8291

/**
 * The message encrypted for one phone, as RFC 8291 has it ("aes128gcm"):
 * a key agreed between a throwaway key pair of the relay's and the phone's
 * own, mixed with the phone's auth secret, then one AES-128-GCM record.
 * salt and serverKey are for tests; left out, both are fresh every time.
 */
export function encryptForPush(
  plaintext: Uint8Array,
  sub: { p256dh: string; auth: string },
  salt: Uint8Array = randomBytes(16),
  serverKey?: { privateKey: Uint8Array },
): Buffer {
  const uaPublic = fromB64url(sub.p256dh);
  const authSecret = fromB64url(sub.auth);

  const ecdh = createECDH("prime256v1");
  if (serverKey) {
    ecdh.setPrivateKey(serverKey.privateKey);
  } else {
    ecdh.generateKeys();
  }
  const asPublic = ecdh.getPublicKey();
  const shared = ecdh.computeSecret(uaPublic);

  const keyInfo = Buffer.concat([Buffer.from("WebPush: info\0", "latin1"), uaPublic, asPublic]);
  const ikm = Buffer.from(hkdfSync("sha256", shared, authSecret, keyInfo, 32));
  const cek = Buffer.from(hkdfSync("sha256", ikm, salt, Buffer.from("Content-Encoding: aes128gcm\0", "latin1"), 16));
  const nonce = Buffer.from(hkdfSync("sha256", ikm, salt, Buffer.from("Content-Encoding: nonce\0", "latin1"), 12));

  // One record, the last: the content, then the 0x02 delimiter.
  const cipher = createCipheriv("aes-128-gcm", cek, nonce);
  const sealed = Buffer.concat([cipher.update(Buffer.concat([Buffer.from(plaintext), Buffer.from([2])])), cipher.final(), cipher.getAuthTag()]);

  const recordSize = Buffer.alloc(4);
  recordSize.writeUInt32BE(4096);

  return Buffer.concat([Buffer.from(salt), recordSize, Buffer.from([asPublic.length]), asPublic, sealed]);
}

// ------------------------------------------------------------------ VAPID

/** The relay's own key pair, which identifies it to the push services (RFC 8292). */
export interface VapidKeys {
  readonly privateKey: KeyObject;
  /** 65 bytes, uncompressed - what the page hands the browser as applicationServerKey. */
  readonly publicRaw: Buffer;
}

function vapidFromJwk(jwk: { x: string; y: string; d: string }): VapidKeys {
  const privateKey = createPrivateKey({ key: { kty: "EC", crv: "P-256", x: jwk.x, y: jwk.y, d: jwk.d }, format: "jwk" });
  const publicRaw = Buffer.concat([Buffer.from([4]), fromB64url(jwk.x), fromB64url(jwk.y)]);
  return { privateKey, publicRaw };
}

function freshVapidJwk(): { x: string; y: string; d: string } {
  const { privateKey } = generateKeyPairSync("ec", { namedCurve: "P-256" });
  const jwk = privateKey.export({ format: "jwk" }) as { x?: string; y?: string; d?: string };
  return { x: jwk.x ?? "", y: jwk.y ?? "", d: jwk.d ?? "" };
}

/** The Authorization header for one push service: a signed note, good for twelve hours. */
export function vapidHeader(keys: VapidKeys, endpoint: string, subject: string, now: Date): string {
  const audience = new URL(endpoint).origin;
  const header = b64url(Buffer.from(JSON.stringify({ typ: "JWT", alg: "ES256" })));
  const claims = b64url(Buffer.from(JSON.stringify({ aud: audience, exp: Math.floor(now.getTime() / 1000) + 12 * 3600, sub: subject })));
  const signature = sign("sha256", Buffer.from(`${header}.${claims}`), { key: keys.privateKey, dsaEncoding: "ieee-p1363" });
  return `vapid t=${header}.${claims}.${b64url(signature)}, k=${b64url(keys.publicRaw)}`;
}

// ------------------------------------------------------- what to say, when

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function num(value: unknown): number {
  return typeof value === "number" && Number.isFinite(value) ? value : 0;
}

function ordinal(n: number): string {
  const tail = n % 100;
  return n + (tail >= 11 && tail <= 13 ? "th" : (["th", "st", "nd", "rd"][n % 10] ?? "th"));
}

/** The page's callKey: what the call-up says, to tell a change. */
function callKey(n: unknown): string {
  return isObject(n) ? `${String(n["race"] ?? "")}|${num(n["away"])}|${n["staged"] === true}` : "none";
}

function dutyKey(m: unknown): string {
  return isObject(m) && typeof m["race"] === "string" ? `${m["race"]}|${num(m["away"])}` : "none";
}

/**
 * What one driver's live entry, before and after, is worth telling them -
 * the page's own rules (worthABuzz, and the marshal duty's). Nothing for a
 * driver with no entry before.
 */
/** How close a race's start has to be for "Your race is in about 5 minutes". */
export const SOON_MS = 5 * 60 * 1000;

/**
 * Whether a driver's next race starts within SOON_MS of `atMs`, by the
 * laptop's estimate (next.eta) - not on track or staged yet. False with no
 * estimate or no time to measure from.
 */
function startsSoon(n: unknown, atMs: number | undefined): boolean {
  if (!isObject(n) || atMs === undefined || !Number.isFinite(atMs) || n["now"] === true || n["staged"] === true || typeof n["eta"] !== "string") {
    return false;
  }

  const eta = Date.parse(n["eta"]);
  return Number.isFinite(eta) && eta - atMs <= SOON_MS && eta - atMs > -60 * 1000;
}

/** "You're marshalling Q2 Heat 3 in about 5 minutes - Turn 3": a marshal duty's soon push, in words. */
export function marshalSoonWords(m: Record<string, unknown>): string {
  const post = typeof m["post"] === "string" && m["post"].trim() !== "" ? " - " + m["post"].trim() : "";
  return "You're marshalling " + String(m["race"] ?? "the next race") + " in about 5 minutes" + post;
}

/**
 * The soon push's title with the post in it - "Marshal at Turn 3 in about 5
 * minutes" (12 Nov 2026, Asher: "make sure the marshal post is in the 5
 * minutes notification for marshalling"). A phone's lock screen shows the
 * title whole but cuts the body short, and the end of the body was where
 * the post was. Without a named post, the title it always had.
 */
export function marshalSoonTitle(m: Record<string, unknown>): string {
  const post = typeof m["post"] === "string" ? m["post"].replace(/[\u0000-\u001f\u007f]/g, " ").trim().slice(0, 40) : "";
  return post !== "" ? `Marshal at ${post} in about 5 minutes` : "Marshal duty in about 5 minutes";
}

export function liveAlerts(before: unknown, after: unknown, url: string, beforeAtMs?: number, afterAtMs?: number): PushMessage[] {
  if (!isObject(before) || !isObject(after)) {
    return [];
  }

  const out: PushMessage[] = [];
  const n = after["next"];

  if (isObject(n) && callKey(n) !== callKey(before["next"]) && (n["staged"] === true || (n["now"] !== true && num(n["away"]) === 0))) {
    const race = typeof n["race"] === "string" ? n["race"] : "Your race";
    const slot = num(n["slot"]) > 0 ? ` · grid slot ${num(n["slot"])}` : "";
    out.push({
      title: n["staged"] === true ? "Your race is staged - to the driver stand" : "Your race is next",
      body: race + slot,
      tag: "hc-race",
      url,
    });
  }

  const m = after["marshal"];

  if (isObject(m) && dutyKey(m) !== dutyKey(before["marshal"]) && num(m["away"]) === 0 && typeof m["race"] === "string" && m["race"] !== "") {
    const post = typeof m["post"] === "string" && m["post"] !== "" ? ` - ${m["post"]}` : "";
    out.push({ title: "Marshal duty next", body: m["race"] + post, tag: "hc-marshal", url });
  }

  // "Your race is in about 5 minutes" (11 Nov 2026, race-night build plan,
  // Phase 1): once per race, as its estimated start comes inside five
  // minutes - not when "Your race is next" goes out at the same moment.
  const race = isObject(n) && typeof n["race"] === "string" ? n["race"] : "";
  const wasSoon = startsSoon(before["next"], beforeAtMs) && isObject(before["next"]) && before["next"]["race"] === race;

  if (race !== "" && startsSoon(n, afterAtMs) && !wasSoon && !out.some((message) => message.tag === "hc-race")) {
    out.push({ title: "Your race is in about 5 minutes", body: race, tag: "hc-soon", url });
  }

  // "You're marshalling Q2 Heat 3 in about 5 minutes - Turn 3" (12 Nov 2026,
  // setup plan step 8): the same once-per-race rule, from the marshal duty's
  // own estimate (marshal.eta, which the laptop sends from then on). Not in
  // the same breath as "Marshal duty next", which already sent them there,
  // and not while they have said they're at their post.
  const duty = isObject(m) && typeof m["race"] === "string" ? m["race"] : "";
  const dutyWasSoon = startsSoon(before["marshal"], beforeAtMs) && isObject(before["marshal"]) && before["marshal"]["race"] === duty;

  if (duty !== "" && isObject(m) && m["in"] !== true && startsSoon(m, afterAtMs) && !dutyWasSoon && !out.some((message) => message.tag === "hc-marshal")) {
    out.push({ title: marshalSoonTitle(m), body: marshalSoonWords(m), tag: "hc-marshal-soon", url });
  }

  return out;
}

/**
 * Keeps the "in about 5 minutes" pushes to once a race (Bug Hunt 17).
 * liveAlerts only sees the document before, and the laptop's estimate can
 * step back over the five-minute line and in again - an estimate that moves
 * with the clock, rounded to the minute, does it every minute - so the phone
 * was buzzed each time. `sent` remembers, for each driver, the race and the
 * marshal duty already inside five minutes; a soon push for the same one is
 * dropped. Forgotten when their next race, or duty, is a different one.
 */
export function onceSoon(sent: Map<string, string>, who: string, entry: unknown, atMs: number | undefined, alerts: PushMessage[]): PushMessage[] {
  let out = alerts;

  for (const [field, tag] of [["next", "hc-soon"], ["marshal", "hc-marshal-soon"]] as const) {
    const part = isObject(entry) ? entry[field] : undefined;
    const race = isObject(part) && typeof part["race"] === "string" ? part["race"] : "";
    const key = `${who}|${field}`;

    if (sent.get(key) !== race) {
      sent.delete(key);
    } else if (race !== "") {
      out = out.filter((message) => message.tag !== tag);
    }

    // Remembered whether or not it was pushed: "Your race is next" going at
    // the same moment stands in for it. Not a marshal who is at their post,
    // who is told nothing and has not been.
    if (race !== "" && startsSoon(part, atMs) && !(field === "marshal" && isObject(part) && part["in"] === true)) {
      sent.set(key, race);
    }
  }

  return out;
}

function racesOf(night: unknown): unknown[] {
  return isObject(night) && Array.isArray(night["races"]) ? night["races"] : [];
}

/** A race of theirs just filed on their night: "Your result is in". Nothing when the same night has no more races than before. */
export function nightAlerts(before: unknown, after: unknown, url: string): PushMessage[] {
  // A driver's night is one item kept from meeting to meeting, so `before`
  // can be last week's. A new night starts from no races (6 Nov 2026), or a
  // returning driver heard nothing until they passed last week's count.
  const sameNight = (isObject(before) ? before["night"] : undefined) === (isObject(after) ? after["night"] : undefined);
  const was = sameNight ? racesOf(before).length : 0;
  const races = racesOf(after);

  if (races.length <= was) {
    return [];
  }

  const r = races[races.length - 1];
  const race = isObject(r) ? r : {};
  const label = typeof race["label"] === "string" ? race["label"] : "";
  const out = typeof race["out"] === "string" && race["out"] !== "" ? race["out"] : "";
  const placed = num(race["pos"]) > 0 ? ordinal(num(race["pos"])) + (num(race["of"]) > 0 ? ` of ${num(race["of"])}` : "") : "";
  // A demo race or a practice run is filed on the night marked as such (3
  // Nov 2026) and said so here, so nobody takes a rehearsal for a result.
  const run = race["kind"] === "demo" ? " (demo)" : race["kind"] === "practice" ? " (practice)" : "";

  return [{
    title: "Your result is in" + run,
    body: [[label, out || placed].filter((s) => s !== "").join(" - "), ...leaderboardLines(race)].join("\n"),
    tag: "hc-result",
    // The race's own result (14 Nov 2026, driver phone plan item 9), not
    // just Me: a tap lands on the thing the alert was about. A demo race is
    // never sent to results, so its tap stays on their night, where it is.
    url: race["kind"] !== "demo" && typeof race["sid"] === "string" && /^[0-9a-f]{32}$/.test(race["sid"]) ? url.replace(/\/mynight$/, `/results/${race["sid"]}`) : url,
  }];
}

/**
 * The race's top three under the result (12 Nov 2026, Asher: "when the race
 * results are posted add the leaderboard to the push notification"): "1. Sam P
 * - 20 laps", their own line marked "(you)". From races[].top, which the
 * laptop sends from then on - an older laptop's night has none, and the push
 * is the one line it always was. Names are cut short and stripped of control
 * characters: a push is plain text, a line each.
 */
export function leaderboardLines(race: Record<string, unknown>): string[] {
  const top = Array.isArray(race["top"]) ? race["top"] : [];
  const mine = num(race["pos"]);
  const lines: string[] = [];

  for (const row of top.slice(0, 3)) {
    if (!isObject(row) || num(row["p"]) < 1 || typeof row["n"] !== "string") {
      continue;
    }

    const name = row["n"].replace(/[\u0000-\u001f\u007f]/g, " ").replace(/\s+/g, " ").trim().slice(0, 40);

    if (name === "") {
      continue;
    }

    const laps = num(row["laps"]) > 0 ? ` - ${num(row["laps"])} ${num(row["laps"]) === 1 ? "lap" : "laps"}` : "";
    lines.push(`${num(row["p"])}. ${name}${laps}${num(row["p"]) === mine ? " (you)" : ""}`);
  }

  return lines;
}

// ---------------------------------------------------------------- service

const DRIVER_ID = /^[0-9a-f]{20}$/;

export class PushService {
  private readonly store: Store;
  private readonly keyFile: string | undefined;
  private readonly subject: string;
  private readonly now: () => Date;
  private readonly log: (line: string) => void;
  private readonly post: PushPoster;
  private keys: VapidKeys | undefined;
  private loading: Promise<VapidKeys> | undefined;
  /**
   * One club's sign-ups changed one at a time (7 Nov 2026, Bug Hunt 13).
   * Each change reads a driver's list and writes it back, and an alert
   * being sent writes back what it read before its posts - up to ten
   * seconds a phone - so without this a phone signing up meanwhile was
   * written over, and lost, when another phone turned out to be gone.
   * Only the reading and writing is held, never a post.
   */
  private readonly locks = new Map<string, Promise<void>>();
  /** The race and marshal duty each driver has already had "in about 5 minutes" for (onceSoon). */
  private readonly soonSent = new Map<string, string>();
  /**
   * The night each driver last had "videos are up" for (race video V2), so
   * it comes once a night. In memory: a relay restart mid-night can send it
   * once more, which beats keeping a list of who raced on disk.
   */
  private readonly videoSent = new Map<string, string>();

  constructor(options: PushOptions) {
    this.store = options.store;
    this.keyFile = options.keyFile;
    this.subject = options.subject;
    this.now = options.now ?? (() => new Date());
    this.log = options.log ?? (() => {});
    this.post = options.post ?? (async (url, init) => {
      const r = await fetch(url, {
        method: init.method,
        headers: init.headers,
        body: new Blob([new Uint8Array(init.body)]),
        signal: AbortSignal.timeout(SEND_TIMEOUT_MS),
      });
      // The reason (7 Nov 2026): a refused alert logged only "answered
      // 400", which says nothing about what to fix. The body is short and
      // carries nothing of the driver's - the push service's own words.
      const reason = r.ok ? "" : (await r.text().catch(() => "")).slice(0, 200);
      return { status: r.status, reason };
    });
  }

  /** The relay's key pair: read from keyFile, or made and written there the first time. */
  async vapid(): Promise<VapidKeys> {
    if (this.keys) {
      return this.keys;
    }

    // WHY (7 Nov 2026, Bug Hunt 13): any failure to read the key used to
    // count as "not made yet", so a passing EIO or EMFILE on a later start
    // made a new key and wrote it over the good one - and every phone
    // already signed up, in every club, is bound to the old one, so the
    // push services refused every alert from then on. Only a missing file
    // is "not made yet" now, as for every other file the relay keeps; one
    // that is there but will not read stops push for this attempt, and
    // the next attempt reads it again.
    this.loading ??= (async () => {
      let jwk: { x: string; y: string; d: string } | undefined;

      if (this.keyFile !== undefined) {
        let text: string | undefined;

        try {
          text = await readFile(this.keyFile, "utf8");
        } catch (e) {
          if ((e as NodeJS.ErrnoException).code !== "ENOENT") {
            throw new Error(`the relay's push key could not be read: ${String(e)}`);
          }
        }

        if (text !== undefined) {
          let kept: { x?: unknown; y?: unknown; d?: unknown } | undefined;

          try {
            kept = JSON.parse(text) as { x?: unknown; y?: unknown; d?: unknown };
          } catch {
            kept = undefined;
          }

          if (kept === undefined || typeof kept !== "object" || kept === null
            || typeof kept.x !== "string" || typeof kept.y !== "string" || typeof kept.d !== "string") {
            throw new Error("the relay's push key is there but is not a key - left as it is");
          }

          jwk = { x: kept.x, y: kept.y, d: kept.d };
        }
      }

      if (jwk === undefined) {
        jwk = freshVapidJwk();

        if (this.keyFile !== undefined) {
          // Written to a file beside it and renamed over, so a crash
          // part-way leaves no half a key for the next start to refuse.
          const temp = `${this.keyFile}.${randomBytes(6).toString("hex")}.tmp`;
          await mkdir(dirname(this.keyFile), { recursive: true });
          await writeFile(temp, JSON.stringify(jwk), { mode: 0o600 });
          await rename(temp, this.keyFile);
          this.log("[push] made the relay's push key");
        }
      }

      this.keys = vapidFromJwk(jwk);
      return this.keys;
    })();

    const loading = this.loading;

    try {
      return await loading;
    } catch (e) {
      // Not kept: a failed attempt would otherwise be the answer until
      // the relay restarted.
      if (this.loading === loading) {
        this.loading = undefined;
      }

      this.log(`[push] ${String(e)}`);
      throw e;
    }
  }

  /** The public key the page hands the browser, base64url. */
  async publicKey(): Promise<string> {
    return b64url((await this.vapid()).publicRaw);
  }

  /**
   * A driver's phones. A file that is there but could not be read this
   * time throws (9 Nov 2026, Bug Hunt 15): answered as "no phones", a
   * sign-up from a tablet wrote a list of just the tablet over the file and
   * the driver's other phones stopped hearing anything. Only a missing
   * file, or one that is not JSON - which only a new write mends - is an
   * empty list.
   */
  private async subsOf(club: string, id: string, channel: string = PUSH_CHANNEL): Promise<PushSub[]> {
    const doc = await this.store.getItem(club, channel, id).catch((error: unknown) => {
      if (error instanceof StoreReadError && error.corrupt) {
        return undefined;
      }

      throw error;
    });
    const list = doc && isObject(doc.body) && Array.isArray(doc.body["subs"]) ? doc.body["subs"] : [];
    return list.filter((s): s is PushSub => isObject(s) && typeof s["endpoint"] === "string" && typeof s["p256dh"] === "string" && typeof s["auth"] === "string")
      .map((s) => (s.label === undefined || pushLabelFrom(s.label) === s.label ? s : { ...s, label: undefined }))
      .map((s) => (s.kinds === undefined ? s : { ...s, kinds: pushKindsFrom(s.kinds) })) as PushSub[];
  }

  private async keep(club: string, id: string, subs: readonly PushSub[], channel: string = PUSH_CHANNEL, extra: Record<string, unknown> = {}): Promise<void> {
    if (subs.length === 0) {
      await this.store.deleteItem(club, channel, id).catch(() => undefined);
      return;
    }

    await this.store.putItem(club, channel, id, { seq: 0, publishedAt: this.now().toISOString(), body: { ...extra, subs } });
  }

  /** Signs a phone up for alerts about a driver it follows by name (follower alerts, see FOLLOW_CHANNEL). "full" at the ceilings. */
  async followSubscribe(club: string, name: string, sub: PushSub): Promise<"ok" | "full" | "bad"> {
    const clean = followNameFrom(name);

    if (clean === "") {
      return "bad";
    }

    const kinds = (sub.kinds ?? FOLLOW_KINDS).filter((k) => FOLLOW_KINDS.includes(k));
    // No label: a follower phone hears the driver's name in every title already.
    const { label: _label, ...unlabelled } = sub;
    const mine: PushSub = { ...unlabelled, kinds: kinds.length > 0 ? kinds : [...FOLLOW_KINDS] };
    const id = followIdOf(clean);

    return this.withLock(club, async () => {
      const subs = await this.subsOf(club, id, FOLLOW_CHANNEL);

      if (!subs.some((s) => s.endpoint === mine.endpoint)) {
        const all = await this.store.listItems(club, FOLLOW_CHANNEL);
        const count = all.reduce((n, item) => n + (isObject(item.doc.body) && Array.isArray(item.doc.body["subs"]) ? item.doc.body["subs"].length : 0), 0);

        if (count >= FOLLOW_SUBS_PER_CLUB) {
          return "full" as const;
        }
      }

      const others = subs.filter((s) => s.endpoint !== mine.endpoint);
      await this.keep(club, id, [...others, mine].slice(-FOLLOW_SUBS_PER_NAME), FOLLOW_CHANNEL, { name: clean });
      return "ok" as const;
    });
  }

  /** Takes one phone off a followed name's alerts. */
  async followUnsubscribe(club: string, name: string, endpoint: string): Promise<void> {
    const clean = followNameFrom(name);

    if (clean === "") {
      return;
    }

    const id = followIdOf(clean);

    await this.withLock(club, async () => {
      const subs = await this.subsOf(club, id, FOLLOW_CHANNEL);
      const left = subs.filter((s) => s.endpoint !== endpoint);

      if (left.length !== subs.length) {
        await this.keep(club, id, left, FOLLOW_CHANNEL, { name: clean });
      }
    });
  }

  /** Every followed name with a phone signed up, by store id. */
  private async followed(club: string): Promise<Map<string, string>> {
    const all = await this.store.listItems(club, FOLLOW_CHANNEL).catch(() => []);
    const out = new Map<string, string>();

    for (const item of all) {
      const name = isObject(item.doc.body) ? followNameFrom(item.doc.body["name"]) : "";
      if (FOLLOW_ID.test(item.id) && name !== "") out.set(item.id, name);
    }

    return out;
  }

  /** The running order has just been written over: a followed driver whose race is now next. Never throws. */
  async afterProgramme(club: string, before: unknown, after: unknown): Promise<void> {
    try {
      const names = await this.followed(club);
      const url = `/c/${encodeURIComponent(club)}/`;

      for (const [id, name] of names) {
        for (const message of followProgrammeAlerts(before, after, name, url)) {
          await this.deliver(club, id, message, PUSH_TTL_SECONDS, FOLLOW_CHANNEL);
        }
      }
    } catch (e) {
      this.log(`[push] "${club}": could not work out the follower alerts: ${String(e)}`);
    }
  }

  /** A result has just been published: a followed driver who is in it. Never throws. */
  async afterResult(club: string, sessionId: string, before: unknown, after: unknown): Promise<void> {
    try {
      const names = await this.followed(club);
      const url = `/c/${encodeURIComponent(club)}/results#${encodeURIComponent(sessionId)}`;

      for (const [id, name] of names) {
        for (const message of followResultAlerts(before, after, name, url)) {
          await this.deliver(club, id, message, PUSH_TTL_SECONDS, FOLLOW_CHANNEL);
        }
      }
    } catch (e) {
      this.log(`[push] "${club}": could not work out the follower result alerts: ${String(e)}`);
    }
  }

  private async withLock<T>(club: string, work: () => Promise<T>): Promise<T> {
    const previous = this.locks.get(club) ?? Promise.resolve();
    const run = previous.then(work);
    const settled = run.then(
      () => undefined,
      () => undefined,
    );

    this.locks.set(club, settled);

    try {
      return await run;
    } finally {
      if (this.locks.get(club) === settled) {
        this.locks.delete(club);
      }
    }
  }

  /**
   * Every phone signed up for a club, forgotten - the club closing
   * drivers' nights. With `wipe`, that is what forgets them instead (Delete
   * everything takes the club's whole folder). Either way no alert still
   * being sent can write a driver's list back afterwards: it reads the
   * list again, inside the same lock, and finds nothing.
   */
  async forget(club: string, wipe?: () => Promise<void>): Promise<void> {
    await this.withLock(club, async () => {
      if (wipe !== undefined) {
        await wipe();
        return;
      }

      for (const channel of [PUSH_CHANNEL, FOLLOW_CHANNEL]) {
        for (const item of await this.store.itemSizes(club, channel).catch(() => [])) {
          await this.store.deleteItem(club, channel, item.id).catch(() => undefined);
        }
      }
    });
  }

  /** Signs a phone up for a driver's alerts. "full" when the club has reached its ceiling. */
  async subscribe(club: string, id: string, sub: PushSub): Promise<"ok" | "full"> {
    if (!DRIVER_ID.test(id)) {
      return "full";
    }

    return this.withLock(club, () => this.subscribeHeld(club, id, sub));
  }

  private async subscribeHeld(club: string, id: string, sub: PushSub): Promise<"ok" | "full"> {
    const mine = await this.subsOf(club, id);

    if (!mine.some((s) => s.endpoint === sub.endpoint)) {
      // Not caught (Bug Hunt 15): a listing that failed is not a club
      // with no phones, and would have let the ceiling be passed.
      const all = await this.store.listItems(club, PUSH_CHANNEL);
      const count = all.reduce((n, item) => n + (isObject(item.doc.body) && Array.isArray(item.doc.body["subs"]) ? item.doc.body["subs"].length : 0), 0);

      if (count >= PUSH_SUBS_PER_CLUB) {
        return "full";
      }
    }

    const others = mine.filter((s) => s.endpoint !== sub.endpoint);
    await this.keep(club, id, [...others, sub].slice(-PUSH_SUBS_PER_DRIVER));
    return "ok";
  }

  /** Takes one phone off a driver's alerts. */
  async unsubscribe(club: string, id: string, endpoint: string): Promise<void> {
    if (!DRIVER_ID.test(id)) {
      return;
    }

    await this.withLock(club, async () => {
      const mine = await this.subsOf(club, id);
      const left = mine.filter((s) => s.endpoint !== endpoint);

      if (left.length !== mine.length) {
        await this.keep(club, id, left);
      }
    });
  }

  /** Every driver of the club with a phone signed up. */
  private async signedUp(club: string): Promise<Set<string>> {
    const all = await this.store.listItems(club, PUSH_CHANNEL).catch(() => []);
    return new Set(all.map((item) => item.id).filter((id) => DRIVER_ID.test(id)));
  }

  /**
   * The laptop's live document has just been written over: whatever it
   * now says that a driver signed up should hear. Never throws, and is not
   * waited for - the laptop's publish has already been answered.
   */
  async afterLive(club: string, before: unknown, after: unknown): Promise<void> {
    try {
      const ids = await this.signedUp(club);

      if (ids.size === 0) {
        return;
      }

      const was = isObject(before) && isObject(before["drivers"]) ? before["drivers"] : {};
      const now = isObject(after) && isObject(after["drivers"]) ? after["drivers"] : {};
      const beforeAt = isObject(before) && typeof before["at"] === "string" ? Date.parse(before["at"]) : undefined;
      const afterAt = isObject(after) && typeof after["at"] === "string" ? Date.parse(after["at"]) : this.now().getTime();

      for (const id of ids) {
        for (const message of onceSoon(this.soonSent, `${club}|${id}`, now[id], afterAt, liveAlerts(was[id], now[id], myNightUrl(club), beforeAt, afterAt))) {
          await this.send(club, id, message);
        }
      }
    } catch (e) {
      this.log(`[push] "${club}": could not work out the live alerts: ${String(e)}`);
    }
  }

  /** A driver's night has just been written over: a result, if there is a new race on it. */
  async afterNight(club: string, id: string, before: unknown, after: unknown): Promise<void> {
    try {
      if (!DRIVER_ID.test(id) || !(await this.signedUp(club)).has(id)) {
        return;
      }

      for (const message of nightAlerts(before, after, myNightUrl(club))) {
        await this.send(club, id, message);
      }
    } catch (e) {
      this.log(`[push] "${club}": could not work out a result alert: ${String(e)}`);
    }
  }

  /**
   * "Videos from tonight are up" (14 Nov 2026, race video plan V2): once a
   * night to each signed-up driver who raced in a race with video. night
   * names the night (the stream's first piece); messageFor reads the
   * driver's own races and answers undefined while none has video yet, so
   * a driver whose race is in a later piece still hears once it is up.
   */
  async afterVideos(club: string, night: string, messageFor: (id: string) => Promise<PushMessage | undefined>): Promise<void> {
    try {
      // Who has had it tonight, kept in the club's private "videoalerts"
      // snapshot too (look-back, 14 Nov 2026): a relay restarted mid-night
      // must not send it to everybody again.
      const kept = (await this.store.getSnapshot(club, "videoalerts").catch(() => undefined))?.body;
      const sent = new Set(isObject(kept) && kept["night"] === night && Array.isArray(kept["sent"]) ? (kept["sent"] as unknown[]).filter((s): s is string => typeof s === "string") : []);
      let added = false;

      for (const id of await this.signedUp(club)) {
        const key = `${club}|${id}`;
        if (this.videoSent.get(key) === night || sent.has(id)) continue;
        const message = await messageFor(id);
        if (message === undefined) continue;
        this.videoSent.set(key, night);
        sent.add(id);
        added = true;
        await this.send(club, id, message, BROADCAST_TTL_SECONDS);
      }

      if (added) {
        const at = this.now();
        await this.store.putSnapshot(club, "videoalerts", { seq: at.getTime(), publishedAt: at.toISOString(), body: { night, sent: [...sent].slice(-PUSH_SUBS_PER_CLUB) } });
      }
    } catch (e) {
      this.log(`[push] "${club}": could not send the race video alerts: ${String(e)}`);
    }
  }

  /**
   * One message to every phone a driver signed up. A phone the push service
   * says is gone is forgotten. ttlSeconds is how long it may wait for a
   * phone that is off: ten minutes unless said, never more than a day.
   */
  async send(club: string, id: string, message: PushMessage, ttlSeconds: number = PUSH_TTL_SECONDS): Promise<number> {
    return (await this.deliver(club, id, message, ttlSeconds)).sent;
  }

  /**
   * One message to every driver of the club with a phone signed up (12 Nov
   * 2026, setup plan step 7): the laptop calling the next race night off.
   *
   * WHY it reaches only some phones: sign-ups are forgotten when the club
   * closes drivers' nights (Relay.forgetPushSubs), so this goes to the
   * phones signed up while nights are open - not to every driver the club
   * has ever had. Accepted: a phone is only ever signed up for a night.
   *
   * badges: drivers tried. sent: phones the push services took. failed:
   * phones refused, gone or out of reach, plus one for each driver whose
   * phones could not be read this time. One driver failing never stops
   * the rest.
   */
  async sendAll(
    club: string,
    message: PushMessage,
    ttlSeconds: number = BROADCAST_TTL_SECONDS,
  ): Promise<{ badges: number; sent: number; failed: number }> {
    const ids = await this.signedUp(club);
    let sent = 0;
    let failed = 0;

    for (const id of ids) {
      try {
        const done = await this.deliver(club, id, message, ttlSeconds);
        sent += done.sent;
        failed += done.tried - done.sent;
      } catch (e) {
        failed++;
        this.log(`[push] "${club}": could not send ${message.tag} to one driver: ${String(e)}`);
      }
    }

    return { badges: ids.size, sent, failed };
  }

  /** send, also saying how many phones were tried. */
  private async deliver(club: string, id: string, message: PushMessage, ttlSeconds: number, channel: string = PUSH_CHANNEL): Promise<{ tried: number; sent: number }> {
    const ttl = Number.isFinite(ttlSeconds)
      ? Math.min(Math.max(Math.floor(ttlSeconds), 0), PUSH_TTL_MAX_SECONDS)
      : PUSH_TTL_SECONDS;
    // Only the phones that want this kind (14 Nov 2026, item 9).
    const kind = alertKindOf(message.tag);
    const subs = (await this.subsOf(club, id, channel)).filter((s) => s.kinds === undefined || s.kinds.includes(kind));

    if (subs.length === 0) {
      return { tried: 0, sent: 0 };
    }

    // Where a tap goes, saying which kind of alert it came from so the page
    // can count it (pagecounts.ts) - and the club's Home, not the relay's
    // own front page, for an alert that names no page of its own.
    const base = typeof message.url === "string" && message.url.startsWith("/") ? message.url : `/c/${encodeURIComponent(club)}/`;
    const from = message.tag === "hc-soon" ? "soon" : kind === "news" ? "nextnight" : kind;
    message = { ...message, url: `${base}${base.includes("?") ? "&" : "?"}from=alert-${from}` };

    const keys = await this.vapid();
    const payload = Buffer.from(JSON.stringify(message));
    const gone: string[] = [];
    let sent = 0;

    for (const sub of subs) {
      try {
        const answer = await this.post(sub.endpoint, {
          method: "POST",
          headers: {
            Authorization: vapidHeader(keys, sub.endpoint, this.subject, this.now()),
            "Content-Encoding": "aes128gcm",
            "Content-Type": "application/octet-stream",
            TTL: String(ttl),
            Urgency: "high",
            // No Topic header (7 Nov 2026): Apple refused every alert with
            // 400 {"reason":"BadWebPushTopic"} for "hc-race", which the
            // standard allows. Nothing is lost - the tag inside the
            // message already has a newer alert replace the older one on
            // the phone; Topic only folded alerts still queued unsent.
          },
          body: encryptForPush(sub.label ? Buffer.from(JSON.stringify({ ...message, title: `${sub.label}: ${message.title}` })) : payload, sub),
        });

        if (answer.status === 404 || answer.status === 410) {
          gone.push(sub.endpoint);
        } else if (answer.status >= 200 && answer.status < 300) {
          sent++;
        } else {
          this.log(`[push] "${club}": ${new URL(sub.endpoint).hostname} answered ${answer.status}${answer.reason ? ` ${answer.reason}` : ""}`);
        }
      } catch (e) {
        this.log(`[push] "${club}": could not reach ${new URL(sub.endpoint).hostname}: ${String(e)}`);
      }
    }

    // The list read again, inside the lock: what was read before the posts
    // may since have gained a phone, or gone with the club.
    // A list that could not be read this time is left as it is: the gone
    // phone is forgotten on a later alert instead.
    if (gone.length > 0) {
      await this.withLock(club, async () => {
        const now = await this.subsOf(club, id, channel);
        const left = now.filter((s) => !gone.includes(s.endpoint));

        if (left.length !== now.length) {
          const kept = channel === FOLLOW_CHANNEL ? await this.store.getItem(club, channel, id).catch(() => undefined) : undefined;
          await this.keep(club, id, left, channel, kept && isObject(kept.body) ? { name: kept.body["name"] } : {});
        }
      }).catch((e: unknown) => this.log(`[push] "${club}": could not forget a gone phone: ${String(e)}`));
    }

    // Accepted ones logged too (7 Nov 2026): with only refusals in the log,
    // "no alert came" could not be told apart from "none was sent". The
    // kind of alert (its tag) and a count - no name, no race.
    if (sent > 0) {
      this.log(`[push] "${club}": ${message.tag} accepted for ${sent} phone${sent === 1 ? "" : "s"}`);
    }

    return { tried: subs.length, sent };
  }
}

/** Where a tap on an alert goes: the club's "my night", which opens the night this phone remembers. No badge code in it. */
export function myNightUrl(club: string): string {
  return `/c/${encodeURIComponent(club)}/mynight`;
}

/**
 * The service worker, served at /c/<club>/sw.js so it covers that club's
 * pages and no other club's. It shows what a push brings and opens the
 * page on a tap.
 *
 * THE PAGES WITH NO SIGNAL (14 Nov 2026, driver phone plan item 1). It
 * also keeps the club's pages for a phone that loses its signal - a race
 * night in a metal building, where the worst signal comes exactly when
 * most people are checking. What it keeps, and what it never does:
 *
 *  - The club's own pages (Home, Live, Schedule, Results and its tabs,
 *    Tracks, Me, check-in, the one-bar app): network first, and the copy
 *    from the last visit when the network is gone or takes over three
 *    seconds. Kept by address, without the query, so one copy a page.
 *  - The icon and the logo, which are versioned (?v=): the copy first.
 *  - NEVER a driver's own night or garage, a sign-up, Manage, anything
 *    under /api/ (the pages keep their own last answer, and private
 *    answers stay out of a shared cache), or anything but a GET.
 *  - Never another club's address: only paths under this worker's scope.
 *
 * The cache is named after the build (PAGES_BUILD, filled in when it is
 * served), so a deploy throws the old pages away the moment the new worker
 * takes over. A page never opened on this phone answers a short page of
 * its own saying so, rather than the browser's "no internet".
 */
const SERVICE_WORKER_TEMPLATE = `'use strict';
const HC_BUILD = '__HC_BUILD__';
const HC_CACHE = 'hc-pages-' + HC_BUILD;
const HC_PAGES = ['', 'live', 'schedule', 'results', 'standings', 'badges', 'tracks', 'mynight', 'checkin', 'app'];
const HC_STATIC = ['icon.png', 'logo.png'];
const HC_WAIT_MS = 3000;
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (e) => e.waitUntil(Promise.all([
  self.clients.claim(),
  typeof caches === 'undefined' ? null : caches.keys()
    .then((keys) => Promise.all(keys.filter((k) => k.indexOf('hc-pages-') === 0 && k !== HC_CACHE).map((k) => caches.delete(k))))
    .catch(() => null),
])));
// What a request is to this worker: a club page, a versioned picture, or
// nothing (left to the network as if there were no worker).
function hcRoute(req) {
  if (!req || req.method !== 'GET') return null;
  let u, scope;
  try { u = new URL(req.url); scope = new URL(self.registration.scope); } catch (e) { return null; }
  if (u.origin !== scope.origin) return null;
  const base = scope.pathname.replace(/\\/$/, '');
  if (u.pathname !== base && u.pathname.indexOf(base + '/') !== 0) return null;
  const rest = u.pathname.slice(base.length).replace(/^\\//, '').replace(/\\/$/, '');
  if (HC_PAGES.indexOf(rest) >= 0) return { page: true, key: u.origin + base + (rest === '' ? '/' : '/' + rest) };
  if (HC_STATIC.indexOf(rest) >= 0 && /^\\?v=[0-9A-Za-z_-]{1,64}$/.test(u.search)) return { page: false, key: u.href };
  return null;
}
function hcNotHere() {
  const html = '<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">'
    + '<title>No signal</title><body style="margin:0;padding:32px 20px;font:17px/1.5 system-ui,sans-serif;background:#0F0722;color:#F7F2FF">'
    + '<h1 style="font-size:1.4rem">No signal</h1><p>This page hasn\\u2019t been opened on this phone yet, so there\\u2019s no copy of it to show.</p>'
    + '<p>It opens as soon as the phone is back online.</p><p><a href="" style="color:#2EE6FF">Try again</a></p></body>';
  return new Response(html, { status: 503, headers: { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' } });
}
// Network first; the kept copy when the network fails or is slow; the
// network's answer kept for next time whenever it comes.
function hcPage(req, key) {
  return caches.open(HC_CACHE).then((cache) => new Promise((resolve) => {
    let done = false;
    const give = (res) => { if (!done && res) { done = true; resolve(res); } };
    const kept = () => cache.match(key).catch(() => undefined);
    const slow = setTimeout(() => { kept().then(give); }, HC_WAIT_MS);
    fetch(req).then((res) => {
      clearTimeout(slow);
      if (res && res.ok && res.type === 'basic') cache.put(key, res.clone()).catch(() => null);
      give(res);
    }).catch(() => {
      clearTimeout(slow);
      kept().then((hit) => give(hit || hcNotHere()));
    });
  })).catch(() => fetch(req));
}
function hcStatic(req, key) {
  return caches.open(HC_CACHE).then((cache) => cache.match(key).then((hit) => hit || fetch(req).then((res) => {
    if (res && res.ok && res.type === 'basic') cache.put(key, res.clone()).catch(() => null);
    return res;
  }))).catch(() => fetch(req));
}
self.addEventListener('fetch', (e) => {
  if (typeof caches === 'undefined') return;
  const route = hcRoute(e.request);
  if (route === null) return;
  e.respondWith(route.page ? hcPage(e.request, route.key) : hcStatic(e.request, route.key));
});`;

/** The service worker for one build of the pages (see PAGES_BUILD in pagebuild.ts). */
export function serviceWorkerFor(build: string): string {
  return SERVICE_WORKER_TEMPLATE.replace("__HC_BUILD__", /^[0-9a-z]{1,40}$/.test(build) ? build : "dev") + SERVICE_WORKER_REST;
}

const SERVICE_WORKER_REST = `
self.addEventListener('push', (e) => {
  let m = {};
  try { m = e.data ? e.data.json() : {}; } catch (err) { m = {}; }
  const title = typeof m.title === 'string' && m.title ? m.title : 'HeatCaller';
  e.waitUntil(self.registration.showNotification(title, {
    body: typeof m.body === 'string' ? m.body : '',
    tag: typeof m.tag === 'string' ? m.tag : 'hc',
    renotify: true,
    vibrate: [250, 120, 250, 120, 400],
    data: { url: typeof m.url === 'string' && m.url.charAt(0) === '/' ? m.url : '/' },
  }));
});
self.addEventListener('notificationclick', (e) => {
  e.notification.close();
  const url = (e.notification.data && e.notification.data.url) || '/';
  // An open club page is taken to the alert's own page too (10 Nov 2026,
  // bug hunt 16): only focused, a phone left on Live or Results came back
  // to that page and the driver never saw the race the alert was about.
  // navigate() needs a page this worker controls; one it does not is left
  // as it is and the alert's page opens in a new window instead.
  const target = new URL(url, self.registration.scope).href;
  e.waitUntil(self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((all) => {
    for (const c of all) {
      if (c.url.indexOf(self.registration.scope) === 0 && 'focus' in c) {
        return c.focus().then((w) => {
          const page = w || c;
          if (page.url === target || !('navigate' in page)) return page;
          return Promise.resolve(page.navigate(target)).catch(() => self.clients.openWindow(target));
        });
      }
    }
    return self.clients.openWindow(target);
  }));
});
`;

/** The worker as served before there was a build to name its cache after; what the tests run. */
export const SERVICE_WORKER_JS = serviceWorkerFor("dev");
