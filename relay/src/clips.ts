import { mkdir, open, readdir, readFile, rm, unlink } from "node:fs/promises";
import { join } from "node:path";
import { writeFileAtomically } from "./voice.js";

/**
 * A DRIVER'S OWN RACE CLIPS (22 Oct 2026, driver experience).
 *
 * After each race the laptop encodes the announcer's call of it as an mp3
 * (RaceAudioRecorder) and, when the club has said yes, sends one copy per
 * driver who raced it here - so the driver can play it, and keep it, from
 * their own night page.
 *
 * A COPY OF videos.ts, ON PURPOSE. The vault, its folder outside the
 * club's published tree, the atomic writes and the "oldest go first" rule
 * are the podium videos' own, which have already been through two bug
 * hunts. What differs is only what a clip is: an mp3 of a few megabytes,
 * always one driver's, and many more of them - so the caps are per driver
 * as well as per club.
 *
 * PRIVATE TO THE BADGE. A clip is listed only in the answer to that
 * driver's own night (readOneNight), which needs the badge code; and its
 * own address is 128 random bits from the laptop, so the link is the
 * credential, as a podium video's is. Nothing lists a club's clips.
 *
 * KEPT FOR A WEEK, eight per driver, and never more than the club's share;
 * all of them go the moment the club closes drivers' nights, and with the
 * club when it deletes everything.
 */

/** A clip's own name: 32 hex characters, 128 random bits from the laptop. */
export const CLIP_ID = /^[0-9a-f]{32}$/;

/** A race's id as the laptop names sessions (SessionRecord.Id, a Guid "N"). */
export const CLIP_RACE_ID = /^[0-9a-f]{32}$/;

/** A driver's badge id (RelayCheckIn.IdFor). */
export const CLIP_DRIVER_ID = /^[0-9a-f]{20}$/;

/** The biggest clip taken. A 150-second call at the laptop's bitrate is two or three megabytes. */
export const CLIP_MAX_BYTES = 4 * 1024 * 1024;

/** How long a clip is kept. */
export const CLIP_KEEP_MS = 7 * 24 * 60 * 60 * 1000;

/** The most one driver keeps, newest first - a club night of heats and a main. */
export const CLIP_MAX_PER_DRIVER = 8;

/** The most one club keeps at once. */
export const CLIP_MAX_PER_CLUB = 800;

/** The most one club's clips may come to on disk together. */
export const CLIP_MAX_CLUB_BYTES = 300 * 1024 * 1024;

/** The longest label kept - "Q2 Heat 1 (Stock Buggy)". */
export const CLIP_LABEL_MAX = 80;

/** What is known about one clip - everything but the bytes. */
export interface ClipMeta {
  readonly id: string;
  /** The driver's badge id. Required: a clip is always somebody's. */
  readonly driver: string;
  /** The race's name as the club wrote it. */
  readonly label: string;
  /** The session it is from, when the laptop said - so the night page can put it on that race's card. */
  readonly race?: string;
  /** When it arrived, ISO 8601. */
  readonly at: string;
  readonly bytes: number;
}

/** Where the clips are kept. Disk in a running relay, memory in the tests. */
export interface ClipVault {
  put(club: string, meta: ClipMeta, bytes: Buffer): Promise<void>;
  list(club: string): Promise<ClipMeta[]>;
  meta(club: string, id: string): Promise<ClipMeta | undefined>;
  /** Bytes start to end inclusive, or undefined when the clip is gone. */
  read(club: string, id: string, start: number, end: number): Promise<Buffer | undefined>;
  remove(club: string, id: string): Promise<void>;
  /** Every one of a club's clips - when nights close or the club is deleted. */
  removeClub(club: string): Promise<void>;
  /** The clubs holding clips, for the hourly clean-up. */
  listClubs(): Promise<string[]>;
}

/** The folder under dataDir, with a dot so no club id can name it. */
export const CLIP_DIR = ".clips";

