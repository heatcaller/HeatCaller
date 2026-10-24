import { mkdir, open, readdir, readFile, rm, unlink } from "node:fs/promises";
import { join } from "node:path";
import { writeFileAtomically } from "./voice.js";

/**
 * THE PODIUM'S HIGHLIGHT VIDEOS (21 Oct 2026).
 *
 * After a race the laptop makes a short upright video for each of the top
 * three - the announcer calling their race, captioned (HighlightVideo.cs) -
 * and sends it here, so a driver can watch and keep it on their phone: from
 * the QR code the Big Screen puts up after the race, or from their own night.
 *
 * ONLY THE PODIUM. The laptop sends nothing else, and this refuses anything
 * else whatever sends it: a position outside 1 to 3 is turned away before a
 * byte is kept. Three videos a race is a cost a relay can carry; forty is not.
 *
 * NOT IN THE CLUB'S PUBLISHED TREE, for the reasons the voice cache is not:
 * these are megabytes, not kilobytes, and the store's documents are JSON a
 * page reads. They live in dataDir/.videos/<club>/ - with a dot, so no club
 * id can ever name the folder - and none of it counts against a club's
 * publishing quota.
 *
 * THE LINK IS THE CREDENTIAL, as a badge id is for a driver's night: every
 * video and every race has 128 random bits for a name, chosen on the laptop.
 * Nothing lists a club's videos to anybody; a race's page is reached by the
 * QR code shown in the room and a video by that page or the driver's night.
 *
 * KEPT FOR A WEEK, and never more than a club's share: the oldest go first,
 * on every upload. A highlight is something people save the night they see
 * it; a relay is not an archive.
 */

/** A video's own name and a race's: 32 hex characters, 128 random bits from the laptop. */
export const VIDEO_ID = /^[0-9a-f]{32}$/;

/** The biggest video taken. A 55-second 1080x1920 clip at the laptop's 6 Mbps is about 42 MB at the very most; most are far less. */
export const VIDEO_MAX_BYTES = 48 * 1024 * 1024;

/** How long a video is kept. */
export const VIDEO_KEEP_MS = 7 * 24 * 60 * 60 * 1000;

/** The most one club keeps at once, newest first - eight race nights of three. */
export const VIDEO_MAX_PER_CLUB = 120;

/** The most one club's videos may come to on disk together. */
export const VIDEO_MAX_CLUB_BYTES = 1024 * 1024 * 1024;

/** What is known about one video - everything but the bytes. */
export interface VideoMeta {
  readonly id: string;
  /** The race it is from - the name of the race's page. */
  readonly race: string;
  /** 1, 2 or 3. */
  readonly position: number;
  /** The driver's badge id, so their own night can offer it - or absent. */
  readonly driver?: string;
  /** The driver's name as the club wrote it. */
  readonly name: string;
  /** The race's own name - "A-Main", "Heat 3". */
  readonly label: string;
  /** When it arrived, ISO 8601. */
  readonly at: string;
  readonly bytes: number;
}

/** Where the videos are kept. Disk in a running relay, memory in the tests. */
export interface VideoVault {
  put(club: string, meta: VideoMeta, bytes: Buffer): Promise<void>;
  list(club: string): Promise<VideoMeta[]>;
  meta(club: string, id: string): Promise<VideoMeta | undefined>;
  /** Bytes start to end inclusive, or undefined when the video is gone. */
  read(club: string, id: string, start: number, end: number): Promise<Buffer | undefined>;
  remove(club: string, id: string): Promise<void>;
  /** Every one of a club's videos - when the club is deleted. */
  removeClub(club: string): Promise<void>;
  /** The clubs holding videos, for the hourly clean-up (22 Oct 2026: without it the timed sweep found no clubs). */
  listClubs(): Promise<string[]>;
}

/** The folder under dataDir, with a dot so no club id can name it. */
export const VIDEO_DIR = ".videos";

/**
 * Whether a parsed document is a video's meta, field by field - a file
 * on disk is read back as unknown, and a hand-edited one must not be
 * trusted into a page.
 */
export function asVideoMeta(value: unknown): VideoMeta | undefined {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return undefined;
  }

  const v = value as Record<string, unknown>;
  const id = v["id"];
  const race = v["race"];
  const position = v["position"];
  const name = v["name"];
  const label = v["label"];
  const at = v["at"];
  const bytes = v["bytes"];
  const driver = v["driver"];

  if (
    typeof id !== "string" || !VIDEO_ID.test(id)
    || typeof race !== "string" || !VIDEO_ID.test(race)
    || typeof position !== "number" || !Number.isInteger(position) || position < 1 || position > 3
    || typeof name !== "string" || typeof label !== "string"
    || typeof at !== "string" || Number.isNaN(Date.parse(at))
    || typeof bytes !== "number" || !Number.isInteger(bytes) || bytes < 0
  ) {
    return undefined;
  }

  return {
    id,
    race,
    position,
    name,
    label,
    at,
    bytes,
    ...(typeof driver === "string" && /^[0-9a-f]{20}$/.test(driver) ? { driver } : {}),
  };
}

