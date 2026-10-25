import { createHash } from "node:crypto";
import { readFile, readdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { join, relative, sep } from "node:path";

/**
 * Which source this build was made from, so the relay admin app can tell
 * when the relay on Fly is older than the relay folder on its PC (v1.3).
 *
 * The hash covers every .ts file under src/, by its path from src/ (with
 * forward slashes, in ordinal order) and its text with Windows line endings
 * read as plain ones - a checkout on Windows and the copy Fly builds from
 * hash the same. The admin app works it out the same way
 * (HeatCaller.Admin.Core, BuildInfo.SourceHash).
 *
 * `npm run build` writes it next to the compiled relay as build-info.json;
 * the Dockerfile copies dist/src, so it ships with the build it describes.
 */
export interface BuildInfo {
  readonly version: string;
  readonly sourceHash: string;
  readonly builtAt: string;
}

async function tsFiles(root: string, folder: string, found: string[]): Promise<void> {
  for (const entry of await readdir(folder, { withFileTypes: true })) {
    const path = join(folder, entry.name);

    if (entry.isDirectory()) {
      await tsFiles(root, path, found);
    } else if (entry.isFile() && entry.name.endsWith(".ts")) {
      found.push(relative(root, path).split(sep).join("/"));
    }
  }
}

/** The source hash of every .ts file under `srcDir`: lower-case hex SHA-256. */
export async function sourceHash(srcDir: string): Promise<string> {
  const files: string[] = [];
  await tsFiles(srcDir, srcDir, files);
  files.sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));

  const hash = createHash("sha256");

  for (const file of files) {
    const text = (await readFile(join(srcDir, ...file.split("/")), "utf8")).replace(/\r\n/g, "\n");
    hash.update(file, "utf8");
    hash.update("\0", "utf8");
    hash.update(text, "utf8");
    hash.update("\0", "utf8");
  }

  return hash.digest("hex");
}

/** The build-info.json beside the compiled relay, or null for a build from before it (or a broken file). */
export async function readBuildInfo(path: string = fileURLToPath(new URL("./build-info.json", import.meta.url))): Promise<BuildInfo | null> {
  try {
    const raw = JSON.parse(await readFile(path, "utf8")) as Partial<BuildInfo>;

    return typeof raw.version === "string" && typeof raw.sourceHash === "string" && /^[0-9a-f]{64}$/.test(raw.sourceHash) && typeof raw.builtAt === "string"
      ? { version: raw.version, sourceHash: raw.sourceHash, builtAt: raw.builtAt }
      : null;
  } catch {
    return null;
  }
}

/** `node dist/src/buildinfo.js <srcDir> <out.json>` - run by `npm run build`. */
async function main(argv: readonly string[]): Promise<void> {
  const [srcDir, out] = argv;

  if (srcDir === undefined || out === undefined) {
    throw new Error("usage: node dist/src/buildinfo.js <src folder> <build-info.json>");
  }

  const pkg = JSON.parse(await readFile(join(srcDir, "..", "package.json"), "utf8")) as { version?: unknown };
  const info: BuildInfo = {
    version: typeof pkg.version === "string" ? pkg.version : "0.0.0",
    sourceHash: await sourceHash(srcDir),
    builtAt: new Date().toISOString(),
  };

  await writeFile(out, JSON.stringify(info, null, 2) + "\n", "utf8");
}

if (process.argv[1] !== undefined && fileURLToPath(import.meta.url) === process.argv[1]) {
  main(process.argv.slice(2)).catch((error: unknown) => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  });
}
