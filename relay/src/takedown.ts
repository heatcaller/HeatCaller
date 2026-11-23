import { randomBytes } from "node:crypto";
import { lchown, lstat, mkdir, readFile, readdir, rename, rm, stat } from "node:fs/promises";
import { join, relative as relativePath } from "node:path";
import { CLIP_DIR, CLIP_ID } from "./clips.js";
import { CREW_SAMPLE_DIR, CREW_SAMPLE_KEY } from "./crewsamples.js";
import type { Store } from "./store.js";
import { isValidClubId } from "./tenancy.js";
import { mergedStreamBody, raceVideo, STREAM_MAX_PIECES, STREAM_NIGHTS_KEPT, streamFrom, YOUTUBE_ID } from "./video.js";
import { writeFileAtomically } from "./voice.js";

/**
 * ONE ITEM TAKEN DOWN AFTER A COPYRIGHT NOTICE (23 Nov 2026, asked for by
 * Asher - website/legal-drafts/copyright-takedown-policy.md).
 *
 * WHY: the safe harbour (17 U.S.C. 512) asks us to remove or disable access
 * to the one thing a notice names, promptly. Until now the relay could
 * only delete a whole club, which is far more than a notice about one
 * sponsor's picture asks for, and which the club's laptop would simply
 * publish again ten minutes later.
 *
 * So nothing the laptop sent is rewritten. A record per club, here, names
 * what is down; every route that serves one of those things leaves it out
 * as it serves it - before anything is cached - and restoring it after a
 * counter-notice is just marking the record restored. The laptop goes on
 * publishing exactly as before: a refused publish would stall its whole
 * queue, which is a worse night for the club than a missing picture.
 *
 * The records live beside the clubs, not in a club's folder, so the club's
 * own Delete Everything Published cannot bring a taken-down item back by
 * deleting and publishing again.
 *
 * Two things are files rather than parts of a document - a driver's race
 * clip and a crew voice sample - and those are moved into a held folder
 * here (not deleted), so a restore can put them back.
 *
 * Written only by the admin commands (takedownadmin.ts), over fly ssh, as
 * root; read only by the running relay (TakedownBook below), which notices
 * a changed file within a couple of seconds. No complainant's details are
 * kept: only the short notice reference the admin typed.
 */

/** The folder under dataDir, with a dot so no club id can name it. */
export const TAKEDOWN_DIR = ".takedowns";

/** Where taken-down files wait for a restore: <dataDir>/.takedowns/held/<club>/<kind>/. */
export const TAKEDOWN_HELD_DIR = "held";

/** What can be taken down, by the key that names it. */
export const TAKEDOWN_KINDS = ["logo", "sponsor", "track", "video", "clip", "crewsample", "garagephoto", "badge"] as const;

export type TakedownKind = (typeof TAKEDOWN_KINDS)[number];

/** The longest notice reference and restore reason kept. A reference, not the notice. */
export const TAKEDOWN_NOTICE_MAX = 120;
export const TAKEDOWN_REASON_MAX = 300;

/** Three valid notices not countered in twelve months is the policy's repeat-infringer line. Flagged only, never acted on. */
export const TAKEDOWN_REPEAT_AT = 3;

/** How often the running relay looks at a club's file, at most. */
export const TAKEDOWN_CHECK_MS = 2_000;

const GARAGE_PHOTO = /^[0-9a-f]{12}\.jpg$/;

export interface TakedownRecord {
  /** "td-" and twelve hex digits. */
  readonly id: string;
  readonly kind: TakedownKind;
  /** The item's key: "" for the logo, the sponsor's id, the track's name, the video's id, and so on. */
  readonly ref: string;
  /** The admin's short reference for the notice, e.g. "DMCA 2026-014". Never the complainant's details. */
  readonly notice: string;
  readonly state: "active" | "restored";
  readonly takenDownAt: string;
  readonly restoredAt: string | null;
  readonly restoreReason: string | null;
}

export interface TakedownFile {
  readonly version: 1;
  readonly club: string;
  readonly takedowns: readonly TakedownRecord[];
}

/** What is down for one club right now, by kind. */
export interface ActiveTakedowns {
  readonly logo: boolean;
  readonly sponsors: ReadonlySet<string>;
  readonly tracks: ReadonlySet<string>;
  readonly videos: ReadonlySet<string>;
  readonly clips: ReadonlySet<string>;
  readonly crewSamples: ReadonlySet<string>;
  readonly garagePhotos: ReadonlySet<string>;
  readonly badges: ReadonlySet<string>;
  /** Whether anything at all is down - the routes skip the work when not. */
  readonly any: boolean;
}

