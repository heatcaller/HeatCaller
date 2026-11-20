/**
 * ADD THE RACE NIGHT TO THE CALENDAR (14 Nov 2026, driver phone plan item
 * 10). Home's next race night card had "Coming? Put yourself down" but
 * nothing to put it in a phone's calendar. /c/<club>/nextnight.ics is one
 * event, made from the same nextnight document the card is drawn from: the
 * club's name and venue, the day, the first race's time in the club's own
 * zone (all day when the club gave no time), and a reminder two hours
 * before. A night called off is the same event marked cancelled, so a
 * calendar that already has it shows it struck out.
 *
 * Plain RFC 5545, made by hand: a handful of lines, text escaped, lines
 * folded at 75 octets, CRLF. Nothing about any driver is in it.
 */

/** Escapes a TEXT value: backslash, semicolon, comma and newlines. */
export function icsText(value: string): string {
  return value.replace(/\\/g, "\\\\").replace(/;/g, "\\;").replace(/,/g, "\\,").replace(/\r?\n/g, "\\n");
}

/** Folds one content line at 75 octets, as the standard asks. */
export function icsFold(line: string): string {
  const bytes = Buffer.from(line, "utf8");
  if (bytes.length <= 75) return line;
  const parts: string[] = [];
  let start = 0;

  while (start < bytes.length) {
    let end = Math.min(start + (parts.length === 0 ? 75 : 74), bytes.length);
    // Never split a UTF-8 character.
    while (end < bytes.length && (bytes[end]! & 0xc0) === 0x80) end--;
    parts.push(bytes.subarray(start, end).toString("utf8"));
    start = end;
  }

  return parts.join("\r\n ");
}

/** How far the zone's wall clock is ahead of UTC at one instant, in ms. */
function zoneOffset(at: number, timeZone: string): number {
  const parts = new Intl.DateTimeFormat("en-US", {
    timeZone, hourCycle: "h23", year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit",
  }).formatToParts(new Date(at));
  const n = (type: string) => Number(parts.find((p) => p.type === type)?.value);
  return Date.UTC(n("year"), n("month") - 1, n("day"), n("hour"), n("minute"), n("second")) - Math.floor(at / 1000) * 1000;
}

/** A wall-clock time in a zone as the UTC instant, or undefined for a zone Intl doesn't know. */
export function zonedToUtc(year: number, month: number, dayOfMonth: number, hour: number, minute: number, timeZone: string): Date | undefined {
  try {
    const wall = Date.UTC(year, month - 1, dayOfMonth, hour, minute);
    // Twice, so a night across a clock change gets the offset on its own side.
    let utc = wall - zoneOffset(wall, timeZone);
    utc = wall - zoneOffset(utc, timeZone);
    return Number.isFinite(utc) ? new Date(utc) : undefined;
  } catch {
    return undefined;
  }
}

const DAY = /^(\d{4})-(\d{2})-(\d{2})$/;
const CLOCK = /^(\d{1,2}):(\d{2})$/;


/** A UTC instant as an iCalendar date-time: 20261118T230000Z. */
function icsUtc(at: Date): string {
  return at.toISOString().replace(/[-:]/g, "").replace(/\.\d{3}/, "");
}

/**
 * One race night as a VEVENT's lines, from a nextnight document's fields,
 * or undefined when it has no day. Shared by nextnight.ics and nights.ics,
 * with the same UID, so a night added from one and then seen in the other is
 * the same event in the calendar, not two.
 */
function nightEvent(club: string, clubName: string, b: Record<string, unknown>, timeZone: string | undefined, stamp: string): string[] | undefined {
  const day = typeof b["day"] === "string" ? DAY.exec(b["day"]) : null;
  if (day === null) return undefined;

  const word = (v: unknown) => (typeof v === "string" ? v.trim() : "");
  const ymd = `${day[1]}${day[2]}${day[3]}`;
  const clock = CLOCK.exec(word(b["firstRace"]));
  const title = word(b["title"]);
  const venue = word(b["venue"]);
  const classes = (Array.isArray(b["classes"]) ? b["classes"] : []).map(word).filter((c) => c !== "");
  const off = b["off"] === true;

  const lines = [
    "BEGIN:VEVENT",
    `UID:${ymd}-${club}@heatcaller`,
    `DTSTAMP:${stamp}`,
  ];

  if (clock !== null) {
    const hh = clock[1]!.padStart(2, "0");
    const start = `${ymd}T${hh}${clock[2]}00`;
    // In the club's zone when the laptop said it, written as UTC: a TZID
    // needs a VTIMEZONE block (RFC 5545 3.2.19), which Outlook insists on.
    // Else a floating time, which a calendar reads in the phone's own zone.
    const utc = timeZone ? zonedToUtc(Number(day[1]), Number(day[2]), Number(day[3]), Number(hh), Number(clock[2]), timeZone) : undefined;
    lines.push(utc !== undefined ? `DTSTART:${icsUtc(utc)}` : `DTSTART:${start}`);
    lines.push("DURATION:PT4H");
  } else {
    lines.push(`DTSTART;VALUE=DATE:${ymd}`);
  }

  lines.push(`SUMMARY:${icsText((off ? "Called off: " : "") + (title !== "" ? `${title} at ${clubName}` : `Race night at ${clubName}`))}`);
  if (venue !== "") lines.push(`LOCATION:${icsText(venue)}`);
  const said = [classes.length > 0 ? `Classes: ${classes.join(", ")}` : "", off ? word(b["offText"]) : ""].filter((s) => s !== "").join("\n");
  if (said !== "") lines.push(`DESCRIPTION:${icsText(said)}`);
  lines.push(`STATUS:${off ? "CANCELLED" : "CONFIRMED"}`);

  if (!off) {
    lines.push("BEGIN:VALARM", "ACTION:DISPLAY", `DESCRIPTION:${icsText(`Race night at ${clubName}`)}`, "TRIGGER:-PT2H", "END:VALARM");
  }

  lines.push("END:VEVENT");
  return lines;
}

