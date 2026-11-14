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
export function streamFrom(body: unknown, dead: ReadonlySet<string> = new Set()): Stream | undefined {
  if (typeof body !== "object" || body === null) return undefined;
  const b = body as Record<string, unknown>;
  const pieces: StreamPiece[] = [];

  for (const raw of Array.isArray(b["pieces"]) ? b["pieces"].slice(0, STREAM_MAX_PIECES) : []) {
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

/**
 * Checks each video the night it arrives, then once a day for a week (V7).
 * 401 (embedding turned off), 403 and 404 (private, removed) make it dead:
 * no race shows it. A 200 brings it back - a video still processing after
 * the stream, or made public again. YouTube not answering changes nothing.
 * Kept in memory: a relay restart checks again, which costs one ask a video.
 */
export class VideoCheck {
  private readonly seen = new Map<string, Map<string, { firstMs: number; lastMs: number; dead: boolean; asking: boolean }>>();

  constructor(private readonly ask: OEmbedAsk, private readonly now: () => Date) {}

  /** The club's dead videos now. */
  dead(club: string): ReadonlySet<string> {
    const mine = this.seen.get(club);
    return new Set(mine ? [...mine].filter(([, v]) => v.dead).map(([id]) => id) : []);
  }

  /** Asks about any of these videos that is new, or due its daily check. Resolves when those asks are done. */
  async saw(club: string, ids: readonly string[]): Promise<void> {
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
        if (mine.size >= VIDEO_CHECK_MAX) continue;
        mine.set(id, { firstMs: now, lastMs: now, dead: false, asking: true });
        due.push(id);
      } else if (!known.asking && now - known.lastMs >= DAY_MS && now - known.firstMs < 7 * DAY_MS) {
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
  }
}
