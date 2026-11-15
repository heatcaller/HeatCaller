/**
 * RACE VIDEO ON THE DRIVER'S PHONE (14 Nov 2026, plans/race-video-access-plan.md).
 *
 * The club streams to its own YouTube channel; YouTube holds all the video
 * and the relay none. The laptop sends one private document, `stream`:
 * each piece of tonight's stream (when it started and stopped on the
 * laptop's clock, its YouTube id, the club's nudge), the lead-in, whether
 * video shows on public results, and the races that are hidden or had a
 * junior in them. From that and a race's own result the relay works out the
 * race's moment in the video (V1) - never the phone, and never a list of
 * every race's video for anyone to read.
 *
 *  - t: seconds into the video = green flag - piece start - lead-in, plus
 *    the nudge, never below 0 (the green flag is the result's startedUtc,
 *    stamped by StartGreenNow; lap times count from it too).
 *  - end: the race's end - piece start + 10 s, plus the nudge.
 *  - laps: each flying lap's start in the video, from startMs and lapsMs.
 *
 * No video at all when the club has video off, the race isn't inside a
 * piece with a YouTube id (the stream dropped, or the link isn't in yet),
 * the piece or the race is hidden, or the video failed its check (V7). On
 * public results also when the club shows video on drivers' own nights
 * only, or a junior raced (a fixed rule, Asher: "juniors stay on their own
 * night").
 */

export const YOUTUBE_ID = /^[A-Za-z0-9_-]{11}$/;
const SID = /^[0-9a-f]{32}$/;

/** The most pieces one night may have. */
export const STREAM_MAX_PIECES = 10;

export interface StreamPiece {
  readonly videoId: string;
  readonly startMs: number;
  /** Undefined while the piece is still streaming. */
  readonly endMs: number | undefined;
  /** The club's nudge, seconds (V5). */
  readonly nudge: number;
  readonly hidden: boolean;
}

export interface Stream {
  readonly on: boolean;
  readonly publicResults: boolean;
  readonly leadIn: number;
  /** The club streams with YouTube's Ultra-low latency (V4): the video is a few seconds behind, not about 20. */
  readonly lowLatency: boolean;
  readonly pieces: readonly StreamPiece[];
  readonly hidden: ReadonlySet<string>;
  readonly juniors: ReadonlySet<string>;
  /** Videos the relay's check found private, removed or not embeddable (V7). */
  readonly dead: ReadonlySet<string>;
}

export interface RaceVideo {
  readonly id: string;
  readonly t: number;
  readonly end: number;
  /** Each flying lap's start, seconds into the video - for one driver's race. */
  readonly laps?: readonly number[];
  /** Which of laps is their best (index), when known. */
  readonly best?: number;
}

const num = (v: unknown, fallback: number) => (typeof v === "number" && Number.isFinite(v) ? v : fallback);
const ms = (v: unknown) => (typeof v === "string" ? Date.parse(v) : NaN);
const sids = (v: unknown) => new Set(Array.isArray(v) ? v.filter((s): s is string => typeof s === "string" && SID.test(s)) : []);

/** The laptop's stream document, checked; undefined when there is none worth reading. */
export function streamFrom(body: unknown, dead: ReadonlySet<string> = new Set(), maxPieces: number = STREAM_MAX_PIECES): Stream | undefined {
  if (typeof body !== "object" || body === null) return undefined;
  const b = body as Record<string, unknown>;
  const pieces: StreamPiece[] = [];

  for (const raw of Array.isArray(b["pieces"]) ? b["pieces"].slice(0, maxPieces) : []) {
    if (typeof raw !== "object" || raw === null) continue;
    const p = raw as Record<string, unknown>;
    const startMs = ms(p["startedUtc"]);
    const endMs = p["endedUtc"] === null || p["endedUtc"] === undefined ? undefined : ms(p["endedUtc"]);
    if (!Number.isFinite(startMs) || (endMs !== undefined && (!Number.isFinite(endMs) || endMs < startMs))) continue;
    const videoId = typeof p["videoId"] === "string" && YOUTUBE_ID.test(p["videoId"]) ? p["videoId"] : "";
    pieces.push({ videoId, startMs, endMs, nudge: Math.max(-600, Math.min(600, num(p["nudge"], 0))), hidden: p["hidden"] === true });
  }

  return {
    on: b["on"] === true,
    publicResults: b["publicResults"] !== false,
    leadIn: Math.max(0, Math.min(30, num(b["leadIn"], 10))),
    lowLatency: b["lowLatency"] === true,
    pieces,
    hidden: sids(b["hidden"]),
    juniors: sids(b["juniors"]),
    dead,
  };
}

