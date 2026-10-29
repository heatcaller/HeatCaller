import { mkdir, readFile, rename, stat, writeFile } from "node:fs/promises";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { LAP_VOICE, LAP_VOICE_DIR, lapClipCost, lapClips, type LapClip } from "./lapvoice.js";
import { elevenLabsRenderer, VoiceUpstreamError } from "./voice.js";

/**
 * Makes the lap voice's clips on an ElevenLabs key (lap-voice-render.cmd
 * runs this). See lapvoice.ts for what they are.
 *
 * RUN ON YOUR OWN PC, NEVER ON THE RELAY. The key is read from
 * RELAY_VOICE_KEY or from voice-test.key, the file the local voice test
 * already uses, and goes nowhere but ElevenLabs.
 *
 * SAFE TO RUN AGAIN. A clip already in the folder is never asked for twice,
 * so a run that stopped half way - the PC slept, the key ran out - carries
 * on where it was and costs only what is left. "count" prints what a run
 * would make and cost, and spends nothing.
 */

export interface RenderReport {
  made: number;
  kept: number;
  failed: string[];
  /** Characters sent to ElevenLabs this run. */
  chars: number;
  /** Why the run stopped early, when it did. */
  stopped?: string;
}

export interface RenderOptions {
  readonly dir: string;
  readonly render: (text: string) => Promise<Buffer>;
  readonly clips?: readonly LapClip[];
  /** Clips asked for at once. ElevenLabs' Starter plan takes three. */
  readonly together?: number;
  readonly log?: (line: string) => void;
  /** Waits between tries, swappable for tests. */
  readonly wait?: (ms: number) => Promise<void>;
}

const sleep = (ms: number): Promise<void> => new Promise((done) => setTimeout(done, ms));

async function present(path: string): Promise<boolean> {
  try {
    return (await stat(path)).size > 0;
  } catch {
    return false;
  }
}

/** The clips the folder is missing. */
export async function missingClips(dir: string, clips: readonly LapClip[] = lapClips()): Promise<LapClip[]> {
  const missing: LapClip[] = [];
  for (const clip of clips) {
    if (!(await present(join(dir, `${clip.name}.mp3`)))) missing.push(clip);
  }
  return missing;
}

export async function renderLapClips(options: RenderOptions): Promise<RenderReport> {
  const all = options.clips ?? lapClips();
  const log = options.log ?? (() => {});
  const wait = options.wait ?? sleep;
  const report: RenderReport = { made: 0, kept: 0, failed: [], chars: 0 };

  await mkdir(options.dir, { recursive: true });
  const todo = await missingClips(options.dir, all);
  report.kept = all.length - todo.length;

  let next = 0;
  let stop: string | undefined;

  async function one(clip: LapClip): Promise<void> {
    for (let attempt = 1; attempt <= 5; attempt++) {
      try {
        const audio = await options.render(clip.text);
        report.chars += clip.text.length;
        const path = join(options.dir, `${clip.name}.mp3`);
        // Written beside and then renamed, so a run cut off mid-file never
        // leaves half a clip that the next run would keep.
        await writeFile(`${path}.part`, audio);
        await rename(`${path}.part`, path);
        report.made++;
        if (report.made % 100 === 0) log(`  ${report.made} of ${todo.length} made...`);
        return;
      } catch (error) {
        const status = error instanceof VoiceUpstreamError ? error.status : 0;
        // A wrong key or a spent allowance will not get better by asking
        // again: stop the whole run, and the next one carries on.
        if (status === 401 || status === 402 || status === 403) {
          stop = status === 401 && /quota/i.test(String(error))
            ? "ElevenLabs says this key has no characters left this month."
            : `ElevenLabs refused the key (${status}). Check voice-test.key.`;
          return;
        }
        if (attempt === 5) {
          report.failed.push(clip.name);
          log(`  ${clip.name} ("${clip.text}") failed: ${String(error).slice(0, 160)}`);
          return;
        }
        // Too many at once, or a hiccup: wait longer each time.
        await wait(status === 429 ? 4000 * attempt : 1000 * attempt);
      }
    }
  }

  async function worker(): Promise<void> {
    while (stop === undefined && next < todo.length) {
      const clip = todo[next++] as LapClip;
      await one(clip);
    }
  }

  await Promise.all(Array.from({ length: Math.max(1, options.together ?? 2) }, () => worker()));
  if (stop !== undefined) report.stopped = stop;
  return report;
}

async function readKey(relayDir: string): Promise<string> {
  const fromEnv = (process.env.RELAY_VOICE_KEY ?? "").trim();
  if (fromEnv !== "") return fromEnv;
  try {
    return ((await readFile(join(relayDir, "voice-test.key"), "utf8")).split(/\r?\n/)[0] ?? "").trim();
  } catch {
    return "";
  }
}

async function main(argv: readonly string[]): Promise<number> {
  const relayDir = process.cwd();
  const dir = join(relayDir, LAP_VOICE_DIR);
  const all = lapClips();
  const cost = lapClipCost(all);
  const missing = await missingClips(dir, all);
  const left = lapClipCost(missing);

  console.log(`The lap voice: ${LAP_VOICE.name} (${LAP_VOICE.what}), ElevenLabs voice ${LAP_VOICE.voiceId}, model ${LAP_VOICE.modelId}.`);
  console.log(`All of it: ${cost.clips} clips, ${cost.chars.toLocaleString("en-US")} characters, about $${cost.dollars.toFixed(2)} at Flash's $0.04 per 1,000.`);
  console.log(missing.length === 0
    ? `Every clip is already in ${dir}.`
    : `Still to make: ${left.clips} clips, ${left.chars.toLocaleString("en-US")} characters, about $${left.dollars.toFixed(2)}.`);

  // 3 tells lap-voice-render.cmd there is nothing to ask about.
  if (missing.length === 0) return argv.includes("count") ? 3 : 0;
  if (argv.includes("count")) return 0;

  const key = await readKey(relayDir);
  if (key === "") {
    console.log("");
    console.log("No ElevenLabs key. Put it on the first line of voice-test.key in the relay folder, then run this again.");
    return 1;
  }

  console.log(`Making them in ${dir}...`);
  const render = elevenLabsRenderer(key);
  const report = await renderLapClips({
    dir,
    render: (text) => render(LAP_VOICE, text),
    clips: all,
    log: (line) => console.log(line),
  });

  console.log("");
  console.log(`Made ${report.made}, ${report.chars.toLocaleString("en-US")} characters. ${report.kept} were already there.`);
  if (report.failed.length > 0) console.log(`${report.failed.length} failed; run this again to try those.`);
  if (report.stopped !== undefined) {
    console.log(report.stopped);
    return 1;
  }

  const done = (await missingClips(dir, all)).length === 0;
  console.log(done
    ? "Every clip is made. Run fly deploy --ha=false from the relay folder and every club hears them."
    : "Some clips are still missing. Run this again to make them; the phone's voice says any lap they would have.");
  return done ? 0 : 1;
}

if (process.argv[1] !== undefined && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main(process.argv.slice(2)).then(
    (code) => process.exit(code),
    (error: unknown) => {
      console.error(String(error));
      process.exit(1);
    },
  );
}
