import { createHash, createHmac, pbkdf2Sync, randomBytes, timingSafeEqual } from "node:crypto";
import { createServer, type IncomingMessage, type Server, type ServerResponse } from "node:http";
import { isIP } from "node:net";
import { isValidItemId, StoreReadError, type Store } from "./store.js";
import { AUTO_WHEN_USED } from "./autotopup.js";
import { isActive, isValidClubId, type Tenant, type TenantRegistry } from "./tenancy.js";
import { CHANNELS, channelSpec, type ChannelSpec, type StoredDocument } from "./types.js";
import { cardVersionOf, clubCard, sessionCard } from "./card.js";
import { PinGuard } from "./pinguard.js";
import { previewCard, previewsIn, type PagePreview, type SessionPreview } from "./preview.js";
import {
  badgesPage,
  checkinPage,
  homePage,
  livePage,
  appShellPage,
  managePage,
  manifest,
  missingPage,
  nightPage,
  pausedPage,
  relayNoticePage,
  resultsPage,
  schedulePage,
  signupPage,
  standingsPage,
  tracksPage,
  myNightPage,
  type PageContext,
} from "./pages.js";
import {
  CLIP_DRIVER_ID,
  CLIP_ID,
  CLIP_LABEL_MAX,
  CLIP_MAX_BYTES,
  CLIP_RACE_ID,
  clipsToDrop,
  looksLikeMp3,
  type ClipMeta,
  type ClipVault,
} from "./clips.js";
import {
  askFieldsFrom,
  askRefOf,
  asDriverAsk,
  DRIVER_ASK_KINDS,
  DRIVER_ASK_MAX_BODY,
  DRIVER_ASK_MAX_STORED,
  DRIVER_ASKS_CHANNEL,
  DRIVER_ASKS_COLLECT_MAX_BODY,
  DRIVER_ASKS_OPEN_DOC,
  DRIVER_ASKS_PER_CLUB,
  DRIVER_ASKS_PER_DRIVER,
  DRIVER_ASK_SETUP_MAX_BODY,
  DRIVER_WRITES_PER_DRIVER,
  isWriteKind,
  type DriverAskBody,
} from "./driverasks.js";
import {
  asDriverPhoto,
  DRIVER_PHOTO_MAX_BODY,
  DRIVER_PHOTOS_CHANNEL,
  DRIVER_PHOTOS_COLLECT_MAX_BODY,
  DRIVER_PHOTOS_PER_CLUB,
  DRIVER_PHOTOS_PER_DRIVER,
  GARAGE_CHANNEL,
  photoBytesOf,
  type DriverPhotoBody,
} from "./garage.js";
import { garagePage } from "./garagepage.js";
import { DEFAULT_LOOK, lookFrom, type Look } from "./site.js";
import { sunIcon } from "./sun.js";
import { BRAIN_LONG_WORDS_CEILING, BRAIN_PLAN_OFF, type BrainService } from "./brain.js";
import { LapVoiceLibrary } from "./lapvoice.js";
import { VOICE_PLAN_OFF, type MeterUsage, type VoiceService } from "./voice.js";
import { isMeterName, type TopUpRequests } from "./topuprequests.js";
import type { UsageLog } from "./usagelog.js";
import { PUSH_CHANNEL, pushSubFrom, SERVICE_WORKER_JS, type PushService } from "./push.js";

/** The public pages under /c/<club>/, by their last path segment. */
const PAGES: Readonly<Record<string, (club: string, clubName: string, context: PageContext) => string>> = Object.freeze({
  live: livePage,
  schedule: schedulePage,
  results: resultsPage,
  standings: standingsPage,
  badges: badgesPage,
  tracks: tracksPage,
  checkin: checkinPage,
  // A driver's way to their own night with their personal PIN (26 Oct 2026, asked for by Asher) - see myNightPage.
  mynight: myNightPage,
});

/** Where the relay keeps a club's queue of check-in taps. Not a channel: nobody publishes to it, and nobody reads it but the laptop. */
const INBOX = "inbox";

/** The channel a session's preview card is built from - never the results themselves. See preview.ts. */
const PREVIEW_CHANNEL = "resultsindex";

/**
 * How far back a shared link can carry its own card, and how much of a
 * club is held in memory to do it.
 *
 * A link being shared is nearly always a link to racing that has just
 * happened, so three years is generous. Past either of these a link still
 * opens the right session - the page fetches what it needs itself - it
 * simply arrives with the club's generic card rather than its own.
 */
const PREVIEW_YEARS = 3;
const PREVIEW_MAX_SESSIONS = 3000;

/**
 * The most every club's previews held together may come to, in bytes, as
 * previewBytes estimates them.
 *
 * One club's is at most PREVIEW_MAX_SESSIONS sessions of a few hundred
 * bytes each - a megabyte or two at its very largest - but it is held for
 * every club a crawler has asked about since the last restart, and fifty
 * clubs with long histories must not add up to a real part of a 256 MB
 * machine. Past this the club asked about longest ago goes, exactly as
 * the listings cache does it; its next link reads its index again.
 */
const PREVIEW_CACHE_MAX_TOTAL = 16 * 1024 * 1024;

/**
 * Drawn result cards kept ready to send (card.ts).
 *
 * A card is the most expensive thing this relay can be asked to make:
 * three quarters of a million pixels, every one of them written at least
 * once, then deflated. It is also the most re-asked-for, because every
 * person a link is forwarded to is another chat app fetching it. So a
 * card is drawn once per club-and-session and held until the club
 * publishes its index again or changes its colours - the same rule the
 * previews themselves follow, and for the same reason.
 *
 * Bounded in bytes, not in cards: a hundred-odd kilobytes each on a
 * 256 MB machine, and a relay with fifty clubs must not hold all of
 * them. Past the bound the oldest go, and being asked for one again
 * simply draws it again.
 */
const CARD_CACHE_MAX_TOTAL = 8 * 1024 * 1024;

/**
 * How many cards one address may make this relay DRAW in a minute, and
 * how many it will draw for everybody put together.
 *
 * Cached cards are free and are not counted; only a miss is. The first
 * limit is for one client walking a club's session ids, the second is
 * the one that matters: at roughly twenty-five milliseconds a card, four
 * hundred a minute is about a sixth of one core, and there is no way to
 * make this relay spend more than that on drawing however many crawlers
 * arrive at once. Past either, the club's own generic card goes out
 * instead - already drawn, already held, and a perfectly good picture -
 * rather than an error a chat app would draw as a broken image and then
 * remember.
 *
 * There is no slot to take, unlike a listing build, because there is
 * nothing to take one against: drawing is arithmetic with no await in
 * it, so a card finishes before the next request is looked at and the
 * three megabytes it needs are never wanted twice at once. What has to
 * be bounded here is TIME, and that is what these two do.
 */
const CARDS_PER_ADDRESS = 60;

/** What a sign-in is told when SLOW_HASHES_PER_ADDRESS or SLOW_HASHES_IN_ALL is used up. Not a wrong PIN, and not counted as one. */
const SLOW_HASH_BUSY = "Too many sign-ins right now. Wait a minute and try again.";
const CARDS_IN_ALL = 400;

/** A tap older than this is from another day, and applying it now would be wrong rather than late. */
const INBOX_MAX_AGE_MS = 12 * 60 * 60 * 1000;

/** A ceiling, not a working limit - a whole club checking in twice over is a few hundred. */
const INBOX_MAX_ITEMS = 500;

/** Check-in requests are a few dozen bytes. */
const CHECKIN_MAX_BODY = 4 * 1024;

/** Where the relay keeps a club's race-day-manager tunnel: requests waiting for the laptop, and answers waiting for a phone. Not a channel, for the same reason INBOX is not one. */
/**
 * WHAT PHONES SAW OF EACH SPONSOR (5 Nov 2026): the Live page's sponsors
 * card counts a view and a tap (SPONSORS_JS in pages.ts), and they are kept
 * here by the hour, {hours: {"2026-11-05T22": {id: [seen, taps]}}}, for the
 * laptop's sponsor report. Not a channel: never served to the public. Only
 * ids in the published sponsors document are counted, so it cannot be used
 * to write anything else into a club's folder.
 */
const SPONSOR_VIEWS = "sponsorviews";

/** How long the hourly counts are kept: a season's worth of nights is the laptop's to keep. */
const SPONSOR_VIEWS_DAYS = 45;

/** A phone's report of what it saw is a short list of ids. */
const SPONSOR_SEEN_MAX_BODY = 2048;

const MANAGE_QUEUE = "managequeue";

/**
 * A phone's manage/signin, manage/request or manage/response body: a PIN,
 * a pass, an id, and - for request - one op's op/args/confirm. Sized for
 * the same request RaceDayService.HandleJsonAsync itself accepts
 * (RaceDayService.MaxRequestChars, 64 KiB) plus room for the pass and id
 * that travel alongside it here.
 */
const MANAGE_MAX_BODY = 68 * 1024;

/**
 * A queued request or a collected answer older than this is stale enough
 * that neither side is still waiting on it: RaceDayService itself gives up
 * on a phone call within about a minute (AnswerTimeout plus its one
 * extension), and this is generous well past that for a laptop whose own
 * poll of manage/inbox is slow or has just reconnected.
 */
const MANAGE_QUEUE_MAX_AGE_MS = 10 * 60 * 1000;

/**
 * A ceiling on how many pending requests, or collected answers, one club's
 * tunnel may hold at once. Generous for how this is actually used - one
 * manager, one action at a time - and here only so a client (or a laptop
 * that has stopped polling) cannot grow the document without bound.
 */
const MANAGE_QUEUE_MAX_ITEMS = 300;

/** A reqId is the phone's own idempotency key for one request/answer pair - not a store id, so store.ts's channel-name rule does not apply to it. */
const MANAGE_REQ_ID = /^[A-Za-z0-9-]{8,64}$/;

/**
 * Where the relay keeps a club's driver sign-ups: what each new driver
 * typed, waiting for the desk, and then the desk's answer, waiting for
 * their phone. Not a channel, for the same reason INBOX and MANAGE_QUEUE
 * are not: nobody publishes to it, and nobody reads it whole but the
 * laptop, through signup/inbox. One entry per invite, keyed by the
 * invite's hash - a phone that sends its sign-up again replaces it.
 */
const SIGNUP_BOX = "signupbox";

/**
 * A phone's signup/signin, signup/status or signup/submit body: a code, a
 * pass, and for submit one form of short fields - the longest is an email
 * address. The same few kilobytes check-in allows.
 */
const SIGNUP_MAX_BODY = 4 * 1024;

/**
 * Shout-outs and dedications the club's own page takes in, for the
 * announcer - the room's way into the broadcast from outside the room.
 *
 * WHY THE RELAY TAKES THESE AT ALL
 *
 * HeatCaller's own web server already does, on the club Wi-Fi, and that
 * covers everybody standing in the building. This covers the ones who are
 * not: a driver's family at home, somebody who could not make it, the
 * member who wants their mate's first race mentioned. That is a different
 * and larger group of people, and it is the half of the feature a club
 * would notice.
 *
 * EVERY ONE OF THEM IS UNAPPROVED. The relay holds a sentence until the
 * laptop collects it, and the laptop files it as something the race
 * director has to tick off before the announcer will say it (ClubNotes).
 * Nothing on this path can reach a PA on its own, and nothing about it is
 * trusted: not the message, not the name, not the sender.
 */
const NOTES_CHANNEL = "notes";

/** The flag the laptop sets to say its club wants these at all - see notesOpenFor. */
const NOTES_OPEN_DOC = "notesopen";

/**
 * Whether this club is letting people listen along from home.
 *
 * Off-channel, like the notes and the vote switches, and for the same
 * reason: it is a setting rather than something a page reads, and a
 * channel would make it publishable by name and keep a history of it.
 */
const LISTEN_OPEN_DOC = "listenopen";

/** The flag the laptop sets to say its club offers drivers their own night - see nightsOpenFor. */
const NIGHTS_OPEN_DOC = "nightsopen";

/** The channel one driver's night lives in - see CHANNELS.nights. */
const NIGHTS_CHANNEL = "nights";

/**
 * Car photos read at once, across every club - each up to
 * DRIVER_PHOTO_MAX_BODY held in memory while it arrives. A few drivers at
 * one moment is plenty; the rest are told to try again in a minute.
 */
const PHOTO_UPLOADS_IN_FLIGHT_MAX = 6;

/** The collection the announcer's lines arrive in - see CHANNELS.listen. */
const LISTEN_CHANNEL = "listen";

/** Each driver's live evening - see CHANNELS.driverlive. Switched by the nights switch. (22 Oct 2026, driver experience) */
const DRIVER_LIVE_CHANNEL = "driverlive";

/** Each driver's own details - see CHANNELS.driverme. Switched by the driver-requests switch. (22 Oct 2026, driver experience) */
const DRIVER_ME_CHANNEL = "driverme";

/**
 * How old the live document may be and still be served (22 Oct 2026,
 * driver experience). The laptop sends it at least once a minute while it
 * runs (its "at" moves on the minute), so half an hour without one is a
 * laptop that has gone home - and "your race is next" from then is a lie.
 *
 * Judged by when THIS relay took it, not by the "at" inside it: a laptop
 * whose clock is an hour out would otherwise never be fresh, or never
 * stale.
 */
const DRIVER_LIVE_FRESH_MS = 30 * 60 * 1000;

/**
 * The channels a club has to switch ON before this relay will take a
 * document for one, and the plain refusal for one that is off.
 *
 * WHY A PUBLISH IS REFUSED AND NOT JUST A READ (13 Oct 2026). Both of
 * these channels hold something about a named person - the club's own
 * announcer going out to anybody with the address, and a driver's evening
 * under their badge id - and both switches DELETE what is held when they
 * are turned off, because a club that has just declined should not have
 * that sitting on a relay. A publish the relay accepted anyway would undo
 * that a second later: the close empties the folder, the next pass of a
 * laptop with a stale switch fills it again, and the club's "no" means
 * nothing. The nights channel had exactly that hole until this existed -
 * the read was gated and the write was not.
 *
 * Refused HERE rather than trusted to the laptop, for the reason the
 * listen gate already gave: a switch on one machine is not a guarantee.
 * An older build, a restored backup, or a second laptop somebody set up
 * with the same token all send without knowing about it.
 *
 * A channel that is not in here has no switch and is always taken.
 */
const SWITCHED_CHANNELS: ReadonlyMap<string, string> = new Map([
  [LISTEN_CHANNEL, "This club is not letting people listen along, so the announcer's lines are not accepted."],
  [NIGHTS_CHANNEL, "This club is not offering drivers their own night, so those are not accepted."],
  // 22 Oct 2026, driver experience: the drivers' live evening goes with
  // their nights, and their own details with the driver-requests switch.
  [DRIVER_LIVE_CHANNEL, "This club is not offering drivers their own night, so their live evening is not accepted."],
  [DRIVER_ME_CHANNEL, "This club is not taking drivers' own requests, so their details are not accepted."],
  // 5 Nov 2026: each driver's garage - packs, cars, sheets - goes with
  // their details.
  [GARAGE_CHANNEL, "This club is not taking drivers' own requests, so their garages are not accepted."],
]);

/**
 * What a club is told when it publishes to a channel it has not switched
 * on. The fallback cannot be reached from the two gates above - they only
 * fire for a channel in the map - and is here so that a third switched
 * channel added without a message says something true rather than
 * "undefined".
 */
const refusalFor = (channel: string): string =>
  SWITCHED_CHANNELS.get(channel) ?? `This club has not switched on "${channel}", so those are not accepted.`;

/**
 * A note's whole body, in bytes.
 *
 * One kilobyte. The message itself is capped far below this
 * (NOTE_MAX_CHARS) and the only other field is a name, so anything near
 * this is not a shout-out.
 */
const NOTES_MAX_BODY = 1024;

/** The ids of collected notes, in a batch from the laptop. Room for the whole held set and no more. */
const NOTES_COLLECT_MAX_BODY = 8 * 1024;

/**
 * The longest a message may be, matching ClubNotes.MostChars on the
 * laptop.
 *
 * The two are deliberately the same number written in two places, because
 * the alternative is the relay accepting something the laptop will then
 * silently drop - and the person who typed it being told it was sent. If
 * one moves, the other has to.
 */
const NOTE_MAX_CHARS = 200;

/** And the longest a sender's name may be - ClubNote.From's own 40. */
const NOTE_MAX_NAME = 40;

/**
 * The most notes the relay will hold for one club at a time.
 *
 * Forty. The laptop empties this every time it polls, so the only way to
 * reach it is a club with nobody running the app and somebody sending
 * messages anyway - and then forty is plenty to look at when they next
 * open it, and a ceiling on what a stranger can leave in a club's folder.
 */
const NOTES_HELD_MOST = 40;

/**
 * THE ROOM'S VOTE (12 Oct 2026) - who the room reckons wins the next race,
 * from anybody with the club's link rather than only from the hall.
 *
 * WHY THIS ROUTE IS CHEAPER TO OWN THAN THE SHOUT-OUTS ABOVE
 *
 * A note is a stranger's WORDS, which is why that path holds everything
 * unapproved until a race director reads it. A vote is a tap on one of the
 * names the LAPTOP put on the ballot: this relay will not store a driver
 * id that is not on the published ballot, so nothing arriving here can
 * become a word the announcer says. The worst anybody with the link can
 * achieve is being wrong about who wins.
 *
 * WHY THE VOTES ARE KEYED BY VOTER AND NOT BY ARRIVAL
 *
 * One vote per person, changeable. The item id is a hash of the ballot's
 * own salt and the sender's address, so a second vote REPLACES the first -
 * the same rule the laptop's own store has (RoomVote holds votes per
 * voter). Counting arrivals instead would make the tally a measure of who
 * pressed hardest.
 *
 * The salt is minted per ballot and never leaves the relay, so the ids say
 * nothing: they cannot be correlated between two races or two clubs, and
 * nobody can test whether a given address has voted.
 */
const VOTE_CHANNEL = "votes";

/** The ballot the laptop publishes: whether the club wants this, whether a vote is running, and the grid. */
const VOTE_BALLOT_DOC = "voteballot";

/**
 * The ballot's whole body, in bytes.
 *
 * Four kilobytes. Sixteen drivers at sixty characters is under a
 * kilobyte, so anything near this is not a grid.
 */
const VOTE_BALLOT_MAX_BODY = 4 * 1024;

/** A vote's whole body: one driver id. */
const VOTE_SEND_MAX_BODY = 512;

/**
 * The most drivers one ballot may carry, matching RoomVote.MostOptions on
 * the laptop - and the same deliberate duplication the note length has:
 * if one moves, the other has to.
 */
const VOTE_MOST_DRIVERS = 16;

/** The longest a driver's name or id may be - RoomVote.MostChars' own sixty. */
const VOTE_MAX_CHARS = 60;

/**
 * The most votes the relay will hold for one race, matching
 * RoomVote.MostVoters.
 *
 * Nobody reaches it by voting. It is a ceiling on what the relay keeps in
 * a club's folder, and the laptop reads the whole set each poll rather
 * than draining it - see readRoomVotes.
 */
const VOTES_HELD_MOST = 500;

/** The laptop's signup/answer body: a hash, a state, a line for the phone and at most one driver's name and number. */
const SIGNUP_ANSWER_MAX_BODY = 8 * 1024;

/**
 * How long a sign-up is kept - waiting for the desk, counted from when it
 * was sent, or answered, counted from the answer. The laptop's invites work
 * for fourteen days (SignupStore.InviteLifetime), so a sign-up made on the
 * last day of one still waits as long as the invite itself would have, and
 * the phone can still read its answer a fortnight later - and then the
 * relay forgets it, transponder number and all. The desk's own record of
 * who signed up is on the laptop, where it belongs.
 */
const SIGNUP_KEEP_MS = 14 * 24 * 60 * 60 * 1000;

/**
 * A ceiling on sign-ups held for one club, waiting and answered together.
 * Each needs an invite of its own, which only the desk can make, so this
 * is never a working limit - a club inviting its whole membership at once
 * is a few hundred. It is here so a laptop gone wrong cannot grow the
 * document without end.
 */
const SIGNUP_MAX_ITEMS = 500;

/** The most invites read from one signup document. Past any real club's use, and a bound on the walk every pass check makes. */
const SIGNUP_MAX_INVITES = 2000;

/** The most classes a new driver is offered. Each is cut to sixty characters, as the laptop cuts a sign-up's class. */
const SIGNUP_MAX_CLASSES = 100;

/**
 * One finished session's result is a few kilobytes - a big field is
 * still well under a hundred. The relay's general limit is sized for the
 * badge artwork, and letting every result be that big would let a
 * collection that is never trimmed by the laptop grow without end.
 */
const COLLECTION_ITEM_MAX_BYTES = 256 * 1024;

/**
 * One answer the laptop posts back to manage/answer - RaceDayService's own
 * Response, which can carry an op's returned data. Given the same
 * allowance as one race result: both are "the biggest single thing this
 * app moves in one go", and neither has a reason to be allowed further
 * than the other.
 */
const MANAGE_ANSWER_MAX_BODY = COLLECTION_ITEM_MAX_BYTES;

/**
 * The most the requests waiting for the laptop may add up to, in bytes -
 * MANAGE_QUEUE_MAX_ITEMS's twin. The count alone let three hundred requests
 * of up to MANAGE_MAX_BODY each sit in the tunnel, some twenty megabytes,
 * and all of it used to count against the club's quota for results (see
 * planRoom). A real request is an op and a handful of arguments, a few
 * hundred bytes, and the laptop takes every one waiting each time it polls,
 * every four seconds; this is thousands of them - a laptop away for the
 * whole ten minutes MANAGE_QUEUE_MAX_AGE_MS keeps them, with a phone asking
 * all the while, many times over - and still fourteen of the biggest a phone
 * may send.
 *
 * Fourteen, not sixteen: all of it goes to the laptop in ONE manage/inbox
 * answer, and the laptop reads no answer over a mebibyte
 * (RelayPublisher's MaxResponseContentBufferSize). At sixteen, a queue
 * filled with big requests made that answer bigger than the laptop would
 * read, and every manager request - honest ones included - sat unread
 * until the whole queue aged out ten minutes later. MANAGE_INBOX_MAX_BYTES
 * is that ceiling; this leaves it room for the envelope.
 */
const MANAGE_PENDING_MAX_BYTES = 14 * MANAGE_MAX_BODY;

/** The most the laptop reads of one relay answer - RelayPublisher's MaxResponseContentBufferSize. manage/inbox must always fit. */
export const MANAGE_INBOX_MAX_BYTES = 1024 * 1024;

/**
 * The most the answers waiting to be collected may add up to, in bytes -
 * newest kept, as MANAGE_QUEUE_MAX_ITEMS keeps them. A phone collects its
 * answer within a couple of seconds of it arriving and stops waiting after
 * ninety, so what is kept past that is margin, not a record. Room for every
 * phone's last few minutes on a busy night - a driver list is tens of
 * kilobytes, most answers far less - and always for the answer just posted,
 * which MANAGE_ANSWER_MAX_BODY keeps to a sixteenth of this.
 */
const MANAGE_ANSWERS_MAX_BYTES = 16 * MANAGE_ANSWER_MAX_BODY;

/**
 * Collections used to be capped at three hundred documents, oldest
 * forgotten as new ones arrived. They are not any more: a channel names
 * its own ceiling with ChannelSpec.maxItems, and results name none, so the
 * club's storage quota is what bounds them. See the note in types.ts.
 *
 * Nothing here is unbounded on disk - CLUB_MAX_BYTES still holds, and is
 * what always did the protecting. What the count DID protect was the flat
 * listing, and that has its own ceiling: LISTING_MAX_BYTES, which answers
 * the newest that fit and says how many were left out. A page with a long
 * archive to show should read the index and ask for documents by id
 * rather than pulling a listing it will not draw.
 */

/**
 * Everything one club may hold. Far past anything real - the badge art is
 * the biggest single thing, at a few megabytes - and there so one club's
 * laptop gone wrong cannot fill the disk every other club shares. The
 * race-day manager's tunnel is the one thing left out: it has a budget of
 * its own - see planRoom.
 */
const CLUB_MAX_BYTES = 50 * 1024 * 1024;

/**
 * The largest ?meta=1&limit=N whose cut is kept beside its meta listing.
 * The home page asks for limit=3; nothing asks for more than a handful.
 * Keeping only small cuts keeps what is held bounded in count and bytes.
 * 17 Oct 2026, Bug Hunt 8, A19-1.
 */
const META_LIMIT_CACHED = 50;

/**
 * The most one collection listing may be. A listing is the newest
 * documents that fit, and the older ones past this are left out of it -
 * each is still there to fetch on its own. Real results are a few
 * kilobytes each, so three hundred of them are well under this; it is
 * here for a collection of big documents, whose listing would otherwise be
 * all of them at once, parsed and re-serialised in a 256 MB machine.
 */
const LISTING_MAX_BYTES = 8 * 1024 * 1024;

/**
 * A document bigger than this on disk is left out of listings without
 * being read. Nothing published now can be - COLLECTION_ITEM_MAX_BYTES is
 * smaller - but a file an older relay accepted could be.
 */
const LISTING_ITEM_MAX_BYTES = 512 * 1024;

/**
 * The most documents one ?ids= read may name.
 *
 * A page draws a race day at a time - twenty or so sessions - so fifty is
 * a day and change. The cap is here because this is the one read whose
 * cost the CALLER picks: without it, one request could ask the relay to
 * read a whole collection, and fifty of those at once is a machine doing
 * nothing else.
 */
const ITEMS_BY_ID_MAX = 50;

/**
 * The most one ?ids= answer may be, in bytes.
 *
 * It used to be LISTING_MAX_BYTES, the same eight megabytes a whole
 * listing may be - but a listing is built once and held, and this answer
 * is built afresh for every request that asks, by anybody, with no
 * password. Twenty of those at once was most of a 256 MB machine. A real
 * race day is twenty-odd results of a few kilobytes each, so a megabyte
 * is a whole day with a lot of room left; a page asking for more than
 * fits is told how many were left out (see readSomeItems) and asks again
 * for the rest.
 */
const ITEMS_BY_ID_MAX_BYTES = 1024 * 1024;

/**
 * ?ids= reads being answered at once, across every club, and how many
 * more may wait for a turn before the relay says "busy" instead.
 *
 * The same idea as LISTING_BUILDS_AT_ONCE, with a slot of their own so a
 * crowd of these cannot hold up a club's listing, nor the other way round.
 * One answer is at most ITEMS_BY_ID_MAX_BYTES plus the garbage of parsing
 * the documents in it one at a time - a few megabytes - so two at once is
 * nothing, and it is the "at once" that has to be bounded: unbounded, it
 * was every request that arrived together, each holding its own answer.
 * Past the queue, a request is refused with 503 rather than held: a page
 * treats that like any other failed read and tries again on its next
 * refresh, and a flood of them cannot pile up requests in memory.
 */
const ITEMS_BY_ID_AT_ONCE = 2;
const ITEMS_BY_ID_QUEUE_MAX = 200;

/**
 * Public collection reads that cost the relay a read of the disk - every
 * ?ids= read, and a single document that is not already held - allowed
 * from one address in a minute.
 *
 * Keyed by address alone, not club and address: the cost falls on the
 * one machine every club shares, so a client does not get a fresh
 * allowance by asking about another club. Generous for the same reason
 * checkin's reads are: a paddock of phones behind the venue's one address
 * with the live results open, each re-reading its day every thirty
 * seconds, is a few hundred a minute before anybody has touched anything.
 */
const COLLECTION_READS_PER_ADDRESS = 600;

/**
 * The two open voice-sample routes, from one address in a minute.
 *
 * These are the only routes in the relay that can spend money for
 * somebody with no token at all, so they are the only ones where the
 * address is the whole of what is known about the caller. Somebody making
 * their mind up fetches the list and then one sample per voice - call it a
 * dozen requests - and listens for a while; nothing honest comes near
 * sixty. What stops the money is not this but the samples themselves:
 * there is one fixed line per voice and it is rendered once, ever (see
 * VoiceService.sample). This stops the bandwidth and the file reads.
 */
const VOICE_SAMPLES_PER_ADDRESS = 60;

/**
 * Collection listings kept ready to send. A results page polls the whole
 * list, and reading and re-serialising three hundred files for each poll
 * of each phone is most of what the relay would otherwise spend its time
 * on. Capped in total so a relay with many clubs cannot run a small
 * machine out of memory with it; every listing fits, being at most
 * LISTING_MAX_BYTES.
 */
const LISTING_CACHE_MAX_TOTAL = 32 * 1024 * 1024;

/**
 * Listings being built at once, across every club. Building a full-sized
 * one costs around 50 MB while it runs - its documents parsed one at a
 * time, and the garbage that leaves - so clubs asked at the same moment
 * take turns rather than each take that much of a 256 MB machine at once.
 * A turn is a fraction of a second, and only a listing that is not
 * already held waits for one. Requests for the same listing never build
 * it twice - they wait for the one already being built.
 */
const LISTING_BUILDS_AT_ONCE = 1;

const DRIVER_ID = /^[0-9a-f]{20}$/;

interface InboxItem {
  readonly n: number;
  readonly id: string;
  readonly wanted: boolean;
  readonly atUtc: string;
}

interface Inbox {
  readonly next: number;
  readonly items: readonly InboxItem[];
}

/** One phone request, waiting for the laptop to fetch and run it. */
interface ManagePendingItem {
  readonly reqId: string;
  /** Exactly {op, args?, confirm?} - what HandleJsonAsync itself expects. The relay never looks inside it. */
  readonly body: unknown;

  /**
   * The phone's own TEXT of that same {op, args, confirm} - the exact
   * bytes it signed - carried through to the laptop untouched.
   *
   * A string, not an object, and that is the point of it. `body` above is
   * rebuilt here from the fields this relay recognises, so its bytes are
   * this relay's and not the phone's; a signature over them would prove
   * nothing about what the laptop is going to run. The laptop verifies
   * against this text and then runs THIS text. Empty from a phone that has
   * not enrolled, which is every phone until a club sets one up.
   */
  readonly payload?: string;

  /** Which enrolled phone signed it. Public, and meaningless without the key it names. */
  readonly deviceId?: string;

  /** When the phone signed it, ISO-8601. The laptop refuses a stale one against its OWN clock. */
  readonly sentAt?: string;

  /**
   * HMAC-SHA256 over club, deviceId, reqId, sentAt and payload, keyed on
   * a secret this relay has never held and cannot derive.
   *
   * Never checked here, and could not be: the key is the laptop's and the
   * phone's. It is carried for the laptop to judge, which is exactly the
   * arrangement that makes it worth anything - a proof this relay could
   * verify would be a proof this relay could forge.
   */
  readonly mac?: string;

  readonly enqueuedAt: string;
}

/** One answer the laptop has posted back, waiting for the phone to collect it. */
interface ManageAnswerItem {
  readonly reqId: string;
  /** What RaceDayService.HandleJsonAsync answered - the relay never looks inside this either. */
  readonly response: unknown;
  readonly answeredAt: string;
}

/** The race-day-manager tunnel for one club: everything not yet answered, and everything answered but not yet collected. */
interface ManageQueue {
  readonly pending: readonly ManagePendingItem[];
  readonly answers: readonly ManageAnswerItem[];
}

/**
 * The manage document as the relay uses it, read fresh each time (see
 * currentManage) - unlike checkin's roster, there is nothing here worth
 * caching in memory.
 */
interface ManageView {
  readonly enabled: boolean;
  /** The manager PIN's hash, or "" when the club has not set one. */
  readonly pinSha256: string;
  /** The club's PIN salt (see pinSaltOf), or "" for a laptop that still publishes plain SHA-256. */
  readonly salt: string;
  /** The pass a signed-in manager device carries (see passFor), or "" with no manager PIN. */
  readonly pass: string;
}

/**
 * What a new driver typed on the sign-up page, checked (see
 * signupFormFrom). The field names are the laptop's own - RelayPublisher's
 * ReadSignup reads exactly these - and nothing else is kept.
 */
interface SignupForm {
  readonly name: string;
  readonly className: string;
  /** "own" with a number, "rental" for one of the club's, or "unknown" for "I don't know yet". */
  readonly transponderKind: "own" | "rental" | "unknown";
  /** Only for "own"; "" otherwise, whatever the phone sent. */
  readonly transponder: string;
  readonly car: string;
  /** Optional: "" is fine. Where the laptop emails the driver's own check-in PIN once they are approved. */
  readonly email: string;
  /** A parent or guardian signing up a driver under 18. */
  readonly guardian: boolean;
}

/** An approved driver as the phone is told about them - enough for a link to their own check-in card, and no more. */
interface SignupDriver {
  /** The same keyed id a check-in badge carries (DRIVER_ID). */
  readonly id: string;
  readonly name: string;
  readonly className: string;
  readonly number: string;
}

/**
 * One sign-up the relay is holding, by the hash of the invite it was made
 * with. "pending" carries the form and nothing else; an answered one
 * carries the desk's answer and no longer the form - once the laptop has
 * it, what the driver typed (a transponder number among it) is not the
 * relay's to keep.
 */
interface SignupItem {
  readonly h: string;
  readonly state: "pending" | "approved" | "rejected";
  readonly submittedAt: string;
  readonly form?: SignupForm;
  readonly message?: string;
  readonly checkedIn?: boolean;
  readonly driver?: SignupDriver;
  readonly answeredAt?: string;
}

/**
 * The signup document as the relay uses it, read fresh every time, as the
 * manage document is: it is small, and changes whenever the desk makes or
 * cancels an invite.
 */
interface SignupView {
  readonly enabled: boolean;
  /** Today's event's name, or "" - shown above the form. */
  readonly event: string;
  /** The classes a new driver can pick from. Empty when the club has none, and the class is then typed. */
  readonly classes: readonly string[];
  /** Each usable invite's hash, to when it expires in milliseconds. */
  readonly invites: ReadonlyMap<string, number>;
  /** The club's PIN salt, or "". An invite made since the laptop had one is hashed with it; one made before is not - see inviteHashFor. */
  readonly salt: string;
}

interface CheckinDriver {
  readonly id: string;
  readonly name: string;
  readonly className: string;
  readonly number: string;
  readonly checkedIn: boolean;
  readonly photo: string;
}

/**
 * The check-in document as the relay uses it, read and cleaned once.
 *
 * Racer PINs are why this is kept at all: finding whose PIN was typed
 * means hashing the guess and looking it up, and building that lookup
 * from the roster on every request would re-walk every driver - photos
 * and all - for each poll of each phone. The racer hashes live only here,
 * never on a CheckinDriver, so nothing that sends a driver can send one.
 */
interface CheckinView {
  readonly enabled: boolean;
  readonly event: string;
  /** The staff PIN's hash, or "" when the club has not set one. */
  readonly pinSha256: string;
  /** The club's PIN salt, or "": which of the two recipes the staff and racer hashes here were made with. */
  readonly salt: string;
  /** The pass a signed-in staff device carries (see passFor), or "" with no staff PIN. */
  readonly staffPass: string;
  /** The club pass key every pass here was made with - for telling a pass from a PIN that has since changed (checkinRequest). */
  readonly passKey: Buffer;
  readonly drivers: readonly CheckinDriver[];
  /**
   * Each racer PIN hash to the one driver it opens, and that racer's pass:
   * looked up by hash for a PIN, walked in full for a pass (racerByPass).
   */
  readonly racers: ReadonlyMap<string, RacerEntry>;
}

interface RacerEntry {
  readonly driver: CheckinDriver;
  readonly pass: string;
}

/**
 * The drivers' live document or their details document, read and parsed
 * once (22 Oct 2026, driver experience) - see Relay.driverDocOf.
 */
interface DriverDoc {
  /** When this relay took it, in milliseconds - what freshness is judged by. */
  readonly takenAt: number;
  /** The document's own "at", or "" when it has none. */
  readonly at: string;
  /** Badge id to that driver's entry. A Map, so no key a laptop sent can be "__proto__". */
  readonly drivers: ReadonlyMap<string, Readonly<Record<string, unknown>>>;
}

/**
 * A club's look as its site document sets it, and the picture drawn from
 * it once somebody has asked for it: the club's generic result card. It
 * lives here rather than in a cache of its own so that forgetSite throws
 * it away along with the look it was drawn from - a club that changes
 * its colours cannot be left serving it in the old ones. (The home-screen
 * icon was kept here too until 30 Oct 2026; it is HeatCaller's sun for
 * every club now, drawn once in sun.ts.)
 */
interface SiteEntry {
  readonly look: Look;
  card: Buffer | undefined;
}

/** A document being read, and which version of it (an epoch - see siteEpochs and checkinEpoch) the read began at. */
interface Load<T> {
  readonly epoch: number;
  readonly done: Promise<T>;
}

/** A listing being built, and which version of the collection it is a listing of. */
interface ListingBuild {
  readonly generation: string;
  /** undefined only for one document held whole that turned out not to be there - see buildOnce. */
  readonly done: Promise<Buffer | undefined>;
}

/**
 * A count of recent events per key, forgetting anything older than the
 * window. Used for PIN guesses and for check-in traffic in general.
 *
 * In memory, on purpose: a relay restart forgets it, and the cost of that
 * is a would-be guesser getting a fresh ten tries after a deploy - which
 * against a relay that also sleeps when idle is no cost worth a database.
 */
export class Limiter {
  private readonly hits = new Map<string, number[]>();

  constructor(
    private readonly limit: number,
    private readonly windowMs: number,
    private readonly clock: () => number,
    private readonly maxKeys = 10000,
  ) {}

  private recent(key: string): number[] {
    const cutoff = this.clock() - this.windowMs;
    const kept = (this.hits.get(key) ?? []).filter((t) => t > cutoff);

    if (kept.length === 0) {
      this.hits.delete(key);
    } else {
      this.hits.set(key, kept);
    }

    return kept;
  }

  /** Whether this key has used up its allowance. */
  blocked(key: string): boolean {
    return this.recent(key).length >= this.limit;
  }

  /** Whether this key still has room for `count` more - for a request that costs more than one. */
  allows(key: string, count: number): boolean {
    return this.recent(key).length + count <= this.limit;
  }

  /** Counts one. */
  hit(key: string): void {
    const kept = this.recent(key);

    // Taken out and put back, so the map's order is the order keys were
    // last used in and the first entry is always the stalest.
    this.hits.delete(key);
    this.hits.set(key, [...kept, this.clock()]);

    if (this.hits.size > this.maxKeys) {
      this.prune();
    }
  }

  /** How many keys it is holding. For tests. */
  get size(): number {
    return this.hits.size;
  }

  /**
   * Bounded, so a flood of distinct keys cannot grow this forever - but
   * never by forgetting everything at once. Clearing the lot would hand
   * every guesser a fresh allowance the moment somebody sent ten thousand
   * junk addresses, which is a cheap way to buy one. Keys whose window has
   * passed go first, since they hold nothing; then, only if that was not
   * enough, the ones used longest ago. Down to nine-tenths of the cap, so
   * the next hit does not pay for the whole walk again.
   */
  private prune(): void {
    const cutoff = this.clock() - this.windowMs;

    for (const [key, times] of this.hits) {
      if ((times[times.length - 1] ?? 0) <= cutoff) {
        this.hits.delete(key);
      }
    }

    const target = Math.floor(this.maxKeys * 0.9);

    for (const key of this.hits.keys()) {
      if (this.hits.size <= target) {
        break;
      }

      this.hits.delete(key);
    }
  }
}

export interface RelayOptions {
  readonly store: Store;
  readonly tenants: TenantRegistry;
  /** Largest payload accepted, in bytes. */
  readonly maxBodyBytes?: number;
  /** Swappable so tests are not at the mercy of the clock. */
  readonly now?: () => Date;
  readonly log?: (line: string) => void;
  /**
   * When set, this relay only forwards: every request except /healthz is
   * answered with a permanent redirect to the same path on this origin.
   * For an old address kept alive after a move, so badges and posters
   * printed with it still land on the new one.
   */
  readonly redirectOrigin?: string | undefined;
  /**
   * The HeatCaller voice. Absent means this relay does not offer it at all:
   * every club speaks with its own ElevenLabs key or the free Kokoro
   * voice, exactly as before this existed.
   */
  readonly voice?: VoiceService | undefined;
  /**
   * Where drivers' own race clips are kept (22 Oct 2026, driver
   * experience) - see clips.ts. Absent, the relay does not take them, and
   * says so.
   */
  readonly clips?: ClipVault | undefined;
  /**
   * The commentary brain. Absent means this relay writes no commentary:
   * clubs use Ollama on the race laptop, or go without, exactly as
   * before this existed.
   */
  readonly brain?: BrainService | undefined;
  /**
   * Where a club's asks for more of an allowance are kept (see
   * topuprequests.ts). Absent means none are taken: the usage routes still
   * answer, with no requests, and asking is refused as unavailable.
   */
  readonly topUpRequests?: TopUpRequests | undefined;
  /**
   * Where every voice and commentary request is logged for the relay admin
   * app's charts (see usagelog.ts). Absent means nothing is logged - the
   * requests themselves are unaffected either way.
   */
  readonly usageLog?: UsageLog | undefined;
  /**
   * The lap voice's clips (29 Oct 2026) - see lapvoice.ts. Absent, the
   * manifest lists none and the pages use the phone's own voice.
   */
  readonly lapVoice?: LapVoiceLibrary | undefined;
  /**
   * Push alerts to drivers' phones (6 Nov 2026) - see push.ts. Absent, the
   * relay takes no sign-ups and the Me page keeps its on-screen alerts.
   */
  readonly push?: PushService | undefined;
}

interface Parsed {
  readonly method: string;
  readonly segments: readonly string[];
  readonly query: URLSearchParams;
}

const DEFAULT_MAX_BODY = 1024 * 1024;

export class Relay {
  private readonly store: Store;
  private readonly lapVoice: LapVoiceLibrary;
  private readonly tenants: TenantRegistry;
  private readonly maxBodyBytes: number;
  private readonly now: () => Date;
  private readonly log: (line: string) => void;
  private readonly redirectOrigin: string | undefined;
  private readonly locks = new Map<string, Promise<void>>();
  private readonly pinFailures: Limiter;
  private readonly pinGuard: PinGuard;
  private readonly checkinTraffic: Limiter;
  private readonly checkinReads: Limiter;
  private readonly driverTaps: Limiter;
  /** Wrong PINs carried by check-in taps, per club|badge - see checkinRequest (26 Oct 2026). Apart from pinGuard and pinFailures on purpose. */
  private readonly badgePinFailures: Limiter;
  /** Ten wrong manager-PIN guesses in ten minutes, per club and address - the manager's own pinFailures. */
  private readonly managePinFailures: Limiter;
  /** The manager PIN's own club-wide lockout, kept apart from checkin's pinGuard - see PinGuard's own doc comment. */
  private readonly managePinGuard: PinGuard;
  /**
   * The last non-empty staff (checkin) and manager PIN hash each club
   * published, in memory only. The laptop publishes an empty PIN while the
   * feature is off, so comparing against the previous document alone let
   * unticking and re-ticking the box (same PIN) lift a lockout. 17 Oct
   * 2026, Bug Hunt 8, A19-3. After a restart these are empty and the
   * previous document is compared against, as before.
   */
  private readonly lastStaffPin = new Map<string, string>();
  private readonly lastManagePin = new Map<string, string>();
  /** signin, request and the response poll together - one manager, working at a human's pace, needs nowhere near checkin's traffic budget. */
  private readonly manageTraffic: Limiter;
  /** Ten wrong invite codes in ten minutes, per club and address - sign-up's own pinFailures. */
  private readonly signupPinFailures: Limiter;
  /** Sign-up's own club-wide lockout, apart from check-in's and the manager's - see PinGuard's doc comment. */
  private readonly signupPinGuard: PinGuard;
  /** signin, status and submit together, per club and address. */
  private readonly signupTraffic: Limiter;
  /** Slow PIN hashes paid, per club and address, across check-in, the manager and sign-up together. See SLOW_HASHES_PER_ADDRESS. */
  private readonly slowHashes: Limiter;
  /** The same for the whole relay, one key. See SLOW_HASHES_IN_ALL. */
  private readonly slowHashBudget: Limiter;

  /**
   * Shout-outs from one address, per club: four in five minutes.
   *
   * Far tighter than sign-up's traffic limiter beside it, and for a
   * different reason. A sign-up is somebody filling a form in and
   * correcting it; a shout-out is one sentence somebody means to send
   * once. There is no honest reason to send a fifth, so the limit is set
   * where a person will never meet it and a script meets it immediately.
   */
  private readonly notesTraffic: Limiter;
  private readonly voteTraffic: Limiter;

  /** Sponsor views and taps from one address - see sponsorSeen. */
  private readonly sponsorTraffic: Limiter;
  /** One laptop rendering lines: a race night's worth in a burst when Prepare Lines runs, and a trickle after that. */
  private readonly voiceTraffic: Limiter;
  /** The two open voice-sample routes, per address - nobody is signed in, so that is all there is. See VOICE_SAMPLES_PER_ADDRESS. */
  private readonly voiceSamples: Limiter;
  private readonly voice: VoiceService | undefined;
  // 28 Oct 2026, asked for by Asher ("Remove the podium videos for
  // downloading from the relay and just have locally"): the podium video
  // vault, its upload, its pages and forget-media are gone. The videos stay
  // on the laptop. See removeLeftoverVideos (oldvideos.ts) for the disk.
  /** Drivers' own race clips - see clips.ts (22 Oct 2026, driver experience). */
  private readonly clips: ClipVault | undefined;
  private readonly push: PushService | undefined;
  /** Phones fetching a clip, per club and address - an audio player asks in ranges too. */
  private readonly clipReads: Limiter;
  /** One laptop sending clips: one per driver per race. */
  private readonly clipUploads: Limiter;
  /** Clip uploads being read right now, across every club. */
  private clipUploadsInFlight = 0;
  /** Car photos being read right now, across every club - see driverPhoto. */
  private photoUploadsInFlight = 0;
  /**
   * How many times each club's clips have been wiped - by closing nights or
   * deleting everything. An upload that started before and finishes after
   * sees the count has moved and keeps nothing.
   */
  private readonly clipWipes = new Map<string, number>();
  /**
   * Drivers' asks from one address, per club: three hundred in ten minutes
   * (22 Oct 2026, driver experience).
   *
   * WHY (23 Oct 2026, bug hunt 10): it was twenty, but a whole paddock
   * shares the venue Wi-Fi's one address (or a carrier's NAT), so the 21st
   * pack change between two rounds was refused for everybody. This is a
   * flood brake only - the per-person limits are askPerDriver, askQueries
   * and DRIVER_ASKS_PER_DRIVER, all keyed by badge.
   */
  private readonly askTraffic: Limiter;
  /** Asks from one badge, per club: six an hour, whatever they are. */
  private readonly askPerDriver: Limiter;
  /** Result queries from one badge, per club: three an hour. */
  private readonly askQueries: Limiter;
  /**
   * Changes the laptop applies by itself - packs, cars, setups - from one
   * badge, per club: a hundred and twenty an hour (5 Nov 2026), so swapping
   * packs between every round and logging every sheet never runs out.
   */
  private readonly askWrites: Limiter;
  /** Car photos from one badge, per club: thirty an hour. */
  private readonly photoUploads: Limiter;
  /**
   * The night page's live poll ({part:"live"}), per club and address -
   * apart from checkinReads, because a paddock of phones on the venue's
   * one address polling every twenty seconds would otherwise use up the
   * allowance the check-in page and full night reads share.
   */
  private readonly nightLiveReads: Limiter;
  /**
   * The drivers' live and details documents, parsed, per club and channel
   * - "club/driverlive", "club/driverme". null is "nothing there". Every
   * publish to either calls forgetDriverDoc, as checkin's does
   * forgetCheckin: two hundred phones polling a quarter-megabyte document
   * must not mean two hundred parses of it.
   */
  private readonly driverDocs = new Map<string, DriverDoc | null>();
  private driverDocEpoch = 0;
  private readonly driverDocLoads = new Map<string, Load<DriverDoc | undefined>>();
  private readonly brain: BrainService | undefined;
  private readonly topUpRequests: TopUpRequests | undefined;
  private readonly usageLog: UsageLog | undefined;
  private readonly listings = new Map<string, Buffer>();
  /**
   * ?meta=1&limit=N cuts, keyed to the very meta listing Buffer they were
   * cut from, so a rebuilt listing (a new Buffer) never meets an old cut
   * and forgetListing needs to do nothing for them. 17 Oct 2026, Bug Hunt
   * 8, A19-1: without this every poll re-parsed the whole meta listing.
   */
  private readonly metaCuts = new WeakMap<Buffer, Map<number, Buffer>>();
  private listingBytes = 0;
  /**
   * Bumped by a whole club's delete; listingGens is bumped per collection
   * by anything that writes to it. Together they are a listing's
   * generation, so a listing read before a write is never cached - or
   * joined by a request that came - after it.
   */
  private listingEpoch = 0;
  private readonly listingGens = new Map<string, number>();
  /** Listings being built right now, so a second request for one waits for it rather than building it again. */
  private readonly listingBuilds = new Map<string, ListingBuild>();
  private listingBuildsRunning = 0;
  private readonly listingBuildQueue: (() => void)[] = [];
  /** ?ids= reads under way and waiting - see ITEMS_BY_ID_AT_ONCE. */
  private itemReadsRunning = 0;
  private readonly itemReadQueue: (() => void)[] = [];
  /** Public collection reads that went to the disk, per address - see COLLECTION_READS_PER_ADDRESS. */
  private readonly collectionReads: Limiter;
  /** Each club's look, from its site document. Only a publish of that document changes it. */
  private readonly sites = new Map<string, SiteEntry>();
  /**
   * Bumped by every write to a club's site document, for the same reason
   * as listingEpoch - but one count per club rather than one for the
   * whole relay. A single count meant any club publishing its site threw
   * away every OTHER club's read in flight: harmless for a look, which is
   * simply read again, but a club's result cards are only kept when
   * nothing changed while they were drawn (see cardPng), and a relay where
   * somebody somewhere is always publishing would then keep none of them.
   * Never deleted, so a count can never go back to a number a read in
   * flight already noted; there is one per configured club at most.
   */
  private readonly siteEpochs = new Map<string, number>();
  /** The default look a club gets while its site document will not read, with its pictures - see siteOf. */
  private readonly siteFallbacks = new Map<string, SiteEntry>();
  /** Site documents being read right now, so requests that arrive together share one read. */
  private readonly siteLoads = new Map<string, Load<SiteEntry>>();
  /** Each club's check-in document, cleaned. null is "nothing published", which is worth remembering too. */
  private readonly checkins = new Map<string, CheckinView | null>();
  /** Bumped by every write to a check-in document, for the same reason as listingEpoch. */
  private checkinEpoch = 0;
  /**
   * Check-in documents being read right now, shared the same way. The
   * roster carries every driver's photo, up to the whole body limit, and a
   * relay woken from sleep is handed every request that queued while it
   * started at once: a few hundred page loads each reading and parsing
   * their own copy was most of a gigabyte on a 256 MB machine.
   */
  private readonly checkinLoads = new Map<string, Load<CheckinView | undefined>>();
  /**
   * Each club's newest index years, read down to what a link's preview
   * card may say - see preview.ts. Held exactly like the look and the
   * check-in roster above, and for the same reason: the pages that need
   * it need no password, and a crawler handed fifty shared results would
   * otherwise be fifty reads of the same documents.
   */
  private readonly previews = new Map<string, { readonly book: ReadonlyMap<string, SessionPreview>; readonly bytes: number }>();
  /** What previews holds, by previewBytes - see PREVIEW_CACHE_MAX_TOTAL. */
  private previewCacheBytes = 0;
  /** Bumped by every write to a club's resultsindex document - per club, as siteEpochs is and for the same reason. */
  private readonly previewEpochs = new Map<string, number>();
  /** Index documents being read for a preview right now, shared the same way. */
  private readonly previewLoads = new Map<string, Load<ReadonlyMap<string, SessionPreview>>>();
  /** Drawn result cards, keyed club and session. Thrown away by forgetCards - see CARD_CACHE_MAX_TOTAL. */
  private readonly cards = new Map<string, Buffer>();
  private cardBytes = 0;
  /** How many cards this relay has actually drawn, as against served from the cache. For tests. */
  private cardsDrawn = 0;
  /** Cards one address has made this relay draw, and cards drawn for everybody - see CARDS_PER_ADDRESS. */
  private readonly cardDraws: Limiter;
  private readonly cardBudget: Limiter;
  /** Each club's pass key, read (or made) once - see passKeyOf. One per configured club at most: every route that asks has checked the club exists. */
  private readonly passKeys = new Map<string, Promise<Buffer>>();
  /**
   * Sign-up passes already made, keyed to the very pass key Buffer they
   * were made with, so a new key (a club deleted and set up again) drops
   * them with no code of its own. See signupPassOf. 17 Oct 2026, Bug Hunt
   * 8, A20-3.
   */
  private readonly signupPasses = new WeakMap<Buffer, Map<string, string>>();

  constructor(options: RelayOptions) {
    this.store = options.store;
    this.tenants = options.tenants;
    this.maxBodyBytes = options.maxBodyBytes ?? DEFAULT_MAX_BODY;
    this.now = options.now ?? (() => new Date());
    this.log = options.log ?? ((line) => console.log(line));
    this.redirectOrigin = options.redirectOrigin;

    // Ten wrong PINs in ten minutes, per club and address. In memory: it
    // is the club-wide count below that sets how fast a PIN can be found.
    this.pinFailures = new Limiter(10, 10 * 60 * 1000, () => this.now().getTime());

    // And fifty across every address at once, kept on disk and closing
    // the list for longer each time it is hit. The per-address limit
    // alone is beaten by spreading guesses over many addresses; this one
    // cannot be. What that costs a club, and how little, is in PinGuard.
    this.pinGuard = new PinGuard(this.store, () => this.now().getTime(), this.log);

    // Generous for a person, a wall for a script: a desk checking in a
    // whole club taps perhaps once a second at most. Taps only - reading
    // the list has its own allowance below.
    this.checkinTraffic = new Limiter(120, 60 * 1000, () => this.now().getTime());

    // Reads - the list and a badge - counted apart from taps, and far more
    // of them allowed. A club's paddock often shares one address through
    // the venue's Wi-Fi or a phone carrier's NAT, and every open check-in
    // page polls: thirty phones at one poll each ten seconds is 180 a
    // minute from "one" address before anybody has tapped anything. Had
    // those counted against the tap budget, the desk would have been
    // locked out by the people it was checking in.
    this.checkinReads = new Limiter(600, 60 * 1000, () => this.now().getTime());

    // Per driver, from anywhere. A person taps their own badge a couple of
    // times at most; a script hammering one id to churn the queue and the
    // roster gets six a minute and no more.
    this.driverTaps = new Limiter(6, 60 * 1000, () => this.now().getTime());
    // Five wrong PINs in ten minutes for any one badge (26 Oct 2026): a
    // driver fumbling their own PIN is nowhere near it, and somebody holding
    // another driver's badge gets five tries in a million per ten minutes.
    this.badgePinFailures = new Limiter(5, 10 * 60 * 1000, () => this.now().getTime());

    // The race-day manager's own PIN guard, same shape as check-in's and
    // for the same reasons, but kept entirely apart: a manager PIN and a
    // check-in PIN are different secrets, so a run of wrong guesses at one
    // must never close the other's list, and a change to one must never
    // reset the other's count.
    this.managePinFailures = new Limiter(10, 10 * 60 * 1000, () => this.now().getTime());
    this.managePinGuard = new PinGuard(
      this.store,
      () => this.now().getTime(),
      this.log,
      "managepinguard",
      "manager",
      "the manager PIN",
    );

    // One manager at a time, working at a human's pace: a sign-in, an
    // occasional action, and a response poll every second or two while one
    // is outstanding. Generous next to that, and nowhere near check-in's
    // budget - there is no paddock full of phones polling this one.
    this.manageTraffic = new Limiter(120, 60 * 1000, () => this.now().getTime());

    // Driver sign-up's invite codes, guarded the same way again and kept
    // apart from both of the above: a run of wrong invite codes must never
    // close check-in or the manager, and the other way round. Ten per
    // connection in ten minutes, fifty across the club - the numbers the
    // security page promises. An invite code is eight digits against a
    // six-digit racer PIN, and only the desk can make one, so these same
    // limits leave a guesser far further from finding one.
    this.signupPinFailures = new Limiter(10, 10 * 60 * 1000, () => this.now().getTime());
    this.signupPinGuard = new PinGuard(
      this.store,
      () => this.now().getTime(),
      this.log,
      "signupguard",
      "sign-up",
      "the invite list",
    );

    // A phone waiting for the desk polls its status every ten seconds, and
    // a club night's newcomers may all be on the venue's one address - so
    // more room than the manager's, still a wall for a script.
    this.signupTraffic = new Limiter(300, 60 * 1000, () => this.now().getTime());

    // Every slow PIN hash, right guess or wrong. The PIN limits above only
    // count wrong ones, so a racer with their own PIN could sign in over
    // and over - or keep a stale pass's PIN coming, or an expired invite
    // code - and make the relay pay a tenth of a second each time with
    // every club waiting. These count the hashes themselves, before any
    // is worked out. See SLOW_HASHES_PER_ADDRESS.
    this.slowHashes = new Limiter(SLOW_HASHES_PER_ADDRESS, 60 * 1000, () => this.now().getTime());
    this.slowHashBudget = new Limiter(SLOW_HASHES_IN_ALL, 60 * 1000, () => this.now().getTime(), 1);
    this.notesTraffic = new Limiter(4, 5 * 60 * 1000, () => this.now().getTime());

    // Looser than the notes limiter, and matching the laptop's own Wi-Fi
    // route: changing your mind is a legitimate thing to do here, where
    // sending a second dedication is not.
    this.voteTraffic = new Limiter(6, 5 * 60 * 1000, () => this.now().getTime());
    this.sponsorTraffic = new Limiter(20, 5 * 60 * 1000, () => this.now().getTime());

    // Prepare Lines on the laptop walks a whole announcer style in one go -
    // a hundred or so lines, two at a time - and a club is entitled to do
    // that before a meeting. Generous enough for that burst, and still a
    // wall for anything running flat out: the allowance, not this, is what
    // limits what a club can spend.
    this.voiceTraffic = new Limiter(300, 60 * 1000, () => this.now().getTime());

    // The samples, which anyone at all can fetch. Its own limiter rather
    // than the one above, keyed by address alone: there is no club and no
    // token to key it by, and a visitor hearing voices must never be able
    // to use up the budget a club's laptop renders its lines from.
    this.voiceSamples = new Limiter(VOICE_SAMPLES_PER_ADDRESS, 60 * 1000, () => this.now().getTime());

    // Drawing cards. Two limits rather than one because they are for two
    // different things: the per-address one keeps a single client from
    // walking a club's session ids, and the second - one key, the whole
    // relay - is the hard ceiling on how much of a small machine's CPU
    // this feature can ever take. See CARDS_PER_ADDRESS.
    this.cardDraws = new Limiter(CARDS_PER_ADDRESS, 60 * 1000, () => this.now().getTime());
    this.cardBudget = new Limiter(CARDS_IN_ALL, 60 * 1000, () => this.now().getTime(), 1);

    // Public collection reads that are not already held. See
    // COLLECTION_READS_PER_ADDRESS.
    this.collectionReads = new Limiter(COLLECTION_READS_PER_ADDRESS, 60 * 1000, () => this.now().getTime());

    this.voice = options.voice;
    this.lapVoice = options.lapVoice ?? new LapVoiceLibrary(new Map());
    // 22 Oct 2026, driver experience: clips and drivers' asks. See the fields.
    this.clips = options.clips;
    this.push = options.push;
    this.clipReads = new Limiter(900, 60 * 1000, () => this.now().getTime());
    this.clipUploads = new Limiter(120, 60 * 1000, () => this.now().getTime());
    this.askTraffic = new Limiter(300, 10 * 60 * 1000, () => this.now().getTime());
    this.askPerDriver = new Limiter(6, 60 * 60 * 1000, () => this.now().getTime());
    this.askQueries = new Limiter(3, 60 * 60 * 1000, () => this.now().getTime());
    this.askWrites = new Limiter(120, 60 * 60 * 1000, () => this.now().getTime());
    this.photoUploads = new Limiter(30, 60 * 60 * 1000, () => this.now().getTime());
    this.nightLiveReads = new Limiter(1200, 60 * 1000, () => this.now().getTime());
    this.brain = options.brain;
    this.topUpRequests = options.topUpRequests;
    this.usageLog = options.usageLog;
  }

  /**
   * Runs one read-modify-write for a club with no other in flight.
   *
   * Node runs one thing at a time, but a request awaiting the disk lets
   * another one in - and two check-in taps that both read the queue before
   * either writes it would lose one of them. Every write a club makes goes
   * through here, publishes included, for the same reason: a tap that read
   * the roster, then lost the disk to the laptop publishing a newer one,
   * would write the older roster back over it.
   */
  private async withLock<T>(club: string, work: () => Promise<T>): Promise<T> {
    const previous = this.locks.get(club) ?? Promise.resolve();
    const run = previous.then(work);
    const settled = run.then(
      () => undefined,
      () => undefined,
    );

    this.locks.set(club, settled);

    try {
      return await run;
    } finally {
      if (this.locks.get(club) === settled) {
        this.locks.delete(club);
      }
    }
  }

  /**
   * The club's pass key (see keyedPass): 32 random bytes, kept on disk in
   * the club's own folder so a pass survives a restart, made the first time
   * a club needs one. Held in memory after that. Two first asks share one
   * read, and the key is made inside the club's lock with the file read
   * again there, so two can never be made and the second overwrite the
   * first - which would sign out every device the first one's passes went
   * to.
   *
   * Never called from inside withLock: it may take the lock itself.
   *
   * A file that could not be read throws (a 503, "try again"), and is not
   * remembered: it may hold the key every signed-in device's pass was made
   * with. A file that is not JSON will never mend and is replaced - the
   * price is every device signing in once more. A key that cannot be saved
   * (a full disk) is used from memory, and passes change on the next
   * restart - again, a sign-in, not a failure.
   */
  private passKeyOf(club: string): Promise<Buffer> {
    let held = this.passKeys.get(club);

    if (held === undefined) {
      const loading = this.loadPassKey(club);

      held = loading;
      this.passKeys.set(club, loading);
      loading.catch(() => {
        if (this.passKeys.get(club) === loading) {
          this.passKeys.delete(club);
        }
      });
    }

    return held;
  }

  private async loadPassKey(club: string): Promise<Buffer> {
    const read = async (): Promise<Buffer | undefined> => {
      try {
        const body = (await this.store.getSnapshot(club, PASS_KEY_DOC))?.body;
        const hex = isPlainObject(body) ? body["key"] : undefined;

        return typeof hex === "string" && /^[0-9a-f]{64}$/.test(hex) ? Buffer.from(hex, "hex") : undefined;
      } catch (error) {
        if (error instanceof StoreReadError && error.corrupt) {
          return undefined;
        }

        throw error;
      }
    };

    const found = await read();

    if (found !== undefined) {
      return found;
    }

    return this.withLock(club, async () => {
      const again = await read();

      if (again !== undefined) {
        return again;
      }

      const key = randomBytes(32);

      try {
        await this.store.putSnapshot(club, PASS_KEY_DOC, {
          seq: 0,
          publishedAt: this.now().toISOString(),
          body: { key: key.toString("hex") },
        });
      } catch (error) {
        this.log(`[relay] "${club}": could not save its pass key (${String(error)}); signed-in devices will sign in again after a restart.`);
      }

      return key;
    });
  }

  /**
   * The pass this relay hands out for a PIN hash - what a right PIN is
   * answered with. For tests, which can no longer work a pass out from the
   * PIN alone; that is the point.
   */
  async passOf(kind: "staff" | "racer" | "manager" | "signup", club: string, hash: string): Promise<string> {
    return keyedPass(await this.passKeyOf(club), kind, club, hash);
  }

  /**
   * How this club's pages look right now. Never throws: a broken document
   * means the default look, not a broken page.
   *
   * From memory when it can be. Every page, the manifest and the icon ask,
   * none of them needs a password, and reading the site document - logo
   * and all - and the whole check-in roster for each of them was work
   * anybody could make the relay do as fast as they could ask.
   */
  private async lookFor(club: string): Promise<Look> {
    const site = await this.siteOf(club);
    let checkIn = false;
    let listening = false;

    try {
      checkIn = (await this.checkinOf(club))?.enabled === true;
    } catch {
      // As before: a check-in document that cannot be read is check-in closed.
    }

    try {
      listening = await this.listeningOpenFor(club);
    } catch {
      // And the same rule: a switch that cannot be read is a switch that
      // is off. The page then offers nothing, which is the right way for
      // this one to fail - a Listen button that answers nothing is worse
      // than no button.
    }

    // The club's notes and the room's vote from its own pages (2 Nov 2026,
    // docs audit: the app offered both and no page sent either). The same
    // rule again: a switch that cannot be read is off, and Live then offers
    // no form rather than one that answers nothing.
    let notes = false;
    let vote = false;

    try {
      notes = await this.notesOpenFor(club);
    } catch {
      // Off.
    }

    try {
      vote = (await this.roomBallotFor(club)) !== undefined;
    } catch {
      // Off.
    }

    return checkIn === site.look.checkIn && listening === site.look.listening
      && notes === site.look.notes && vote === site.look.vote
      ? site.look
      : { ...site.look, checkIn, listening, notes, vote };
  }

  /**
   * The club's site document, turned into a look, held until the club
   * publishes another. Requests that ask while it is being read share the
   * one read. Never throws.
   */
  private async siteOf(club: string): Promise<SiteEntry> {
    const held = this.sites.get(club);

    if (held !== undefined) {
      return held;
    }

    try {
      return await this.shared(this.siteLoads, club, () => this.siteEpochs.get(club) ?? 0, async () => {
        const entry: SiteEntry = {
          look: lookFrom(await this.store.getSnapshot(club, "site"), undefined),
          card: undefined,
        };
        return entry;
      }, (entry) => this.sites.set(club, entry));
    } catch {
      // Not held as the club's look: a read that failed may work next
      // time, and the next request tries it. But the entry that stands in
      // meanwhile IS held, one per club, because the picture drawn on it
      // is kept on it: a fresh entry each time was a fresh club card drawn
      // for every request while the document would
      // not read - the one path that drew a card with no budget counting
      // it. Only configured clubs get this far, so there is one of these
      // at most for each of them, and forgetSite drops it with the rest.
      let fallback = this.siteFallbacks.get(club);

      if (fallback === undefined) {
        fallback = { look: DEFAULT_LOOK, card: undefined };
        this.siteFallbacks.set(club, fallback);
      }

      return fallback;
    }
  }

  /**
   * One read of a club's document shared by every request that asks while
   * it is out - and kept, by `keep`, only if nothing was written to it
   * meanwhile. A request that arrives after a write starts a read of its
   * own rather than joining one that may have found the old document. A
   * read that fails is kept by nobody, and each request waiting on it
   * gets the error.
   */
  private shared<T>(
    loads: Map<string, Load<T>>,
    club: string,
    epoch: () => number,
    read: () => Promise<T>,
    keep: (value: T) => void,
  ): Promise<T> {
    const at = epoch();
    const running = loads.get(club);

    if (running !== undefined && running.epoch === at) {
      return running.done;
    }

    const done = read();
    const mine: Load<T> = { epoch: at, done };

    loads.set(club, mine);

    done
      .then(
        (value) => {
          if (epoch() === at) {
            keep(value);
          }
        },
        () => undefined,
      )
      .finally(() => {
        if (loads.get(club) === mine) {
          loads.delete(club);
        }
      });

    return done;
  }

  /** Called after every write to a club's site document. */
  private forgetSite(club: string): void {
    this.siteEpochs.set(club, (this.siteEpochs.get(club) ?? 0) + 1);
    this.sites.delete(club);
    this.siteFallbacks.delete(club);

    // The drawn cards too: every one of them is in the colours of the
    // site document that has just been replaced.
    this.forgetCards(club);
  }

  /**
   * What a link to one of this club's sessions may say about it.
   *
   * The id alone does not say which year it was run in, so this reads the
   * newest PREVIEW_YEARS index documents and holds what they say until
   * the club publishes another - one read per publish rather than one per
   * link, which is the difference between a crawler being free and a
   * crawler being a disk. A club with a longer history than that still
   * shares every link; the older ones simply arrive with the club's
   * generic card on them.
   *
   * Never throws. A preview that could not be built is not an error, it
   * is the ordinary results page.
   */
  private async previewsOf(club: string): Promise<ReadonlyMap<string, SessionPreview>> {
    const held = this.previews.get(club);

    if (held !== undefined) {
      // Taken out and put back, so the map's order is the order clubs were
      // last asked about and the first entry is the one to let go.
      this.previews.delete(club);
      this.previews.set(club, held);
      return held.book;
    }

    try {
      return await this.shared(
        this.previewLoads,
        club,
        () => this.previewEpochs.get(club) ?? 0,
        () => this.readPreviews(club),
        (book) => this.keepPreviews(club, book),
      );
    } catch {
      // Not held: a read that failed may work next time, and until then
      // every link to this club is still a link to its results.
      return new Map();
    }
  }

  private async readPreviews(club: string): Promise<ReadonlyMap<string, SessionPreview>> {
    // Newest year first by its own name - a year republished to correct
    // an old result is not suddenly this season - and only a few of them.
    const years = [...(await this.store.itemSizes(club, PREVIEW_CHANNEL))]
      .map((item) => item.id)
      .sort()
      .reverse()
      .slice(0, PREVIEW_YEARS);

    const book = new Map<string, SessionPreview>();

    for (const year of years) {
      if (book.size >= PREVIEW_MAX_SESSIONS) {
        break;
      }

      // A document that will never parse is one this club has no preview
      // from, exactly as it is for the listings; one that could not be
      // read this time throws, and nothing is kept.
      const doc = await this.store.getItem(club, PREVIEW_CHANNEL, year).catch((error: unknown) => {
        if (error instanceof StoreReadError && error.corrupt) {
          return undefined;
        }

        throw error;
      });

      for (const session of previewsIn(doc, PREVIEW_MAX_SESSIONS - book.size)) {
        // Newest year wins an id two years both claim, which is the same
        // way the page resolves one.
        if (!book.has(session.id)) {
          book.set(session.id, session);
        }
      }
    }

    return book;
  }

  /** Holds one club's previews, letting go of the clubs asked about longest ago past PREVIEW_CACHE_MAX_TOTAL - never this one. */
  private keepPreviews(club: string, book: ReadonlyMap<string, SessionPreview>): void {
    this.dropPreviews(club);

    const bytes = previewBytes(book);

    this.previews.set(club, { book, bytes });
    this.previewCacheBytes += bytes;

    for (const [oldest, held] of this.previews) {
      if (this.previewCacheBytes <= PREVIEW_CACHE_MAX_TOTAL || oldest === club) {
        break;
      }

      this.previewCacheBytes -= held.bytes;
      this.previews.delete(oldest);
    }
  }

  private dropPreviews(club: string): void {
    const held = this.previews.get(club);

    if (held !== undefined) {
      this.previewCacheBytes -= held.bytes;
      this.previews.delete(club);
    }
  }

  /** How many clubs' previews are held, and their estimated size. For tests. */
  get previewCache(): { readonly clubs: number; readonly bytes: number } {
    return { clubs: this.previews.size, bytes: this.previewCacheBytes };
  }

  /** Called after every write to a club's index, and when a club deletes everything. */
  private forgetPreviews(club: string): void {
    this.previewEpochs.set(club, (this.previewEpochs.get(club) ?? 0) + 1);
    this.dropPreviews(club);
    this.forgetCards(club);
  }

  /**
   * Every drawn card this club has, thrown away.
   *
   * Called by both the things that can make one wrong: publishing the
   * index, which is what the podium on it comes from, and publishing the
   * site document, which is what its colours come from. The club's own
   * generic card lives on the site entry and goes with that one.
   */
  private forgetCards(club: string): void {
    for (const [key, held] of this.cards) {
      if (key.startsWith(`${club}|`)) {
        this.cardBytes -= held.length;
        this.cards.delete(key);
      }
    }
  }

  private keepCard(key: string, bytes: Buffer): void {
    const old = this.cards.get(key);

    if (old !== undefined) {
      this.cardBytes -= old.length;
      this.cards.delete(key);
    }

    this.cards.set(key, bytes);
    this.cardBytes += bytes.length;

    // Oldest first, the map's order being the order they were drawn in,
    // and never this one: a card evicted the moment it was drawn would
    // be drawn again by the very next request for it.
    for (const [oldest, held] of this.cards) {
      if (this.cardBytes <= CARD_CACHE_MAX_TOTAL || oldest === key) {
        break;
      }

      this.cardBytes -= held.length;
      this.cards.delete(oldest);
    }
  }

  /** What the card cache holds, as counted and as it really is, and how many were drawn rather than served. For tests. */
  get cardCache(): { readonly entries: number; readonly counted: number; readonly held: number; readonly drawn: number } {
    let held = 0;

    for (const bytes of this.cards.values()) {
      held += bytes.length;
    }

    return { entries: this.cards.size, counted: this.cardBytes, held, drawn: this.cardsDrawn };
  }

  /** The card for one session of one club, or undefined for a link the index cannot account for. */
  private async cardFor(tenant: Tenant, sessionId: string, look: Look): Promise<PagePreview | undefined> {
    // Checked before it is looked up, not because the lookup would be
    // unsafe - nothing here touches the disk with it - but because an id
    // this relay could never have stored is an id it has no business
    // repeating back in a link, and og:url is a link.
    if (!isValidItemId(sessionId)) {
      return undefined;
    }

    const session = (await this.previewsOf(tenant.club)).get(sessionId);

    if (session === undefined) {
      return undefined;
    }

    // The drawn picture's address, with a fingerprint of everything on
    // it. Built from the id the index gave rather than the one in the
    // request - they are equal, but only one of them has been through
    // the index - and every character of it is this relay's own.
    const image = `/c/${tenant.club}/results/${session.id}/card.png?v=${cardVersionOf(look, tenant.name, session)}`;

    return previewCard(session, tenant.club, tenant.name, image);
  }

  /**
   * The picture for one shared session: 1200x630, the club's colours,
   * the podium big enough to read without tapping.
   *
   * Never an error and never a 404. A chat app that asks for this has
   * already decided to draw a card; handing it anything but an image
   * leaves a broken picture beside the club's link, and a broken picture
   * it will remember for as long as it caches. So an unknown session, an
   * index that would not read, an id that could never have been one, and
   * a relay that is already drawing as much as it will all end the same
   * way: the club's own card, which is a good-looking picture in its own
   * right.
   */
  private async cardPng(request: IncomingMessage, response: ServerResponse, tenant: Tenant, sessionId: string): Promise<void> {
    // Which versions of the site document and the index this card is
    // being drawn from, noted before anything is read. A correction that
    // lands while this request waits on either read - or on anything else
    // - throws the club's cards away (forgetCards), and a card drawn from
    // what was read before it must not then be put back: it would be the
    // wrong podium, held and handed out until the club published again.
    const siteAt = this.siteEpochs.get(tenant.club) ?? 0;
    const previewAt = this.previewEpochs.get(tenant.club) ?? 0;

    // The site document, not lookFor: the card has no check-in tab on it,
    // and reading a club's whole check-in roster to draw a picture would
    // be the expensive half of this route.
    const site = await this.siteOf(tenant.club);

    // The index before the cache, because the cache is keyed by what the
    // index says (below). Held in memory once read, so this is free for
    // every request but the first after a publish.
    const session = isValidItemId(sessionId) ? (await this.previewsOf(tenant.club)).get(sessionId) : undefined;

    if (session === undefined) {
      // Not a stand-in: this link has no session this relay can find, and
      // the club's card is the right picture for it. Still short-lived,
      // because "cannot find" includes an index that could not be read
      // just now, and the session's own card should replace it as soon
      // as that passes.
      this.sendStandInCard(response, this.clubCardFor(tenant, site));
      return;
    }

    // Keyed by the card's fingerprint as well as the session, so a card is
    // only ever found by a request that would draw exactly that card. The
    // epochs below keep a stale one from being stored; this keeps one from
    // being FOUND, whatever else goes wrong.
    const key = `${tenant.club}|${sessionId}|${cardVersionOf(site.look, tenant.name, session)}`;
    const held = this.cards.get(key);

    if (held !== undefined) {
      sendBody(response, 200, "image/png", held);
      return;
    }

    // A held card costs nothing and is not counted. Only drawing is. By
    // address alone, not club and address: the drawing is this machine's
    // CPU whichever club it is for, so a client walking many clubs' links
    // must not get a fresh allowance with each one.
    const who = clientAddress(request);

    if (this.cardDraws.blocked(who) || this.cardBudget.blocked("all")) {
      // Standing in for a session that has a card of its own, just not one
      // this relay will draw this minute. Sent so that nothing keeps it
      // for long: at an hour, a chat app fetching a freshly shared link
      // while somebody else was walking the club's ids would show the
      // generic picture beside that link long after the budget came back.
      this.sendStandInCard(response, this.clubCardFor(tenant, site));
      return;
    }

    this.cardDraws.hit(who);
    this.cardBudget.hit("all");
    this.cardsDrawn++;

    let bytes: Buffer;

    try {
      bytes = sessionCard(site.look, tenant.name, session);
    } catch (error: unknown) {
      // card.ts is written not to throw on a club's own text, and it is
      // tested against every shape of it there is - but this is the one
      // route where being wrong about that is expensive out of all
      // proportion. An exception here would be a 500, a 500 is a broken
      // image, and a broken image is what the chat app keeps for the
      // next fortnight. So it ends the same way every other failure on
      // this route ends: with the club's own card.
      this.log(`[relay] "${tenant.club}" card for ${sessionId} could not be drawn: ${String(error)}`);
      this.sendStandInCard(response, this.clubCardFor(tenant, site));
      return;
    }

    // Drawing is synchronous, so nothing has changed since the reads
    // above were answered - but those reads were awaits, and a publish
    // may have landed during them.
    if ((this.siteEpochs.get(tenant.club) ?? 0) === siteAt && (this.previewEpochs.get(tenant.club) ?? 0) === previewAt) {
      this.keepCard(key, bytes);
    }

    sendBody(response, 200, "image/png", bytes);
  }

  /**
   * The club's own card sent in place of a session's: cached for a minute
   * rather than sendBody's hour. See cardPng for when, and why.
   */
  private sendStandInCard(response: ServerResponse, bytes: Buffer): void {
    response.writeHead(200, {
      ...BASE_HEADERS,
      "content-type": "image/png",
      "content-length": bytes.length,
      "cache-control": "public, max-age=60",
    });

    response.end(bytes);
  }

  /**
   * The club's own card, drawn once and kept with the look it was drawn
   * from. Not counted against the
   * drawing budget, because it is what the budget falls back TO: counting
   * it would mean a relay under load refusing the only picture it has
   * left to send.
   */
  private clubCardFor(tenant: Tenant, site: SiteEntry): Buffer {
    if (site.card === undefined) {
      this.cardsDrawn++;
      site.card = clubCard(site.look, tenant.name);
    }

    return site.card;
  }

  listen(port: number, host = "0.0.0.0"): Server {
    const server = createServer((request, response) => {
      this.handle(request, response).catch((error: unknown) => {
        // Never let a rejected promise take the process down. A club's
        // results page going blank is bad; the relay exiting takes every
        // club's down at once.
        this.log(`[relay] unhandled: ${String(error)}`);

        if (response.headersSent) {
          response.destroy();
          return;
        }

        // A document that is there but could not be read: say so as
        // "try again", which it may well be, rather than as a fault - and
        // never as "nothing published", which is what it used to become.
        if (error instanceof StoreReadError) {
          sendJson(response, 503, { ok: false, error: "The relay could not read that just now. Try again in a moment." });
          return;
        }

        sendJson(response, 500, { ok: false, error: "Something went wrong in the relay." });
      });
    });

    // Tighter than Node's defaults, which give a client five minutes to
    // finish a request - long enough for a handful of slow connections to
    // hold a small machine's sockets open indefinitely. Nothing honest
    // takes more than a few seconds to send a result or ask for a page.
    //
    // WHY (22 Oct 2026, Bug Hunt 9): an upload from the laptop is the
    // exception - it was first the podium video, up to 48 MB; since 28 Oct
    // 2026 (the videos stay on the laptop) it is a driver's clip (POST
    // .../clip/<id>), up to 4 MB over a track's uplink, which the laptop
    // gives five minutes (RelayPublisher.Uploads.cs). A 30 s server-wide
    // ceiling would cover the whole body, so a slow line would cut it off
    // with a 408. The server-wide ceiling is the laptop's five minutes;
    // handle() puts the 30-second ceiling back on every request,
    // receiveClip lifts it only for an upload that passed its checks and
    // gives that an idle timeout instead, so a slow but steady upload
    // finishes and a stalled one is still dropped.
    server.headersTimeout = 15_000;
    server.requestTimeout = 300_000;
    server.keepAliveTimeout = 5_000;
    server.maxHeadersCount = 100;

    // WHY (22 Oct 2026, Bug Hunt 9): uploads past their week used to be
    // deleted only when the same club uploaded again, so a club that
    // stopped sending kept its last night's files - drivers' names and
    // all - on the disk for good. A sweep now runs a minute after start
    // and then every hour. Both timers are unref'd so they never hold the
    // process open, and they stop when the server closes. It swept the
    // podium videos too until 28 Oct 2026, when they stopped coming here;
    // now only drivers' clips.
    const sweep = (): void => {
      void this.sweepClips().catch(() => 0);
    };
    const firstSweep = setTimeout(sweep, 60_000);
    const hourlySweep = setInterval(sweep, 60 * 60 * 1000);
    firstSweep.unref();
    hourlySweep.unref();
    server.once("close", () => {
      clearTimeout(firstSweep);
      clearInterval(hourlySweep);
    });

    server.listen(port, host);
    return server;
  }

  async handle(request: IncomingMessage, response: ServerResponse): Promise<void> {
    const parsed = parse(request);

    if (parsed === undefined) {
      sendJson(response, 400, { ok: false, error: "Unreadable request." });
      return;
    }

    const { segments } = parsed;

    // WHY (22 Oct 2026, Bug Hunt 9): the server-wide requestTimeout is now
    // five minutes for the laptop's uploads (see listen()), so every request
    // gets the old 30-second ceiling back here: a body still arriving after
    // 30 s is cut off. Only receiveClip and driverPhoto lift it
    // (liftCeiling), and only once an upload has passed every check it can
    // be given before its body arrives - so a POST with no token, or
    // one refused as busy, still cannot trickle for minutes while it is
    // drained. The upload then gets an idle timeout instead.
    const cut = setTimeout(() => {
      if (!request.complete) {
        request.destroy();
      }
    }, 30_000);
    cut.unref();
    request.once("end", () => clearTimeout(cut));
    response.once("close", () => clearTimeout(cut));
    const liftCeiling = (): void => clearTimeout(cut);

    // HEAD is GET without the body, and Node leaves the body off a HEAD
    // answer by itself - so it only has to reach the same routes. Without
    // this a link checker or an uptime monitor asking HEAD was told every
    // page was missing.
    const method = parsed.method === "HEAD" ? "GET" : parsed.method;

    if (method === "OPTIONS") {
      response.writeHead(204, { ...BASE_HEADERS, allow: ALLOWED_METHODS, "content-length": 0 });
      response.end();
      return;
    }

    if (method === "GET" && segments.length === 1 && segments[0] === "healthz") {
      sendJson(response, 200, { ok: true, clubs: this.tenants.count });
      return;
    }

    // Forwarding mode, for an old address after a move: everything goes to
    // the same path on the new one. 308 rather than 301 so a POST stays a
    // POST. request.url is always a path here ("/..."), and it is appended
    // to a fixed origin, so nothing in it can send anyone anywhere else.
    if (this.redirectOrigin !== undefined) {
      const path = (request.url ?? "/").startsWith("/") ? (request.url ?? "/") : "/";
      response.writeHead(308, {
        ...BASE_HEADERS,
        location: this.redirectOrigin + path,
        "cache-control": "public, max-age=3600",
        "content-length": 0,
        connection: "close",
      });
      response.end();
      request.resume();
      return;
    }

    // The public pages, which live outside /api/v1 because they are what
    // gets printed on a poster. Same club rule as everything else: the id
    // is in the path and is validated before it is used.
    if (method === "GET" && segments[0] === "c" && segments.length >= 2) {
      const name = segments[1];

      if (name !== undefined && isValidClubId(name)) {
        const tenant = this.tenants.club(name);

        // A paused club (24 Oct 2026, relay admin): every page, file and
        // icon under its address is the one notice. Its results stay on
        // the volume, untouched, for when it is switched back on - but a
        // club that has stopped paying or asked to stop is not showing
        // them meanwhile. 403 rather than 404: the club exists, and a
        // search engine told "gone" would forget a page that may be back
        // next month.
        if (tenant !== undefined && !isActive(tenant)) {
          const context = await this.contextFor(request, tenant.club);
          sendHtml(response, 403, pausedPage(tenant.club, tenant.name, context), context.nonce);
          return;
        }

        if (tenant !== undefined && segments.length === 2) {
          // The short address is the one people will type, and the one on
          // the poster - so it is a page in its own right that points at
          // everything else, not a redirect to one corner of it.
          const context = await this.contextFor(request, tenant.club);
          sendHtml(response, 200, homePage(tenant.club, tenant.name, context), context.nonce);
          return;
        }

        if (tenant !== undefined && segments.length === 3) {
          const page = segments[2] as string;
          const render = Object.prototype.hasOwnProperty.call(PAGES, page) ? PAGES[page] : undefined;

          if (render !== undefined) {
            const context = await this.contextFor(request, tenant.club);
            sendHtml(response, 200, render(tenant.club, tenant.name, context), context.nonce);
            return;
          }

          if (page === "manifest.webmanifest") {
            const look = await this.lookFor(tenant.club);
            sendBody(response, 200, "application/manifest+json; charset=utf-8", manifest(tenant.club, tenant.name, look));
            return;
          }

          // PUSH ALERTS (6 Nov 2026): the club's service worker, at the
          // club's own address so its scope is this club's pages and no
          // other's. Never cached long: a phone checks it for a new one.
          if (page === "sw.js" && this.push !== undefined) {
            sendBody(response, 200, "text/javascript; charset=utf-8", SERVICE_WORKER_JS, "no-cache");
            return;
          }

          // HeatCaller's sun for every club, logo or not (30 Oct 2026,
          // relay branding, Asher: "synthwave colors for the icon
          // regardless of logo"). The club's logo stays on its pages.
          if (page === "icon.png") {
            sendBody(response, 200, "image/png", sunIcon());
            return;
          }

          if (page === "logo.png") {
            const { look } = await this.siteOf(tenant.club);

            if (look.logo !== undefined) {
              sendBody(response, 200, "image/png", look.logo);
              return;
            }
          }

          // The club's podium videos tab (26 Oct 2026),
          // is gone with the videos (28 Oct 2026, asked for by Asher: they
          // stay on the laptop). It is not in PAGES, so an old bookmark
          // gets the relay's ordinary "not found" below.

          // Not in PAGES on purpose: this is the race-day manager's own
          // page, not one of the club's public tabs, so it never appears
          // in shell()'s nav bar. The manager PIN and the pass it earns
          // are the real lock; this is only obscurity on top of that.
          // The home-screen icon's one bar (1 Nov 2026, see appShellPage).
          // Not in PAGES either: it is never a tab, only what every page
          // opened from the icon goes through first.
          if (page === "app") {
            const context = await this.contextFor(request, tenant.club);
            sendHtml(response, 200, appShellPage(tenant.club, tenant.name, context), context.nonce);
            return;
          }

          if (page === "manage") {
            const context = await this.contextFor(request, tenant.club);
            sendHtml(response, 200, managePage(tenant.club, tenant.name, context), context.nonce);
            return;
          }

          // Driver sign-up. Not in PAGES either, and never in the nav bar
          // or on the home page: nobody can sign up without an invite, and
          // the invite carries the link. Served whether or not sign-up is
          // open - the page asks, and says so - so an old link never 404s.
          if (page === "signup") {
            const context = await this.contextFor(request, tenant.club);
            sendHtml(response, 200, signupPage(tenant.club, tenant.name, context), context.nonce);
            return;
          }

          // A driver's own night. Not in PAGES, for the same reasons again
          // and one more: the page itself is noindex, because there is
          // nothing on it for anybody without the code from their badge.
          //
          // SERVED WHATEVER THE ANSWER WILL BE, and that is not laziness.
          // This route knows nothing about the code - it is never in the
          // address this handler sees when the link is the "#d=" form a
          // fragment is never sent - so the page is the thing that asks,
          // over POST, and the thing that says what came back. Refusing
          // here for a club with the switch off would also hand a stranger
          // a way to learn which clubs offer this, which is the opposite of
          // what readOneNight's single 404 is for.
          if (page === "night") {
            const context = await this.contextFor(request, tenant.club);
            sendHtml(response, 200, nightPage(tenant.club, tenant.name, context), context.nonce);
            return;
          }

          // A driver's own garage (5 Nov 2026) - opened from their night
          // with the same code in the fragment, and served whatever the
          // answer will be for the night page's reasons.
          if (page === "garage") {
            const context = await this.contextFor(request, tenant.club);
            sendHtml(response, 200, garagePage(tenant.club, tenant.name, context), context.nonce);
            return;
          }
        }

        // One session's own address, which exists so that a link to it
        // can carry a card of its own into a group chat. It serves the
        // ordinary results page - the page reads the id out of its own
        // path and opens that session - with the club's index read down
        // to a title and a line of text for its head.
        //
        // A session nobody can find is not an error here: no index, an
        // index this relay could not read, a result the club has since
        // deleted, an id that could never have been one - all of them
        // serve the same page with the club's generic card, and the page
        // itself says the session has gone. A stale link that 404s is a
        // link somebody thinks is broken.
        if (tenant !== undefined && segments.length === 4 && segments[2] === "results") {
          const context = await this.contextFor(request, tenant.club);
          const card = await this.cardFor(tenant, segments[3] as string, context.look);

          sendHtml(
            response,
            200,
            resultsPage(tenant.club, tenant.name, card === undefined ? context : { ...context, preview: card }),
            context.nonce,
          );
          return;
        }

        // The picture that goes with the page above, which is what turns
        // a shared link from a line of text with a small icon into a
        // banner with the podium on it. A path under the session's own
        // address rather than a query on some shared one, so that a chat
        // app caching it by URL caches one session's picture and not
        // another's; ?v= on the end is a fingerprint of what is drawn
        // (see cardVersionOf) and is deliberately ignored here - the
        // answer is always the current card, whatever version was asked
        // for, so a stale link is never a stale picture.
        // THE PODIUM'S VIDEOS (21 Oct to 28 Oct 2026) had two routes here,
        // a race's page (what the Big Screen's QR code opened) and the mp4
        // itself.
        // Both are gone - 28 Oct 2026, asked for by Asher: "Remove the
        // podium videos for downloading from the relay and just have
        // locally." An old QR code or link falls through to the ordinary
        // "not found".

        // A driver's own race clip (22 Oct 2026, driver experience) - named
        // by 128 random bits, listed only on that driver's night. See clips.ts.
        if (tenant !== undefined && segments.length === 4 && segments[2] === "clip") {
          await this.clipFile(request, response, tenant.club, segments[3] as string, parsed.query);
          return;
        }

        if (tenant !== undefined && segments.length === 5 && segments[2] === "results" && segments[4] === "card.png") {
          await this.cardPng(request, response, tenant, segments[3] as string);
          return;
        }

        // Anything else under a club (29 Oct 2026, relay navigation round
        // 1): a page with the club's tabs on it rather than the API's line
        // of JSON, so an old QR code or a typo is not a dead end. Still 404.
        if (tenant !== undefined) {
          const context = await this.contextFor(request, tenant.club);
          sendHtml(response, 404, missingPage(tenant.club, tenant.name, context), context.nonce);
          return;
        }
      }

      // No such club: the same in plain words, with nothing about which
      // clubs do exist.
      sendHtml(response, 404, relayNoticePage(
        "No club here",
        "There's no club at this address. Check the link on the club's poster or in their email.",
      ), randomBytes(16).toString("base64"));
      return;
    }

    // The relay's own address, which somebody will type sooner or later.
    if (method === "GET" && segments.length === 0) {
      sendHtml(response, 200, relayNoticePage(
        "HeatCaller",
        "This is where HeatCaller clubs put their live timing and results. Your club's own page is at the address on its poster.",
      ), randomBytes(16).toString("base64"));
      return;
    }

    // Hear it before you buy it. The one group of routes outside /api/v1
    // that is not a page: a pre-rendered sample of every HeatCaller voice,
    // the list of them, and the Preview button's own render. The first two
    // take no token at all - they are what somebody who has never run
    // HeatCaller tries first, and what a club choosing an announcer's
    // voice compares by - so both are limited per address. See voice.ts.
    if (method === "GET" && segments.length === 2 && segments[0] === "voice" && segments[1] === "samples") {
      await this.voiceSampleList(request, response);
      return;
    }

    if (method === "GET" && segments.length === 3 && segments[0] === "voice" && segments[1] === "sample") {
      await this.voiceSample(request, response, segments[2] as string);
      return;
    }

    if (method === "POST" && segments.length === 2 && segments[0] === "voice" && segments[1] === "preview") {
      await this.voicePreview(request, response);
      return;
    }

    // The lap voice (29 Oct 2026): which clips there are, and each clip.
    // No token and nothing counted - the same stock recordings for every
    // club, made once on the operator's own key (see lapvoice.ts).
    if (method === "GET" && segments.length === 1 && segments[0] === "lap-voice") {
      sendBody(response, 200, "application/json; charset=utf-8", JSON.stringify(this.lapVoice.manifest), "public, max-age=300");
      return;
    }

    if (method === "GET" && segments.length === 2 && segments[0] === "lap-voice") {
      const file = segments[1] as string;
      const clip = file.endsWith(".mp3") ? this.lapVoice.clip(file.slice(0, -4)) : undefined;

      if (clip === undefined) {
        sendJson(response, 404, { ok: false, error: "There is no such clip." });
        return;
      }

      // A year: the page asks with the library's version on the address,
      // so clips made again are new addresses.
      sendBody(response, 200, "audio/mpeg", clip, "public, max-age=31536000, immutable");
      return;
    }

    // Everything else lives under a club. There is no route in this relay
    // that reads or writes anything without naming one, which is what
    // makes "could this have touched another club's data" answerable by
    // looking at the URL. The three above are the exception, and the only
    // one of them that writes anything writes the preview count of the
    // club its own token names - see voicePreview.
    if (segments[0] !== "api" || segments[1] !== "v1" || segments[2] !== "c") {
      noRoute(response, method);
      return;
    }

    const club = segments[3];

    if (club === undefined || !isValidClubId(club)) {
      sendJson(response, 404, { ok: false, error: "No such club." });
      return;
    }

    const known = this.tenants.club(club);

    // Paused from the relay admin app: every route under the club, token
    // or pass or neither, answers this and does nothing - no publish, no
    // voice, no commentary, no check-in, no phone. Said in words the club's
    // laptop can show, so a club that was paused hears why rather than
    // seeing its token suddenly called wrong. The body is read and dropped
    // so a refused upload does not hold the connection open.
    if (known !== undefined && !isActive(known)) {
      request.resume();
      sendJson(response, 403, {
        ok: false,
        paused: true,
        error: "This club is paused on the HeatCaller relay. Ask HeatCaller to switch it back on.",
      });
      return;
    }

    if (known === undefined) {
      // The same answer as a malformed id, which keeps this route from
      // saying more than it must. It does not make club ids secret, and
      // nothing should rely on them being so: /c/<club> serves a page for
      // a club that exists and a 404 for one that does not, and a club
      // that has published anything answers its public channels. Club ids
      // go on posters; they are names, not credentials.
      sendJson(response, 404, { ok: false, error: "No such club." });
      return;
    }

    const rest = segments.slice(4);

    // Written into the API as a refusal rather than left as an absence.
    // A yellow flag thrown over the public internet, arriving whenever it
    // arrives, is a safety problem dressed up as a feature - and an
    // endpoint that answers "no, never" is harder to add by accident than
    // one that merely does not exist yet.
    if (rest[0] === "command") {
      sendJson(response, 403, {
        ok: false,
        error: "Race control is never available over the relay. Flags stay on the venue's own network.",
      });
      return;
    }

    if (method === "POST" && rest[0] === "checkin" && rest.length === 2) {
      await this.checkinRequest(request, response, club, rest[1] as string);
      return;
    }

    if (method === "GET" && rest.length === 1 && rest[0] === "inbox") {
      await this.readInbox(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "inbox" && rest[1] === "ack") {
      await this.ackInbox(request, response, club);
      return;
    }

    // The race-day manager's tunnel: a phone signs in with the manager PIN
    // (never the check-in one), asks and polls through its own pass, and
    // the laptop collects and answers through its publish token - the same
    // four-way shape as check-in's PIN/pass and inbox/ack, kept entirely
    // separate from it end to end.
    if (method === "POST" && rest.length === 2 && rest[0] === "manage" && rest[1] === "signin") {
      await this.manageSignIn(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "manage" && rest[1] === "request") {
      await this.manageRequest(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "manage" && rest[1] === "response") {
      await this.manageAnswerPoll(request, response, club);
      return;
    }

    if (method === "GET" && rest.length === 2 && rest[0] === "manage" && rest[1] === "inbox") {
      await this.readManageInbox(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "manage" && rest[1] === "answer") {
      await this.answerManage(request, response, club);
      return;
    }

    // Driver sign-up: a new driver's phone trades an invite code for a
    // pass, sends its form and polls for the desk's answer; the laptop
    // collects the sign-ups and answers them through its publish token.
    // The same PIN/pass and inbox/answer shape as the two above, and kept
    // apart from both end to end.
    if (method === "POST" && rest.length === 2 && rest[0] === "signup" && rest[1] === "signin") {
      await this.signupSignIn(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "signup" && rest[1] === "status") {
      await this.signupStatusPoll(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "signup" && rest[1] === "submit") {
      await this.signupSubmit(request, response, club);
      return;
    }

    if (method === "GET" && rest.length === 2 && rest[0] === "signup" && rest[1] === "inbox") {
      await this.readSignupInbox(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "signup" && rest[1] === "answer") {
      await this.answerSignup(request, response, club);
      return;
    }

    // Shout-outs for the announcer. Three routes and the same shape as
    // sign-up above: the public one takes a sentence in, and the laptop
    // collects and clears with the publish token it already has. The
    // fourth says whether the club wants them at all.
    if (method === "POST" && rest.length === 2 && rest[0] === "notes" && rest[1] === "send") {
      await this.sendClubNote(request, response, club);
      return;
    }

    if (method === "GET" && rest.length === 2 && rest[0] === "notes" && rest[1] === "inbox") {
      await this.readClubNotes(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "notes" && rest[1] === "collected") {
      await this.clearClubNotes(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "notes" && rest[1] === "open") {
      await this.setClubNotesOpen(request, response, club);
      return;
    }

    // Listening along from home. One route, and the same shape as the
    // notes switch above: the laptop says whether its club wants it, and
    // switching it off clears what is held. The lines themselves arrive
    // through publish/listen like anything else.
    // A DRIVER ASKING FOR THEIR OWN NIGHT. A POST rather than a GET
    // because the badge id goes in the BODY: a query string lands in
    // access logs, in referrers and in a shared browser's history, and
    // this id is the whole credential.
    if (method === "POST" && rest.length === 1 && rest[0] === "night") {
      await this.readOneNight(request, response, club);
      return;
    }

    // PUSH ALERTS (6 Nov 2026) - see push.ts and pushSignUp. The key the
    // browser needs is public; signing a phone up or off takes the badge
    // code, in the body, as reading the night does.
    if (method === "GET" && rest.length === 2 && rest[0] === "push" && rest[1] === "key") {
      if (this.push === undefined) {
        sendJson(response, 404, { ok: false, error: "This relay does not send alerts." });
        return;
      }

      // A key that will not read is not made over (push.ts vapid), so it
      // is said here rather than answered as a fault.
      const key = await this.push.publicKey().catch(() => undefined);

      if (key === undefined) {
        sendJson(response, 503, { ok: false, error: "Alerts are not working on this relay just now. Try again later." });
        return;
      }

      sendJson(response, 200, { ok: true, key });
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "push" && (rest[1] === "subscribe" || rest[1] === "unsubscribe")) {
      await this.pushSignUp(request, response, club, rest[1] === "subscribe");
      return;
    }

    // The podium video upload from the laptop is gone (28 Oct
    // 2026, asked for by Asher: the videos stay on the laptop). An older
    // laptop that still sends one is refused like any unknown address, and
    // the video simply stays on that PC.

    // A driver's own race clip from the laptop (22 Oct 2026, driver
    // experience) - see clips.ts and receiveClip.
    if (method === "POST" && rest.length === 2 && rest[0] === "clip") {
      await this.receiveClip(request, response, club, rest[1] as string, parsed.query, liftCeiling);
      return;
    }

    // A DRIVER ASKING THE DESK (22 Oct 2026, driver experience): the phone
    // sends an ask with its badge code, the laptop collects and confirms
    // with its token - the notes shape - and opens or closes the whole
    // thing. See driverasks.ts. (driver/forget-media, which took a
    // driver's podium videos down, went with the videos on 28 Oct 2026.)
    if (method === "POST" && rest.length === 2 && rest[0] === "driver" && rest[1] === "ask") {
      await this.driverAsk(request, response, club);
      return;
    }

    if (method === "GET" && rest.length === 2 && rest[0] === "driver" && rest[1] === "inbox") {
      await this.readDriverAsks(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "driver" && rest[1] === "collected") {
      await this.clearDriverAsks(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "driver" && rest[1] === "open") {
      await this.setDriverAsksOpen(request, response, club);
      return;
    }

    // A DRIVER'S OWN GARAGE (5 Nov 2026): the phone reads its document and
    // sends car photos; the laptop collects the photos with its token, as
    // it does the asks. See garage.ts.
    if (method === "POST" && rest.length === 2 && rest[0] === "driver" && rest[1] === "garage") {
      await this.readGarage(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "driver" && rest[1] === "photo") {
      await this.driverPhoto(request, response, club, liftCeiling);
      return;
    }

    if (method === "GET" && rest.length === 2 && rest[0] === "driver" && rest[1] === "photos") {
      await this.readDriverPhotos(request, response, club);
      return;
    }

    if (method === "GET" && rest.length === 3 && rest[0] === "driver" && rest[1] === "photo") {
      await this.readDriverPhoto(request, response, club, rest[2] as string);
      return;
    }

    if (method === "POST" && rest.length === 3 && rest[0] === "driver" && rest[1] === "photos" && rest[2] === "collected") {
      await this.clearDriverPhotos(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "nights" && rest[1] === "open") {
      await this.setNightsOpen(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "listen" && rest[1] === "open") {
      await this.setListeningOpen(request, response, club);
      return;
    }

    // The room's vote. Same shape again: the laptop publishes the ballot
    // and reads the votes with the publish token it already has, and the
    // two public routes let anybody with the link see the grid and pick
    // from it. There is no "collected" route - the laptop re-reads the
    // whole set each poll, because a vote can be CHANGED and draining
    // them would lose the change. See readRoomVotes.
    if (method === "POST" && rest.length === 2 && rest[0] === "vote" && rest[1] === "open") {
      await this.setRoomVoteBallot(request, response, club);
      return;
    }

    if (method === "GET" && rest.length === 2 && rest[0] === "vote" && rest[1] === "ballot") {
      await this.roomVoteBallot(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "vote" && rest[1] === "send") {
      await this.sendRoomVote(request, response, club);
      return;
    }

    if (method === "GET" && rest.length === 2 && rest[0] === "vote" && rest[1] === "inbox") {
      await this.readRoomVotes(request, response, club);
      return;
    }

    // The sponsors at the foot of Live (5 Nov 2026): phones say what they
    // saw and tapped, and the laptop reads the counts for its report.
    if (method === "POST" && rest.length === 2 && rest[0] === "sponsors" && rest[1] === "seen") {
      await this.sponsorSeen(request, response, club);
      return;
    }

    if (method === "GET" && rest.length === 2 && rest[0] === "sponsors" && rest[1] === "views") {
      await this.sponsorViews(request, response, club);
      return;
    }

    // The HeatCaller voice: the laptop asks this relay to speak a line, on
    // HeatCaller's ElevenLabs account, with the publish token it already
    // has. The key is here and nowhere else; see voice.ts.
    if (method === "GET" && rest.length === 2 && rest[0] === "voice" && rest[1] === "catalogue") {
      await this.voiceCatalogue(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "voice" && rest[1] === "speak") {
      await this.voiceSpeak(request, response, club);
      return;
    }

    // The commentary brain: the laptop sends the facts it has already
    // worked out and gets back the words. See brain.ts - and note the
    // model is only ever asked to phrase, never to work anything out.
    if (method === "GET" && rest.length === 2 && rest[0] === "brain" && rest[1] === "catalogue") {
      await this.brainCatalogue(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "brain" && rest[1] === "write") {
      await this.brainWrite(request, response, club);
      return;
    }

    // How much of each monthly allowance this club has used, and asking for
    // more - the laptop with its token, the manager's phone with its pass.
    // Read only: nothing here spends a line or a character.
    if (method === "GET" && rest.length === 1 && rest[0] === "usage") {
      await this.usageRead(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "usage" && rest[1] === "topup-request") {
      await this.usageTopUpRequest(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "manage" && rest[1] === "usage") {
      await this.manageUsage(request, response, club);
      return;
    }

    if (method === "POST" && rest.length === 2 && rest[0] === "manage" && rest[1] === "topup-request") {
      await this.manageTopUpRequest(request, response, club);
      return;
    }

    if (method === "POST" && rest[0] === "publish") {
      await this.publish(request, response, parsed, club, rest.slice(1));
      return;
    }

    if (method === "GET" && rest[0] === "snapshot" && rest.length === 2) {
      await this.readSnapshot(response, club, rest[1] as string);
      return;
    }

    if (method === "GET" && rest[0] === "collection" && rest.length === 2) {
      await this.readCollection(request, response, club, rest[1] as string, parsed.query);
      return;
    }

    if (method === "GET" && rest[0] === "collection" && rest.length === 3) {
      await this.readItem(request, response, club, rest[1] as string, rest[2] as string);
      return;
    }

    if (method === "GET" && rest.length === 1 && rest[0] === "whoami") {
      this.whoami(request, response, club);
      return;
    }

    if (method === "DELETE" && rest.length === 0) {
      await this.deleteClub(request, response, club);
      return;
    }

    noRoute(response, method);
  }

  /**
   * Who is this, and are they who they say they are.
   *
   * The two questions are answered together and the second one is the
   * whole of multi-tenancy: a token names exactly one club, so publishing
   * to a club that is not yours is not a permission to be checked against
   * a list, it is a mismatch between two strings.
   */
  private authorise(request: IncomingMessage, club: string): Tenant | "no-token" | "wrong-club" {
    const header = request.headers.authorization ?? "";
    const match = /^Bearer (.+)$/.exec(header.trim());

    if (match === null) {
      return "no-token";
    }

    const tenant = this.tenants.resolveToken(match[1] as string);

    if (tenant === undefined) {
      return "no-token";
    }

    return tenant.club === club ? tenant : "wrong-club";
  }

  private async publish(
    request: IncomingMessage,
    response: ServerResponse,
    parsed: Parsed,
    club: string,
    path: readonly string[],
  ): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      // Logged, because a token being used against the wrong club is
      // either a misconfigured laptop or somebody trying it on, and both
      // are worth seeing.
      this.log(`[relay] refused: a token published to "${club}", which is not its club.`);
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const channelName = path[0];
    const spec = channelName === undefined ? undefined : channelSpec(channelName);

    if (spec === undefined) {
      sendJson(response, 404, { ok: false, error: `No channel called "${channelName ?? ""}".` });
      return;
    }

    const itemId = path[1];

    if (spec.kind === "snapshot" && itemId !== undefined) {
      sendJson(response, 400, {
        ok: false,
        error: `"${spec.name}" holds one current document, so it takes no id.`,
      });
      return;
    }

    if (spec.kind === "collection" && (itemId === undefined || !isValidItemId(itemId))) {
      sendJson(response, 400, {
        ok: false,
        error: `"${spec.name}" holds many documents, so it needs an id - ${spec.what}.`,
      });
      return;
    }

    if (path.length > (spec.kind === "collection" ? 2 : 1)) {
      sendJson(response, 404, { ok: false, error: "No such endpoint." });
      return;
    }

    // A CHANNEL WITH A SWITCH IS REFUSED UNTIL THE CLUB HAS SAID YES - see
    // SWITCHED_CHANNELS for which, and why the WRITE is gated and not only
    // the read.
    //
    // 403 and said plainly, because the laptop has a bug worth seeing if
    // it is sending these at all.
    if (await this.switchedOff(club, spec.name)) {
      sendJson(response, 403, { ok: false, error: refusalFor(spec.name) });
      return;
    }

    // A snapshot's own maxItemBytes caps it too (22 Oct 2026, driver
    // experience) - see ChannelSpec.maxItemBytes.
    const limit = Math.min(
      this.maxBodyBytes,
      spec.maxItemBytes ?? (spec.kind === "collection" ? COLLECTION_ITEM_MAX_BYTES : this.maxBodyBytes),
    );
    const body = await readBody(request, response, limit);

    if (body === "too-big") {
      sendJson(response, 413, {
        ok: false,
        error:
          spec.kind === "collection" || spec.maxItemBytes !== undefined
            ? `One "${spec.name}" document may be at most ${limit} bytes, and this one is larger.`
            : `That payload is larger than this relay accepts (${limit} bytes).`,
      });
      return;
    }

    if (body === "unreadable") {
      sendJson(response, 400, { ok: false, error: "The body was not valid JSON." });
      return;
    }

    const seq = readSeq(parsed.query.get("seq"));

    if (seq === undefined) {
      sendJson(response, 400, { ok: false, error: "seq must be a whole number, zero or more." });
      return;
    }

    const outcome = await this.withLock(club, async () => {
      // A document already there that cannot be read is written over, as
      // it always was: a publish is how a club puts a broken one right.
      // Only the order check below is lost for it.
      let unreadable = false;
      const existing = await (spec.kind === "snapshot"
        ? this.store.getSnapshot(club, spec.name)
        : this.store.getItem(club, spec.name, itemId as string)
      ).catch((error: unknown) => {
        if (!(error instanceof StoreReadError)) {
          throw error;
        }

        unreadable = true;
        return undefined;
      });

      // Out of order, so the one already here is the newer of the two.
      // Answered with a 200 rather than a conflict: nothing went wrong, a
      // frame simply lost a race it was allowed to lose, and a publisher
      // that treated this as an error would retry a stale frame forever.
      if (existing !== undefined && seq > 0 && existing.seq >= seq) {
        return { applied: false, seq: existing.seq };
      }

      const doc: StoredDocument = {
        seq,
        publishedAt: this.now().toISOString(),
        // Heat line-ups are the roster split by heat, so they follow the
        // relay's roster switch too - taken out here, so the names never
        // reach the disk. See withoutLineups.
        body: spec.name === "programme" && who.publishRoster !== true ? withoutLineups(body) : body,
      };

      if (spec.kind === "snapshot") {
        // A switched snapshot is checked again inside the lock, as a
        // collection is below (22 Oct 2026, driver experience): the drivers'
        // live document is the first switched snapshot, and a close landing
        // while it arrived would otherwise be undone by it.
        if (SWITCHED_CHANNELS.has(spec.name) && (await this.switchedOff(club, spec.name))) {
          return "closed" as const;
        }

        await this.store.putSnapshot(club, spec.name, doc);

        // The copy readSnapshot holds, and any read of it in flight.
        this.forgetListing(club, spec.name);

        if (spec.name === DRIVER_LIVE_CHANNEL || spec.name === DRIVER_ME_CHANNEL) {
          this.forgetDriverDoc(club, spec.name);
        }

        // Push alerts (6 Nov 2026): what changed for a driver signed up.
        // Not waited for - it posts to the push services, and the laptop's
        // publish is answered without it. afterLive never throws.
        if (spec.name === DRIVER_LIVE_CHANNEL && this.push !== undefined && !unreadable) {
          void this.push.afterLive(club, existing?.body, body);
        }

        if (spec.name === "checkin") {
          this.forgetCheckin(club);

          // A new staff PIN makes every wrong guess so far a guess at a
          // PIN that no longer exists - so the club-wide count starts
          // again, and a club whose list somebody has locked can reopen
          // it by changing the PIN. Only a change: the laptop republishes
          // this document every few seconds with the same PIN in it.
          // Not when the old document could not be read: whether the PIN
          // changed is then unknown, and a lockout is not lifted on a guess.
          const before = staffPinOf(existing?.body);
          const after = staffPinOf(body);
          // 17 Oct 2026, Bug Hunt 8, A19-3: compare against the last real
          // PIN, not an empty one published while check-in was off.
          const known = this.lastStaffPin.get(club) ?? before;

          if (!unreadable && after !== "" && after !== known) {
            await this.pinGuard.reset(club);
          }

          if (after !== "") {
            this.lastStaffPin.set(club, after);
          }
        }

        if (spec.name === "site") {
          this.forgetSite(club);
        }

        if (spec.name === "manage") {
          // A new manager PIN makes every wrong guess so far a guess at a
          // PIN that no longer exists, so its own club-wide count starts
          // again - exactly why a changed check-in PIN resets pinGuard,
          // above. Turning the manager off is not a PIN change and resets
          // nothing: a manager who guessed wrong minutes ago should not
          // find the count cleared just because the desk unticked a box.
          // Nor is turning it back on with the same PIN: the comparison is
          // with the last real PIN, not the empty one published while it
          // was off (17 Oct 2026, Bug Hunt 8, A19-3).
          const before = managePinOf(existing?.body);
          const after = managePinOf(body);
          const known = this.lastManagePin.get(club) ?? before;

          if (!unreadable && after !== "" && after !== known) {
            await this.managePinGuard.reset(club);
          }

          if (after !== "") {
            this.lastManagePin.set(club, after);
          }
        }

        if (spec.name === "signup") {
          // The same idea for invite codes, which come and go rather than
          // change: when not one of the codes the relay now holds was
          // among those it held before - the desk cancelled every open
          // invite and made new ones - every wrong guess so far was a guess
          // at codes that no longer exist, and sign-up's own count starts
          // again. That is how the desk reopens code sign-in after a
          // lockout, as the laptop's Online Sign-ups window tells it to.
          // One new invite beside the old ones resets nothing: the old
          // codes are still there to be guessed at. Turning sign-up off and
          // on again also takes every code off and puts them back, which
          // resets it too - the desk's own action either way, and never a
          // phone's.
          const before = inviteHashesOf(existing?.body);
          const after = inviteHashesOf(body);

          if (!unreadable && after.size > 0 && ![...after].some((h) => before.has(h))) {
            await this.signupPinGuard.reset(club);
          }
        }
      } else {
        // Read again inside the lock: the club may have switched this
        // channel off while the document was arriving, and that switch
        // takes down everything held under this same lock. Written after
        // it, this document would sit on a relay the club has just said no
        // to.
        if (await this.switchedOff(club, spec.name)) {
          return "closed" as const;
        }

        const victims = await this.planRoom(spec, club, itemId as string, Buffer.byteLength(JSON.stringify(doc)));

        if (victims === undefined) {
          return "full" as const;
        }

        // The new one written first, the old ones deleted after. The other
        // way round, a write that failed - a full disk, most likely - had
        // already cost the club its oldest results for nothing.
        await this.store.putItem(club, spec.name, itemId as string, doc);
        this.forgetListing(club, spec.name);

        if (spec.name === PREVIEW_CHANNEL) {
          this.forgetPreviews(club);
        }

        // Push alerts (6 Nov 2026): a race newly on a driver's night is
        // their result in. Not waited for, and never throws.
        if (spec.name === NIGHTS_CHANNEL && this.push !== undefined && !unreadable) {
          void this.push.afterNight(club, itemId as string, existing?.body, body);
        }

        await this.prune(club, spec.name, victims);
      }

      return { applied: true, seq };
    });

    // The same refusal as before the body was read. See there.
    if (outcome === "closed") {
      sendJson(response, 403, { ok: false, error: refusalFor(spec.name) });
      return;
    }

    // Its own budget, not the club's quota, is what a channel like the
    // results index runs out of - and only later documents fill it.
    if (outcome === "full" && spec.ownBudget !== undefined) {
      this.log(`[relay] "${club}": "${spec.name}" is full of later documents; "${itemId ?? ""}" was refused.`);
      sendJson(response, 507, {
        ok: false,
        error: `"${spec.name}" is holding all it may (${spec.ownBudget} bytes) with documents later than "${itemId ?? ""}", so that one cannot be kept.`,
      });
      return;
    }

    if (outcome === "full") {
      this.log(`[relay] "${club}" is at its storage quota; a "${spec.name}" document was refused.`);
      sendJson(response, 507, {
        ok: false,
        error: `This club is holding all the relay allows it (${CLUB_MAX_BYTES} bytes), so "${spec.name}" cannot take another document.`,
      });
      return;
    }

    sendJson(response, 200, { ok: true, ...outcome });
  }

  /**
   * Which documents in a collection must go to make room for one more,
   * oldest first - or undefined when it would not fit even with the whole
   * collection gone (for a channel with a budget of its own, with every
   * EARLIER document gone), and the caller answers 507.
   *
   * Two limits: a count, so a listing stays something a phone can fetch,
   * and the club's quota, so a disk shared by every club cannot be filled
   * by one. Only collections are held to the quota. They are the only
   * thing that grows - a snapshot replaces itself - and refusing a live
   * frame would stop the timing page mid-race over a disk that results
   * filled, which is the wrong thing to stop.
   *
   * Only a plan: nothing is deleted here. The caller writes the new
   * document first and then calls prune, so a write that fails loses
   * nothing. For that moment the club holds up to one document more than
   * its limits, which the next publish would put right in any case.
   * Called inside the club's lock.
   */
  private async planRoom(spec: ChannelSpec, club: string, id: string, bytes: number): Promise<string[] | undefined> {
    const channel = spec.name;
    const maxItems = spec.maxItems ?? Number.POSITIVE_INFINITY;

    const [items, used, tunnel, signups, votes, notes, inbox, asks, photos, pushSubs] = await Promise.all([
      this.store.itemSizes(club, channel),
      this.store.usage(club),
      this.store.snapshotBytes(club, MANAGE_QUEUE),
      this.store.snapshotBytes(club, SIGNUP_BOX),
      this.store.itemSizes(club, VOTE_CHANNEL),
      this.store.itemSizes(club, NOTES_CHANNEL),
      this.store.snapshotBytes(club, INBOX),
      // Drivers' asks are written by phones too (22 Oct 2026, driver
      // experience), with a ceiling of their own (DRIVER_ASKS_PER_CLUB).
      this.store.itemSizes(club, DRIVER_ASKS_CHANNEL),
      // And their car photos on the way to the laptop (5 Nov 2026), with
      // a ceiling of their own (DRIVER_PHOTOS_PER_CLUB).
      this.store.itemSizes(club, DRIVER_PHOTOS_CHANNEL),
      // And phones signed up for alerts (7 Nov 2026, Bug Hunt 13): written
      // by phones, with a ceiling of their own (PUSH_SUBS_PER_CLUB).
      this.store.itemSizes(club, PUSH_CHANNEL),
    ]);
    const replaced = items.find((item) => item.id === id);
    const others = items.filter((item) => item.id !== id).sort((a, b) => a.at - b.at);

    // A channel with a budget of its own is held to THAT and to nothing
    // else. It is not counted against the club's quota, so the quota
    // cannot evict it, and it cannot evict anything outside itself - see
    // ChannelSpec.ownBudget for what that was costing.
    if (spec.ownBudget !== undefined) {
      let mine = others.reduce((sum, item) => sum + item.bytes, 0) + bytes;
      let mineCount = others.length + 1;
      const mineVictims: string[] = [];

      // A ROLLING CHANNEL EVICTS BY AGE AND HONOURS ITS COUNT. Everything
      // below this is written for resultsindex, whose ids are calendar
      // years, and neither half of it generalises - see
      // ChannelSpec.rollOldest. `others` is already sorted oldest-written
      // first, every one of them is a candidate, and maxItems is a real
      // limit here rather than one the branch never reaches.
      if (spec.rollOldest === true) {
        for (const item of others) {
          if (mineCount <= maxItems && mine <= spec.ownBudget) {
            break;
          }

          mineVictims.push(item.id);
          mine -= item.bytes;
          mineCount--;
        }

        // Still over after dropping every other item means this one
        // document is bigger than the whole budget, which maxItemBytes
        // should already have refused. Refusing beats keeping nothing but
        // it.
        return mine > spec.ownBudget ? undefined : mineVictims;
      }

      // Oldest by id, not by when it was written. The only such channel is
      // resultsindex, keyed by calendar year, and its promise is that the
      // oldest YEAR makes way. `at` is the file's last write, and the
      // laptop republishes an old year whenever one of its sessions is
      // edited or deleted - so ordering by it made 2019 the newest the
      // moment a stray 2019 heat was deleted, and the next overflow took
      // 2021 instead, a season the laptop never knew to send again.
      // Four-digit years sort as text the way they do as numbers.
      //
      // And only years OLDER than the one being published. `others` leaves
      // the incoming id out, so with nothing more said, republishing 2025
      // after an edit evicted 2026 - the current season - and the next
      // 2026 publish evicted 2025 back. Newer years are never pushed out
      // by an older one: if the older years cannot make room, the
      // incoming document is the one refused.
      const byAge = others
        .filter((item) => item.id < id)
        .sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : a.at - b.at));

      for (const item of byAge) {
        if (mine <= spec.ownBudget) {
          break;
        }

        mineVictims.push(item.id);
        mine -= item.bytes;
      }

      return mine > spec.ownBudget ? undefined : mineVictims;
    }

    // The race-day manager's tunnel is left out of the count. It used to be
    // counted like anything else, so a busy manager - or anyone holding the
    // manager PIN - could fill the club's quota with requests, and the
    // first anyone knew of it was a result, nothing to do with any of them,
    // pushing out older results to make room or being refused with a 507.
    // The tunnel has a budget of its own instead (MANAGE_PENDING_MAX_BYTES
    // and MANAGE_ANSWERS_MAX_BYTES, a little over five megabytes between
    // them) - a manager whose requests fill it is told so - and nothing a
    // manager asks for can cost a result anything. Measured inside the
    // club's lock, which every write to the tunnel also takes, so it is the
    // same file usage() just counted.
    // Anything with a budget of its own comes off the club's usage for
    // the same reason the tunnel does: it was never the club's quota to
    // spend, so it must not be what pushes a result out either.
    // Driver sign-ups are left out for the tunnel's reason too: they are
    // written by phones, not by the laptop, and a phone must never be able
    // to cost a result anything. They have their own ceiling instead -
    // SIGNUP_MAX_ITEMS, each needing an invite only the desk can make.
    // Room votes, race notes and the check-in inbox are written by phones
    // too, so they come off for the same reason; each has a ceiling of its
    // own (VOTE_CHANNEL, NOTES_HELD_MOST, INBOX_MAX_ITEMS). 17 Oct 2026,
    // Bug Hunt 8, A20-2.
    const offQuota = await this.offQuotaBytes(club);
    const phoneWritten =
      votes.reduce((sum, item) => sum + item.bytes, 0) + notes.reduce((sum, item) => sum + item.bytes, 0) + inbox +
      asks.reduce((sum, item) => sum + item.bytes, 0) + photos.reduce((sum, item) => sum + item.bytes, 0) +
      pushSubs.reduce((sum, item) => sum + item.bytes, 0);

    let total = used - tunnel - signups - phoneWritten - offQuota - (replaced?.bytes ?? 0) + bytes;
    let count = others.length + 1;
    const victims: string[] = [];

    for (const item of others) {
      if (count <= maxItems && total <= CLUB_MAX_BYTES) {
        break;
      }

      victims.push(item.id);
      total -= item.bytes;
      count--;
    }

    if (total > CLUB_MAX_BYTES) {
      return undefined;
    }

    return victims;
  }

  /** How many of a club's stored bytes belong to channels with a budget of their own, and so are not the quota's business. */
  private async offQuotaBytes(club: string): Promise<number> {
    let total = 0;

    for (const spec of Object.values(CHANNELS)) {
      if (spec.ownBudget === undefined) {
        continue;
      }

      for (const item of await this.store.itemSizes(club, spec.name)) {
        total += item.bytes;
      }
    }

    return total;
  }

  /**
   * Deletes what planRoom picked, once the new document is safely written.
   * A delete that fails is logged, not thrown: the publish it follows has
   * already succeeded, and the next publish plans again from what is
   * really on disk.
   */
  private async prune(club: string, channel: string, victims: readonly string[]): Promise<void> {
    if (victims.length === 0) {
      return;
    }

    for (const victim of victims) {
      try {
        await this.store.deleteItem(club, channel, victim);
      } catch (error) {
        this.log(`[relay] "${club}": could not delete an old "${channel}" document (${String(error)}).`);
      }
    }

    this.forgetListing(club, channel);
  }

  /**
   * A listing's cache key. The meta listing - ids and sizes, no bodies -
   * is a second listing of the same collection, so it gets a key of its
   * own and is invalidated by the same publish.
   */
  private listingKey(club: string, channel: string, meta: boolean): string {
    return meta ? `${club}/${channel}#meta` : `${club}/${channel}`;
  }

  private forgetListing(club: string, channel: string): void {
    // BOTH of them. The meta listing is built from the same files, so a
    // publish that changes the collection makes it just as wrong - and a
    // held one that was never invalidated would go on saying a result is
    // not there long after it arrived.
    for (const key of [this.listingKey(club, channel, false), this.listingKey(club, channel, true)]) {
      this.listingGens.set(key, (this.listingGens.get(key) ?? 0) + 1);

      const held = this.listings.get(key);

      if (held !== undefined) {
        this.listingBytes -= held.length;
        this.listings.delete(key);
      }
    }

    // And every one of its documents held whole (see readItem), which the
    // same publish may just have replaced. Their generation is the
    // collection's own listing's, moved on above, so one being read right
    // now is not kept either.
    const documents = `${club}/${channel}/`;

    for (const [key, held] of this.listings) {
      if (key.startsWith(documents)) {
        this.listingBytes -= held.length;
        this.listings.delete(key);
      }
    }
  }

  private forgetClubListings(club: string): void {
    this.listingEpoch++;

    for (const [key, held] of this.listings) {
      if (key.startsWith(`${club}/`)) {
        this.listingBytes -= held.length;
        this.listings.delete(key);
      }
    }
  }

  /** Which version of a collection a listing would be built from now. */
  private listingGeneration(key: string): string {
    return `${this.listingEpoch}/${this.listingGens.get(key) ?? 0}`;
  }

  private keepListing(key: string, bytes: Buffer): void {
    // Out before in, and its size with it: a listing replacing one of its
    // own used to be counted twice, and the count - never the memory -
    // grew until it pushed every other club's listing out.
    const old = this.listings.get(key);

    if (old !== undefined) {
      this.listingBytes -= old.length;
      this.listings.delete(key);
    }

    if (bytes.length > LISTING_CACHE_MAX_TOTAL) {
      return;
    }

    this.listings.set(key, bytes);
    this.listingBytes += bytes.length;

    // Oldest first: the map's order is the order they were built in.
    for (const [old, held] of this.listings) {
      if (this.listingBytes <= LISTING_CACHE_MAX_TOTAL || old === key) {
        break;
      }

      this.listingBytes -= held.length;
      this.listings.delete(old);
    }
  }

  private async readSnapshot(response: ServerResponse, club: string, channel: string): Promise<void> {
    const spec = channelSpec(channel);

    // A private channel is answered exactly as a missing one is.
    if (spec === undefined || spec.kind !== "snapshot" || spec.private === true) {
      sendJson(response, 404, { ok: false, error: `No snapshot channel called "${channel}".` });
      return;
    }

    const rosterAllowed = this.tenants.club(club)?.publishRoster === true;

    if (channel === "roster" && !rosterAllowed) {
      sendJson(response, 403, {
        ok: false,
        error: "This club does not publish its roster.",
      });
      return;
    }

    // Held once read, in the listing cache, until the club publishes this
    // channel again (forgetListing) - and requests that arrive together
    // share one read. It used to be read, parsed and serialised afresh for
    // every request: the live board polls "state" every two seconds from
    // every phone, and "badges" carries the artwork, up to the whole body
    // limit - fifty people opening the badges page at once was fifty copies
    // of it in a 256 MB machine, for anyone with no password at all.
    const key = this.listingKey(club, channel, false);
    const held = this.listings.get(key);

    if (held !== undefined) {
      sendPublicBytes(response, 200, held);
      return;
    }

    const bytes = await this.buildOnce(
      key,
      key,
      async () => {
        const doc = await this.store.getSnapshot(club, channel);

        if (doc === undefined) {
          return undefined;
        }

        // And again here, for a programme stored before publish took them
        // out: the laptop only resends it when it changes, so a club
        // between meetings would have gone on serving the old one's names.
        const served = channel === "programme" && !rosterAllowed ? { ...doc, body: withoutLineups(doc.body) } : doc;

        return Buffer.from(JSON.stringify(served), "utf8");
      },
      false,
    );

    if (bytes === undefined) {
      sendJson(response, 404, { ok: false, error: `Nothing has been published to "${channel}" yet.` });
      return;
    }

    sendPublicBytes(response, 200, bytes);
  }

  /**
   * A collection, three ways.
   *
   *   (nothing)  every document that fits in LISTING_MAX_BYTES, newest first
   *   ?meta=1    ids, times and sizes - no bodies at all
   *   ?ids=a,b   just those documents, in the order asked for
   *
   * The last two exist because a collection is no longer capped at three
   * hundred documents. A club with years of results has a listing far
   * bigger than any page wants to draw, and a phone on club wi-fi should
   * not fetch a season to show a day of it. With these, a page reads the
   * index the club publishes, decides what it is about to show, and asks
   * for exactly that.
   *
   * NONE of them looks inside a document. The relay still does not know
   * what a result is, which is the rule that keeps a column added in the
   * desktop app from being a relay deploy.
   */
  private async readCollection(
    request: IncomingMessage,
    response: ServerResponse,
    club: string,
    channel: string,
    query: URLSearchParams,
  ): Promise<void> {
    const spec = channelSpec(channel);

    // A PRIVATE COLLECTION IS ANSWERED AS A MISSING ONE, exactly as
    // readSnapshot has always answered a private snapshot (13 Oct 2026).
    //
    // IT WAS NOT CHECKED HERE AT ALL, and that was a hole waiting for the
    // first private collection to be added. Nothing leaked, because every
    // private channel until then - checkin, manage, signup - is a
    // SNAPSHOT, and readSnapshot has always honoured the flag. But
    // ChannelSpec.private read as a promise the registry made and this
    // reader did not keep, so the first per-person collection anybody
    // added would have been served whole, to anybody, with no token.
    //
    // And worse than its bodies: ?meta=1 lists every id in a collection.
    // The nights channel is keyed by the badge ids from
    // RelayCheckIn.IdFor, so a listing would have turned eighty
    // unguessable bits into a public list - and those same ids are what
    // let somebody be checked in, so it would have handed over check-in
    // as well.
    if (spec === undefined || spec.kind !== "collection" || spec.private === true) {
      sendJson(response, 404, { ok: false, error: `No collection called "${channel}".` });
      return;
    }

    const asked = query.get("ids");

    if (asked !== null) {
      await this.readSomeItems(request, response, club, channel, asked);
      return;
    }

    const meta = query.get("meta") === "1";

    // ?meta=1&limit=N: only the newest N. The home page polls for its
    // three latest results, and a club with years of them has a meta
    // listing of hundreds of kilobytes - every phone, every fifteen
    // seconds. Cut from the held meta listing, so it costs no disk read.
    const rawLimit = query.get("limit");
    let limit: number | undefined;

    if (rawLimit !== null) {
      limit = Number(rawLimit);

      if (!meta || !Number.isInteger(limit) || limit < 1) {
        sendJson(response, 400, { ok: false, error: '"limit" must be a whole number of 1 or more, and goes with "meta=1".' });
        return;
      }
    }

    const key = this.listingKey(club, channel, meta);
    const held =
      this.listings.get(key) ??
      ((await this.buildOnce(key, key, () => (meta ? this.buildMetaListing(club, channel) : this.buildListing(club, channel)))) as Buffer);

    if (limit === undefined) {
      sendPublicBytes(response, 200, held);
      return;
    }

    // 17 Oct 2026, Bug Hunt 8, A19-1: the cut is parsed once per listing,
    // not once per request. A limit too big to keep is charged like any
    // other read that does real work, so it cannot be used to make the
    // relay re-parse a large listing as often as a script likes.
    let cuts = this.metaCuts.get(held);
    const kept = cuts?.get(limit);

    if (kept !== undefined) {
      sendPublicBytes(response, 200, kept);
      return;
    }

    if (limit > META_LIMIT_CACHED) {
      const who = clientAddress(request);

      if (this.collectionReads.blocked(who)) {
        sendJson(response, 429, { ok: false, error: "Too many requests. Wait a minute and try again." });
        return;
      }

      this.collectionReads.hit(who);
      sendPublicBytes(response, 200, newestOfMeta(held, limit));
      return;
    }

    const cut = newestOfMeta(held, limit);

    if (cuts === undefined) {
      cuts = new Map<number, Buffer>();
      this.metaCuts.set(held, cuts);
    }

    cuts.set(limit, cut);
    sendPublicBytes(response, 200, cut);
  }

  /**
   * Builds something to go in the listing cache - a listing, or one
   * document held whole (see readItem) - once, however many requests ask
   * for it together, and keeps it only if its collection was not written
   * to meanwhile.
   *
   * `generationKey` is the listing whose generation says whether what was
   * built is still current. For a listing it is its own key; for one
   * document it is its collection's listing, which every publish to that
   * collection moves on. A build that answers undefined - a document that
   * is not there - is shared with whoever was waiting but not kept.
   */
  private async buildOnce(
    key: string,
    generationKey: string,
    make: () => Promise<Buffer | undefined>,
    /** False for one snapshot: a single read, which must not wait behind a big listing's build. */
    queued = true,
  ): Promise<Buffer | undefined> {
    // Noted before the read: a publish landing while the files are read
    // would make what was read out of date, and it must not then be kept
    // - nor waited on by a request that arrived after that publish.
    const generation = this.listingGeneration(generationKey);
    let build = this.listingBuilds.get(key);

    if (build === undefined || build.generation !== generation) {
      const done = queued ? this.inBuildSlot(make) : make();
      const mine: ListingBuild = { generation, done };

      build = mine;
      this.listingBuilds.set(key, mine);

      done
        .then(
          (bytes) => {
            if (bytes !== undefined && this.listingGeneration(generationKey) === generation) {
              this.keepListing(key, bytes);
            }
          },
          () => undefined,
        )
        .finally(() => {
          if (this.listingBuilds.get(key) === mine) {
            this.listingBuilds.delete(key);
          }
        });
    }

    return build.done;
  }

  /**
   * What is in a collection, without any of it.
   *
   * One id, time and size per document, newest first, and nothing is read
   * or parsed - itemSizes is a directory listing. That is the whole point:
   * a club with five seasons of results can say what it has in a few
   * kilobytes, and the page then asks for the handful it is about to draw.
   *
   * Held in the same cache as the full listing and thrown away by the
   * same publish, so it can never say a result is missing after it lands.
   */
  private async buildMetaListing(club: string, channel: string): Promise<Buffer> {
    const sizes = [...(await this.store.itemSizes(club, channel))].sort((a, b) => b.at - a.at);

    const items = sizes.map((item) => ({
      id: item.id,
      publishedAt: new Date(item.at).toISOString(),
      bytes: item.bytes,
    }));

    return Buffer.from(JSON.stringify({ items, meta: true }), "utf8");
  }

  /**
   * The documents a page names, in the order it named them.
   *
   * Capped at ITEMS_BY_ID_MAX ids and ITEMS_BY_ID_MAX_BYTES of answer:
   * this is the one read a caller chooses the size of, and "give me every
   * id you can think of" must not be a way to make the relay read a whole
   * collection for one request. An id that is unknown, unreadable or not a
   * usable id at all is simply absent from the answer - a page asking for
   * ten sessions and getting nine draws nine, which is what it should do
   * with a result somebody deleted in between anyway.
   *
   * Not cached - it varies with whatever was asked for - so what bounds it
   * is everything else: a turn among ITEMS_BY_ID_AT_ONCE, an allowance per
   * address, and an answer of at most a megabyte. Before those, twenty of
   * these at once from anywhere at all were three quarters of a gigabyte.
   *
   * "omitted" is how many of the ids asked for were not read because the
   * answer was already full: distinct, usable ids from the one that did
   * not fit onwards. It does NOT count repeats, ids no collection could
   * hold, or documents that are not there - none of those would arrive by
   * asking again, and a page told they were waiting would ask forever.
   * Being cut off always happens at one place in the list, so a page that
   * sees "omitted" asks again for everything after the last document it
   * was given. An answer always carries at least one document when any is
   * there, so that asking again always gets somewhere.
   */
  private async readSomeItems(
    request: IncomingMessage,
    response: ServerResponse,
    club: string,
    channel: string,
    asked: string,
  ): Promise<void> {
    // Guarded again, though readCollection is its only caller and refuses
    // a private channel before reaching here. A reader that is correct
    // only because of who happens to call it is one edit away from not
    // being - and this one hands back bodies by id, which is exactly what
    // a private collection must not do.
    if (channelSpec(channel)?.private === true) {
      sendJson(response, 404, { ok: false, error: `No collection called "${channel}".` });
      return;
    }

    const wanted = asked
      .split(",")
      .map((id) => id.trim())
      .filter((id) => id.length > 0);

    if (wanted.length === 0) {
      sendJson(response, 400, { ok: false, error: '"ids" was empty. Give it a comma-separated list of document ids.' });
      return;
    }

    if (wanted.length > ITEMS_BY_ID_MAX) {
      sendJson(response, 400, {
        ok: false,
        error: `"ids" may name at most ${ITEMS_BY_ID_MAX} documents at a time; this asked for ${wanted.length}.`,
      });
      return;
    }

    const who = clientAddress(request);

    if (this.collectionReads.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a minute and try again." });
      return;
    }

    this.collectionReads.hit(who);

    // Repeats and ids no collection could hold, gone before anything is
    // read, so what is left is exactly what "omitted" counts from.
    const ids = [...new Set(wanted)].filter((id) => isValidItemId(id));

    const json = await this.inItemReadSlot(() => this.itemsById(club, channel, ids));

    if (json === undefined) {
      sendJson(response, 503, { ok: false, error: "The relay is busy. Try again in a moment." });
      return;
    }

    sendPublicText(response, 200, json);
  }

  /** The answer readSomeItems sends, as text - see there. */
  private async itemsById(club: string, channel: string, ids: readonly string[]): Promise<string> {
    // Each document's serialised form, never its parsed body: what this
    // holds at once is the answer and one document, as buildListing does.
    const texts: string[] = [];
    let total = 0;
    let omitted = 0;

    for (let i = 0; i < ids.length; i++) {
      const id = ids[i] as string;

      // Same rule as the listing: a file that will never parse is left
      // out, one that could not be read this time fails the request. A
      // page that quietly drew nine of ten results because the disk
      // hiccuped would be a page that lies.
      const doc = await this.store.getItem(club, channel, id).catch((error: unknown) => {
        if (error instanceof StoreReadError && error.corrupt) {
          return undefined;
        }

        throw error;
      });

      if (doc === undefined || typeof doc.publishedAt !== "string") {
        continue;
      }

      const text = JSON.stringify({ id, publishedAt: doc.publishedAt, body: doc.body });
      const bytes = Buffer.byteLength(text);

      // Full: this one and every usable id after it are left for the page
      // to ask for again. Never the first, or a document bigger than the
      // cap on its own - a year's index can be a megabyte - could never
      // be fetched this way at all.
      if (texts.length > 0 && total + bytes > ITEMS_BY_ID_MAX_BYTES) {
        omitted = ids.length - i;
        break;
      }

      texts.push(text);
      total += bytes;
    }

    // By hand, as buildListing writes its own: the same text
    // JSON.stringify({ items, omitted }) would give, without parsing
    // every document back to make it.
    return `{"items":[${texts.join(",")}]${omitted > 0 ? `,"omitted":${omitted}` : ""}}`;
  }

  /**
   * Runs one ?ids= read when there is room among ITEMS_BY_ID_AT_ONCE,
   * queues it when there is not, and answers undefined without running it
   * when the queue is full too. The same hand-over as inBuildSlot.
   */
  private async inItemReadSlot<T>(work: () => Promise<T>): Promise<T | undefined> {
    if (this.itemReadsRunning >= ITEMS_BY_ID_AT_ONCE) {
      if (this.itemReadQueue.length >= ITEMS_BY_ID_QUEUE_MAX) {
        return undefined;
      }

      await new Promise<void>((resolve) => this.itemReadQueue.push(resolve));
    } else {
      this.itemReadsRunning++;
    }

    try {
      return await work();
    } finally {
      const next = this.itemReadQueue.shift();

      if (next !== undefined) {
        next();
      } else {
        this.itemReadsRunning--;
      }
    }
  }

  /** How many ?ids= reads are under way and waiting. For tests. */
  get itemReadSlots(): { readonly running: number; readonly waiting: number } {
    return { running: this.itemReadsRunning, waiting: this.itemReadQueue.length };
  }

  /**
   * A collection's listing, newest documents first until LISTING_MAX_BYTES,
   * then put back in publishing order.
   *
   * One document is read and parsed at a time and only its serialised
   * form is kept, so what this holds at once is the listing itself and one
   * document - not every document parsed, then all of them serialised,
   * which is what three hundred big results used to cost for each request
   * that asked at the same moment.
   */
  private async buildListing(club: string, channel: string): Promise<Buffer> {
    const sizes = [...(await this.store.itemSizes(club, channel))].sort((a, b) => b.at - a.at);
    const kept: { publishedAt: string; text: string }[] = [];
    let total = 0;
    let omitted = 0;

    for (let i = 0; i < sizes.length; i++) {
      const item = sizes[i] as (typeof sizes)[number];

      if (item.bytes > LISTING_ITEM_MAX_BYTES) {
        omitted++;
        continue;
      }

      // A file that is not JSON will stay that way, so it is left out like
      // one that has gone. One that could not be read this time fails the
      // whole build, which is then not kept: a listing missing a result
      // it really has would be held until the next publish.
      const doc = await this.store.getItem(club, channel, item.id).catch((error: unknown) => {
        if (error instanceof StoreReadError && error.corrupt) {
          return undefined;
        }

        throw error;
      });

      // Gone since it was listed, or unreadable, or not a document at all.
      if (doc === undefined || typeof doc.publishedAt !== "string") {
        continue;
      }

      const text = JSON.stringify({ id: item.id, publishedAt: doc.publishedAt, body: doc.body });
      const bytes = Buffer.byteLength(text);

      // Full: this one and everything older stay out.
      if (total + bytes > LISTING_MAX_BYTES) {
        omitted += sizes.length - i;
        break;
      }

      kept.push({ publishedAt: doc.publishedAt, text });
      total += bytes;
    }

    // Oldest first before the sort, which is stable: two documents with
    // the same publishedAt then keep the order they were written in.
    kept.reverse();
    kept.sort((a, b) => a.publishedAt.localeCompare(b.publishedAt));

    // Written out by hand rather than parsed back and serialised again:
    // the same text JSON.stringify({ items }) would give, without holding
    // a second copy of every document to make it. "omitted" only when
    // something was, so an ordinary listing is exactly what it always was.
    const json = `{"items":[${kept.map((entry) => entry.text).join(",")}]${omitted > 0 ? `,"omitted":${omitted}` : ""}}`;

    return Buffer.from(json, "utf8");
  }

  /** Runs one listing build when there is room for it among LISTING_BUILDS_AT_ONCE; otherwise queues it. */
  private async inBuildSlot<T>(work: () => Promise<T>): Promise<T> {
    if (this.listingBuildsRunning >= LISTING_BUILDS_AT_ONCE) {
      // The slot is handed over by whoever finishes, so the count is not
      // taken here - it already includes this build.
      await new Promise<void>((resolve) => this.listingBuildQueue.push(resolve));
    } else {
      this.listingBuildsRunning++;
    }

    try {
      return await work();
    } finally {
      const next = this.listingBuildQueue.shift();

      if (next !== undefined) {
        next();
      } else {
        this.listingBuildsRunning--;
      }
    }
  }

  /** What the listing cache holds, as counted and as it really is. For tests. */
  get listingCache(): { readonly entries: number; readonly counted: number; readonly held: number } {
    let held = 0;

    for (const bytes of this.listings.values()) {
      held += bytes.length;
    }

    return { entries: this.listings.size, counted: this.listingBytes, held };
  }

  /**
   * One document of a collection.
   *
   * A year's index is held once read, in the listing cache, until the
   * club publishes to the index again: every results page reads the year
   * it opens and re-reads it every thirty seconds on the live view, and a
   * year can be a megabyte - parsed and serialised afresh for each of
   * those, from anybody, was the same shape of cost as an uncached
   * listing. A result is read each time, as before; it is a few
   * kilobytes and a page asks for results by ?ids=. Either way, a read
   * that has to go to the disk counts against the address's allowance.
   */
  private async readItem(
    request: IncomingMessage,
    response: ServerResponse,
    club: string,
    channel: string,
    id: string,
  ): Promise<void> {
    const spec = channelSpec(channel);

    // Private here too - see readCollection. One document of a private
    // collection is the whole of what somebody would want from it.
    if (spec === undefined || spec.kind !== "collection" || spec.private === true || !isValidItemId(id)) {
      sendJson(response, 404, { ok: false, error: "No such document." });
      return;
    }

    const key = this.itemKey(club, spec.name, id);
    const held = spec.name === PREVIEW_CHANNEL ? this.listings.get(key) : undefined;

    if (held !== undefined) {
      sendPublicBytes(response, 200, held);
      return;
    }

    const who = clientAddress(request);

    if (this.collectionReads.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a minute and try again." });
      return;
    }

    this.collectionReads.hit(who);

    if (spec.name !== PREVIEW_CHANNEL) {
      const doc = await this.store.getItem(club, channel, id);

      if (doc === undefined) {
        sendJson(response, 404, { ok: false, error: "No such document." });
        return;
      }

      sendPublicJson(response, 200, doc);
      return;
    }

    const bytes = await this.buildOnce(key, this.listingKey(club, spec.name, false), async () => {
      const doc = await this.store.getItem(club, spec.name, id);

      return doc === undefined ? undefined : Buffer.from(JSON.stringify(doc), "utf8");
    });

    if (bytes === undefined) {
      sendJson(response, 404, { ok: false, error: "No such document." });
      return;
    }

    sendPublicBytes(response, 200, bytes);
  }

  /**
   * One document's key in the listing cache. A "/" before the id, which
   * a listing's key never has after its channel, so forgetListing can
   * find every one of a collection's documents by that prefix.
   */
  private itemKey(club: string, channel: string, id: string): string {
    return `${club}/${channel}/${id}`;
  }

  /**
   * Answers "is this token right, and which club is it for" and changes
   * nothing.
   *
   * Without it the only way to check a token is to publish something,
   * which means the desktop app's Test button would have to write a junk
   * document into a live channel to prove it could. An operator pasting a
   * token in deserves a truthful yes or no that costs nothing.
   */
  private whoami(request: IncomingMessage, response: ServerResponse, club: string): void {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    sendJson(response, 200, {
      ok: true,
      club: who.club,
      name: who.name,
      publishRoster: who.publishRoster,
    });
  }

  /**
   * Which HeatCaller voices this club may use, and where its allowance
   * stands. The laptop asks as Voice Settings opens, and once an hour
   * after that.
   *
   * Answers 200 with enabled:false rather than a 404 when the voice is not
   * switched on here or not included for this club: "we know what you are
   * asking and the answer is no" is something the app can put on screen,
   * where a 404 only tells it the relay is old.
   *
   * The laptop asks as Voice Settings opens and when somebody presses
   * Refresh - not on a timer.
   */
  private async voiceCatalogue(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    // Cheap, but counted with the lines: it is the one other route a token
    // can hold open, and a page left refreshing should not be the thing
    // that finds that out.
    const asking = `${club}|${clientAddress(request)}`;

    if (this.voiceTraffic.blocked(asking)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a moment and try again." });
      return;
    }

    this.voiceTraffic.hit(asking);

    if (this.voice === undefined) {
      sendJson(response, 200, {
        ok: true,
        enabled: false,
        voices: [],
        usage: { used: 0, allowance: 0, resetsAt: null },
      });
      return;
    }

    const view = await this.voice.catalogue(club, who.voice ?? VOICE_PLAN_OFF);

    sendJson(response, 200, { ok: true, ...view });
  }

  /**
   * One line, spoken in the club's chosen HeatCaller voice.
   *
   * Answers audio/mpeg, or JSON saying why not - and the app treats every
   * refusal the same way: the free Kokoro voice says that line instead.
   * Nothing here is ever allowed to leave a flag call silent.
   */
  private async voiceSpeak(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      this.log(`[relay] refused: a token asked "${club}" for a voice line, which is not its club.`);
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const whoFrom = `${club}|${clientAddress(request)}`;

    if (this.voiceTraffic.blocked(whoFrom)) {
      sendJson(response, 429, { ok: false, error: "Too many lines at once. Wait a moment and try again." });
      return;
    }

    this.voiceTraffic.hit(whoFrom);

    // Small on purpose: the longest line the voice will speak is a few
    // hundred characters, and a body bigger than this is a mistake or an
    // attempt to make the relay hold something.
    const body = await readBody(request, response, 8 * 1024);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That request could not be read." });
      return;
    }

    if (this.voice === undefined) {
      sendJson(response, 503, {
        ok: false,
        refusal: "off",
        error: "The HeatCaller voice isn't switched on. Use your own ElevenLabs key or the free Kokoro voice for now.",
      });
      return;
    }

    const voiceId = typeof body["voice"] === "string" ? body["voice"].trim() : "";
    const text = typeof body["text"] === "string" ? body["text"] : "";

    // The laptop says whether a line can be kept for every club: true only
    // for a line with nothing filled into it. Anything with a driver's
    // name arrives false and is kept for this club alone.
    const shared = body["shared"] === true;

    // A fresh take, past the caches: the Re-roll button on Voice Settings
    // (22 Oct 2026). Charged like any line that was not saved.
    const fresh = body["fresh"] === true;

    const started = Date.now();
    const result = await this.voice.speak(club, who.voice ?? VOICE_PLAN_OFF, voiceId, text, shared, fresh);

    // For the admin app's charts: counts and labels, never the words.
    // Not awaited - the club's line goes out without waiting on the log.
    void this.usageLog?.record({
      at: this.now().toISOString(),
      club,
      meter: "voice",
      kind: fresh ? "reroll" : shared ? "shared" : "line",
      units: result.ok && !result.cached ? (result.characters ?? 0) : 0,
      outcome: result.ok ? "ok" : result.refusal,
      ms: Date.now() - started,
      voice: loggedVoice(voiceId, result),
      ...(result.ok && result.model !== undefined ? { model: result.model } : {}),
      ...(result.ok ? { cached: result.cached } : {}),
    });

    if (!result.ok) {
      sendJson(response, result.status, {
        ok: false,
        refusal: result.refusal,
        error: result.message,
        ...(result.usage === undefined
          ? {}
          : { used: result.usage.used, allowance: result.usage.allowance, resetsAt: result.usage.resetsAt }),
      });
      return;
    }

    sendAudio(response, result.audio, {
      "x-voice-cache": result.cached ? "hit" : "miss",
      "x-voice-used": String(result.usage.used),
      "x-voice-allowance": String(result.usage.allowance),
      "x-voice-resets": result.usage.resetsAt,
    });
  }

  /**
   * Every HeatCaller voice there is a sample of. No token, no club.
   *
   * The catalogue in the app could only ever give a club a name and a line
   * of description, so a club chose the voice its members would hear all
   * season by reading about it. This and the route below are the answer to
   * that, and they are also the first thing anybody looking at HeatCaller
   * can try.
   *
   * Answers an empty list rather than a 404 when this relay has no voice
   * configured, for the same reason voiceCatalogue answers enabled:false:
   * "we know what you are asking and there is nothing" is something a page
   * can put on screen.
   */
  private async voiceSampleList(request: IncomingMessage, response: ServerResponse): Promise<void> {
    const who = clientAddress(request);

    if (this.voiceSamples.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a minute and try again." });
      return;
    }

    this.voiceSamples.hit(who);

    if (this.voice === undefined) {
      sendPublicJson(response, 200, { samples: [] });
      return;
    }

    sendPublicJson(response, 200, {
      samples: (await this.voice.samples()).map((sample) => ({
        id: sample.id,
        name: sample.name,
        url: `/voice/sample/${encodeURIComponent(sample.id)}`,
      })),
    });
  }

  /**
   * One voice's sample: the same fixed, neutral line in every voice, so
   * two of them can actually be compared.
   *
   * WHY THIS CAN BE OPEN TO THE INTERNET. Nothing a caller sends decides
   * what is rendered - the words are a constant in voice.ts and the voice
   * must be one the catalogue names - so the most this route can ever cost
   * is one short line per voice in the catalogue, once, for all time. The
   * first visitor to ask for a voice pays for it; it is kept on the volume
   * and everyone after that is served from there. See VoiceService.sample.
   */
  private async voiceSample(request: IncomingMessage, response: ServerResponse, voiceId: string): Promise<void> {
    const who = clientAddress(request);

    if (this.voiceSamples.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a minute and try again." });
      return;
    }

    this.voiceSamples.hit(who);

    if (this.voice === undefined) {
      sendJson(response, 404, { ok: false, error: "There is no sample of that voice." });
      return;
    }

    const result = await this.voice.sample(voiceId);

    if (!result.ok) {
      sendJson(response, result.status, { ok: false, error: result.message });
      return;
    }

    sendSampleAudio(response, result.audio, { "x-voice-cache": result.cached ? "hit" : "miss" });
  }

  /**
   * One line of a club's own words, in one voice, for the Preview button
   * on Voice Settings.
   *
   * THE ONE ROUTE WITH NO CLUB IN ITS PATH, and the only one that cannot
   * have one: the app has a token and a voice id and nothing else to go
   * on. It is safe for the very reason that rule exists - "could this have
   * touched another club's data" is answerable - because here the token IS
   * the club. There is nothing in the URL that could name a different one,
   * so there is no mismatch to check and nothing to get wrong; the only
   * thing this route writes is the preview count of the club its own token
   * names.
   *
   * Charged against that club's small DAILY preview allowance and never
   * against its month: a club must not be able to arrive on a race night
   * and find it spent its voice on Tuesday deciding which one to use. Over
   * the day's allowance the answer is 429 and says so in words.
   */
  private async voicePreview(request: IncomingMessage, response: ServerResponse): Promise<void> {
    const who = this.tokenHolder(request);

    if (who === undefined) {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    const whoFrom = `${who.club}|${clientAddress(request)}`;

    if (this.voiceTraffic.blocked(whoFrom)) {
      sendJson(response, 429, { ok: false, error: "Too many lines at once. Wait a moment and try again." });
      return;
    }

    this.voiceTraffic.hit(whoFrom);

    // The same 8 KiB a line to speak gets: a preview is one sentence, and
    // the service caps it far shorter than this again.
    const body = await readBody(request, response, 8 * 1024);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That request could not be read." });
      return;
    }

    if (this.voice === undefined) {
      sendJson(response, 503, {
        ok: false,
        refusal: "off",
        error: "The HeatCaller voice isn't switched on here, so there is nothing to preview.",
      });
      return;
    }

    // "voiceId" here where speak takes "voice": a preview is its own
    // request with its own shape, and the app sends the two separately.
    const voiceId = typeof body["voiceId"] === "string" ? body["voiceId"].trim() : "";
    const text = typeof body["text"] === "string" ? body["text"] : "";

    const started = Date.now();
    const result = await this.voice.preview(who.club, who.voice ?? VOICE_PLAN_OFF, voiceId, text);

    // A preview spends ElevenLabs characters too (from its own daily
    // allowance), so it is logged like a line - see voiceSpeak.
    void this.usageLog?.record({
      at: this.now().toISOString(),
      club: who.club,
      meter: "voice",
      kind: "preview",
      units: result.ok && !result.cached ? (result.characters ?? 0) : 0,
      outcome: result.ok ? "ok" : result.refusal,
      ms: Date.now() - started,
      voice: loggedVoice(voiceId, result),
      ...(result.ok && result.model !== undefined ? { model: result.model } : {}),
      ...(result.ok ? { cached: result.cached } : {}),
    });

    if (!result.ok) {
      sendJson(response, result.status, {
        ok: false,
        refusal: result.refusal,
        error: result.message,
        ...(result.usage === undefined
          ? {}
          : { used: result.usage.used, allowance: result.usage.allowance, resetsAt: result.usage.resetsAt }),
      });
      return;
    }

    // Named apart from speak's x-voice-used on purpose: a laptop reading
    // these must never mistake a day of auditioning for the month's
    // characters and put the wrong number on Voice Settings.
    sendAudio(response, result.audio, {
      "x-voice-cache": result.cached ? "hit" : "miss",
      "x-voice-preview-used": String(result.usage.used),
      "x-voice-preview-allowance": String(result.usage.allowance),
      "x-voice-preview-resets": result.usage.resetsAt,
    });
  }

  /**
   * The club a bearer token names, whatever club the path asked for -
   * which is what voicePreview needs, being the one route with no club in
   * its path.
   *
   * Must stay in step with authorise, which answers the same question for
   * every other route and then checks the club as well.
   */
  private tokenHolder(request: IncomingMessage): Tenant | undefined {
    const header = request.headers.authorization ?? "";
    const match = /^Bearer (.+)$/.exec(header.trim());

    return match === null ? undefined : this.tenants.resolveToken(match[1] as string);
  }

  /**
   * Whether this club has the commentary brain, and where its month
   * stands. Asked when the Commentary page opens, like the voice's.
   */
  private async brainCatalogue(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    // Counted with the voice's traffic rather than given a limiter of its
    // own: both are the costly work a token can hold open, they are asked
    // for by the same laptop minutes apart, and one budget is one thing to
    // reason about when a club says the announcer went quiet.
    const asking = `${club}|${clientAddress(request)}`;

    if (this.voiceTraffic.blocked(asking)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a moment and try again." });
      return;
    }

    this.voiceTraffic.hit(asking);

    if (this.brain === undefined) {
      sendJson(response, 200, {
        ok: true,
        enabled: false,
        model: "",
        usage: { used: 0, allowance: 0, resetsAt: null },
      });
      return;
    }

    const view = await this.brain.catalogue(club, who.brain ?? BRAIN_PLAN_OFF);

    sendJson(response, 200, { ok: true, ...view });
  }

  /**
   * One line of commentary, written for this club.
   *
   * Answers the line as JSON, or JSON saying why not - and the app treats
   * every refusal the same way: Ollama on the race laptop writes it
   * instead, or the announcer's template lines do. Nothing here is ever
   * allowed to stop a race, which is why none of it is on the path of a
   * flag call.
   */
  private async brainWrite(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      this.log(`[relay] refused: a token asked "${club}" for a commentary line, which is not its club.`);
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const whoFrom = `${club}|${clientAddress(request)}`;

    if (this.voiceTraffic.blocked(whoFrom)) {
      sendJson(response, 429, { ok: false, error: "Too many lines at once. Wait a moment and try again." });
      return;
    }

    this.voiceTraffic.hit(whoFrom);

    // Bigger than the voice's 8 KiB: a recap carries the finishing order,
    // the gaps and any rivalries, which is more than a line to speak.
    // Still far smaller than anything that would cost real money.
    const body = await readBody(request, response, 32 * 1024);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That request could not be read." });
      return;
    }

    if (this.brain === undefined) {
      sendJson(response, 503, {
        ok: false,
        refusal: "off",
        error: "The commentary brain isn't switched on for this relay.",
      });
      return;
    }

    const system = typeof body["system"] === "string" ? body["system"] : "";
    const facts = typeof body["facts"] === "string" ? body["facts"] : "";
    const words = typeof body["words"] === "number" ? body["words"] : 60;
    const variety = typeof body["variety"] === "number" ? body["variety"] : 0.7;

    const started = Date.now();
    const askedWords = Math.max(10, Math.min(Math.round(words), BRAIN_LONG_WORDS_CEILING));
    const result = await this.brain.write(
      club,
      who.brain ?? BRAIN_PLAN_OFF,
      system,
      facts,
      // Clamped here as well as in the service: a laptop asking for a
      // thousand words is a bug, and the bill is HeatCaller's. Up to the
      // long-piece ceiling, not 300: the service holds a normal line to
      // maxWords and a recap or preview to maxLongWords, and a clamp here
      // below that cut every long piece short before it got there.
      Math.max(10, Math.min(Math.round(words), BRAIN_LONG_WORDS_CEILING)),
      Math.max(0, Math.min(variety, 1)),
    );

    // For the admin app's charts - see voiceSpeak. The facts and the line
    // written are never kept; the tokens are Anthropic's own count.
    void this.usageLog?.record({
      at: this.now().toISOString(),
      club,
      meter: "commentary",
      kind: askedWords > this.brain.maxWords ? "long" : "line",
      units: result.ok ? 1 : 0,
      // Anthropic's own 429 or 5xx is "upstream" here, though the club is
      // told "busy": the log is for telling Anthropic failing from the
      // relay's own one-line-at-a-time limit (relay admin, round C).
      outcome: result.ok ? "ok" : result.fromModel === true ? "upstream" : result.refusal,
      ms: Date.now() - started,
      model: this.brain.model,
      words: askedWords,
      ...(result.ok && result.tokens !== undefined ? { in: result.tokens.input, out: result.tokens.output } : {}),
    });

    if (!result.ok) {
      sendJson(response, result.status, {
        ok: false,
        refusal: result.refusal,
        error: result.message,
        ...(result.usage === undefined
          ? {}
          : { used: result.usage.used, allowance: result.usage.allowance, resetsAt: result.usage.resetsAt }),
      });
      return;
    }

    sendJson(response, 200, {
      ok: true,
      text: result.text,
      used: result.usage.used,
      allowance: result.usage.allowance,
      resetsAt: result.usage.resetsAt,
    });
  }

  /**
   * The usage answer itself, for the laptop and the phone alike: each meter
   * read without spending, and this month's top-up requests.
   */
  private async usageView(tenant: Tenant): Promise<Record<string, unknown>> {
    const none = (): Omit<MeterUsage, "resetsAt"> & { resetsAt: null } => ({
      enabled: false,
      used: 0,
      allowance: 0,
      topUp: 0,
      resetsAt: null,
      known: true,
    });

    const club = tenant.club;
    const commentary = this.brain === undefined ? none() : await this.brain.usage(club, tenant.brain ?? BRAIN_PLAN_OFF);
    const voice = this.voice === undefined ? none() : await this.voice.usage(club, tenant.voice ?? VOICE_PLAN_OFF);
    const requests = this.topUpRequests === undefined ? [] : await this.topUpRequests.list(club);

    return {
      ok: true,
      commentary,
      voice,
      requests: requests.map((ask) => ({ id: ask.id, meter: ask.meter, askedAt: ask.askedAt, note: ask.note, status: ask.status })),
    };
  }

  /** Records an ask for more, and answers it - shared by the laptop's route and the phone's. */
  private async askForMore(response: ServerResponse, club: string, body: Record<string, unknown>): Promise<void> {
    const meter = body["meter"];

    if (!isMeterName(meter)) {
      sendJson(response, 400, { ok: false, error: 'meter must be "commentary" or "voice".' });
      return;
    }

    if (this.topUpRequests === undefined) {
      sendJson(response, 503, { ok: false, error: "This relay doesn't take top-up requests. Email support instead." });
      return;
    }

    const result = await this.topUpRequests.ask(club, meter, body["note"]);

    if (!result.ok) {
      sendJson(response, result.status, { ok: false, error: result.error });
      return;
    }

    const ask = result.request;

    // Answered at once when the club's config allows it (autotopup.ts):
    // only an ask still open, from a club that is on and has the meter in its plan.
    // 2026-11-05 Bug hunt 11: was "only a new ask" (!result.already). An ask
    // made below 90% stayed open, and every later ask that month came back
    // "already" and skipped this check, so a club that then ran out on race
    // night never got its automatic top-up. An open ask asked again is
    // checked again; autoTopUp refuses an id it has answered and keeps
    // perMonth, so it can't give twice.
    const tenant = this.tenants.club(club);
    const rule = tenant?.autoTopUp?.[meter];
    const included = meter === "voice" ? tenant?.voice?.enabled === true : tenant?.brain?.enabled === true;
    // And only when the club has nearly used what it has (AUTO_WHEN_USED):
    // otherwise a few taps of Ask for More would take every automatic
    // top-up of the month at once.
    let nearlySpent = false;

    if (ask.status === "open" && rule !== undefined && tenant !== undefined && isActive(tenant) && included) {
      const usage = meter === "voice"
        ? (tenant.voice !== undefined ? await this.voice?.usage(club, tenant.voice) : undefined)
        : (tenant.brain !== undefined ? await this.brain?.usage(club, tenant.brain) : undefined);
      nearlySpent = usage !== undefined && usage.enabled && usage.used >= usage.allowance * AUTO_WHEN_USED;
    }

    const given = nearlySpent && rule !== undefined
      ? await this.topUpRequests.autoTopUp(club, meter, ask.id, rule, ask.askedAt)
      : undefined;

    sendJson(response, 200, {
      ok: true,
      request: { id: ask.id, meter: ask.meter, askedAt: ask.askedAt, note: ask.note, status: given === undefined ? ask.status : "done" },
      already: result.already,
      ...(given === undefined ? {} : { autoTopUp: given.amount }),
    });
  }

  /** The laptop asking where this club's month stands. Counted with the voice and brain catalogues - the same laptop asks all three. */
  private async usageRead(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const asking = `${club}|${clientAddress(request)}`;

    if (this.voiceTraffic.blocked(asking)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a moment and try again." });
      return;
    }

    this.voiceTraffic.hit(asking);

    sendJson(response, 200, await this.usageView(who));
  }

  /** The laptop asking for more of one meter this month. */
  private async usageTopUpRequest(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const asking = `${club}|${clientAddress(request)}`;

    if (this.voiceTraffic.blocked(asking)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a moment and try again." });
      return;
    }

    this.voiceTraffic.hit(asking);

    const body = await readBody(request, response, 8 * 1024);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That request could not be read." });
      return;
    }

    await this.askForMore(response, club, body);
  }

  /**
   * The manager's phone checking the pass it was given, as manage/response
   * does. The body when it is good, undefined once an answer has been sent.
   */
  private async manageBodyWithPass(
    request: IncomingMessage,
    response: ServerResponse,
    club: string,
  ): Promise<Record<string, unknown> | undefined> {
    const who = `${club}|${clientAddress(request)}`;

    if (this.manageTraffic.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a minute and try again." });
      return undefined;
    }

    this.manageTraffic.hit(who);

    const body = await readBody(request, response, MANAGE_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That request could not be read." });
      return undefined;
    }

    const pass = typeof body["pass"] === "string" ? (body["pass"] as string) : "";
    const manage = await this.currentManage(club);

    if (manage === undefined || !manage.enabled || manage.pass === "" || !PASS.test(pass) || !sameHash(pass, manage.pass)) {
      sendJson(response, 403, { ok: false, error: "Sign in again." });
      return undefined;
    }

    return body;
  }

  /** The manager's phone asking where this club's month stands - the laptop's own answer, by pass rather than token. */
  private async manageUsage(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const body = await this.manageBodyWithPass(request, response, club);
    const tenant = this.tenants.club(club);

    if (body === undefined) {
      return;
    }

    if (tenant === undefined) {
      sendJson(response, 404, { ok: false, error: "No such club." });
      return;
    }

    sendJson(response, 200, await this.usageView(tenant));
  }

  /** The manager's phone asking for more of one meter this month. */
  private async manageTopUpRequest(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const body = await this.manageBodyWithPass(request, response, club);

    if (body === undefined) {
      return;
    }

    await this.askForMore(response, club, body);
  }

  private async deleteClub(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    await this.withLock(club, async () => {
      // The wrong-PIN count goes too, from memory as well as disk, and the
      // wipe waits for any save of it already under way - or that save
      // would put the file back in the folder just deleted.
      //
      // All three guards at once, the wipe innermost: each stops writing
      // and waits for its own save in flight before the files go. One after
      // another, the manager's and sign-up's guards were still writing while
      // the wipe ran - a wrong manager PIN or invite code takes no club lock
      // - and a save landing after it put a count file back in the folder of
      // a club that had asked to be forgotten, lockout and all.
      //
      // The sign-ups themselves - names, transponder numbers, emails - are
      // in the club's folder, so the wipe takes them; nothing of them is
      // held in memory to forget.
      await this.pinGuard.forget(club, () =>
        this.managePinGuard.forget(club, () =>
          this.signupPinGuard.forget(club, () => this.forgetPushSubs(club, () => this.store.deleteClub(club))),
        ),
      );

      // The pass key went with the folder; the one held in memory goes too,
      // so a club set up again under this id starts with a new one and no
      // pass from before the delete opens anything.
      this.passKeys.delete(club);
      // 17 Oct 2026, Bug Hunt 8, A19-3: a club set up again starts fresh.
      this.lastStaffPin.delete(club);
      this.lastManagePin.delete(club);
      // Its drivers' clips, kept outside the club's folder (clips.ts).
      //
      // WHY (22 Oct 2026, Bug Hunt 9): an upload whose body was still
      // arriving when Delete was pressed took its own lock after the wipe
      // and wrote the file - a named driver's - straight back. wipeClips
      // moves the wipe count first, so any upload already on its way keeps
      // nothing. (The podium videos were wiped here the same way until 28
      // Oct 2026, when they stopped coming to the relay.)
      await this.wipeClips(club);
      this.forgetDriverDoc(club, DRIVER_LIVE_CHANNEL);
      this.forgetDriverDoc(club, DRIVER_ME_CHANNEL);

      // And the voice's lines for this club - drivers' names, spoken -
      // which the voice keeps in a folder of its own rather than the
      // club's, so the wipe above never touched them. The month's count,
      // its history and any top-up stay: they hold no names, and the
      // tenant is in the relay's config, so the same token works again at
      // once. Wiping the count would give the club a fresh allowance on
      // every delete and erase what it is billed by.
      await this.voice?.forget(club);
      this.forgetClubListings(club);
      this.forgetCheckin(club);
      this.forgetSite(club);
      this.forgetPreviews(club);

      // WHY (20 Oct 2026, Bug Hunt 8): the top-up asks stay too, so the
      // monthly cap and the operator's queue still hold, but their notes -
      // free text that can name a driver - are blanked. They live outside
      // the club folder, so the wipe above never reached them. Last, after
      // the in-memory forgets: a disk error here then fails only this
      // step, and pressing Delete again is safe.
      await this.topUpRequests?.forgetNotes(club);
    });
    this.log(`[relay] "${club}" deleted everything it had published.`);

    sendJson(response, 200, { ok: true, deleted: club });
  }

  private async contextFor(request: IncomingMessage, club: string): Promise<PageContext> {
    // A fresh nonce per page: the one script each page carries names it,
    // and the content policy runs nothing that does not.
    return { look: await this.lookFor(club), origin: originOf(request), nonce: randomBytes(16).toString("base64") };
  }

  // ---------------------------------------------------------- check-in

  /**
   * The three things the public check-in page can ask, all POSTs so that
   * nothing a browser or a messaging app pre-fetches can change anything:
   *
   *   list  { pin, pass? }   every driver, for the desk or a driver
   *                          looking for themselves - needs the PIN,
   *                          or the pass an earlier right PIN was given
   *   one   { id }           a single driver, for a badge link - the id
   *                          itself is the credential
   *   tap   { id, wanted,    check in or out - queued for the laptop. A
   *           pass?, pin? }  driver with a personal PIN needs their
   *                          own PIN, or the pass it was answered with
   *                          (or the desk's staff pass / row handle) -
   *                          26 Oct 2026
   *
   * The id is a keyed hash the laptop makes from the transponder, eighty
   * bits of it: unguessable, stable across meetings so a printed badge
   * keeps working, and no use to anyone as a tracking key.
   */
  private async checkinRequest(
    request: IncomingMessage,
    response: ServerResponse,
    club: string,
    action: string,
  ): Promise<void> {
    if (action !== "list" && action !== "one" && action !== "tap") {
      sendJson(response, 404, { ok: false, error: "No such endpoint." });
      return;
    }

    const who = `${club}|${clientAddress(request)}`;
    const traffic = action === "tap" ? this.checkinTraffic : this.checkinReads;

    if (traffic.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a minute and try again." });
      return;
    }

    traffic.hit(who);

    const body = await readBody(request, response, CHECKIN_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That request could not be read." });
      return;
    }

    const input = body;
    const checkin = await this.checkinOf(club);

    if (checkin === undefined || !checkin.enabled) {
      sendJson(response, 409, { ok: false, error: "Online check-in is not open right now." });
      return;
    }

    const drivers = checkin.drivers;

    // WHY (23 Oct 2026, bug hunt 10): the staff list used to hand out every
    // driver's real badge id - and since 13 and 22 Oct that id alone opens
    // the driver's private night, clips, live entry and details asks. The
    // staff PIN is a desk PIN shared with volunteers, so it must not carry
    // that. Staff now get the same rows with an opaque handle in place of
    // the id (staffRowRef); a tap with that handle is turned back into the
    // driver below. A racer still gets their own real id.
    const staffRows = (): CheckinDriver[] =>
      drivers.map((d) => ({ ...d, id: staffRowRef(checkin.passKey, club, checkin.pinSha256, d.id) }));

    if (action === "list") {
      // A pass first. It is what a right PIN was answered with, and it is
      // 256 bits nobody can guess, so taking it costs the PIN limits
      // nothing - which is what lets a desk already signed in carry on
      // through a lockout that somebody else's guessing caused.
      const pass = typeof input["pass"] === "string" ? (input["pass"] as string) : "";

      if (PASS.test(pass)) {
        if (checkin.staffPass !== "" && sameHash(pass, checkin.staffPass)) {
          sendJson(response, 200, { ok: true, mode: "staff", event: checkin.event, drivers: staffRows(), pass: checkin.staffPass });
          return;
        }

        // Against every racer's pass, in constant time - see racerByPass.
        const passRacer = racerByPass(checkin, pass);

        if (passRacer !== undefined) {
          sendJson(response, 200, { ok: true, mode: "racer", event: checkin.event, driver: passRacer, pass });
          return;
        }

        // A pass that opens nothing - the PIN it came from has changed -
        // is simply not used. The PIN beside it is then judged as ever.
      }

      // The club's wrong-PIN count, read from disk if this is the first
      // ask since the relay started. Awaited here, before the checks, and
      // nothing below awaits anything until the guess has been judged and
      // - if wrong - counted. That is what makes the limits hold when a
      // burst arrives at once, as it does when the relay wakes: every
      // request that queued while it started is let in together, and when
      // the checks came before an await and the counting after it, every
      // guess in the burst was compared before the first one was counted.
      await this.pinGuard.ready(club);

      // Before any PIN is compared, and for racer PINs as much as the
      // staff one: a guess is a guess whichever PIN it was aimed at.
      if (this.pinFailures.blocked(who)) {
        sendJson(response, 429, { ok: false, error: "Too many wrong PINs from this connection. Try again in a few minutes." });
        return;
      }

      const lockedUntil = this.pinGuard.lockedUntilNow(club);

      if (lockedUntil > 0) {
        const minutes = Math.max(1, Math.ceil((lockedUntil - this.now().getTime()) / 60000));
        const wait = minutes <= 90 ? `${minutes} minute${minutes === 1 ? "" : "s"}` : `${Math.round(minutes / 60)} hours`;

        sendJson(response, 429, {
          ok: false,
          error:
            `Too many wrong PINs have been tried for this club, so PIN sign-in is closed for about ${wait}. ` +
            "Phones already signed in keep working, and so does checking in or out from a QR check-in with your own PIN.",
        });
        return;
      }

      // No PIN of either kind to match, so nothing to guess at - refused
      // without counting, as it always was.
      if (checkin.pinSha256 === "" && checkin.racers.size === 0) {
        sendJson(response, 403, { ok: false, error: "The club has not set a check-in PIN, so the list is closed. QR check-ins still work." });
        return;
      }

      const offered = typeof input["pin"] === "string" ? (input["pin"] as string).trim() : "";

      // A PIN is four to eight digits, whichever kind it is. Anything else
      // is answered and counted exactly as a wrong PIN, as it was before
      // there was a shape to check: nobody honest types one, and letting
      // junk through uncounted would only tell a guesser what a PIN looks
      // like for free.
      const wellFormed = /^[0-9]{4,8}$/.test(offered);

      // What the two hashes below will cost, taken before either is worked
      // out - right PIN, wrong one, or a stale pass's PIN alike, since all
      // of them pay. Refused, not counted as a guess, when there is no room.
      const slow = wellFormed && checkin.salt !== "" ? (checkin.pinSha256 !== "" ? 2 : 1) : 0;

      if (!this.takeSlowHashes(who, slow)) {
        sendJson(response, 429, { ok: false, error: SLOW_HASH_BUSY });
        return;
      }

      // Staff first, compared in constant time as ever.
      const staffHash = wellFormed && checkin.pinSha256 !== "" ? pinHash(club, offered, checkin.salt) : "";

      if (staffHash !== "" && sameHash(staffHash, checkin.pinSha256)) {
        sendJson(response, 200, { ok: true, mode: "staff", event: checkin.event, drivers: staffRows(), pass: checkin.staffPass });
        return;
      }

      // Then a racer's own PIN, found by its hash. A map lookup rather than
      // a constant-time walk: the hash is what the laptop published, not
      // the PIN, and knowing how much of a hash a guess matched says
      // nothing about which PIN to try next.
      const racerHash = wellFormed ? racerPinHash(club, offered, checkin.salt) : "";
      const racer = wellFormed ? checkin.racers.get(racerHash) : undefined;

      if (racer !== undefined) {
        sendJson(response, 200, { ok: true, mode: "racer", event: checkin.event, driver: racer.driver, pass: racer.pass });
        return;
      }

      // A page left open sends its PIN with the pass that PIN was given,
      // and once the PINs change that pass opens nothing. It used to be
      // counted as a wrong guess, so a dozen phones left open on the venue
      // Wi-Fi after New PINs Now - or the weekly change - locked that
      // address out of PIN sign-in, new PINs and all. A pass is the
      // relay's own keyed hash of the PIN it was given for, so one made
      // from THIS PIN proves the page really had it once: refused, but not
      // counted. Only that one PIN, so it is no way to guess for free. The
      // hashes are the ones just worked out, and the key is the view's own,
      // so nothing here awaits or runs another slow hash.
      if (PASS.test(pass) && wellFormed) {
        const hadIt =
          sameHash(pass, keyedPass(checkin.passKey, "racer", club, racerHash)) ||
          (staffHash !== "" && sameHash(pass, keyedPass(checkin.passKey, "staff", club, staffHash)));

        if (hadIt) {
          sendJson(response, 403, { ok: false, error: "That PIN has changed." });
          return;
        }
      }

      // Counted in memory here, in the same step as the checks above, so
      // the next request already sees it. Only the save to disk is waited
      // for, and that after the count.
      this.pinFailures.hit(who);
      await this.pinGuard.failNow(club);
      sendJson(response, 403, { ok: false, error: "That PIN is not right." });
      return;
    }

    const asked = typeof input["id"] === "string" ? (input["id"] as string) : "";
    let driver = DRIVER_ID.test(asked) ? drivers.find((d) => d.id === asked) : undefined;

    // Whether this tap came off the staff list, by a row's handle - which
    // only the staff PIN (or its pass) is ever answered with.
    let viaStaffRow = false;

    // A tap from the staff list carries the row's handle, not the badge id
    // (23 Oct 2026, bug hunt 10 - see staffRows above). Only a tap: "one"
    // answers with the real id, so a handle must never open it.
    if (driver === undefined && action === "tap" && DRIVER_ID.test(asked) && checkin.pinSha256 !== "") {
      driver = drivers.find((d) => sameHash(staffRowRef(checkin.passKey, club, checkin.pinSha256, d.id), asked));
      viaStaffRow = driver !== undefined;
    }

    // Everything from here on is keyed by the real badge id.
    const id = driver?.id ?? "";

    if (driver === undefined) {
      sendJson(response, 404, { ok: false, error: "That driver is not on today's check-in list." });
      return;
    }

    if (action === "one") {
      // AND WHETHER THERE IS A NIGHT TO OFFER THEM (13 Oct 2026). This is
      // the one place a driver has already shown their own badge code and
      // the page is already showing just them - so it is the one place the
      // relay can hand them a link to their own night without anybody
      // having to print a new badge or send them an address.
      //
      // Told plainly, because nothing is given away by it: the guard
      // directly above answers 404 for an id that is not on the list, so
      // this line is only ever reached by somebody already holding a real
      // badge code - and whether a club offers this is not a secret from
      // its own drivers. What IS a secret is which codes are real, and
      // that is what the 404 above and readOneNight's single 404 keep.
      const nights = await this.nightsOpenFor(club);

      // AND WHETHER IT IS THEIR FIRST NIGHT (22 Oct 2026, driver
      // experience), from their own live entry, so the badge page can offer
      // the first-night guide before they have raced. Only ever true, and
      // only to the holder of this badge code - the same reach as above.
      const live = nights ? await this.liveEntryOf(club, id) : undefined;

      sendJson(response, 200, {
        ok: true,
        event: checkin.event,
        driver,
        nights,
        ...(live !== undefined && live.entry["firstNight"] === true ? { firstNight: true } : {}),
      });
      return;
    }

    // WHY (26 Oct 2026, asked for by Asher: "is there a reason the phone
    // sign in page you can sign in/out anyone you want without a pin?"): a
    // tap used to need nothing but the driver's badge code. That code is
    // printed on a lanyard, sits in their night link and can be read off a
    // badge across the desk, so whoever saw it could check that driver in
    // or out. A driver who HAS a personal PIN is now checked in or out only
    // with it - the same rule driver/ask already keeps for their details.
    // The proof is either the pass checkin/list answered that PIN with,
    // matched to THIS driver in constant time (racerByPass), or the PIN
    // itself in the tap's body, checked against this badge's own hash only
    // (see below - the review of the same day). The desk's staff list
    // keeps working: a row handle (only ever given for the staff PIN) or
    // the staff pass is staff's own way in. A driver with no personal PIN
    // has nothing else to show, so their badge still works as before.
    const tapPass = typeof input["pass"] === "string" && PASS.test(input["pass"] as string) ? (input["pass"] as string) : "";
    const byStaff = viaStaffRow || (tapPass !== "" && checkin.staffPass !== "" && sameHash(tapPass, checkin.staffPass));
    const byOwnPass = tapPass !== "" && racerByPass(checkin, tapPass)?.id === id;

    // The pass this tap earned by carrying the driver's PIN itself, handed
    // back so the page need not ask again this tab - see below.
    let earnedPass = "";

    if (!byStaff && !byOwnPass) {
      const own = racerEntryOf(checkin, id);

      if (own !== undefined) {
        // WHY (26 Oct 2026, review of the change above): a pass comes only
        // from checkin/list, and checkin/list closes for the whole club
        // (up to twelve hours) when anybody types junk PINs, or for every
        // phone on the venue's one NAT address after ten wrong ones. That
        // turned a stranger's guessing into "no driver with a PIN can check
        // in from a phone" - where badges used to carry on through a
        // lockout. So the tap may carry the PIN itself, and it is checked
        // against THIS badge's own stored hash and nobody else's: a
        // targeted guess at one PIN by somebody already holding that badge
        // code, not a search of the club's PINs. It has its own limit, five
        // wrong in ten minutes per badge, and is kept apart from the
        // club-wide lockout and the per-address PIN count. It still pays
        // the slow hash and its per-address CPU budget, which is "busy",
        // never a lockout. The PIN is only ever in the POST body.
        const offered = typeof input["pin"] === "string" ? (input["pin"] as string).trim() : "";
        const badge = `${club}|${id}`;

        if (offered === "") {
          sendJson(response, 403, {
            ok: false,
            needPin: true,
            error: "Type your own check-in PIN (the one the club emailed you) to check in or out.",
          });
          return;
        }

        if (this.badgePinFailures.blocked(badge)) {
          sendJson(response, 429, {
            ok: false,
            needPin: true,
            error: "Too many wrong PINs for this driver. Wait ten minutes, or check in at the desk.",
          });
          return;
        }

        const wellFormed = /^[0-9]{4,8}$/.test(offered);

        if (wellFormed && !this.takeSlowHashes(who, checkin.salt !== "" ? 1 : 0)) {
          sendJson(response, 429, { ok: false, error: SLOW_HASH_BUSY });
          return;
        }

        if (!wellFormed || !sameHash(racerPinHash(club, offered, checkin.salt), own.hash)) {
          this.badgePinFailures.hit(badge);
          sendJson(response, 403, { ok: false, needPin: true, error: "That PIN is not right." });
          return;
        }

        earnedPass = own.pass;
      }
    }

    if (typeof input["wanted"] !== "boolean") {
      sendJson(response, 400, { ok: false, error: "Say whether to check in or out." });
      return;
    }

    const wanted = input["wanted"] as boolean;
    const tapper = `${club}|${id}`;

    if (this.driverTaps.blocked(tapper)) {
      sendJson(response, 429, { ok: false, error: "That driver has been tapped too often. Wait a minute and try again." });
      return;
    }

    this.driverTaps.hit(tapper);

    const queued = await this.withLock(club, async () => {
      // The roster again, now that nothing else can write: the wait for
      // this lock may have been behind the desk closing check-in, taking
      // this driver off, or the club deleting everything - and a tap queued
      // after any of those wrote a driver's id into the folder of a club
      // that had just asked to be forgotten. As sign-up and the manager
      // already check (see signupSubmit). A roster that cannot be read
      // just now is not a "no": the tap is queued, as it always was.
      let latest: StoredDocument | undefined;
      let unreadable = false;

      try {
        latest = await this.store.getSnapshot(club, "checkin");
      } catch {
        unreadable = true;
      }

      const latestBody = isPlainObject(latest?.body) ? latest.body : undefined;

      if (!unreadable) {
        if (latestBody === undefined || latestBody["enabled"] !== true) {
          return "closed" as const;
        }

        const listed = Array.isArray(latestBody["drivers"]) ? (latestBody["drivers"] as unknown[]) : [];

        if (!listed.some((entry) => isPlainObject(entry) && entry["id"] === id)) {
          return "gone" as const;
        }
      }

      const inbox = await this.inboxOf(club);
      const at = this.now();

      // One waiting tap per driver: a newer one replaces it, since only
      // the last thing a driver asked for is what they want - and a queue
      // that kept every tap would let one badge fill it for everybody.
      const kept = inbox.items.filter(
        (item) => at.getTime() - Date.parse(item.atUtc) < INBOX_MAX_AGE_MS && item.id !== id,
      );

      // Full is refused, not made room for. Every tap in the queue is one
      // the laptop has not seen yet, and dropping the oldest to take this
      // one would lose a driver's check-in silently; a refusal at least
      // tells the person tapping.
      if (kept.length >= INBOX_MAX_ITEMS) {
        return false;
      }

      await this.store.putSnapshot(club, INBOX, {
        seq: 0,
        publishedAt: at.toISOString(),
        body: { next: inbox.next + 1, items: [...kept, { n: inbox.next, id, wanted, atUtc: at.toISOString() }] },
      });

      // The relay's copy of the roster shows the tap straight away, so the
      // next person to open the list sees it too - the laptop's next
      // publish, once it has applied the tap, then says the same thing
      // for real. Re-read inside the lock: the laptop may have published
      // since the check at the top.
      //
      // A roster that cannot be read is left as it is - the tap is queued,
      // and the laptop's next publish shows it - rather than failing a tap
      // that has already been taken. Read above, inside this same lock.
      if (latest !== undefined && latestBody !== undefined && Array.isArray(latestBody["drivers"])) {
        let changed = false;

        const updated = (latestBody["drivers"] as unknown[]).map((entry) => {
          if (!isPlainObject(entry) || entry["id"] !== id || entry["checkedIn"] === wanted) {
            return entry;
          }

          changed = true;
          return { ...entry, checkedIn: wanted };
        });

        // Only when it says something new. The roster carries every
        // driver's photo, so it is by far the biggest thing a tap would
        // write, and rewriting it to say what it already says was most
        // of the cost of somebody tapping the same badge over and over.
        if (changed) {
          await this.store.putSnapshot(club, "checkin", {
            ...latest,
            body: { ...latestBody, drivers: updated },
          });
          this.forgetCheckin(club);
        }
      }

      return true;
    });

    if (queued === "closed") {
      sendJson(response, 409, { ok: false, error: "Online check-in is not open right now." });
      return;
    }

    if (queued === "gone") {
      sendJson(response, 404, { ok: false, error: "That driver is not on today's check-in list." });
      return;
    }

    if (!queued) {
      this.log(`[relay] "${club}": the check-in queue is full; a tap was refused.`);
      sendJson(response, 503, {
        ok: false,
        error: "Check-in is busy - the timing desk has not collected recent taps yet. Try again in a minute, or check in at the desk.",
      });
      return;
    }

    this.log(`[relay] "${club}": a check-${wanted ? "in" : "out"} was queued for the laptop.`);
    sendJson(response, 200, earnedPass === "" ? { ok: true, checkedIn: wanted } : { ok: true, checkedIn: wanted, pass: earnedPass });
  }

  /**
   * The club's check-in document, cleaned, from memory when it can be.
   * Only the relay writes the document, and every write calls
   * forgetCheckin, so what is held is never older than the disk.
   */
  private async checkinOf(club: string): Promise<CheckinView | undefined> {
    const held = this.checkins.get(club);

    if (held !== undefined) {
      return held ?? undefined;
    }

    // A write landing while this read waits on the disk bumps the epoch,
    // and what this read found is then not kept: it may be the old one.
    // A read that fails - the file is there but could not be read - throws
    // to the caller and is not kept either: it is not "nothing published".
    return this.shared(
      this.checkinLoads,
      club,
      () => this.checkinEpoch,
      async () => {
        const doc = await this.store.getSnapshot(club, "checkin");

        // The pass key only when there is a roster to make passes for. Every
        // page view asks for the check-in tab's state, and making the key
        // for a club with no roster wrote a file for every anonymous visit
        // to it - a club that had just deleted everything got its folder
        // back from the first person to open its home page.
        return readCheckin(club, doc, isPlainObject(doc?.body) ? await this.passKeyOf(club) : Buffer.alloc(0));
      },
      (view) => this.checkins.set(club, view ?? null),
    );
  }

  /** Called after every write to a club's check-in document. After, not before: a read in between would cache the old one. */
  private forgetCheckin(club: string): void {
    this.checkinEpoch++;
    this.checkins.delete(club);
  }

  /**
   * One of the relay's own queue documents - INBOX, MANAGE_QUEUE or
   * SIGNUP_BOX, and since 20 Oct 2026 VOTE_BALLOT_DOC too - as
   * getSnapshot reads it, except that one which will never parse reads as
   * empty.
   *
   * WHY. Every route that writes these documents reads them first, under
   * the lock, and a corrupt file (left short by a power cut before it
   * reached the disk - FileStore does not fsync - or by a hand edit) threw
   * on that read every time. listen() answered 503 "try again", but
   * nothing ever wrote a good file over the bad one, so check-in taps, the
   * race-day manager or sign-ups stayed broken on every race night after,
   * until someone deleted the file on the server. Starting empty lets the
   * next write replace it, as PinGuard and loadPassKey already do for
   * theirs. What the bad file held is lost either way: taps a phone made
   * are made again, and the laptop tells taps apart by their time as well
   * as their number, so numbering from 1 again cannot be mistaken for
   * taps it has applied.
   *
   * Only corruption. A file that could not be read this time (EIO, EMFILE,
   * a permission) still throws, because it may read next time and writing
   * an empty queue over it would throw away what it holds.
   */
  private async queueSnapshotOf(club: string, name: string): Promise<StoredDocument | undefined> {
    try {
      return await this.store.getSnapshot(club, name);
    } catch (error) {
      if (error instanceof StoreReadError && error.corrupt) {
        this.log(`[relay] "${club}": its ${name} document is not readable JSON; starting it again empty.`);
        return undefined;
      }

      throw error;
    }
  }

  private async inboxOf(club: string): Promise<Inbox> {
    const doc = await this.queueSnapshotOf(club, INBOX);
    const body = isPlainObject(doc?.body) ? doc.body : undefined;
    const next = body?.["next"];
    const items = body?.["items"];

    // Only the relay writes this document, but it is read back from disk,
    // and one bad entry should cost that entry rather than every tap.
    return {
      next: typeof next === "number" && Number.isInteger(next) && next > 0 ? next : 1,
      items: Array.isArray(items) ? items.filter(isInboxItem) : [],
    };
  }

  /** The laptop collecting taps. Token only: the queue says who is at the track, and that is the club's business. */
  private async readInbox(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const inbox = await this.inboxOf(club);
    const now = this.now().getTime();

    // Told to the laptop, which polls here anyway: a club whose PIN list
    // somebody has locked would otherwise only find out from a phone at
    // the desk, and changing the staff PIN is what reopens it.
    const pinLockedUntil = await this.pinGuard.lockedUntil(club);

    sendJson(response, 200, {
      ok: true,
      items: inbox.items.filter((item) => now - Date.parse(item.atUtc) < INBOX_MAX_AGE_MS),
      ...(pinLockedUntil > 0 ? { pinLockedUntil: new Date(pinLockedUntil).toISOString() } : {}),
    });
  }

  /** The laptop saying it has applied everything up to n. Only then are taps forgotten, so a laptop that crashed mid-way sees them again. */
  private async ackInbox(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const body = await readBody(request, response, CHECKIN_MAX_BODY);
    const upTo = isPlainObject(body) ? body["upTo"] : undefined;

    if (typeof upTo !== "number" || !Number.isInteger(upTo) || upTo < 0) {
      sendJson(response, 400, { ok: false, error: "upTo must be a whole number." });
      return;
    }

    const left = await this.withLock(club, async () => {
      const inbox = await this.inboxOf(club);
      const items = inbox.items.filter((item) => item.n > upTo);

      await this.store.putSnapshot(club, INBOX, {
        seq: 0,
        publishedAt: this.now().toISOString(),
        body: { next: inbox.next, items },
      });

      return items.length;
    });

    sendJson(response, 200, { ok: true, remaining: left });
  }

  /**
   * The club's manage document, cleaned - read fresh every time, since
   * unlike checkin's roster there is nothing here worth holding in memory
   * between requests. A read that fails throws, exactly as checkinOf does
   * and for the same reason: "could not read it" and "nothing published"
   * are different things, and only listen()'s own catch may answer for
   * both - see readCheckin's doc comment.
   */
  private async currentManage(club: string): Promise<ManageView | undefined> {
    const doc = await this.store.getSnapshot(club, "manage");

    // The key only when there is a manage document to make a pass for - see checkinOf.
    return readManage(club, doc, isPlainObject(doc?.body) ? await this.passKeyOf(club) : Buffer.alloc(0));
  }

  /** The club's manage tunnel - requests waiting for the laptop, answers waiting for a phone - cleaned the same way inboxOf cleans the check-in inbox. */
  private async manageQueueOf(club: string): Promise<ManageQueue> {
    const doc = await this.queueSnapshotOf(club, MANAGE_QUEUE);
    const body = isPlainObject(doc?.body) ? doc.body : undefined;
    const pending = body?.["pending"];
    const answers = body?.["answers"];

    return {
      pending: Array.isArray(pending) ? pending.filter(isManagePendingItem) : [],
      answers: Array.isArray(answers) ? answers.filter(isManageAnswerItem) : [],
    };
  }

  /**
   * Takes `count` slow PIN hashes from this address's allowance and the
   * relay's, or none at all when either has no room - false then, and the
   * caller must not hash. No await in it, so it sits in the same step as
   * the PIN checks around it. Nothing to take for none (a laptop with no
   * salt still hashes with the old single SHA-256, which costs nothing).
   */
  private takeSlowHashes(who: string, count: number): boolean {
    if (count <= 0) {
      return true;
    }

    if (!this.slowHashes.allows(who, count) || !this.slowHashBudget.allows("all", count)) {
      return false;
    }

    for (let i = 0; i < count; i++) {
      this.slowHashes.hit(who);
      this.slowHashBudget.hit("all");
    }

    return true;
  }

  /**
   * A phone trading the manager PIN for a pass - manage's own version of
   * checkinRequest's "list" action, without a roster to hand back: the
   * pass is all a manager needs, and the manage page asks for whatever it
   * wants next through manage/request.
   */
  private async manageSignIn(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = `${club}|${clientAddress(request)}`;

    if (this.manageTraffic.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a minute and try again." });
      return;
    }

    this.manageTraffic.hit(who);

    const body = await readBody(request, response, MANAGE_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That request could not be read." });
      return;
    }

    const manage = await this.currentManage(club);

    if (manage === undefined || !manage.enabled || manage.pinSha256 === "") {
      sendJson(response, 409, { ok: false, error: "The race-day manager is not open over the relay right now." });
      return;
    }

    // Read from disk if this is the first ask since the relay started, and
    // checked and counted with no await between - same reasoning as
    // check-in's own PIN guard, above.
    await this.managePinGuard.ready(club);

    if (this.managePinFailures.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "Too many wrong PINs from this connection. Try again in a few minutes." });
      return;
    }

    const lockedUntil = this.managePinGuard.lockedUntilNow(club);

    if (lockedUntil > 0) {
      const minutes = Math.max(1, Math.ceil((lockedUntil - this.now().getTime()) / 60000));
      const wait = minutes <= 90 ? `${minutes} minute${minutes === 1 ? "" : "s"}` : `${Math.round(minutes / 60)} hours`;

      sendJson(response, 429, {
        ok: false,
        error: `Too many wrong PINs have been tried for this club, so manager sign-in is closed for about ${wait}.`,
      });
      return;
    }

    const offered = typeof body["pin"] === "string" ? (body["pin"] as string).trim() : "";

    // Six digits or more, as IsUsableManagerPin requires - so anything
    // shorter is answered and counted exactly as a wrong PIN, the same
    // treatment check-in gives a badly-shaped guess.
    const wellFormed = /^[0-9]{6,32}$/.test(offered);

    // The hash below is paid for right PINs too - taken first, as check-in's.
    if (!this.takeSlowHashes(who, wellFormed && manage.salt !== "" ? 1 : 0)) {
      sendJson(response, 429, { ok: false, error: SLOW_HASH_BUSY });
      return;
    }

    if (wellFormed && sameHash(managePinHash(club, offered, manage.salt), manage.pinSha256)) {
      // The salt goes back with the pass: the phone makes the key it signs
      // every request with from the PIN it has just typed and this salt,
      // and the laptop makes the same key from its own copy of the PIN -
      // so the laptop can tell a request the phone wrote from one this
      // relay made up (see the manage page's sign()). Nothing secret: the
      // salt is in the published document already. Left out for a laptop
      // that publishes none, whose phones then sign nothing, as before.
      sendJson(response, 200, manage.salt !== "" ? { ok: true, pass: manage.pass, salt: manage.salt } : { ok: true, pass: manage.pass });
      return;
    }

    this.managePinFailures.hit(who);
    await this.managePinGuard.failNow(club);
    sendJson(response, 403, { ok: false, error: "That PIN is not right." });
  }

  /**
   * A phone enqueuing one op for the laptop to run - never runs anything
   * itself, and never looks inside args or confirm: those are
   * RaceDayService's own business, on the other end of the tunnel.
   *
   * reqId is the phone's idempotency key, not the store's: resubmitting
   * the same one replaces a still-pending copy (a retry of a request that
   * itself timed out) and is a no-op against one already answered (a
   * retry after the "ok" here was lost) - so asking twice by accident
   * never runs anything twice.
   */
  private async manageRequest(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = `${club}|${clientAddress(request)}`;

    if (this.manageTraffic.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a minute and try again." });
      return;
    }

    this.manageTraffic.hit(who);

    const body = await readBody(request, response, MANAGE_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That request could not be read." });
      return;
    }

    const reqId = typeof body["reqId"] === "string" ? (body["reqId"] as string) : "";

    if (!MANAGE_REQ_ID.test(reqId)) {
      sendJson(response, 400, { ok: false, error: "reqId must be 8 to 64 letters, digits or dashes." });
      return;
    }

    const pass = typeof body["pass"] === "string" ? (body["pass"] as string) : "";
    const manage = await this.currentManage(club);

    if (manage === undefined || !manage.enabled || manage.pass === "" || !PASS.test(pass) || !sameHash(pass, manage.pass)) {
      sendJson(response, 403, { ok: false, error: "Sign in again." });
      return;
    }

    if (typeof body["op"] !== "string" || body["op"].trim() === "") {
      sendJson(response, 400, { ok: false, error: "The request didn't say what to do." });
      return;
    }

    // Forwarded exactly as HandleJsonAsync itself expects - op, args,
    // confirm - and nothing more. id is not among them: that is reqId's
    // job here, one layer up, and the app-level id it would otherwise be
    // is merely echoed back, which nothing on this path reads.
    const forwarded: Record<string, unknown> = { op: body["op"] };

    if ("args" in body) {
      forwarded["args"] = body["args"];
    }

    if (typeof body["confirm"] === "string") {
      forwarded["confirm"] = body["confirm"];
    }

    // The phone's proof, taken as it comes and carried, not judged. There
    // is nothing here this relay could check - the key belongs to the
    // laptop and the phone - and nothing it should: a proof this relay
    // could verify would be a proof this relay could forge, which is the
    // whole reason the laptop stopped taking its word for it.
    //
    // Bounded, though. Anything oversized or the wrong type is dropped
    // rather than stored, because the queue's own byte budget is shared
    // with every other request waiting (MANAGE_PENDING_MAX_BYTES) and a
    // field nobody here reads is exactly where somebody would put a
    // megabyte. A dropped proof is not a forged one: the laptop refuses
    // the request, which is the safe way round.
    const proof = manageProofOf(body);

    const outcome = await this.withLock(club, async () => {
      // The manage document again, now that nothing else can write: the
      // wait for this lock may have been behind the desk turning the
      // manager off, changing its PIN, or deleting the club - and a request
      // queued after any of those is one the pass no longer entitles. Read
      // raw rather than through currentManage, which may want the pass key
      // and so this same lock.
      const latest = await this.store.getSnapshot(club, "manage");
      const latestBody = isPlainObject(latest?.body) ? latest.body : undefined;

      if (latestBody === undefined || latestBody["enabled"] !== true || managePinOf(latestBody) !== manage.pinSha256) {
        return "signed-out" as const;
      }

      const queue = await this.manageQueueOf(club);
      const now = this.now();
      const cutoff = now.getTime() - MANAGE_QUEUE_MAX_AGE_MS;
      const answers = queue.answers.filter((item) => Date.parse(item.answeredAt) > cutoff);

      // Already handled - the phone missed the "ok" the first time, or is
      // retrying for some other reason. The answer is still there to poll
      // for; nothing here has to run twice for it.
      if (answers.some((item) => item.reqId === reqId)) {
        return "queued" as const;
      }

      const pending = queue.pending.filter((item) => Date.parse(item.enqueuedAt) > cutoff && item.reqId !== reqId);
      const waiting = [...pending, { reqId, body: forwarded, ...proof, enqueuedAt: now.toISOString() }];

      // Full is refused, not made room for - the same choice check-in's
      // own tap queue makes, and for the same reason: dropping the oldest
      // request to take this one would lose it silently, and a refusal at
      // least tells the manager something is wrong. Full by bytes as well
      // as by count: the count alone let the queue grow to twenty
      // megabytes, and it was the club's quota for results that paid for
      // it - see MANAGE_PENDING_MAX_BYTES.
      if (pending.length >= MANAGE_QUEUE_MAX_ITEMS || jsonBytes(waiting) > MANAGE_PENDING_MAX_BYTES) {
        return "full" as const;
      }

      await this.store.putSnapshot(club, MANAGE_QUEUE, {
        seq: 0,
        publishedAt: now.toISOString(),
        body: { pending: waiting, answers },
      });

      return "queued" as const;
    });

    if (outcome === "signed-out") {
      sendJson(response, 403, { ok: false, error: "Sign in again." });
      return;
    }

    if (outcome === "full") {
      // Logged, as check-in's full queue is: the manager's page says so,
      // but whoever runs the relay should be able to see it too.
      this.log(`[relay] "${club}": the manager's request queue is full; a request was refused.`);
      sendJson(response, 503, {
        ok: false,
        error: "The race-day manager is busy - the laptop has not caught up with recent requests yet. Try again shortly.",
      });
      return;
    }

    sendJson(response, 200, { ok: true });
  }

  /** A phone polling for the answer to one request it made earlier. */
  private async manageAnswerPoll(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = `${club}|${clientAddress(request)}`;

    if (this.manageTraffic.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a minute and try again." });
      return;
    }

    this.manageTraffic.hit(who);

    const body = await readBody(request, response, MANAGE_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That request could not be read." });
      return;
    }

    const reqId = typeof body["reqId"] === "string" ? (body["reqId"] as string) : "";

    if (!MANAGE_REQ_ID.test(reqId)) {
      sendJson(response, 400, { ok: false, error: "reqId must be 8 to 64 letters, digits or dashes." });
      return;
    }

    const pass = typeof body["pass"] === "string" ? (body["pass"] as string) : "";
    const manage = await this.currentManage(club);

    if (manage === undefined || !manage.enabled || manage.pass === "" || !PASS.test(pass) || !sameHash(pass, manage.pass)) {
      sendJson(response, 403, { ok: false, error: "Sign in again." });
      return;
    }

    const queue = await this.manageQueueOf(club);
    const cutoff = this.now().getTime() - MANAGE_QUEUE_MAX_AGE_MS;
    const found = queue.answers.find((item) => item.reqId === reqId && Date.parse(item.answeredAt) > cutoff);

    if (found !== undefined) {
      sendJson(response, 200, { ok: true, ready: true, response: found.response });
      return;
    }

    sendJson(response, 200, { ok: true, ready: false });
  }

  /** The laptop collecting requests a phone has made. Token only, exactly like readInbox: what a manager has asked for is the club's own business. */
  private async readManageInbox(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const queue = await this.manageQueueOf(club);
    const now = this.now().getTime();
    const cutoff = now - MANAGE_QUEUE_MAX_AGE_MS;

    // Told to the laptop, which polls here anyway - the same reasoning as
    // readInbox telling it checkin's own pinLockedUntil.
    const pinLockedUntil = await this.managePinGuard.lockedUntil(club);

    sendJson(response, 200, {
      ok: true,
      // The proof goes out exactly as it came in. Left off an item that
      // never carried one, rather than sent as empty strings: the laptop
      // reads a missing field and an empty one the same way, and not
      // inventing fields keeps this answer the size it has always been for
      // a club with no enrolled phones.
      items: queue.pending
        .filter((item) => Date.parse(item.enqueuedAt) > cutoff)
        .map((item) => ({ reqId: item.reqId, body: item.body, ...manageProofOf(item) })),
      ...(pinLockedUntil > 0 ? { pinLockedUntil: new Date(pinLockedUntil).toISOString() } : {}),
    });
  }

  /**
   * The laptop posting back what HandleJsonAsync answered for one request.
   * Removes it from pending and files it under answers in the same
   * write, so a phone's poll finds it and the laptop never fetches it
   * again from manage/inbox.
   */
  private async answerManage(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const body = await readBody(request, response, MANAGE_ANSWER_MAX_BODY);

    if (body === "too-big") {
      sendJson(response, 413, { ok: false, error: `An answer may be at most ${MANAGE_ANSWER_MAX_BODY} bytes.` });
      return;
    }

    if (body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That request could not be read." });
      return;
    }

    const reqId = typeof body["reqId"] === "string" ? (body["reqId"] as string) : "";

    if (!MANAGE_REQ_ID.test(reqId)) {
      sendJson(response, 400, { ok: false, error: "reqId must be 8 to 64 letters, digits or dashes." });
      return;
    }

    if (!("response" in body)) {
      sendJson(response, 400, { ok: false, error: 'An answer needs "response".' });
      return;
    }

    await this.withLock(club, async () => {
      const queue = await this.manageQueueOf(club);
      const now = this.now();
      const cutoff = now.getTime() - MANAGE_QUEUE_MAX_AGE_MS;

      const pending = queue.pending.filter((item) => Date.parse(item.enqueuedAt) > cutoff && item.reqId !== reqId);
      const answers = queue.answers.filter((item) => Date.parse(item.answeredAt) > cutoff && item.reqId !== reqId);

      // Newest kept when there are more than the cap, because an answer
      // nobody has collected in a while is one nobody is waiting for any
      // more. Results used to be capped the same way and no longer are -
      // a club's history is worth keeping, a stale tunnel answer is not. By bytes as well as by count, so answers
      // can never grow the tunnel past its own budget - see
      // MANAGE_ANSWERS_MAX_BYTES. The answer just posted always stays:
      // refusing it instead would leave its request pending, and the
      // laptop posting it again at every poll.
      const kept = newestWithin(
        [...answers, { reqId, response: body["response"], answeredAt: now.toISOString() }].slice(-MANAGE_QUEUE_MAX_ITEMS),
        MANAGE_ANSWERS_MAX_BYTES,
      );

      await this.store.putSnapshot(club, MANAGE_QUEUE, {
        seq: 0,
        publishedAt: now.toISOString(),
        body: { pending, answers: kept },
      });
    });

    sendJson(response, 200, { ok: true });
  }

  // ------------------------------------------------------------ sign-up

  /**
   * The club's signup document, cleaned - read fresh every time, as
   * currentManage is. A read that fails throws, for the same reason.
   */
  private async currentSignup(club: string): Promise<SignupView | undefined> {
    return readSignup(await this.store.getSnapshot(club, "signup"));
  }

  /**
   * The sign-ups the relay is holding for a club, cleaned the way inboxOf
   * cleans the check-in queue, and without any past SIGNUP_KEEP_MS - so a
   * sign-up is forgotten on time even when nothing is written for weeks.
   */
  private async signupBoxOf(club: string): Promise<SignupItem[]> {
    const doc = await this.queueSnapshotOf(club, SIGNUP_BOX);
    const body = isPlainObject(doc?.body) ? doc.body : undefined;
    const items = body?.["items"];
    const now = this.now().getTime();

    return (Array.isArray(items) ? items : [])
      .map(signupItemFrom)
      .filter((item): item is SignupItem => item !== undefined && now - signupItemAge(item) < SIGNUP_KEEP_MS);
  }

  /**
   * The pass key for sign-in and the status poll - but only when there is a
   * signup document or a held sign-up to make passes for, as checkinOf and
   * currentManage do for theirs. Both routes answer anyone, with no
   * credentials, and making the key writes passkey.json: before this, a
   * club that had just deleted everything got its folder back from the
   * first stranger to POST {} at either one, and every configured club that
   * had never used sign-up grew a file the same way. With neither a
   * document nor a held sign-up, signupByPass has no hash to compare a
   * pass against, so the empty key is never used to make or check one -
   * both routes go on to answer "not open" exactly as before.
   */
  private async signupKeyOf(club: string, view: SignupView | undefined, box: readonly SignupItem[]): Promise<Buffer> {
    return view !== undefined || box.length > 0 ? await this.passKeyOf(club) : Buffer.alloc(0);
  }

  /**
   * Which invite a phone's pass is for, or undefined.
   *
   * Compared with sameHash against the pass of every sign-up held and, while
   * sign-up is open, every invite listed - all of them, whether or not one
   * before matched, as racerByPass does and for its reason: a pass is the
   * secret exactly as sent. Up to SIGNUP_MAX_ITEMS + SIGNUP_MAX_INVITES
   * passes, each two HMACs - so each is made once per pass key and kept
   * (signupPassOf), and an unauthenticated request costs comparisons, not
   * HMACs. 17 Oct 2026, Bug Hunt 8, A20-3.
   *
   * A sign-up already held is found whether sign-up is open or not, and
   * whether or not its invite is still listed: the laptop takes an invite
   * off as soon as it has answered it, and the phone must still be able to
   * read that answer.
   */
  private signupByPass(club: string, key: Buffer, view: SignupView | undefined, box: readonly SignupItem[], pass: string): string | undefined {
    const hashes = new Set(box.map((item) => item.h));

    if (view !== undefined && view.enabled) {
      for (const h of view.invites.keys()) {
        hashes.add(h);
      }
    }

    let found: string | undefined;

    for (const h of hashes) {
      if (sameHash(pass, this.signupPassOf(club, key, h))) {
        found = h;
      }
    }

    return found;
  }

  /**
   * keyedPass(key, "signup", club, h), made once per key and kept. The
   * held map is cleared when it reaches the most hashes signupByPass can
   * ever compare against, so it stays bounded whatever comes and goes.
   * 17 Oct 2026, Bug Hunt 8, A20-3.
   */
  private signupPassOf(club: string, key: Buffer, h: string): string {
    let made = this.signupPasses.get(key);

    if (made === undefined) {
      made = new Map<string, string>();
      this.signupPasses.set(key, made);
    }

    const name = `${club}|${h}`;
    const kept = made.get(name);

    if (kept !== undefined) {
      return kept;
    }

    if (made.size >= SIGNUP_MAX_ITEMS + SIGNUP_MAX_INVITES) {
      made.clear();
    }

    const pass = keyedPass(key, "signup", club, h);
    made.set(name, pass);

    return pass;
  }

  /** Everything a phone is told once it holds a pass for invite h: the pass itself, what to show above the form, and where its sign-up stands. */
  private signupAnswer(club: string, key: Buffer, view: SignupView | undefined, box: readonly SignupItem[], h: string): Record<string, unknown> {
    return {
      ok: true,
      pass: keyedPass(key, "signup", club, h),
      event: view?.event ?? "",
      classes: view?.classes ?? [],
      status: signupStatusOf(box.find((item) => item.h === h)),
    };
  }

  /**
   * A phone trading an invite code for a pass - check-in's "list" and the
   * manager's signin, for a code that opens one sign-up and nothing else.
   *
   *   { pin, pass? }  ->  { pass, event, classes, status }
   *
   * A pass first, as check-in takes one, so a phone that has signed in once
   * carries on through a lockout somebody else's guessing caused - and a
   * stale pass with no code beside it is refused without counting as a
   * guess. Then the code, which is guarded exactly as the other PINs are:
   * per connection and across the club, checked and counted with no await
   * in between.
   *
   * A right code whose sign-up has already been answered opens that answer,
   * even once the laptop has taken the invite off - "open the link in your
   * email again" is how a driver finds out. A right code that has expired
   * is told so, not that it is wrong, and is not counted: whoever typed it
   * had it, and telling them it expired gives a guesser nothing.
   */
  private async signupSignIn(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = `${club}|${clientAddress(request)}`;

    if (this.signupTraffic.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a minute and try again." });
      return;
    }

    this.signupTraffic.hit(who);

    const body = await readBody(request, response, SIGNUP_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That request could not be read." });
      return;
    }

    const [view, box] = await Promise.all([this.currentSignup(club), this.signupBoxOf(club)]);
    const key = await this.signupKeyOf(club, view, box);
    const now = this.now().getTime();
    const pass = typeof body["pass"] === "string" ? (body["pass"] as string) : "";
    const offered = normaliseInviteCode(typeof body["pin"] === "string" ? (body["pin"] as string) : "");

    // Six digits or eight: the laptop's invites are eight, shown as
    // "1234 5678", and normaliseInviteCode has already taken out the space.
    // Anything else is answered and counted further down exactly as a wrong
    // code, the same treatment check-in gives a badly-shaped PIN - and is
    // never hashed, since it cannot be any invite's code.
    const wellFormed = /^[0-9]{6,8}$/.test(offered);

    // The typed code's hash, worked out at most once per request. Once the
    // laptop has published a salt it is a 100,000-round PBKDF2 - about a
    // tenth of a second with the whole relay waiting on it - so it must
    // only ever be paid for a request the PIN limits below can see and
    // count, or for a phone holding a pass this relay really issued.
    // Nothing else may reach it: before this was so, any made-up pass with
    // any code beside it cost a hash ahead of every limit, 300 a minute
    // per address per club, and a single connection could stall every
    // club's live timing.
    let typedHash: string | undefined;

    // Set when the pass branch hashed the typed code and it was NOT the
    // pass's own - the only case the club-lock branch below counts.
    let passMismatch = false;
    const hashOfTyped = (): string => (typedHash ??= inviteHashFor(club, offered, view, box));

    if (PASS.test(pass)) {
      // The pass alone first: a few HMACs, nothing slow. Only a pass that
      // opens something here is worth comparing with the code at all.
      const byPass = this.signupByPass(club, key, view, box, pass);

      // With a code beside it, the pass counts only if it is that code's
      // own. A phone still holding the pass from an earlier invite - a
      // driver signing up a second child with a second invite - must be
      // taken to the new one, not shown the old one's answer again. So the
      // code is hashed here, but only for a real pass (which takes having
      // signed in with a real code once) and only for a code that could be
      // one. Not held back for a connection shut out for wrong codes: at a
      // track a whole paddock shares one address, and a phone that has
      // signed in carries on through a block somebody else's guessing
      // caused. A code that is not the pass's own is judged further down,
      // guarded and counted as ever, reusing this same hash.
      let h: string | undefined;

      if (offered === "") {
        h = byPass;
      } else if (byPass !== undefined && wellFormed) {
        // But never while this connection is shut out for wrong codes or
        // the club's sign-up is locked. A code that is not the pass's own
        // would only be refused with a 429 further down, and once this
        // address is blocked that refusal counts nothing - so a pass holder
        // looping wrong codes paid a slow hash every time for free, 300 a
        // minute per address, and a couple of addresses could hold the
        // event loop for every club. While shut out the pass is simply
        // taken at its word, as if no code were beside it: a phone
        // reopening its own invite link still gets its answer, which is
        // the paddock case the paragraph above protects, and nothing is
        // hashed. The one thing given up is the second-child case, which
        // shows the first sign-up again until the block lifts - better
        // than a stalled relay, and the code is judged properly then.
        // ready() is awaited here, and nothing awaits between these
        // checks and the hash, so a burst is judged against the count as
        // it stands.
        await this.signupPinGuard.ready(club);

        // And the same, taken at its word, when this address or the relay
        // has used up its slow hashes (see takeSlowHashes): a pass holder
        // sending its own code over and over must not cost a hash each time.
        if (
          this.signupPinFailures.blocked(who) ||
          this.signupPinGuard.lockedUntilNow(club) > 0 ||
          !this.takeSlowHashes(who, typedHash === undefined && view?.salt ? 1 : 0)
        ) {
          h = byPass;
        } else {
          h = hashOfTyped() === byPass ? byPass : undefined;
          passMismatch = h === undefined;
        }
      }

      // A pass is good for a sign-up held, or an invite still usable. One
      // for an invite that has since expired is simply not used, and the
      // code beside it - if any - is judged as ever.
      if (h !== undefined && (box.some((item) => item.h === h) || (view?.enabled === true && (view.invites.get(h) ?? 0) > now))) {
        sendJson(response, 200, this.signupAnswer(club, key, view, box, h));
        return;
      }
    }

    if (view === undefined || !view.enabled) {
      sendJson(response, 409, { ok: false, error: "Sign-up is not open right now." });
      return;
    }

    // No code at all is no guess at all - a phone reopening the page with a
    // pass that no longer opens anything - and is not counted.
    if (offered === "") {
      sendJson(response, 403, { ok: false, error: "Type the code from your invite." });
      return;
    }

    // Read from disk if this is the first ask since the relay started;
    // after this, nothing awaits until the guess has been judged and - if
    // wrong - counted. See checkinRequest for why that order matters.
    await this.signupPinGuard.ready(club);

    if (this.signupPinFailures.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "Too many wrong codes from this connection. Try again in a few minutes." });
      return;
    }

    const lockedUntil = this.signupPinGuard.lockedUntilNow(club);

    if (lockedUntil > 0) {
      const minutes = Math.max(1, Math.ceil((lockedUntil - now) / 60000));
      const wait = minutes <= 90 ? `${minutes} minute${minutes === 1 ? "" : "s"}` : `${Math.round(minutes / 60)} hours`;

      // The pass branch above does not hash while the club is locked, so
      // this is only reached with a hash paid when the lock came on in the
      // moment between (a guess from elsewhere landing during the await).
      // A pass holder whose code was hashed above and turned out not to be
      // the pass's own still cost a slow hash, and the club lock means it
      // is not judged or counted below. Count it against this connection
      // alone (not the club, whose lock is already on).
      // A pass's own code that simply arrived after its invite expired is
      // right, not a guess, and is not counted - as it is not without a lock.
      if (passMismatch) {
        this.signupPinFailures.hit(who);
      }

      sendJson(response, 429, {
        ok: false,
        error:
          `Too many wrong codes have been tried for this club, so sign-up is closed for about ${wait}. ` +
          "Phones already signed in keep working, or ask at the timing desk.",
      });
      return;
    }

    // wellFormed is worked out at the top. A badly-shaped code is answered
    // and counted exactly as a wrong one; a well-formed one is hashed now -
    // or not again, if the pass above already did. Its slow hash is taken
    // first, for right and expired codes as much as wrong ones.
    if (!this.takeSlowHashes(who, wellFormed && typedHash === undefined && view.salt !== "" ? 1 : 0)) {
      sendJson(response, 429, { ok: false, error: SLOW_HASH_BUSY });
      return;
    }

    const h = wellFormed ? hashOfTyped() : "";

    // A lookup by hash, as a racer PIN is found: the hash is what the
    // laptop published, not the code, and how much of one a guess matched
    // says nothing about which code to try next.
    const expires = wellFormed ? view.invites.get(h) : undefined;
    const held = wellFormed && box.some((item) => item.h === h);

    if (held || (expires !== undefined && expires > now)) {
      sendJson(response, 200, this.signupAnswer(club, key, view, box, h));
      return;
    }

    if (expires !== undefined) {
      sendJson(response, 403, { ok: false, error: "That invite has expired. Ask the timing desk for a new one." });
      return;
    }

    this.signupPinFailures.hit(who);
    await this.signupPinGuard.failNow(club);
    sendJson(response, 403, { ok: false, error: "That code is not right." });
  }

  /**
   * A phone that has signed in asking where its sign-up stands - polled
   * every ten seconds or so while the desk has it, which is how "You are
   * in!" appears by itself. A pass only; nothing here is a guess.
   */
  private async signupStatusPoll(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = `${club}|${clientAddress(request)}`;

    if (this.signupTraffic.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a minute and try again." });
      return;
    }

    this.signupTraffic.hit(who);

    const body = await readBody(request, response, SIGNUP_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That request could not be read." });
      return;
    }

    const [view, box] = await Promise.all([this.currentSignup(club), this.signupBoxOf(club)]);
    const key = await this.signupKeyOf(club, view, box);
    const pass = typeof body["pass"] === "string" ? (body["pass"] as string) : "";
    const h = PASS.test(pass) ? this.signupByPass(club, key, view, box, pass) : undefined;
    const held = h !== undefined && box.some((item) => item.h === h);

    if (h === undefined || (!held && (view?.invites.get(h) ?? 0) <= this.now().getTime())) {
      // Nothing held and nothing open: sign-up closed since, the invite
      // cancelled or expired, or a pass that was never one.
      if (view === undefined || !view.enabled) {
        sendJson(response, 409, { ok: false, error: "Sign-up is not open right now." });
        return;
      }

      sendJson(response, 403, { ok: false, error: "Sign in again with the code from your invite." });
      return;
    }

    // The same answer as signin's, less the pass the phone already holds.
    const answer = this.signupAnswer(club, key, view, box, h);
    delete answer["pass"];
    sendJson(response, 200, answer);
  }

  /**
   * A new driver sending their sign-up. Needs the pass an invite gave out,
   * an invite still usable, and no answer yet: once the desk has answered,
   * that invite's sign-up is done and cannot be sent again. Until then a
   * phone may send it as often as it likes - "Change my details" - and each
   * replaces the last, so the desk only ever sees one per invite.
   */
  private async signupSubmit(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = `${club}|${clientAddress(request)}`;

    if (this.signupTraffic.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a minute and try again." });
      return;
    }

    this.signupTraffic.hit(who);

    const body = await readBody(request, response, SIGNUP_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That sign-up could not be read." });
      return;
    }

    const view = await this.currentSignup(club);

    if (view === undefined || !view.enabled) {
      sendJson(response, 409, { ok: false, error: "Sign-up is not open right now." });
      return;
    }

    const [box, key] = await Promise.all([this.signupBoxOf(club), this.passKeyOf(club)]);
    const pass = typeof body["pass"] === "string" ? (body["pass"] as string) : "";
    const h = PASS.test(pass) ? this.signupByPass(club, key, view, box, pass) : undefined;

    if (h === undefined) {
      sendJson(response, 403, { ok: false, error: "Sign in again with the code from your invite." });
      return;
    }

    if (box.some((item) => item.h === h && item.state !== "pending")) {
      sendJson(response, 403, { ok: false, error: "The timing desk has already answered this sign-up." });
      return;
    }

    if ((view.invites.get(h) ?? 0) <= this.now().getTime()) {
      sendJson(response, 403, {
        ok: false,
        error: view.invites.has(h)
          ? "That invite has expired. Ask the timing desk for a new one."
          : "That invite is no longer open. Ask at the timing desk.",
      });
      return;
    }

    const form = signupFormFrom(body["form"], view.classes);

    if (typeof form === "string") {
      sendJson(response, 400, { ok: false, error: form });
      return;
    }

    const outcome = await this.withLock(club, async () => {
      // Read again inside the lock: the desk's answer may have landed
      // since the check above, and a sign-up sent over an answer would
      // take the answer away from the phone that is waiting for it.
      const current = await this.signupBoxOf(club);

      if (current.some((item) => item.h === h && item.state !== "pending")) {
        return "answered" as const;
      }

      // And the signup document again, for the same reason: everything
      // above was judged before this request waited for the lock, and the
      // wait may have been behind the desk closing sign-up, cancelling this
      // invite, or deleting the whole club. Written anyway, the form - a
      // name, an email, a transponder number - sat in the folder of a club
      // that had just asked to be forgotten, or in front of a desk that had
      // just cancelled the invite it came with.
      const latest = await this.currentSignup(club);

      if (latest === undefined || !latest.enabled) {
        return "closed" as const;
      }

      if ((latest.invites.get(h) ?? 0) <= this.now().getTime()) {
        return "gone" as const;
      }

      const others = current.filter((item) => item.h !== h);

      // Full is refused, not made room for, as check-in's tap queue is.
      if (others.length >= SIGNUP_MAX_ITEMS) {
        return "full" as const;
      }

      const item: SignupItem = { h, state: "pending", submittedAt: this.now().toISOString(), form };

      await this.store.putSnapshot(club, SIGNUP_BOX, {
        seq: 0,
        publishedAt: this.now().toISOString(),
        body: { items: [...others, item] },
      });

      return item;
    });

    if (outcome === "answered") {
      sendJson(response, 403, { ok: false, error: "The timing desk has already answered this sign-up." });
      return;
    }

    if (outcome === "closed") {
      sendJson(response, 409, { ok: false, error: "Sign-up is not open right now." });
      return;
    }

    if (outcome === "gone") {
      sendJson(response, 403, { ok: false, error: "That invite is no longer open. Ask at the timing desk." });
      return;
    }

    if (outcome === "full") {
      this.log(`[relay] "${club}": the sign-up list is full; a sign-up was refused.`);
      sendJson(response, 503, {
        ok: false,
        error: "Sign-up is busy - the timing desk has not caught up with recent sign-ups yet. Try again later, or ask at the desk.",
      });
      return;
    }

    // No name in the log: who signs up is the club's business, and the
    // laptop says it in its own console.
    this.log(`[relay] "${club}": a driver sign-up is waiting for the laptop.`);
    sendJson(response, 200, { ok: true, status: signupStatusOf(outcome) });
  }

  /**
   * The laptop collecting sign-ups waiting for the desk. Token only: these
   * are names, emails and transponder numbers. Every one still waiting,
   * every time - nothing is taken off by collecting, only by answering, so
   * a laptop that closes half way loses nothing (RelaySignupDesk).
   */
  /**
   * Whether this club offers drivers their own night.
   *
   * ABSENT MEANS NO, like every other switch here, and off is what the
   * laptop ships with: this publishes a named person's results to an
   * address, and the club notes and the room's vote both ship off for
   * less.
   */
  /**
   * Whether this club has NOT switched on the channel a document is being
   * published to - see SWITCHED_CHANNELS.
   *
   * False for every channel without a switch, which is all but two of
   * them, and it costs nothing for those: the lookup is in memory and no
   * snapshot is read.
   */
  private async switchedOff(club: string, channel: string): Promise<boolean> {
    if (!SWITCHED_CHANNELS.has(channel)) {
      return false;
    }

    if (channel === LISTEN_CHANNEL) {
      return !(await this.listeningOpenFor(club));
    }

    // 22 Oct 2026, driver experience: the drivers' details go with the
    // driver-requests switch; their live evening, like their nights, with
    // the nights switch.
    if (channel === DRIVER_ME_CHANNEL || channel === GARAGE_CHANNEL) {
      return !(await this.driverAsksOpenFor(club));
    }

    return !(await this.nightsOpenFor(club));
  }

  /**
   * Bytes held outside the store - a driver's clip - in ranges, because a
   * phone's player asks for them that way and will not play (or seek) them
   * served whole without. ?download=1 (download here) hands it over as a
   * file to keep. Factored out of the podium video route (22 Oct 2026,
   * driver experience) so the clips were served by the very same code,
   * its two bug-hunt fixes included; kept when that route went (28 Oct
   * 2026).
   */
  private async sendRanged(
    request: IncomingMessage,
    response: ServerResponse,
    total: number,
    type: string,
    fileName: string,
    download: boolean,
    read: (start: number, end: number) => Promise<Buffer | undefined>,
  ): Promise<void> {
    let start = 0;
    let end = total - 1;
    let partial = false;

    const range = request.headers.range;

    if (typeof range === "string" && range.length > 0) {
      const m = /^bytes=(\d*)-(\d*)$/.exec(range.trim());

      if (m === null || (m[1] === "" && m[2] === "")) {
        response.writeHead(416, { ...BASE_HEADERS, "content-range": `bytes */${total}`, "content-length": 0 });
        response.end();
        return;
      }

      if (m[1] === "") {
        // The last N bytes.
        const suffix = Number(m[2]);
        start = Math.max(0, total - suffix);
      } else {
        start = Number(m[1]);
        end = m[2] === "" ? total - 1 : Math.min(Number(m[2]), total - 1);
      }

      if (!Number.isFinite(start) || !Number.isFinite(end) || start > end || start >= total) {
        response.writeHead(416, { ...BASE_HEADERS, "content-range": `bytes */${total}`, "content-length": 0 });
        response.end();
        return;
      }

      partial = true;
    }

    response.writeHead(partial ? 206 : 200, {
      ...BASE_HEADERS,
      "content-type": type,
      "accept-ranges": "bytes",
      "content-length": end - start + 1,
      "cache-control": "private, max-age=3600",
      ...(partial ? { "content-range": `bytes ${start}-${end}/${total}` } : {}),
      ...(download ? { "content-disposition": `attachment; filename="${fileName}"` } : {}),
    });

    if (request.method === "HEAD") {
      response.end();
      return;
    }

    // A megabyte at a time, so a whole file is never in memory at once.
    const chunk = 1024 * 1024;

    for (let at = start; at <= end; at += chunk) {
      const piece = await read(at, Math.min(end, at + chunk - 1)).catch(() => undefined);

      if (piece === undefined || piece.length === 0) {
        break;
      }

      if (response.destroyed) {
        return;
      }

      // Woken by "close" as well as "drain" (22 Oct 2026, bug hunt): a
      // phone that drops the connection never drains, and the handler -
      // with its megabyte - waited for ever.
      if (!response.write(piece)) {
        await new Promise<void>((resolve) => {
          const done = (): void => {
            response.off("drain", done);
            response.off("close", done);
            resolve();
          };

          response.on("drain", done);
          response.on("close", done);
        });

        if (response.destroyed) {
          return;
        }
      }
    }

    response.end();
  }

  private async nightsOpenFor(club: string): Promise<boolean> {
    const body = (await this.store.getSnapshot(club, NIGHTS_OPEN_DOC))?.body;

    return isPlainObject(body) && body["open"] === true;
  }

  /**
   * The laptop saying whether its club offers drivers their own night -
   * sent when the setting changes and on every start, so a club that
   * turns it off is CLOSED here rather than merely stopping publishing.
   *
   * Closing throws away every night held, for the reason closing
   * shout-outs does: a club that has just declined this should not have a
   * folder full of its drivers' evenings sitting on a relay.
   */
  private async setNightsOpen(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const body = await readBody(request, response, NOTES_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That could not be read." });
      return;
    }

    const open = body["open"] === true;

    await this.store.putSnapshot(club, NIGHTS_OPEN_DOC, {
      seq: this.now().getTime(),
      publishedAt: this.now().toISOString(),
      body: { open },
    });

    if (!open) {
      await this.withLock(club, async () => {
        // WHY (20 Oct 2026, Bug Hunt 8): ids from the directory listing.
        // listItems read and parsed every file and quietly skipped one that
        // would not read (corrupt, EIO), so that file was never deleted and
        // came back when the switch went on again. itemSizes needs no parse.
        for (const item of await this.store.itemSizes(club, NIGHTS_CHANNEL)) {
          await this.store.deleteItem(club, NIGHTS_CHANNEL, item.id);
        }

        // The cached listing with them. A private collection is never
        // served through the listing cache, so this is belt and braces
        // rather than a leak - but prune pairs the two and so does the
        // listen switch, and a cache that outlived its files is the bug
        // that taught them to.
        this.forgetListing(club, NIGHTS_CHANNEL);

        // AND THE DRIVERS' LIVE EVENING WITH THEM (22 Oct 2026, driver
        // experience) - where each of them is racing next, under their
        // badge id. The store has no way to delete a snapshot, so it is
        // written over with nothing, which every reader takes as absent;
        // seq 0, so the laptop's next publish after reopening always wins.
        await this.blankSnapshot(club, DRIVER_LIVE_CHANNEL);

        // AND THE PHONES SIGNED UP FOR ALERTS (7 Nov 2026, Bug Hunt 13) -
        // each one's push address and keys, under the driver's badge id.
        // Nothing else ever took them off, and a phone cannot sign itself
        // off while nights are closed, so they stayed until Delete
        // everything and were buzzed again the night the switch went back on.
        await this.forgetPushSubs(club);
      });

      // And their clips - the announcer calling a named driver's race.
      await this.wipeClips(club);
    }

    sendJson(response, 200, { ok: true, open });
  }

  /**
   * One driver's own night, for somebody holding that driver's badge id.
   *
   * NO LOGIN, AND THAT IS THE DESIGN RATHER THAN A SHORTCUT. The id is
   * twenty hex characters HMAC'd from the transponder under the club's own
   * thirty-two random bytes - eighty bits that cannot be guessed, that the
   * club can revoke for everybody at once, and that are already printed on
   * the badge in the driver's hand. "The id itself is the credential" is
   * the rule check-in was built on and this keeps it. A password would be
   * a worse version of something that already works.
   *
   * THE ID IS NEVER THE TRANSPONDER, which is the other half of it: a
   * transponder on a public address is a key that follows somebody from
   * meeting to meeting.
   *
   * ANSWERED THE SAME WAY whether the club is closed, the driver has no
   * night or the id is not an id at all - a 404 with no detail. Three
   * different answers would let somebody working through guesses learn
   * which ids exist, which is the one thing eighty bits are there to
   * prevent.
   */
  /**
   * PUSH ALERTS (6 Nov 2026): a phone signing up for a driver's alerts, or
   * off them. The badge code in the body is the credential, as for the
   * night itself, held to the same allowance and the same answer: a code
   * that is not a driver tonight, or a club not offering nights, is "Not
   * found" whichever it was. The push address must be one of the push
   * services' (pushSubFrom), so the relay never posts anywhere else.
   */
  private async pushSignUp(request: IncomingMessage, response: ServerResponse, club: string, on: boolean): Promise<void> {
    const who = `${club}|${clientAddress(request)}`;

    if (this.push === undefined) {
      sendJson(response, 404, { ok: false, error: "This relay does not send alerts." });
      return;
    }

    if (this.checkinReads.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a minute and try again." });
      return;
    }

    this.checkinReads.hit(who);
    const body = await readBody(request, response, CHECKIN_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That could not be read." });
      return;
    }

    const id = typeof body["id"] === "string" ? body["id"].trim().toLowerCase() : "";

    if (!DRIVER_ID.test(id) || !(await this.nightsOpenFor(club))) {
      sendJson(response, 404, { ok: false, error: "Not found" });
      return;
    }

    const push = this.push;

    if (!on) {
      const endpoint = isPlainObject(body["sub"]) && typeof body["sub"]["endpoint"] === "string" ? body["sub"]["endpoint"] : "";
      await this.withLock(club, () => push.unsubscribe(club, id, endpoint));
      sendJson(response, 200, { ok: true });
      return;
    }

    const known = (await this.liveEntryOf(club, id)) !== undefined
      || (await this.store.getItem(club, NIGHTS_CHANNEL, id).catch(() => undefined)) !== undefined;

    if (!known) {
      sendJson(response, 404, { ok: false, error: "Not found" });
      return;
    }

    const sub = pushSubFrom(body["sub"], this.now().toISOString());

    if (sub === undefined) {
      sendJson(response, 400, { ok: false, error: "This phone's alerts could not be set up." });
      return;
    }

    // Written inside the club's lock, with the switch looked at again there
    // (7 Nov 2026, Bug Hunt 13), as driverAsk and signupSubmit do: closing
    // nights and Delete everything both take the lock, so a sign-up that
    // read "open" before either cannot land after it and put a phone's
    // push address back in a club that had just let them go.
    const outcome = await this.withLock(club, async () =>
      (await this.nightsOpenFor(club)) ? push.subscribe(club, id, sub) : ("closed" as const),
    );

    if (outcome === "closed") {
      sendJson(response, 404, { ok: false, error: "Not found" });
      return;
    }

    if (outcome === "full") {
      sendJson(response, 507, { ok: false, error: "This club cannot take any more phones for alerts." });
      return;
    }

    sendJson(response, 200, { ok: true });
  }

  private async readOneNight(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = `${club}|${clientAddress(request)}`;

    // Read before the limit is chosen (22 Oct 2026, driver experience): the
    // live poll, {part:"live"}, has an allowance of its own - see
    // nightLiveReads - and only the body says which this is. A body is at
    // most CHECKIN_MAX_BODY, so reading one before refusing costs nothing.
    if (this.checkinReads.blocked(who) && this.nightLiveReads.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a minute and try again." });
      return;
    }

    const body = await readBody(request, response, CHECKIN_MAX_BODY);
    const liveOnly = isPlainObject(body) && body["part"] === "live";
    const reads = liveOnly ? this.nightLiveReads : this.checkinReads;

    if (reads.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a minute and try again." });
      return;
    }

    reads.hit(who);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That could not be read." });
      return;
    }

    const id = typeof body["id"] === "string" ? body["id"].trim().toLowerCase() : "";

    if (!DRIVER_ID.test(id)) {
      sendJson(response, 404, { ok: false, error: "Not found" });
      return;
    }

    if (!(await this.nightsOpenFor(club))) {
      sendJson(response, 404, { ok: false, error: "Not found" });
      return;
    }

    // THEIR LIVE EVENING (22 Oct 2026, driver experience) - next race,
    // marshal duty, transponder, the desk's answers - from the driver's
    // own entry in the private live document, when it is fresh. A driver
    // with a live entry and no night yet (before their first race) is
    // answered with night: null rather than the 404, which is now only for
    // somebody with neither.
    const live = await this.liveEntryOf(club, id);
    const liveField = live === undefined ? {} : { live: { ...live.entry, at: live.at } };

    // The page's twenty-second poll: only the live entry. A night with no
    // live entry (the laptop has gone home) still answers ok, so the page
    // keeps what it has rather than reading the poll as "not found".
    if (liveOnly) {
      if (live === undefined && (await this.store.getItem(club, NIGHTS_CHANNEL, id).catch(() => undefined)) === undefined) {
        sendJson(response, 404, { ok: false, error: "Not found" });
        return;
      }

      sendJson(response, 200, { ok: true, ...liveField });
      return;
    }

    const doc = await this.store.getItem(club, NIGHTS_CHANNEL, id).catch(() => undefined);

    if (doc === undefined && live === undefined) {
      sendJson(response, 404, { ok: false, error: "Not found" });
      return;
    }

    // Their own race clips (22 Oct 2026, driver experience) - matched on
    // the same badge id, which is already the credential for this. (Their
    // podium videos were listed here too until 28 Oct 2026; those now stay
    // on the laptop.)
    const clips = await this.clipsOfDriver(club, id);

    // What they can ask the desk to change, and whether they can ask at
    // all right now - only while the club takes asks and they are on
    // tonight's check-in list, exactly the rule driver/ask applies. "pin"
    // is whether they have a personal PIN, which the laptop never needs
    // to say: the relay knows from its own racer map.
    const checkin = await this.askGateOf(club, id);
    const meEntry = checkin === undefined ? undefined : (await this.driverDocOf(club, DRIVER_ME_CHANNEL))?.drivers.get(id);
    const me = checkin === undefined || meEntry === undefined ? {} : { me: { ...meEntry, pin: hasRacerPin(checkin, id) } };
    const queriesOff = checkin !== undefined && await this.resultQueriesOffFor(club);

    sendJson(response, 200, {
      ok: true,
      night: doc === undefined ? null : doc.body,
      ...(clips.length > 0 ? { clips } : {}),
      ...liveField,
      ...me,
      ...(checkin !== undefined ? { asksOpen: true } : {}),
      ...(queriesOff ? { queriesOff: true } : {}),
    });
  }

  // ------------------------------------------- drivers' own documents
  //
  // 22 Oct 2026, driver experience: the private live and details
  // documents, parsed once and held, as the check-in roster is.

  /** One of the drivers' private documents, parsed - or undefined when there is none, or it will not read. */
  private async driverDocOf(club: string, channel: string): Promise<DriverDoc | undefined> {
    const key = `${club}/${channel}`;
    const held = this.driverDocs.get(key);

    if (held !== undefined) {
      return held ?? undefined;
    }

    return this.shared(
      this.driverDocLoads,
      key,
      () => this.driverDocEpoch,
      async () => driverDocFrom(await this.store.getSnapshot(club, channel)),
      (doc) => {
        this.driverDocs.set(key, doc ?? null);
      },
    ).catch(() => undefined);
  }

  /** Called after every write to a club's driverlive or driverme. After, not before, as forgetCheckin is. */
  private forgetDriverDoc(club: string, channel: string): void {
    this.driverDocEpoch++;
    this.driverDocs.delete(`${club}/${channel}`);
  }

  /** One driver's entry in the live document, with the document's "at" - only while the document is fresh. */
  private async liveEntryOf(club: string, id: string): Promise<{ entry: Readonly<Record<string, unknown>>; at: string } | undefined> {
    const doc = await this.driverDocOf(club, DRIVER_LIVE_CHANNEL);

    if (doc === undefined || this.now().getTime() - doc.takenAt > DRIVER_LIVE_FRESH_MS) {
      return undefined;
    }

    const entry = doc.drivers.get(id);

    return entry === undefined ? undefined : { entry, at: doc.at !== "" ? doc.at : new Date(doc.takenAt).toISOString() };
  }

  /**
   * Writes a snapshot over with nothing, which every reader takes as absent
   * - the store has no way to delete one. seq 0, so whatever the laptop
   * publishes next always wins. Called inside the club's lock.
   */
  private async blankSnapshot(club: string, channel: string): Promise<void> {
    await this.store.putSnapshot(club, channel, { seq: 0, publishedAt: this.now().toISOString(), body: null });
    this.forgetListing(club, channel);

    if (channel === DRIVER_LIVE_CHANNEL || channel === DRIVER_ME_CHANNEL) {
      this.forgetDriverDoc(club, channel);
    }
  }

  // --------------------------------------------------- drivers' clips

  /**
   * A driver's own race clip from the laptop (22 Oct 2026, driver
   * experience). The bytes are the body (audio/mpeg); what it is comes in
   * the query: driver (badge id, required), label, race. Only while the
   * club offers drivers their nights. See clips.ts.
   */
  private async receiveClip(
    request: IncomingMessage,
    response: ServerResponse,
    club: string,
    rawId: string,
    query: URLSearchParams,
    liftCeiling: () => void = () => {},
  ): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      await discardRest(request, response);
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      await discardRest(request, response);
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    if (this.clips === undefined) {
      await discardRest(request, response);
      sendJson(response, 404, { ok: false, error: "This relay does not keep drivers' clips." });
      return;
    }

    // Taken before the switch is read: a close that lands after this moves
    // it, and one that landed before is seen by the check below.
    const wipesAtStart = this.clipWipes.get(club) ?? 0;

    if (!(await this.nightsOpenFor(club))) {
      await discardRest(request, response);
      sendJson(response, 403, { ok: false, error: "This club is not offering drivers their own night, so their clips are not accepted." });
      return;
    }

    if (this.clipUploadsInFlight >= 2) {
      await discardRest(request, response);
      sendJson(response, 429, { ok: false, error: "Busy with other clips. Try again in a minute." });
      return;
    }

    if (this.clipUploads.blocked(club)) {
      await discardRest(request, response);
      sendJson(response, 429, { ok: false, error: "Too many clips at once. Try again in a minute." });
      return;
    }

    this.clipUploads.hit(club);

    const id = rawId.toLowerCase();
    const driver = (query.get("driver") ?? "").trim().toLowerCase();
    const race = (query.get("race") ?? "").trim().toLowerCase();
    const label = cleanText(query.get("label") ?? "", CLIP_LABEL_MAX);

    if (!CLIP_ID.test(id) || !CLIP_DRIVER_ID.test(driver) || (race.length > 0 && !CLIP_RACE_ID.test(race))) {
      await discardRest(request, response);
      sendJson(response, 400, { ok: false, error: "That clip's details could not be read." });
      return;
    }

    const declared = Number(request.headers["content-length"] ?? "0");

    if (Number.isFinite(declared) && declared > CLIP_MAX_BYTES) {
      await discardRest(request, response);
      sendJson(response, 413, { ok: false, error: "That clip is too big." });
      return;
    }

    this.clipUploadsInFlight += 1;
    let bytes: Buffer | "too-big" | "unreadable";

    // An idle timeout instead of the 30-second ceiling (see listen()):
    // four megabytes over a track's uplink can take longer than thirty
    // seconds.
    liftCeiling();
    request.setTimeout(30_000, () => request.destroy());

    try {
      bytes = await readRawBody(request, response, CLIP_MAX_BYTES);
    } finally {
      if (!request.destroyed) {
        request.setTimeout(0);
      }
      this.clipUploadsInFlight -= 1;
    }

    if (bytes === "too-big") {
      sendJson(response, 413, { ok: false, error: "That clip is too big." });
      return;
    }

    if (bytes === "unreadable" || !looksLikeMp3(bytes)) {
      sendJson(response, 400, { ok: false, error: "That is not an mp3." });
      return;
    }

    const meta: ClipMeta = {
      id,
      driver,
      label,
      at: this.now().toISOString(),
      bytes: bytes.length,
      ...(race.length > 0 ? { race } : {}),
    };

    const vault = this.clips;
    const body = bytes;

    const kept = await this.withLock(`${club}|clips`, async () => {
      // Nights closed, or the club deleted everything, while this was on
      // its way: keep none of it.
      if ((this.clipWipes.get(club) ?? 0) !== wipesAtStart || !(await this.nightsOpenFor(club))) {
        return false;
      }

      // Room first: the oldest go, never the newest. See clipsToDrop.
      for (const old of clipsToDrop(await vault.list(club), this.now().getTime(), { driver, bytes: body.length })) {
        await vault.remove(club, old.id);
      }

      await vault.put(club, meta, body);
      return true;
    });

    if (!kept) {
      sendJson(response, 410, { ok: false, error: "The club closed drivers' nights, or deleted what it had published, while this clip was on its way." });
      return;
    }

    sendJson(response, 200, { ok: true, clip: `/c/${club}/clip/${id}.mp3` });
  }

  /**
   * Every phone signed up for a club's alerts, forgotten. Called inside the
   * club's lock. With `wipe`, that is what forgets them (Delete everything
   * takes the whole folder), run while no alert being sent can write a
   * driver's list back - see PushService.forget.
   */
  private async forgetPushSubs(club: string, wipe?: () => Promise<void>): Promise<void> {
    if (this.push !== undefined) {
      await this.push.forget(club, wipe);
      return;
    }

    if (wipe !== undefined) {
      await wipe();
      return;
    }

    for (const item of await this.store.itemSizes(club, PUSH_CHANNEL)) {
      await this.store.deleteItem(club, PUSH_CHANNEL, item.id).catch(() => undefined);
    }
  }

  /** Every one of a club's clips, gone - when nights close or the club is deleted. Anything on its way keeps nothing. */
  private async wipeClips(club: string): Promise<void> {
    this.clipWipes.set(club, (this.clipWipes.get(club) ?? 0) + 1);
    const vault = this.clips;

    if (vault !== undefined) {
      await this.withLock(`${club}|clips`, () => vault.removeClub(club).catch(() => {}));
    }
  }

  /**
   * Deletes every club's clips past their week, or past a driver's or a
   * club's share - exactly what clipsHeld already hides, so it never takes
   * a clip a phone can still see. On the hourly timers (see listen()).
   */
  async sweepClips(clubs?: Iterable<string>): Promise<number> {
    const vault = this.clips;

    if (vault === undefined) {
      return 0;
    }

    const names = clubs !== undefined ? [...clubs] : await vault.listClubs().catch(() => [] as string[]);
    let dropped = 0;

    for (const club of names) {
      if (!isValidClubId(club)) {
        continue;
      }

      await this.withLock(`${club}|clips`, async () => {
        for (const old of clipsToDrop(await vault.list(club), this.now().getTime())) {
          await vault.remove(club, old.id);
          dropped += 1;
        }
      }).catch(() => {});
    }

    return dropped;
  }

  /** A club's clips still held - anything past its week or its share dropped from the answer, swept or not. */
  private async clipsHeld(club: string): Promise<ClipMeta[]> {
    if (this.clips === undefined) {
      return [];
    }

    const held = await this.clips.list(club).catch(() => [] as ClipMeta[]);
    const stale = new Set(clipsToDrop(held, this.now().getTime()).map((c) => c.id));

    return held.filter((c) => !stale.has(c.id));
  }

  /** One driver's clips, newest first, as the night page shows them. */
  private async clipsOfDriver(
    club: string,
    driver: string,
  ): Promise<{ url: string; download: string; label: string; at: string; race?: string }[]> {
    return (await this.clipsHeld(club))
      .filter((c) => c.driver === driver)
      .sort((a, b) => Date.parse(b.at) - Date.parse(a.at))
      .map((c) => ({
        url: `/c/${club}/clip/${c.id}.mp3`,
        download: `/c/${club}/clip/${c.id}.mp3?download=1`,
        label: c.label,
        at: c.at,
        ...(c.race !== undefined ? { race: c.race } : {}),
      }));
  }

  /** One clip's bytes, in ranges - see sendRanged. */
  private async clipFile(
    request: IncomingMessage,
    response: ServerResponse,
    club: string,
    file: string,
    query: URLSearchParams,
  ): Promise<void> {
    const address = `${club}|${clientAddress(request)}`;

    if (this.clipReads.blocked(address)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a minute and try again." });
      return;
    }

    this.clipReads.hit(address);

    const id = file.endsWith(".mp3") ? file.slice(0, -4).toLowerCase() : "";
    const vault = this.clips;

    // One file read for an id that is not held, before the whole folder is
    // (22 Oct 2026, security review): clipsHeld reads every one of the
    // club's up-to-800 metas, and this route is public - so a guessed id
    // cost the relay 800 reads, 900 times a minute per address. Only a real
    // clip's address (128 random bits) pays for the full check.
    const known = vault !== undefined && CLIP_ID.test(id) ? await vault.meta(club, id).catch(() => undefined) : undefined;
    const meta = known !== undefined ? (await this.clipsHeld(club)).find((c) => c.id === id) : undefined;

    if (vault === undefined || meta === undefined || meta.bytes === 0) {
      sendJson(response, 404, { ok: false, error: "That clip has gone - they are kept for a week." });
      return;
    }

    const fileName = meta.label.replace(/[^A-Za-z0-9 ._()-]/g, "").trim().slice(0, 80) || "Race call";

    await this.sendRanged(request, response, meta.bytes, "audio/mpeg", `${fileName}.mp3`, query.get("download") === "1", (start, end) =>
      vault.read(club, id, start, end),
    );
  }

  // ---------------------------------------------------- drivers' asks
  //
  // 22 Oct 2026, driver experience - see driverasks.ts.

  /** Whether this club takes drivers' asks at all. Absent means no, as every switch here does. */
  private async driverAsksOpenFor(club: string): Promise<boolean> {
    const body = (await this.store.getSnapshot(club, DRIVER_ASKS_OPEN_DOC))?.body;

    return isPlainObject(body) && body["open"] === true;
  }

  /** Whether the laptop has switched result queries ("Something wrong?") off. Absent means on. */
  private async resultQueriesOffFor(club: string): Promise<boolean> {
    const body = (await this.store.getSnapshot(club, DRIVER_ASKS_OPEN_DOC))?.body;

    return isPlainObject(body) && body["queries"] === false;
  }

  /**
   * The check-in roster, when this driver may ask the desk for something
   * right now - nights open, asks open, check-in open and them on its list
   * - or undefined. One rule, so the night answer's asksOpen and the ask
   * route can never disagree. Every way of failing it is answered the same.
   */
  private async askGateOf(club: string, id: string): Promise<CheckinView | undefined> {
    if (!(await this.nightsOpenFor(club)) || !(await this.driverAsksOpenFor(club))) {
      return undefined;
    }

    const checkin = await this.checkinOf(club).catch(() => undefined);

    if (checkin === undefined || !checkin.enabled || !checkin.drivers.some((d) => d.id === id)) {
      return undefined;
    }

    return checkin;
  }

  /**
   * A driver asking the desk for something, with their badge code - see
   * driverasks.ts. Held until the laptop collects it; never acted on here.
   */
  private async driverAsk(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = `${club}|${clientAddress(request)}`;

    if (this.askTraffic.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a few minutes and try again." });
      return;
    }

    this.askTraffic.hit(who);

    // Read to the setup sheet's limit, then held to 2 KB for every other
    // kind (5 Nov 2026): only a whole sheet needs more.
    const body = await readBody(request, response, DRIVER_ASK_SETUP_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)
      || (body["kind"] !== "setup" && jsonBytes(body) > DRIVER_ASK_MAX_BODY)) {
      sendJson(response, 400, { ok: false, error: "That request could not be read." });
      return;
    }

    const id = typeof body["id"] === "string" ? body["id"].trim().toLowerCase() : "";

    // ONE ANSWER for a code that is not a code, a club that is closed and a
    // driver who is not on tonight's list - as readOneNight does, so this
    // route tells a stranger nothing about which codes are real.
    const checkin = DRIVER_ID.test(id) ? await this.askGateOf(club, id) : undefined;

    if (checkin === undefined) {
      sendJson(response, 404, { ok: false, error: "Not found" });
      return;
    }

    const kind = typeof body["kind"] === "string" ? body["kind"] : "";

    if (!(DRIVER_ASK_KINDS as readonly string[]).includes(kind)) {
      sendJson(response, 400, { ok: false, error: "That request could not be read." });
      return;
    }

    if (kind === "query" && await this.resultQueriesOffFor(club)) {
      sendJson(response, 403, { ok: false, error: "Your club is not taking result queries online. Ask at the desk." });
      return;
    }

    // The personal PIN's proof is the pass checkin/list answered it with,
    // checked against this driver's own in constant time (racerByPass).
    const pass = typeof body["pass"] === "string" && PASS.test(body["pass"]) ? body["pass"] : "";
    const withPin = pass !== "" && racerByPass(checkin, pass)?.id === id;

    // A driver who HAS a personal PIN changes their details only with it:
    // the badge code alone is printed on a lanyard. A driver without one
    // can use the badge - but not for their email (askFieldsFrom).
    // The garage - packs, cars and setups, which the laptop applies by
    // itself - asked for it too for an afternoon, until (5 Nov 2026, Asher)
    // "remove the pin requirement for the garage. keep it for changing
    // called and shown". The badge code is enough there; the per-driver
    // write limit below still holds.
    const write = isWriteKind(kind);

    if (kind === "details" && hasRacerPin(checkin, id) && !withPin) {
      sendJson(response, 403, { ok: false, error: "Sign in with your personal PIN first." });
      return;
    }

    const check = askFieldsFrom(kind, body["f"], withPin);

    if (!check.ok) {
      sendJson(response, check.status, { ok: false, error: check.error });
      return;
    }

    const badge = `${club}|${id}`;
    const limiter = write ? this.askWrites : this.askPerDriver;

    if (limiter.blocked(badge) || (kind === "query" && this.askQueries.blocked(badge))) {
      sendJson(response, 429, {
        ok: false,
        error: write ? "That is a lot of changes in one hour - wait a little and try again." : "You have asked a few times already - wait a while, or ask at the desk.",
      });
      return;
    }

    limiter.hit(badge);

    if (kind === "query") {
      this.askQueries.hit(badge);
    }

    const at = this.now();
    const stored: DriverAskBody = { b: id, k: kind as DriverAskBody["k"], v: withPin ? "pin" : "badge", f: check.f, at: at.toISOString() };

    if (jsonBytes(stored) > (kind === "setup" ? DRIVER_ASK_SETUP_MAX_BODY : DRIVER_ASK_MAX_STORED)) {
      sendJson(response, 413, { ok: false, error: "That is too long." });
      return;
    }

    const outcome = await this.withLock(club, async () => {
      // Read again inside the lock, as sendClubNote does: the club may have
      // closed asks or nights, or taken them off the list, while this
      // waited for it - and closing deletes what is held under this lock.
      if ((await this.askGateOf(club, id)) === undefined) {
        return "closed" as const;
      }

      const held = await this.store.listItems(club, DRIVER_ASKS_CHANNEL);

      if (held.length >= DRIVER_ASKS_PER_CLUB) {
        return "full" as const;
      }

      // Writes and desk asks each have their own room (5 Nov 2026), so a
      // busy evening of pack swaps never stops a query reaching the desk.
      const mineHeld = held.filter((item) => {
        const ask = asDriverAsk(item.doc.body);
        return ask !== undefined && ask.b === id && isWriteKind(ask.k) === write;
      }).length;

      if (mineHeld >= (write ? DRIVER_WRITES_PER_DRIVER : DRIVER_ASKS_PER_DRIVER)) {
        return "mine" as const;
      }

      // Random, as a note's id is: it says nothing about what was asked or
      // how many have been.
      const itemId = randomBytes(12).toString("hex");

      await this.store.putItem(club, DRIVER_ASKS_CHANNEL, itemId, {
        seq: at.getTime(),
        publishedAt: at.toISOString(),
        body: stored,
      });

      return { itemId };
    });

    if (outcome === "closed") {
      sendJson(response, 404, { ok: false, error: "Not found" });
      return;
    }

    if (outcome === "full") {
      sendJson(response, 503, { ok: false, error: "The desk has a lot to get through right now. Try again in a while, or ask at the desk." });
      return;
    }

    if (outcome === "mine") {
      sendJson(response, 429, {
        ok: false,
        error: write
          ? "HeatCaller has not picked up your last changes yet - is the timing desk's laptop on? Try again in a minute."
          : `You have ${DRIVER_ASKS_PER_DRIVER} waiting already - the desk will get to them.`,
      });
      return;
    }

    sendJson(response, 200, { ok: true, ref: askRefOf(outcome.itemId) });
  }

  /**
   * What the laptop collects: every ask held, oldest first. NOT DELETED
   * HERE - the laptop files them, then confirms through driver/collected,
   * so a poll whose answer never arrived loses nothing (the notes shape).
   */
  private async readDriverAsks(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const items = (await this.store.listItems(club, DRIVER_ASKS_CHANNEL))
      .map((item) => ({ id: item.id, seq: item.doc.seq, ask: asDriverAsk(item.doc.body) }))
      .filter((item): item is { id: string; seq: number; ask: DriverAskBody } => item.ask !== undefined)
      .sort((a, b) => a.seq - b.seq || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0))
      .map((item) => ({ id: item.id, ...item.ask }));

    sendJson(response, 200, { ok: true, items });
  }

  /** The laptop saying it has filed these asks, so the relay can let them go. An id already gone is not an error. */
  private async clearDriverAsks(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const body = await readBody(request, response, DRIVER_ASKS_COLLECT_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That could not be read." });
      return;
    }

    const asked = Array.isArray(body["ids"]) ? (body["ids"] as unknown[]) : [];
    const ids = asked
      .filter((id): id is string => typeof id === "string" && /^[0-9a-f]{24}$/.test(id))
      .slice(0, DRIVER_ASKS_PER_CLUB);

    let gone = 0;

    await this.withLock(club, async () => {
      for (const id of ids) {
        await this.store.deleteItem(club, DRIVER_ASKS_CHANNEL, id);
        gone++;
      }
    });

    sendJson(response, 200, { ok: true, cleared: gone });
  }

  // ---------------------------------------------------- drivers' garages
  //
  // 5 Nov 2026, asked for by Asher - see garage.ts and
  // claude/garage-relay-contract.md.

  /**
   * One driver's garage, for the phone's Garage page: the document the
   * laptop built, whether they have a personal PIN and whether the pass
   * sent is theirs, and how their recent requests went. The same gate as
   * an ask - and its one 404 for every way of failing it.
   */
  private async readGarage(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const address = `${club}|${clientAddress(request)}`;

    if (this.nightLiveReads.blocked(address)) {
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a minute and try again." });
      return;
    }

    this.nightLiveReads.hit(address);

    const body = await readBody(request, response, 1024);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That request could not be read." });
      return;
    }

    const id = typeof body["id"] === "string" ? body["id"].trim().toLowerCase() : "";
    const checkin = DRIVER_ID.test(id) ? await this.askGateOf(club, id) : undefined;

    if (checkin === undefined) {
      sendJson(response, 404, { ok: false, error: "Not found" });
      return;
    }

    const pass = typeof body["pass"] === "string" && PASS.test(body["pass"]) ? body["pass"] : "";
    const doc = await this.store.getItem(club, GARAGE_CHANNEL, id).catch(() => undefined);
    const live = await this.liveEntryOf(club, id);
    const asks = live !== undefined && Array.isArray(live.entry["asks"]) ? live.entry["asks"] : [];

    sendJson(response, 200, {
      ok: true,
      garage: doc === undefined || !isPlainObject(doc.body) ? null : doc.body,
      pin: hasRacerPin(checkin, id),
      signedIn: pass !== "" && racerByPass(checkin, pass)?.id === id,
      asks,
    });
  }

  /**
   * A car photo from the driver's phone, already shrunk there to a JPEG of
   * 450 KB or less. Held until the laptop collects it - which checks the
   * whole picture and that the car is theirs - as an ask is.
   */
  private async driverPhoto(request: IncomingMessage, response: ServerResponse, club: string, liftCeiling: () => void): Promise<void> {
    const who = `${club}|${clientAddress(request)}`;

    if (this.askTraffic.blocked(who)) {
      await discardRest(request, response);
      sendJson(response, 429, { ok: false, error: "Too many requests. Wait a few minutes and try again." });
      return;
    }

    this.askTraffic.hit(who);

    const declared = Number(request.headers["content-length"] ?? "0");

    if (Number.isFinite(declared) && declared > DRIVER_PHOTO_MAX_BODY) {
      await discardRest(request, response);
      sendJson(response, 413, { ok: false, error: "That photo is too big. Try a smaller one." });
      return;
    }

    // WHY (7 Nov 2026, Bug Hunt 13): the ceiling was lifted before anything
    // was checked, so any address could hold a connection for the whole
    // five minutes, half a megabyte buffered, with no badge code and on a
    // club not taking photos at all - a couple of hundred of them filled
    // the relay. The badge code is inside the body, so it cannot be looked
    // at first; what can be is: the club taking asks at all, and how many
    // photos are being read right now across every club (as clips have).
    if (!(await this.nightsOpenFor(club)) || !(await this.driverAsksOpenFor(club))) {
      await discardRest(request, response);
      sendJson(response, 404, { ok: false, error: "Not found" });
      return;
    }

    if (this.photoUploadsInFlight >= PHOTO_UPLOADS_IN_FLIGHT_MAX) {
      await discardRest(request, response);
      sendJson(response, 429, { ok: false, error: "Busy with other photos. Try again in a minute." });
      return;
    }

    // A phone on the venue's Wi-Fi can take more than the 30-second
    // ceiling to send half a megabyte - an idle timeout instead, as a
    // clip has.
    this.photoUploadsInFlight += 1;
    liftCeiling();
    request.setTimeout(30_000, () => request.destroy());

    let body: unknown;

    try {
      body = await readBody(request, response, DRIVER_PHOTO_MAX_BODY);
    } finally {
      if (!request.destroyed) {
        request.setTimeout(0);
      }
      this.photoUploadsInFlight -= 1;
    }

    if (body === "too-big") {
      sendJson(response, 413, { ok: false, error: "That photo is too big. Try a smaller one." });
      return;
    }

    if (body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That photo could not be read." });
      return;
    }

    const id = typeof body["id"] === "string" ? body["id"].trim().toLowerCase() : "";
    const checkin = DRIVER_ID.test(id) ? await this.askGateOf(club, id) : undefined;

    if (checkin === undefined) {
      sendJson(response, 404, { ok: false, error: "Not found" });
      return;
    }

    const pass = typeof body["pass"] === "string" && PASS.test(body["pass"]) ? body["pass"] : "";
    // A car's photo is the garage's, so the badge code is enough (5 Nov
    // 2026, Asher: "remove the pin requirement for the garage").
    const withPin = pass !== "" && racerByPass(checkin, pass)?.id === id;

    const car =typeof body["car"] === "string" ? body["car"].trim().toLowerCase() : "";

    if (!/^[0-9a-f]{32}$/.test(car)) {
      sendJson(response, 400, { ok: false, error: "Which car is the photo of?" });
      return;
    }

    const photo = photoBytesOf(body["data"]);

    if (!photo.ok) {
      sendJson(response, 400, { ok: false, error: photo.error });
      return;
    }

    const badge = `${club}|${id}`;

    if (this.photoUploads.blocked(badge)) {
      sendJson(response, 429, { ok: false, error: "That is a lot of photos in one hour - wait a little and try again." });
      return;
    }

    this.photoUploads.hit(badge);

    const at = this.now();
    const stored: DriverPhotoBody = { b: id, car, v: withPin ? "pin" : "badge", data: body["data"] as string, at: at.toISOString() };

    const outcome = await this.withLock(club, async () => {
      if ((await this.askGateOf(club, id)) === undefined) {
        return "closed" as const;
      }

      const held = await this.store.listItems(club, DRIVER_PHOTOS_CHANNEL);

      if (held.length >= DRIVER_PHOTOS_PER_CLUB) {
        return "full" as const;
      }

      if (held.filter((item) => asDriverPhoto(item.doc.body)?.b === id).length >= DRIVER_PHOTOS_PER_DRIVER) {
        return "mine" as const;
      }

      const itemId = randomBytes(12).toString("hex");

      await this.store.putItem(club, DRIVER_PHOTOS_CHANNEL, itemId, {
        seq: at.getTime(),
        publishedAt: at.toISOString(),
        body: stored,
      });

      return { itemId };
    });

    if (outcome === "closed") {
      sendJson(response, 404, { ok: false, error: "Not found" });
      return;
    }

    if (outcome === "full" || outcome === "mine") {
      sendJson(response, outcome === "full" ? 503 : 429, {
        ok: false,
        error: "HeatCaller has not picked up the photos already sent - is the timing desk's laptop on? Try again in a minute.",
      });
      return;
    }

    sendJson(response, 200, { ok: true, ref: askRefOf(outcome.itemId) });
  }

  /** The photos waiting, oldest first, without their pictures - the laptop then takes them one at a time. */
  private async readDriverPhotos(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    if (!this.laptopMay(request, response, club)) {
      return;
    }

    const items = (await this.store.listItems(club, DRIVER_PHOTOS_CHANNEL))
      .map((item) => ({ id: item.id, seq: item.doc.seq, photo: asDriverPhoto(item.doc.body) }))
      .filter((item): item is { id: string; seq: number; photo: DriverPhotoBody } => item.photo !== undefined)
      .sort((a, b) => a.seq - b.seq || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0))
      .map((item) => ({
        id: item.id,
        b: item.photo.b,
        car: item.photo.car,
        v: item.photo.v,
        at: item.photo.at,
        bytes: Math.floor((item.photo.data.length * 3) / 4),
      }));

    sendJson(response, 200, { ok: true, items });
  }

  /** One waiting photo, picture and all. */
  private async readDriverPhoto(request: IncomingMessage, response: ServerResponse, club: string, rawId: string): Promise<void> {
    if (!this.laptopMay(request, response, club)) {
      return;
    }

    const id = rawId.toLowerCase();
    const doc = /^[0-9a-f]{24}$/.test(id) ? await this.store.getItem(club, DRIVER_PHOTOS_CHANNEL, id).catch(() => undefined) : undefined;
    const photo = doc === undefined ? undefined : asDriverPhoto(doc.body);

    if (photo === undefined) {
      sendJson(response, 404, { ok: false, error: "That photo has gone." });
      return;
    }

    sendJson(response, 200, { ok: true, id, ...photo });
  }

  /** The laptop saying it has these photos now. An id already gone is not an error. */
  private async clearDriverPhotos(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    if (!this.laptopMay(request, response, club)) {
      return;
    }

    const body = await readBody(request, response, DRIVER_PHOTOS_COLLECT_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That could not be read." });
      return;
    }

    const ids = (Array.isArray(body["ids"]) ? (body["ids"] as unknown[]) : [])
      .filter((id): id is string => typeof id === "string" && /^[0-9a-f]{24}$/.test(id))
      .slice(0, DRIVER_PHOTOS_PER_CLUB);

    let gone = 0;

    await this.withLock(club, async () => {
      for (const id of ids) {
        await this.store.deleteItem(club, DRIVER_PHOTOS_CHANNEL, id);
        gone++;
      }
    });

    sendJson(response, 200, { ok: true, cleared: gone });
  }

  /** The laptop's publish token for this club, or the 401/403 already sent. */
  private laptopMay(request: IncomingMessage, response: ServerResponse, club: string): boolean {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return false;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return false;
    }

    return true;
  }

  /**
   * The laptop saying whether its club takes drivers' asks - sent when the
   * setting changes and on every start. CLOSING CLEARS WHAT IS HELD: every
   * ask not yet collected, and the drivers' details document, which only
   * exists so they can ask.
   */
  private async setDriverAsksOpen(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const body = await readBody(request, response, NOTES_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That could not be read." });
      return;
    }

    const open = body["open"] === true;
    // Result queries on their own switch (5 Nov 2026, Asher: "can you turn
    // off the something wrong with a result from the app?"). Only an
    // explicit false turns them off, so a laptop from before the switch
    // keeps them as they were.
    const queriesOff = open && body["queries"] === false;

    await this.store.putSnapshot(club, DRIVER_ASKS_OPEN_DOC, {
      seq: this.now().getTime(),
      publishedAt: this.now().toISOString(),
      body: queriesOff ? { open, queries: false } : { open },
    });

    if (!open) {
      await this.withLock(club, async () => {
        // Ids from the directory listing, as the other switches do, so a
        // file that will not parse is deleted too.
        for (const item of await this.store.itemSizes(club, DRIVER_ASKS_CHANNEL)) {
          await this.store.deleteItem(club, DRIVER_ASKS_CHANNEL, item.id);
        }

        // Their garages and the photos on their way go too (5 Nov 2026).
        for (const channel of [DRIVER_PHOTOS_CHANNEL, GARAGE_CHANNEL]) {
          for (const item of await this.store.itemSizes(club, channel)) {
            await this.store.deleteItem(club, channel, item.id);
          }
        }

        this.forgetListing(club, GARAGE_CHANNEL);

        await this.blankSnapshot(club, DRIVER_ME_CHANNEL);
      });
    }

    sendJson(response, 200, { ok: true, open });
  }

  /**
   * Whether this club wants shout-outs from its page at all.
   *
   * SET BY THE LAPTOP, not inferred. The alternative - take them and let
   * the laptop decide later - means holding strangers' words in the folder
   * of a club that never asked for them, which is the one thing a store
   * holding other people's text must not do.
   *
   * Absent means no. A club that has never run a version of HeatCaller
   * that knows about this has not switched it on.
   */
  private async notesOpenFor(club: string): Promise<boolean> {
    const body = (await this.store.getSnapshot(club, NOTES_OPEN_DOC))?.body;

    return isPlainObject(body) && body["open"] === true;
  }

  /**
   * Whether the club's own list for tonight has room for another.
   *
   * THE THIRD STATE, AND IT CLOSES A LIE (13 Oct 2026). Open and closed
   * were the only two, and "open" meant "the relay will take it" rather
   * than "the club can use it". The relay holds forty; the night's list
   * on the laptop takes twelve from the room (ClubNotes.MostFromTheRoom),
   * and nothing told the relay which number mattered. So the thirteenth
   * sender was answered {"ok":true}, had their message collected off the
   * relay, and had it refused on the laptop - with the only trace a line
   * in the club's own console. They were told it went through. It did not.
   *
   * SET BY THE LAPTOP, like the open flag and for the same reason: the
   * relay does not know what a note is, cannot count the ones the club
   * typed itself, and must not learn. It is told on the notes poll, which
   * runs every thirty seconds anyway.
   *
   * ABSENT MEANS ROOM. A laptop from before this says nothing about being
   * full, and the honest reading of silence is the old behaviour - take
   * the note - rather than refusing every sender at a club that has not
   * updated.
   */
  private async notesFullFor(club: string): Promise<boolean> {
    const body = (await this.store.getSnapshot(club, NOTES_OPEN_DOC))?.body;

    return isPlainObject(body) && body["full"] === true;
  }

  /**
   * The laptop saying whether its club takes shout-outs - sent when the
   * setting changes and on every start, so a club that turns it off is
   * closed on the relay too rather than merely ignoring what arrives.
   */
  private async setClubNotesOpen(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const body = await readBody(request, response, NOTES_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That could not be read." });
      return;
    }

    const open = body["open"] === true;

    // Whether the club's list for tonight has room - see notesFullFor. A
    // laptop that does not send it is not full, which is the old
    // behaviour and the right reading of silence.
    const full = body["full"] === true;

    await this.store.putSnapshot(club, NOTES_OPEN_DOC, {
      seq: this.now().getTime(),
      publishedAt: this.now().toISOString(),
      body: { open, full },
    });

    // CLOSING CLEARS WHAT IS HELD. A club switching this off has decided
    // it does not want other people's messages, and leaving a pile of
    // them in its folder would be keeping exactly what it just declined.
    if (!open) {
      await this.withLock(club, async () => {
        // WHY (20 Oct 2026, Bug Hunt 8): ids from the directory listing.
        // listItems read and parsed every file and quietly skipped one that
        // would not read (corrupt, EIO), so that file was never deleted and
        // came back when the switch went on again. itemSizes needs no parse.
        for (const item of await this.store.itemSizes(club, NOTES_CHANNEL)) {
          await this.store.deleteItem(club, NOTES_CHANNEL, item.id);
        }
      });
    }

    sendJson(response, 200, { ok: true, open });
  }

  /**
   * Whether this club is letting people listen along from home.
   *
   * ABSENT MEANS NO, which is the same default every public route on this
   * relay has and the one the plan asked for by name. A room's announcer
   * saying something unfortunate about a ten-year-old's driving is a
   * different matter in a hall and on the internet, so a club that has
   * never been asked has not agreed.
   */
  private async listeningOpenFor(club: string): Promise<boolean> {
    const body = (await this.store.getSnapshot(club, LISTEN_OPEN_DOC))?.body;

    return isPlainObject(body) && body["open"] === true;
  }

  /**
   * The laptop saying whether its club is letting people listen along -
   * sent when the setting changes and on every start, so a club that
   * turns it off is closed on the relay too rather than merely stopping
   * sending.
   *
   * Both halves matter. Stopping sending leaves the last few minutes of
   * an evening sitting on a public address for as long as the club's
   * budget holds them; closing the switch is what takes them down.
   */
  private async setListeningOpen(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const body = await readBody(request, response, NOTES_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That could not be read." });
      return;
    }

    const open = body["open"] === true;

    await this.store.putSnapshot(club, LISTEN_OPEN_DOC, {
      seq: this.now().getTime(),
      publishedAt: this.now().toISOString(),
      body: { open },
    });

    // CLOSING TAKES DOWN WHAT IS HELD, for the reason the notes switch
    // clears its own pile: a club switching this off has decided it does
    // not want its announcer on the internet, and leaving the last few
    // minutes of it there would be keeping exactly what it just declined.
    if (!open) {
      await this.withLock(club, async () => {
        // WHY (20 Oct 2026, Bug Hunt 8): ids from the directory listing.
        // listItems read and parsed every file and quietly skipped one that
        // would not read (corrupt, EIO), so that file was never deleted and
        // came back when the switch went on again. itemSizes needs no parse.
        for (const item of await this.store.itemSizes(club, LISTEN_CHANNEL)) {
          await this.store.deleteItem(club, LISTEN_CHANNEL, item.id);
        }

        // AND THE CACHED LISTING WITH THEM, which the notes switch does
        // not have to do and this one does. Notes are off-channel and are
        // only ever read by the laptop through notes/inbox; `listen` is a
        // PUBLIC collection served through the cached listing route, so
        // deleting the files alone left the last few minutes of the
        // evening being handed out of memory to anybody who asked. Caught
        // by a test rather than by reading - prune pairs its own deletes
        // with this call for the same reason.
        this.forgetListing(club, LISTEN_CHANNEL);
      });
    }

    sendJson(response, 200, { ok: true, open });
  }

  /**
   * A shout-out from anybody with the club's link.
   *
   * PUBLIC, and the only thing it can achieve is a line in a list that the
   * race director reads. See NOTES_CHANNEL for why that is enough to make
   * this route safe to have.
   */
  private async sendClubNote(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = `${club}|${clientAddress(request)}`;

    if (this.notesTraffic.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "You have sent a few already - give the announcer a chance." });
      return;
    }

    this.notesTraffic.hit(who);

    if (!(await this.notesOpenFor(club))) {
      // 404 rather than 403, as the laptop's own route does: a club that
      // has this switched off has no such page, and saying "switched off"
      // tells a stranger there is a thing to switch on.
      sendJson(response, 404, { ok: false, error: "Not found" });
      return;
    }

    const body = await readBody(request, response, NOTES_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That message could not be read." });
      return;
    }

    const message = typeof body["m"] === "string" ? body["m"].trim() : "";
    const from = typeof body["n"] === "string" ? body["n"].trim() : "";

    if (message.length === 0) {
      sendJson(response, 400, { ok: false, error: "There is nothing in it." });
      return;
    }

    if (message.length > NOTE_MAX_CHARS) {
      sendJson(response, 400, {
        ok: false,
        error: `That is a bit long - keep it under ${NOTE_MAX_CHARS} characters so the announcer can work it into a sentence.`,
      });
      return;
    }

    const outcome = await this.withLock(club, async () => {
      // Read again inside the lock: the club may have closed notes, or the
      // held set may have filled, while this request waited for it.
      if (!(await this.notesOpenFor(club))) {
        return "closed" as const;
      }

      // TWO WAYS TO BE FULL, and until now only the first was checked.
      // The relay's own buffer filling is rare - forty held means a laptop
      // that has not collected for a while. The club's night list filling
      // is ORDINARY: twelve from the room is a busy evening, and it was
      // the case that answered a sender "yes" and then threw their
      // message away. See notesFullFor.
      if ((await this.store.itemSizes(club, NOTES_CHANNEL)).length >= NOTES_HELD_MOST) {
        return "full" as const;
      }

      if (await this.notesFullFor(club)) {
        return "full" as const;
      }

      // The id is random and says nothing. It is not a hash of the
      // message, which would let anybody test whether a given sentence had
      // been sent, and not a counter, which would leak how many had.
      const id = randomBytes(12).toString("hex");

      await this.store.putItem(club, NOTES_CHANNEL, id, {
        seq: this.now().getTime(),
        publishedAt: this.now().toISOString(),
        body: {
          m: message,
          ...(from.length > 0 ? { n: from.slice(0, NOTE_MAX_NAME) } : {}),
        },
      });

      return "sent" as const;
    });

    if (outcome === "closed") {
      sendJson(response, 404, { ok: false, error: "Not found" });
      return;
    }

    if (outcome === "full") {
      sendJson(response, 429, {
        ok: false,
        error: "The announcer has plenty to get through already tonight. Try again in a while.",
      });
      return;
    }

    sendJson(response, 200, { ok: true });
  }

  /**
   * What the laptop collects: everything held, oldest first.
   *
   * NOT DELETED HERE. The laptop confirms through notes/collected once the
   * notes are on its own disk, so a poll whose answer never arrived does
   * not lose somebody's message - the same at-least-once shape every other
   * inbox on this relay has, and duplicates are the laptop's problem to
   * drop (ClubNotes refuses a note it already holds).
   */
  private async readClubNotes(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const held = await this.store.listItems(club, NOTES_CHANNEL);

    const items = held
      .filter((item) => isPlainObject(item.doc.body))
      .slice()
      .sort((a, b) => a.doc.seq - b.doc.seq)
      .map((item) => {
        const body = item.doc.body as Record<string, unknown>;

        return {
          id: item.id,
          m: typeof body["m"] === "string" ? body["m"] : "",
          n: typeof body["n"] === "string" ? body["n"] : "",
          at: item.doc.publishedAt,
        };
      })
      .filter((item) => item.m.length > 0);

    sendJson(response, 200, { ok: true, items });
  }

  /**
   * The laptop saying it has them, so the relay can let them go.
   *
   * An id it is not holding is not an error: a poll answered twice, or a
   * club closing notes between the poll and the confirmation, both land
   * here with ids that have already gone.
   */
  private async clearClubNotes(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const body = await readBody(request, response, NOTES_COLLECT_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That could not be read." });
      return;
    }

    const asked = Array.isArray(body["ids"]) ? (body["ids"] as unknown[]) : [];

    // Held to what the relay would ever be holding, so a body of ten
    // thousand ids is not ten thousand store calls.
    const ids = asked
      .filter((id): id is string => typeof id === "string" && isValidItemId(id))
      .slice(0, NOTES_HELD_MOST);

    let gone = 0;

    await this.withLock(club, async () => {
      for (const id of ids) {
        await this.store.deleteItem(club, NOTES_CHANNEL, id);
        gone++;
      }
    });

    sendJson(response, 200, { ok: true, cleared: gone });
  }

  /**
   * The ballot as the laptop last published it, or undefined when this
   * club has never switched the vote on.
   *
   * Absent means no, exactly as notesOpenFor's does: a club that has never
   * run a version of HeatCaller that knows about this has not asked for
   * it.
   */
  private async roomBallotFor(club: string): Promise<
    { open: boolean; race: string; drivers: { id: string; name: string }[]; salt: string } | undefined
  > {
    // WHY (20 Oct 2026, Bug Hunt 8): a corrupt ballot file reads as "not
    // enabled" (404) instead of a 503 on every phone, until the laptop's
    // next vote/open writes a good one over it.
    const body = (await this.queueSnapshotOf(club, VOTE_BALLOT_DOC))?.body;

    if (!isPlainObject(body) || body["enabled"] !== true) {
      return undefined;
    }

    const drivers = Array.isArray(body["drivers"]) ? (body["drivers"] as unknown[]) : [];

    return {
      open: body["open"] === true,
      race: typeof body["race"] === "string" ? body["race"] : "",
      salt: typeof body["salt"] === "string" ? body["salt"] : "",
      drivers: drivers
        .filter(isPlainObject)
        .map((one) => ({
          id: typeof one["id"] === "string" ? one["id"] : "",
          name: typeof one["name"] === "string" ? one["name"] : "",
        }))
        .filter((one) => one.id.length > 0 && one.name.length > 0),
    };
  }

  /**
   * The laptop publishing the ballot: whether its club wants this at all,
   * whether a vote is running, and the grid to pick from.
   *
   * Sent when the race director opens or closes a vote, when the setting
   * changes, and on every start - so a club that turns it off is CLOSED on
   * the relay rather than merely ignoring what arrives.
   *
   * THE SALT IS MINTED HERE AND NEVER SENT BACK. It keys the votes by
   * voter without the ids saying who anybody is; a new one per ballot
   * means two races cannot be correlated. See VOTE_CHANNEL.
   */
  private async setRoomVoteBallot(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const body = await readBody(request, response, VOTE_BALLOT_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That could not be read." });
      return;
    }

    const enabled = body["enabled"] === true;
    const open = enabled && body["open"] === true;

    // The race's LABEL, which is for showing people and nothing else.
    const race = typeof body["race"] === "string" ? body["race"].trim().slice(0, VOTE_MAX_CHARS) : "";

    // AND ITS ID, WHICH IS WHAT IDENTITY IS DECIDED ON (13 Oct 2026).
    // Until now the label was both, and the laptop had already written
    // down why that cannot work: a club running two classes has two
    // rounds both labelled "A-Main" (RoomVote.IsTheRace, which refuses
    // the label for exactly this reason and says it is "the worst failure
    // this feature has available to it"). The relay was doing the thing
    // the app refuses to do, and it cost two separate things:
    //
    //   the votes were not cleared, because sameRace came out true - so
    //   the Mod A-main's ballot opened with the Stock A-main's votes
    //   already in it, which is the one case the clearing block below
    //   exists to prevent;
    //
    //   and the salt was reused, which breaks the promise this route's own
    //   comment makes - "a new one per ballot means two races cannot be
    //   correlated". With one label over two races the same voter kept the
    //   same hashed id across both, and the whole point of minting a salt
    //   per ballot is that they cannot.
    const raceKey = typeof body["raceKey"] === "string" ? body["raceKey"].trim().slice(0, VOTE_MAX_CHARS) : "";

    // The laptop's own id for this poll. Absent from a laptop older than
    // it, and then the race's label is all there is to go on.
    const ballotId = typeof body["ballot"] === "string" ? body["ballot"].trim().slice(0, VOTE_MAX_CHARS) : "";

    const offered = Array.isArray(body["drivers"]) ? (body["drivers"] as unknown[]) : [];

    const seen = new Set<string>();
    const drivers: { id: string; name: string }[] = [];

    for (const one of offered) {
      if (!isPlainObject(one) || drivers.length >= VOTE_MOST_DRIVERS) {
        continue;
      }

      const id = typeof one["id"] === "string" ? one["id"].trim().slice(0, VOTE_MAX_CHARS) : "";
      const name = typeof one["name"] === "string" ? one["name"].trim().slice(0, VOTE_MAX_CHARS) : "";

      // An id that storage would refuse is dropped here rather than at the
      // moment somebody votes for it, when the answer would be a 400 that
      // looked like the voter's fault.
      if (id.length === 0 || name.length === 0 || !isValidItemId(id) || seen.has(id)) {
        continue;
      }

      seen.add(id);
      drivers.push({ id, name });
    }

    // A NEW BALLOT IS A NEW RACE, so the votes for the one before it go.
    // Without this, opening the B-main's vote would start it with the
    // A-main's votes already in it.
    //
    // BY THE POLL'S ID when both ballots carry one. The label alone took a
    // vote reopened on the same race for the same vote, kept the relay's
    // votes, and handed them back into the laptop's new, empty poll.
    // WHY (20 Oct 2026, Bug Hunt 8): through queueSnapshotOf. A corrupt
    // ballot file threw here, answered 503, and this route is the only
    // writer of it, so the room vote stayed dead for good. Read as none,
    // it is a different race: a new salt, a good file, the votes cleared.
    const previous = (await this.queueSnapshotOf(club, VOTE_BALLOT_DOC))?.body;
    const previousId = isPlainObject(previous) && typeof previous["ballot"] === "string" ? previous["ballot"] : "";

    // THE ID WHERE THERE IS ONE, THE LABEL WHERE THERE IS NOT, and that
    // fallback is deliberate rather than tidy. A laptop from before this
    // change sends no raceKey, and for it the old comparison has to go on
    // working exactly as it did: this route is also called when a vote
    // CLOSES, and a close that failed to match would delete the votes
    // before the laptop had read them - which is throwing away the answer,
    // the thing the clearing block below is careful not to do.
    //
    // A laptop that upgrades mid-ballot has an id on the way in and none
    // in the stored document, so the two differ and the open vote's votes
    // are cleared. That is the safe direction of the two: a spurious clear
    // costs a ballot that was being re-opened anyway, where a missed clear
    // carries one race's votes into another.
    const identityOf = (held: Record<string, unknown>): string => {
      const key = typeof held["raceKey"] === "string" ? held["raceKey"] : "";

      return key.length > 0 ? "k:" + key : "l:" + (typeof held["race"] === "string" ? held["race"] : "");
    };

    const identity = raceKey.length > 0 ? "k:" + raceKey : "l:" + race;

    const sameRace =
      isPlainObject(previous) &&
      identityOf(previous) === identity &&
      (raceKey.length > 0 || race.length > 0) &&
      // And the laptop's poll id when both ballots carry one: a vote
      // reopened on the same race is a new, empty poll on the laptop.
      (ballotId.length === 0 || previousId.length === 0 || previousId === ballotId);

    const salt =
      sameRace && typeof previous["salt"] === "string" && previous["salt"].length > 0
        ? previous["salt"]
        : randomBytes(16).toString("hex");

    await this.store.putSnapshot(club, VOTE_BALLOT_DOC, {
      seq: this.now().getTime(),
      publishedAt: this.now().toISOString(),
      body: { enabled, open, race, raceKey, ballot: ballotId, drivers, salt },
    });

    // CLEARED when the club switches the feature off, and when the ballot
    // moves to a different race. Not when a vote merely closes: the
    // laptop reads the votes AFTER the race to work out its verdict, and
    // throwing them away at the flag would be throwing away the answer.
    if (!enabled || !sameRace) {
      await this.withLock(club, async () => {
        // WHY (20 Oct 2026, Bug Hunt 8): ids from the directory listing.
        // listItems read and parsed every file and quietly skipped one that
        // would not read (corrupt, EIO), so that file was never deleted and
        // came back when the switch went on again. itemSizes needs no parse.
        for (const item of await this.store.itemSizes(club, VOTE_CHANNEL)) {
          await this.store.deleteItem(club, VOTE_CHANNEL, item.id);
        }
      });
    }

    sendJson(response, 200, { ok: true, enabled, open, drivers: drivers.length });
  }

  /**
   * The grid to pick from, for anybody with the club's link.
   *
   * NO TALLY GOES OUT, and that is deliberate - the same rule the laptop's
   * own page has. A room that can see what it is voting for is a
   * bandwagon rather than a prediction, and the announcer's line is only
   * worth anything if each vote was that person's own guess.
   */
  private async roomVoteBallot(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const ballot = await this.roomBallotFor(club);

    if (ballot === undefined) {
      // 404 rather than 403, as every other switched-off route here does:
      // a club that has not asked for this has no such page.
      sendJson(response, 404, { ok: false, error: "Not found" });
      return;
    }

    sendJson(response, 200, {
      ok: true,
      open: ballot.open,
      race: ballot.race,
      drivers: ballot.drivers,
    });
  }

  /**
   * A vote from anybody with the club's link.
   *
   * PUBLIC, and the only thing it can achieve is one of the laptop's own
   * driver ids landing in a list the laptop then counts. See VOTE_CHANNEL
   * for why that makes this route safe to have without an approval gate.
   */
  /**
   * A phone saw the sponsors card, or tapped one of them - see SPONSOR_VIEWS.
   * {seen: [id, ...]} or {tap: id}. Ids not in the published sponsors are
   * dropped; a body with none left is refused.
   */
  private async sponsorSeen(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = `${club}|${clientAddress(request)}`;

    if (this.sponsorTraffic.blocked(who)) {
      sendJson(response, 429, { ok: false, error: "Counted already." });
      return;
    }

    this.sponsorTraffic.hit(who);

    const body = await readBody(request, response, SPONSOR_SEEN_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That could not be read." });
      return;
    }

    let published: StoredDocument | undefined;

    try {
      published = await this.store.getSnapshot(club, "sponsors");
    } catch {
      sendJson(response, 503, { ok: false, error: "Try again in a moment." });
      return;
    }

    const listed = isPlainObject(published?.body) && Array.isArray((published.body as Record<string, unknown>)["sponsors"])
      ? ((published.body as Record<string, unknown>)["sponsors"] as unknown[])
      : [];
    const known = new Set(listed
      .map((one) => (isPlainObject(one) && typeof one["id"] === "string" ? one["id"] : ""))
      .filter((id) => id !== ""));

    if (known.size === 0) {
      sendJson(response, 404, { ok: false, error: "Not found" });
      return;
    }

    const seen = Array.isArray(body["seen"])
      ? [...new Set((body["seen"] as unknown[]).filter((id): id is string => typeof id === "string" && known.has(id)))].slice(0, 50)
      : [];
    const tap = typeof body["tap"] === "string" && known.has(body["tap"]) ? body["tap"] : "";

    if (seen.length === 0 && tap === "") {
      sendJson(response, 400, { ok: false, error: "None of those are this club's sponsors." });
      return;
    }

    const at = this.now();
    const hour = at.toISOString().slice(0, 13);
    const oldest = new Date(at.getTime() - SPONSOR_VIEWS_DAYS * 24 * 3600 * 1000).toISOString().slice(0, 13);

    const counted = await this.withLock(club, async () => {
      let held: StoredDocument | undefined;

      try {
        held = await this.store.getSnapshot(club, SPONSOR_VIEWS);
      } catch (error) {
        // Unreadable for good (not JSON) starts again; a read that merely
        // failed is not written over.
        if (!(error instanceof StoreReadError && error.corrupt)) return false;
      }

      const was = isPlainObject(held?.body) && isPlainObject((held.body as Record<string, unknown>)["hours"])
        ? ((held.body as Record<string, unknown>)["hours"] as Record<string, unknown>)
        : {};
      const hours: Record<string, Record<string, [number, number]>> = {};

      for (const [key, slot] of Object.entries(was)) {
        if (key < oldest || !isPlainObject(slot)) continue;
        const kept: Record<string, [number, number]> = {};
        for (const [id, pair] of Object.entries(slot)) {
          if (Array.isArray(pair) && pair.length === 2 && Number.isFinite(pair[0]) && Number.isFinite(pair[1])) {
            kept[id] = [Number(pair[0]), Number(pair[1])];
          }
        }
        hours[key] = kept;
      }

      const now = hours[hour] ?? {};

      for (const id of seen) {
        const pair = now[id] ?? [0, 0];
        now[id] = [pair[0] + 1, pair[1]];
      }

      if (tap !== "") {
        const pair = now[tap] ?? [0, 0];
        now[tap] = [pair[0], pair[1] + 1];
      }

      hours[hour] = now;

      await this.store.putSnapshot(club, SPONSOR_VIEWS, {
        seq: at.getTime(),
        publishedAt: at.toISOString(),
        body: { hours },
      });

      return true;
    });

    if (!counted) {
      sendJson(response, 503, { ok: false, error: "Try again in a moment." });
      return;
    }

    sendJson(response, 200, { ok: true });
  }

  /** The hourly sponsor counts, for the laptop's sponsor report. Publish token only. */
  private async sponsorViews(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    let held: StoredDocument | undefined;

    try {
      held = await this.store.getSnapshot(club, SPONSOR_VIEWS);
    } catch {
      sendJson(response, 503, { ok: false, error: "Try again in a moment." });
      return;
    }

    const hours = isPlainObject(held?.body) && isPlainObject((held.body as Record<string, unknown>)["hours"])
      ? (held.body as Record<string, unknown>)["hours"]
      : {};

    sendJson(response, 200, { ok: true, hours });
  }

  private async sendRoomVote(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = `${club}|${clientAddress(request)}`;

    if (this.voteTraffic.blocked(who)) {
      sendJson(response, 429, {
        ok: false,
        error: "You have changed your mind a few times already - that one stands.",
      });
      return;
    }

    this.voteTraffic.hit(who);

    const ballot = await this.roomBallotFor(club);

    if (ballot === undefined) {
      sendJson(response, 404, { ok: false, error: "Not found" });
      return;
    }

    if (!ballot.open) {
      sendJson(response, 409, { ok: false, error: "Voting on this one has closed." });
      return;
    }

    const body = await readBody(request, response, VOTE_SEND_MAX_BODY);

    if (body === "too-big" || body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That vote could not be read." });
      return;
    }

    const picked = typeof body["d"] === "string" ? body["d"].trim() : "";

    if (picked.length === 0) {
      sendJson(response, 400, { ok: false, error: "There is nobody in it." });
      return;
    }

    // ONLY A NAME THE LAPTOP PUT ON THE BALLOT. This is what keeps the
    // channel to a set of the laptop's own ids rather than a place to
    // write arbitrary strings into a club's folder.
    if (!ballot.drivers.some((one) => one.id === picked)) {
      sendJson(response, 400, { ok: false, error: "That driver is not on the grid for this race." });
      return;
    }

    const voter = this.roomVoterKey(ballot.salt, request);

    const outcome = await this.withLock(club, async () => {
      // Read again inside the lock: the club may have closed the vote, or
      // moved the ballot on, while this request waited for it. Moved on is
      // a new salt as well as a new race: this vote's key was made from
      // the old one, so written now it would count in the next race under
      // a key the same phone's vote there would not replace.
      const now = await this.roomBallotFor(club);

      if (
        now === undefined ||
        !now.open ||
        now.salt !== ballot.salt ||
        now.race !== ballot.race ||
        !now.drivers.some((one) => one.id === picked)
      ) {
        return "closed" as const;
      }

      const held = await this.store.itemSizes(club, VOTE_CHANNEL);

      // The ceiling only bites on a voter who is not already in it - a
      // vote being CHANGED must never be turned away for a limit about
      // how many people have voted.
      if (held.length >= VOTES_HELD_MOST && !held.some((one) => one.id === voter)) {
        return "full" as const;
      }

      await this.store.putItem(club, VOTE_CHANNEL, voter, {
        seq: this.now().getTime(),
        publishedAt: this.now().toISOString(),
        body: { d: picked },
      });

      return "sent" as const;
    });

    if (outcome === "closed") {
      sendJson(response, 409, { ok: false, error: "Voting on this one has closed." });
      return;
    }

    if (outcome === "full") {
      sendJson(response, 429, { ok: false, error: "That is as many votes as this one can take." });
      return;
    }

    sendJson(response, 200, { ok: true });
  }

  /**
   * Who a vote belongs to, without storing anything about them.
   *
   * The ballot's salt and the sender's address, hashed. The salt never
   * leaves this relay and is minted per race, so the id cannot be
   * correlated between two races or two clubs and nobody can test whether
   * a given address has voted. See VOTE_CHANNEL.
   */
  private roomVoterKey(salt: string, request: IncomingMessage): string {
    return createHash("sha256")
      .update(`${salt}|${clientAddress(request)}`)
      .digest("hex")
      .slice(0, 32);
  }

  /**
   * What the laptop collects: every vote held, by voter.
   *
   * NOT DRAINED, and this is the one place the vote's shape differs from
   * the shout-outs'. A note is said once and collecting it is the end of
   * it; a vote can be CHANGED after the laptop has already seen it, so
   * draining the set would lose the change and leave the laptop counting
   * a pick its owner had replaced. The laptop re-applies the whole set
   * each poll instead, which is free because RoomVote.Cast holds one vote
   * per voter and replacing a pick with the same pick does nothing.
   *
   * The set is cleared when the ballot moves to another race or the club
   * switches the feature off - see setRoomVoteBallot.
   */
  private async readRoomVotes(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const held = await this.store.listItems(club, VOTE_CHANNEL);

    const items = held
      .filter((item) => isPlainObject(item.doc.body))
      .slice()
      .sort((a, b) => a.doc.seq - b.doc.seq)
      .map((item) => ({
        v: item.id,
        d: typeof (item.doc.body as Record<string, unknown>)["d"] === "string"
          ? ((item.doc.body as Record<string, unknown>)["d"] as string)
          : "",
        // WHY (20 Oct 2026, Bug Hunt 8, #22): when the relay took this vote,
        // in milliseconds on its own clock (sendRoomVote stamps seq with
        // it). The laptop reads the inbox every fifteen seconds, and
        // without this a vote cast just before the close was judged by
        // when it was read and refused as "closed".
        at: item.doc.seq,
      }))
      .filter((item) => item.d.length > 0)
      .slice(0, VOTES_HELD_MOST);

    sendJson(response, 200, { ok: true, items });
  }

  private async readSignupInbox(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const box = await this.signupBoxOf(club);

    // Told to the laptop, which polls here anyway, as readInbox tells it
    // check-in's: the Online Sign-ups window says code sign-in is locked,
    // and until when.
    const pinLockedUntil = await this.signupPinGuard.lockedUntil(club);

    sendJson(response, 200, {
      ok: true,
      items: box
        .filter((item) => item.state === "pending" && item.form !== undefined)
        .map((item) => ({ h: item.h, form: item.form, submittedAt: item.submittedAt })),
      ...(pinLockedUntil > 0 ? { pinLockedUntil: new Date(pinLockedUntil).toISOString() } : {}),
    });
  }

  /**
   * The laptop's answer to one sign-up, exactly as RelaySignupDesk.Answer
   * builds it:
   *
   *   { h, state: "approved" | "rejected", message, checkedIn, driver }
   *
   * The form goes and the answer takes its place in one write, so the
   * laptop never collects that sign-up again and the phone's next poll
   * finds the answer. An answer for a sign-up the relay is not holding is
   * kept all the same - it is the desk's word on that invite, and the phone
   * may yet ask. A 400 here is final: the laptop stops sending that one.
   */
  private async answerSignup(request: IncomingMessage, response: ServerResponse, club: string): Promise<void> {
    const who = this.authorise(request, club);

    if (who === "no-token") {
      sendJson(response, 401, { ok: false, error: "A publish token is required." });
      return;
    }

    if (who === "wrong-club") {
      sendJson(response, 403, { ok: false, error: "That token does not belong to this club." });
      return;
    }

    const body = await readBody(request, response, SIGNUP_ANSWER_MAX_BODY);

    if (body === "too-big") {
      sendJson(response, 413, { ok: false, error: `An answer may be at most ${SIGNUP_ANSWER_MAX_BODY} bytes.` });
      return;
    }

    if (body === "unreadable" || !isPlainObject(body)) {
      sendJson(response, 400, { ok: false, error: "That request could not be read." });
      return;
    }

    const h = typeof body["h"] === "string" ? (body["h"] as string).toLowerCase() : "";

    if (!PIN_SHA256.test(h)) {
      sendJson(response, 400, { ok: false, error: "h must be an invite's hash." });
      return;
    }

    const state = body["state"];

    if (state !== "approved" && state !== "rejected") {
      sendJson(response, 400, { ok: false, error: 'state must be "approved" or "rejected".' });
      return;
    }

    const message = cleanText(body["message"], 300);
    const driver = state === "approved" ? signupDriverFrom(body["driver"]) : undefined;

    await this.withLock(club, async () => {
      const current = await this.signupBoxOf(club);
      const now = this.now().toISOString();
      const before = current.find((item) => item.h === h);

      const answered: SignupItem = {
        h,
        state,
        submittedAt: before?.submittedAt ?? now,
        message,
        checkedIn: state === "approved" && body["checkedIn"] === true,
        ...(driver === undefined ? {} : { driver }),
        answeredAt: now,
      };

      // Everything waiting is kept - dropping one would lose a sign-up
      // nobody has seen. Past the ceiling, the answers read longest ago
      // make way, and never the one just given.
      const others = current.filter((item) => item.h !== h);
      const waiting = others.filter((item) => item.state === "pending");
      const done = others
        .filter((item) => item.state !== "pending")
        .sort((a, b) => signupItemAge(a) - signupItemAge(b));
      const room = Math.max(0, SIGNUP_MAX_ITEMS - 1 - waiting.length);
      const kept = done.slice(Math.max(0, done.length - room));

      await this.store.putSnapshot(club, SIGNUP_BOX, {
        seq: 0,
        publishedAt: now,
        body: { items: [...waiting, ...kept, answered] },
      });
    });

    sendJson(response, 200, { ok: true });
  }
}

/**
 * How much work one PIN hash costs: PBKDF2-HMAC-SHA256 at this many
 * rounds. Must match PinKdf.Iterations on the laptop - the two make the
 * same hash from the same PIN or nobody signs in.
 *
 * WHY. A PIN hash is published so that a copy of a stored document is not
 * a copy of the PIN. One SHA-256 over four to eight digits did not manage
 * that: every PIN at a club fell to 10^6 hashes, well under a second, and
 * all of them together, since they shared everything but the digits (25
 * Sep security audit). A random salt per club and a hundred thousand
 * rounds make each guess cost what a hundred thousand guesses did, and
 * no table made for one club says anything about another.
 *
 * What it does NOT do is make a short PIN strong. Six digits are a
 * million guesses; at this cost that is minutes on a graphics card, not
 * forever. It turns "instant, for every club at once" into "a deliberate
 * effort against one", which is what a salt and a work factor can buy.
 *
 * A hundred thousand, not more, because this relay pays it too - once per
 * sign-in attempt, on the event loop (see checkinRequest for why nothing
 * there may await between judging a guess and counting it). About a tenth
 * of a second on a small shared CPU; the PIN limits keep the number of
 * attempts small.
 */
export const PIN_KDF_ITERATIONS = 100_000;

/**
 * Slow PIN hashes one address may make the relay pay for one club in a
 * minute - check-in, the manager and sign-up together, right PINs as well
 * as wrong ones. A right PIN is not a guess, so the PIN limits never count
 * it, and before this a racer holding their own PIN could sign in in a
 * loop and spend twenty seconds or more of the relay's CPU a minute, with
 * every club's live timing waiting behind it. Forty is a paddock of phones
 * on the venue's one address all signing in in the same minute (a
 * check-in sign-in costs two: the staff PIN and the racer's) - and four
 * seconds of CPU a minute at most from any one address.
 */
export const SLOW_HASHES_PER_ADDRESS = 40;

/**
 * And for the whole relay at once, whoever asks - the ceiling that holds
 * however many addresses somebody has (an IPv6 /64 each is not hard to
 * come by). About eighteen seconds of CPU a minute, under a third of the
 * small machine, so live timing always has the rest. Past either, the
 * sign-in is refused with "busy, try again in a minute" and nothing is
 * hashed or counted as a guess. A phone already signed in is not affected:
 * it proves itself with a pass, which costs no slow hash.
 */
export const SLOW_HASHES_IN_ALL = 180;

/** A club's PIN salt as the laptop publishes it: 32 random bytes, hex. */
const PIN_SALT = /^[0-9a-f]{64}$/;

/**
 * The salted, slow recipe every PIN hash uses once the laptop publishes a
 * salt. The purpose - which PIN this is - goes in front of the club and
 * the salt, so a staff PIN, a racer PIN, a manager PIN and an invite code
 * can never open each other, whatever digits they share.
 */
function pinKdf(purpose: string, club: string, salt: string, secret: string): string {
  return pbkdf2Sync(secret, `${purpose}|${club}|${salt}`, PIN_KDF_ITERATIONS, 32, "sha256").toString("hex");
}

/**
 * What the laptop hashes the staff check-in PIN to, and what the relay
 * hashes a guess to. The club is in it so one club's hash says nothing
 * about another's. With a salt, the salted recipe; without one - a laptop
 * from before salting - the old single SHA-256, so an older laptop keeps
 * working against this relay.
 */
export function pinHash(club: string, pin: string, salt = ""): string {
  return salt !== ""
    ? pinKdf("hg-checkin2", club, salt, pin.trim())
    : createHash("sha256").update(`hg-checkin|${club}|${pin.trim()}`, "utf8").digest("hex");
}

/**
 * The same for a racer's own PIN, with its own prefix: were the two the
 * same, a racer whose PIN happened to equal the desk's would publish the
 * staff hash on their own row, and a staff PIN could open one racer.
 */
export function racerPinHash(club: string, pin: string, salt = ""): string {
  return salt !== ""
    ? pinKdf("hg-racer2", club, salt, pin.trim())
    : createHash("sha256").update(`hg-racer|${club}|${pin.trim()}`, "utf8").digest("hex");
}

/**
 * What the laptop hashes the race-day manager's PIN to, and what the
 * relay hashes a guess to. Its own prefix, entirely apart from pinHash and
 * racerPinHash: a club whose manager PIN happens to equal its check-in PIN
 * must not let one open the other.
 */
export function managePinHash(club: string, pin: string, salt = ""): string {
  return salt !== ""
    ? pinKdf("hg-manager2", club, salt, pin.trim())
    : createHash("sha256").update(`hg-manager|${club}|${pin.trim()}`, "utf8").digest("hex");
}

/**
 * What the laptop hashes a driver sign-up invite code to
 * (SignupStore.InviteHash), and what the relay hashes a typed code to. Its
 * own prefix again, so an invite code can never open check-in, a racer or
 * the manager, whatever digits it happens to share with their PINs.
 *
 * The laptop hashes the code as it made it - eight digits, trimmed - but
 * shows it to people as "1234 5678", and people type what they see. So the
 * spaces and dashes a person may type are taken out here before hashing
 * (see normaliseInviteCode). For the laptop's own codes, which have
 * neither, that is exactly its trim, byte for byte.
 */
export function signupPinHash(club: string, code: string, salt = ""): string {
  return salt !== ""
    ? pinKdf("hg-signup2", club, salt, normaliseInviteCode(code))
    : createHash("sha256").update(`hg-signup|${club}|${normaliseInviteCode(code)}`, "utf8").digest("hex");
}

/**
 * Which hash a typed invite code is known by: the salted one when the
 * laptop has published a salt and an invite (or a sign-up already held)
 * is known by it, otherwise the old unsalted one if THAT is known, and
 * otherwise whichever the laptop would make for a new invite now.
 *
 * Both, because an invite lives for two weeks and outlives the laptop
 * being updated: invites made before it had a salt are still published
 * under the old hash until they expire, and new ones under the salted
 * hash - with nothing on the invite to say which. Trying both costs one
 * SHA-256 beside the slow one.
 */
function inviteHashFor(club: string, code: string, view: SignupView | undefined, box: readonly { readonly h: string }[]): string {
  const salted = view !== undefined && view.salt !== "" ? signupPinHash(club, code, view.salt) : "";
  const plain = signupPinHash(club, code);
  const known = (h: string): boolean => h !== "" && (view?.invites.has(h) === true || box.some((item) => item.h === h));

  if (known(salted)) {
    return salted;
  }

  if (known(plain)) {
    return plain;
  }

  return salted !== "" ? salted : plain;
}

/** The club's PIN salt in a published document, or "" when it carries none (a laptop from before salting). */
function pinSaltOf(body: unknown): string {
  const salt = isPlainObject(body) ? body["pinSalt"] : undefined;

  return typeof salt === "string" && PIN_SALT.test(salt) ? salt : "";
}

/** An invite code as typed, with the spaces and dashes a person copies from "1234 5678" taken out. */
function normaliseInviteCode(code: string): string {
  return code.replace(/[\s-]/g, "");
}

const PIN_SHA256 = /^[0-9a-f]{64}$/;
const PASS = /^[0-9a-f]{64}$/;

/**
 * The inner half of a pass: a keyed hash of the PIN's own hash. It changes
 * when the PIN does, so a changed PIN signs out every device holding the
 * old one. It is NOT a pass on its own any more - anyone can make it from
 * a PIN they mean to try - and is only ever handed out wrapped in the
 * club's pass key (keyedPass). Exported so a test can show the bare recipe
 * opens nothing. The kind is in it so a staff
 * pass, a racer pass, a manager pass and a sign-up pass can never be each
 * other.
 *
 * A sign-up pass is made from its invite's hash, not from anything the
 * desk can change, so it keeps working after the laptop has taken a spent
 * invite off the relay: that is when the phone holding it is waiting for
 * its answer.
 */
export function passFor(kind: "staff" | "racer" | "manager" | "signup", club: string, hash: string): string {
  return createHmac("sha256", hash).update(`hg-pass|${kind}|${club}`, "utf8").digest("hex");
}

/**
 * What a pass actually is: passFor, keyed again with the club's own random
 * pass key (see Relay.passKeyOf).
 *
 * passFor alone needs nothing but the PIN - the PIN's hash is a public
 * recipe - so anyone could make the pass for every PIN they meant to try,
 * offline, and send passes instead of PINs. A pass is judged before any
 * PIN limit, and one that opens nothing was never counted, so that was
 * guessing with no limit at all: through check-in once an address or the
 * club was locked out, and through manage/request, manage/response,
 * signup/status and signup/submit always. With a key only this relay
 * holds, a pass is 256 bits nobody can make, which is what the rest of
 * this file has always taken it to be. It still changes when the PIN
 * does, and still survives a restart - the key is on disk.
 */
export function keyedPass(key: Buffer, kind: "staff" | "racer" | "manager" | "signup", club: string, hash: string): string {
  return createHmac("sha256", key).update(`hg-pass2|${passFor(kind, club, hash)}`, "utf8").digest("hex");
}

/**
 * The handle the staff check-in list shows in place of a driver's badge id
 * (23 Oct 2026, bug hunt 10 - see checkinRequest). A badge id is the whole
 * credential for a driver's private night, so a list shared with every
 * gate volunteer must not carry it. Twenty hex characters, the shape of a
 * badge id, so the page handles it the same way; keyed with the relay-only
 * pass key, so a leaked list cannot be turned back into badge ids; and
 * made with the staff PIN's hash in it, so it goes stale when the PIN
 * changes. Its own label, so it can never be mistaken for a pass.
 */
export function staffRowRef(key: Buffer, club: string, staffPinSha256: string, driverId: string): string {
  return createHmac("sha256", key).update(`hg-staffrow|${club}|${staffPinSha256}|${driverId}`, "utf8").digest("hex").slice(0, 20);
}

/** Where a club's pass key is kept. A snapshot name no channel uses, like pinguard: nobody can publish it or read it, and deleting the club deletes it. */
const PASS_KEY_DOC = "passkey";

/** The staff PIN hash in a check-in document as published, or "" - read without cleaning the rest of it, which is big. */
function staffPinOf(body: unknown): string {
  const stored = isPlainObject(body) ? body["pinSha256"] : undefined;

  return typeof stored === "string" && PIN_SHA256.test(stored) ? stored : "";
}

/** The manager PIN hash in a manage document as published, or "" - staffPinOf's twin for the other document. */
function managePinOf(body: unknown): string {
  const stored = isPlainObject(body) ? body["pinSha256"] : undefined;

  return typeof stored === "string" && PIN_SHA256.test(stored) ? stored : "";
}

/**
 * A programme with its heat line-ups taken out - "lineups" false and every
 * line's drivers null, exactly what a laptop with line-ups off sends - for
 * a club whose relay entry does not allow its roster (Tenant.publishRoster).
 *
 * Only the laptop's switch used to decide this. The roster channel itself
 * was refused, but the same names, split by heat, went out in the
 * programme for anyone to read on the Schedule page - though the relay's
 * operator never agreed to publish them. Only those two fields change,
 * and only where they are there; anything not shaped as expected is left
 * as it is rather than refused, so this never throws.
 */
function withoutLineups(body: unknown): unknown {
  if (!isPlainObject(body)) {
    return body;
  }

  const lines = body["lines"];

  return {
    ...body,
    ...("lineups" in body ? { lineups: false } : {}),
    ...(Array.isArray(lines)
      ? {
          lines: lines.map((line: unknown) =>
            // The grid (7 Nov 2026) is the same names again, with their slot
            // and car, so it goes with them.
            isPlainObject(line) && ("drivers" in line || "grid" in line)
              ? { ...line, ...("drivers" in line ? { drivers: null } : {}), ...("grid" in line ? { grid: null } : {}) }
              : line,
          ),
        }
      : {}),
  };
}

/** The laptop's manage document, cleaned. Undefined when there is none, or it is not an object - readCheckin's twin, much simpler: nothing here but whether it is on and what it is guarded by. */
function readManage(club: string, doc: StoredDocument | undefined, key: Buffer): ManageView | undefined {
  const manage = isPlainObject(doc?.body) ? doc.body : undefined;

  if (manage === undefined) {
    return undefined;
  }

  const pinSha256 = managePinOf(manage);

  return {
    enabled: manage["enabled"] === true,
    pinSha256,
    salt: pinSaltOf(manage),
    pass: pinSha256 === "" ? "" : keyedPass(key, "manager", club, pinSha256),
  };
}

/**
 * The laptop's signup document, cleaned (SignupStore.Document):
 *
 *   { enabled, event, classes: [..], invites: [{ h, x }] }
 *
 * h is an invite's hash and x when it stops working, ISO 8601 UTC. An
 * invite without a readable expiry is left out rather than taken to last
 * for ever. Undefined when there is no document, or it is not an object.
 */
function readSignup(doc: StoredDocument | undefined): SignupView | undefined {
  const signup = isPlainObject(doc?.body) ? doc.body : undefined;

  if (signup === undefined) {
    return undefined;
  }

  const classes: string[] = [];
  const seen = new Set<string>();

  for (const entry of Array.isArray(signup["classes"]) ? (signup["classes"] as unknown[]) : []) {
    const name = cleanText(entry, 60);

    if (name !== "" && !seen.has(name.toLowerCase()) && classes.length < SIGNUP_MAX_CLASSES) {
      seen.add(name.toLowerCase());
      classes.push(name);
    }
  }

  const invites = new Map<string, number>();

  for (const entry of Array.isArray(signup["invites"]) ? (signup["invites"] as unknown[]).slice(0, SIGNUP_MAX_INVITES) : []) {
    const h = isPlainObject(entry) && typeof entry["h"] === "string" ? entry["h"].toLowerCase() : "";
    const x = isPlainObject(entry) && typeof entry["x"] === "string" ? Date.parse(entry["x"]) : NaN;

    if (PIN_SHA256.test(h) && Number.isFinite(x)) {
      invites.set(h, x);
    }
  }

  return {
    enabled: signup["enabled"] === true,
    event: cleanText(signup["event"], 80),
    classes,
    invites,
    salt: pinSaltOf(signup),
  };
}

/** Every invite hash in a signup document as published, read without the rest - for noticing when the whole list has been replaced. */
function inviteHashesOf(body: unknown): Set<string> {
  const invites = isPlainObject(body) && Array.isArray(body["invites"]) ? (body["invites"] as unknown[]) : [];
  const hashes = new Set<string>();

  for (const entry of invites.slice(0, SIGNUP_MAX_INVITES)) {
    const h = isPlainObject(entry) && typeof entry["h"] === "string" ? entry["h"].toLowerCase() : "";

    if (PIN_SHA256.test(h)) {
      hashes.add(h);
    }
  }

  return hashes;
}

/**
 * One line of text: trimmed, runs of white space made one space, control
 * characters out, and no longer than max. "" for anything but a string.
 */
function cleanText(value: unknown, max: number): string {
  if (typeof value !== "string") {
    return "";
  }

  return value.replace(/[\u0000-\u001f\u007f]/g, " ").replace(/\s+/g, " ").trim().slice(0, max);
}

/**
 * A new driver's form, checked - or, when it will not do, what to tell
 * them, in words the sign-up page shows as they are. Every field the
 * laptop reads is here, in the laptop's own limits (RelayPublisher's
 * ReadSignup cuts each to the same length), and nothing else is kept.
 *
 * The class must be one the club offered, matched without regard to case
 * and stored as the club spells it, so the desk never sees a class it does
 * not have. A club with no classes at all offers none, and the class is
 * then whatever the driver typed.
 */
function signupFormFrom(value: unknown, classes: readonly string[]): SignupForm | string {
  if (!isPlainObject(value)) {
    return "That sign-up could not be read.";
  }

  const raw = (key: string) => (typeof value[key] === "string" ? (value[key] as string) : "");
  const name = cleanText(value["name"], 1000);

  if (name === "") {
    return "Type the driver's name.";
  }

  if (name.length > 60) {
    return "The driver's name is too long - 60 letters at most.";
  }

  const typedClass = cleanText(value["className"], 1000);
  let className = typedClass;

  if (classes.length > 0) {
    const match = classes.find((c) => c.toLowerCase() === typedClass.toLowerCase());

    if (match === undefined) {
      return "Pick a class from the list.";
    }

    className = match;
  } else if (typedClass.length > 60) {
    return "The class is too long - 60 letters at most.";
  }

  const kind = value["transponderKind"];

  if (kind !== "own" && kind !== "rental" && kind !== "unknown") {
    return "Say whether you have your own transponder, would like to rent one, or don't know yet.";
  }

  // Letters and digits only: a transponder number is printed on the
  // transponder, and whatever it is typed as goes straight into the desk's
  // driver list. Only asked for with "own"; a rental or "don't know yet"
  // keeps no number, whatever the phone sent.
  const transponder = kind === "own" ? raw("transponder").trim() : "";

  if (kind === "own" && transponder === "") {
    return "Type your transponder's number, or pick one of the other two.";
  }

  if (kind === "own" && !/^[A-Za-z0-9]{1,20}$/.test(transponder)) {
    return "A transponder number is letters and digits only - check it and type it again.";
  }

  const car = cleanText(value["car"], 1000);

  if (car.length > 60) {
    return "The car is too long - 60 letters at most.";
  }

  const email = raw("email").trim();

  if (email !== "" && (email.length > 254 || !/^[^\s@<>"',;]+@[^\s@<>"',;]+\.[^\s@<>"',;]+$/.test(email))) {
    return "That email address doesn't look right.";
  }

  return {
    name,
    className,
    transponderKind: kind,
    transponder,
    car,
    email,
    guardian: value["guardian"] === true,
  };
}

/** A form as the relay stored it, read back from disk - only the relay writes it, but one bad entry should cost that entry and nothing more. */
function storedSignupForm(value: unknown): SignupForm | undefined {
  if (!isPlainObject(value)) {
    return undefined;
  }

  const kind = value["transponderKind"];

  if (kind !== "own" && kind !== "rental" && kind !== "unknown") {
    return undefined;
  }

  return {
    name: cleanText(value["name"], 60),
    className: cleanText(value["className"], 60),
    transponderKind: kind,
    transponder: kind === "own" ? cleanText(value["transponder"], 20) : "",
    car: cleanText(value["car"], 60),
    email: cleanText(value["email"], 254),
    guardian: value["guardian"] === true,
  };
}

/**
 * The approved driver from the laptop's answer, reduced to the four
 * fields a phone is told - whatever else the answer carried is dropped, so
 * nothing the laptop adds later can reach a phone by accident. Undefined
 * with no driver (online check-in is off on the laptop, so there is no
 * card to link to) or one without a proper id.
 */
function signupDriverFrom(value: unknown): SignupDriver | undefined {
  if (!isPlainObject(value) || typeof value["id"] !== "string" || !DRIVER_ID.test(value["id"])) {
    return undefined;
  }

  return {
    id: value["id"],
    name: cleanText(value["name"], 80),
    className: cleanText(value["className"], 60),
    number: cleanText(value["number"], 8),
  };
}

/** One entry of the relay's own sign-up document, checked on the way back in. */
function signupItemFrom(value: unknown): SignupItem | undefined {
  if (!isPlainObject(value)) {
    return undefined;
  }

  const h = value["h"];
  const state = value["state"];
  const submittedAt = value["submittedAt"];

  if (typeof h !== "string" || !PIN_SHA256.test(h) || typeof submittedAt !== "string") {
    return undefined;
  }

  if (state === "pending") {
    const form = storedSignupForm(value["form"]);
    return form === undefined ? undefined : { h, state, submittedAt, form };
  }

  if ((state !== "approved" && state !== "rejected") || typeof value["answeredAt"] !== "string") {
    return undefined;
  }

  const driver = signupDriverFrom(value["driver"]);

  return {
    h,
    state,
    submittedAt,
    message: cleanText(value["message"], 300),
    checkedIn: value["checkedIn"] === true,
    ...(driver === undefined ? {} : { driver }),
    answeredAt: value["answeredAt"],
  };
}

/** When a sign-up last changed - sent, or answered - in milliseconds. NaN for a date that cannot be read, which no age test passes. */
function signupItemAge(item: SignupItem): number {
  return Date.parse(item.answeredAt ?? item.submittedAt);
}

/**
 * Where a sign-up stands, as its phone is told:
 *
 *   { state: "open" }                                   nothing sent yet
 *   { state: "pending", form, submittedAt }             with the desk
 *   { state: "approved" | "rejected", message,
 *     checkedIn, driver? }                              answered
 *
 * The form goes back while it waits so "Change my details" starts from
 * what was sent; once answered it is not the relay's any more.
 */
function signupStatusOf(item: SignupItem | undefined): Record<string, unknown> {
  if (item === undefined) {
    return { state: "open" };
  }

  if (item.state === "pending") {
    return { state: "pending", form: item.form, submittedAt: item.submittedAt };
  }

  return {
    state: item.state,
    message: item.message ?? "",
    checkedIn: item.checkedIn === true,
    ...(item.driver === undefined ? {} : { driver: item.driver }),
  };
}

/** The laptop's check-in document, cleaned. Undefined when there is none, or it is not an object. */
function readCheckin(club: string, doc: StoredDocument | undefined, key: Buffer): CheckinView | undefined {
  const checkin = isPlainObject(doc?.body) ? doc.body : undefined;

  if (checkin === undefined) {
    return undefined;
  }

  const drivers: CheckinDriver[] = [];
  const racers = new Map<string, RacerEntry>();
  const shared = new Set<string>();

  for (const entry of Array.isArray(checkin["drivers"]) ? (checkin["drivers"] as unknown[]) : []) {
    const driver = cleanDriver(entry);

    if (driver === undefined) {
      continue;
    }

    drivers.push(driver);

    const rp = (entry as Record<string, unknown>)["rp"];

    if (typeof rp !== "string" || !PIN_SHA256.test(rp)) {
      continue;
    }

    // Two drivers with one PIN is the laptop's mistake, and neither of
    // them is opened by it: handing whoever typed it the wrong person's
    // check-in would be worse than handing them "not right".
    if (racers.has(rp)) {
      shared.add(rp);
    } else {
      racers.set(rp, { driver, pass: keyedPass(key, "racer", club, rp) });
    }
  }

  for (const rp of shared) {
    racers.delete(rp);
  }

  // No second map from each racer's pass to its driver any more: a pass is
  // compared against every racer in turn, not looked up (racerByPass).
  const pinSha256 = staffPinOf(checkin);

  return {
    enabled: checkin["enabled"] === true,
    event: typeof checkin["event"] === "string" ? checkin["event"] : "",
    pinSha256,
    salt: pinSaltOf(checkin),
    staffPass: pinSha256 === "" ? "" : keyedPass(key, "staff", club, pinSha256),
    passKey: key,
    drivers,
    racers,
  };
}

function sameHash(a: string, b: string): boolean {
  const x = Buffer.from(a, "hex");
  const y = Buffer.from(b, "hex");

  return x.length === y.length && x.length > 0 && timingSafeEqual(x, y);
}

/**
 * The racer a pass opens, or undefined - found by comparing it with
 * sameHash against every racer's pass in turn, as the staff and manager
 * passes, their PINs and the publish tokens all are. It used to be a map
 * lookup keyed on the pass itself. A racer PIN is looked up by its hash,
 * which is fine (see checkinRequest), but a pass is the secret exactly as
 * sent, so how long finding it took depended - however faintly - on the
 * secret. Every racer is compared whether or not one before it matched, so
 * the time taken is the same wherever in the list the match is, or with
 * none at all. At most one can match: each pass is keyed by its racer's own
 * PIN hash, and a hash two drivers share opens neither (readCheckin). A
 * club has a few hundred racers at most, so the walk costs next to nothing.
 */
function racerByPass(checkin: CheckinView, pass: string): CheckinDriver | undefined {
  let found: CheckinDriver | undefined;

  for (const entry of checkin.racers.values()) {
    if (sameHash(pass, entry.pass)) {
      found = entry.driver;
    }
  }

  return found;
}

/**
 * This driver's own racer PIN hash and pass, or undefined when they have
 * none (26 Oct 2026) - what a tap carrying a PIN is checked against, and
 * nothing else. A hash two drivers share was dropped by readCheckin, so it
 * opens neither here either.
 */
function racerEntryOf(checkin: CheckinView, id: string): { hash: string; pass: string } | undefined {
  for (const [hash, entry] of checkin.racers) {
    if (entry.driver.id === id) {
      return { hash, pass: entry.pass };
    }
  }

  return undefined;
}

/** Whether this driver has a personal PIN - some racer entry opens them (22 Oct 2026, driver experience). */
function hasRacerPin(checkin: CheckinView, id: string): boolean {
  for (const entry of checkin.racers.values()) {
    if (entry.driver.id === id) {
      return true;
    }
  }

  return false;
}

/**
 * driverlive or driverme as stored, parsed down to each badge id's entry
 * (22 Oct 2026, driver experience). A blanked document (body null), or
 * anything that is not an object, reads as absent.
 */
function driverDocFrom(doc: StoredDocument | undefined): DriverDoc | undefined {
  if (doc === undefined || !isPlainObject(doc.body)) {
    return undefined;
  }

  const drivers = new Map<string, Readonly<Record<string, unknown>>>();
  const listed = doc.body["drivers"];

  if (isPlainObject(listed)) {
    for (const [id, entry] of Object.entries(listed)) {
      if (DRIVER_ID.test(id) && isPlainObject(entry)) {
        drivers.set(id, entry);
      }
    }
  }

  const at = doc.body["at"];

  return {
    takenAt: Date.parse(doc.publishedAt) || 0,
    at: typeof at === "string" && !Number.isNaN(Date.parse(at)) ? at : "",
    drivers,
  };
}

/** What a value costs written out as JSON, in bytes - how it is stored. */
function jsonBytes(value: unknown): number {
  return Buffer.byteLength(JSON.stringify(value));
}

/**
 * The newest items - the end of the array - that fit in maxBytes written
 * out as one JSON array, and always at least the newest one.
 */
function newestWithin<T>(items: readonly T[], maxBytes: number): T[] {
  // The two brackets, then each item from the newest back, with the comma
  // that joins it to the one after.
  let bytes = 2;
  let first = items.length;

  while (first > 0) {
    const size = jsonBytes(items[first - 1]) + (first < items.length ? 1 : 0);

    if (first < items.length && bytes + size > maxBytes) {
      break;
    }

    bytes += size;
    first--;
  }

  return items.slice(first);
}

/** A JSON object, as opposed to null, an array or a bare value - any of which a document's body may be. */
function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isInboxItem(value: unknown): value is InboxItem {
  return (
    isPlainObject(value) &&
    typeof value["n"] === "number" &&
    typeof value["id"] === "string" &&
    typeof value["wanted"] === "boolean" &&
    typeof value["atUtc"] === "string"
  );
}

function isManagePendingItem(value: unknown): value is ManagePendingItem {
  return isPlainObject(value) && typeof value["reqId"] === "string" && "body" in value && typeof value["enqueuedAt"] === "string";
}

/**
 * The most a signed payload may be, as text.
 *
 * The phone's own copy of a body this relay already caps at
 * MANAGE_MAX_BODY, so it cannot be the larger of the two - and a request
 * carries both. Kept a little under the body cap rather than equal to it
 * because the payload is the {op, args, confirm} alone, without the pass,
 * the reqId and the proof fields that share the request with it.
 */
const MANAGE_PAYLOAD_MAX = 60 * 1024;

/** A device id or a signature: hex, the length each is minted at. Anything else is not one and is dropped. */
const MANAGE_DEVICE_ID = /^[0-9a-f]{24}$/;
const MANAGE_MAC = /^[0-9a-f]{64}$/;

/**
 * A phone's proof, taken off a request or off a stored item, with anything
 * malformed left out.
 *
 * All four or none: a payload with no signature is text nobody vouched
 * for, and a signature with no payload signs nothing. Half a proof reaching
 * the laptop would only be refused there, a poll later, with the manager
 * left reading "not signed by the phone it claims to be from" about a phone
 * that signed it perfectly well.
 *
 * This is a shape check and nothing more. Whether the signature is RIGHT is
 * the laptop's business and could not be this relay's - see
 * ManagePendingItem.mac.
 */
function manageProofOf(value: unknown): Partial<Pick<ManagePendingItem, "payload" | "deviceId" | "sentAt" | "mac">> {
  if (!isPlainObject(value)) {
    return {};
  }

  const payload = value["payload"];
  const deviceId = value["deviceId"];
  const sentAt = value["sentAt"];
  const mac = value["mac"];

  if (
    typeof payload !== "string" ||
    payload === "" ||
    payload.length > MANAGE_PAYLOAD_MAX ||
    typeof deviceId !== "string" ||
    !MANAGE_DEVICE_ID.test(deviceId) ||
    typeof sentAt !== "string" ||
    sentAt.length > 40 ||
    typeof mac !== "string" ||
    !MANAGE_MAC.test(mac)
  ) {
    return {};
  }

  return { payload, deviceId, sentAt, mac };
}

function isManageAnswerItem(value: unknown): value is ManageAnswerItem {
  return isPlainObject(value) && typeof value["reqId"] === "string" && "response" in value && typeof value["answeredAt"] === "string";
}

/** One driver from the laptop's document, reduced to known fields of known types. Anything else is dropped. */
function cleanDriver(value: unknown): CheckinDriver | undefined {
  if (typeof value !== "object" || value === null) {
    return undefined;
  }

  const d = value as Record<string, unknown>;
  const text = (key: string, max: number) => (typeof d[key] === "string" ? (d[key] as string).slice(0, max) : "");

  if (typeof d["id"] !== "string" || !DRIVER_ID.test(d["id"] as string)) {
    return undefined;
  }

  const photo = text("photo", 60 * 1024);

  return {
    id: d["id"] as string,
    name: text("name", 80),
    className: text("className", 60),
    number: text("number", 8),
    checkedIn: d["checkedIn"] === true,
    photo: /^data:image\/(png|jpeg);base64,[A-Za-z0-9+/]+={0,2}$/.test(photo) ? photo : "",
  };
}

/**
 * Who is asking, for rate limits only - never for anything that decides
 * access.
 *
 * On Fly, the proxy puts the real address in Fly-Client-IP and overwrites
 * whatever a client sent, so it can be believed - and only there: anywhere
 * else a header is whatever the client says it is, and believing one
 * would let every request claim a fresh address and walk straight past
 * the PIN limit. Off Fly, the socket's own address is the only honest
 * answer. Fly marks its machines with FLY_APP_NAME.
 */
/**
 * Roughly what one club's previews cost to hold, in bytes: two a
 * character for every string (which is what V8 needs at worst) and a
 * flat allowance for each object around them. Not exact and not meant to
 * be - it only has to grow with what is really held, so that
 * PREVIEW_CACHE_MAX_TOTAL bounds something real.
 */
function previewBytes(book: ReadonlyMap<string, SessionPreview>): number {
  let bytes = 0;

  for (const session of book.values()) {
    bytes += 160;
    bytes += 2 * (session.id.length + session.label.length + session.track.length + session.season.length);
    bytes += 2 * (session.day.length + session.at.length + session.won.length);

    for (const place of session.top) {
      bytes += 64 + 2 * place.n.length;
    }
  }

  return bytes;
}

function clientAddress(request: IncomingMessage): string {
  if (process.env["FLY_APP_NAME"] !== undefined) {
    const fly = request.headers["fly-client-ip"];

    if (typeof fly === "string" && fly.length > 0) {
      return addressKey(fly);
    }
  }

  return addressKey(request.socket.remoteAddress ?? "unknown");
}

/**
 * The part of an address that names one subscriber.
 *
 * For IPv4 that is the whole address. For IPv6 it is the first 64 bits:
 * a home or a phone is handed a whole /64, and picks addresses inside it
 * freely - so a limit keyed on the full address would give one phone
 * eighteen quintillion fresh allowances. An IPv4 address written the IPv6
 * way (::ffff:192.0.2.1, which is how Node shows one on a dual-stack
 * socket) is the IPv4 address it wraps, or the same client would count
 * twice under two names.
 */
export function addressKey(raw: string): string {
  const address = raw.trim().toLowerCase().replace(/%.*$/, "");
  const mapped = /^::ffff:(\d{1,3}(?:\.\d{1,3}){3})$/.exec(address);

  if (mapped !== null) {
    return mapped[1] as string;
  }

  if (isIP(address) !== 6) {
    return address;
  }

  const [head = "", tail = ""] = address.split("::", 2);
  const left = head === "" ? [] : head.split(":");
  const right = address.includes("::") ? (tail === "" ? [] : tail.split(":")) : [];

  // An embedded IPv4 tail (::1.2.3.4) is two groups; it only ever sits in
  // the last 32 bits, so it never reaches the first four groups kept here.
  const rightGroups = right.reduce((n, part) => n + (part.includes(".") ? 2 : 1), 0);
  const groups = [...left, ...Array.from({ length: Math.max(0, 8 - left.length - rightGroups) }, () => "0"), ...right];

  return `${groups
    .slice(0, 4)
    .map((group) => group.replace(/^0+(?=.)/, ""))
    .join(":")}::/64`;
}

/**
 * "https://heatcaller-relay.fly.dev", for the absolute links a shared
 * link's preview needs. Empty if the Host header is anything but a plain
 * host name - it goes into a page, and a header is whatever the client
 * says it is.
 */
function originOf(request: IncomingMessage): string {
  const host = request.headers.host ?? "";

  if (!/^[A-Za-z0-9.-]+(:\d{1,5})?$/.test(host)) {
    return "";
  }

  const proto = request.headers["x-forwarded-proto"];
  const scheme = proto === "https" || proto === "http" ? proto : "http";

  return `${scheme}://${host}`;
}

function parse(request: IncomingMessage): Parsed | undefined {
  if (request.method === undefined || request.url === undefined) {
    return undefined;
  }

  let url: URL;

  try {
    url = new URL(request.url, "http://relay.invalid");
  } catch {
    return undefined;
  }

  let segments: string[];

  try {
    segments = url.pathname
      .split("/")
      .filter((part) => part.length > 0)
      .map((part) => decodeURIComponent(part));
  } catch {
    // "%E0%A4%A" and the like: not a path anybody meant, and a bad
    // request rather than the relay's own failure.
    return undefined;
  }

  // A path segment that decoded into a separator is either an attempt at
  // something or a client that is broken; either way it does not get to
  // reach a store.
  if (segments.some((part) => part.includes("/") || part.includes("\\") || part === ".." )) {
    return undefined;
  }

  return { method: request.method, segments, query: url.searchParams };
}

/** The publisher's counter. Absent means "I am not tracking order for this one". */
function readSeq(raw: string | null): number | undefined {
  if (raw === null) {
    return 0;
  }

  if (!/^\d{1,15}$/.test(raw)) {
    return undefined;
  }

  return Number(raw);
}

/**
 * How much of a refused body is read and thrown away before the connection
 * is simply cut. Nothing is kept, so this costs bandwidth, not memory; it
 * only has to cover what an honest client would plausibly have been
 * sending when it was refused (the largest body a config can allow is
 * 16 MiB), and DRAIN_MAX_MS still bounds a client that never stops.
 */
const DRAIN_MAX_BYTES = 20 * 1024 * 1024;
const DRAIN_MAX_MS = 2_000;

/**
 * The request's JSON, or why not.
 *
 * Too big is decided as early as it can be: from content-length before a
 * byte is read, and otherwise as the chunks arrive, so the relay never
 * holds more than the limit. Either way the answer is marked
 * "connection: close" and what is left of the body is drained rather than
 * the socket cut - a client still sending when the socket is cut sees a
 * reset instead of the 413 that says what went wrong.
 *
 * "too-big" is only reported once that draining is over, so the caller's
 * 413 goes out after the client has finished sending. Answering first and
 * draining after is not enough: the server closes a "connection: close"
 * socket as soon as the answer is written, and on Windows closing a socket
 * with unread data in it sends a reset that throws away the answer the
 * client had not yet read.
 */
/**
 * A request's body as bytes, up to <paramref name="limit"/> - for the one
 * route that takes something that is not JSON, a driver's clip (the
 * podium video, until 28 Oct 2026). Same shape as readBody: "too-big"
 * once past the limit (the rest discarded), "unreadable" for nothing at
 * all.
 */
function readRawBody(
  request: IncomingMessage,
  response: ServerResponse,
  limit: number,
): Promise<Buffer | "too-big" | "unreadable"> {
  return new Promise((resolve) => {
    let settled = false;
    const chunks: Buffer[] = [];
    let total = 0;

    const finish = (value: Buffer | "too-big" | "unreadable"): void => {
      if (!settled) {
        settled = true;
        resolve(value);
      }
    };

    const onData = (chunk: Buffer): void => {
      total += chunk.length;

      if (total > limit) {
        chunks.length = 0;
        request.off("data", onData);
        request.off("end", onEnd);
        request.off("close", onClose);
        void discardRest(request, response).then(() => finish("too-big"));
        return;
      }

      chunks.push(chunk);
    };

    const onEnd = (): void => {
      finish(total === 0 ? "unreadable" : Buffer.concat(chunks));
    };

    // Taken off while a too-big body drains, so the 413 is not turned into
    // "unreadable" by the close that follows the drain.
    const onClose = (): void => finish("unreadable");

    request.on("data", onData);
    request.on("end", onEnd);
    request.on("error", () => finish("unreadable"));
    request.on("aborted", () => finish("unreadable"));
    // WHY (22 Oct 2026, Bug Hunt 9): a stalled upload is now destroyed by
    // the relay itself (receiveClip's idle timeout), and a destroyed
    // request is not sure to say "aborted" - "close" always comes, and
    // after "end" it changes nothing because the answer is already settled.
    request.on("close", onClose);
  });
}

function readBody(
  request: IncomingMessage,
  response: ServerResponse,
  limit: number,
): Promise<unknown | "too-big" | "unreadable"> {
  return new Promise((resolve) => {
    let settled = false;

    const finish = (value: unknown | "too-big" | "unreadable"): void => {
      if (!settled) {
        settled = true;
        resolve(value);
      }
    };

    const tooBig = (): void => {
      request.off("data", onData);
      request.off("end", onEnd);
      void discardRest(request, response).then(() => finish("too-big"));
    };

    const chunks: Buffer[] = [];
    let total = 0;

    const onData = (chunk: Buffer): void => {
      total += chunk.length;

      if (total > limit) {
        chunks.length = 0;
        tooBig();
        return;
      }

      chunks.push(chunk);
    };

    const onEnd = (): void => {
      if (total === 0) {
        finish("unreadable");
        return;
      }

      try {
        finish(JSON.parse(Buffer.concat(chunks).toString("utf8")) as unknown);
      } catch {
        finish("unreadable");
      }
    };

    const declared = Number(request.headers["content-length"] ?? NaN);

    if (Number.isFinite(declared) && declared > limit) {
      tooBig();
      return;
    }

    request.on("data", onData);
    request.on("end", onEnd);

    // A client that gives up half way never sends the end. (While a
    // refused body is draining these are left to discardRest, which
    // settles as "too-big" however the draining ends.)
    request.on("error", () => {
      if (total <= limit) {
        finish("unreadable");
      }
    });
    request.on("close", () => {
      if (total <= limit) {
        finish("unreadable");
      }
    });
  });
}

/**
 * Throws away the rest of a body that was refused, for a little while.
 *
 * The answer goes out first and says "connection: close", which is the
 * client's cue to stop. Reading on rather than cutting the socket gives
 * it the chance to see that answer: a socket closed while the other end
 * is still writing is reset, and a reset can take the unread answer with
 * it. Bounded in bytes and in time, so a client that never stops is cut
 * off after all.
 *
 * Settles when the draining is over - the body ended, the client went
 * away, or it was cut off - and never rejects.
 */
function discardRest(request: IncomingMessage, response: ServerResponse): Promise<void> {
  if (!response.headersSent) {
    response.setHeader("connection", "close");
  }

  return new Promise<void>((resolve) => {
    let drained = 0;
    let done = false;

    const settle = (): void => {
      if (!done) {
        done = true;
        clearTimeout(timer);
        resolve();
      }
    };

    const cut = (): void => {
      settle();
      request.destroy();
    };

    const timer = setTimeout(cut, DRAIN_MAX_MS);
    timer.unref();

    request.on("data", (chunk: Buffer) => {
      drained += chunk.length;

      if (drained > DRAIN_MAX_BYTES) {
        cut();
      }
    });

    request.on("end", settle);
    request.on("close", settle);
    request.on("error", settle);

    if (request.readableEnded) {
      settle();
      return;
    }

    request.resume();
  });
}

/**
 * Sent with every answer.
 *
 * nosniff so a document is only ever what its content type says; no
 * framing, so nobody can lay the check-in page under a decoy and steal a
 * tap; HSTS, so a phone that has been here once never again tries plain
 * http, where the address could be answered by anyone on the same Wi-Fi;
 * and no camera, microphone or location for anything on the page.
 */
const ALLOWED_METHODS = "GET, HEAD, POST, DELETE, OPTIONS";

const BASE_HEADERS: Readonly<Record<string, string>> = Object.freeze({
  "x-content-type-options": "nosniff",
  "x-frame-options": "SAMEORIGIN",
  "referrer-policy": "strict-origin-when-cross-origin",
  "strict-transport-security": "max-age=31536000",
  "permissions-policy": "camera=(), microphone=(), geolocation=(), payment=()",
  "cross-origin-opener-policy": "same-origin",
});

/**
 * Nothing here answers this. A method no route in the relay takes at all -
 * PUT, PATCH and the like - is a 405 naming the ones it does, as HTTP asks,
 * rather than a 404 that says the address is wrong when it is the method.
 */
function noRoute(response: ServerResponse, method: string): void {
  if (method !== "GET" && method !== "POST" && method !== "DELETE") {
    response.setHeader("allow", ALLOWED_METHODS);
    sendJson(response, 405, { ok: false, error: `This relay does not take ${method} requests.` });
    return;
  }

  sendJson(response, 404, { ok: false, error: "No such endpoint." });
}

/**
 * The voice id as the usage log may keep it: the catalogue id the line was
 * spoken with, or "unknown". It is the one thing in the log that comes
 * from the request rather than the relay, so nothing a laptop typed is
 * kept unless it named a voice the relay actually has.
 */
function loggedVoice(voiceId: string, result: { readonly ok: boolean; readonly refusal?: string }): string {
  const refusedAsUnknown = !result.ok && result.refusal === "unknown-voice";
  return refusedAsUnknown || !/^[A-Za-z0-9_-]{1,64}$/.test(voiceId) ? "unknown" : voiceId;
}

function sendJson(response: ServerResponse, status: number, body: unknown): void {
  const text = JSON.stringify(body);

  response.writeHead(status, {
    ...BASE_HEADERS,
    "content-type": "application/json; charset=utf-8",
    "content-length": Buffer.byteLength(text),
    "cache-control": "no-store",
  });

  response.end(text);
}

/**
 * A page. no-store because a results page that a phone caches is a
 * results page showing last week's heat, and the document it draws is
 * fetched separately anyway.
 *
 * The content policy is the backstop behind the escaping: the one script
 * that runs is the page's own, named by its nonce, so even a name that
 * somehow got past esc() as markup could not run anything. Data comes
 * only from this relay; images only from here or as data: pictures the
 * laptop sent; nothing may frame the page or be submitted elsewhere.
 * Styles stay inline-able - the pages are built that way, and a style
 * cannot run code.
 */
function sendHtml(response: ServerResponse, status: number, html: string, nonce: string): void {
  const policy = [
    "default-src 'none'",
    `script-src 'nonce-${nonce}'`,
    "style-src 'unsafe-inline'",
    "img-src 'self' data:",

    // MEDIA, for listening along from home - see CHANNELS.listen.
    //
    // Without it, audio falls back to default-src 'none' and the browser
    // blocks every note of it. That is the whole of this line's reason,
    // and the `data:` in it needs its own:
    //
    // The lines arrive as base64 inside a club's own document, and THE
    // RELAY MUST NOT DECODE THEM. Serving the audio from a route of its
    // own would be tidier and would need only 'self' here - but it would
    // mean the relay reaching inside a payload to find the bytes, and not
    // doing that is the rule types.ts opens with and the thing that keeps
    // a column added in the desktop app from being a relay deploy. So the
    // page decodes it and plays a data: URI, and this line is the cost of
    // that choice rather than an oversight.
    //
    // On every page rather than only the one that plays: the policy is one
    // string for the whole site, two would be two things to keep right,
    // and the widening is small - a page with a nonce on its only script
    // and every value escaped has no way to put an <audio> anywhere.
    "media-src 'self' data:",
    "connect-src 'self'",
    "manifest-src 'self'",
    // PUSH ALERTS (6 Nov 2026) - see push.ts. The club's service worker,
    // /c/<club>/sw.js, from this relay and nowhere else. Without it a
    // worker falls back to script-src, whose nonce no worker can carry.
    "worker-src 'self'",
    "base-uri 'none'",
    "form-action 'self'",
    // FRAMED BY ITS OWN PAGES ONLY (1 Nov 2026). The home-screen icon's
    // one bar shows the club's pages in a frame of its own (appShellPage),
    // so a page here may be framed by another page here and by nothing
    // else, and may frame only pages here.
    "frame-src 'self'",
    "frame-ancestors 'self'",
  ].join("; ");

  response.writeHead(status, {
    ...BASE_HEADERS,
    "content-security-policy": policy,
    "content-type": "text/html; charset=utf-8",
    "content-length": Buffer.byteLength(html),
    "cache-control": "no-store",
  });

  response.end(html);
}

/** Bytes of a known type - the manifest, the icon, the logo. Cached for an hour: they change when a club re-brands, not between visits. */
function sendBody(response: ServerResponse, status: number, type: string, body: string | Buffer, cache = "public, max-age=3600"): void {
  response.writeHead(status, {
    ...BASE_HEADERS,
    "content-type": type,
    "content-length": Buffer.byteLength(body),
    "cache-control": cache,
  });

  response.end(body);
}

/**
 * The same, plus the header that lets an OBS browser source or somebody
 * else's page read it. Only ever used for documents that are already
 * public to anyone with the address.
 */
function sendPublicJson(response: ServerResponse, status: number, body: unknown): void {
  sendPublicText(response, status, JSON.stringify(body));
}

/**
 * The same, for JSON already serialised. A Buffer - a collection listing -
 * goes out as it is, so a listing sent to many phones at once is one copy
 * in memory rather than one per phone.
 */
function sendPublicText(response: ServerResponse, status: number, text: string): void {
  sendPublicBytes(response, status, Buffer.from(text, "utf8"));
}

/**
 * One rendered line, for the laptop that asked for it.
 *
 * Never cached by anything in between: the audio is the club's, the
 * headers say what it cost, and a proxy holding a copy would be holding a
 * driver's name. Not public either - no access-control header - because
 * only the laptop's own token gets this far.
 */
function sendAudio(response: ServerResponse, bytes: Buffer, headers: Readonly<Record<string, string>>): void {
  response.writeHead(200, {
    ...BASE_HEADERS,
    ...headers,
    "content-type": "audio/mpeg",
    "content-length": bytes.length,
    "cache-control": "no-store",
  });

  response.end(bytes);
}

/**
 * One voice's sample, for anyone at all.
 *
 * Cached hard, which sendAudio above deliberately is not: a sample is one
 * fixed neutral line with nobody's name in it, and it is the same audio
 * for that voice for as long as the voice's settings do not change - so a
 * browser or a proxy holding a copy is exactly what should happen. Every
 * copy held somewhere else is a request this relay does not serve and a
 * sample a small machine does not read off its volume. Public, so the
 * HeatCaller website can play these from its own pages.
 *
 * The trade-off is named rather than hidden: the address carries no
 * revision (the app-side contract fixes it as /voice/sample/<voiceId>),
 * so changing a voice's settings does not reach a listener who already
 * has a copy until theirs expires. The relay itself renders the new one
 * at once - the file it keeps is named by the revision - and no `immutable`
 * is sent, so a reload picks it up.
 */
function sendSampleAudio(response: ServerResponse, bytes: Buffer, headers: Readonly<Record<string, string>>): void {
  response.writeHead(200, {
    ...BASE_HEADERS,
    ...headers,
    "content-type": "audio/mpeg",
    "content-length": bytes.length,
    "cache-control": "public, max-age=31536000",
    "access-control-allow-origin": "*",
  });

  response.end(bytes);
}

/** The first `limit` items of a meta listing (already newest first), as a meta listing. */
function newestOfMeta(listing: Buffer, limit: number): Buffer {
  const parsed = JSON.parse(listing.toString("utf8")) as { items: unknown[] };

  if (parsed.items.length <= limit) {
    return listing;
  }

  return Buffer.from(JSON.stringify({ items: parsed.items.slice(0, limit), meta: true }), "utf8");
}

function sendPublicBytes(response: ServerResponse, status: number, bytes: Buffer): void {
  response.writeHead(status, {
    ...BASE_HEADERS,
    "content-type": "application/json; charset=utf-8",
    "content-length": bytes.length,
    "cache-control": "no-store",
    "access-control-allow-origin": "*",
  });

  response.end(bytes);
}
