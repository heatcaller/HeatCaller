import { createHash } from "node:crypto";
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import type { CatalogueVoice } from "./voice.js";

/**
 * THE LAP VOICE (29 Oct 2026, asked for by Asher): the relay's spoken laps -
 * "Lap times in my ear" on the Live page and on a driver's own night - in an
 * ElevenLabs voice instead of the phone's own.
 *
 * NOT A LINE PER LAP. ElevenLabs records a fixed set of short clips ONCE -
 * every lap time from 3.00 to 19.99 (and the two halves of one with a
 * thousandth, "six nine" and "thirty-five"), "Lap 1" to "Lap 200", "P1" to "P30",
 * the gaps, "best lap" and the target words - and the phone plays the ones a
 * lap needs, one after the other. Made by lapvoicerender.ts on a key the
 * operator gives it, on their own PC; nothing on the relay ever calls
 * ElevenLabs for this, so a busy night costs nothing and no driver's name
 * goes anywhere.
 *
 * ONE VOICE FOR EVERY CLUB, whatever announcer a club picked in its Cast
 * tab: Daniel, ElevenLabs' "Steady Broadcaster". One set of clips is about
 * a dollar; one per voice would be a dollar per voice and a choice nobody
 * asked for. A stock voice, never a clone - the clips are heard by every
 * club, and a cloned voice is somebody's likeness (see CatalogueVoice).
 *
 * THE WORDS ARE THE APP'S. Numbers are spelled out the way RaceNumbers.cs in
 * the desktop app says them - "nine forty-two", "P four", "half a second
 * back" - because ElevenLabs read the same digits two ways on one night, and
 * a clip is heard thousands of times.
 *
 * WHERE THEY LIVE: relay/lap-voice/, beside this source. The clips are
 * git-ignored (they are made on somebody's own key) and go into the image
 * with the next fly deploy, so every club hears the same files. A relay with
 * none says so in its manifest and the pages use the phone's voice, as they
 * always did.
 */

/** The one voice. The id is ElevenLabs' own, from its voice list on 29 Oct 2026. */
export const LAP_VOICE: CatalogueVoice = Object.freeze({
  id: "lap-daniel",
  name: "Daniel",
  what: "Steady Broadcaster",
  voiceId: "onwK4e9ZLuTAKqWW03F9",
  // Flash: the cheapest model ElevenLabs has ($0.04 per 1,000 characters on
  // 29 Oct 2026), and the one the relay's voice already uses.
  modelId: "eleven_flash_v2_5",
  // A little steadier than the announcer's lines: a clip is one of two
  // thousand and they are heard next to each other, so they should match.
  stability: 0.6,
  similarity: 0.75,
  style: 0,
  speakerBoost: true,
  // The phone voice runs at 1.1 on these pages, for laps ten seconds apart.
  speed: 1.1,
  // Small enough that a lap's clips arrive at once on mobile data.
  outputFormat: "mp3_44100_64",
  cloned: false,
});

/** The folder name, relative to the relay folder (and to /app in the image). */
export const LAP_VOICE_DIR = "lap-voice";

/** Lap times with a clip, in hundredths: 3.00 to 19.99. Anything else is the phone's voice. */
export const LAP_TIME_MIN_CS = 300;
export const LAP_TIME_MAX_CS = 1999;
export const LAP_NUMBER_MAX = 200;
export const POSITION_MAX = 30;
/** Gaps past this many seconds, and more laps than LAPS_DOWN_MAX, are the phone's voice. */
export const GAP_SECONDS_MAX = 30;
export const LAPS_DOWN_MAX = 9;
/** Laps to go and minutes left with a clip (3 Nov 2026); more is not said. */
export const LAPS_LEFT_MAX = 50;
export const MINUTES_LEFT_MAX = 60;
/** How far off a target lap gets a clip, in hundredths: 2.00. */
export const TARGET_MARGIN_MAX_CS = 200;

