import { StoreReadError, type Store } from "./store.js";

/**
 * Where a club's wrong-PIN count is kept. A snapshot name no channel
 * uses, like the check-in inbox: nobody can publish to it or read it
 * over the API, and it lives in the club's own folder, so deleting the
 * club deletes it too.
 */
export const PIN_GUARD_DOC = "pinguard";

/** Wrong PINs, from every address together, before a club's PIN list closes. */
export const CLUB_FAIL_LIMIT = 50;

/** How far back those wrong PINs are counted. */
export const CLUB_FAIL_WINDOW_MS = 10 * 60 * 1000;

/**
 * How long the list stays closed, the first time the limit is hit and
 * each time after. The first matches what the relay has always done; each
 * lockout after that, with no quiet day in between, is longer, up to half
 * a day - which is where it stays however long somebody keeps at it.
 */
export const LOCK_STEPS_MS: readonly number[] = Object.freeze([
  10 * 60 * 1000,
  30 * 60 * 1000,
  2 * 60 * 60 * 1000,
  6 * 60 * 60 * 1000,
  12 * 60 * 60 * 1000,
]);

/** A day with no lockout and the next one starts from the first step again. */
export const QUIET_RESET_MS = 24 * 60 * 60 * 1000;

interface GuardState {
  /** When each recent wrong PIN arrived, oldest first, milliseconds. */
  fails: number[];
  /** How many lockouts in a row, 0 for none. Picks the next lockout's length. */
  level: number;
  /** Milliseconds; the list is closed until then. 0 for never locked. */
  lockedUntil: number;
  /**
   * Whether what is on disk has been read into this. False while the file
   * could not be read: the count is then kept in memory only, is never
   * written (that would put a count of this morning's guesses over a
   * lockout the file may be holding), and is merged with the file once a
   * later read works.
   */
  loaded: boolean;
  /** Reset while not loaded: whatever the file says is older than the reset, and is not merged back in. */
  resetUnsaved: boolean;
}

/**
 * The club-wide wrong-PIN count, kept on disk.
 *
 * The per-address count stays in memory (see Limiter), but this one
 * cannot: the relay stops when nobody is using it, and a count that
 * started from nothing on every wake-up handed anyone patient a fresh
 * fifty guesses each time. Written after every wrong PIN - at most fifty
 * small writes per club per lockout - and read back the first time a
 * club's PINs are asked about after a start.
 *
 * Why it escalates: fifty guesses every ten minutes, forever, is seven
 * thousand a day - a four-digit staff PIN in under two days, and with a
 * hundred racers holding six-digit PINs, one of them in about as long.
 * With the lockout growing to twelve hours, somebody keeping at it gets
 * about a hundred guesses a day instead.
 *
 * Why it stops at twelve hours, and what it costs: anybody can close a
 * club's PIN list by typing fifty wrong PINs, so the lockout is also a
 * way to annoy a club. That is why it never grows past half a day, why a
 * quiet day forgets the history, and why the relay keeps it from hurting
 * much: a phone or tablet already signed in keeps working throughout
 * (it proves itself with a pass, not a guess - see the check-in route),
 * badges never stop working, and a club that changes its staff PIN on
 * the laptop reopens the list at once, since every guess made so far was
 * at a PIN that no longer exists.
 *
 * How the check-in route uses it: `await ready(club)` first, then
 * lockedUntilNow, the PIN comparison and failNow with no await in
 * between. Node runs one request at a time between awaits, so a burst of
 * guesses - which is what a relay woken from sleep is handed, every
 * request that queued while it started released at once - is checked and
 * counted one guess at a time. Checking after an await and counting after
 * another let every guess in such a burst through before the first was
 * counted.
 *
 * One instance guards one kind of PIN. A second kind - the race-day
 * manager's - gets a second instance, its own document (docName) so the
 * two counts cannot overwrite each other, and its own wording for the two
 * log lines (guardedPins, resetSubject); everything above applies to it
 * the same way, independently, per club.
 */
export class PinGuard {
  private readonly states = new Map<string, GuardState>();
  private readonly loading = new Map<string, Promise<GuardState>>();
  private readonly writes = new Map<string, Promise<void>>();
  /** Bumped by forget, so a read that started before a club was deleted is not kept after it. */
  private readonly generations = new Map<string, number>();
  /** Clubs being deleted right now: nothing is written for them until the files are gone. */
  private readonly forgetting = new Set<string>();
  private lastWriteError = 0;
  private lastReadError = 0;

  constructor(
    private readonly store: Store,
    private readonly clock: () => number,
    private readonly log: (line: string) => void,
    /**
     * Where this instance's count is kept - a second guard, for a second
     * kind of PIN, needs a document of its own or the two would overwrite
     * one another. Must fit store.ts's channel-name pattern.
     */
    private readonly docName: string = PIN_GUARD_DOC,
    /** Read "wrong ___ PINs" in the lockout log line - what a guess against ANY of this instance's PINs is called. */
    private readonly guardedPins: string = "check-in",
    /** Read "___ changed, so its wrong-PIN count starts again." - the one PIN whose change reopens the list (reset is never called for the others). */
    private readonly resetSubject: string = "the staff PIN",
  ) {}