export const NO_TAKEDOWNS: ActiveTakedowns = Object.freeze({
  logo: false,
  sponsors: new Set<string>(),
  tracks: new Set<string>(),
  videos: new Set<string>(),
  clips: new Set<string>(),
  crewSamples: new Set<string>(),
  garagePhotos: new Set<string>(),
  badges: new Set<string>(),
  any: false,
});

export function takedownPath(dataDir: string, club: string): string {
  if (!isValidClubId(club)) {
    throw new Error(`Refusing to touch takedowns for an invalid club id: ${JSON.stringify(club)}`);
  }

  return join(dataDir, TAKEDOWN_DIR, `${club}.json`);
}

export function heldFolder(dataDir: string, club: string, kind: "clip" | "crewsample"): string {
  if (!isValidClubId(club)) {
    throw new Error(`Refusing to touch takedowns for an invalid club id: ${JSON.stringify(club)}`);
  }

  return join(dataDir, TAKEDOWN_DIR, TAKEDOWN_HELD_DIR, club, kind);
}

/** One line of text, control characters out, trimmed and cut. */
export function oneLineOf(value: string, max: number): string {
  return value.replace(/[\u0000-\u001f\u007f]/g, " ").replace(/\s+/g, " ").trim().slice(0, max);
}

/**
 * A kind's key, checked and put in the one form it is stored in, or an
 * error in words. A YouTube address is accepted for a video and a photo's
 * id without ".jpg" for a garage photo, since that is what an admin copies.
 */
export function normaliseRef(kind: TakedownKind, raw: string | undefined): { ok: true; ref: string } | { ok: false; error: string } {
  const ref = oneLineOf(raw ?? "", 200);

  switch (kind) {
    case "logo":
      return ref === "" || ref === "logo" ? { ok: true, ref: "" } : { ok: false, error: "A club has one logo, so logo takes nothing after it." };
    case "sponsor":
    case "badge":
      return ref.length >= 1 && ref.length <= 100
        ? { ok: true, ref }
        : { ok: false, error: `Name the ${kind} by its id (takedown-find lists them).` };
    case "track":
      return ref.length >= 1 && ref.length <= 100
        ? { ok: true, ref }
        : { ok: false, error: "Name the track exactly as the Tracks page shows it." };
    case "video": {
      const id = youTubeIdOf(ref) ?? (YOUTUBE_ID.test(ref) ? ref : undefined);
      return id !== undefined ? { ok: true, ref: id } : { ok: false, error: "That is not a YouTube video id or address." };
    }
    case "clip": {
      const id = ref.toLowerCase().replace(/\.mp3$/, "");
      return CLIP_ID.test(id) ? { ok: true, ref: id } : { ok: false, error: "A clip's id is the 32 hex characters before .mp3 in its address." };
    }
    case "crewsample": {
      const key = ref.toLowerCase().replace(/\.mp3$/, "");
      return CREW_SAMPLE_KEY.test(key) ? { ok: true, ref: key } : { ok: false, error: "A crew voice sample's key is the 12 hex characters before .mp3 in its address." };
    }
    case "garagephoto": {
      const id = ref.toLowerCase().endsWith(".jpg") ? ref.toLowerCase() : `${ref.toLowerCase()}.jpg`;
      return GARAGE_PHOTO.test(id) ? { ok: true, ref: id } : { ok: false, error: "A garage photo's id is 12 hex characters, then .jpg." };
    }
  }
}

export function isTakedownKind(value: string): value is TakedownKind {
  return (TAKEDOWN_KINDS as readonly string[]).includes(value);
}

