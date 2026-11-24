import { createHash } from "node:crypto";
import { AutoTopUps } from "./autotopup.js";
import { access, lchown, mkdir, readdir, readFile, rename, rm, stat, unlink, utimes, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { isValidClubId } from "./tenancy.js";

/**
 * The HeatCaller voice: ElevenLabs, spoken through the relay, so a club
 * needs no account and no key of its own.
 *
 * WHY THIS EXISTS AT ALL. A club's laptop could hold an ElevenLabs key -
 * that is what "bring your own key" still does - but a key HeatCaller
 * hands out sits on a shared machine several volunteers use, and
 * ElevenLabs' own terms say credentials are not shared. So the key stays
 * here, on one server, and the laptop asks this relay for the audio with
 * the club token it already has. See claude/elevenlabs-managed-voices.md.
 *
 * WHAT THIS MUST NEVER DO:
 *  - let a club choose the voice id, the model or the voice settings. It
 *    names one of the voices HeatCaller offers, by a short id, and this
 *    module decides what that means. A club that could name a model could
 *    name the dearest one;
 *  - let a club's text through unbounded. Every request is capped, and
 *    every character that actually reaches ElevenLabs is counted against
 *    that club's allowance for the month;
 *  - answer with anything but audio. The voice id and the key never leave
 *    this process.
 *
 * WHAT MAKES IT AFFORDABLE. Two caches. The laptop keeps every line it has
 * heard (VoiceLibrary), so a club asks once. And this relay keeps a copy
 * of every line it renders: lines with no name or number in them - "Green
 * flag!", "Cars on the grid" - are the same words for every club in the
 * country, so they are rendered ONCE, ever, and served from disk after
 * that, free. Lines carrying a driver's name are kept per club and never
 * shared, because they are personal data and some of the drivers are
 * children.
 *
 * HEARING A VOICE BEFORE CHOOSING IT. Two more things, both open to
 * people rather than only to laptops: a SAMPLE of each voice reading one
 * fixed neutral line, served to anyone with no token at all, and a
 * PREVIEW of a club's own words charged to a small daily allowance of its
 * own. See "Hearing a voice before choosing it" below. The club's month
 * is never touched by either, which is the whole reason the second one
 * has an allowance of its own.
 */

/** One voice HeatCaller offers, as the config describes it. The voiceId and the key never leave this process. */
export interface CatalogueVoice {
  /** The short id a club stores and sends, e.g. "clubhouse". */
  readonly id: string;
  /** What the app shows in its list, e.g. "Clubhouse". */
  readonly name: string;
  /** One line under the name, e.g. "Warm, clear, easy over a PA". */
  readonly what: string;
  /** The ElevenLabs voice. Never sent to a club. */
  readonly voiceId: string;
  readonly modelId: string;
  readonly stability: number;
  readonly similarity: number;
  readonly style: number;
  readonly speakerBoost: boolean;
  readonly speed: number;
  /** ElevenLabs output_format, e.g. "mp3_44100_128". Blank leaves it to ElevenLabs. */
  readonly outputFormat: string;
  /**
   * This voice is a CLONE of a real person - a club's own announcer, who
   * recorded half an hour once so the app would sound like the person the
   * room already knows.
   *
   * IT CHANGES TWO THINGS, AND BOTH OF THEM ARE ABOUT WHOSE VOICE IT IS.
   *
   * A cloned voice is somebody's likeness. The club holds the recording
   * and the consent; this relay holds a voice id and renders lines with
   * it. Everything that would let that voice be heard by anyone the
   * consent does not cover has to be off, and neither of the two was off
   * by default - they were written for a catalogue of stock voices, where
   * both are exactly right:
   *
   *   1. THE SHARED CACHE. A line with no name in it is kept once for
   *      every club - "Green flag!" is the same words everywhere, and
   *      rendering it a hundred times is a hundred times the bill. But
   *      "the same words" is not "the same voice": in a cloned voice that
   *      render is a recording of a named person, and serving it to
   *      another club is handing out their likeness. See speak.
   *
   *   2. THE PUBLIC SAMPLE. /voice/sample/<id> needs no token at all and
   *      is open to the internet on purpose - a stock sample is one
   *      neutral line in a voice nobody owns, and somebody choosing a
   *      voice should be able to hear it before they buy anything. A
   *      cloned voice's sample is a real person's voice on an address
   *      anyone can fetch. See samples and sample.
   *
   * Default false, so a catalogue written before this flag existed keeps
   * behaving exactly as it did.
   */
  readonly cloned: boolean;
}

/** What one club may do with the HeatCaller voice. */
export interface VoicePlan {
  readonly enabled: boolean;
  /** Characters this club may send to ElevenLabs in a calendar month. Cache hits cost nothing and are not counted. */
  readonly monthlyChars: number;
  /** Which catalogue ids this club may use. Empty means every stock voice in the catalogue - a cloned one only ever when named. */
  readonly voices: readonly string[];
}

/** The voice block of the relay config: the catalogue and the defaults every club inherits. */
export interface VoiceConfig {
  readonly voices: readonly CatalogueVoice[];
  readonly monthlyChars: number;
  readonly maxChars: number;
  /**
   * Characters a club may spend AUDITIONING voices in a day (UTC) - its
   * own small allowance, nothing to do with the month's.
   *
   * Optional here, and resolved to the default in the service, because
   * readVoiceConfig always fills it and a VoiceConfig written by hand
   * (the tests, mostly) should not have to know about previews to say
   * what a catalogue is. Zero switches previews off and leaves the
   * samples working.
   */
  readonly previewDailyChars?: number;
  /** The hardest cap on one preview. A preview is a sentence, not a recap. Optional for the same reason as above. */
  readonly previewMaxChars?: number;
}

export const VOICE_DEFAULT_MONTHLY_CHARS = 100_000;
export const VOICE_DEFAULT_MAX_CHARS = 600;

/**
 * A day of auditioning, by default: about fifteen to thirty previews.
 *
 * Small on purpose and separate on purpose. The whole reason previews have
 * an allowance of their own is that a club must never arrive on a race
 * night to find it spent its voice on Tuesday choosing one, so this number
 * is sized for somebody sitting at Voice Settings making up their mind -
 * not for a club, and not for a day's racing.
 */
export const VOICE_DEFAULT_PREVIEW_DAILY_CHARS = 3_000;

/** One preview, at most. Shorter than a flag call, let alone a recap. */
export const VOICE_DEFAULT_PREVIEW_MAX_CHARS = 200;

/**
 * The one line every sample speaks, in every voice.
 *
 * The same words in each, so two samples can actually be compared - a
 * voice that sounded better reading a different sentence tells a club
 * nothing. Neutral: no club name, no driver's name, no flag call, so it
 * is nobody's personal data and it is not a recording of a race. About
 * eight seconds of speech, which is long enough to hear a voice's warmth
 * and short enough that nobody closes the page first.
 */
export const VOICE_SAMPLE_LINE =
  "Drivers, this is your two minute call for the next heat. Make your way to the rostrum and check your transponder.";

/** A club's standing with the voice, as the app shows it: "34,120 of 100,000 characters this month". */
export interface VoiceUsage {
  readonly used: number;
  readonly allowance: number;
  /** When the count goes back to zero: the first moment of next month, UTC, ISO 8601. */
  readonly resetsAt: string;
  /**
   * False when the usage file is there but could not be read, so used is a
   * stand-in rather than the club's count. Absent means known.
   *
   * WHY (20 Oct 2026, Bug Hunt 8, n101): an unreadable file used to come
   * back as plain "0 used", and the app took that as the club's real
   * figure - its meter dropped to nothing spent and a spent month ended
   * early, mixing voices on the night. Now the stand-in says it is one.
   */
  readonly known?: boolean;
}

/**
 * One meter as the usage route reports it - the voice's characters or the
 * brain's lines - read without spending anything. allowance includes
 * topUp. A meter the club's plan does not include reads enabled:false and
 * an allowance of 0, with whatever it has used still shown: a club that
 * was switched off mid-month still spent it.
 */
export interface MeterUsage {
  readonly enabled: boolean;
  readonly used: number;
  readonly allowance: number;
  readonly topUp: number;
  readonly resetsAt: string;
  /**
   * False when the club's count could not be read and `used` is a stand-in
   * 0 (22 Oct 2026, bug hunt) - the app keeps its last real figure rather
   * than showing a spent month as empty.
   */
  readonly known: boolean;
}

export interface VoiceCatalogueView {
  readonly enabled: boolean;
  readonly voices: readonly { id: string; name: string; what: string; rev: string; cloned: boolean }[];
  readonly usage: VoiceUsage;
}

/** Everything that can go wrong, in the app's terms rather than HTTP's. */
export type VoiceRefusal =
  /** No ElevenLabs key on this relay - the HeatCaller voice is not switched on here at all. */
  | "off"
  /** This club's plan does not include it. */
  | "not-included"
  /** A voice id this club may not use, or that is not in the catalogue. */
  | "unknown-voice"
  /** Empty text, or longer than maxChars. */
  | "text"
  /** The club has spent its characters for this month. */
  | "allowance"
  /** Too many lines being rendered at once, here or at ElevenLabs. */
  | "busy"
  /** ElevenLabs itself refused or could not be reached. */
  | "upstream";

export interface SpeakOk {
  readonly ok: true;
  readonly audio: Buffer;
  /** True when it came from this relay's cache, so nothing was spent. */
  readonly cached: boolean;
  readonly usage: VoiceUsage;
  /** Characters sent to ElevenLabs for this line - absent when it came from the cache. For the usage log. */
  readonly characters?: number;
  /** The ElevenLabs model it was spoken with. For the usage log. */
  readonly model?: string;
}

export interface SpeakNo {
  readonly ok: false;
  readonly refusal: VoiceRefusal;
  readonly status: number;
  readonly message: string;
  readonly usage?: VoiceUsage;
}

export type SpeakResult = SpeakOk | SpeakNo;

/**
 * A line being spoken as it arrives (11 Nov 2026): raw 16-bit mono PCM at
 * 22.05 kHz (STREAM_FORMAT), piece by piece. Read it to the end, or stop
 * early - either way the slot is given back and the characters are settled
 * (refunded when nothing at all came).
 */
export interface SpeakStreaming {
  readonly ok: true;
  readonly stream: AsyncIterable<Uint8Array>;
  readonly usage: VoiceUsage;
  readonly characters: number;
  readonly model: string;
  /** Gives the slot and the characters back for a stream that was never read. Safe after it was read, and twice. */
  readonly cancel: () => Promise<void>;
}

export type SpeakStreamResult = SpeakResult | SpeakStreaming;

/** What a streamed line is sent as - ElevenLabs' own name for it, which the laptop asks for too. */
export const STREAM_FORMAT = "pcm_22050";

/** What calls ElevenLabs' stream endpoint: the line's audio as it comes. Swapped in tests; absent when no key is configured. */
export type StreamFn = (voice: CatalogueVoice, text: string, signal: AbortSignal) => Promise<AsyncIterable<Uint8Array>>;

/**
 * One voice's sample, served to anyone at all.
 *
 * No usage in it, either way: a sample is not charged to a club - there is
 * no club, nobody is signed in - and what it costs HeatCaller is one
 * render per voice in the catalogue, ever. See VoiceService.sample.
 */
export interface SampleOk {
  readonly ok: true;
  readonly audio: Buffer;
  /** True when it was already to hand, so nothing was rendered. Every ask but the first. */
  readonly cached: boolean;
}

export interface SampleNo {
  readonly ok: false;
  readonly status: number;
  readonly message: string;
}

export type SampleResult = SampleOk | SampleNo;

/** One voice there is a sample of, as the public list names it. The ElevenLabs voice id is not in it, as everywhere else. */
export interface SampleListing {
  readonly id: string;
  readonly name: string;
}

/** What actually calls ElevenLabs. Swapped in tests, and absent when no key is configured. */
export type RenderFn = (voice: CatalogueVoice, text: string) => Promise<Buffer>;

/** ElevenLabs answered, and said no. Carries the status so the caller can tell a refusal from a dropped connection. */
export class VoiceUpstreamError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message);
    this.name = "VoiceUpstreamError";
  }
}

export interface VoiceServiceOptions {
  readonly config: VoiceConfig;
  readonly dataDir: string;
  /** Absent means no key: every request is refused with "off", and nothing is rendered. */
  readonly render?: RenderFn | undefined;
  /** Streams a line as ElevenLabs makes it - see speakStream. Absent: a stream asked for is answered whole. */
  readonly stream?: StreamFn | undefined;
  /** The longest a streamed line may go without a piece arriving. See DEFAULT_STREAM_IDLE_MS. */
  readonly streamIdleMs?: number;
  readonly now?: () => Date;
  readonly log?: (line: string) => void;
  /**
   * Renders in flight at once, across every club. ElevenLabs' own limit on
   * a Scale plan is 15 (30 for the Flash models); this stays well under so
   * a busy Saturday cannot turn into 429s, which would cost a club its
   * flag call rather than merely its place in the queue.
   */
  readonly maxInFlight?: number;
  /** Waiting for a slot beyond this many is answered "busy" rather than queued behind a wall. */
  readonly maxWaiting?: number;
  /** Of maxInFlight, the most one club may hold at once. See DEFAULT_MAX_IN_FLIGHT_PER_CLUB. */
  readonly maxInFlightPerClub?: number;
  /** Of maxWaiting, the most one club may hold at once. */
  readonly maxWaitingPerClub?: number;
  readonly sharedCacheBytes?: number;
  readonly clubCacheBytes?: number;
}