  /**
   * Reads the club's count from disk if it has not been yet. Never throws:
   * a file that cannot be read leaves a count in memory that still decides,
   * and the next call tries the file again.
   */
  async ready(club: string): Promise<void> {
    await this.load(club);
  }

  /** When this club's PIN list opens again, in milliseconds - or 0 when it is open now. */
  async lockedUntil(club: string): Promise<number> {
    await this.ready(club);
    return this.lockedUntilNow(club);
  }

  /**
   * The same, from memory, without waiting for anything - for use after
   * ready, in the same step as the PIN comparison and failNow.
   */
  lockedUntilNow(club: string): number {
    const state = this.held(club);
    const now = this.clock();

    this.settle(state, now);

    return state.lockedUntil > now ? state.lockedUntil : 0;
  }

  /** Counts one wrong PIN, closing the list if that was one too many. Never throws. */
  async fail(club: string): Promise<void> {
    await this.ready(club);
    await this.failNow(club);
  }

  /**
   * The same, counted in memory before this returns - so the next request
   * already sees it - and saved in the background. The promise is the save
   * and never rejects.
   */
  failNow(club: string): Promise<void> {
    const state = this.held(club);
    const now = this.clock();

    this.settle(state, now);

    // A guess can arrive while the list closes, having been let in just
    // before. It is not counted again: the list is shut either way.
    if (state.lockedUntil > now) {
      return Promise.resolve();
    }

    state.fails = [...state.fails.filter((t) => t > now - CLUB_FAIL_WINDOW_MS), now];

    if (state.fails.length >= CLUB_FAIL_LIMIT) {
      state.level = Math.min(state.level + 1, LOCK_STEPS_MS.length);
      state.lockedUntil = now + (LOCK_STEPS_MS[state.level - 1] as number);
      state.fails = [];

      this.log(
        `[relay] "${club}": ${CLUB_FAIL_LIMIT} wrong ${this.guardedPins} PINs - the PIN list is closed for ` +
          `${Math.round((state.lockedUntil - now) / 60000)} minutes (lockout ${state.level} in a row).`,
      );
    }

    return this.save(club);
  }

  /** Forgets everything - for a club that has changed its staff PIN. Never throws. */
  async reset(club: string): Promise<void> {
    await this.ready(club);

    const state = this.held(club);

    if (state.level === 0 && state.lockedUntil === 0 && state.fails.length === 0 && state.loaded) {
      return;
    }

    state.fails = [];
    state.level = 0;
    state.lockedUntil = 0;
    state.resetUnsaved = !state.loaded;

    this.log(`[relay] "${club}": ${this.resetSubject} changed, so its wrong-PIN count starts again.`);
    await this.save(club);
  }

  /**
   * For a club being deleted: stops writing its count, lets a write already
   * under way finish, runs `wipe` - which removes the club's files - and
   * then forgets the count in memory. In that order so that neither a save
   * that was mid-flight nor one queued behind it can put the file back
   * after the wipe, and so that a count read from the file before the wipe
   * is not kept after it.
   */
  async forget(club: string, wipe: () => Promise<void>): Promise<void> {
    this.forgetting.add(club);

    try {
      await (this.writes.get(club) ?? Promise.resolve());
      await wipe();
    } finally {
      this.generations.set(club, (this.generations.get(club) ?? 0) + 1);
      this.states.delete(club);
      this.loading.delete(club);
      this.forgetting.delete(club);
    }
  }

  /** The history fades: after a quiet day, the next lockout is the short one again. */
  private settle(state: GuardState, now: number): void {
    if (state.level > 0 && state.lockedUntil <= now && now - state.lockedUntil >= QUIET_RESET_MS) {
      state.level = 0;
    }
  }

  /**
   * The club's state in memory. Normally ready has put it there; if not -
   * the club was deleted in between - a fresh one, not yet loaded, so
   * nothing is written for it until the file has been read.
   */
  private held(club: string): GuardState {
    let state = this.states.get(club);

    if (state === undefined) {
      state = { ...fresh(), loaded: false };
      this.states.set(club, state);
    }

    return state;
  }

  /**
   * The club's state, read from disk once and held after that. Two first
   * asks share one read. A read that fails is tried again by the next ask,
   * with what was counted meanwhile kept and merged in once it works.
   */
  private load(club: string): Promise<GuardState> {
    const held = this.states.get(club);

    if (held !== undefined && held.loaded) {
      return Promise.resolve(held);
    }

    let pending = this.loading.get(club);

    if (pending === undefined) {
      const generation = this.generations.get(club) ?? 0;

      pending = this.read(club).then((disk) => {
        const mine = this.loading.get(club) === pending;

        if (mine) {
          this.loading.delete(club);
        }

        // Deleted while this read was out: what it found is gone.
        if ((this.generations.get(club) ?? 0) !== generation) {
          return this.held(club);
        }

        const current = this.states.get(club);

        if (current !== undefined && current.loaded) {
          return current;
        }

        if (disk === undefined) {
          // Could not read it. Whatever is counted in memory carries on
          // deciding, and is not written over the file.
          return this.held(club);
        }

        if (current === undefined) {
          this.states.set(club, disk);
          return disk;
        }

        // Counted in memory while the file could not be read: both count.
        const merged = current.resetUnsaved ? { ...current, loaded: true, resetUnsaved: false } : merge(disk, current);

        this.states.set(club, merged);
        void this.save(club);
        return merged;
      });

      this.loading.set(club, pending);
    }

    return pending;
  }

