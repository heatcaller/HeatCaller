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

const DAY = /^(\d{4})-(\d{2})-(\d{2})$/;
const CLOCK = /^(\d{1,2}):(\d{2})$/;

/**
 * The next race night as a calendar file, or undefined when there is no
 * night to offer (none published, withdrawn, or past).
 */
export function nextNightIcs(club: string, clubName: string, body: unknown, timeZone: string | undefined, now: Date): string | undefined {
  if (typeof body !== "object" || body === null) return undefined;
  const b = body as Record<string, unknown>;
  const day = typeof b["day"] === "string" ? DAY.exec(b["day"]) : null;
  const until = typeof b["until"] === "string" ? Date.parse(b["until"]) : NaN;

  if (day === null || !Number.isFinite(until) || now.getTime() > until) return undefined;

  const word = (v: unknown) => (typeof v === "string" ? v.trim() : "");
  const ymd = `${day[1]}${day[2]}${day[3]}`;
  const clock = CLOCK.exec(word(b["firstRace"]));
  const title = word(b["title"]);
  const venue = word(b["venue"]);
  const classes = (Array.isArray(b["classes"]) ? b["classes"] : []).map(word).filter((c) => c !== "");
  const off = b["off"] === true;
  const stamp = now.toISOString().replace(/[-:]/g, "").replace(/\.\d{3}/, "");

  const lines = [
    "BEGIN:VCALENDAR",
    "VERSION:2.0",
    "PRODID:-//HeatCaller//Race night//EN",
    "CALSCALE:GREGORIAN",
    "METHOD:PUBLISH",
    "BEGIN:VEVENT",
    `UID:${ymd}-${club}@heatcaller`,
    `DTSTAMP:${stamp}`,
  ];

  if (clock !== null) {
    const hh = clock[1]!.padStart(2, "0");
    const start = `${ymd}T${hh}${clock[2]}00`;
    // In the club's zone when the laptop said it; else a floating time,
    // which a calendar reads in the phone's own zone.
    lines.push(timeZone ? `DTSTART;TZID=${timeZone}:${start}` : `DTSTART:${start}`);
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

  lines.push("END:VEVENT", "END:VCALENDAR");
  return lines.map(icsFold).join("\r\n") + "\r\n";
}
