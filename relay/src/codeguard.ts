import { createHash, createHmac } from "node:crypto";
import { PinGuard, type GuardOptions } from "./pinguard.js";
import type { Store } from "./store.js";

/**
 * WRONG-GUESS LIMITS FOR CODES (23 Nov 2026, Asher, security program
 * section 5): "family-link and crew codes get a wrong-guess limit of their
 * own". Until now a wrong family-link code and a wrong badge code on a crew
 * pick were answered "Not found" as often as anybody liked to ask, held
 * back only by the per-address traffic limits - which a guesser beats by
 * spreading the asking over many addresses, as the PIN guards learned.
 *
 * Both codes are long random strings - 96 bits for a family link, 80 for a
 * badge - so nobody will guess one by typing. The limit is there so the
 * relay notices and stops somebody trying, as it does for PINs, and so the
 * security program can say every code a phone sends is limited. That is
 * why the numbers are set by what a false lockout would cost a club, not
 * by how many guesses a code can stand: see FAMILY_CODE_LIMITS and
 * CREW_CODE_LIMITS.
 *
 * Built on PinGuard: the same club-wide count from every address, kept on
 * disk, the same growing lockout (10 minutes, 30, 2 hours, 6, then 12),
 * the same daily ceiling for a paced guesser, and forgotten by Delete
 * Everything Published. Beside it, as for PINs, a per-address count in
 * memory, so one connection is stopped long before the club is.
 *
 * Two things PINs do not need:
 *
 * - A wrong code is counted once per club per day, however often it comes.
 *   A family page polls every thirty seconds, so one relative's tab left
 *   open on a link the driver has stopped - or a link from last season -
 *   would otherwise close every family's links within an hour. A guesser
 *   gains nothing from this: each new guess is a new code.
 *
 * - Phones already holding a working code keep working through a lockout.
 *   A family page is answered with a pass (familyPass) the first time its
 *   code works, and sends it back on every poll; a crew pick from a driver
 *   signed in with their personal PIN carries that PIN's pass. Both are
 *   256-bit keyed hashes only this relay can make, so they cannot be used
 *   to test codes during a lockout. A code sent WITHOUT such a pass is
 *   refused during a lockout whether it is right or wrong, with the same
 *   words, so the lockout tells a guesser nothing either.
 */

/** Ten minutes - the per-address window, as for wrong PINs. */
export const ADDRESS_FAIL_WINDOW_MS = 10 * 60 * 1000;

/** How long a wrong code is remembered, so it is counted once: a day, the longest any guard counts. */
export const SEEN_WRONG_MS = 24 * 60 * 60 * 1000;

/** The most wrong codes remembered per club. Past it the oldest is forgotten - and so may be counted again, which only ever errs towards closing. */
export const SEEN_WRONG_MAX = 2000;

export interface CodeLimits {
  /** Different wrong codes, from every address together, in ten minutes before the club closes. */
  readonly club: number;
  /** Different wrong codes, from every address together, in a day - the paced guesser's ceiling. */
  readonly day: number;
  /** Different wrong codes from one address in ten minutes. */
  readonly address: number;
}

/**
 * Family-link codes: 24 hex characters, 96 random bits each.
 *
 * Honest wrong codes are links a driver has stopped, links of a driver the
 * club has forgotten, and links copied with a piece missing. Counted once
 * each, a club night has a handful - a busy one with a hundred relatives
 * following might see a dozen. So twice the PIN numbers: 100 in ten
 * minutes and 300 in a day across the club, 20 in ten minutes from one
 * address (a venue's Wi-Fi is one address for everybody on it). Even the
 * 300 a day, every day, is nothing against 96 bits: with ten links per
 * driver and two hundred drivers, a guesser would need about 10^22 years.
 */
export const FAMILY_CODE_LIMITS: CodeLimits = Object.freeze({ club: 100, day: 300, address: 20 });

/**
 * The badge code a crew pick is sent with: 20 hex characters, an 80-bit
 * keyed hash. A crew pick is sent by hand, a few times a night, by drivers
 * on the club's own list, so an honest wrong one is rare - a driver the club
 * has since forgotten, still holding their old page. The PIN numbers: 50
 * in ten minutes and 150 in a day across the club, 10 from one address.
 */
export const CREW_CODE_LIMITS: CodeLimits = Object.freeze({ club: 50, day: 150, address: 10 });

/** How long until it opens, in plain words - the way the PIN lockout says it. */
export function waitWords(lockedUntil: number, now: number): string {
  const minutes = Math.max(1, Math.ceil((lockedUntil - now) / 60000));

  return minutes <= 90 ? `${minutes} minute${minutes === 1 ? "" : "s"}` : `${Math.round(minutes / 60)} hours`;
}

/**
 * The pass a family page is given the first time its code works, and
 * sends back on every poll - what keeps it working through a lockout. A
 * keyed hash of the club and code, with the club's pass key (the one PIN
 * passes use), so nobody without that key can make one for a code they
 * mean to try. It needs no storing: the link existing is checked beside it,
 * so a stopped link's pass opens nothing.
 */
