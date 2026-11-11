import { mkdir, readFile, rename, stat, writeFile } from "node:fs/promises";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { LAP_DOLLARS_PER_1K, LAP_VOICE, LAP_VOICE_DIR, lapClipCost, lapClips, type LapClip } from "./lapvoice.js";
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
 *
 * AND THE FOLDER SAYS WHICH MODEL MADE IT (11 Nov 2026). LAP_VOICE moved to
 * eleven_v4_turbo on 10 Nov, but a clip counted as made if its file was
 * there - so the 8 Nov v3 takes went on being played under a v4 label, and
 * nothing would ever have made them again. Now a run writes the model it
 * made the set on into MODEL_FILE, and a folder made on another model (or
 * before this file existed) is a set to make again: the old one is moved
 * aside to lap-voice-<its model>, kept rather than lost, and the set is made
 * afresh on LAP_VOICE's model. "count" says so first, and spends nothing.
 */

/** The file in the clips folder that says which model made them. */
export const MODEL_FILE = "MODEL";

/** Which model the clips in <paramref name="dir"/> were made on - undefined when it doesn't say. */
export async function clipsModel(dir: string): Promise<string | undefined> {
  try {
    const said = (await readFile(join(dir, MODEL_FILE), "utf8")).trim();
    return said.length > 0 ? said : undefined;
  } catch {
    return undefined;
  }
}

/**
 * Whether the clips in the folder are another model's: some are there, and
 * the folder says a different model made them, or says nothing at all (a
 * set made before MODEL_FILE existed). An empty folder is nobody's.
 */
export async function madeOnAnotherModel(dir: string, model: string, clips: readonly LapClip[] = lapClips()): Promise<boolean> {
  const said = await clipsModel(dir);
  if (said === model) return false;

  for (const clip of clips) {
    if (await present(join(dir, `${clip.name}.mp3`))) return true;
  }

  return false;
}

/** Where a set made on <paramref name="model"/> is kept when it is replaced - "lap-voice-eleven_v3", or "lap-voice-older" for a set that never said. */
export function setAsideDir(dir: string, model: string | undefined): string {
  const tag = (model ?? "older").replace(/[^A-Za-z0-9_.-]/g, "-");
  return `${dir}-${tag}`;
}

export interface RenderReport {
  made: number;
  kept: number;
  failed: string[];
  /** Characters sent to ElevenLabs this run. */
  chars: number;
  /** Why the run stopped early, when it did. */
  stopped?: string;
  /** Where another model's set was moved to, when there was one. */
  setAside?: string;
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
  /** The model the clips are made on - LAP_VOICE's unless a test says otherwise. Written to MODEL_FILE. */
  readonly model?: string;
}

const sleep = (ms: number): Promise<void> => new Promise((done) => setTimeout(done, ms));

async function exists(path: string): Promise<boolean> {
  try {
    await stat(path);
    return true;
  } catch {
    return false;
  }
}

async function present(path: string): Promise<boolean> {
  try {
    return (await stat(path)).size > 0;
  } catch {
    return false;
  }
}

/** The clips the folder is missing - all of them when it holds another model's set (madeOnAnotherModel). */
export async function missingClips(dir: string, clips: readonly LapClip[] = lapClips(), model: string = LAP_VOICE.modelId): Promise<LapClip[]> {
  if (await madeOnAnotherModel(dir, model, clips)) return [...clips];

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
  const model = options.model ?? LAP_VOICE.modelId;
  const report: RenderReport = { made: 0, kept: 0, failed: [], chars: 0 };

  // Another model's set is kept, beside it, and this one starts empty.
  if (await madeOnAnotherModel(options.dir, model, all)) {
    const was = await clipsModel(options.dir);
    let aside = setAsideDir(options.dir, was);

    // Never over a set already kept there.
    for (let n = 2; await exists(aside); n++) {
      aside = `${setAsideDir(options.dir, was)}-${n}`;
    }

    await rename(options.dir, aside);
    report.setAside = aside;
    log(`The clips there were made on ${was ?? "an older model"}; kept in ${aside}, and made again on ${model}.`);
  }

  await mkdir(options.dir, { recursive: true });

  // Said before the first clip, so a run that stops half way carries on as
  // this model's set rather than starting again.
  await writeFile(join(options.dir, MODEL_FILE), `${model}\n`);

  const todo = await missingClips(options.dir, all, model);
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
  console.log(`All of it: ${cost.clips} clips, ${cost.chars.toLocaleString("en-US")} characters, about $${cost.dollars.toFixed(2)} at $${LAP_DOLLARS_PER_1K.toFixed(2)} per 1,000 on ${LAP_VOICE.modelId}.`);

  if (await madeOnAnotherModel(dir, LAP_VOICE.modelId, all)) {
    const was = await clipsModel(dir);
    console.log(`The clips in ${dir} were made on ${was ?? "an older model (before the folder said which)"}, not ${LAP_VOICE.modelId} -`);
    console.log(`so every one is made again, and the old set is kept in ${setAsideDir(dir, was)}.`);
  }
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
