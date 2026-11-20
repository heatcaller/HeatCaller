import { mkdir, readdir, readFile, rm } from "node:fs/promises";
import { join } from "node:path";
import { writeFileAtomically } from "./voice.js";

/**
 * SAMPLES OF THE VOICES DRIVERS MAY PICK FOR THEIR CREW (19 Nov 2026,
 * plans/driver-crew-plan.md 2.1).
 *
 * An ElevenLabs voice comes with its own preview clip, which the phone
 * plays straight from ElevenLabs. A HeatCaller or a Kokoro voice has none
 * a phone can reach, so the club's PC renders one once (HeatCaller's free
 * sample, or a line in the Kokoro voice) and leaves it here, under the
 * voice's crew key - twelve hex characters, a hash the laptop makes, never
 * the voice's id. The laptop asks which keys are here first and sends only
 * the ones that are not.
 *
 * NOBODY'S WORDS. A sample is a fixed line in a voice, with no name in it,
 * so it is served to anyone who asks for the key - the same voice demo for
 * every phone - and cached. A club's samples go when it deletes everything.
 */

/** A voice's crew key, as the laptop makes it (DriverCrew.KeyOf). */
export const CREW_SAMPLE_KEY = /^[0-9a-f]{12}$/;

/** The most one sample may be: a short line at 64 kbps is under 100 KB. */
export const CREW_SAMPLE_MAX_BYTES = 512 * 1024;

/** The most samples one club may keep - driverme's own cap on voices (CREW_MAX_VOICES). */
export const CREW_SAMPLE_MAX_PER_CLUB = 200;

/** The folder under dataDir, with a dot so no club id can name it. */
export const CREW_SAMPLE_DIR = ".crewsamples";

export interface CrewSampleVault {
  /** The keys a club has samples for. */
  keys(club: string): Promise<string[]>;
  put(club: string, key: string, bytes: Buffer): Promise<void>;
  /** The sample, or undefined when there is none. */
  read(club: string, key: string): Promise<Buffer | undefined>;
  /** Every one of a club's samples - when the club deletes everything. */
  removeClub(club: string): Promise<void>;
}

export class MemoryCrewSampleVault implements CrewSampleVault {
  private readonly clubs = new Map<string, Map<string, Buffer>>();

  private of(club: string): Map<string, Buffer> {
    let held = this.clubs.get(club);

    if (held === undefined) {
      held = new Map();
      this.clubs.set(club, held);
    }

    return held;
  }

  async keys(club: string): Promise<string[]> {
    return [...this.of(club).keys()];
  }

  async put(club: string, key: string, bytes: Buffer): Promise<void> {
    this.of(club).set(key, Buffer.from(bytes));
  }

  async read(club: string, key: string): Promise<Buffer | undefined> {
    return this.of(club).get(key);
  }

  async removeClub(club: string): Promise<void> {
    this.clubs.delete(club);
  }
}

/** The samples on disk: dataDir/.crewsamples/<club>/<key>.mp3, written atomically. */
export class DiskCrewSampleVault implements CrewSampleVault {
  private readonly root: string;

  constructor(dataDir: string) {
    this.root = join(dataDir, CREW_SAMPLE_DIR);
  }

  private file(club: string, key: string): string {
    if (!CREW_SAMPLE_KEY.test(key)) {
      throw new Error("Not a crew voice key.");
    }

    return join(this.root, club, `${key}.mp3`);
  }

  async keys(club: string): Promise<string[]> {
    try {
      return (await readdir(join(this.root, club)))
        .filter((name) => name.endsWith(".mp3"))
        .map((name) => name.slice(0, -4))
        .filter((key) => CREW_SAMPLE_KEY.test(key));
    } catch {
      return [];
    }
  }

  async put(club: string, key: string, bytes: Buffer): Promise<void> {
    await mkdir(join(this.root, club), { recursive: true, mode: 0o700 });
    await writeFileAtomically(this.file(club, key), bytes);
  }

  async read(club: string, key: string): Promise<Buffer | undefined> {
    if (!CREW_SAMPLE_KEY.test(key)) {
      return undefined;
    }

    try {
      return await readFile(this.file(club, key));
    } catch {
      return undefined;
    }
  }

  async removeClub(club: string): Promise<void> {
    await rm(join(this.root, club), { recursive: true, force: true });
  }
}