/** The video id in a YouTube address, or undefined when it is not one. */
export function youTubeIdOf(text: string): string | undefined {
  let url: URL;

  try {
    url = new URL(/^[a-z]+:\/\//i.test(text) ? text : `https://${text}`);
  } catch {
    return undefined;
  }

  const host = url.hostname.toLowerCase().replace(/^(www\.|m\.|music\.)/, "");
  const parts = url.pathname.split("/").filter((p) => p.length > 0);
  let id: string | undefined;

  if (host === "youtu.be") {
    id = parts[0];
  } else if (host === "youtube.com" || host === "youtube-nocookie.com") {
    id = url.searchParams.get("v") ?? (["embed", "live", "shorts", "v"].includes(parts[0] ?? "") ? parts[1] : undefined);
  }

  return id !== undefined && YOUTUBE_ID.test(id) ? id : undefined;
}

/** A file's records, field by field; anything that is not a record is left out. */
export function readTakedownBody(raw: unknown, club: string): TakedownFile {
  const list = typeof raw === "object" && raw !== null && Array.isArray((raw as Record<string, unknown>)["takedowns"])
    ? ((raw as Record<string, unknown>)["takedowns"] as unknown[])
    : [];
  const takedowns: TakedownRecord[] = [];

  for (const item of list) {
    if (typeof item !== "object" || item === null) continue;
    const r = item as Record<string, unknown>;
    const kind = typeof r["kind"] === "string" && isTakedownKind(r["kind"]) ? r["kind"] : undefined;
    const ref = kind === undefined || typeof r["ref"] !== "string" ? undefined : normaliseRef(kind, r["ref"]);

    if (kind === undefined || ref === undefined || !ref.ok || typeof r["id"] !== "string" || !/^td-[0-9a-f]{12}$/.test(r["id"])) continue;
    if (typeof r["takenDownAt"] !== "string" || Number.isNaN(Date.parse(r["takenDownAt"]))) continue;

    takedowns.push({
      id: r["id"],
      kind,
      ref: ref.ref,
      notice: typeof r["notice"] === "string" ? oneLineOf(r["notice"], TAKEDOWN_NOTICE_MAX) : "",
      state: r["state"] === "restored" ? "restored" : "active",
      takenDownAt: r["takenDownAt"],
      restoredAt: typeof r["restoredAt"] === "string" ? r["restoredAt"] : null,
      restoreReason: typeof r["restoreReason"] === "string" ? oneLineOf(r["restoreReason"], TAKEDOWN_REASON_MAX) : null,
    });
  }

  return { version: 1, club, takedowns };
}

/** What is down, from a club's records. */
export function activeOf(records: readonly TakedownRecord[]): ActiveTakedowns {
  const live = records.filter((r) => r.state === "active");

  if (live.length === 0) {
    return NO_TAKEDOWNS;
  }

  const of = (kind: TakedownKind) => new Set(live.filter((r) => r.kind === kind).map((r) => r.ref));

  return {
    logo: live.some((r) => r.kind === "logo"),
    sponsors: of("sponsor"),
    tracks: of("track"),
    videos: of("video"),
    clips: of("clip"),
    crewSamples: of("crewsample"),
    garagePhotos: of("garagephoto"),
    badges: of("badge"),
    any: true,
  };
}

/**
 * The policy's count for one club: notices taken down in the last twelve
 * months and not countered (restored). Counted by notice, not by item - one
 * notice naming three pictures is one notice - with the reference compared
 * without case or spacing.
 */
export function takedownCounts(records: readonly TakedownRecord[], now: Date): { active: number; last12Months: number; repeat: boolean } {
  const since = new Date(now.getTime());
  since.setUTCFullYear(since.getUTCFullYear() - 1);
  const notices = new Set(
    records
      .filter((r) => r.state === "active" && Date.parse(r.takenDownAt) > since.getTime())
      .map((r) => (r.notice === "" ? r.id : r.notice.toLowerCase().replace(/\s+/g, " ").trim())),
  );

  return {
    active: records.filter((r) => r.state === "active").length,
    last12Months: notices.size,
    repeat: notices.size >= TAKEDOWN_REPEAT_AT,
  };
}

// ------------------------------------------------------- serving it down

function plain(value: unknown): Record<string, unknown> | undefined {
  return typeof value === "object" && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : undefined;
}

/**
 * A snapshot's body with what is down taken out, or the same body when
 * nothing of it is. Only the site, sponsors, tracks and badges documents
 * carry anything that can be taken down; every other channel passes through.
 *
 *   site      the logo (and its plate) gone - the pages then show no logo
 *   sponsors  the whole sponsor gone, not only its picture: the notice is
 *             about what people see, which is the sponsor's tile - name,
 *             line and picture together - and leaving a tile up with a
 *             hole in it is not obviously "removed"
 *   tracks    the layout's picture gone; its name, records and laps stay
 *   badges    the achievement's artwork gone; its name and who won it stay
 */
export function withoutTakenDown(channel: string, body: unknown, active: ActiveTakedowns): unknown {
  if (!active.any) {
    return body;
  }

  const doc = plain(body);

  if (doc === undefined) {
    return body;
  }

  if (channel === "site" && active.logo && ("logo" in doc || "logoPlate" in doc)) {
    const { logo: _logo, logoPlate: _plate, ...rest } = doc;
    return rest;
  }

  if (channel === "sponsors" && active.sponsors.size > 0 && Array.isArray(doc["sponsors"])) {
    return { ...doc, sponsors: (doc["sponsors"] as unknown[]).filter((s) => !active.sponsors.has(String(plain(s)?.["id"] ?? ""))) };
  }

  if (channel === "tracks" && active.tracks.size > 0 && Array.isArray(doc["tracks"])) {
    return {
      ...doc,
      tracks: (doc["tracks"] as unknown[]).map((t) => {
        const track = plain(t);
        if (track === undefined || !active.tracks.has(oneLineOf(String(track["name"] ?? ""), 200))) return t;
        const { image: _image, ...rest } = track;
        return rest;
      }),
    };
  }

  if (channel === "badges" && active.badges.size > 0 && Array.isArray(doc["badges"])) {
    return {
      ...doc,
      badges: (doc["badges"] as unknown[]).map((b) => {
        const badge = plain(b);
        if (badge === undefined || !active.badges.has(String(badge["id"] ?? ""))) return b;
        const { art: _art, ...rest } = badge;
        return rest;
      }),
    };
  }

  return body;
}

/** A driver's garage with any taken-down car photo out of every car (and not its cover). */
export function garageWithoutTakenDown(body: unknown, active: ActiveTakedowns): unknown {
  const doc = plain(body);

  if (!active.any || active.garagePhotos.size === 0 || doc === undefined || !Array.isArray(doc["cars"])) {
    return body;
  }

  return {
    ...doc,
    cars: (doc["cars"] as unknown[]).map((c) => {
      const car = plain(c);
      if (car === undefined || !Array.isArray(car["photos"])) return c;
      const photos = (car["photos"] as unknown[]).filter((p) => !active.garagePhotos.has(String(plain(p)?.["id"] ?? "").toLowerCase()));
      const coverDown = typeof car["cover"] === "string" && active.garagePhotos.has(car["cover"].toLowerCase());
      const { cover: _cover, ...rest } = car;
      return coverDown ? { ...rest, photos } : { ...car, photos };
    }),
  };
}

// ------------------------------------------------- the running relay's copy

/**
 * The running relay's view of every club's takedowns, read from the files
 * the admin commands write.
 *
 * There is no channel between the two processes but the disk, so a club's
 * file is looked at (stat only) at most every TAKEDOWN_CHECK_MS when a read
 * for that club asks, and read again only when it has changed. `changed`
 * tells the relay to throw away what it holds of the club, so nothing
 * cached before the change goes on being served.
 *
 * A file that is there but will not read keeps the last set that did, and
 * says so in the log: an empty set would quietly put everything back up.
 */
export class TakedownBook {
  private readonly held = new Map<string, { active: ActiveTakedowns; mark: string | undefined; checkedAt: number }>();
  private readonly loads = new Map<string, Promise<{ active: ActiveTakedowns; changed: boolean }>>();

  constructor(
    readonly dataDir: string,
    private readonly options: { readonly clock?: () => number; readonly checkEveryMs?: number; readonly log?: (line: string) => void } = {},
  ) {}

  private clock(): number {
    return (this.options.clock ?? Date.now)();
  }

  /** What is down for the club, and whether that changed since the last look. */
  async of(club: string): Promise<{ active: ActiveTakedowns; changed: boolean }> {
    const was = this.held.get(club);
    const every = this.options.checkEveryMs ?? TAKEDOWN_CHECK_MS;

    if (was !== undefined && this.clock() - was.checkedAt < every) {
      return { active: was.active, changed: false };
    }

    const running = this.loads.get(club);

    if (running !== undefined) {
      // Somebody else is looking: the same answer, change and all. Each
      // caller that hears "changed" drops the club's caches itself, so
      // none can serve from a cache the other has not dropped yet; a
      // second drop costs one read.
      return running;
    }

    const load = this.look(club).finally(() => this.loads.delete(club));
    this.loads.set(club, load);
    return load;
  }

  private async look(club: string): Promise<{ active: ActiveTakedowns; changed: boolean }> {
    const was = this.held.get(club);
    const path = takedownPath(this.dataDir, club);
    let mark: string | undefined;

    try {
      const info = await stat(path);
      mark = `${info.mtimeMs}:${info.size}:${info.ino}`;
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== "ENOENT") {
        this.options.log?.(`[takedown] "${club}": could not look at its takedown file (${String(error)}); keeping what was known.`);
        const active = was?.active ?? NO_TAKEDOWNS;
        this.held.set(club, { active, mark: was?.mark, checkedAt: this.clock() });
        return { active, changed: false };
      }
    }

    if (was !== undefined && mark === was.mark) {
      this.held.set(club, { ...was, checkedAt: this.clock() });
      return { active: was.active, changed: false };
    }

    let active: ActiveTakedowns = NO_TAKEDOWNS;

    if (mark !== undefined) {
      try {
        active = activeOf(readTakedownBody(JSON.parse(await readFile(path, "utf8")) as unknown, club).takedowns);
      } catch (error) {
        this.options.log?.(`[takedown] "${club}": its takedown file will not read (${String(error)}); keeping what was known.`);
        // The mark is not kept, so the next look tries again.
        const kept = was?.active ?? NO_TAKEDOWNS;
        this.held.set(club, { active: kept, mark: was?.mark, checkedAt: this.clock() });
        return { active: kept, changed: false };
      }
    }

    this.held.set(club, { active, mark, checkedAt: this.clock() });
    // A first look counts as a change only when something is down: there
    // is nothing cached of the club from before to throw away otherwise.
    return { active, changed: was === undefined ? active.any : true };
  }

  /** The held files of a club's taken-down clips and samples - when the club deletes everything. The records stay. */
  async wipeHeld(club: string): Promise<void> {
    await rm(join(this.dataDir, TAKEDOWN_DIR, TAKEDOWN_HELD_DIR, club), { recursive: true, force: true });
  }
}