/** The piece a race at greenMs..endMs sits wholly inside, with a usable video. */
function pieceFor(stream: Stream, greenMs: number, endMs: number): StreamPiece | undefined {
  return stream.pieces.find((p) => p.videoId !== "" && !p.hidden && !stream.dead.has(p.videoId)
    && greenMs >= p.startMs && (p.endMs === undefined || endMs <= p.endMs));
}

/**
 * A race's moment in the video, or undefined (see the rules above).
 * startMs and lapsMs are one driver's, for the lap list; leave them out for
 * the race alone. forPublic applies the public-results and junior rules.
 */
export function raceVideo(
  stream: Stream | undefined,
  race: { sid: string; startedUtc: unknown; endedUtc: unknown },
  forPublic: boolean,
  driver?: { startMs?: unknown; lapsMs?: unknown },
): RaceVideo | undefined {
  if (stream === undefined || !stream.on || !SID.test(race.sid) || stream.hidden.has(race.sid)) return undefined;
  if (forPublic && (!stream.publicResults || stream.juniors.has(race.sid))) return undefined;
  const green = ms(race.startedUtc);
  const ended = ms(race.endedUtc);
  if (!Number.isFinite(green)) return undefined;
  const endMs = Number.isFinite(ended) && ended >= green ? ended : green;
  const piece = pieceFor(stream, green, endMs);
  if (piece === undefined) return undefined;

  const at = (whenMs: number) => Math.max(0, Math.round(((whenMs - piece.startMs) / 1000 + piece.nudge) * 10) / 10);
  const video: { id: string; t: number; end: number; laps?: number[]; best?: number } = {
    id: piece.videoId,
    t: Math.max(0, at(green) - stream.leadIn),
    end: at(endMs) + 10,
  };

  const laps = driver && Array.isArray(driver.lapsMs) ? driver.lapsMs.filter((v): v is number => typeof v === "number" && v > 0) : [];
  const start = driver && typeof driver.startMs === "number" && driver.startMs >= 0 ? driver.startMs : undefined;

  if (laps.length > 0 && start !== undefined) {
    let from = green + start;
    video.laps = laps.map((lap) => { const t = at(from); from += lap; return t; });
    video.best = laps.indexOf(Math.min(...laps));
  }

  return video;
}

/** The piece streaming now, if any: open, with a usable video. The caller checks the laptop is still sending. */
export function liveVideo(stream: Stream | undefined): string | undefined {
  if (stream === undefined || !stream.on) return undefined;
  const open = stream.pieces.filter((p) => p.endMs === undefined && p.videoId !== "" && !p.hidden && !stream.dead.has(p.videoId));
  return open.length > 0 ? open[open.length - 1]!.videoId : undefined;
}

/**
 * YOUTUBE'S OWN ANSWER FOR A VIDEO (V7): the HTTP status of its public
 * oEmbed address for that video, or undefined when YouTube didn't answer.
 * Server to server, so no driver's phone tells Google anything.
 */
export type OEmbedAsk = (videoId: string) => Promise<number | undefined>;

/** The real ask: youtube.com/oembed, no key, five seconds at most. */
export const askYouTube: OEmbedAsk = async (videoId) => {
  try {
    const url = "https://www.youtube.com/oembed?format=json&url=" + encodeURIComponent("https://www.youtube.com/watch?v=" + videoId);
    const answer = await fetch(url, { signal: AbortSignal.timeout(5000), redirect: "manual" });
    await answer.body?.cancel().catch(() => {});
    return answer.status;
  } catch {
    return undefined;
  }
};

const DAY_MS = 24 * 60 * 60 * 1000;

/** Videos one club's checks have seen. */
const VIDEO_CHECK_MAX = 200;

/** How long a video is checked for: the night it arrives, then once a day for a week. */
export const VIDEO_CHECK_FOR_MS = 7 * DAY_MS;

/**
 * Makes room for a new video once a club's checks are full (bug hunt 19
 * #14): forgets the oldest video whose week of checks is over, one still
 * playing before one found dead, so a dead video stays dead the longest.
 * False when every video is still inside its week.
 */
