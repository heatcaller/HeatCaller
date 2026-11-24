import { copyFile, mkdir, readFile, rename, stat, writeFile } from "node:fs/promises";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import {
  LAP_DOLLARS_PER_1K,
  LAP_VOICE,
  LAP_VOICE_DIR,
  LAP_VOICE_NAMES,
  lapClipCost,
  lapClips,
  type LapClip,
  VOICE_BEFORE_VOICE_FILE,
  VOICE_FILE,
} from "./lapvoice.js";
import { ElevenLabsLogging, elevenLabsRenderer, noLoggingFromEnv, VoiceUpstreamError } from "./voice.js";

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
 *
 * AND WHICH VOICE (24 Nov 2026, Asher: the lap voice moves from Daniel to
 * Finley before ElevenLabs retires Daniel on 31 Dec). Only the model was
 * checked, and clips are found by file name - so changing LAP_VOICE's voice
 * would have made nothing at all: every Daniel clip was "already there", and
 * the phones would have gone on playing Daniel under Finley's name. Now a run
 * writes the voice id into VOICE_FILE beside MODEL_FILE, and a set made in
 * another voice is set aside the same way, as lap-voice-daniel (or the old
 * voice's id). A folder with a MODEL file and no VOICE file is the set made
 * before VOICE existed, which was Daniel's.
 */

/** The file in the clips folder that says which model made them. */
export const MODEL_FILE = "MODEL";

export { VOICE_BEFORE_VOICE_FILE, VOICE_FILE };

/** The folder an old set is kept in, by the voice it was made in ("daniel"). Any other voice is kept under its id. */
function voiceTag(voice: string): string {
  const name = LAP_VOICE_NAMES[voice];
  return name !== undefined ? name.toLowerCase() : voice;
}

async function readSaid(dir: string, file: string): Promise<string | undefined> {
  try {
    const said = (await readFile(join(dir, file), "utf8")).trim();
    return said.length > 0 ? said : undefined;
  } catch {
    return undefined;
  }
}

/** Which model the clips in <paramref name="dir"/> were made on - undefined when it doesn't say. */
export async function clipsModel(dir: string): Promise<string | undefined> {
  return readSaid(dir, MODEL_FILE);
}

/** Which voice the clips in <paramref name="dir"/> were made in - Daniel's when the folder doesn't say (VOICE_BEFORE_VOICE_FILE). */
export async function clipsVoice(dir: string): Promise<string> {
  return (await readSaid(dir, VOICE_FILE)) ?? VOICE_BEFORE_VOICE_FILE;
}

/** What a set is made with: an ElevenLabs model and voice id. */
export interface LapSetMaker {
  readonly model: string;
  readonly voice: string;
}

/**
 * THE RULE, kept pure so it can be tested on its own: whether a set the
 * folder says was made with <paramref name="said"/> (model undefined when
 * there is no MODEL file) is another set from the one wanted, and if so the
 * tag of the folder it is kept in. Undefined means it is this set.
 *
 * Another voice is tagged by the voice ("daniel"), since that is what
 * somebody looking for the old set would know it by; otherwise another
 * model by the model ("eleven_v3", "older" for a set that never said).
 * Whether the folder holds any clips at all is the caller's question.
 */
export function anotherSetTag(said: { readonly model: string | undefined; readonly voice: string }, wanted: LapSetMaker): string | undefined {
  if (said.voice !== wanted.voice) return voiceTag(said.voice);
  if (said.model !== wanted.model) return said.model ?? "older";
  return undefined;
}

/**
 * Whether the clips in the folder are another set's, and the tag it is kept
 * under: some clips are there, and the folder says another model or voice
 * made them (or says nothing - see anotherSetTag). An empty folder is
 * nobody's.
 */
export async function madeAnotherWay(dir: string, wanted: LapSetMaker, clips: readonly LapClip[] = lapClips()): Promise<string | undefined> {
  const tag = anotherSetTag({ model: await clipsModel(dir), voice: await clipsVoice(dir) }, wanted);
  if (tag === undefined) return undefined;

  for (const clip of clips) {
    if (await present(join(dir, `${clip.name}.mp3`))) return tag;
  }

  return undefined;
}

/** LAP_VOICE's model and voice: what a set is made with unless a test says otherwise. */
export const LAP_SET: LapSetMaker = Object.freeze({ model: LAP_VOICE.modelId, voice: LAP_VOICE.voiceId });

/** Where a set is kept when it is replaced - "lap-voice-daniel", "lap-voice-eleven_v3", or "lap-voice-older" for a set that never said. */
export function setAsideDir(dir: string, tag: string | undefined): string {
  const safe = (tag ?? "older").replace(/[^A-Za-z0-9_.-]/g, "-");
  return `${dir}-${safe}`;
}

/** Files in the clips folder that describe the folder, not the set, and stay with it when a set is moved aside. */
const FOLDER_FILES = ["README.md"];

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
  /** The ElevenLabs voice id they are made in - LAP_VOICE's unless a test says otherwise. Written to VOICE_FILE. */
  readonly voice?: string;
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

/** The clips the folder is missing - all of them when it holds another model's or voice's set (madeAnotherWay). */
export async function missingClips(dir: string, clips: readonly LapClip[] = lapClips(), wanted: LapSetMaker = LAP_SET): Promise<LapClip[]> {
  if ((await madeAnotherWay(dir, wanted, clips)) !== undefined) return [...clips];

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
  const wanted: LapSetMaker = { model: options.model ?? LAP_VOICE.modelId, voice: options.voice ?? LAP_VOICE.voiceId };
  const report: RenderReport = { made: 0, kept: 0, failed: [], chars: 0 };

  // Another model's or voice's set is kept, beside it, and this one starts empty.
  const tag = await madeAnotherWay(options.dir, wanted, all);
  if (tag !== undefined) {
    let aside = setAsideDir(options.dir, tag);

    // Never over a set already kept there.
    for (let n = 2; await exists(aside); n++) {
      aside = `${setAsideDir(options.dir, tag)}-${n}`;
    }

    await rename(options.dir, aside);
    report.setAside = aside;
    log(`The clips there were another set (${tag}); kept in ${aside}, and made again in voice ${wanted.voice} on ${wanted.model}.`);

    // WHY (24 Nov 2026, Asher: the Daniel to Finley switch): the whole folder
    // moves, and it took the folder's own README (tracked in git) with it,
    // so git showed it deleted after every switch. The README describes the
    // folder, not a set, so it comes back.
    await mkdir(options.dir, { recursive: true });
    for (const file of FOLDER_FILES) {
      try {
        await copyFile(join(aside, file), join(options.dir, file));
      } catch {
        // Not there to carry over.
      }
    }
  }

  await mkdir(options.dir, { recursive: true });

  // Said before the first clip, so a run that stops half way carries on as
  // this set rather than starting again.
  await writeFile(join(options.dir, MODEL_FILE), `${wanted.model}\n`);
  await writeFile(join(options.dir, VOICE_FILE), `${wanted.voice}\n`);

  const todo = await missingClips(options.dir, all, wanted);
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

  const tag = await madeAnotherWay(dir, LAP_SET, all);
  if (tag !== undefined) {
    const was = await clipsModel(dir);
    const wasVoice = await clipsVoice(dir);
    console.log(wasVoice !== LAP_SET.voice
      ? `The clips in ${dir} are in ${LAP_VOICE_NAMES[wasVoice] !== undefined ? `${LAP_VOICE_NAMES[wasVoice]}'s` : "another"} voice (${wasVoice}), not ${LAP_VOICE.name} (${LAP_VOICE.voiceId}) -`
      : `The clips in ${dir} were made on ${was ?? "an older model (before the folder said which)"}, not ${LAP_VOICE.modelId} -`);
    console.log(`so every one is made again, and the old set is kept in ${setAsideDir(dir, tag)}.`);
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
  // The same "don't keep our lines" as the relay (24 Nov 2026): each clip is
  // also taken out of the key's ElevenLabs history once it is in hand. The
  // clips carry no names, so this is only tidiness and the same behaviour in
  // both places; the log says once if the key can't.
  const logging = new ElevenLabsLogging(noLoggingFromEnv(process.env["RELAY_VOICE_NO_LOGGING"]), (line) => console.log(line));
  const render = elevenLabsRenderer(key, undefined, logging);
  const report = await renderLapClips({
    dir,
    render: (text) => render(LAP_VOICE, text),
    clips: all,
    log: (line) => console.log(line),
  });

  // The deletes are sent without waiting; wait for them here, or
  // process.exit cuts the last few off.
  await logging.settled();

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