// ----------------------------------------------------- the admin's side

export type Owner = { readonly uid: number; readonly gid: number } | undefined;

/** The club's records; none for a club with no file. Throws when the file is there and will not read. */
export async function readTakedowns(dataDir: string, club: string): Promise<TakedownFile> {
  let text: string;

  try {
    text = await readFile(takedownPath(dataDir, club), "utf8");
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === "ENOENT") {
      return { version: 1, club, takedowns: [] };
    }

    throw error;
  }

  // Not JSON is an error, never "no takedowns": writing over it would put
  // every item it named back up.
  return readTakedownBody(JSON.parse(text) as unknown, club);
}

/** Makes a folder (and its parents under dataDir) and hands each to the relay's user - see voice-topup. */
async function folderFor(dataDir: string, path: string, owner: Owner): Promise<void> {
  await mkdir(path, { recursive: true, mode: 0o700 });

  if (owner === undefined) {
    return;
  }

  // Every folder from dataDir down: mkdir -p may have made any of them,
  // and one only root can enter hides everything under it from the relay.
  const relative = relativePath(dataDir, path).split(/[\\/]/).filter((p) => p.length > 0 && p !== "..");
  let at = dataDir;

  for (const part of relative) {
    at = join(at, part);
    await lchown(at, owner.uid, owner.gid);
  }
}