function roomFor(mine: Map<string, { firstMs: number; dead: boolean; asking: boolean }>, now: number): boolean {
  let oldest: [string, { firstMs: number; dead: boolean }] | undefined;
  for (const entry of mine) {
    const [, v] = entry;
    if (v.asking || now - v.firstMs < VIDEO_CHECK_FOR_MS) continue;
    if (oldest === undefined || (oldest[1].dead && !v.dead) || (oldest[1].dead === v.dead && v.firstMs < oldest[1].firstMs)) oldest = entry;
  }
  if (oldest === undefined) return false;
  mine.delete(oldest[0]);
  return true;
}

/**
 * Checks each video the night it arrives, then once a day for a week (V7).
 * 401 (embedding turned off), 403 and 404 (private, removed) make it dead:
 * no race shows it. A 200 brings it back - a video still processing after
 * the stream, or made public again. YouTube not answering changes nothing.
 * A dead video goes on being asked about once a day after its week (bug
 * hunt 20), so one the club makes public later still comes back. Kept in
 * the club's "videocheck" snapshot across restarts (see VideoCheckKeep).
 */
export class VideoCheck {
  private readonly seen = new Map<string, Map<string, { firstMs: number; lastMs: number; dead: boolean; asking: boolean }>>();

  /** Clubs whose checks have been read back from the keep (see VideoCheckKeep). */
  private readonly loaded = new Map<string, Promise<void>>();

  constructor(private readonly ask: OEmbedAsk, private readonly now: () => Date, private readonly keep?: VideoCheckKeep) {}

  /**
   * Reads the club's checks back once after a restart (look-back, 14 Nov
   * 2026), so a deploy mid-night neither forgets a dead video nor asks
   * YouTube about every video again. Call before dead().
   */
  ready(club: string): Promise<void> {
    let p = this.loaded.get(club);
    if (p === undefined) {
      p = (async () => {
        let kept: Awaited<ReturnType<VideoCheckKeep["load"]>>;
        try {
          kept = this.keep === undefined ? undefined : await this.keep.load(club);
        } catch {
          // Not read (bug hunt 20): read again next time, and saw() saves
          // nothing meanwhile, so the kept checks are not written over.
          this.loaded.delete(club);
          return;
        }
        if (kept === undefined || this.seen.has(club)) return;
        const mine = new Map<string, { firstMs: number; lastMs: number; dead: boolean; asking: boolean }>();
        for (const k of kept.slice(0, VIDEO_CHECK_MAX)) {
          if (YOUTUBE_ID.test(k.id) && Number.isFinite(k.firstMs) && Number.isFinite(k.lastMs)) mine.set(k.id, { firstMs: k.firstMs, lastMs: k.lastMs, dead: k.dead === true, asking: false });
        }
        this.seen.set(club, mine);
      })();
      this.loaded.set(club, p);
    }
    return p;
  }

  /** The club's dead videos now. */
  dead(club: string): ReadonlySet<string> {
    const mine = this.seen.get(club);
    return new Set(mine ? [...mine].filter(([, v]) => v.dead).map(([id]) => id) : []);
  }

  /** Asks about any of these videos that is new, or due its daily check. Resolves when those asks are done. */
  async saw(club: string, ids: readonly string[]): Promise<void> {
    await this.ready(club);
    if (!this.loaded.has(club) && !this.seen.has(club)) return;
    let mine = this.seen.get(club);
    if (mine === undefined) {
      mine = new Map();
      this.seen.set(club, mine);
    }
    const now = this.now().getTime();
    const due: string[] = [];

    for (const id of new Set(ids)) {
      if (!YOUTUBE_ID.test(id)) continue;
      const known = mine.get(id);
      if (known === undefined) {
        if (mine.size >= VIDEO_CHECK_MAX && !roomFor(mine, now)) continue;
        mine.set(id, { firstMs: now, lastMs: now, dead: false, asking: true });
        due.push(id);
      } else if (!known.asking && now - known.lastMs >= DAY_MS && (known.dead || now - known.firstMs < VIDEO_CHECK_FOR_MS)) {
        known.lastMs = now;
        known.asking = true;
        due.push(id);
      }
    }

    await Promise.all(due.map(async (id) => {
      const status = await this.ask(id).catch(() => undefined);
      const v = mine.get(id);
      if (v === undefined) return;
      v.asking = false;
      if (status === 200) v.dead = false;
      else if (status === 401 || status === 403 || status === 404) v.dead = true;
    }));

    if (due.length > 0 && this.keep !== undefined) {
      await this.keep.save(club, [...mine].map(([id, v]) => ({ id, firstMs: v.firstMs, lastMs: v.lastMs, dead: v.dead }))).catch(() => {});
    }
  }
}