const CALENDAR_HEAD = ["BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//HeatCaller//Race night//EN", "CALSCALE:GREGORIAN", "METHOD:PUBLISH"];

/**
 * The next race night as a calendar file, or undefined when there is no
 * night to offer (none published, withdrawn, or past).
 */
export function nextNightIcs(club: string, clubName: string, body: unknown, timeZone: string | undefined, now: Date): string | undefined {
  if (typeof body !== "object" || body === null) return undefined;
  const b = body as Record<string, unknown>;
  const until = typeof b["until"] === "string" ? Date.parse(b["until"]) : NaN;

  if (!Number.isFinite(until) || now.getTime() > until) return undefined;

  const event = nightEvent(club, clubName, b, timeZone, icsUtc(now));
  if (event === undefined) return undefined;

  return [...CALENDAR_HEAD, ...event, "END:VCALENDAR"].map(icsFold).join("\r\n") + "\r\n";
}

// ------------------------------------------------------------ every night

/**
 * EVERY NIGHT, AS A FEED (19 Nov 2026, driver phone plan item 10 step 3).
 * The laptop only ever announces the next night, so the relay keeps its own
 * short list of the nights it has been told about (the "nightsfeed"
 * snapshot, never served as it is): each announcement added or put in place
 * of the one for its day, a night called off kept and marked so, and an
 * announced night the club then moved - a future night that is no longer
 * the next one - taken out. Nights more than 60 days gone are let go, and
 * at most 40 kept. /c/<club>/nights.ics serves them as one calendar a phone
 * subscribes to once.
 *
 * Only what the public Home card already shows: day, title, venue, first
 * race, classes and whether it's off. Nothing about any driver.
 */
export const NIGHTS_FEED = "nightsfeed";

const FEED_KEEP_DAYS = 60;
const FEED_MOST = 40;

type FeedNight = Record<string, unknown>;

/** The fields of a nextnight document the feed keeps, or undefined for one with no day. */
function feedNightFrom(body: unknown): FeedNight | undefined {
  if (typeof body !== "object" || body === null) return undefined;
  const b = body as Record<string, unknown>;
  if (typeof b["day"] !== "string" || !DAY.test(b["day"])) return undefined;
  const text = (v: unknown, most: number) => (typeof v === "string" ? v.trim().slice(0, most) : "");

  return {
    day: b["day"],
    title: text(b["title"], 120),
    venue: text(b["venue"], 200),
    firstRace: typeof b["firstRace"] === "string" && CLOCK.test(b["firstRace"]) ? b["firstRace"] : "",
    classes: (Array.isArray(b["classes"]) ? b["classes"] : []).map((c) => text(c, 60)).filter((c) => c !== "").slice(0, 30),
    off: b["off"] === true,
    offText: text(b["offText"], 300),
    until: typeof b["until"] === "string" && Number.isFinite(Date.parse(b["until"])) ? b["until"] : "",
  };
}

/** The nights the feed held, read back; anything not a night is left out. */
function feedNights(was: unknown): FeedNight[] {
  const list = typeof was === "object" && was !== null ? (was as Record<string, unknown>)["nights"] : undefined;
  return (Array.isArray(list) ? list : []).map(feedNightFrom).filter((n): n is FeedNight => n !== undefined);
}

/** The feed with a newly published nextnight document taken in (see NIGHTS_FEED). */
export function nightsFeedWith(was: unknown, next: unknown, now: Date): { nights: FeedNight[] } {
  const night = feedNightFrom(next);
  const nowMs = now.getTime();
  const oldest = new Date(nowMs - FEED_KEEP_DAYS * 24 * 3600 * 1000).toISOString().slice(0, 10);
  const over = (n: FeedNight) => typeof n["until"] === "string" && n["until"] !== "" && Date.parse(n["until"] as string) < nowMs;

  let nights = feedNights(was)
    .filter((n) => (n["day"] as string) >= oldest)
    // A night still to come that isn't the one just announced was moved
    // (or taken down before it happened): it isn't on any more.
    .filter((n) => over(n) || (night !== undefined && n["day"] === night["day"]) || n["off"] === true)
    .filter((n) => night === undefined || n["day"] !== night["day"]);

  if (night !== undefined) nights.push(night);
  nights.sort((a, b) => ((a["day"] as string) < (b["day"] as string) ? -1 : 1));
  if (nights.length > FEED_MOST) nights = nights.slice(-FEED_MOST);
  return { nights };
}

/** Every night the feed holds as one calendar to subscribe to. Always a calendar, if an empty one. */
export function nightsIcs(club: string, clubName: string, feed: unknown, timeZone: string | undefined, now: Date): string {
  const stamp = icsUtc(now);
  const lines = [
    ...CALENDAR_HEAD,
    `X-WR-CALNAME:${icsText(`${clubName} race nights`)}`,
    // How often a subscribed calendar looks again: twice a day is plenty
    // for a club announcing a week ahead, and a called-off night still
    // reaches it the same day.
    "REFRESH-INTERVAL;VALUE=DURATION:PT6H",
    "X-PUBLISHED-TTL:PT6H",
  ];

  for (const night of feedNights(feed)) {
    const event = nightEvent(club, clubName, night, timeZone, stamp);
    if (event !== undefined) lines.push(...event);
  }

  lines.push("END:VCALENDAR");
  return lines.map(icsFold).join("\r\n") + "\r\n";
}