/** ElevenLabs' Flash price per 1,000 characters, US dollars, read on 29 Oct 2026. */
export const FLASH_DOLLARS_PER_1K = 0.04;

export interface LapClip {
  /** The file name without ".mp3": letters, digits and hyphens only. */
  readonly name: string;
  /** What ElevenLabs is asked to say. */
  readonly text: string;
}

const SMALL = [
  "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
  "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
];
const TENS = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

/** RaceNumbers.Words: "thirty-four", "one hundred and five". */
export function words(n: number): string {
  if (n < 20) return SMALL[n] as string;
  if (n < 100) {
    const ones = n % 10;
    return ones === 0 ? (TENS[Math.floor(n / 10)] as string) : `${TENS[Math.floor(n / 10)]}-${SMALL[ones]}`;
  }
  const rest = n % 100;
  const hundreds = `${SMALL[Math.floor(n / 100)]} hundred`;
  return rest === 0 ? hundreds : `${hundreds} and ${words(rest)}`;
}

/** RaceNumbers.LapSpoken: "three flat", "nine oh six", "twelve thirty-four". */
export function lapTimeWords(cs: number): string {
  const whole = Math.floor(cs / 100);
  const hundredths = cs % 100;
  if (hundredths === 0) return `${words(whole)} flat`;
  return hundredths < 10 ? `${words(whole)} oh ${words(hundredths)}` : `${words(whole)} ${words(hundredths)}`;
}

/**
 * RaceNumbers.LapSpoken for a time in milliseconds (31 Oct 2026): the full
 * time the screen shows. "six nine thirty-five" for 6.935, "five oh oh six"
 * for 5.006; a time with nothing in the thousandths is lapTimeWords, "nine
 * oh six".
 */
export function lapTimeWordsMs(ms: number): string {
  if (ms % 10 === 0) return lapTimeWords(ms / 10);
  return `${tenthWords(Math.floor(ms / 100))} ${pairWords(ms % 100)}`;
}

/** The first half of a time with a thousandth: whole seconds and the tenth, "six nine", "six oh". */
export function tenthWords(ds: number): string {
  const tenth = ds % 10;
  return `${words(Math.floor(ds / 10))} ${tenth === 0 ? "oh" : SMALL[tenth]}`;
}

/** The second half: the last two digits as a pair, "thirty-five", "oh six". */
export function pairWords(pair: number): string {
  return pair < 10 ? `oh ${SMALL[pair]}` : words(pair);
}

/** The hundredths after "point", digit by digit: "three five", "three", "oh five". */
function pointDigits(h: number): string {
  if (h < 10) return `oh ${SMALL[h]}`;
  return h % 10 === 0 ? (SMALL[h / 10] as string) : `${SMALL[Math.floor(h / 10)]} ${SMALL[h % 10]}`;
}

/** How far off a target, in hundredths: "point three five", "one point oh five", "a second". */
export function marginWords(cs: number): string {
  const whole = Math.floor(cs / 100);
  const h = cs % 100;
  if (whole === 0) return `point ${pointDigits(h)}`;
  if (h === 0) return whole === 1 ? "a second" : `${words(whole)} seconds`;
  return `${words(whole)} point ${pointDigits(h)}`;
}

/**
 * Every clip there is, in the order they are made. The page picks names
 * from the same rules (LAPVOICE_JS in pages.ts), and a test holds the two
 * to each other.
 */