export function familyPass(key: Buffer, club: string, code: string): string {
  return createHmac("sha256", key).update(`hg-familypass|${club}|${code}`, "utf8").digest("hex");
}

export interface CodeRefusal {
  readonly status: 429;
  readonly error: string;
}

export class CodeGuard {
  private readonly guard: PinGuard;
  /** club|address -> when each wrong code from it arrived, oldest first. */
  private readonly addressFails = new Map<string, number[]>();
  /** club -> hash of each wrong code seen -> when, oldest first. */
  private readonly seen = new Map<string, Map<string, number>>();

  constructor(
    store: Store,
    private readonly clock: () => number,
    log: (line: string) => void,
    /** The guard's document - its own, apart from every PIN guard's. */
    docName: string,
    private readonly limits: CodeLimits,
    /** Plain words: the club's lockout, given how long it has to go ("12 minutes"). */
    private readonly clubWords: (wait: string) => string,
    /** Plain words: one address has tried too many. */
    private readonly addressWords: string,
    /** For the log lines, e.g. "family-link codes" and "family links". */
    names: { readonly wrongWhat: string; readonly closedWhat: string },
  ) {
    const options: GuardOptions = {
      clubFailLimit: limits.club,
      dayFailLimit: limits.day,
      wrongWhat: names.wrongWhat,
      closedWhat: names.closedWhat,
    };

    // reset is never called on these - a code is not changed the way a
    // staff PIN is - so resetSubject is never said.
    this.guard = new PinGuard(store, clock, log, docName, names.wrongWhat, names.wrongWhat, options);
  }

  /** Reads the club's count from disk if it has not been yet. Never throws. Await it before refusalNow. */
  ready(club: string): Promise<void> {
    return this.guard.ready(club);
  }

  /** When the club's lockout ends, or 0 - for tests and the log. */
  lockedUntil(club: string): Promise<number> {
    return this.guard.lockedUntil(club);
  }

  /**
   * The answer for a code sent without a pass while this club or this
   * address is locked out, or undefined when it is open. From memory, with
   * no await, so a route can call it, judge the code and call wrongNow in
   * one step (see PinGuard on bursts).
   */
  refusalNow(club: string, address: string): CodeRefusal | undefined {
    const lockedUntil = this.guard.lockedUntilNow(club);

    if (lockedUntil > 0) {
      return { status: 429, error: this.clubWords(waitWords(lockedUntil, this.clock())) };
    }

    if (this.recentFrom(`${club}|${address}`).length >= this.limits.address) {
      return { status: 429, error: this.addressWords };
    }

    return undefined;
  }

  /**
   * Counts one wrong code - unless this club has seen this same code in
   * the last day. Counted in memory before this returns; the promise is
   * the save to disk, and never rejects.
   */
  wrongNow(club: string, address: string, code: string): Promise<void> {
    const now = this.clock();
    const hash = createHash("sha256").update(code, "utf8").digest("hex").slice(0, 32);
    let seen = this.seen.get(club);

    if (seen === undefined) {
      seen = new Map();
      this.seen.set(club, seen);
    }

    for (const [old, at] of seen) {
      if (at > now - SEEN_WRONG_MS) {
        break;
      }

      seen.delete(old);
    }

    const before = seen.get(hash);

    if (before !== undefined && before > now - SEEN_WRONG_MS) {
      return Promise.resolve();
    }

    seen.delete(hash);
    seen.set(hash, now);

    while (seen.size > SEEN_WRONG_MAX) {
      const oldest = seen.keys().next().value as string;
      seen.delete(oldest);
    }

    const key = `${club}|${address}`;
    this.addressFails.set(key, [...this.recentFrom(key), now]);

    // A bound on addresses held, as Limiter keeps one: the stalest go first.
    if (this.addressFails.size > 10000) {
      const stalest = this.addressFails.keys().next().value as string;
      this.addressFails.delete(stalest);
    }

    return this.guard.failNow(club);
  }

  /**
   * For a club being deleted (Delete Everything Published): the count on
   * disk goes with the club's files, in the order PinGuard.forget keeps,
   * and what is held in memory - the per-address counts and the wrong
   * codes seen - goes too.
   */
  async forget(club: string, wipe: () => Promise<void>): Promise<void> {
    try {
      await this.guard.forget(club, wipe);
    } finally {
      this.seen.delete(club);

      for (const key of [...this.addressFails.keys()]) {
        if (key.startsWith(`${club}|`)) {
          this.addressFails.delete(key);
        }
      }
    }
  }

  private recentFrom(key: string): number[] {
    const cutoff = this.clock() - ADDRESS_FAIL_WINDOW_MS;
    const kept = (this.addressFails.get(key) ?? []).filter((t) => t > cutoff);

    if (kept.length === 0) {
      this.addressFails.delete(key);
    } else {
      this.addressFails.delete(key);
      this.addressFails.set(key, kept);
    }

    return kept;
  }
}