const DEFAULT_MAX_IN_FLIGHT = 6;
const DEFAULT_MAX_WAITING = 40;

/**
 * One club's share of the two above.
 *
 * The slots are every club's, and without a share of its own one club's
 * Prepare Lines - a hundred lines, fired as fast as the laptop can send
 * them - could hold all six renders and every place in the queue, so the
 * club racing that evening had its flag call answered "busy" by somebody
 * else's rehearsal. Two at once is what the laptop itself sends; ten
 * waiting is a burst and a half behind them. Past either, the answer is
 * "busy", the same answer the whole relay being full gives, and the
 * laptop's Kokoro voice says the line.
 */
const DEFAULT_MAX_IN_FLIGHT_PER_CLUB = 2;
const DEFAULT_MAX_WAITING_PER_CLUB = 10;

/**
 * Where everything the voice keeps lives, under the data folder.
 *
 * With a dot in front, because the data folder is also where every club's
 * own folder is, by its id - and it used to be "voice", which is a club id
 * like any other. A club called "voice" that deleted everything it had
 * published took every club's usage and every cached line with it; its
 * publishes landed in the middle of the voice's own files. No club id can
 * start with a dot (isValidClubId), so no club can ever name this one.
 * See migrateVoiceFolder for a relay that still has the old one.
 */
export const VOICE_DIR = ".voice";

/** Where VOICE_DIR used to be. */
const OLD_VOICE_DIR = "voice";

/**
 * Where the samples anyone at all can hear are kept, under VOICE_DIR.
 *
 * A folder of their own rather than the shared cache, which is swept
 * oldest-used-first when it goes over its size: a swept sample would be
 * paid for again by the next visitor, and the point of a sample is that it
 * is paid for once. There are only ever as many as there are voices in the
 * catalogue, so there is nothing here to sweep.
 */
const SAMPLES_DIR = "samples";

/**
 * Where each club's day of previews is counted, under VOICE_DIR.
 *
 * Its own folder, so a day's preview count can never be read as a month's
 * character count or written over one. That confusion is the one thing
 * this feature must not be able to cause: the month's file holds three
 * years of what a club is billed by.
 */
const PREVIEW_DIR = "preview";

/**
 * The slots a sample render takes, under a name no club can have.
 *
 * A club id may never start with a dot (isValidClubId), so this can never
 * collide with a real club - the same reason VOICE_DIR has one. It means a
 * crawler walking the sample list is held to one club's share of the
 * relay's renders and cannot take a race night's with it.
 */
const SAMPLE_SLOT_HOLDER = ".samples";

/**
 * How long a sample ElevenLabs would not speak is left alone.
 *
 * Without this, ElevenLabs being down while anything at all walks the
 * sample list is an unbounded run of failed requests on HeatCaller's
 * account: the render never lands, so nothing is ever cached, so every
 * visitor tries again. A minute makes that one attempt a minute per voice,
 * and is short enough that nobody waiting for the key to come back
 * notices.
 */
const SAMPLE_RETRY_AFTER_MS = 60 * 1000;

/** Lines with no names in them, kept for every club. Small files; a whole announcer style is a few megabytes. */
const DEFAULT_SHARED_CACHE_BYTES = 200 * 1024 * 1024;

/** One club's own lines - driver names, mostly. The laptop keeps its own copy too, so this is a convenience, not a store of record. */
const DEFAULT_CLUB_CACHE_BYTES = 25 * 1024 * 1024;

/**
 * The most an ElevenLabs answer may be before it is treated as a fault.
 * A capped line of 600 characters is a few seconds of speech - a couple of
 * hundred kilobytes at the formats offered here.
 */
const MAX_AUDIO_BYTES = 4 * 1024 * 1024;

/**
 * The longest a streamed line may go without a piece arriving before it is
 * given up. ElevenLabs can answer 200 and then send nothing; with no limit
 * the line kept one of its club's two slots until the connection died of
 * its own accord, minutes later, long after the laptop had said the line
 * in Kokoro. Audio that is flowing arrives many times a second.
 */
const DEFAULT_STREAM_IDLE_MS = 15_000;

export class VoiceService {
  private readonly config: VoiceConfig;
  private readonly dataDir: string;
  private readonly render: RenderFn | undefined;
  private readonly streamer: StreamFn | undefined;
  private readonly streamIdleMs: number;
  private readonly now: () => Date;
  private readonly log: (line: string) => void;
  private readonly maxInFlight: number;
  private readonly maxWaiting: number;
  private readonly sharedCacheBytes: number;
  private readonly clubCacheBytes: number;
  private readonly previewDailyChars: number;
  private readonly previewMaxChars: number;

  private readonly byId = new Map<string, CatalogueVoice>();
  private readonly revs = new Map<string, string>();

  /** Usage writes, one club at a time, so two lines finishing together cannot both read 10 and both write 11. */
  private readonly usageLocks = new Map<string, Promise<void>>();
  private readonly usageCache = new Map<string, { month: string; used: number; history: Readonly<Record<string, number>> }>();

  /** Each club's top-up file, as last read, by its modification time - see topUpFor. */
  private readonly topUpCache = new Map<string, { mtimeMs: number; months: Readonly<Record<string, number>> }>();

  /** Preview counts, one club at a time, as usageLocks does - and in a map of its own, so an audition never waits behind a flag call. */
  private readonly previewLocks = new Map<string, Promise<void>>();
  private readonly previewCache = new Map<string, { day: string; used: number }>();

  /** Each sample, once it has been rendered or read off the volume. A handful of voices at a couple of hundred kilobytes each. */
  private readonly sampleAudio = new Map<string, Buffer>();

  /**
   * Samples being rendered right now, by voice id, so two first visitors
   * arriving together are one render and not two. Every one of them waits
   * on the same promise and they all get the same bytes.
   */
  private readonly samplesInFlight = new Map<string, Promise<Buffer | undefined>>();

  /**
   * Lines being rendered right now by speak, keyed by where they will be
   * cached ("shared" or the club) and the cache key, so a second request
   * for the same line waits for the first instead of rendering - and being
   * charged for - it again. 17 Oct 2026, Bug Hunt 8, A22-3.
   */
  private readonly linesInFlight = new Map<string, Promise<Buffer | undefined>>();

  /** When a sample render last failed, by voice id - see SAMPLE_RETRY_AFTER_MS. */
  private readonly sampleFailedAt = new Map<string, number>();

  private readonly maxInFlightPerClub: number;
  private readonly maxWaitingPerClub: number;
  /** VOICE_DIR, in this relay's data folder. */
  private readonly root: string;

  private inFlight = 0;
  private waiting = 0;
  private readonly queue: { readonly club: string; readonly go: () => void }[] = [];
  /** Each club's share of inFlight and waiting - see DEFAULT_MAX_IN_FLIGHT_PER_CLUB. Only clubs with one or the other are held. */
  private readonly clubSlots = new Map<string, { inFlight: number; waiting: number }>();

  /** Prunes already running, per folder, so a busy minute does not start a dozen sweeps. */
  private readonly pruning = new Set<string>();

  constructor(options: VoiceServiceOptions) {
    this.config = options.config;
    this.dataDir = options.dataDir;
    this.render = options.render;
    this.streamer = options.stream;
    this.streamIdleMs = options.streamIdleMs ?? DEFAULT_STREAM_IDLE_MS;
    this.now = options.now ?? (() => new Date());
    this.log = options.log ?? (() => {});
    this.maxInFlight = options.maxInFlight ?? DEFAULT_MAX_IN_FLIGHT;
    this.maxWaiting = options.maxWaiting ?? DEFAULT_MAX_WAITING;
    this.maxInFlightPerClub = options.maxInFlightPerClub ?? DEFAULT_MAX_IN_FLIGHT_PER_CLUB;
    this.maxWaitingPerClub = options.maxWaitingPerClub ?? DEFAULT_MAX_WAITING_PER_CLUB;
    this.root = join(this.dataDir, VOICE_DIR);
    this.sharedCacheBytes = options.sharedCacheBytes ?? DEFAULT_SHARED_CACHE_BYTES;
    this.clubCacheBytes = options.clubCacheBytes ?? DEFAULT_CLUB_CACHE_BYTES;
    this.previewDailyChars = this.config.previewDailyChars ?? VOICE_DEFAULT_PREVIEW_DAILY_CHARS;
    this.previewMaxChars = this.config.previewMaxChars ?? VOICE_DEFAULT_PREVIEW_MAX_CHARS;

    for (const voice of this.config.voices) {
      this.byId.set(voice.id, voice);
      this.revs.set(voice.id, revisionOf(voice));
    }
  }

  /** True when this relay has an ElevenLabs key at all. */
  get available(): boolean {
    return this.render !== undefined && this.config.voices.length > 0;
  }

  /**
   * What the app shows on Voice Settings: the voices this club may pick
   * from, and where its allowance stands. Cheap - one small file read.
   *
   * The rev is a fingerprint of everything that shapes a voice's audio. The
   * laptop keeps it with each saved line, so changing a voice's settings
   * here re-renders lines rather than playing old ones that no longer
   * match.
   */
  async catalogue(club: string, plan: VoicePlan): Promise<VoiceCatalogueView> {
    // While the usage file can't be read this is 0 of 0, known:false -
    // not the stand-in "0 used" it once was. See usageFor (20 Oct 2026,
    // Bug Hunt 8, n101).
    const usage = await this.usageFor(club, plan);

    return {
      enabled: this.available && plan.enabled,
      voices: this.voicesFor(plan).map((voice) => ({
        id: voice.id,
        name: voice.name,
        what: voice.what,
        rev: this.revs.get(voice.id) ?? "",

        // TOLD TO THE CLUB WHOSE VOICE IT IS, which is not a leak: this
        // route takes that club's own token, and the catalogue it answers
        // with is already filtered to the voices that club may use. The
        // app needs it so its own Voice Settings page can say what the
        // club is choosing and ask about consent. Nothing tokenless ever
        // carries this.
        cloned: voice.cloned,
      })),
      usage,
    };
  }

  /**
   * Where this club's month stands, for the usage route and the manager's
   * phone: the count, the plan with any top-up, and nothing spent. Read the
   * same way the catalogue reads it - one stat for the top-up, the count
   * from memory once it has been read.
   */
  async usage(club: string, plan: VoicePlan): Promise<MeterUsage> {
    const month = monthOf(this.now());
    const enabled = this.available && plan.enabled;
    const topUp = enabled ? await this.topUpFor(club, month) : 0;
    const state = await this.usageState(club, plan, month);

    return {
      enabled,
      used: state.usage.used,
      allowance: enabled ? plan.monthlyChars + topUp : 0,
      topUp,
      resetsAt: state.usage.resetsAt,
      known: state.known,
    };
  }

  /**
   * The catalogue entries a club may use, in config order.
   *
   * An empty list is every STOCK voice, never a clone: a cloned voice is
   * one club's announcer, and only the club whose list names it has the
   * consent to make it say anything. See CatalogueVoice.cloned.
   */
  private voicesFor(plan: VoicePlan): readonly CatalogueVoice[] {
    if (plan.voices.length === 0) {
      return this.config.voices.filter((voice) => !voice.cloned);
    }

    return this.config.voices.filter((voice) => plan.voices.includes(voice.id));
  }

  /**
   * One line, spoken. From this relay's cache when it has been said before
   * - by this club, or by any club when the line carries no name - and
   * otherwise from ElevenLabs, counted against the club's month.
   *
   * @param shared The laptop says whether this line can be kept for every
   * club: true only for a line rendered straight from a template with no
   * values filled in. Anything with a driver's name in it arrives false.
   */
  async speak(
    club: string,
    plan: VoicePlan,
    voiceId: string,
    text: string,
    shared: boolean,
    fresh = false,
  ): Promise<SpeakResult> {
    const checked = this.checkLine(club, plan, voiceId, text);

    if (!checked.ok) {
      return checked;
    }

    const { voice, spoken, render } = checked;

    return this.speakChecked(club, plan, voice, spoken, render, shared, fresh);
  }

