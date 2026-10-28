import { lstat, readdir, rm } from "node:fs/promises";
import { join, resolve, sep } from "node:path";

/**
 * THE PODIUM VIDEOS' LEFTOVERS, CLEARED ONCE (28 Oct 2026).
 *
 * From 21 to 28 Oct 2026 the laptop sent the top three's highlight videos
 * here, and the relay kept them in dataDir/.videos/<club>/ (an mp4 and a
 * json beside it, a week each, up to 1 GB a club). Asher asked for them to
 * go: "Remove the podium videos for downloading from the relay and just
 * have locally." The laptop keeps making them; they just stay on it.
 *
 * With the routes gone nothing would ever read or sweep that folder again,
 * so a relay that ran the old build would carry up to a gigabyte a club of
 * drivers' faces and names on its volume for good. This runs once at
 * start-up and removes it.
 *
 * SAFE BY CONSTRUCTION. Only the one folder, dataDir/.videos - the dot
 * means no club id can ever have named it, so it is nobody's published
 * data. It is checked to be a real directory (not a link that could point
 * anywhere else on the machine) and to sit directly inside dataDir before
 * anything is removed. Missing is the normal case after the first start
 * and is not an error. A failure is logged and the relay starts anyway: a
 * relay that is up is worth more than a tidy disk.
 */

/** The old podium video folder's name under dataDir. */
export const OLD_VIDEO_DIR = ".videos";

/** What was found and removed - for the log line and the tests. */
export interface LeftoverVideos {
  /** Files removed (mp4s and their json beside them). */
  readonly files: number;
  /** Their size together. */
  readonly bytes: number;
}

/**
 * Deletes dataDir/.videos and everything in it, and says what went.
 * Never throws.
 */
export async function removeLeftoverVideos(
  dataDir: string,
  log: (line: string) => void = () => {},
): Promise<LeftoverVideos> {
  const root = resolve(dataDir);
  const folder = join(root, OLD_VIDEO_DIR);

  // Belt and braces: the path is built from a constant, but nothing is
  // removed unless it is exactly <dataDir>/.videos.
  if (resolve(folder) !== root + sep + OLD_VIDEO_DIR) {
    return { files: 0, bytes: 0 };
  }

  try {
    const stat = await lstat(folder);

    if (!stat.isDirectory()) {
      // A file or a link where the folder was: not something this build
      // wrote, so it is left alone and said.
      log(`[relay] ${folder} is not a folder - left as it is`);
      return { files: 0, bytes: 0 };
    }
  } catch {
    // Not there: already cleared, or this relay never kept a video.
    return { files: 0, bytes: 0 };
  }

  let files = 0;
  let bytes = 0;

  try {
    const counted = await count(folder);
    files = counted.files;
    bytes = counted.bytes;

    await rm(folder, { recursive: true, force: true });

    log(
      `[relay] removed the old podium videos: ${files} file(s), ${(bytes / (1024 * 1024)).toFixed(1)} MB from ${folder}` +
        " (they stay on the club's laptop now)",
    );
  } catch (error) {
    log(`[relay] could not remove the old podium videos in ${folder}: ${error instanceof Error ? error.message : String(error)}`);
  }

  return { files, bytes };
}

/** Files and bytes under a folder, without following links. */
async function count(folder: string): Promise<LeftoverVideos> {
  let files = 0;
  let bytes = 0;

  for (const entry of await readdir(folder, { withFileTypes: true })) {
    const path = join(folder, entry.name);

    if (entry.isDirectory()) {
      const inner = await count(path);
      files += inner.files;
      bytes += inner.bytes;
    } else {
      files += 1;
      bytes += await lstat(path).then((s) => s.size, () => 0);
    }
  }

  return { files, bytes };
}
