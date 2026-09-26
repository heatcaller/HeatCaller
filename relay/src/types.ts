/**
 * What a club can publish, and how the relay treats each kind.
 *
 * The relay does NOT understand any of these payloads. It stores what it
 * was given and hands it back. That is deliberate: the leaderboard columns
 * a club picks, the KPIs they add, the shape of a standings row are all
 * decisions made in the desktop app, and a relay that parsed them would
 * have to be redeployed every time one changed.
 */
export type ChannelKind = "snapshot" | "collection";

export interface ChannelSpec {
  readonly name: string;
  readonly kind: ChannelKind;
  /** One line, for the error a client sees when it names the wrong one. */
  readonly what: string;
  /**
   * Never served as-is to the public. The laptop publishes it and the
   * relay reads it, but GET /snapshot/<name> answers exactly as it does
   * for a channel that does not exist - whether a private document is
   * there at all is nobody's business.
   */
  readonly private?: boolean;

  /**
   * The most documents this collection keeps, past which the oldest are
   * forgotten as new ones arrive. Absent means no count limit: the club's
   * storage quota is the only ceiling.
   *
   * Results used to be capped at three hundred - ten or so race nights -
   * on the reasoning that the laptop keeps every result it ever took, so
   * the relay's copy could be a window rather than the record. That is
   * true of the laptop and useless to everybody else: a driver looking up
   * last season on their phone has no laptop. A session is one or two
   * kilobytes, so a decade of racing is a few megabytes against a fifty
   * megabyte quota - the cap was never what protected the disk, and the
   * quota still is.
   */
  readonly maxItems?: number;

  /**
   * This collection has a budget of its own and is not counted against
   * the club's storage quota - like the race-day manager's tunnel, and
   * for the same reason.
   *
   * The results index is not a club's DATA, it is a small note about
   * where the data is. Counted against the quota it became the thing that
   * made room for results: publishing this year's index at quota deleted
   * LAST year's, and a whole season went unreachable on a page while its
   * results sat there still taking up the space. Nothing about results
   * should be able to cost a club its index, and the index is three
   * orders of magnitude smaller than what it indexes.
   */
  readonly ownBudget?: number;

  /**
   * The largest one document in this collection may be, when the general
   * COLLECTION_ITEM_MAX_BYTES is the wrong ceiling for it.
   *
   * A year of a busy club - twenty heats a night, forty-odd nights -
   * passes 256 KB somewhere around nine hundred sessions, and the publish
   * is then refused with a 413 the laptop treats as "drop it". That year's
   * index would freeze at the last version that fitted while results kept
   * publishing perfectly, so nothing would look wrong; the archive would
   * just stop gaining race days, for good.
   */
  readonly maxItemBytes?: number;
}

/**
 * SNAPSHOT channels hold one current document - publishing replaces it.
 * COLLECTION channels hold many, keyed by an id the publisher chooses.
 *
 * Results have to be a collection. A race day produces one record per
 * session and they all matter; treating them as a snapshot would mean the
 * day's results were whichever heat finished last.
 */