  /**
   * Whether this club may have this line in this voice at all - the club,
   * the voice being switched on, the plan, the voice being one of the
   * club's, the length - and the line as it will be spoken. Shared by speak
   * and speakStream (11 Nov 2026), so a streamed line is held to exactly
   * what a whole one is.
   */
  private checkLine(
    club: string,
    plan: VoicePlan,
    voiceId: string,
    text: string,
  ): SpeakNo | { readonly ok: true; readonly voice: CatalogueVoice; readonly spoken: string; readonly render: RenderFn } {
    if (!isValidClubId(club)) {
      return { ok: false, refusal: "unknown-voice", status: 404, message: "No such club." };
    }

    if (this.render === undefined || this.config.voices.length === 0) {
      return {
        ok: false,
        refusal: "off",
        status: 503,
        message: "The HeatCaller voice isn't switched on. Use your own ElevenLabs key or the free Kokoro voice for now.",
      };
    }

    const render = this.render;

    if (!plan.enabled) {
      return {
        ok: false,
        refusal: "not-included",
        status: 403,
        message: "The HeatCaller voice isn't included for this club. Use your own ElevenLabs key or the free Kokoro voice.",
      };
    }

    const voice = this.byId.get(voiceId);

    if (voice === undefined || !this.voicesFor(plan).includes(voice)) {
      return {
        ok: false,
        refusal: "unknown-voice",
        status: 400,
        message: "That voice isn't one this club can use. Pick one from the list on Voice Settings.",
      };
    }

    // Tags like [excited] are v3's and v4's; every other model reads them out as
    // words. The laptop cannot know which model a HeatCaller voice uses -
    // that is deliberately not published - so the tags are taken out here.
    const spoken = readsAudioTags(voice.modelId) ? text.trim() : stripAudioTags(text);

    if (spoken.length === 0) {
      return { ok: false, refusal: "text", status: 400, message: "There was nothing to say." };
    }

    if (spoken.length > this.config.maxChars) {
      return {
        ok: false,
        refusal: "text",
        status: 400,
        message: `That line is longer than the ${this.config.maxChars} characters the HeatCaller voice speaks at once.`,
      };
    }

    return { ok: true, voice, spoken, render };
  }

  /** The rest of speak, once checkLine has passed the line. */
  private async speakChecked(
    club: string,
    plan: VoicePlan,
    voice: CatalogueVoice,
    spoken: string,
    render: RenderFn,
    shared: boolean,
    fresh: boolean,
  ): Promise<SpeakResult> {
    // A CLONED VOICE IS NEVER KEPT ACROSS CLUBS, whatever the laptop
    // said. See CatalogueVoice.cloned.
    //
    // The laptop decides `shared` by looking the words up among its own
    // shipped templates - "Green flag!" is the same words at every club,
    // so it is rendered once and kept for all of them. That is right for
    // a stock voice and wrong for a clone, because the thing being kept
    // is not the words, it is a recording of a named person saying them.
    // One club's announcer would end up calling another club's flags.
    //
    // DECIDED HERE RATHER THAN ON THE LAPTOP, and that is the point: the
    // laptop is not told which voices are clones of whom, and a rule that
    // depends on a club's own app being up to date is a rule that leaks
    // the first time somebody runs an old build. The relay knows, so the
    // relay decides.
    //
    // It costs this club a little more - its own cache still works, so
    // only the cross-club saving is lost - and it is the club whose voice
    // it is, so it is the right club to pay.
    const sharedLine = shared && !voice.cloned;

    // A FRESH TAKE (22 Oct 2026, known issues: "a bad take can't be
    // re-rolled"). The Re-roll button on Voice Settings asks for the line
    // again, past every cache, charged like any line that was not saved.
    // The new take is kept in the CLUB'S OWN cache even for a line every
    // club shares: one club not liking how "Green flag!" came out is no
    // reason to change it for every other club, and that club pays for it.
    // A shared line therefore looks in the club's own cache first.
    const keepFor = fresh ? false : sharedLine;

    const key = cacheKey(voice, spoken);
    const cached = fresh
      ? undefined
      : (sharedLine ? await this.readCache(club, false, key) : undefined) ?? (await this.readCache(club, keepFor, key));

    if (cached !== undefined) {
      // Free: nothing reached ElevenLabs, so nothing is counted. This is
      // the whole point of the cache, and of Prepare Lines on the laptop.
      return { ok: true, audio: cached, cached: true, usage: await this.usageFor(club, plan) };
    }

    // ONE RENDER PER LINE AT A TIME. Two requests for the same uncached
    // line - Prepare Lines and the live announcer together, or two clubs
    // asking for the same shared line - used to both reserve, both render
    // and both be charged. The second now waits for the first and is
    // given its audio, free, as a cache hit would be. If the first was
    // refused or failed (perhaps against another club's allowance), the
    // second goes on down its own path: it never inherits a refusal, and
    // never skips its own reserve. 17 Oct 2026, Bug Hunt 8, A22-3.
    const flight = `${keepFor ? "shared" : club}|${key}${fresh ? "|fresh" : ""}`;
    const already = this.linesInFlight.get(flight);

    if (already !== undefined) {
      const theirs = await already;

      if (theirs !== undefined) {
        return { ok: true, audio: theirs, cached: true, usage: await this.usageFor(club, plan) };
      }
    }

    let settle: (audio: Buffer | undefined) => void = () => undefined;
    const mine = new Promise<Buffer | undefined>((resolve) => {
      settle = resolve;
    });
    // Another waiter released by the same failure may have got here first;
    // then this one just renders on its own rather than queue again.
    const owner = !this.linesInFlight.has(flight);

    if (owner) {
      this.linesInFlight.set(flight, mine);
    }

    let made: Buffer | undefined;

    try {
      const result = await this.speakUncached(club, plan, voice, spoken, keepFor, key, render);

      if (result.ok) {
        made = result.audio;
      }

      return result;
    } finally {
      settle(made);

      if (owner && this.linesInFlight.get(flight) === mine) {
        this.linesInFlight.delete(flight);
      }
    }
  }

  /**
   * The rest of speak, for a line that was not in the cache: reserve,
   * render, cache. Split out so speak can register it as in flight (see
   * linesInFlight). 17 Oct 2026, Bug Hunt 8, A22-3.
   */
  private async speakUncached(
    club: string,
    plan: VoicePlan,
    voice: CatalogueVoice,
    spoken: string,
    keepFor: boolean,
    key: string,
    render: RenderFn,
  ): Promise<SpeakResult> {
    // Taken BEFORE the render, under the usage lock, rather than checked
    // now and counted at the end. A render takes seconds, and a club's
    // laptop can have several in flight: checking first let every one of
    // them read the same count, pass, and spend - so the allowance was a
    // suggestion rather than a limit. What is not spent is given back below.
    const reserved = await this.reserve(club, plan, spoken.length);

    if (!reserved.ok && reserved.unreadable === true) {
      return {
        ok: false,
        refusal: "busy",
        status: 503,
        message: "The HeatCaller voice can't check this club's allowance just now. The free Kokoro voice speaks this one.",
        // WHY (20 Oct 2026, Bug Hunt 8, n101): no usage here. The figures
        // reserve has are a stand-in of 0 used, and sending them reset the
        // app's This Month meter and its stored allowance to "nothing
        // spent". With none, the refusal carries no figures and the app
        // keeps what it last knew.
      };
    }

    if (!reserved.ok) {
      return {
        ok: false,
        refusal: "allowance",
        status: 402,
        message:
          "This club has used its HeatCaller voice allowance for the month. " +
          "The free Kokoro voice speaks until it resets, and every line already saved is kept.",
        usage: reserved.usage,
      };
    }

    const usage = reserved.usage;
    const slot = await this.takeSlot(club);

    if (!slot) {
      await this.refund(club, spoken.length, reserved.month);

      return {
        ok: false,
        refusal: "busy",
        status: 503,
        message: "The HeatCaller voice is busy. The free Kokoro voice speaks this one.",
        usage: await this.usageFor(club, plan),
      };
    }

    let audio: Buffer;

    try {
      audio = await render(voice, spoken);
    } catch (error) {
      const status = error instanceof VoiceUpstreamError ? error.status : 0;

      this.log(`[voice] "${club}": ElevenLabs did not speak a line (${status === 0 ? String(error) : status}).`);

      // Nothing was rendered, so nothing was spent.
      await this.refund(club, spoken.length, reserved.month);

      return {
        ok: false,
        refusal: "upstream",
        status: 502,
        message: "The HeatCaller voice couldn't speak that line. The free Kokoro voice says it instead.",
        usage: await this.usageFor(club, plan),
      };
    } finally {
      this.releaseSlot(club);
    }

    if (audio.length === 0 || audio.length > MAX_AUDIO_BYTES) {
      this.log(`[voice] "${club}": ElevenLabs answered with ${audio.length} bytes, which is not a line.`);

      // Charged by ElevenLabs, most likely, but not by us: a club must not
      // pay its allowance for something it cannot play.
      await this.refund(club, spoken.length, reserved.month);

      return {
        ok: false,
        refusal: "upstream",
        status: 502,
        message: "The HeatCaller voice couldn't speak that line. The free Kokoro voice says it instead.",
        usage: await this.usageFor(club, plan),
      };
    }

    // The characters are already counted (see reserve). A cache write that
    // fails costs the club nothing beyond rendering the line again.
    await this.writeCache(club, keepFor, key, audio);

    return { ok: true, audio, cached: false, usage, characters: spoken.length, model: voice.modelId };
  }

  /**
   * One line, spoken AS IT IS MADE (11 Nov 2026, voice speed plan) - for a
   * club laptop with "Start race calls while ElevenLabs is still sending
   * them" ticked. The first audio reaches the laptop in about a fifth of a
   * second instead of after the whole line.
   *
   * HELD TO EVERYTHING SPEAK IS (checkLine): the club, the plan, the voice
   * being one of the club's, the length; then the month's allowance is
   * reserved, and a slot taken, before ElevenLabs is asked. What is not
   * spoken is given back: all of it when ElevenLabs refuses or nothing at
   * all arrives.
   *
   * ANSWERED WHOLE, as speak would, for anything this cannot stream: a line
   * shared across clubs (kept once, for all of them, which needs the whole
   * clip), a fresh take, a line this club's cache already has (free), or a
   * relay with no stream configured. The laptop plays either.
   *
   * A streamed line is not kept in the relay's cache - the laptop keeps its
   * own copy of what it says, and a line with a driver's name and a lap time
   * in it is rarely said twice.
   */
  async speakStream(
    club: string,
    plan: VoicePlan,
    voiceId: string,
    text: string,
    shared: boolean,
    fresh = false,
  ): Promise<SpeakStreamResult> {
    const checked = this.checkLine(club, plan, voiceId, text);

    if (!checked.ok) {
      return checked;
    }

    const { voice, spoken, render } = checked;
    const streamer = this.streamer;

    if (streamer === undefined || fresh || (shared && !voice.cloned)) {
      return this.speakChecked(club, plan, voice, spoken, render, shared, fresh);
    }

    const key = cacheKey(voice, spoken);
    const cached = await this.readCache(club, false, key);

    if (cached !== undefined) {
      return { ok: true, audio: cached, cached: true, usage: await this.usageFor(club, plan) };
    }

    const reserved = await this.reserve(club, plan, spoken.length);

    if (!reserved.ok && reserved.unreadable === true) {
      return {
        ok: false,
        refusal: "busy",
        status: 503,
        message: "The HeatCaller voice can't check this club's allowance just now. The free Kokoro voice speaks this one.",
      };
    }

    if (!reserved.ok) {
      return {
        ok: false,
        refusal: "allowance",
        status: 402,
        message:
          "This club has used its HeatCaller voice allowance for the month. " +
          "The free Kokoro voice speaks until it resets, and every line already saved is kept.",
        usage: reserved.usage,
      };
    }

    if (!(await this.takeSlot(club))) {
      await this.refund(club, spoken.length, reserved.month);

      return {
        ok: false,
        refusal: "busy",
        status: 503,
        message: "The HeatCaller voice is busy. The free Kokoro voice speaks this one.",
        usage: await this.usageFor(club, plan),
      };
    }

    const stop = new AbortController();
    let upstream: AsyncIterable<Uint8Array>;

    try {
      upstream = await streamer(voice, spoken, stop.signal);
    } catch (error) {
      const status = error instanceof VoiceUpstreamError ? error.status : 0;
      this.releaseSlot(club);
      this.log(`[voice] "${club}": ElevenLabs did not stream a line (${status === 0 ? String(error) : status}).`);
      await this.refund(club, spoken.length, reserved.month);

      return {
        ok: false,
        refusal: "upstream",
        status: 502,
        message: "The HeatCaller voice couldn't speak that line. The free Kokoro voice says it instead.",
        usage: await this.usageFor(club, plan),
      };
    }

    // The slot and the characters are settled however the reader stops:
    // played to the end, cut off by the laptop, or a fault part way.
    let settled = false;
    const settle = async (sent: number): Promise<void> => {
      if (settled) return;
      settled = true;
      this.releaseSlot(club);
      stop.abort();

      if (sent === 0) {
        // Nothing reached the club, so nothing is charged.
        await this.refund(club, spoken.length, reserved.month);
      }
    };

    const log = this.log;
    const idleMs = this.streamIdleMs;

    async function* relay(): AsyncGenerator<Uint8Array> {
      let sent = 0;
      const pieces = upstream[Symbol.asyncIterator]();

      try {
        for (;;) {
          // Each piece is waited for only so long - see DEFAULT_STREAM_IDLE_MS.
          let timer: ReturnType<typeof setTimeout> | undefined;
          const idle = new Promise<"idle">((resolve) => {
            timer = setTimeout(() => resolve("idle"), idleMs);
          });
          let next: IteratorResult<Uint8Array> | "idle";

          try {
            next = await Promise.race([pieces.next(), idle]);
          } finally {
            clearTimeout(timer);
          }

          if (next === "idle") {
            log(`[voice] "${club}": a streamed line stalled - nothing from ElevenLabs for ${idleMs} ms - and was given up.`);
            // A fault, not an ending: half a line must not pass for a whole one.
            throw new Error(`ElevenLabs sent nothing for ${idleMs} ms`);
          }

          if (next.done === true) break;

          const piece = next.value;

          if (piece.length === 0) continue;

          if (sent + piece.length > MAX_AUDIO_BYTES) {
            log(`[voice] "${club}": a streamed line ran past ${MAX_AUDIO_BYTES} bytes and was cut off.`);
            break;
          }

          sent += piece.length;
          yield piece;
        }
      } finally {
        await settle(sent);
        // Not waited for: a stalled upstream may never answer this either.
        void Promise.resolve(pieces.return?.()).catch(() => {});
      }
    }

    return {
      ok: true,
      stream: relay(),
      usage: reserved.usage,
      characters: spoken.length,
      model: voice.modelId,
      // For a caller that never got to read it: the slot and the characters back.
      cancel: () => settle(0),
    };
  }