/** Where VideoCheck keeps what it learnt, per club, across restarts. */
export interface VideoCheckKeep {
  load(club: string): Promise<{ id: string; firstMs: number; lastMs: number; dead?: boolean }[] | undefined>;
  save(club: string, checks: { id: string; firstMs: number; lastMs: number; dead: boolean }[]): Promise<void>;
}

/** Nights of stream the relay keeps for a club: about eighteen months of weekly racing (VL7). */
export const STREAM_NIGHTS_KEPT = 120;

/** The races a club has hidden, remembered past the night (a sid -> true map), at most this many. */
export const STREAM_HIDDEN_KEPT = 3000;

/**
 * The name a night's stream is kept under: its first piece's start, so the
 * laptop's later sends of the same night write over it. Undefined for a
 * document with no piece, which names no night.
 */
export function streamNightId(body: unknown): string | undefined {
  const pieces = typeof body === "object" && body !== null && Array.isArray((body as Record<string, unknown>)["pieces"])
    ? ((body as Record<string, unknown>)["pieces"] as unknown[])
    : [];
  const starts = pieces.map((p) => (typeof p === "object" && p !== null ? Date.parse(String((p as Record<string, unknown>)["startedUtc"])) : NaN)).filter(Number.isFinite);
  return starts.length === 0 ? undefined : `n${Math.min(...starts)}`;
}

/**
 * EVERY NIGHT'S VIDEO, NOT ONLY TONIGHT'S (look-back, 14 Nov 2026). The laptop
 * forgets a night 20 hours on, and sends only tonight's stream; the relay keeps
 * each night it was sent. One stream to work races out of: the club's switches
 * and lead-in from the latest document, every night's pieces (the latest
 * night's own copy first), and every night's juniors. Hidden races are the
 * latest document's plus those the relay remembers (hidden).
 */
export function mergedStreamBody(latest: unknown, nights: readonly unknown[], hidden: readonly string[]): Record<string, unknown> | undefined {
  if (typeof latest !== "object" || latest === null) return undefined;
  const top = latest as Record<string, unknown>;
  const seen = new Set<number>();
  const pieces: unknown[] = [];
  const juniors = new Set<string>();
  const hid = new Set<string>(hidden);

  for (const doc of [latest, ...nights]) {
    if (typeof doc !== "object" || doc === null) continue;
    const d = doc as Record<string, unknown>;
    for (const p of Array.isArray(d["pieces"]) ? d["pieces"].slice(0, STREAM_MAX_PIECES) : []) {
      const at = typeof p === "object" && p !== null ? Date.parse(String((p as Record<string, unknown>)["startedUtc"])) : NaN;
      if (!Number.isFinite(at) || seen.has(at)) continue;
      seen.add(at);
      pieces.push(p);
    }
    for (const s of Array.isArray(d["juniors"]) ? d["juniors"] : []) if (typeof s === "string") juniors.add(s);
  }

  for (const s of Array.isArray(top["hidden"]) ? top["hidden"] : []) if (typeof s === "string") hid.add(s);
  return { ...top, pieces, juniors: [...juniors], hidden: [...hid] };
}

/**
 * The races hidden past the night: what was remembered, plus every race the
 * latest document hides, less any the laptop has just shown again (hidden in
 * the document before, and not now).
 */
export function hiddenAfter(remembered: readonly string[], before: unknown, after: unknown): string[] {
  const list = (doc: unknown) => (typeof doc === "object" && doc !== null && Array.isArray((doc as Record<string, unknown>)["hidden"])
    ? ((doc as Record<string, unknown>)["hidden"] as unknown[]).filter((s): s is string => typeof s === "string" && SID.test(s))
    : []);
  const now = new Set(list(after));
  // Only within one night: the laptop starts each night with no hidden
  // races, which is not the club showing last night's again.
  const sameNight = streamNightId(before) !== undefined && streamNightId(before) === streamNightId(after);
  const shownAgain = sameNight ? list(before).filter((s) => !now.has(s)) : [];
  const out = new Set(remembered.filter((s) => !shownAgain.includes(s)));
  for (const s of now) out.add(s);
  return [...out].slice(-STREAM_HIDDEN_KEPT);
}
