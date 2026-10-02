import { createHash } from "node:crypto";
import { access, lchown, mkdir, readdir, readFile, rename, rm, stat, unlink, utimes, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { isValidClubId } from "./tenancy.js";

/**
 * The HeatCaller voice: ElevenLabs, spoken through the relay, so a club
 * needs no account, no key and no bill of its own.
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
}

/** What one club may do with the HeatCaller voice. */
export interface VoicePlan {
  readonly enabled: boolean;
  /** Characters this club may send to ElevenLabs in a calendar month. Cache hits cost nothing and are not counted. */
  readonly monthlyChars: number;
  /** Which catalogue ids this club may use. Empty means every voice in the catalogue. */
  readonly voices: readonly string[];
}

/** The voice block of the relay config: the catalogue and the defaults every club inherits. */
export interface VoiceConfig {
  readonly voices: readonly CatalogueVoice[];
  readonly monthlyChars: number;
  readonly maxChars: number;
}

export const VOICE_DEFAULT_MONTHLY_CHARS = 100_000;
export const VOICE_DEFAULT_MAX_CHARS = 600;

/** A club's standing with the voice, as the app shows it: "34,120 of 100,000 characters this month". */
export interface VoiceUsage {
  readonly used: number;
  readonly allowance: number;
  /** When the count goes back to zero: the first moment of next month, UTC, ISO 8601. */
  readonly resetsAt: string;
}

export interface VoiceCatalogueView {
  readonly enabled: boolean;
  readonly voices: readonly { id: string; name: string; what: string; rev: string }[];
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
}

export interface SpeakNo {
  readonly ok: false;
  readonly refusal: VoiceRefusal;
  readonly status: number;
  readonly message: string;
  readonly usage?: VoiceUsage;
}

export type SpeakResult = SpeakOk | SpeakNo;

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
 * laptop's Windows voice says the line.
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

export class VoiceService {
  private readonly config: VoiceConfig;
  private readonly dataDir: string;
  private readonly render: RenderFn | undefined;
  private readonly now: () => Date;
  private readonly log: (line: string) => void;
  private readonly maxInFlight: number;
  private readonly maxWaiting: number;
  private readonly sharedCacheBytes: number;
  private readonly clubCacheBytes: number;

  private readonly byId = new Map<string, CatalogueVoice>();
  private readonly revs = new Map<string, string>();

  /** Usage writes, one club at a time, so two lines finishing together cannot both read 10 and both write 11. */
  private readonly usageLocks = new Map<string, Promise<void>>();
  private readonly usageCache = new Map<string, { month: string; used: number; history: Readonly<Record<string, number>> }>();

  /** Each club's top-up file, as last read, by its modification time - see topUpFor. */
  private readonly topUpCache = new Map<string, { mtimeMs: number; months: Readonly<Record<string, number>> }>();

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
    this.now = options.now ?? (() => new Date());
    this.log = options.log ?? (() => {});
    this.maxInFlight = options.maxInFlight ?? DEFAULT_MAX_IN_FLIGHT;
    this.maxWaiting = options.maxWaiting ?? DEFAULT_MAX_WAITING;
    this.maxInFlightPerClub = options.maxInFlightPerClub ?? DEFAULT_MAX_IN_FLIGHT_PER_CLUB;
    this.maxWaitingPerClub = options.maxWaitingPerClub ?? DEFAULT_MAX_WAITING_PER_CLUB;
    this.root = join(this.dataDir, VOICE_DIR);
    this.sharedCacheBytes = options.sharedCacheBytes ?? DEFAULT_SHARED_CACHE_BYTES;
    this.clubCacheBytes = options.clubCacheBytes ?? DEFAULT_CLUB_CACHE_BYTES;

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
    const usage = await this.usageFor(club, plan);

    return {
      enabled: this.available && plan.enabled,
      voices: this.voicesFor(plan).map((voice) => ({
        id: voice.id,
        name: voice.name,
        what: voice.what,
        rev: this.revs.get(voice.id) ?? "",
      })),
      usage,
    };
  }

  /** The catalogue entries a club may use, in config order. */
  private voicesFor(plan: VoicePlan): readonly CatalogueVoice[] {
    if (plan.voices.length === 0) {
      return this.config.voices;
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
  async speak(club: string, plan: VoicePlan, voiceId: string, text: string, shared: boolean): Promise<SpeakResult> {
    if (!isValidClubId(club)) {
      return { ok: false, refusal: "unknown-voice", status: 404, message: "No such club." };
    }

    if (this.render === undefined || this.config.voices.length === 0) {
      return {
        ok: false,
        refusal: "off",
        status: 503,
        message: "The HeatCaller voice isn't switched on. Use your own ElevenLabs key or the free Windows voice for now.",
      };
    }

    if (!plan.enabled) {
      return {
        ok: false,
        refusal: "not-included",
        status: 403,
        message: "The HeatCaller voice isn't included for this club. Use your own ElevenLabs key or the free Windows voice.",
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

    // Tags like [excited] are v3's; every other model reads them out as
    // words. The laptop cannot know which model a HeatCaller voice uses -
    // that is deliberately not published - so the tags are taken out here.
    const spoken = voice.modelId.toLowerCase().includes("v3") ? text.trim() : stripAudioTags(text);

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

    const key = cacheKey(voice, spoken);
    const cached = await this.readCache(club, shared, key);

    if (cached !== undefined) {
      // Free: nothing reached ElevenLabs, so nothing is counted. This is
      // the whole point of the cache, and of Prepare Lines on the laptop.
      return { ok: true, audio: cached, cached: true, usage: await this.usageFor(club, plan) };
    }

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
        message: "The HeatCaller voice can't check this club's allowance just now. The free Windows voice speaks this one.",
        usage: reserved.usage,
      };
    }

    if (!reserved.ok) {
      return {
        ok: false,
        refusal: "allowance",
        status: 402,
        message:
          "This club has used its HeatCaller voice allowance for the month. " +
          "The free Windows voice speaks until it resets, and every line already saved is kept.",
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
        message: "The HeatCaller voice is busy. The free Windows voice speaks this one.",
        usage: await this.usageFor(club, plan),
      };
    }

    let audio: Buffer;

    try {
      audio = await this.render(voice, spoken);
    } catch (error) {
      const status = error instanceof VoiceUpstreamError ? error.status : 0;

      this.log(`[voice] "${club}": ElevenLabs did not speak a line (${status === 0 ? String(error) : status}).`);

      // Nothing was rendered, so nothing was spent.
      await this.refund(club, spoken.length, reserved.month);

      return {
        ok: false,
        refusal: "upstream",
        status: 502,
        message: "The HeatCaller voice couldn't speak that line. The free Windows voice says it instead.",
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
        message: "The HeatCaller voice couldn't speak that line. The free Windows voice says it instead.",
        usage: await this.usageFor(club, plan),
      };
    }

    // The characters are already counted (see reserve). A cache write that
    // fails costs the club nothing beyond rendering the line again.
    await this.writeCache(club, shared, key, audio);

    return { ok: true, audio, cached: false, usage };
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
    return (await this.usageState(club, plan, month)).usage;
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
      // kept, and not spent against. See above.
      return { usage: { used: 0, allowance, resetsAt: resetOf(this.now()) }, known: false };
    }

    this.usageCache.set(club, { month, used: read.used, history: read.history });

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
        // says it in the free Windows voice and asks again with the next
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
    const before = this.usageLocks.get(club) ?? Promise.resolve();
    let release: () => void = () => {};
    const mine = new Promise<void>((resolve) => {
      release = resolve;
    });

    // The chained promise is what the map holds, so it is what the check
    // below must compare with: comparing with `mine` never matched, and a
    // club's entry was never let go.
    const tail = before.then(() => mine);

    this.usageLocks.set(club, tail);

    await before;

    try {
      await work();
    } finally {
      release();

      if (this.usageLocks.get(club) === tail) {
        this.usageLocks.delete(club);
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
   * folder is swept.
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

/**
 * What ElevenLabs is actually asked, when a key is configured.
 *
 * The same request the laptop used to make with the club's own key - same
 * fields, same shape - so a line rendered here and one a club rendered for
 * itself sound identical.
 */
export function elevenLabsRenderer(apiKey: string, timeoutMs = 30_000): RenderFn {
  return async (voice, text) => {
    const format = voice.outputFormat.trim();
    const url =
      `https://api.elevenlabs.io/v1/text-to-speech/${encodeURIComponent(voice.voiceId)}` +
      (format.length > 0 ? `?output_format=${encodeURIComponent(format)}` : "");

    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);

    try {
      const response = await fetch(url, {
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
      });

      if (!response.ok) {
        // The body is read but never passed on: it can carry the account's
        // own details, and a club has no use for them.
        const body = await response.text().catch(() => "");
        throw new VoiceUpstreamError(response.status, `ElevenLabs answered ${response.status}: ${body.slice(0, 200)}`);
      }

      const bytes = Buffer.from(await response.arrayBuffer());

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
    return { voices: [], monthlyChars: VOICE_DEFAULT_MONTHLY_CHARS, maxChars: VOICE_DEFAULT_MAX_CHARS };
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
      modelId: text(voice["modelId"]).length > 0 ? text(voice["modelId"]) : "eleven_flash_v2_5",
      stability: number(voice["stability"], 0.5, 0, 1),
      similarity: number(voice["similarity"], 0.75, 0, 1),
      style: number(voice["style"], 0, 0, 1),
      speakerBoost: voice["speakerBoost"] !== false,
      speed: number(voice["speed"], 1, 0.7, 1.2),
      outputFormat: text(voice["outputFormat"]),
    });
  }

  return {
    voices,
    monthlyChars: whole(raw["monthlyChars"], VOICE_DEFAULT_MONTHLY_CHARS, 0, 100_000_000, "voice.monthlyChars", source),
    maxChars: whole(raw["maxChars"], VOICE_DEFAULT_MAX_CHARS, 40, 5_000, "voice.maxChars", source),
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
      throw new Error(`Club "${club}" in ${source} names a voice "${id}" that the catalogue does not have.`);
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

/** "[excited] Green flag!" -> "Green flag!", for the models that would read the tag out loud. */
export function stripAudioTags(value: string): string {
  // Character for character what VoiceProfile.StripAudioTags does on the
  // laptop - a tag is replaced with nothing, then doubled spaces collapse.
  // The two have to agree: the Windows voice says the stripped line when it
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