  // --- Hearing a voice before choosing it ---------------------------------
  //
  // Two things, and they are not the same thing. A SAMPLE is one fixed
  // neutral line, the same words in every voice, served to anyone at all
  // with no token and no club - it is what somebody who has never run
  // HeatCaller hears, and what a club compares voices by. A PREVIEW is a
  // club's own words in a voice it may use, for the Preview button on
  // Voice Settings, charged to a small daily allowance that has nothing to
  // do with the month's.
  //
  // Both exist because the catalogue could only ever give a club a name
  // and a line of description, so a club chose its announcer's voice by
  // reading about it.

  /**
   * The voices there is a sample of, in catalogue order.
   *
   * "Is a sample of" rather than "has a file for": a voice this relay can
   * render is listed before anybody has asked for it, because the first
   * ask is what renders it and a fresh relay with an empty list would
   * defeat the whole point. A relay whose key has since been taken away
   * lists only what it already has on the volume - that audio is paid for
   * and still worth hearing.
   */
  async samples(): Promise<readonly SampleListing[]> {
    const listed: SampleListing[] = [];

    for (const voice of this.config.voices) {
      // A CLONED VOICE IS NOT ADVERTISED. This list needs no token and is
      // open to the internet, which is right for a stock voice and is a
      // real person's name and voice for a clone. See
      // CatalogueVoice.cloned.
      if (voice.cloned) {
        continue;
      }

      if (await this.hasSample(voice)) {
        listed.push({ id: voice.id, name: voice.name });
      }
    }

    return listed;
  }

  private async hasSample(voice: CatalogueVoice): Promise<boolean> {
    if (this.sampleAudio.has(voice.id)) {
      return true;
    }

    if (this.render !== undefined) {
      return true;
    }

    const info = await stat(this.samplePath(voice)).catch(() => undefined);

    return info !== undefined && info.isFile() && info.size > 0;
  }

  /**
   * One voice's sample.
   *
   * WHAT THIS CAN COST. The first visitor to ask for a voice nobody has
   * asked for yet pays for one render of VOICE_SAMPLE_LINE on HeatCaller's
   * account; it is kept in memory and on the volume, and every visitor
   * after that is free. The whole feature's ceiling is therefore one short
   * line per voice in the catalogue, ever - a few hundred characters all
   * told - which is why these two routes can be open to the internet at
   * all. Nothing a caller sends shapes what is rendered: the words are a
   * constant in this file and the voice must be one the catalogue names.
   */
  async sample(voiceId: string): Promise<SampleResult> {
    const voice = this.byId.get(voiceId);

    // THE SAME ANSWER AS A VOICE THAT DOES NOT EXIST, and that is
    // deliberate rather than tidy. This route needs no token, so a
    // refusal worded differently from a miss would tell anybody who asked
    // which of a relay's voices are clones of somebody - which is a fact
    // about a person, published to whoever wants it. Unknown and
    // not-for-you look identical, the way an unknown club and a malformed
    // one already do on this relay.
    if (voice === undefined || voice.cloned) {
      return { ok: false, status: 404, message: "There is no sample of that voice." };
    }

    const held = this.sampleAudio.get(voice.id);

    if (held !== undefined) {
      return { ok: true, audio: held, cached: true };
    }

    const onVolume = await readFile(this.samplePath(voice)).catch(() => undefined);

    if (onVolume !== undefined && onVolume.length > 0) {
      this.sampleAudio.set(voice.id, onVolume);
      return { ok: true, audio: onVolume, cached: true };
    }

    if (this.render === undefined) {
      return { ok: false, status: 404, message: "There is no sample of that voice." };
    }

    const failedAt = this.sampleFailedAt.get(voice.id);

    if (failedAt !== undefined && this.now().getTime() - failedAt < SAMPLE_RETRY_AFTER_MS) {
      return { ok: false, status: 503, message: "That sample can't be made just now. Try again in a minute." };
    }

    // One render however many ask at once, and taken out of the map when
    // it settles, so the next ask after a failure starts a new one rather
    // than waiting on a promise that is already done.
    let making = this.samplesInFlight.get(voice.id);

    if (making === undefined) {
      making = this.renderSample(voice);
      this.samplesInFlight.set(voice.id, making);

      // Cleared from here, through then(), rather than in a finally inside
      // renderSample: a renderSample that ever returned without awaiting
      // anything would run that finally BEFORE the set above and leave a
      // finished promise in the map for ever, so every later visitor
      // would be answered 503 by a render that had long since given up.
      // then() cannot run before this turn of the loop is over.
      const forget = (): void => {
        this.samplesInFlight.delete(voice.id);
      };

      void making.then(forget, forget);
    }

    const audio = await making;

    if (audio === undefined) {
      return { ok: false, status: 503, message: "That sample can't be made just now. Try again in a minute." };
    }

    return { ok: true, audio, cached: false };
  }

  /**
   * Renders one sample and keeps it. Never throws and never rejects - a
   * sample that cannot be made is a 503 the next visitor may well not see,
   * not a fault, and sample() awaits this promise on behalf of however
   * many callers are waiting on the same voice.
   */
  private async renderSample(voice: CatalogueVoice): Promise<Buffer | undefined> {
    const render = this.render;

    // Checked by sample() before this is ever reached. Read into a local
    // so it cannot be read twice either side of an await.
    if (render === undefined) {
      return undefined;
    }

    const slot = await this.takeSlot(SAMPLE_SLOT_HOLDER);

    if (!slot) {
      // Not remembered as a failure: nothing was asked of ElevenLabs, the
      // relay was simply full, and the next visitor should try.
      return undefined;
    }

    let audio: Buffer;

    try {
      audio = await render(voice, VOICE_SAMPLE_LINE);
    } catch (error) {
      const status = error instanceof VoiceUpstreamError ? error.status : 0;

      this.log(`[voice] ElevenLabs did not speak the sample of "${voice.id}" (${status === 0 ? String(error) : status}).`);
      this.sampleFailedAt.set(voice.id, this.now().getTime());
      return undefined;
    } finally {
      this.releaseSlot(SAMPLE_SLOT_HOLDER);
    }

    if (audio.length === 0 || audio.length > MAX_AUDIO_BYTES) {
      this.log(`[voice] ElevenLabs answered the sample of "${voice.id}" with ${audio.length} bytes, which is not a line.`);
      this.sampleFailedAt.set(voice.id, this.now().getTime());
      return undefined;
    }

    this.sampleAudio.set(voice.id, audio);
    this.sampleFailedAt.delete(voice.id);

    try {
      await mkdir(join(this.root, SAMPLES_DIR), { recursive: true, mode: 0o700 });
      await writeFileAtomically(this.samplePath(voice), audio);
    } catch (error) {
      // Kept in memory even so, so this relay serves it free until it
      // restarts. The audio is already paid for; only a deploy would pay
      // again.
      this.log(`[voice] Could not keep the sample of "${voice.id}" (${String(error)}).`);
    }

    return audio;
  }

  /**
   * The sample file for one voice, named with the voice's revision as well
   * as its id.
   *
   * So that changing a voice's model or its settings renders the sample
   * again rather than serving one that no longer sounds like the voice a
   * club would get - the same reason the laptop keeps the revision with
   * every line it has saved. The old file is simply left; there are only
   * ever a handful, and nothing walks that folder.
   */
  private samplePath(voice: CatalogueVoice): string {
    return join(this.root, SAMPLES_DIR, `${voice.id}-${this.revs.get(voice.id) ?? revisionOf(voice)}.mp3`);
  }

  /**
   * A club's own words, in one voice, for the Preview button.
   *
   * Charged against the club's small DAILY preview allowance and never
   * against the month's characters. That separation is the whole point: a
   * club must not be able to arrive on a race night and find it spent its
   * voice on Tuesday deciding which one to use. Over the day's allowance
   * the answer is 429 - come back tomorrow - rather than the month's 402,
   * because nothing a club is billed for has been spent.
   *
   * The audio is kept with the club's own lines and never shared, however
   * innocent the words look: a preview is whatever somebody typed into the
   * box, which may well be a driver's name.
   */
  async preview(club: string, plan: VoicePlan, voiceId: string, text: string): Promise<SpeakResult> {
    if (!isValidClubId(club)) {
      return { ok: false, refusal: "unknown-voice", status: 404, message: "No such club." };
    }

    if (this.render === undefined || this.config.voices.length === 0) {
      return {
        ok: false,
        refusal: "off",
        status: 503,
        message: "The HeatCaller voice isn't switched on here, so there is nothing to preview.",
      };
    }

    if (!plan.enabled) {
      return {
        ok: false,
        refusal: "not-included",
        status: 403,
        message: "The HeatCaller voice isn't included for this club, so there is nothing to preview.",
      };
    }

    const voice = this.byId.get(voiceId);

    if (voice === undefined || !this.voicesFor(plan).includes(voice)) {
      return {
        ok: false,
        refusal: "unknown-voice",
        status: 400,
        message: "That voice isn't one this club can use. Pick one from the list on Voice Settings.",
      };
    }

    const spoken = readsAudioTags(voice.modelId) ? text.trim() : stripAudioTags(text);

    if (spoken.length === 0) {
      return { ok: false, refusal: "text", status: 400, message: "There was nothing to preview." };
    }

    // The shorter of the two caps, so a relay configured with a maxChars
    // below the preview cap does not speak past what it says it speaks.
    const cap = Math.min(this.previewMaxChars, this.config.maxChars);

    if (spoken.length > cap) {
      return {
        ok: false,
        refusal: "text",
        status: 400,
        message: `A preview is at most ${cap} characters - a sentence, to hear the voice. Shorten it and try again.`,
      };
    }

    // A key of its own, never one speak reads: a preview is charged to the
    // day's previews, not the month, so a line first heard here and kept
    // under speak's key was spoken free on race night from then on, never
    // counted against the club's allowance (9 Nov 2026, Bug hunt 15, B39).
    // A line speak has already paid for is still free to hear here.
    const key = previewCacheKey(voice, spoken);
    const cached = (await this.readCache(club, false, cacheKey(voice, spoken))) ?? (await this.readCache(club, false, key));

    if (cached !== undefined) {
      // Free, like any other cache hit: a club listening to the same
      // sentence twice while it makes up its mind pays once.
      return { ok: true, audio: cached, cached: true, usage: await this.previewUsage(club) };
    }

    // Taken before the render and given back if it fails, exactly as
    // speak's are and for the same reason - see reserve.
    const reserved = await this.previewReserve(club, spoken.length);

    if (!reserved.ok) {
      return {
        ok: false,
        refusal: "allowance",
        status: 429,
        message:
          `That is this club's previews for today - ${reserved.usage.allowance} characters a day of trying voices out. ` +
          "Its race-night voice allowance has not been touched, and previews start again tomorrow.",
        usage: reserved.usage,
      };
    }

    // The club's own share of the relay's renders, not a share of its own:
    // a preview is an ElevenLabs render like any other, and somebody
    // pressing Preview over and over should be held exactly where a
    // laptop rendering lines is held.
    const slot = await this.takeSlot(club);

    if (!slot) {
      await this.previewRefund(club, spoken.length, reserved.day);

      return {
        ok: false,
        refusal: "busy",
        status: 503,
        message: "The HeatCaller voice is busy. Try the preview again in a moment.",
        usage: await this.previewUsage(club),
      };
    }

    let audio: Buffer;

    try {
      audio = await this.render(voice, spoken);
    } catch (error) {
      const status = error instanceof VoiceUpstreamError ? error.status : 0;

      this.log(`[voice] "${club}": ElevenLabs did not speak a preview (${status === 0 ? String(error) : status}).`);

      await this.previewRefund(club, spoken.length, reserved.day);

      return {
        ok: false,
        refusal: "upstream",
        status: 502,
        message: "The HeatCaller voice couldn't speak that preview. Try again in a moment.",
        usage: await this.previewUsage(club),
      };
    } finally {
      this.releaseSlot(club);
    }

    if (audio.length === 0 || audio.length > MAX_AUDIO_BYTES) {
      this.log(`[voice] "${club}": ElevenLabs answered a preview with ${audio.length} bytes, which is not a line.`);

      await this.previewRefund(club, spoken.length, reserved.day);

      return {
        ok: false,
        refusal: "upstream",
        status: 502,
        message: "The HeatCaller voice couldn't speak that preview. Try again in a moment.",
        usage: await this.previewUsage(club),
      };
    }

    await this.writeCache(club, false, key, audio);

    return { ok: true, audio, cached: false, usage: reserved.usage, characters: spoken.length, model: voice.modelId };
  }