export function lapClips(): LapClip[] {
  const clips: LapClip[] = [
    { name: "on", text: "Lap times on" },
    // 3 Nov 2026, Asher: said as lap times are turned off.
    { name: "off", text: "Lap times off" },
    { name: "best", text: "best lap" },
    // After a margin clip ("point two"), for a lap slower than their best (3 Nov 2026).
    { name: "pb-off", text: "off your best" },
  ];

  for (let cs = LAP_TIME_MIN_CS; cs <= LAP_TIME_MAX_CS; cs++) {
    clips.push({ name: `t${cs}`, text: lapTimeWords(cs) });
  }

  // A time with a thousandth (31 Oct 2026, asked for by Asher: "make the
  // relay read sound the same" as the app). Not a clip per time - that is
  // seventeen thousand clips, ten times the cost and the image - but the
  // two halves the app says it in, played back to back with no breath:
  // "six nine" and "thirty-five". 170 + 90 clips.
  for (let ds = LAP_TIME_MIN_CS / 10; ds <= Math.floor(LAP_TIME_MAX_CS / 10); ds++) {
    clips.push({ name: `tt${ds}`, text: tenthWords(ds) });
  }
  for (let pair = 1; pair <= 99; pair++) {
    if (pair % 10 !== 0) clips.push({ name: `tp${pair}`, text: pairWords(pair) });
  }

  for (let n = 1; n <= LAP_NUMBER_MAX; n++) {
    clips.push({ name: `lap${n}`, text: `Lap ${words(n)}` });
  }

  // RaceNumbers.PositionSpoken: the letter stays a letter.
  for (let n = 1; n <= POSITION_MAX; n++) {
    clips.push({ name: `p${n}`, text: `P ${words(n)}` });
  }
  // Places gained or lost since the last lap (3 Nov 2026, Asher): a whole
  // clip each rather than "up to" before "P two", so it is said as one line.
  for (let n = 1; n <= POSITION_MAX; n++) {
    clips.push({ name: `pu${n}`, text: `Up to P ${words(n)}` });
  }
  for (let n = 1; n <= POSITION_MAX; n++) {
    clips.push({ name: `pd${n}`, text: `Down to P ${words(n)}` });
  }

  // RaceNumbers.Behind over GapSpoken: tenths under a second, halves to
  // ten, whole seconds after. A gap is the car ahead's, so it is "back".
  clips.push({ name: "g-0", text: "nothing in it" });
  gapClips(clips, "g", "back");
  // The same gaps to P1 and to the car behind (3 Nov 2026, Asher: "time to
  // P1"): "four seconds off the lead", "half a second clear". Nothing in it
  // is "g-0" for all three.
  gapClips(clips, "gl", "off the lead");
  gapClips(clips, "gb", "clear");

  // Laps or time left (3 Nov 2026), from the board's countdown.
  clips.push({ name: "lt-last", text: "last lap" });
  for (let n = 2; n <= LAPS_LEFT_MAX; n++) {
    clips.push({ name: `lt-l${n}`, text: `${words(n)} laps to go` });
  }
  for (let m = 1; m <= MINUTES_LEFT_MAX; m++) {
    clips.push({ name: `lt-m${m}`, text: m === 1 ? "a minute left" : `${words(m)} minutes left` });
  }
  for (let t = 10; t <= 50; t += 10) {
    clips.push({ name: `lt-s${t}`, text: `${words(t)} seconds left` });
  }

  // The target words, in two clips: how far, then which way.
  clips.push({ name: "tg-on", text: "on target" });
  clips.push({ name: "tg-under", text: "under target" });
  clips.push({ name: "tg-off", text: "off target" });
  for (let cs = 1; cs <= TARGET_MARGIN_MAX_CS; cs++) {
    clips.push({ name: `tg-m${cs}`, text: marginWords(cs) });
  }

  return clips;
}

