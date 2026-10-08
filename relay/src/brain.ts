import { mkdir, readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import {
  monthOf,
  readUsageRecord,
  resetOf,
  VOICE_HISTORY_MONTHS,
  writeFileAtomically,
} from "./voice.js";

/**
 * The commentary brain: the words, where voice.ts is the speaking.
 *
 * HeatCaller's announcer has always been able to write colour commentary -
 * a "coming up" line before a round, a recap after the flag, an interview
 * with the winner's mechanic - and until now that writing happened on the
 * club's own laptop through Ollama. That worked, and it is kept (see
 * LocalLlmClient in the app), but it asked a club volunteer to install a
 * second program from a command prompt, and the small model it could
 * afford to run beside a race wrote like a small model.
 *
 * This moves the writing here, for the same reason the voice is here: the
 * account is HeatCaller's, the key is on the relay and nowhere else, and
 * the club needs nothing but the publish token it already has.
 *
 * WHY THIS IS AFFORDABLE WHERE THE VOICE IS NOT
 *
 * These two look alike and cost nothing alike. Speaking a line is priced
 * per character and a race night of it is real money, which is why
 * voice.ts counts every character and refuses past an allowance. Writing
 * the same line is priced per token at a few dollars a million, and a
 * whole race night of commentary is a few cents. The allowance here is
 * therefore a guard against a runaway loop rather than a budget: generous
 * by default, and counted in LINES rather than characters because one
 * request costs about the same as the next whatever comes back.
 *
 * NOTHING HERE MAY STOP A RACE
 *
 * Every refusal is a sentence, and the app treats all of them the same
 * way: it falls back to the club's own Ollama if it has one, and to the
 * template lines if it does not. Commentary is the garnish. A relay that
 * is down, out of allowance, or never configured must cost a club nothing
 * but the colour.
 */

/** What a club may do with the commentary brain. */
export interface BrainPlan {
  readonly enabled: boolean;
  /**
   * Lines this club may have written in a calendar month.
   *
   * Lines, not characters: the cost of a request is dominated by the
   * facts sent up with it, which are much the same size every time, so
   * counting requests is both cheaper to explain and closer to the truth
   * than counting what comes back.
   */
  readonly monthlyLines: number;
}

/** The brain block of the relay config: the model, and the defaults every club inherits. */
export interface BrainConfig {
  /** The model asked to write. Empty switches the brain off for the whole relay. */
  readonly model: string;
  readonly monthlyLines: number;
  /** The longest answer the brain will ask for, whatever a laptop requests. */
  readonly maxWords: number;
}

/**
 * Claude Haiku: the cheapest current model, and the one this is sized for.
 *
 * Deliberately pinned rather than floating on an alias. The announcer's
 * voice - its register, how long its sentences run - is a product
 * decision, and a model that changed under a club mid-season would change
 * how their race nights sound without anyone choosing that.
 */
export const BRAIN_DEFAULT_MODEL = "claude-haiku-4-5-20251001";

/**
 * 400 lines a month. A busy club night generates perhaps twenty - an
 * intro and a recap per round, with talking points between - so this is
 * roughly twenty race nights, which no club runs. It is a runaway guard.
 */
export const BRAIN_DEFAULT_MONTHLY_LINES = 400;

/** Long enough for a broadcast recap, short enough that nothing blocks the announcer's queue behind it. */
export const BRAIN_DEFAULT_MAX_WORDS = 120;

/** The plan a club has when the config says nothing: the brain is not included. */
export const BRAIN_PLAN_OFF: BrainPlan = Object.freeze({ enabled: false, monthlyLines: 0 });

/** Where the brain keeps its counts. Beside the voice's folder, not inside it: different money, different file. */
export const BRAIN_DIR = ".brain";

/** A club's standing with the brain, as the app shows it: "37 of 400 lines this month". */
export interface BrainUsage {
  readonly used: number;
  readonly allowance: number;
  /** When the count goes back to nothing, ISO-8601. */
  readonly resetsAt: string;
}

export interface BrainCatalogueView {
  readonly enabled: boolean;
  readonly model: string;
  readonly usage: BrainUsage;
}

export type BrainRefusal =
  /** This relay has no key, or no model: the brain is not switched on at all. */
  | "off"
  /** The relay has a brain, but this club's plan does not include it. */
  | "not-included"
  /** The club has written its allowance of lines for the month. */
  | "allowance"
  /** The prompt was empty, or longer than the brain will take at once. */
  | "text"
  /** The brain or the model was busy. Worth trying again later. */
  | "busy"
  /** The model answered with something unusable, or did not answer. */
  | "upstream";

export interface WriteOk {
  readonly ok: true;
  readonly text: string;
  readonly usage: BrainUsage;
}

export interface WriteNo {
  readonly ok: false;
  readonly refusal: BrainRefusal;
  readonly status: number;
  readonly message: string;
  readonly usage?: BrainUsage;
}

export type WriteResult = WriteOk | WriteNo;

/** What actually asks the model. Separated so the tests can run the whole service without a key or a network. */
export type WriteFn = (
  system: string,
  user: string,
  maxTokens: number,
  temperature: number,
) => Promise<string>;

/** Thrown by a WriteFn when the model itself failed, so the service can tell "busy" from "broken". */
export class BrainUpstreamError extends Error {
  constructor(
    message: string,
    readonly retryable: boolean,
  ) {
    super(message);
    this.name = "BrainUpstreamError";
  }
}

export interface BrainServiceOptions {
  readonly config: BrainConfig;
  readonly dataDir: string;
  /** Absent means no key: the brain answers "off" to everything rather than failing. */
  readonly write?: WriteFn | undefined;
  readonly log?: ((line: string) => void) | undefined;
  readonly now?: (() => Date) | undefined;
  /** File ownership for the counts, when the relay has given up root. */
  readonly owner?: { readonly uid: number; readonly gid: number } | undefined;
}

/** The longest prompt a laptop may send. The facts for a recap are a few hundred characters; this is far above that and well below anything that costs real money. */
const MAX_PROMPT_CHARS = 8_000;

/**
 * At most two lines being written for one club at a time.
 *
 * A race night asks for these one at a time with gaps between, so two is
 * already slack. The cap exists because a laptop in a retry loop should
 * cost a club its allowance slowly enough to notice, not in a burst.
 */
const MAX_IN_FLIGHT_PER_CLUB = 2;

/**
 * How many times a count file write is attempted before giving up. Three
 * tries at 20ms and 40ms covers the brief window a Windows virus scanner
 * holds a file it has just seen written; anything past that is a real
 * problem with the disk and should be reported rather than waited on.
 */
const SAVE_ATTEMPTS = 3;

export class BrainService {
  private readonly config: BrainConfig;
  private readonly dataDir: string;
  private readonly writeFn: WriteFn | undefined;
  private readonly log: (line: string) => void;
  private readonly now: () => Date;
  private readonly owner: { readonly uid: number; readonly gid: number } | undefined;

  /** Lines being written right now, by club. */
  private readonly inFlight = new Map<string, number>();

  /**
   * One write of a club's count at a time.
   *
   * Two lines finishing together would otherwise both read the same count,
   * both add one, and both save - and the club would have spent two lines
   * off an allowance that only moved by one. Same reasoning as voice.ts.
   */
  private readonly counting = new Map<string, Promise<unknown>>();

  constructor(options: BrainServiceOptions) {
    this.config = options.config;
    this.dataDir = options.dataDir;
    this.writeFn = options.write;
    this.log = options.log ?? (() => {});
    this.now = options.now ?? (() => new Date());
    this.owner = options.owner;
  }

  /** True when this relay could write a line for somebody - a model and a key. */
  get available(): boolean {
    return this.writeFn !== undefined && this.config.model.length > 0;
  }

  /** What the app shows on the Commentary page: whether the brain is on for this club, and where its month stands. */
  async catalogue(club: string, plan: BrainPlan): Promise<BrainCatalogueView> {
    const on = this.available && plan.enabled;

    return {
      enabled: on,
      model: on ? this.config.model : "",
      usage: await this.usageFor(club, plan),
    };
  }

  /**
   * One line of commentary, written to order.
   *
   * `system` is the announcer's character and the rules it writes under;
   * `user` carries the facts HeatCaller has already worked out. The model
   * is never asked to work anything out - only to phrase it - which is
   * what keeps a wrong lap time out of the PA.
   */
  async write(
    club: string,
    plan: BrainPlan,
    system: string,
    user: string,
    maxWords: number,
    temperature: number,
  ): Promise<WriteResult> {
    if (!this.available) {
      return {
        ok: false,
        refusal: "off",
        status: 503,
        message:
          "The HeatCaller commentary brain isn't switched on. Use Ollama on the race laptop for now, " +
          "or turn commentary off - the announcer's normal lines are unaffected.",
      };
    }

    if (!plan.enabled) {
      return {
        ok: false,
        refusal: "not-included",
        status: 403,
        message: "Commentary isn't included for this club yet.",
        usage: await this.usageFor(club, plan),
      };
    }

    const prompt = user.trim();

    if (prompt.length === 0 || prompt.length > MAX_PROMPT_CHARS || system.length > MAX_PROMPT_CHARS) {
      return {
        ok: false,
        refusal: "text",
        status: 400,
        message: `A commentary request must carry facts, and no more than ${MAX_PROMPT_CHARS} characters of them.`,
      };
    }

    const running = this.inFlight.get(club) ?? 0;

    if (running >= MAX_IN_FLIGHT_PER_CLUB) {
      return {
        ok: false,
        refusal: "busy",
        status: 429,
        message: "Already writing for this club. The announcer carries on with its usual lines.",
        usage: await this.usageFor(club, plan),
      };
    }

    // Spent before the model is asked, not after. Two requests arriving
    // together must not both read the same count, both pass, and both
    // spend - so the allowance moves first and is handed back below if
    // nothing came of it.
    const reserved = await this.reserve(club, plan);

    if (reserved === "unreadable") {
      return {
        ok: false,
        refusal: "busy",
        status: 503,
        message: "The commentary brain can't check this club's allowance just now.",
      };
    }

    if (reserved === "spent") {
      return {
        ok: false,
        refusal: "allowance",
        status: 429,
        message:
          "This club has used its commentary allowance for the month. The announcer's normal lines carry on as usual.",
        usage: await this.usageFor(club, plan),
      };
    }

    this.inFlight.set(club, running + 1);

    try {
      const words = Math.max(10, Math.min(maxWords, this.config.maxWords));

      // Roughly four tokens to three words, rounded up and given room:
      // the cap is a safety net against a model that will not stop, not
      // the length being asked for - the prompt asks for that in words.
      const maxTokens = Math.ceil(words * 2) + 40;

      const answer = await (this.writeFn as WriteFn)(system, prompt, maxTokens, temperature);
      const line = answer.trim();

      if (line.length === 0) {
        await this.refund(club, reserved.month);

        return {
          ok: false,
          refusal: "upstream",
          status: 502,
          message: "The commentary brain answered with nothing.",
          usage: await this.usageFor(club, plan),
        };
      }

      return { ok: true, text: line, usage: await this.usageFor(club, plan) };
    } catch (error) {
      // A line that was never written is a line the club keeps.
      await this.refund(club, reserved.month);

      const upstream = error instanceof BrainUpstreamError;
      const retryable = upstream && error.retryable;

      this.log(`[brain] "${club}": ${error instanceof Error ? error.message : String(error)}`);

      return {
        ok: false,
        refusal: retryable ? "busy" : "upstream",
        status: retryable ? 429 : 502,
        message: retryable
          ? "The commentary brain is busy. The announcer carries on with its usual lines."
          : "The commentary brain couldn't write that one.",
        usage: await this.usageFor(club, plan),
      };
    } finally {
      const left = (this.inFlight.get(club) ?? 1) - 1;

      if (left <= 0) {
        this.inFlight.delete(club);
      } else {
        this.inFlight.set(club, left);
      }
    }
  }

  // --- The allowance --------------------------------------------------------

  private async usageFor(club: string, plan: BrainPlan): Promise<BrainUsage> {
    const month = monthOf(this.now());
    const read = await this.readCount(club, month);

    return {
      used: read === "unreadable" ? 0 : read.used,
      allowance: plan.monthlyLines,
      resetsAt: resetOf(this.now()),
    };
  }

  /**
   * Takes one line off this club's month, or says why it cannot.
   *
   * "unreadable" is its own answer rather than being treated as nothing
   * used: a count file that will not read must never hand a club a fresh
   * allowance by accident. The brain would rather write nothing than spend
   * against a number it does not trust.
   */
  private async reserve(club: string, plan: BrainPlan): Promise<{ month: string } | "spent" | "unreadable"> {
    try {
      return await this.reserveOrThrow(club, plan);
    } catch (error) {
      // A disk that would not take the count. Answered as "cannot check
      // just now" rather than thrown: this is called from write(), and an
      // exception there would reach the request handler as a 500 for what
      // is only a line of colour nobody is waiting on.
      this.log(`[brain] "${club}": could not take a line off the month (${String(error)}).`);
      return "unreadable";
    }
  }

  private async reserveOrThrow(club: string, plan: BrainPlan): Promise<{ month: string } | "spent" | "unreadable"> {
    return (await this.serialise(club, async () => {
      const month = monthOf(this.now());
      const read = await this.readCount(club, month);

      if (read === "unreadable") {
        return "unreadable" as const;
      }

      if (read.used >= plan.monthlyLines) {
        return "spent" as const;
      }

      await this.saveCount(club, month, read.used + 1, read.history);

      return { month };
    })) as { month: string } | "spent" | "unreadable";
  }

  /** Hands a line back when nothing came of it. Best effort: a failure here costs the club one line, not a race. */
  private async refund(club: string, month: string): Promise<void> {
    try {
      await this.serialise(club, async () => {
        const read = await this.readCount(club, month);

        if (read === "unreadable") {
          return;
        }

        await this.saveCount(club, month, Math.max(0, read.used - 1), read.history);
      });
    } catch (error) {
      this.log(`[brain] "${club}": could not hand back a line (${String(error)}).`);
    }
  }

  /** One count operation per club at a time - see the note on `counting`. */
  private async serialise<T>(club: string, work: () => Promise<T>): Promise<T> {
    const before = this.counting.get(club) ?? Promise.resolve();

    // `then(work, work)` on purpose: the next operation runs whether the
    // one before it worked or threw. A failed save must not wedge every
    // later line for that club behind a promise that never settles well.
    const mine = before.then(work, work);
    const guard = mine.then(
      () => {},
      () => {},
    );

    this.counting.set(club, guard);

    try {
      return await mine;
    } finally {
      // Only when nothing queued behind this one. Deleting unconditionally
      // would drop a chain another caller is already waiting on, and two
      // counts would run at once - the exact race this exists to stop.
      if (this.counting.get(club) === guard) {
        this.counting.delete(club);
      }
    }
  }

  private async readCount(
    club: string,
    month: string,
  ): Promise<{ used: number; history: Readonly<Record<string, number>> } | "unreadable"> {
    let raw: string;

    try {
      raw = await readFile(brainUsagePath(this.dataDir, club), "utf8");
    } catch (error) {
      // No file at all is a club that has never had a line written: zero
      // used, which is the truth. Anything else is a file that should be
      // readable and is not, and that is not something to spend against.
      if ((error as NodeJS.ErrnoException).code === "ENOENT") {
        return { used: 0, history: {} };
      }

      this.log(`[brain] "${club}": could not read the count (${String(error)}); not spending against it until it reads.`);
      return "unreadable";
    }

    try {
      return readUsageRecord(JSON.parse(raw), month);
    } catch (error) {
      this.log(`[brain] "${club}": the count is not readable JSON (${String(error)}); not spending against it.`);
      return "unreadable";
    }
  }

  private async saveCount(
    club: string,
    month: string,
    used: number,
    history: Readonly<Record<string, number>>,
  ): Promise<void> {
    const path = brainUsagePath(this.dataDir, club);

    await mkdir(dirname(path), { recursive: true });

    const kept = Object.entries(history)
      .filter(([key]) => key < month)
      .sort(([a], [b]) => (a < b ? 1 : -1))
      .slice(0, VOICE_HISTORY_MONTHS)
      .reduce<Record<string, number>>((all, [key, value]) => {
        all[key] = value;
        return all;
      }, {});

    const bytes = Buffer.from(JSON.stringify({ month, used, history: kept }), "utf8");

    // Windows refuses a rename onto a path something else still has open,
    // with EPERM, and "something else" is routinely a virus scanner that
    // opened the file a millisecond ago because it was just written. It
    // clears on its own in a few milliseconds. Writes here are already
    // serialised per club, so this is not a race of ours to fix - it is
    // the filesystem needing a moment, and the alternative is a club
    // losing a count to an antivirus.
    for (let attempt = 1; ; attempt++) {
      try {
        await writeFileAtomically(path, bytes, this.owner);
        return;
      } catch (error) {
        const code = (error as NodeJS.ErrnoException).code;
        const worthRetrying = code === "EPERM" || code === "EBUSY" || code === "EACCES";

        if (!worthRetrying || attempt >= SAVE_ATTEMPTS) {
          throw error;
        }

        await new Promise((done) => setTimeout(done, attempt * 20));
      }
    }
  }
}

/** Where a club's line count lives. */
export function brainUsagePath(dataDir: string, club: string): string {
  return join(dataDir, BRAIN_DIR, "usage", `${club}.json`);
}

/**
 * Asks Claude for the line, over the Messages API.
 *
 * Plain fetch rather than the SDK: one POST with no streaming and no tool
 * use is not worth a dependency on a relay that currently has none but
 * TypeScript itself.
 */
export function anthropicWriter(apiKey: string, model: string, timeoutMs = 20_000): WriteFn {
  return async (system, user, maxTokens, temperature) => {
    const give = AbortSignal.timeout(timeoutMs);

    let response: Response;

    try {
      response = await fetch("https://api.anthropic.com/v1/messages", {
        method: "POST",
        headers: {
          "content-type": "application/json",
          "x-api-key": apiKey,
          "anthropic-version": "2023-06-01",
        },
        body: JSON.stringify({
          model,
          max_tokens: maxTokens,
          temperature,
          system,
          messages: [{ role: "user", content: user }],
        }),
        signal: give,
      });
    } catch (error) {
      // A timeout or a dead network: worth trying again, so the app may
      // ask for the next line rather than giving up on the brain.
      throw new BrainUpstreamError(`the model could not be reached (${String(error)})`, true);
    }

    if (!response.ok) {
      const body = (await response.text().catch(() => "")).slice(0, 300);

      // 429 is rate limiting and 5xx is their end: both pass. 400 and 401
      // are this relay's mistake - a bad key, a model that does not exist
      // - and retrying those just spends the club's allowance on the same
      // failure, so they do not.
      const retryable = response.status === 429 || response.status >= 500;

      throw new BrainUpstreamError(`the model answered ${response.status}: ${body}`, retryable);
    }

    const payload = (await response.json().catch(() => null)) as
      | { content?: { type?: string; text?: string }[] }
      | null;

    const text = (payload?.content ?? [])
      .filter((part) => part?.type === "text" && typeof part.text === "string")
      .map((part) => part.text as string)
      .join(" ")
      .trim();

    if (text.length === 0) {
      throw new BrainUpstreamError("the model answered with no text", false);
    }

    return text;
  };
}

/** Reads the brain block of the config, and refuses to start rather than starting wrong. */
export function readBrainConfig(value: unknown, source: string): BrainConfig {
  if (value === undefined || value === null) {
    return { model: "", monthlyLines: BRAIN_DEFAULT_MONTHLY_LINES, maxWords: BRAIN_DEFAULT_MAX_WORDS };
  }

  if (typeof value !== "object" || Array.isArray(value)) {
    throw new Error(`"brain" in ${source} must be an object.`);
  }

  const raw = value as Record<string, unknown>;
  const model = typeof raw["model"] === "string" ? raw["model"].trim() : BRAIN_DEFAULT_MODEL;

  return {
    model,
    monthlyLines: wholeNumber(
      raw["monthlyLines"],
      BRAIN_DEFAULT_MONTHLY_LINES,
      0,
      1_000_000,
      "brain.monthlyLines",
      source,
    ),
    maxWords: wholeNumber(raw["maxWords"], BRAIN_DEFAULT_MAX_WORDS, 10, 500, "brain.maxWords", source),
  };
}

/** Reads one club's brain plan. Opt-in, like the voice, and for the same reason: it spends money. */
export function readBrainPlan(value: unknown, defaults: BrainConfig, club: string, source: string): BrainPlan {
  if (value === undefined || value === null) {
    return { enabled: false, monthlyLines: defaults.monthlyLines };
  }

  if (typeof value !== "object" || Array.isArray(value)) {
    throw new Error(`"brain" for club "${club}" in ${source} must be an object.`);
  }

  const raw = value as Record<string, unknown>;

  return {
    enabled: raw["enabled"] === true,
    monthlyLines: wholeNumber(
      raw["monthlyLines"],
      defaults.monthlyLines,
      0,
      1_000_000,
      `brain.monthlyLines for club "${club}"`,
      source,
    ),
  };
}

function wholeNumber(value: unknown, fallback: number, low: number, high: number, what: string, source: string): number {
  if (value === undefined || value === null) {
    return fallback;
  }

  if (typeof value !== "number" || !Number.isInteger(value) || value < low || value > high) {
    throw new Error(`"${what}" in ${source} must be a whole number from ${low} to ${high}, not ${JSON.stringify(value)}.`);
  }

  return value;
}
