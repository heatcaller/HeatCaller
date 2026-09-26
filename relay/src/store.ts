import { randomBytes } from "node:crypto";
import { chmod, lstat, mkdir, readFile, readdir, rename, rm, stat, unlink, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { isValidClubId } from "./tenancy.js";
import type { StoredDocument } from "./types.js";

/**
 * Where a club's published documents live.
 *
 * The club is the FIRST argument of every method, on purpose. A store
 * whose methods took a pre-built key would put the tenancy boundary in
 * the hands of every caller; here there is one place that turns a club
 * and a channel into a key, and it refuses a club id it does not like.
 */
export interface Store {
  putSnapshot(club: string, channel: string, doc: StoredDocument): Promise<void>;

  /**
   * Undefined when nothing is stored there. A document that is there but
   * cannot be read throws (StoreReadError, for the file store) - it is not
   * "nothing", and must not be remembered as nothing. getItem the same.
   */
  getSnapshot(club: string, channel: string): Promise<StoredDocument | undefined>;

  putItem(club: string, channel: string, id: string, doc: StoredDocument): Promise<void>;
  getItem(club: string, channel: string, id: string): Promise<StoredDocument | undefined>;
  listItems(club: string, channel: string): Promise<readonly { id: string; doc: StoredDocument }[]>;

  /**
   * What a collection holds, without reading it: each item's id, its size
   * on disk and when it was written. Cheap enough to ask on every publish
   * to a collection, which listItems is not.
   */
  itemSizes(club: string, channel: string): Promise<readonly ItemSize[]>;
  deleteItem(club: string, channel: string, id: string): Promise<void>;

  /** Bytes this club is holding, everything included. */
  usage(club: string): Promise<number>;

  /**
   * Bytes one snapshot takes, exactly as usage() counts it, or 0 when there
   * is none - without reading it. For leaving one document out of what the
   * quota counts: see planRoom in server.ts, which asks on every publish to
   * a collection.
   */
  snapshotBytes(club: string, channel: string): Promise<number>;

  /** Everything this club has published. The club's own delete button, and its right to be forgotten. */
  deleteClub(club: string): Promise<void>;
}

/**
 * A document that is there but could not be read - as opposed to one that
 * is not there, which a read answers with undefined.
 *
 * corrupt is true when the file was read but is not JSON: that will not
 * mend itself, and only a new write puts it right. Otherwise the read
 * itself failed, and may work next time.
 */
export class StoreReadError extends Error {
  constructor(
    readonly path: string,
    readonly corrupt: boolean,
    cause: unknown,
  ) {
    super(`Could not read ${path}: ${String(cause)}`, { cause });
    this.name = "StoreReadError";
  }
}

export interface ItemSize {
  readonly id: string;
  readonly bytes: number;
  /** When it was last written, milliseconds. Only ever compared with another item's. */
  readonly at: number;
}

/**
 * Ids inside a collection - a session id, say - are chosen by the
 * publisher, so they get the same treatment as the club id: they become
 * file names.
 */
const ITEM_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/;

/**
 * What FileStore creates its files and folders with: the relay's own
 * account and nobody else. (Ignored on Windows, which has no such bits.)
 * A folder that already exists keeps whatever mode it has.
 */
export const FILE_MODE = 0o600;
export const DIR_MODE = 0o700;

export function isValidItemId(value: string): boolean {
  return ITEM_PATTERN.test(value) && !value.includes("..");
}

function guard(club: string, channel: string, id?: string): void {
  if (!isValidClubId(club)) {
    throw new Error(`Refusing to touch storage for an invalid club id: ${JSON.stringify(club)}`);
  }

  if (!/^[a-z][a-z0-9]{1,23}$/.test(channel)) {
    throw new Error(`Refusing to touch storage for an invalid channel: ${JSON.stringify(channel)}`);
  }

  if (id !== undefined && !isValidItemId(id)) {
    throw new Error(`Refusing to touch storage for an invalid item id: ${JSON.stringify(id)}`);
  }
}

/** For tests, and for a relay that is happy to lose everything on restart. */
export class MemoryStore implements Store {
  private readonly clubs = new Map<string, Map<string, StoredDocument>>();

  private bucket(club: string): Map<string, StoredDocument> {
    let found = this.clubs.get(club);

    if (found === undefined) {
      found = new Map();
      this.clubs.set(club, found);
    }

    return found;
  }

  async putSnapshot(club: string, channel: string, doc: StoredDocument): Promise<void> {
    guard(club, channel);
    this.bucket(club).set(`s/${channel}`, doc);
  }

  async getSnapshot(club: string, channel: string): Promise<StoredDocument | undefined> {
    guard(club, channel);
    return this.clubs.get(club)?.get(`s/${channel}`);
  }

  async putItem(club: string, channel: string, id: string, doc: StoredDocument): Promise<void> {
    guard(club, channel, id);

    // Deleted first so a rewrite moves to the end: the map's order is then
    // the order things were written, which is what "oldest" means below
    // when two documents carry the same publishedAt.
    const bucket = this.bucket(club);
    bucket.delete(`c/${channel}/${id}`);
    bucket.set(`c/${channel}/${id}`, doc);
  }

  async getItem(club: string, channel: string, id: string): Promise<StoredDocument | undefined> {
    guard(club, channel, id);
    return this.clubs.get(club)?.get(`c/${channel}/${id}`);
  }

  async listItems(club: string, channel: string): Promise<readonly { id: string; doc: StoredDocument }[]> {
    guard(club, channel);

    const prefix = `c/${channel}/`;
    const out: { id: string; doc: StoredDocument }[] = [];

    for (const [key, doc] of this.clubs.get(club) ?? []) {
      if (key.startsWith(prefix)) {
        out.push({ id: key.slice(prefix.length), doc });
      }
    }

    return out;
  }

  async itemSizes(club: string, channel: string): Promise<readonly ItemSize[]> {
    const items = await this.listItems(club, channel);

    // Written order breaks ties, as the file store's mtime would.
    return items.map((item, index) => ({
      id: item.id,
      bytes: Buffer.byteLength(JSON.stringify(item.doc)),
      at: (Date.parse(item.doc.publishedAt) || 0) + index / (items.length + 1),
    }));
  }

  async deleteItem(club: string, channel: string, id: string): Promise<void> {
    guard(club, channel, id);
    this.clubs.get(club)?.delete(`c/${channel}/${id}`);
  }

  async usage(club: string): Promise<number> {
    guard(club, "state");

    let total = 0;

    for (const doc of this.clubs.get(club)?.values() ?? []) {
      total += Buffer.byteLength(JSON.stringify(doc));
    }

    return total;
  }

  async snapshotBytes(club: string, channel: string): Promise<number> {
    guard(club, channel);

    const doc = this.clubs.get(club)?.get(`s/${channel}`);

    return doc === undefined ? 0 : Buffer.byteLength(JSON.stringify(doc));
  }

  async deleteClub(club: string): Promise<void> {
    guard(club, "state");
    this.clubs.delete(club);
  }
}

/**
 * JSON files on disk, one directory per club.
 *
 * Chosen over a database because of what this actually holds: a handful of
 * small documents per club that are rewritten constantly and read a few
 * times a second. There is no query here, no relation, nothing to index -
 * and a directory of JSON is something a club's admin can back up, read,
 * and delete without being taught anything.
 */
export class FileStore implements Store {
  constructor(private readonly root: string) {}

  private snapshotPath(club: string, channel: string): string {
    return join(this.root, club, "snapshot", `${channel}.json`);
  }

  private itemPath(club: string, channel: string, id: string): string {
    return join(this.root, club, "collection", channel, `${id}.json`);
  }

  /**
   * Written to a neighbour and renamed over the top.
   *
   * A rename within a directory is atomic, so a reader either sees the old
   * document whole or the new one whole. Writing in place would give it a
   * third possibility - half of each - and the reader here is a race
   * results page that would then show nothing at all.
   */
  private async writeAtomic(path: string, doc: StoredDocument): Promise<void> {
    // Owner only (FILE_MODE). What is written here includes the club's pass
    // key, its PIN and invite hashes and new drivers' emails; nothing but
    // the relay itself has any business reading them, and a umask of 022
    // made every one of them readable by every other account on the host.
    await mkdir(dirname(path), { recursive: true, mode: DIR_MODE });

    // Random as well as the pid: two writes to the same document in the
    // same millisecond - two taps, say - would otherwise share a neighbour
    // and one could rename the other's half-written file into place.
    const temp = `${path}.${process.pid}.${randomBytes(6).toString("hex")}.tmp`;

    try {
      await writeFile(temp, JSON.stringify(doc), { encoding: "utf8", mode: FILE_MODE });
      await renameReplacing(temp, path);
    } catch (error) {
      // A full disk is the likely cause, and leaving the half-written
      // neighbour behind would only make it fuller.
      await unlink(temp).catch(() => undefined);
      throw error;
    }
  }

  /**
   * Removes the neighbours a crash left behind.
   *
   * Only ever called at startup, before the relay listens: nothing can be
   * mid-write then, so every *.tmp under the data folder is an orphan.
   * Links are not followed - one could point anywhere.
   */
  async sweepTemp(): Promise<number> {
    let removed = 0;

    const walk = async (dir: string): Promise<void> => {
      let names: string[];

      try {
        names = await readdir(dir);
      } catch {
        return;
      }

      for (const name of names) {
        const path = join(dir, name);
        const info = await lstat(path).catch(() => undefined);

        // And while it is here, anything an older build left readable by
        // other accounts is made the relay's own (see FILE_MODE): writes
        // only ever replaced a document, and the pass key is written once,
        // so without this it would stay as the old umask made it for good.
        if (info !== undefined && process.platform !== "win32" && (info.isDirectory() || info.isFile()) && (info.mode & 0o077) !== 0) {
          await chmod(path, info.mode & 0o700).catch(() => undefined);
        }

        if (info?.isDirectory() === true) {
          await walk(path);
        } else if (info?.isFile() === true && name.endsWith(".tmp")) {
          await unlink(path).then(
            () => removed++,
            () => undefined,
          );
        }
      }
    };

    await walk(this.root);
    return removed;
  }

  private async read(path: string): Promise<StoredDocument | undefined> {
    try {
      const text = await readFile(path, "utf8");
      const parsed: unknown = JSON.parse(text);

      return parsed as StoredDocument;
    } catch (error) {
      const code = (error as NodeJS.ErrnoException).code;

      if (code === "ENOENT") {
        return undefined;
      }

      // Thrown, not answered as "nothing here". A file that is present but
      // could not be read this time - a disk hiccup, a permission, half a
      // file - is not the same as a file that is missing: the relay holds
      // what it reads in memory, and remembering "nothing published" for a
      // check-in roster that is really there closed check-in until the
      // laptop next changed it. Each caller decides what an unreadable
      // file means for it, and none of them keeps the answer.
      console.error(`[relay] could not read ${path}:`, error);
      throw new StoreReadError(path, error instanceof SyntaxError, error);
    }
  }

  async putSnapshot(club: string, channel: string, doc: StoredDocument): Promise<void> {
    guard(club, channel);
    await this.writeAtomic(this.snapshotPath(club, channel), doc);
  }

  async getSnapshot(club: string, channel: string): Promise<StoredDocument | undefined> {
    guard(club, channel);
    return this.read(this.snapshotPath(club, channel));
  }

  async putItem(club: string, channel: string, id: string, doc: StoredDocument): Promise<void> {
    guard(club, channel, id);
    await this.writeAtomic(this.itemPath(club, channel, id), doc);
  }

  async getItem(club: string, channel: string, id: string): Promise<StoredDocument | undefined> {
    guard(club, channel, id);
    return this.read(this.itemPath(club, channel, id));
  }

  async listItems(club: string, channel: string): Promise<readonly { id: string; doc: StoredDocument }[]> {
    guard(club, channel);

    const dir = join(this.root, club, "collection", channel);
    let names: string[];

    try {
      names = await readdir(dir);
    } catch {
      return [];
    }

    const out: { id: string; doc: StoredDocument }[] = [];

    for (const name of names) {
      if (!name.endsWith(".json")) {
        continue;
      }

      const id = name.slice(0, -".json".length);

      // One bad file costs that file, not the whole list - as before.
      const doc = await this.read(join(dir, name)).catch(() => undefined);

      if (doc !== undefined) {
        out.push({ id, doc });
      }
    }

    return out;
  }

  async itemSizes(club: string, channel: string): Promise<readonly ItemSize[]> {
    guard(club, channel);

    const dir = join(this.root, club, "collection", channel);
    let names: string[];

    try {
      names = await readdir(dir);
    } catch {
      return [];
    }

    const out: ItemSize[] = [];

    for (const name of names) {
      if (!name.endsWith(".json")) {
        continue;
      }

      const info = await stat(join(dir, name)).catch(() => undefined);

      if (info !== undefined) {
        // mtime is when the rename landed, which is when it was published.
        out.push({ id: name.slice(0, -".json".length), bytes: info.size, at: info.mtimeMs });
      }
    }

    return out;
  }

  async deleteItem(club: string, channel: string, id: string): Promise<void> {
    guard(club, channel, id);
    await rm(this.itemPath(club, channel, id), { force: true });
  }

  async usage(club: string): Promise<number> {
    guard(club, "state");

    let total = 0;

    const walk = async (dir: string): Promise<void> => {
      let names: string[];

      try {
        names = await readdir(dir);
      } catch {
        return;
      }

      for (const name of names) {
        const info = await lstat(join(dir, name)).catch(() => undefined);

        if (info?.isDirectory() === true) {
          await walk(join(dir, name));
        } else if (info !== undefined) {
          total += info.size;
        }
      }
    };

    await walk(join(this.root, club));
    return total;
  }

  async snapshotBytes(club: string, channel: string): Promise<number> {
    guard(club, channel);

    // lstat, as usage() walks with, and counted by the same rule - anything
    // but a directory, at its size - so the two always agree about what
    // this one file costs. One that is not there, or cannot be looked at,
    // costs nothing here, just as usage() leaves out what it cannot look at.
    const info = await lstat(this.snapshotPath(club, channel)).catch(() => undefined);

    return info !== undefined && !info.isDirectory() ? info.size : 0;
  }

  async deleteClub(club: string): Promise<void> {
    guard(club, "state");
    await rm(join(this.root, club), { recursive: true, force: true });
  }
}

/**
 * Renames a finished temp file over its target.
 *
 * On Linux - where the relay runs on Fly - a rename over an existing file is
 * atomic and simply wins. Windows is different: while one rename is
 * replacing a file (or anything has it open for a moment - an antivirus
 * scan, a search indexer), a second rename onto the same name fails with
 * EPERM, EACCES or EBUSY instead of waiting. That only matters when the
 * relay is run on a Windows PC, but there two taps landing together would
 * lose one of them. So those three errors get a few short retries; any
 * other error, or one that outlasts the retries, is thrown as before.
 */
async function renameReplacing(from: string, to: string): Promise<void> {
  const transient = new Set(["EPERM", "EACCES", "EBUSY"]);

  for (let attempt = 0; ; attempt++) {
    try {
      await rename(from, to);
      return;
    } catch (error) {
      const code = (error as NodeJS.ErrnoException).code ?? "";

      if (!transient.has(code) || attempt >= 8) {
        throw error;
      }

      await new Promise((resolve) => setTimeout(resolve, 5 * (attempt + 1)));
    }
  }
}