/** Whether the first bytes look like an mp3: an ID3 tag, or an MPEG audio frame sync (eleven set bits). */
export function looksLikeMp3(bytes: Buffer): boolean {
  if (bytes.length < 4) {
    return false;
  }

  if (bytes.toString("latin1", 0, 3) === "ID3") {
    return true;
  }

  // 0xFF then the top three bits of the next byte: 0xE0 to 0xFF. The
  // design wrote "0xFF 0xE?", and a real MPEG-1 Layer III frame starts
  // FF FB, so the whole sync is checked rather than one nibble.
  return bytes[0] === 0xff && ((bytes[1] ?? 0) & 0xe0) === 0xe0;
}

/** Whether a parsed document is a clip's meta, field by field - a hand-edited file must not be trusted into a page. */
export function asClipMeta(value: unknown): ClipMeta | undefined {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return undefined;
  }

  const v = value as Record<string, unknown>;
  const id = v["id"];
  const driver = v["driver"];
  const label = v["label"];
  const race = v["race"];
  const at = v["at"];
  const bytes = v["bytes"];

  if (
    typeof id !== "string" || !CLIP_ID.test(id)
    || typeof driver !== "string" || !CLIP_DRIVER_ID.test(driver)
    || typeof label !== "string"
    || typeof at !== "string" || Number.isNaN(Date.parse(at))
    || typeof bytes !== "number" || !Number.isInteger(bytes) || bytes < 0
  ) {
    return undefined;
  }

  return {
    id,
    driver,
    label: label.slice(0, CLIP_LABEL_MAX),
    at,
    bytes,
    ...(typeof race === "string" && CLIP_RACE_ID.test(race) ? { race } : {}),
  };
}

/**
 * Which of a club's clips have to go to make room: anything older than a
 * week; then, per driver, any past their eight (counting the incoming one
 * for its driver); then, once the club is full, everything older than that
 * point. The newest are never the ones thrown away - the same "once full,
 * everything older goes too" rule videosToDrop learned in Bug Hunt 9.
 */
export function clipsToDrop(
  held: readonly ClipMeta[],
  now: number,
  incoming?: { readonly driver: string; readonly bytes: number },
): ClipMeta[] {
  const newestFirst = [...held].sort((a, b) => Date.parse(b.at) - Date.parse(a.at));
  const drop: ClipMeta[] = [];
  const perDriver = new Map<string, number>();
  let count = 0;
  let bytes = 0;

  if (incoming !== undefined) {
    perDriver.set(incoming.driver, 1);
    count = 1;
    bytes = incoming.bytes;
  }

  let full = false;

  for (const clip of newestFirst) {
    const old = now - Date.parse(clip.at) > CLIP_KEEP_MS;
    const mine = perDriver.get(clip.driver) ?? 0;

    if (old || mine + 1 > CLIP_MAX_PER_DRIVER) {
      drop.push(clip);
      continue;
    }

    if (!full && (count + 1 > CLIP_MAX_PER_CLUB || bytes + clip.bytes > CLIP_MAX_CLUB_BYTES)) {
      full = true;
    }

    if (full) {
      drop.push(clip);
      continue;
    }

    perDriver.set(clip.driver, mine + 1);
    count += 1;
    bytes += clip.bytes;
  }

  return drop;
}

/** The clips in memory - for the tests, and nothing else. */
export class MemoryClipVault implements ClipVault {
  private readonly clubs = new Map<string, Map<string, { meta: ClipMeta; bytes: Buffer }>>();

  private of(club: string): Map<string, { meta: ClipMeta; bytes: Buffer }> {
    let held = this.clubs.get(club);

    if (held === undefined) {
      held = new Map();
      this.clubs.set(club, held);
    }

    return held;
  }

  async put(club: string, meta: ClipMeta, bytes: Buffer): Promise<void> {
    this.of(club).set(meta.id, { meta, bytes: Buffer.from(bytes) });
  }

  async list(club: string): Promise<ClipMeta[]> {
    return [...this.of(club).values()].map((v) => v.meta);
  }

