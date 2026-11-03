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
   * This collection's ids carry no order, so when it runs out of room the
   * OLDEST WRITTEN makes way - and its maxItems is honoured as well as
   * its byte budget.
   *
   * WHY THIS HAD TO BE SAID OUT LOUD (12 Oct 2026)
   *
   * `ownBudget` was written for exactly one channel, `resultsindex`, whose
   * ids are calendar years. Two things follow from that and neither
   * generalises:
   *
   *   it evicts by ID, oldest year first, and only ever considers ids
   *   LOWER than the one arriving - so a channel whose ids are not
   *   ordered would find nothing it was allowed to evict, and the
   *   INCOMING document would be refused instead. A rolling feed would
   *   simply stop accepting once it filled, with nothing anywhere saying
   *   so;
   *
   *   and it returns before `maxItems` is ever looked at, so a count
   *   limit on such a channel does nothing at all.
   *
   * Both are right for a year-keyed archive and wrong for anything that
   * rolls. This flag is how a channel says which of the two it is. See
   * Relay.planRoom.
   */
  readonly rollOldest?: boolean;

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
   * SNAPSHOTS TOO (22 Oct 2026, driver experience): a snapshot without
   * this may be as big as the relay's whole body limit, which is right for
   * the badge artwork and far too much for a drivers' live document or a
   * few lines of captions. Set on one, it is that snapshot's ceiling.
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
  badges: { name: "badges", kind: "snapshot", what: "the achievement catalogue and its artwork" },
  achievements: { name: "achievements", kind: "snapshot", what: "who has earned which achievement" },
  tracks: { name: "tracks", kind: "snapshot", what: "the club's track layouts, records and best laps" },
  // How the club's pages look: colours, a welcome line, a logo. The one
  // document the relay reads itself, because a theme applied in the
  // browser after the page has drawn is a page that flashes the wrong
  // colours on every visit. Read through site.ts, which accepts nothing
  // but hex colours, a short line of text and a PNG.
  /*
   * A preview of a meeting that HAS NOT HAPPENED YET - the date, what is
   * on, who is entered, and a reason to come.
   *
   * THE FIRST DOCUMENT HERE THAT GOES WRONG BY ITSELF. Everything else on
   * this relay is replaced by a newer version of itself: a result by a
   * corrected result, a roster by a fresh roster, a frame by the next
   * frame. A preview is the first thing that becomes false through the
   * passage of time alone - left alone it tells the public, in the future
   * tense, about a meeting that finished hours ago.
   *
   * So it comes down two ways, and it needs both:
   *
   *   the laptop withdraws it when the meeting starts, by publishing an
   *   empty one (NightBefore.Withdrawn) rather than dropping the channel -
   *   dropping leaves the last thing sent standing for good, because
   *   there is nothing newer to replace it with;
   *
   *   and the document carries its own expiry, which the PAGE honours.
   *   That is the one that matters, because the case it covers is the
   *   club's laptop never coming back - packed up, switched off, taken
   *   home - which is exactly when a preview is at its most stale and the
   *   one case the laptop cannot fix.
   *
   * The relay does not read either. It does not know what a preview is,
   * when it expires or whether it is empty, and that is deliberate - the
   * rule this file opens with.
   */
  preview: { name: "preview", kind: "snapshot", what: "a preview of a meeting that has not happened yet" },
  /*
   * ONE DRIVER'S NIGHT - what they did at the club tonight, as finished
   * sentences, one document per driver.
   *
   * PRIVATE, AND THE FIRST PRIVATE COLLECTION THIS RELAY HAS HAD. Until
   * now every private channel was a snapshot, and only readSnapshot
   * honoured the flag - readCollection and readItem did not check it at
   * all. Adding this channel without fixing that would have served every
   * driver's night to anybody, and `?meta=1` would have listed every
   * document id. These ids are badge ids (RelayCheckIn.IdFor): eighty
   * unguessable bits that are ALSO what lets somebody be checked in, so a
   * listing of them would have given away check-in with the nights. Both
   * readers now refuse a private collection, and readSomeItems refuses
   * one too.
   *
   * So a night is read through its own route and only by somebody who
   * already has the id - "the id itself is the credential", the rule this
   * relay's check-in was built on.
   *
   * ROLLOLDEST, because the ids are hex and carry no order: an ownBudget
   * channel evicts the lowest id first and only ever considers ids below
   * the one arriving, so without this a full budget would start REFUSING
   * incoming nights rather than making room. That is the trap the listen
   * channel's own entry records.
   */
  nights: {
    name: "nights",
    kind: "collection",
    what: "one driver's own night, for the driver whose badge id it is",
    private: true,
    ownBudget: 4 * 1024 * 1024,
    maxItems: 400,
    maxItemBytes: 64 * 1024,
    rollOldest: true,
  },
  site: { name: "site", kind: "snapshot", what: "the club's colors, welcome line and logo" },
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
  /*
   * The room's last couple of minutes, as audio, so somebody at home can
   * listen along with it.
   *
   * NOT AN ARCHIVE. Since 2 Nov 2026 the laptop sends the room's whole mix -
   * the announcer, the music, the crowd, the imaging - as one mp3 stream cut
   * into documents of about three seconds, and the club's page joins them
   * back together a few seconds behind (LISTEN_JS in pages.ts). A laptop
   * that cannot stream sends the announcer's lines one document each, as
   * it always did, and the page plays those too. Either way it is the same
   * at-least-once shape every other inbox on this relay has: the relay
   * stores documents and never touches the audio inside them.
   *
   * ROLLING, AND THAT IS THE FEATURE RATHER THAN A LIMIT. Only the last
   * `maxItems` lines are kept. Somebody who opens the page mid-evening
   * joins the evening where it is, exactly as they would walking into the
   * hall; they do not get the last two hours to catch up on. It also
   * means a night cannot grow without bound on a volume somebody pays
   * for.
   *
   * A BUDGET OF ITS OWN, AND THIS IS THE PART THAT MATTERS. Audio is
   * thousands of times the size of everything else this relay holds: a
   * night of talking is tens of megabytes against a club's whole 50MB
   * quota. Counted against that quota it would push a club's SAVED
   * RESULTS off the relay to make room for last Tuesday's commentary -
   * a feature for the family at home costing the club its race archive.
   * So it is off-quota, held to its own ceiling, and cannot evict
   * anything but itself.
   *
   * Public, because that is the whole point: the page that plays it is
   * the one the club already sends people.
   */
  listen: {
    name: "listen",
    kind: "collection",
    what: "the room's last couple of minutes, as audio, for listening along",

    // About eight megabytes and forty documents, whichever runs out first.
    // Forty three-second stretches of the stream is two minutes (about two
    // megabytes); forty lines is three or four minutes of an announcer
    // talking. Either is more than enough to be "a few seconds behind".
    ownBudget: 8 * 1024 * 1024,
    maxItems: 40,
    maxItemBytes: 256 * 1024,

    // Its ids are a running count, not years - see ChannelSpec.rollOldest
    // for what assuming otherwise would have cost.
    rollOldest: true,
  },
  /*
   * EACH DRIVER'S LIVE EVENING (22 Oct 2026, driver experience): their
   * next race and how far away it is, whether they are staged, their
   * marshal duty, when their transponder was last seen, whether they are
   * checked in, and the desk's answers to what they asked - keyed by badge
   * id, for the night page to poll.
   *
   * PRIVATE, read only through readOneNight by somebody holding that
   * driver's badge id, exactly as the nights are. A snapshot rather than a
   * collection because it is one document the laptop rewrites every few
   * seconds, not one per driver; the relay hands out one driver's entry
   * and never the whole thing. Switched by the nights switch, and taken
   * down when it closes. Read as stale - and so not served - once it is
   * half an hour old.
   */
  driverlive: {
    name: "driverlive",
    kind: "snapshot",
    what: "each driver's live evening, for the driver whose badge id it is",
    private: true,
    maxItemBytes: 256 * 1024,
  },
  /*
   * WHAT EACH DRIVER CAN ASK THE DESK TO CHANGE (22 Oct 2026, driver
   * experience): their announcer name, pronouns, about line, a masked
   * email, the privacy ticks and their battery packs to choose from -
   * keyed by badge id. Private as driverlive is, switched by the club's
   * driver-requests switch (driver/open), and taken down when that closes.
   */
  driverme: {
    name: "driverme",
    kind: "snapshot",
    what: "each driver's own details, for the driver whose badge id it is",
    private: true,
    maxItemBytes: 512 * 1024,
  },
  /*
   * THE ANNOUNCER'S LAST FEW LINES, AS TEXT (22 Oct 2026, driver
   * experience), for a captions view on the live page - for somebody who
   * cannot hear the PA, or is somewhere they cannot play sound. Public,
   * like the live race; the laptop redacts drivers who asked for initials
   * only before it sends, and publishes an empty document when the club
   * switches captions off.
   */
  captions: {
    name: "captions",
    kind: "snapshot",
    what: "the announcer's last few lines, as captions",
    maxItemBytes: 16 * 1024,
  },
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