  // --- The preview allowance ----------------------------------------------
  //
  // Per club, per day, UTC, in a file of its own under PREVIEW_DIR.
  //
  // Deliberately simpler than the month's count above, because it is worth
  // far less: a preview file that cannot be read is taken as a fresh day,
  // where the month's file being taken that way would write an empty
  // history over three years of what a club is billed by. The most a
  // mangled preview file can cost is one club one day of auditioning, and
  // it is mended by the next write.

  /** Where this club's day of previews is counted. */
  private previewPath(club: string): string {
    return voicePreviewPath(this.dataDir, club);
  }

  /** Where this club's previews stand in <day> - today, unless the caller has already decided which (previewReserve has). */
  private async previewUsage(club: string, day: string = dayOf(this.now())): Promise<VoiceUsage> {
    const allowance = this.previewDailyChars;
    const cached = this.previewCache.get(club);

    if (cached !== undefined && cached.day === day) {
      return { used: cached.used, allowance, resetsAt: dayResetOf(this.now()) };
    }

    const read = await this.readPreviewFile(club, day);

    // Read outside the lock when a cache hit asks, so a preview may have
    // been counted while the file was being read - see usageState, which
    // has the same shape for the same reason.
    const meanwhile = this.previewCache.get(club);

    if (meanwhile !== undefined && meanwhile.day === day) {
      return { used: meanwhile.used, allowance, resetsAt: dayResetOf(this.now()) };
    }

    this.previewCache.set(club, { day, used: read });

    return { used: read, allowance, resetsAt: dayResetOf(this.now()) };
  }

  /** What this club has spent on previews in <day>: nothing when the file is missing, from another day, or unreadable. */
  private async readPreviewFile(club: string, day: string): Promise<number> {
    let raw: unknown;

    try {
      raw = JSON.parse(await readFile(this.previewPath(club), "utf8")) as unknown;
    } catch {
      return 0;
    }

    if (typeof raw !== "object" || raw === null || Array.isArray(raw)) {
      return 0;
    }

    const record = raw as Record<string, unknown>;

    return record["day"] === day ? count(record["used"]) : 0;
  }

  /** Takes characters out of today's preview allowance, or refuses. Read and write under one lock, as reserve is. */
  private async previewReserve(club: string, characters: number): Promise<{ ok: boolean; usage: VoiceUsage; day: string }> {
    let outcome: { ok: boolean; usage: VoiceUsage; day: string } = {
      ok: false,
      usage: { used: 0, allowance: this.previewDailyChars, resetsAt: dayResetOf(this.now()) },
      day: dayOf(this.now()),
    };

    await this.withLock(this.previewLocks, club, async () => {
      // The day, read once, is the day these characters come out of - and
      // the one previewRefund must give them back to.
      const day = dayOf(this.now());
      const current = await this.previewUsage(club, day);

      if (current.used + characters > current.allowance) {
        outcome = { ok: false, usage: current, day };
        return;
      }

      const used = current.used + characters;
      await this.storePreviewUsage(club, used, day);

      outcome = { ok: true, usage: { used, allowance: current.allowance, resetsAt: current.resetsAt }, day };
    });

    return outcome;
  }

  /** Gives back characters a preview never spent, to the day they came out of and only that one - see refund. */
  private async previewRefund(club: string, characters: number, reservedOn: string): Promise<void> {
    await this.withLock(this.previewLocks, club, async () => {
      const day = dayOf(this.now());
      const current = this.previewCache.get(club);

      if (day !== reservedOn || current === undefined || current.day !== day) {
        return;
      }

      await this.storePreviewUsage(club, Math.max(0, current.used - characters), day);
    });
  }

  private async storePreviewUsage(club: string, used: number, day: string): Promise<void> {
    this.previewCache.set(club, { day, used });

    try {
      await mkdir(join(this.root, PREVIEW_DIR), { recursive: true, mode: 0o700 });
      await writeFileAtomically(this.previewPath(club), Buffer.from(JSON.stringify({ day, used }), "utf8"));
    } catch (error) {
      // The count in memory still stands, so the day's allowance holds
      // for as long as this relay is up.
      this.log(`[voice] "${club}": could not write today's preview count (${String(error)}).`);
    }
  }

  // --- The allowance ------------------------------------------------------
  //
  // Per club, per calendar month, UTC. Kept in one small file on the volume
  // so it survives a deploy, which is exactly when a club would otherwise
  // get a fresh allowance by accident.

  private usagePath(club: string): string {
    return voiceUsagePath(this.dataDir, club);
  }

  /**
   * Where a club stands in <month> - this month, unless the caller has
   * already decided which (reserve does, so the count it reads and the
   * count it writes are the same month's even when midnight on the 1st
   * falls between them).
   */
  private async usageFor(club: string, plan: VoicePlan, month: string = monthOf(this.now())): Promise<VoiceUsage> {
    const state = await this.usageState(club, plan, month);

    // WHY (20 Oct 2026, Bug Hunt 8, n101): this is the figure the app is
    // given - in the catalogue, and in the headers of a line the relay had
    // saved. While the usage file can't be read it used to be the stand-in
    // "0 used of the plan", and the app took it as the club's count: its
    // This Month meter dropped to nothing spent, and the half-hourly probe
    // read the catalogue as room, ending a spent month and putting saved
    // lines back on the HeatCaller voice while new ones went to Kokoro.
    //
    // Now it is known:false with no allowance (0 of 0), so an app that does
    // not yet look at known still does no harm: its probe needs an
    // allowance above zero to call it room, so the spent state holds, and
    // its meter reads 0 of 0 as "unknown" and sends nothing. Not "used up"
    // (used = allowance), which was tried first: the meter takes that as
    // the month running out - it emails the club "HeatCaller voice used
    // up", uses up that month's one used-up notice, and Mission Control
    // says Kokoro is speaking - for a club that still has room. Voice
    // Settings still opens; it says "No allowance on this plan" until the
    // file reads again.
    return state.known ? state.usage : { used: 0, allowance: 0, resetsAt: state.usage.resetsAt, known: false };
  }

  /**
   * usageFor, and whether the count it gives is really the club's - read
   * from its usage file, or there being no file yet - rather than a
   * stand-in for a file that is there but could not be read.
   *
   * WHY THE DIFFERENCE MATTERS. A file that is there and cannot be read
   * (not JSON after a power cut, EIO, EMFILE under load, a file whose
   * owner was changed by hand) still holds this month's count and up to
   * three years of the history a club is billed by. Taking it as "nothing
   * spent" used to cache a count of 0 with no history, and the next line
   * reserve took wrote {used: <that line>, history: {}} over the real file:
   * the history gone for good, and the club handed a fresh allowance on
   * HeatCaller's ElevenLabs account. So such a read is never cached - the
   * next ask reads the file again - and reserve refuses to spend against
   * it (see there). Only ENOENT means "nothing counted yet".
   */
  private async usageState(
    club: string,
    plan: VoicePlan,
    month: string,
  ): Promise<{ usage: VoiceUsage; known: boolean }> {
    const allowance = plan.monthlyChars + (await this.topUpFor(club, month));
    const cached = this.usageCache.get(club);

    if (cached !== undefined && cached.month === month) {
      return { usage: { used: cached.used, allowance, resetsAt: resetOf(this.now()) }, known: true };
    }

    const read = await this.readUsageFile(club, month);

    // Read outside the lock when the catalogue asks, so a line may have
    // been counted while the file was being read. What it left in memory
    // is newer than the file this read saw, and stands.
    const meanwhile = this.usageCache.get(club);

    if (meanwhile !== undefined && meanwhile.month === month) {
      return { usage: { used: meanwhile.used, allowance, resetsAt: resetOf(this.now()) }, known: true };
    }

    if (read === undefined) {
      // Shown as nothing spent, so Voice Settings still opens - but not
      // kept, and not spent against. See above. Marked known:false so
      // nothing downstream takes the 0 for the club's count (20 Oct 2026,
      // Bug Hunt 8, n101).
      return { usage: { used: 0, allowance, resetsAt: resetOf(this.now()), known: false }, known: false };
    }

    // A count left in memory for an earlier month is newer than the file
    // too: a reserve at the very end of that month sets it before its write
    // lands, and a read across midnight can see the file from before that
    // write. Kept as that month's line of history, or the next line stored
    // wrote the older figure over it (9 Nov 2026, Bug hunt 15, B30).
    const history =
      meanwhile !== undefined && meanwhile.month < month && meanwhile.used > 0
        ? trimHistory({ ...read.history, [meanwhile.month]: meanwhile.used })
        : read.history;

    this.usageCache.set(club, { month, used: read.used, history });

    return { usage: { used: read.used, allowance, resetsAt: resetOf(this.now()) }, known: true };
  }

  /**
   * The club's usage file read for <month>: zero and no history when there
   * is no file yet, and undefined - logged - when there is one that cannot
   * be read or is not a usage record at all. See usageState for why the
   * two must never be confused.
   */
  private async readUsageFile(
    club: string,
    month: string,
  ): Promise<{ used: number; history: Readonly<Record<string, number>> } | undefined> {
    let text: string;

    try {
      text = await readFile(this.usagePath(club), "utf8");
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code === "ENOENT") {
        return { used: 0, history: {} };
      }

      this.log(`[voice] "${club}": could not read the usage file (${String(error)}); not spending against it until it reads.`);
      return undefined;
    }

    let raw: unknown;

    try {
      raw = JSON.parse(text) as unknown;
    } catch (error) {
      this.log(`[voice] "${club}": the usage file is not readable JSON (${String(error)}); not spending against it until it is mended.`);
      return undefined;
    }

    // readUsageRecord reads anything that is not an object as zero, which
    // is right for its callers that only report - but a file of "null" or
    // [] is not a record this relay wrote, and writing over it would lose
    // whatever it was meant to hold just the same.
    if (typeof raw !== "object" || raw === null || Array.isArray(raw)) {
      this.log(`[voice] "${club}": the usage file does not hold a usage record; not spending against it until it is mended.`);
      return undefined;
    }