/**
 * Which of a club's videos have to go to make room: anything older than a
 * week, then the oldest until the club is under its count and its bytes
 * with room for <paramref name="incoming"/> more. The newest are never the
 * ones thrown away.
 */
export function videosToDrop(held: readonly VideoMeta[], now: number, incoming = 0): VideoMeta[] {
  const newestFirst = [...held].sort((a, b) => Date.parse(b.at) - Date.parse(a.at));
  const drop: VideoMeta[] = [];
  let count = incoming > 0 ? 1 : 0;
  let bytes = incoming;
  // WHY (22 Oct 2026): a big newer video that did not fit used to be dropped
  // while a smaller, older one further down still fitted and was kept - so the
  // newer race's video vanished and the older one stayed. Once the club is full,
  // everything older than that point goes too.
  let full = false;

  for (const video of newestFirst) {
    const old = now - Date.parse(video.at) > VIDEO_KEEP_MS;

    if (!full && (count + 1 > VIDEO_MAX_PER_CLUB || bytes + video.bytes > VIDEO_MAX_CLUB_BYTES)) {
      full = true;
    }

    if (full || old) {
      drop.push(video);
      continue;
    }

    count += 1;
    bytes += video.bytes;
  }

  return drop;
}

/** The videos in memory - for the tests, and nothing else. */
export class MemoryVideoVault implements VideoVault {
  private readonly clubs = new Map<string, Map<string, { meta: VideoMeta; bytes: Buffer }>>();

  private of(club: string): Map<string, { meta: VideoMeta; bytes: Buffer }> {
    let held = this.clubs.get(club);

    if (held === undefined) {
      held = new Map();
      this.clubs.set(club, held);
    }

    return held;
  }

  async put(club: string, meta: VideoMeta, bytes: Buffer): Promise<void> {
    this.of(club).set(meta.id, { meta, bytes: Buffer.from(bytes) });
  }

  async list(club: string): Promise<VideoMeta[]> {
    return [...this.of(club).values()].map((v) => v.meta);
  }

  async meta(club: string, id: string): Promise<VideoMeta | undefined> {
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
 * The videos on disk: dataDir/.videos/<club>/<id>.mp4 beside <id>.json.
 * Written atomically, as the voice cache is, so a crash mid-upload leaves
 * no half a video for a phone to choke on.
 */
export class DiskVideoVault implements VideoVault {
  private readonly root: string;

  constructor(dataDir: string) {
    this.root = join(dataDir, VIDEO_DIR);
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

  private file(club: string, id: string, ext: "mp4" | "json"): string {
    if (!VIDEO_ID.test(id)) {
      throw new Error("Not a video id.");
    }

    return join(this.folder(club), `${id}.${ext}`);
  }

  async put(club: string, meta: VideoMeta, bytes: Buffer): Promise<void> {
    await mkdir(this.folder(club), { recursive: true, mode: 0o700 });

    // The video before its meta: a meta with no video behind it would be
    // listed and fail to play, where a video with no meta is never listed.
    await writeFileAtomically(this.file(club, meta.id, "mp4"), bytes);
    await writeFileAtomically(this.file(club, meta.id, "json"), Buffer.from(JSON.stringify(meta), "utf8"));
  }

  async list(club: string): Promise<VideoMeta[]> {
    let names: string[];

    try {
      names = await readdir(this.folder(club));
    } catch {
      return [];
    }

    const found: VideoMeta[] = [];

    for (const name of names) {
      const id = name.endsWith(".json") ? name.slice(0, -5) : "";

      if (!VIDEO_ID.test(id)) {
        continue;
      }

      const meta = await this.meta(club, id);

      if (meta !== undefined) {
        found.push(meta);
      }
    }

    return found;
  }

  async meta(club: string, id: string): Promise<VideoMeta | undefined> {
    if (!VIDEO_ID.test(id)) {
      return undefined;
    }

    try {
      const parsed = asVideoMeta(JSON.parse(await readFile(this.file(club, id, "json"), "utf8")) as unknown);
      return parsed !== undefined && parsed.id === id ? parsed : undefined;
    } catch {
      return undefined;
    }
  }

  async read(club: string, id: string, start: number, end: number): Promise<Buffer | undefined> {
    if (!VIDEO_ID.test(id) || end < start) {
      return undefined;
    }

    let handle;

    try {
      handle = await open(this.file(club, id, "mp4"), "r");
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
    if (!VIDEO_ID.test(id)) {
      return;
    }

    await unlink(this.file(club, id, "json")).catch(() => {});
    await unlink(this.file(club, id, "mp4")).catch(() => {});
  }

  /** Every club's videos, gone - when a club is deleted. */
  async removeClub(club: string): Promise<void> {
    await rm(this.folder(club), { recursive: true, force: true });
  }
}