/**
 * Written whole, to a temporary name and moved into place, so the running
 * relay never reads half a file - and handed to the relay's user first,
 * because over fly ssh this runs as root and the relay does not (a file
 * only root can read is a takedown the relay never sees).
 */
export async function writeTakedowns(dataDir: string, file: TakedownFile, owner: Owner): Promise<void> {
  const path = takedownPath(dataDir, file.club);
  await folderFor(dataDir, join(dataDir, TAKEDOWN_DIR), owner);
  await writeFileAtomically(path, Buffer.from(JSON.stringify(file, null, 2) + "\n", "utf8"), owner);
}

async function exists(path: string): Promise<boolean> {
  return lstat(path).then(() => true, () => false);
}

/** The files a clip or a crew sample is, where the relay serves them from and where they are held. */
function filesOf(dataDir: string, club: string, kind: "clip" | "crewsample", ref: string): { live: string; held: string }[] {
  if (kind === "clip") {
    return ["mp3", "json"].map((ext) => ({
      live: join(dataDir, CLIP_DIR, club, `${ref}.${ext}`),
      held: join(heldFolder(dataDir, club, "clip"), `${ref}.${ext}`),
    }));
  }

  return [{ live: join(dataDir, CREW_SAMPLE_DIR, club, `${ref}.mp3`), held: join(heldFolder(dataDir, club, "crewsample"), `${ref}.mp3`) }];
}

/** Moves each file that is there from one side to the other; true when at least one moved. */
async function move(dataDir: string, pairs: { from: string; to: string }[], owner: Owner): Promise<boolean> {
  let moved = false;

  for (const { from, to } of pairs) {
    if (!(await exists(from))) continue;
    await folderFor(dataDir, join(to, ".."), owner);
    await rename(from, to);
    moved = true;
  }

  return moved;
}