    return readUsageRecord(raw, month);
  }

  /**
   * Characters added to this club's allowance for <month> by whoever runs
   * the relay (`voice-topup`, see voiceadmin.ts) - nothing when there is
   * no top-up.
   *
   * Read from its own file, which only that command writes and this only
   * reads, so a top-up given while the relay is up takes effect on the
   * club's next line without a restart, and can never be lost to a write
   * of the count racing it. Kept in memory against the file's
   * modification time, so asking costs one stat.
   */
  private async topUpFor(club: string, month: string): Promise<number> {
    // The operator's top-ups and the relay's automatic ones (autotopup.ts),
    // each from the file only its own writer writes.
    this.autoTopUps ??= new AutoTopUps(this.dataDir);
    return (await this.operatorTopUpFor(club, month)) + (await this.autoTopUps.total(club, "voice", month));
  }

  private autoTopUps: AutoTopUps | undefined;

  private async operatorTopUpFor(club: string, month: string): Promise<number> {
    const path = voiceTopUpPath(this.dataDir, club);
    const info = await stat(path).catch(() => undefined);

    if (info === undefined || !info.isFile()) {
      this.topUpCache.delete(club);
      return 0;
    }

    let known = this.topUpCache.get(club);

    if (known === undefined || known.mtimeMs !== info.mtimeMs) {
      try {
        known = { mtimeMs: info.mtimeMs, months: readTopUps(JSON.parse(await readFile(path, "utf8")) as unknown) };
      } catch {
        // Unreadable - no top-up, rather than no voice. Not kept: a file
        // caught between being written and handed to this relay's user
        // (voice-topup) reads again on the next line.
        this.topUpCache.delete(club);
        return 0;
      }

      this.topUpCache.set(club, known);
    }

    return known.months[month] ?? 0;
  }

  /**
   * Takes characters out of this month's allowance, or refuses.
   *
   * Read and write under one lock, so two lines finishing together cannot
   * both read the same count and both spend it.
   */
  private async reserve(
    club: string,
    plan: VoicePlan,
    characters: number,
  ): Promise<{ ok: boolean; unreadable?: boolean; usage: VoiceUsage; month: string }> {
    let outcome: { ok: boolean; unreadable?: boolean; usage: VoiceUsage; month: string } = {
      ok: false,
      usage: { used: 0, allowance: plan.monthlyChars, resetsAt: resetOf(this.now()) },
      month: monthOf(this.now()),
    };

    await this.withUsageLock(club, async () => {
      // The month, read once, is the month these characters are taken
      // from - and the one refund must give them back to. See refund.
      const month = monthOf(this.now());
      const state = await this.usageState(club, plan, month);
      const current = state.usage;

      if (!state.known) {
        // The usage file is there but could not be read. Spending now
        // would mean writing a count - and an empty history - over the
        // one on disk, so the line is refused as busy instead: the laptop
        // says it in the free Kokoro voice and asks again with the next
        // line, by which time a passing fault (EMFILE, EIO) has usually
        // cleared. Lines already in the cache are still served.
        outcome = { ok: false, unreadable: true, usage: current, month };
        return;
      }

      if (current.used + characters > current.allowance) {
        outcome = { ok: false, usage: current, month };
        return;
      }

      const used = current.used + characters;
      await this.storeUsage(club, used, month);

      outcome = { ok: true, usage: { used, allowance: current.allowance, resetsAt: current.resetsAt }, month };
    });

    return outcome;
  }

  /**
   * Gives back characters a render never spent - it failed, or no slot
   * came free - to the month they were taken from, and only to that one.
   *
   * A render takes seconds and a busy one can wait longer, so a line
   * reserved at a minute to midnight on the last of the month can fail in
   * the first seconds of the next. Its characters came out of the old
   * month, which has gone; handing them to the new one would give the
   * club characters it never had - or, against a count already down to
   * zero, take nothing but still write a count over one another line had
   * just raised. So a refund for another month does nothing at all.
   */
  private async refund(club: string, characters: number, reservedIn: string): Promise<void> {
    await this.withUsageLock(club, async () => {
      const month = monthOf(this.now());
      const current = this.usageCache.get(club);

      if (month !== reservedIn || current === undefined || current.month !== month) {
        return;
      }

      await this.storeUsage(club, Math.max(0, current.used - characters), month);
    });
  }

  /**
   * Writes the month's count, in memory and on the volume - with the
   * months before it, which are what a club is billed or its allowance
   * set by. Under withUsageLock, always, and after usageState has read the
   * file for this month and found it readable (reserve and refund both
   * see to that), so the history in memory is the file's.
   */
  private async storeUsage(club: string, used: number, month: string): Promise<void> {
    const cached = this.usageCache.get(club);

    // Always this month's entry: both callers read it under the same lock
    // for the same month first. The fallback only keeps a history from
    // being written away if that ever stops being so.
    const history =
      cached !== undefined && cached.month === month ? cached.history : (await this.readUsageFile(club, month))?.history;

    if (history === undefined) {
      // The file is there and could not be read: writing would put an
      // empty history over the real one. Not reached today (see above);
      // nothing is kept, so the next read of the file decides.
      return;
    }

    this.usageCache.set(club, { month, used, history });

    try {
      await mkdir(join(this.root, "usage"), { recursive: true, mode: 0o700 });
      await writeFileAtomically(this.usagePath(club), Buffer.from(JSON.stringify({ month, used, history }), "utf8"));
    } catch (error) {
      // The count in memory still stands, so the allowance holds for as
      // long as this relay is up.
      this.log(`[voice] "${club}": could not write the month's usage (${String(error)}).`);
    }
  }

  private async withUsageLock(club: string, work: () => Promise<void>): Promise<void> {
    await this.withLock(this.usageLocks, club, work);
  }

  /**
   * One piece of work at a time per key, in whichever map of locks the
   * caller names - the month's counts, or the day's previews, which are
   * kept apart so an audition never waits behind a flag call.
   */
  private async withLock(locks: Map<string, Promise<void>>, key: string, work: () => Promise<void>): Promise<void> {
    const before = locks.get(key) ?? Promise.resolve();
    let release: () => void = () => {};
    const mine = new Promise<void>((resolve) => {
      release = resolve;
    });

    // The chained promise is what the map holds, so it is what the check
    // below must compare with: comparing with `mine` never matched, and a
    // club's entry was never let go.
    const tail = before.then(() => mine);

    locks.set(key, tail);

    await before;

    try {
      await work();
    } finally {
      release();

      if (locks.get(key) === tail) {
        locks.delete(key);
      }
    }
  }

  // --- The cache ----------------------------------------------------------
  //
  // Whether a line is shared is a flag of its own, all the way down, and
  // never read back out of a folder name. It used to be a single "scope"
  // string that was either a club id or the word "shared" - and "shared"
  // is a club id like any other, so a club called that had every private
  // line it spoke, driver names and all, written into the one cache every
  // other club is served from.

  private cacheDir(club: string, shared: boolean): string {
    return shared ? join(this.root, "shared") : join(this.root, "clubs", club);
  }

  private cachePath(club: string, shared: boolean, key: string): string {
    return join(this.cacheDir(club, shared), `${key}.mp3`);
  }

  private async readCache(club: string, shared: boolean, key: string): Promise<Buffer | undefined> {
    const path = this.cachePath(club, shared, key);

    try {
      const bytes = await readFile(path);

      if (bytes.length === 0) {
        return undefined;
      }

      // Touched, so the sweep below keeps what is actually being played
      // and drops what a club stopped using three seasons ago.
      const when = this.now();
      void utimes(path, when, when).catch(() => {});

      return bytes;
    } catch {
      return undefined;
    }
  }

  private async writeCache(club: string, shared: boolean, key: string, audio: Buffer): Promise<void> {
    try {
      await mkdir(this.cacheDir(club, shared), { recursive: true, mode: 0o700 });
      await writeFileAtomically(this.cachePath(club, shared, key), audio);
    } catch (error) {
      // A line that could not be kept is a line paid for twice, not a
      // failed race night. Never surfaced to the club.
      this.log(`[voice] Could not keep a rendered line (${String(error)}).`);
      return;
    }

    void this.prune(club, shared).catch(() => {});
  }

  /**
   * The club's own lines - driver names, spoken - gone. Called when a club
   * deletes everything it has published, which is a club asking to be
   * forgotten; its lines are in a folder of their own here, not in the
   * club's, so the store's delete never reached them.
   *
   * The month's count, the months before it and the operator's top-up all
   * stay. They are numbers with no names in them, and they are not the
   * club's to wipe: the tenant lives in the relay's config, not in what
   * the club published, so the same token goes on working the moment the
   * delete is done. Wiping the count would hand the club a fresh full
   * allowance every time it deleted - a cap any club could lift with one
   * call, paid for on HeatCaller's ElevenLabs account - and the history
   * is what the club is billed by and what voice-usage reports, so a
   * delete would also erase the record of what was spent. The cached
   * count in memory stays for the same reason; it is the file's, and a
   * club that comes back under the same id is the same club on the same
   * plan.
   *
   * The shared lines stay too. They have no names in them and are
   * nobody's. A line still being rendered when this runs can land after
   * it; it is one line, and goes the next time the club is deleted or its
   * folder is swept. So do the samples, which are nobody's either.
   *
   * The day's preview count stays for the same reason the month's does: a
   * club that could wipe it could have a fresh day of auditioning for
   * every delete.
   */
  async forget(club: string): Promise<void> {
    if (!isValidClubId(club)) {
      return;
    }

    // Under the usage lock still, so the folder goes between one line's
    // count and the next rather than in the middle of a reserve.
    await this.withUsageLock(club, async () => {
      await rm(this.cacheDir(club, false), { recursive: true, force: true });
    });
  }

  /**
   * Keeps a cache folder under its size, oldest-used first.
   *
   * Size, not age: a line that is still played every Saturday is worth
   * keeping for years, and one nobody has asked for since a style was
   * changed is worth nothing however new it is.
   */
  private async prune(club: string, shared: boolean): Promise<void> {
    const dir = this.cacheDir(club, shared);

    if (this.pruning.has(dir)) {
      return;
    }

    this.pruning.add(dir);

    try {
      const limit = shared ? this.sharedCacheBytes : this.clubCacheBytes;
      const names = await readdir(dir).catch(() => [] as string[]);
      const files: { path: string; bytes: number; used: number }[] = [];
      let total = 0;

      for (const name of names) {
        if (!name.endsWith(".mp3")) {
          continue;
        }

        const path = join(dir, name);
        const info = await stat(path).catch(() => undefined);

        if (info === undefined || !info.isFile()) {
          continue;
        }

        files.push({ path, bytes: info.size, used: info.mtimeMs });
        total += info.size;
      }

      if (total <= limit) {
        return;
      }

      files.sort((a, b) => a.used - b.used);

      let removed = 0;

      for (const file of files) {
        if (total <= limit * 0.8) {
          break;
        }

        await unlink(file.path).catch(() => {});
        total -= file.bytes;
        removed += 1;
      }

      if (removed > 0) {
        this.log(`[voice] Cache for ${shared ? "shared lines" : `"${club}"`} was over its size - cleared ${removed} line(s).`);
      }
    } finally {
      this.pruning.delete(dir);
    }
  }

  // --- Concurrency --------------------------------------------------------
  //
  // maxInFlight renders at once and maxWaiting in line, across the relay;
  // of those, maxInFlightPerClub and maxWaitingPerClub for any one club.
  // A club at its own share waits (or is told "busy") even when the relay
  // has room, and the line moves on in order, skipping whoever is at
  // their share: a slot freed by one club goes to the first in line that
  // can take it.

  private slotsOf(club: string): { inFlight: number; waiting: number } {
    let slots = this.clubSlots.get(club);

    if (slots === undefined) {
      slots = { inFlight: 0, waiting: 0 };
      this.clubSlots.set(club, slots);
    }

    return slots;
  }

  private tidySlots(club: string): void {
    const slots = this.clubSlots.get(club);

    if (slots !== undefined && slots.inFlight === 0 && slots.waiting === 0) {
      this.clubSlots.delete(club);
    }
  }

  private takeSlot(club: string): Promise<boolean> {
    const mine = this.slotsOf(club);

    if (this.inFlight < this.maxInFlight && mine.inFlight < this.maxInFlightPerClub) {
      this.inFlight += 1;
      mine.inFlight += 1;
      return Promise.resolve(true);
    }

    if (this.waiting >= this.maxWaiting || mine.waiting >= this.maxWaitingPerClub) {
      this.tidySlots(club);
      return Promise.resolve(false);
    }

    this.waiting += 1;
    mine.waiting += 1;

    return new Promise<boolean>((resolve) => {
      this.queue.push({
        club,
        go: () => {
          this.waiting -= 1;
          mine.waiting -= 1;
          this.inFlight += 1;
          mine.inFlight += 1;
          resolve(true);
        },
      });
    });
  }

  private releaseSlot(club: string): void {
    this.inFlight -= 1;
    this.slotsOf(club).inFlight -= 1;
    this.tidySlots(club);

    // Whoever is first in line and not already at their own share, for as
    // long as there is room: the slot just freed may have been all that
    // was holding back a waiter from the same club, while one from another
    // club was only ever waiting on its own share.
    while (this.inFlight < this.maxInFlight) {
      const index = this.queue.findIndex((entry) => (this.clubSlots.get(entry.club)?.inFlight ?? 0) < this.maxInFlightPerClub);

      if (index < 0) {
        break;
      }

      const [next] = this.queue.splice(index, 1);
      next?.go();
    }
  }

  /** Renders under way and waiting, in all and for one club. For tests. */
  slots(club: string): { readonly inFlight: number; readonly waiting: number; readonly clubInFlight: number; readonly clubWaiting: number } {
    const mine = this.clubSlots.get(club);

    return { inFlight: this.inFlight, waiting: this.waiting, clubInFlight: mine?.inFlight ?? 0, clubWaiting: mine?.waiting ?? 0 };
  }
}

/**
 * Moves an older relay's voice folder, dataDir/voice, to where the voice
 * keeps things now (VOICE_DIR), once - so a deploy of this version does
 * not hand every club a fresh month's allowance and throw away every
 * line already paid for.
 *
 * Only when no club is called "voice". If one is, dataDir/voice is that
 * club's folder as well - its published documents and the voice's files
 * mixed together - and there is no telling from here which is which. So
 * it is left exactly where it is, and said so: the voice starts afresh in
 * its new folder, and whoever runs the relay can sort the old one out by
 * hand. Nor when the new folder is already there, which is a relay that
 * has migrated before (or been run once already with this version), and
 * whatever is in it is newer than the old one.
 *
 * Never throws: a voice folder that cannot be moved is logged, and the
 * relay starts anyway.
 */