export const CHANNELS: Readonly<Record<string, ChannelSpec>> = Object.freeze({
  state: { name: "state", kind: "snapshot", what: "the live race, as it stands" },
  roster: { name: "roster", kind: "snapshot", what: "who is entered and who has checked in" },
  programme: { name: "programme", kind: "snapshot", what: "the day's running order" },
  standings: { name: "standings", kind: "snapshot", what: "championship standings" },
  // No maxItems: kept until the club's quota says otherwise, so a club's
  // whole history stays readable rather than rolling off the back after a
  // few months. See the resultsindex channel, which is what makes a long
  // archive navigable without fetching all of it.
  results: { name: "results", kind: "collection", what: "one finished session's result" },
  /*
   * What is IN the results collection, so a page can navigate years of it
   * without fetching years of it.
   *
   * A collection keyed by calendar year. Each document holds that year's
   * sessions - id, day, time, type, label, track, how many raced - and a
   * map of drivers to the sessions they were in, which is everything the
   * results page needs to draw a season picker, page day by day, search a
   * date, and show one driver's history. Only when somebody opens a
   * session does the page fetch the result itself, by id.
   *
   * Calendar year, not championship season, because a session raced
   * outside a championship belongs to no season and must still be in the
   * index. A championship's name rides along on the session as a label.
   *
   * Why the LAPTOP builds this and not the relay: the relay does not know
   * what a result is, and this is the channel that keeps it that way. The
   * alternative was the relay parsing results to group them, and then
   * every column added in the desktop app would be a relay deploy.
   *
   * No maxItems, like results: one document a year is not a growth
   * problem, and losing 2024 to make room for 2027 would be absurd.
   */
  resultsindex: {
    name: "resultsindex",
    kind: "collection",
    what: "what is in the results collection, a year at a time",

    // Two megabytes of its own, off the club's quota: about eight years
    // at the per-document ceiling below, and in practice far more. Past
    // it the oldest year makes way, which is the one eviction that is
    // actually reasonable here.
    ownBudget: 2 * 1024 * 1024,
    maxItemBytes: 1024 * 1024,
  },
  // The badge catalogue, artwork included. Separate from who has earned
  // what because it is big and almost never changes, while the earned
  // list is small and changes after every race: one fetch of the art per
  // visit, not one per poll.
  badges: { name: "badges", kind: "snapshot", what: "the badge catalogue and its artwork" },
  achievements: { name: "achievements", kind: "snapshot", what: "who has earned which badge" },
  tracks: { name: "tracks", kind: "snapshot", what: "the club's track layouts, records and best laps" },
  // How the club's pages look: colours, a welcome line, a logo. The one
  // document the relay reads itself, because a theme applied in the
  // browser after the page has drawn is a page that flashes the wrong
  // colours on every visit. Read through site.ts, which accepts nothing
  // but hex colours, a short line of text and a PNG.
  site: { name: "site", kind: "snapshot", what: "the club's colours, welcome line and logo" },
  // Online check-in: who can be checked in, the PIN's hash, and the
  // drivers' names and photos. Private - the relay hands out pieces of
  // it to a PIN or a badge, never the whole thing.
  checkin: { name: "checkin", kind: "snapshot", what: "online check-in: the roster and its PIN", private: true },
  // The race-day manager's sign-in, over the relay: just whether it is
  // turned on and the manager PIN's hash. Nothing else lives here - the
  // manager's actual requests and their answers pass through the
  // separate manage/... routes in server.ts, never through a channel,
  // because the relay is not meant to hold a growing history of them the
  // way a channel would.
  manage: { name: "manage", kind: "snapshot", what: "whether the race-day manager is open over the relay, and its PIN", private: true },
  // Driver sign-up: whether new drivers can sign up online, the classes
  // they can pick, and the hash and expiry of every invite code still
  // usable - nothing else, no names and no email addresses (see
  // SignupStore.Document on the laptop). Private for the same reason as
  // checkin: the hashes are what a code is checked against, and handing
  // them out would let anyone try all hundred million codes at home. The
  // sign-ups themselves never pass through a channel either - they wait in
  // a document of the relay's own until the desk answers them, through the
  // signup/... routes in server.ts.
  signup: {
    name: "signup",
    kind: "snapshot",
    what: "driver sign-up: whether it is open, the classes, and the invite codes' hashes",
    private: true,
  },
});

export function channelSpec(name: string): ChannelSpec | undefined {
  return Object.prototype.hasOwnProperty.call(CHANNELS, name) ? CHANNELS[name] : undefined;
}

/** A stored document: the publisher's payload, plus what the relay knows about it. */
export interface StoredDocument {
  /**
   * The publisher's own counter.
   *
   * The laptop publishes over a network it does not control, and two
   * frames of live state can arrive out of order. A sequence number the
   * PUBLISHER owns is the only thing that can say which one is newer -
   * arrival time cannot, and neither can the relay's clock.
   */
  readonly seq: number;
  /** When the relay accepted it, ISO 8601. For "last published 3 minutes ago". */
  readonly publishedAt: string;
  readonly body: unknown;
}

export interface PublishResult {
  readonly applied: boolean;
  readonly seq: number;
}
