import { createECDH, createPrivateKey, generateKeyPairSync, hkdfSync, randomBytes, sign, createCipheriv, type KeyObject } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname } from "node:path";
import type { Store } from "./store.js";

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

/** Phones per driver. A new one past this pushes out the oldest. */
export const PUSH_SUBS_PER_DRIVER = 5;

/** Sign-ups per club, all drivers together - a ceiling, not a plan. */
export const PUSH_SUBS_PER_CLUB = 2000;

/** How long a message waits at the push service for a phone that is off. A race call is stale in ten minutes. */
const PUSH_TTL_SECONDS = 600;

const SEND_TIMEOUT_MS = 10000;

/** One phone's sign-up, as the browser gave it. */
export interface PushSub {
  readonly endpoint: string;
  /** The phone's public key, base64url - 65 bytes, uncompressed P-256. */
  readonly p256dh: string;
  /** The phone's auth secret, base64url - 16 bytes. */
  readonly auth: string;
  readonly since: string;
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
export function liveAlerts(before: unknown, after: unknown, url: string): PushMessage[] {
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

  return out;
}

function racesOf(night: unknown): unknown[] {
  return isObject(night) && Array.isArray(night["races"]) ? night["races"] : [];
}

/** A race of theirs just filed on their night: "Your result is in". Nothing when the night has no more races than before. */
export function nightAlerts(before: unknown, after: unknown, url: string): PushMessage[] {
  const was = racesOf(before).length;
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
    body: [label, out || placed].filter((s) => s !== "").join(" - "),
    tag: "hc-result",
    url,
  }];
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
      return { status: r.status };
    });
  }

  /** The relay's key pair: read from keyFile, or made and written there the first time. */
  async vapid(): Promise<VapidKeys> {
    if (this.keys) {
      return this.keys;
    }

    this.loading ??= (async () => {
      let jwk: { x: string; y: string; d: string } | undefined;

      if (this.keyFile !== undefined) {
        try {
          const kept = JSON.parse(await readFile(this.keyFile, "utf8")) as { x?: unknown; y?: unknown; d?: unknown };
          if (typeof kept.x === "string" && typeof kept.y === "string" && typeof kept.d === "string") {
            jwk = { x: kept.x, y: kept.y, d: kept.d };
          }
        } catch {
          // Not made yet.
        }
      }

      if (jwk === undefined) {
        jwk = freshVapidJwk();

        if (this.keyFile !== undefined) {
          await mkdir(dirname(this.keyFile), { recursive: true });
          await writeFile(this.keyFile, JSON.stringify(jwk), { mode: 0o600 });
          this.log("[push] made the relay's push key");
        }
      }

      this.keys = vapidFromJwk(jwk);
      return this.keys;
    })();

    return this.loading;
  }

  /** The public key the page hands the browser, base64url. */
  async publicKey(): Promise<string> {
    return b64url((await this.vapid()).publicRaw);
  }

  private async subsOf(club: string, id: string): Promise<PushSub[]> {
    const doc = await this.store.getItem(club, PUSH_CHANNEL, id).catch(() => undefined);
    const list = doc && isObject(doc.body) && Array.isArray(doc.body["subs"]) ? doc.body["subs"] : [];
    return list.filter((s): s is PushSub => isObject(s) && typeof s["endpoint"] === "string" && typeof s["p256dh"] === "string" && typeof s["auth"] === "string") as PushSub[];
  }

  private async keep(club: string, id: string, subs: readonly PushSub[]): Promise<void> {
    if (subs.length === 0) {
      await this.store.deleteItem(club, PUSH_CHANNEL, id).catch(() => undefined);
      return;
    }

    await this.store.putItem(club, PUSH_CHANNEL, id, { seq: 0, publishedAt: this.now().toISOString(), body: { subs } });
  }

  /** Signs a phone up for a driver's alerts. "full" when the club has reached its ceiling. */
  async subscribe(club: string, id: string, sub: PushSub): Promise<"ok" | "full"> {
    if (!DRIVER_ID.test(id)) {
      return "full";
    }

    const mine = await this.subsOf(club, id);

    if (!mine.some((s) => s.endpoint === sub.endpoint)) {
      const all = await this.store.listItems(club, PUSH_CHANNEL).catch(() => []);
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

    const mine = await this.subsOf(club, id);
    const left = mine.filter((s) => s.endpoint !== endpoint);

    if (left.length !== mine.length) {
      await this.keep(club, id, left);
    }
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

      for (const id of ids) {
        for (const message of liveAlerts(was[id], now[id], myNightUrl(club))) {
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

  /** One message to every phone a driver signed up. A phone the push service says is gone is forgotten. */
  async send(club: string, id: string, message: PushMessage): Promise<number> {
    const subs = await this.subsOf(club, id);

    if (subs.length === 0) {
      return 0;
    }

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
            TTL: String(PUSH_TTL_SECONDS),
            Urgency: "high",
            Topic: message.tag.replace(/[^A-Za-z0-9_-]/g, ""),
          },
          body: encryptForPush(payload, sub),
        });

        if (answer.status === 404 || answer.status === 410) {
          gone.push(sub.endpoint);
        } else if (answer.status >= 200 && answer.status < 300) {
          sent++;
        } else {
          this.log(`[push] "${club}": ${new URL(sub.endpoint).hostname} answered ${answer.status}`);
        }
      } catch (e) {
        this.log(`[push] "${club}": could not reach ${new URL(sub.endpoint).hostname}: ${String(e)}`);
      }
    }

    if (gone.length > 0) {
      await this.keep(club, id, subs.filter((s) => !gone.includes(s.endpoint)));
    }

    return sent;
  }
}

/** Where a tap on an alert goes: the club's "my night", which opens the night this phone remembers. No badge code in it. */
function myNightUrl(club: string): string {
  return `/c/${encodeURIComponent(club)}/mynight`;
}

/**
 * The service worker, served at /c/<club>/sw.js so it covers that club's
 * pages and no other club's. It only shows what a push brings and opens
 * the page on a tap: no fetch handler, so it caches nothing and changes
 * nothing about how the pages load.
 */
export const SERVICE_WORKER_JS = `'use strict';
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (e) => e.waitUntil(self.clients.claim()));
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
  e.waitUntil(self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((all) => {
    for (const c of all) {
      if (c.url.indexOf(self.registration.scope) === 0 && 'focus' in c) return c.focus();
    }
    return self.clients.openWindow(url);
  }));
});
`;