  async meta(club: string, id: string): Promise<ClipMeta | undefined> {
    return this.of(club).get(id)?.meta;
  }

  async read(club: string, id: string, start: number, end: number): Promise<Buffer | undefined> {
    const held = this.of(club).get(id);
    return held === undefined ? undefined : held.bytes.subarray(start, end + 1);
  }

  async remove(club: string, id: string): Promise<void> {
    this.of(club).delete(id);
  }

  async removeClub(club: string): Promise<void> {
    this.clubs.delete(club);
  }

  async listClubs(): Promise<string[]> {
    return [...this.clubs.keys()];
  }
}

/**
 * The clips on disk: dataDir/.clips/<club>/<id>.mp3 beside <id>.json.
 * Written atomically, as the videos are, so a crash mid-upload leaves no
 * half a clip for a phone to choke on.
 */
export class DiskClipVault implements ClipVault {
  private readonly root: string;

  constructor(dataDir: string) {
    this.root = join(dataDir, CLIP_DIR);
  }

  private folder(club: string): string {
    return join(this.root, club);
  }

  async listClubs(): Promise<string[]> {
    try {
      return (await readdir(this.root, { withFileTypes: true })).filter((d) => d.isDirectory()).map((d) => d.name);
    } catch {
      return [];
    }
  }

  private file(club: string, id: string, ext: "mp3" | "json"): string {
    if (!CLIP_ID.test(id)) {
      throw new Error("Not a clip id.");
    }

    return join(this.folder(club), `${id}.${ext}`);
  }

  async put(club: string, meta: ClipMeta, bytes: Buffer): Promise<void> {
    await mkdir(this.folder(club), { recursive: true, mode: 0o700 });

    // The audio before its meta: a meta with nothing behind it would be
    // listed and fail to play, where audio with no meta is never listed.
    await writeFileAtomically(this.file(club, meta.id, "mp3"), bytes);
    await writeFileAtomically(this.file(club, meta.id, "json"), Buffer.from(JSON.stringify(meta), "utf8"));
  }

  async list(club: string): Promise<ClipMeta[]> {
    let names: string[];

    try {
      names = await readdir(this.folder(club));
    } catch {
      return [];
    }

    const found: ClipMeta[] = [];

    for (const name of names) {
      const id = name.endsWith(".json") ? name.slice(0, -5) : "";

      if (!CLIP_ID.test(id)) {
        continue;
      }

      const meta = await this.meta(club, id);

      if (meta !== undefined) {
        found.push(meta);
      }
    }

    return found;
  }

  async meta(club: string, id: string): Promise<ClipMeta | undefined> {
    if (!CLIP_ID.test(id)) {
      return undefined;
    }

    try {
      const parsed = asClipMeta(JSON.parse(await readFile(this.file(club, id, "json"), "utf8")) as unknown);
      return parsed !== undefined && parsed.id === id ? parsed : undefined;
    } catch {
      return undefined;
    }
  }

  async read(club: string, id: string, start: number, end: number): Promise<Buffer | undefined> {
    if (!CLIP_ID.test(id) || end < start) {
      return undefined;
    }

    let handle;

    try {
      handle = await open(this.file(club, id, "mp3"), "r");
    } catch {
      return undefined;
    }

    try {
      const length = end - start + 1;
      const buffer = Buffer.alloc(length);
      const { bytesRead } = await handle.read(buffer, 0, length, start);
      return buffer.subarray(0, bytesRead);
    } finally {
      await handle.close();
    }
  }

  async remove(club: string, id: string): Promise<void> {
    if (!CLIP_ID.test(id)) {
      return;
    }

    await unlink(this.file(club, id, "json")).catch(() => {});
    await unlink(this.file(club, id, "mp3")).catch(() => {});
  }

  /** Every one of a club's clips, gone - when nights close or the club is deleted. */
  async removeClub(club: string): Promise<void> {
    await rm(this.folder(club), { recursive: true, force: true });
  }
}