  /** What the file says, or undefined when it could not be read this time. Never throws. */
  private async read(club: string): Promise<GuardState | undefined> {
    try {
      return clean((await this.store.getSnapshot(club, this.docName))?.body, this.clock());
    } catch (error) {
      if (error instanceof StoreReadError && error.corrupt) {
        // Read, but not JSON: that will not mend itself, and the next
        // wrong PIN writes a good one over it. Counted from none, as a
        // broken counter file always was - refusing every PIN because a
        // small file broke would lock the desk out for a reason nobody
        // could see.
        this.log(`[relay] "${club}": the wrong-PIN count was unreadable; starting from none.`);
        return fresh();
      }

      // Could not be read this time - which is not the same as nothing
      // counted, and must not be remembered as it: the file may hold a
      // lockout. Once a minute at most, for the same reason as below.
      const now = this.clock();

      if (now - this.lastReadError >= 60_000) {
        this.lastReadError = now;
        this.log(`[relay] "${club}": could not read the wrong-PIN count (${String(error)}); counting in memory and trying again.`);
      }

      return undefined;
    }
  }

  /**
   * Writes the club's state as it is when the write starts, one write at a
   * time per club, so an older state can never land on top of a newer one.
   *
   * A write that fails - a full disk, say - is logged and otherwise
   * ignored: the count in memory is still the one that decides, so the
   * limit holds for as long as the relay stays up, and failing the request
   * would turn a full disk into a PIN page that answers with errors.
   *
   * Nothing is written for a state that has not been read from disk (see
   * GuardState.loaded), nor for a club being deleted.
   */
  private save(club: string): Promise<void> {
    const previous = this.writes.get(club) ?? Promise.resolve();

    const run = previous.then(async () => {
      const state = this.states.get(club);

      if (state === undefined || !state.loaded || this.forgetting.has(club)) {
        return;
      }

      try {
        await this.store.putSnapshot(club, this.docName, {
          seq: 0,
          publishedAt: new Date(this.clock()).toISOString(),
          body: { fails: state.fails, level: state.level, lockedUntil: state.lockedUntil },
        });
      } catch (error) {
        // Once a minute at most: a full disk under a stream of guesses
        // would otherwise write a log line for every one of them.
        const now = this.clock();

        if (now - this.lastWriteError >= 60_000) {
          this.lastWriteError = now;
          this.log(`[relay] "${club}": could not save the wrong-PIN count (${String(error)}); it is kept in memory until the relay restarts.`);
        }
      }
    });

    this.writes.set(club, run);

    return run.finally(() => {
      if (this.writes.get(club) === run) {
        this.writes.delete(club);
      }
    });
  }
}

/** The file's count and the one kept in memory while it could not be read, together. */
function merge(disk: GuardState, memory: GuardState): GuardState {
  const fails = [...disk.fails, ...memory.fails].sort((a, b) => a - b).slice(-CLUB_FAIL_LIMIT);

  return {
    fails,
    level: Math.max(disk.level, memory.level),
    lockedUntil: Math.max(disk.lockedUntil, memory.lockedUntil),
    loaded: true,
    resetUnsaved: false,
  };
}

/** Nothing counted, as read from a file that says so (or has nothing in it). */
function fresh(): GuardState {
  return { fails: [], level: 0, lockedUntil: 0, loaded: true, resetUnsaved: false };
}

/**
 * The stored state, checked. Only the relay writes it, but it is read back
 * from disk - and a lockout end far in the future, from a broken file or a
 * clock that jumped, must not close a club's list for longer than any real
 * lockout could.
 */
function clean(value: unknown, now: number): GuardState {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return fresh();
  }

  const raw = value as Record<string, unknown>;
  const longest = LOCK_STEPS_MS[LOCK_STEPS_MS.length - 1] as number;

  const fails = Array.isArray(raw["fails"])
    ? (raw["fails"] as unknown[])
        .filter((t): t is number => typeof t === "number" && Number.isFinite(t) && t <= now)
        .slice(-CLUB_FAIL_LIMIT)
    : [];

  const level =
    typeof raw["level"] === "number" && Number.isInteger(raw["level"])
      ? Math.max(0, Math.min(raw["level"] as number, LOCK_STEPS_MS.length))
      : 0;

  const until = typeof raw["lockedUntil"] === "number" && Number.isFinite(raw["lockedUntil"]) ? (raw["lockedUntil"] as number) : 0;

  return { fails, level, lockedUntil: Math.max(0, Math.min(until, now + longest)), loaded: true, resetUnsaved: false };
}