export async function migrateVoiceFolder(
  dataDir: string,
  clubs: readonly string[],
  log: (line: string) => void = () => {},
): Promise<"moved" | "none" | "kept"> {
  const from = join(dataDir, OLD_VOICE_DIR);
  const to = join(dataDir, VOICE_DIR);

  const exists = async (path: string): Promise<boolean> =>
    access(path).then(
      () => true,
      () => false,
    );

  if (!(await exists(from))) {
    return "none";
  }

  if (clubs.includes(OLD_VOICE_DIR)) {
    log(
      `[voice] ${from} is left where it is: a club is called "${OLD_VOICE_DIR}", so that folder is its own too. ` +
        `The HeatCaller voice now keeps its usage and lines in ${to}; move anything of the voice's across by hand.`,
    );
    return "kept";
  }

  if (await exists(to)) {
    log(`[voice] ${from} and ${to} are both here; ${to} is used and ${from} is left alone.`);
    return "kept";
  }

  try {
    await rename(from, to);
    log(`[voice] Moved the HeatCaller voice's folder from ${from} to ${to}.`);
    return "moved";
  } catch (error) {
    log(`[voice] Could not move ${from} to ${to} (${String(error)}); the voice starts afresh in ${to}.`);
    return "kept";
  }
}

/** A history item id as ElevenLabs gives one: anything else in the header is not ours to send back. */
const HISTORY_ITEM_ID = /^[A-Za-z0-9_-]{1,128}$/;

/**
 * Whether ElevenLabs is asked not to keep the lines it speaks (15 Nov 2026,
 * children's privacy, plan 2.3). The lines name drivers, juniors among them,
 * so every request carries ElevenLabs' own `enable_logging=false` - its
 * zero-retention mode - unless RELAY_VOICE_NO_LOGGING says not to.
 *
 * WHY A FALLBACK. ElevenLabs offers zero retention to enterprise plans only.
 * This was written expecting another plan to turn the request down, so a
 * refusal is asked again once without it; if that goes through, this key is
 * remembered as one that can't have it and the rest of this run asks
 * without, said once in the log. A request that fails both ways was failing
 * for another reason, and nothing is remembered.
 *
 * WHY (24 Nov 2026, Asher: delete each line from ElevenLabs' history): tried
 * on a real non-enterprise key, the flag is NOT turned down - it is accepted
 * with a 200 and quietly ignored, and the line is kept in the key's history
 * like any other. So the fallback above never runs, and asking alone kept
 * nothing out of the history: every line with a driver's name in it stayed
 * there. Every speech answer (whole, stream, with-timestamps) carries a
 * `history-item-id` header, flag or no flag, so now each line is also
 * deleted by that exact id once it is in hand (forget) - only ever the id
 * from our own answer, never a list of the history matched against
 * anything. The fallback is kept for a plan that does refuse the flag.
 *
 * One of these per key: index.ts shares it between the renderer and the
 * streamer, so a refusal found by one is known to the other.
 */
export class ElevenLabsLogging {
  private refused = false;
  /** The key turned a delete down (401/403): it hasn't the Speech history permission. */
  private historyRefused = false;
  /** A delete failed for another reason; said once, and the next ones are still tried. */
  private deleteFailedSaid = false;
  private readonly pending = new Set<Promise<void>>();

  constructor(
    /** Off only when RELAY_VOICE_NO_LOGGING is "0", "false", "off" or "no". */
    readonly askNotToKeep = true,
    private readonly log: (line: string) => void = () => {},
    /** How long one delete may take. It never holds a line up either way. */
    private readonly deleteTimeoutMs = 10_000,
  ) {}

  /** Whether a spoken line is taken out of the key's history: while asked not to keep, and until the key is found unable to. */
  get deleting(): boolean {
    return this.askNotToKeep && !this.historyRefused;
  }

  /**
   * Deletes one line from the key's ElevenLabs history, by the
   * `history-item-id` its own answer carried. Sent and not waited for:
   * nothing here can delay a line or fail it. A 404 is a line already gone.
   */
  forget(apiKey: string, historyItemId: string | null | undefined): void {
    if (!this.deleting) {
      return;
    }

    const id = (historyItemId ?? "").trim();

    if (!HISTORY_ITEM_ID.test(id)) {
      return;
    }

    const sent: Promise<void> = this.sendDelete(apiKey, id)
      .catch(() => {})
      .then(() => {
        this.pending.delete(sent);
      });
    this.pending.add(sent);
  }

  /** Waits for every delete sent so far - for a command that exits when it is done, and for tests. */
  async settled(): Promise<void> {
    while (this.pending.size > 0) {
      await Promise.allSettled([...this.pending]);
    }
  }

  private async sendDelete(apiKey: string, id: string): Promise<void> {
    let response: Response;

    try {
      response = await fetch(`https://api.elevenlabs.io/v1/history/${encodeURIComponent(id)}`, {
        method: "DELETE",
        headers: { "xi-api-key": apiKey },
        signal: AbortSignal.timeout(this.deleteTimeoutMs),
      });
    } catch (error) {
      this.noteDeleteFailed(String(error));
      return;
    }

    // Let go unread: a refusal's body can carry the account's details.
    await response.body?.cancel().catch(() => {});

    if (response.ok || response.status === 404) {
      return;
    }

    if (response.status === 401 || response.status === 403) {
      // The same key has just spoken the line, so a refusal here is the
      // permission, not the key.
      this.noteHistoryRefused(response.status);
      return;
    }

    this.noteDeleteFailed(String(response.status));
  }

  private noteHistoryRefused(status: number): void {
    if (this.historyRefused) {
      return;
    }

    this.historyRefused = true;
    this.log(
      `[voice] This ElevenLabs key can't remove lines from its history (${status}); give it the Speech history ` +
        `permission. Lines stay in the key's ElevenLabs history until this is restarted with a key that can.`,
    );
  }

  private noteDeleteFailed(why: string): void {
    if (this.deleteFailedSaid) {
      return;
    }

    this.deleteFailedSaid = true;
    this.log(`[voice] A line couldn't be removed from the ElevenLabs history (${why.slice(0, 120)}); later lines are still tried.`);
  }

  /** Whether the next request carries enable_logging=false. */
  get asking(): boolean {
    return this.askNotToKeep && !this.refused;
  }

  /** The key's plan turned the flag down and a request without it went through. */
  noteRefused(status: number): void {
    if (this.refused) {
      return;
    }

    this.refused = true;
    this.log(
      `[voice] ElevenLabs turned down "don't keep our lines" (enable_logging=false, ${status}) - this key's plan ` +
        `doesn't offer it. Lines are sent without it until the relay restarts.`,
    );
  }
}

/** RELAY_VOICE_NO_LOGGING, read: on unless it plainly says off. */
export function noLoggingFromEnv(value: string | undefined): boolean {
  const said = (value ?? "").trim().toLowerCase();
  return !["0", "false", "off", "no"].includes(said);
}

/** The URL with enable_logging=false on it, when asked for. */
export function withNoLogging(url: string, ask: boolean): string {
  return ask ? `${url}${url.includes("?") ? "&" : "?"}enable_logging=false` : url;
}

/**
 * One POST to ElevenLabs, with the no-logging flag while it is wanted - and,
 * when a request with it is turned down as a bad request (400, 403, 422), the
 * same request once more without it. See ElevenLabsLogging: on the plans
 * tried so far the flag is accepted and ignored, so the retry is for a plan
 * that does refuse it.
 */
async function postToElevenLabs(url: string, init: RequestInit, logging: ElevenLabsLogging): Promise<Response> {
  const asked = logging.asking;
  const first = await fetch(withNoLogging(url, asked), init);

  if (first.ok || !asked || ![400, 403, 422].includes(first.status)) {
    return first;
  }

  // The refusal's body is let go unread: it can carry the account's details.
  await first.body?.cancel().catch(() => {});

  const second = await fetch(url, init);

  if (second.ok) {
    logging.noteRefused(first.status);
  }

  return second;
}

/**
 * What ElevenLabs is actually asked, when a key is configured.
 *
 * The same request the laptop used to make with the club's own key - same
 * fields, same shape - so a line rendered here and one a club rendered for
 * itself sound identical.
 */
/**
 * The same request as elevenLabsRenderer, to ElevenLabs' stream endpoint,
 * answered as soon as the headers are in (11 Nov 2026): raw PCM in
 * STREAM_FORMAT, read as it arrives. The time limit is on the answer
 * starting; once audio flows, the caller's signal is what stops it.
 */
export function elevenLabsStreamer(apiKey: string, timeoutMs = 30_000, logging = new ElevenLabsLogging()): StreamFn {
  return async (voice, text, signal) => {
    const url =
      `https://api.elevenlabs.io/v1/text-to-speech/${encodeURIComponent(voice.voiceId)}/stream` +
      `?output_format=${STREAM_FORMAT}`;

    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    const stop = (): void => controller.abort();
    signal.addEventListener("abort", stop, { once: true });

    try {
      const response = await postToElevenLabs(url, {
        method: "POST",
        headers: {
          "xi-api-key": apiKey,
          "content-type": "application/json",
          accept: "audio/pcm",
        },
        body: JSON.stringify({
          text,
          model_id: voice.modelId,
          apply_text_normalization: "auto",
          voice_settings: {
            stability: voice.stability,
            similarity_boost: voice.similarity,
            style: voice.style,
            use_speaker_boost: voice.speakerBoost,
            speed: voice.speed,
          },
        }),
        signal: controller.signal,
      }, logging);

      if (!response.ok || response.body === null) {
        const body = await response.text().catch(() => "");
        throw new VoiceUpstreamError(response.status, `ElevenLabs answered ${response.status}: ${body.slice(0, 200)}`);
      }

      // WHY (24 Nov 2026, Asher: delete each line from ElevenLabs' history):
      // the line is taken out of the history once the stream ends or is
      // stopped. Both ways, once: a reader that never starts (VoiceService's
      // cancel) never runs the wrapper's finally, but it does abort signal.
      const historyItemId = response.headers.get("history-item-id");
      let forgotten = false;
      const forget = (): void => {
        if (forgotten) return;
        forgotten = true;
        signal.removeEventListener("abort", forget);
        logging.forget(apiKey, historyItemId);
      };
      signal.addEventListener("abort", forget, { once: true });

      const body = response.body as unknown as AsyncIterable<Uint8Array>;

      return (async function* (): AsyncGenerator<Uint8Array> {
        try {
          yield* body;
        } finally {
          forget();
        }
      })();
    } finally {
      clearTimeout(timer);
    }
  };
}

export function elevenLabsRenderer(apiKey: string, timeoutMs = 30_000, logging = new ElevenLabsLogging()): RenderFn {
  return async (voice, text) => {
    const format = voice.outputFormat.trim();
    const url =
      `https://api.elevenlabs.io/v1/text-to-speech/${encodeURIComponent(voice.voiceId)}` +
      (format.length > 0 ? `?output_format=${encodeURIComponent(format)}` : "");

    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);

    try {
      const response = await postToElevenLabs(url, {
        method: "POST",
        headers: {
          "xi-api-key": apiKey,
          "content-type": "application/json",
          accept: "audio/mpeg",
        },
        body: JSON.stringify({
          text,
          model_id: voice.modelId,
          apply_text_normalization: "auto",
          voice_settings: {
            stability: voice.stability,
            similarity_boost: voice.similarity,
            style: voice.style,
            use_speaker_boost: voice.speakerBoost,
            speed: voice.speed,
          },
        }),
        signal: controller.signal,
      }, logging);

      if (!response.ok) {
        // The body is read but never passed on: it can carry the account's
        // own details, and a club has no use for them.
        const body = await response.text().catch(() => "");
        throw new VoiceUpstreamError(response.status, `ElevenLabs answered ${response.status}: ${body.slice(0, 200)}`);
      }

      let bytes: Buffer;

      try {
        bytes = Buffer.from(await response.arrayBuffer());
      } finally {
        // WHY (24 Nov 2026, Asher: delete each line from ElevenLabs' history):
        // asking not to keep it was accepted and ignored, so the line is
        // deleted by the id its answer carries - once the bytes are in hand,
        // or failed, and before any check below can throw it away.
        logging.forget(apiKey, response.headers.get("history-item-id"));
      }

      if (bytes.length > MAX_AUDIO_BYTES) {
        throw new VoiceUpstreamError(response.status, `ElevenLabs answered with ${bytes.length} bytes.`);
      }

      return bytes;
    } finally {
      clearTimeout(timer);
    }
  };
}