export type TakedownResult =
  | {
      readonly ok: true;
      readonly id: string;
      readonly club: string;
      readonly kind: TakedownKind;
      readonly ref: string;
      readonly notice: string;
      readonly state: "active";
      readonly takenDownAt: string;
      /** True when this target was already down: nothing new was recorded. */
      readonly already: boolean;
      /** For a clip or crew sample, whether its file was found and moved to the held folder; null for every other kind. */
      readonly held: boolean | null;
    }
  | { readonly ok: false; readonly error: string };

/** Takes one item down. The same target already down answers its existing takedown. */
export async function applyTakedown(options: {
  readonly dataDir: string;
  readonly club: string;
  readonly kind: TakedownKind;
  readonly ref: string | undefined;
  readonly notice: string;
  readonly now: Date;
  readonly owner: Owner;
}): Promise<TakedownResult> {
  const { dataDir, club, kind, now, owner } = options;
  const ref = normaliseRef(kind, options.ref);

  if (!ref.ok) {
    return ref;
  }

  const notice = oneLineOf(options.notice, TAKEDOWN_NOTICE_MAX);

  if (notice === "") {
    return { ok: false, error: `Say which notice this is with --notice "<short reference>" - a reference only, not the complainant's details.` };
  }

  let file: TakedownFile;

  try {
    file = await readTakedowns(dataDir, club);
  } catch (error) {
    return { ok: false, error: `The club's takedown file will not read, so nothing was changed: ${String(error)}` };
  }

  const isFile = kind === "clip" || kind === "crewsample";
  const existing = file.takedowns.find((r) => r.state === "active" && r.kind === kind && r.ref === ref.ref);

  // Moved again even when already down: a file put back by hand, or one
  // that arrived after the first takedown, is held all the same.
  const held = isFile
    ? await move(dataDir, filesOf(dataDir, club, kind, ref.ref).map((f) => ({ from: f.live, to: f.held })), owner)
      || (await Promise.all(filesOf(dataDir, club, kind, ref.ref).map((f) => exists(f.held)))).some(Boolean)
    : null;

  if (existing !== undefined) {
    return { ok: true, id: existing.id, club, kind, ref: ref.ref, notice: existing.notice, state: "active", takenDownAt: existing.takenDownAt, already: true, held };
  }

  const record: TakedownRecord = {
    id: `td-${randomBytes(6).toString("hex")}`,
    kind,
    ref: ref.ref,
    notice,
    state: "active",
    takenDownAt: now.toISOString(),
    restoredAt: null,
    restoreReason: null,
  };

  await writeTakedowns(dataDir, { version: 1, club, takedowns: [...file.takedowns, record] }, owner);
  return { ok: true, id: record.id, club, kind, ref: ref.ref, notice, state: "active", takenDownAt: record.takenDownAt, already: false, held };
}

export type RestoreResult =
  | {
      readonly ok: true;
      readonly id: string;
      readonly club: string;
      readonly kind: TakedownKind;
      readonly ref: string;
      readonly state: "restored";
      readonly restoredAt: string;
      readonly reason: string;
      /** For a clip or crew sample, whether a held file was put back; null for every other kind. */
      readonly putBack: boolean | null;
    }
  | { readonly ok: false; readonly error: string };

/** Puts one item back after a counter-notice. The record stays, marked restored. */
export async function restoreTakedown(options: {
  readonly dataDir: string;
  readonly club: string;
  readonly id: string;
  readonly reason: string;
  readonly now: Date;
  readonly owner: Owner;
}): Promise<RestoreResult> {
  const { dataDir, club, id, now, owner } = options;
  const reason = oneLineOf(options.reason, TAKEDOWN_REASON_MAX);

  if (reason === "") {
    return { ok: false, error: `Say why with --reason "<text>", e.g. "counter-notice received 2026-12-01, no suit filed".` };
  }

  let file: TakedownFile;

  try {
    file = await readTakedowns(dataDir, club);
  } catch (error) {
    return { ok: false, error: `The club's takedown file will not read, so nothing was changed: ${String(error)}` };
  }

  const record = file.takedowns.find((r) => r.id === id);

  if (record === undefined) {
    return { ok: false, error: `${club} has no takedown ${id}. takedown-list shows them.` };
  }

  if (record.state === "restored") {
    return { ok: false, error: `${id} was already restored on ${record.restoredAt ?? "an earlier day"}.` };
  }

  const putBack = record.kind === "clip" || record.kind === "crewsample"
    ? await move(dataDir, filesOf(dataDir, club, record.kind, record.ref).map((f) => ({ from: f.held, to: f.live })), owner)
    : null;
  const restored: TakedownRecord = { ...record, state: "restored", restoredAt: now.toISOString(), restoreReason: reason };

  await writeTakedowns(dataDir, { ...file, takedowns: file.takedowns.map((r) => (r.id === id ? restored : r)) }, owner);
  return { ok: true, id, club, kind: record.kind, ref: record.ref, state: "restored", restoredAt: restored.restoredAt as string, reason, putBack };
}