/** One set of gap clips, prefix-t1 to prefix-l9, each ending in tail. */
function gapClips(clips: LapClip[], prefix: string, tail: string): void {
  for (let t = 1; t <= 9; t++) {
    const gap = t === 1 ? "a tenth" : t === 5 ? "half a second" : `${words(t)} tenths`;
    clips.push({ name: `${prefix}-t${t}`, text: `${gap} ${tail}` });
  }
  for (let halves = 2; halves <= 19; halves++) {
    const whole = Math.floor(halves / 2);
    const gap = halves % 2 === 0
      ? (whole === 1 ? "a second" : `${words(whole)} seconds`)
      : (whole === 1 ? "a second and a half" : `${words(whole)} and a half seconds`);
    clips.push({ name: `${prefix}-h${halves}`, text: `${gap} ${tail}` });
  }
  for (let s = 10; s <= GAP_SECONDS_MAX; s++) {
    clips.push({ name: `${prefix}-s${s}`, text: `${words(s)} seconds ${tail}` });
  }
  for (let l = 1; l <= LAPS_DOWN_MAX; l++) {
    clips.push({ name: `${prefix}-l${l}`, text: l === 1 ? `a lap ${tail}` : `${words(l)} laps ${tail}` });
  }
}

/** What one set of clips costs to make: ElevenLabs counts every character, spaces included. */
export function lapClipCost(clips: readonly LapClip[] = lapClips()): { clips: number; chars: number; dollars: number } {
  const chars = clips.reduce((sum, clip) => sum + clip.text.length, 0);
  return { clips: clips.length, chars, dollars: Math.round((chars / 1000) * FLASH_DOLLARS_PER_1K * 100) / 100 };
}

const CLIP_NAME = /^[a-z0-9-]{1,16}$/;

/** A clip that is too big to be one of these is not one of these. */
const MAX_CLIP_BYTES = 256 * 1024;

export interface LapVoiceManifest {
  readonly ok: true;
  /** The voice's name, for the page's note; nothing else about it. */
  readonly voice: string;
  /** Changes when any clip does - the page puts it on every clip's address. */
  readonly v: string;
  readonly clips: readonly string[];
}

/**
 * The clips the relay serves, read once when it starts. Every club gets the
 * same ones, with no token and nothing counted: they are a few thousand
 * stock recordings with nothing of any club's in them.
 *
 * Only names on the list are served, so a stray file in the folder is
 * never on the internet.
 */
export class LapVoiceLibrary {
  private readonly clips: ReadonlyMap<string, Buffer>;
  readonly manifest: LapVoiceManifest;

  constructor(clips: ReadonlyMap<string, Buffer>) {
    this.clips = clips;
    const names = lapClips().map((c) => c.name).filter((name) => clips.has(name));
    const hash = createHash("sha256");
    for (const name of names) {
      hash.update(name);
      hash.update(clips.get(name) as Buffer);
    }
    this.manifest = Object.freeze({
      ok: true,
      voice: LAP_VOICE.name,
      v: names.length > 0 ? hash.digest("hex").slice(0, 12) : "",
      clips: Object.freeze(names),
    });
  }

  /** The folder's clips, or none when there is no folder. Never throws: a relay without clips is a relay with the phone's voice. */
  static load(dir: string, log: (line: string) => void = () => {}): LapVoiceLibrary {
    const found = new Map<string, Buffer>();
    const wanted = new Set(lapClips().map((c) => c.name));
    let files: string[] = [];
    try {
      files = readdirSync(dir);
    } catch {
      log(`[lap voice] No ${dir}, so spoken laps use the phone's voice.`);
      return new LapVoiceLibrary(found);
    }

    for (const file of files) {
      const name = file.endsWith(".mp3") ? file.slice(0, -4) : "";
      if (!wanted.has(name)) continue;
      try {
        const path = join(dir, file);
        const size = statSync(path).size;
        if (size === 0 || size > MAX_CLIP_BYTES) continue;
        found.set(name, readFileSync(path));
      } catch {
        // One unreadable clip is one lap said by the phone.
      }
    }

    const library = new LapVoiceLibrary(found);
    log(found.size === 0
      ? `[lap voice] No clips in ${dir}, so spoken laps use the phone's voice.`
      : `[lap voice] ${found.size} of ${wanted.size} clips in the ${LAP_VOICE.name} voice.`);
    return library;
  }

  clip(name: string): Buffer | undefined {
    return CLIP_NAME.test(name) ? this.clips.get(name) : undefined;
  }
}