/** Reads the voice block of the config. Absent means no catalogue, which is a relay with the voice switched off. */
export function readVoiceConfig(value: unknown, source: string): VoiceConfig {
  if (value === undefined || value === null) {
    return {
      voices: [],
      monthlyChars: VOICE_DEFAULT_MONTHLY_CHARS,
      maxChars: VOICE_DEFAULT_MAX_CHARS,
      previewDailyChars: VOICE_DEFAULT_PREVIEW_DAILY_CHARS,
      previewMaxChars: VOICE_DEFAULT_PREVIEW_MAX_CHARS,
    };
  }

  if (typeof value !== "object" || Array.isArray(value)) {
    throw new Error(`"voice" in ${source} must be an object.`);
  }

  const raw = value as Record<string, unknown>;
  const list = raw["voices"];
  const voices: CatalogueVoice[] = [];

  if (list !== undefined && !Array.isArray(list)) {
    throw new Error(`"voice.voices" in ${source} must be a list.`);
  }

  for (const [index, entry] of ((list as unknown[]) ?? []).entries()) {
    if (typeof entry !== "object" || entry === null) {
      throw new Error(`Voice ${index + 1} in ${source} must be an object.`);
    }

    const voice = entry as Record<string, unknown>;
    const id = text(voice["id"]);
    const voiceId = text(voice["voiceId"]);

    if (!/^[a-z0-9][a-z0-9-]{0,23}$/.test(id)) {
      throw new Error(
        `Voice ${index + 1} in ${source} needs an "id" of lower-case letters, digits and hyphens, up to 24 characters.`,
      );
    }

    if (voiceId.length === 0) {
      throw new Error(`Voice "${id}" in ${source} needs the ElevenLabs "voiceId".`);
    }

    if (voices.some((known) => known.id === id)) {
      throw new Error(`Voice "${id}" is listed twice in ${source}.`);
    }

    voices.push({
      id,
      name: text(voice["name"]).length > 0 ? text(voice["name"]) : id,
      what: text(voice["what"]),
      voiceId,
      modelId: text(voice["modelId"]).length > 0 ? text(voice["modelId"]) : "eleven_v4_turbo",
      stability: number(voice["stability"], 0.5, 0, 1),
      similarity: number(voice["similarity"], 0.75, 0, 1),
      style: number(voice["style"], 0, 0, 1),
      speakerBoost: voice["speakerBoost"] !== false,
      speed: number(voice["speed"], 1, 0.7, 1.2),
      outputFormat: text(voice["outputFormat"]),

      // Opt IN, like every other thing on this relay that changes who can
      // hear what. A catalogue that does not mention it gets the stock
      // behaviour it was written for.
      cloned: voice["cloned"] === true,
    });
  }

  return {
    voices,
    monthlyChars: whole(raw["monthlyChars"], VOICE_DEFAULT_MONTHLY_CHARS, 0, 100_000_000, "voice.monthlyChars", source),
    maxChars: whole(raw["maxChars"], VOICE_DEFAULT_MAX_CHARS, 40, 5_000, "voice.maxChars", source),
    // Zero is allowed and means something: previews off, samples still
    // served. It is the DEFAULT that is kept far below the month's
    // allowance (VOICE_DEFAULT_PREVIEW_DAILY_CHARS), because this one is
    // not metered per club or billed - it is there so somebody can make
    // their mind up, and a day of it should never be worth stealing. The
    // million here is only a sanity ceiling against a mistyped config, not
    // a size anyone should set (22 Oct 2026: this comment used to say the
    // bound itself was below the month's, which it never was).
    previewDailyChars: whole(
      raw["previewDailyChars"],
      VOICE_DEFAULT_PREVIEW_DAILY_CHARS,
      0,
      1_000_000,
      "voice.previewDailyChars",
      source,
    ),
    previewMaxChars: whole(raw["previewMaxChars"], VOICE_DEFAULT_PREVIEW_MAX_CHARS, 40, 600, "voice.previewMaxChars", source),
  };
}

/** A club's own voice line in the config, falling back to the relay's defaults. */
export function readVoicePlan(value: unknown, defaults: VoiceConfig, club: string, source: string): VoicePlan {
  if (value === undefined || value === null) {
    return { enabled: false, monthlyChars: defaults.monthlyChars, voices: [] };
  }

  if (typeof value !== "object" || Array.isArray(value)) {
    throw new Error(`"voice" for club "${club}" in ${source} must be an object.`);
  }

  const raw = value as Record<string, unknown>;
  const list = raw["voices"];

  if (list !== undefined && !Array.isArray(list)) {
    throw new Error(`"voice.voices" for club "${club}" in ${source} must be a list of voice ids.`);
  }

  const voices = ((list as unknown[]) ?? []).map((entry) => text(entry)).filter((entry) => entry.length > 0);

  for (const id of voices) {
    if (!defaults.voices.some((voice) => voice.id === id)) {
      throw new Error(`Club "${club}" in ${source} names a voice "${id}" that the catalog does not have.`);
    }
  }

  return {
    enabled: raw["enabled"] === true,
    monthlyChars: whole(
      raw["monthlyChars"],
      defaults.monthlyChars,
      0,
      100_000_000,
      `voice.monthlyChars for club "${club}"`,
      source,
    ),
    voices,
  };
}

/** The plan a club has when the config says nothing: the voice is not included. */
export const VOICE_PLAN_OFF: VoicePlan = Object.freeze({ enabled: false, monthlyChars: 0, voices: [] });

/**
 * A fingerprint of everything that shapes a voice's audio.
 *
 * The laptop keeps this with every saved line. Change a voice's model or
 * its settings here and the fingerprint changes, so laptops render the
 * lines again instead of playing takes that no longer match the voice the
 * club hears live.
 */
function revisionOf(voice: CatalogueVoice): string {
  return createHash("sha256")
    .update(
      [
        voice.voiceId,
        voice.modelId,
        voice.stability,
        voice.similarity,
        voice.style,
        voice.speakerBoost ? "1" : "0",
        voice.speed,
        voice.outputFormat,
      ].join("|"),
      "utf8",
    )
    .digest("hex")
    .slice(0, 12);
}

/** Where a rendered line is kept. The words and everything that shapes them, and nothing else. */
function cacheKey(voice: CatalogueVoice, text: string): string {
  return createHash("sha256").update(`${revisionOf(voice)}|${voice.id}|${text}`, "utf8").digest("hex");
}

/**
 * Where a preview is kept: a hash of speak's key, so no text speak is ever
 * asked to say can land on it. See preview.
 */
function previewCacheKey(voice: CatalogueVoice, text: string): string {
  return createHash("sha256").update(`preview|${cacheKey(voice, text)}`, "utf8").digest("hex");
}

/**
 * Whether a model performs audio tags like [excited] rather than reading them
 * out: ElevenLabs' v3 and v4 families (eleven_v3, eleven_v4, eleven_v4_turbo).
 * The same rule as VoiceProfile.ReadsAudioTags on the laptop.
 */
export function readsAudioTags(modelId: string): boolean {
  return /v[34]/i.test(modelId);
}

/** "[excited] Green flag!" -> "Green flag!", for the models that would read the tag out loud. */
export function stripAudioTags(value: string): string {
  // Character for character what VoiceProfile.StripAudioTags does on the
  // laptop - a tag is replaced with nothing, then doubled spaces collapse.
  // The two have to agree: the Kokoro voice says the stripped line when it
  // stands in, and a club would otherwise hear two slightly different
  // sentences for the same announcement.
  return value
    .replace(/\[[a-zA-Z ]{1,24}\]/g, "")
    .replace(/ {2,}/g, " ")
    .trim();
}

/** Where a club's count is kept: this month's, and the months before it. */
export function voiceUsagePath(dataDir: string, club: string): string {
  return join(dataDir, VOICE_DIR, "usage", `${club}.json`);
}

/** Where the characters given to a club on top of its plan are kept, by month - see voiceadmin.ts. */
export function voiceTopUpPath(dataDir: string, club: string): string {
  return join(dataDir, VOICE_DIR, "topups", `${club}.json`);
}

/** Where a club's day of previews is counted. Never the same file as the month's count - see PREVIEW_DIR. */
export function voicePreviewPath(dataDir: string, club: string): string {
  return join(dataDir, VOICE_DIR, PREVIEW_DIR, `${club}.json`);
}

/** How many months of a club's counts are kept: three years, which is as far back as anyone bills or ElevenLabs audits. */
export const VOICE_HISTORY_MONTHS = 36;

const MONTH_PATTERN = /^\d{4}-(0[1-9]|1[0-2])$/;

/**
 * A usage file read for <month>: this month's count, and every month
 * before it.
 *
 * The file holds the month it was last written in. When that is not this
 * month, its count is a finished month's, and moves into the history here
 * - the first line a club speaks in a new month is what turns the page,
 * and nothing is lost if it speaks none for a while. Files written before
 * there was a history ({ month, used }) read the same way.
 */
export function readUsageRecord(raw: unknown, month: string): { used: number; history: Readonly<Record<string, number>> } {
  if (typeof raw !== "object" || raw === null || Array.isArray(raw)) {
    return { used: 0, history: {} };
  }

  const record = raw as Record<string, unknown>;
  const history: Record<string, number> = {};
  const past = record["history"];

  if (typeof past === "object" && past !== null && !Array.isArray(past)) {
    for (const [key, value] of Object.entries(past as Record<string, unknown>)) {
      if (MONTH_PATTERN.test(key) && key < month && count(value) > 0) {
        history[key] = count(value);
      }
    }
  }

  const fileMonth = typeof record["month"] === "string" ? record["month"] : "";
  const fileUsed = count(record["used"]);

  if (fileMonth === month) {
    return { used: fileUsed, history: trimHistory(history) };
  }

  if (MONTH_PATTERN.test(fileMonth) && fileMonth < month && fileUsed > 0) {
    history[fileMonth] = fileUsed;
  }

  return { used: 0, history: trimHistory(history) };
}

/** A top-up file: characters added, by month. Anything else in it is ignored. */
export function readTopUps(raw: unknown): Readonly<Record<string, number>> {
  const months: Record<string, number> = {};

  if (typeof raw !== "object" || raw === null || Array.isArray(raw)) {
    return months;
  }

  for (const [key, value] of Object.entries(raw as Record<string, unknown>)) {
    if (MONTH_PATTERN.test(key) && count(value) > 0) {
      months[key] = count(value);
    }
  }

  return months;
}

/** The newest VOICE_HISTORY_MONTHS months, oldest dropped. */
function trimHistory(history: Record<string, number>): Readonly<Record<string, number>> {
  const keys = Object.keys(history).sort();
  const kept: Record<string, number> = {};

  for (const key of keys.slice(Math.max(0, keys.length - VOICE_HISTORY_MONTHS))) {
    kept[key] = history[key] as number;
  }

  return kept;
}

/** A whole, non-negative number of characters, or 0. */
function count(value: unknown): number {
  return typeof value === "number" && Number.isFinite(value) && value > 0 ? Math.floor(value) : 0;
}

export function monthOf(when: Date): string {
  return `${when.getUTCFullYear()}-${String(when.getUTCMonth() + 1).padStart(2, "0")}`;
}

export function resetOf(when: Date): string {
  return new Date(Date.UTC(when.getUTCFullYear(), when.getUTCMonth() + 1, 1)).toISOString();
}

/** The day a preview is charged to: "2026-09-20", UTC like everything else here. */
export function dayOf(when: Date): string {
  return `${monthOf(when)}-${String(when.getUTCDate()).padStart(2, "0")}`;
}

/** When a day's previews come back: the first moment of tomorrow, UTC, ISO 8601. */
export function dayResetOf(when: Date): string {
  return new Date(Date.UTC(when.getUTCFullYear(), when.getUTCMonth(), when.getUTCDate() + 1)).toISOString();
}

function text(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
}

function number(value: unknown, fallback: number, low: number, high: number): number {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return fallback;
  }

  return Math.min(high, Math.max(low, value));
}

function whole(value: unknown, fallback: number, low: number, high: number, what: string, source: string): number {
  if (value === undefined || value === null) {
    return fallback;
  }

  if (typeof value !== "number" || !Number.isInteger(value) || value < low || value > high) {
    throw new Error(`"${what}" in ${source} must be a whole number from ${low} to ${high}, not ${JSON.stringify(value)}.`);
  }

  return value;
}

/**
 * Written to a temporary name and moved into place, so a line is never
 * half a file - a reader would take that for the whole line and play a
 * click.
 */
export async function writeFileAtomically(
  path: string,
  bytes: Buffer,
  owner?: { readonly uid: number; readonly gid: number },
): Promise<void> {
  const temp = `${path}.${Math.random().toString(36).slice(2)}.tmp`;

  try {
    await writeFile(temp, bytes, { flag: "wx", mode: 0o600 });

    // Handed over before it is in place, so nobody can ever read it at
    // its real name while it is still someone else's - see voice-topup.
    if (owner !== undefined) {
      await lchown(temp, owner.uid, owner.gid);
    }

    await rename(temp, path);
  } catch (error) {
    // Only when the move did not happen: after a rename there is nothing
    // at the temporary name, and unlinking it every time was a syscall per
    // cached line for nothing.
    await unlink(temp).catch(() => {});
    throw error;
  }
}