/** Every club that has a takedown file. */
export async function clubsWithTakedowns(dataDir: string): Promise<string[]> {
  const names = await readdir(join(dataDir, TAKEDOWN_DIR)).catch(() => [] as string[]);
  return names.filter((n) => n.endsWith(".json")).map((n) => n.slice(0, -5)).filter(isValidClubId).sort();
}

// --------------------------------------------------- from a notice's URL

export interface TakedownCandidate {
  readonly kind: TakedownKind;
  readonly ref: string;
  /** What it is, in words, for the admin to pick from. */
  readonly label: string;
  /** Whether the club has it published (or held) right now. */
  readonly present: boolean;
  /** The active takedown already on it, or null. */
  readonly takedownId: string | null;
}

/**
 * What a URL from a notice could be pointing at, for one club. Read only.
 * A page address lists everything on that page that can be taken down; a
 * file's address is that one file.
 */
export async function findTakedownTargets(options: {
  readonly dataDir: string;
  readonly store: Store;
  readonly club: string;
  readonly url: string;
}): Promise<{ ok: true; candidates: TakedownCandidate[]; note: string } | { ok: false; error: string }> {
  const { dataDir, store, club } = options;
  const text = options.url.trim();
  const records = await readTakedowns(dataDir, club).catch(() => ({ takedowns: [] as TakedownRecord[] }));
  const downId = (kind: TakedownKind, ref: string) => records.takedowns.find((r) => r.state === "active" && r.kind === kind && r.ref === ref)?.id ?? null;
  const snap = async (channel: string) => plain((await store.getSnapshot(club, channel).catch(() => undefined))?.body);
  const out: TakedownCandidate[] = [];
  const add = (kind: TakedownKind, ref: string, label: string, present: boolean) => {
    if (!out.some((c) => c.kind === kind && c.ref === ref)) out.push({ kind, ref, label, present, takedownId: downId(kind, ref) });
  };

  const video = youTubeIdOf(text);

  if (video !== undefined) {
    add("video", video, `YouTube video ${video}`, (await streamVideoIds(store, club)).has(video));
    return { ok: true, candidates: out, note: "A YouTube address: taking it down stops this club's pages offering that video. The video itself is on YouTube." };
  }

  let path: string[];

  try {
    path = new URL(/^[a-z]+:\/\//i.test(text) ? text : `https://relay.invalid${text.startsWith("/") ? "" : "/"}${text}`).pathname
      .split("/").filter((p) => p.length > 0).map((p) => decodeURIComponent(p));
  } catch {
    return { ok: false, error: "That does not read as an address." };
  }

  // /c/<club>/... or /api/v1/c/<club>/...
  const at = path[0] === "c" ? 1 : path[0] === "api" && path[1] === "v1" && path[2] === "c" ? 3 : -1;

  if (at < 0) {
    return { ok: false, error: "That is not an address on the relay (one with /c/<club> in it) or a YouTube address." };
  }

  const named = path[at] ?? "";
  const site = await snap("site");
  const alias = typeof site?.["alias"] === "string" ? site["alias"].toLowerCase() : "";

  if (named !== club && named.toLowerCase() !== alias) {
    return { ok: false, error: `That address is for "${named}", not ${club}.` };
  }

  const rest = path.slice(at + 1);
  const api = at === 3;
  const logo = async () => add("logo", "", "The club's logo (every page, and the picture on shared links)", typeof site?.["logo"] === "string" && site["logo"] !== "");
  const sponsors = async () => {
    const list = Array.isArray((await snap("sponsors"))?.["sponsors"]) ? ((await snap("sponsors"))?.["sponsors"] as unknown[]) : [];
    for (const s of list) {
      const sponsor = plain(s);
      if (sponsor === undefined || typeof sponsor["id"] !== "string") continue;
      add("sponsor", sponsor["id"], `Sponsor "${oneLineOf(String(sponsor["name"] ?? ""), 60)}"${typeof sponsor["picture"] === "string" ? " (with a picture)" : ""}`, true);
    }
  };
  const tracks = async () => {
    const list = Array.isArray((await snap("tracks"))?.["tracks"]) ? ((await snap("tracks"))?.["tracks"] as unknown[]) : [];
    for (const t of list) {
      const track = plain(t);
      const name = oneLineOf(String(track?.["name"] ?? ""), 200);
      if (track === undefined || name === "" || typeof track["image"] !== "string") continue;
      add("track", name, `Track "${name}" picture`, true);
    }
  };
  const badges = async () => {
    const list = Array.isArray((await snap("badges"))?.["badges"]) ? ((await snap("badges"))?.["badges"] as unknown[]) : [];
    for (const b of list) {
      const badge = plain(b);
      if (badge === undefined || typeof badge["id"] !== "string" || typeof badge["art"] !== "string") continue;
      add("badge", badge["id"], `Achievement "${oneLineOf(String(badge["name"] ?? badge["id"]), 60)}" artwork`, true);
    }
  };
  const raceVideos = async (sid: string) => {
    const result = plain((await store.getItem(club, "results", sid).catch(() => undefined))?.body);
    const stream = await mergedStream(store, club);
    if (result === undefined || stream === undefined) return;
    const found = raceVideo(stream, { sid, startedUtc: result["startedUtc"], endedUtc: result["endedUtc"] }, false);
    if (found !== undefined) add("video", found.id, `YouTube video ${found.id} (this race's video)`, true);
  };

  // The files: one each.
  if (!api && rest[0] === "logo.png") {
    await logo();
  } else if (!api && rest[0] === "clip" && rest[1] !== undefined) {
    const id = rest[1].toLowerCase().replace(/\.mp3$/, "");
    if (!CLIP_ID.test(id)) return { ok: false, error: "That clip address has no clip id in it." };
    const present = await exists(join(dataDir, CLIP_DIR, club, `${id}.mp3`)) || await exists(join(heldFolder(dataDir, club, "clip"), `${id}.mp3`));
    add("clip", id, "A driver's race clip", present);
  } else if (api && rest[0] === "crew" && rest[1] === "sample" && rest[2] !== undefined) {
    const key = rest[2].toLowerCase().replace(/\.mp3$/, "");
    if (!CREW_SAMPLE_KEY.test(key)) return { ok: false, error: "That sample address has no voice key in it." };
    const present = await exists(join(dataDir, CREW_SAMPLE_DIR, club, `${key}.mp3`)) || await exists(join(heldFolder(dataDir, club, "crewsample"), `${key}.mp3`));
    add("crewsample", key, "A crew voice sample", present);
  } else if (api && rest[0] === "snapshot" && rest[1] !== undefined) {
    const channel = rest[1];
    if (channel === "site") await logo();
    else if (channel === "sponsors") await sponsors();
    else if (channel === "tracks") await tracks();
    else if (channel === "badges") await badges();
  } else if (api && rest[0] === "video" && rest[1] === "race" && rest[2] !== undefined) {
    await raceVideos(rest[2].toLowerCase());
  } else if (api && rest[0] === "video") {
    for (const id of await streamVideoIds(store, club)) add("video", id, `YouTube video ${id}`, true);
  } else if (!api) {
    // A page: the logo is on every one, and what else the page draws.
    const page = rest[0] ?? "home";
    await logo();
    if (page === "home" || page === "live" || page === "overlay") await sponsors();
    if (page === "home" || page === "tracks") await tracks();
    if (page === "badges") await badges();
    if (page === "results" && rest[1] !== undefined) await raceVideos(rest[1].toLowerCase());
  }

  const note = out.length === 0
    ? "Nothing on that address can be taken down one item at a time. Listen audio and captions stop with the club's Listen switch; anything else, ask the club to remove it, or delete the club."
    : "Pick the one the notice describes and run takedown with its kind and ref.";

  return { ok: true, candidates: out, note };
}

/** Every night's stream as the relay merges it (server.ts streamOf), from the store. */
async function mergedStream(store: Store, club: string) {
  const latest = await store.getSnapshot(club, "stream").catch(() => undefined);
  const nights = latest === undefined ? [] : await store.listItems(club, "streamnights").catch(() => []);
  return streamFrom(mergedStreamBody(latest?.body, nights.map((n) => n.doc.body), []), new Set(), STREAM_MAX_PIECES * (STREAM_NIGHTS_KEPT + 1));
}

async function streamVideoIds(store: Store, club: string): Promise<Set<string>> {
  const stream = await mergedStream(store, club);
  return new Set((stream?.pieces ?? []).map((p) => p.videoId).filter((id) => id !== ""));
}
