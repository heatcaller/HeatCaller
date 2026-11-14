/**
 * HOW DRIVERS USE THE PAGES (14 Nov 2026, driver phone plan item 20).
 *
 * A few anonymous counts per club per day, so a fix can be judged by a
 * race night rather than guessed at: how often a phone lost its signal and
 * got it back, how often Live had lost touch with the track, how often the
 * install card was shown and taken up, which alerts were opened, and so on.
 *
 * NOTHING ABOUT WHO. A count is a number against a fixed event name and a
 * day: no badge, no driver, no phone id, no page address, no IP kept. Only
 * names on the list below are counted, so the route can't be used to write
 * anything else into a club's folder. Kept 90 days, then dropped.
 *
 * The pages send at most one of each event per page load, together, once a
 * minute and when the page is hidden (hcCount in pages.ts). One address may
 * send a club 120 reports in five minutes - a flood brake, not a per-phone
 * limit, since a room of phones shares one address.
 */

/** The snapshot channel the counts are kept in, never served to the public. */
export const PAGE_COUNTS = "pagecounts";

/** How long the daily counts are kept. */
export const PAGE_COUNTS_DAYS = 90;

/** The most events one report may carry. */
export const PAGE_COUNTS_MAX_EVENTS = 20;

/** A report is a short list of event names. */
export const PAGE_COUNTS_MAX_BODY = 2048;

/** Reports one address may send a club in five minutes. */
export const PAGE_COUNTS_PER_ADDRESS = 120;

/** Every event a page may count. Add here before a page counts anything new. */
export const PAGE_COUNT_EVENTS: readonly string[] = [
  // Item 1: no signal.
  "offline_shown",
  "offline_recovered",
  // Item 3: Live had lost touch with the track.
  "stale_live_shown",
  // Item 2: check-in as its own step.
  "checkin_tapped",
  // Item 7: the install card.
  "install_card_shown",
  "install_accepted",
  "install_dismissed",
  // Item 6: reading.
  "zoom_fit_used",
  "contrast_high",
  "text_size_115",
  "text_size_130",
  // Item 17: a phone that forgot its driver.
  "storage_forgot",
  // Item 18: the back button.
  "back_used",
  // Item 19: Listen Live.
  "listen_late_dropped",
  // Item 16: in-app browsers, by app.
  "inapp_card_shown_facebook",
  "inapp_card_shown_instagram",
  "inapp_card_shown_other",
  // Item 9: alerts opened, by kind.
  "alert_opened_race",
  "alert_opened_soon",
  "alert_opened_marshal",
  "alert_opened_result",
  "alert_opened_nextnight",
  "alert_opened_video",
];

const KNOWN = new Set(PAGE_COUNT_EVENTS);

/** The events in a report that may be counted: known names, each once, at most PAGE_COUNTS_MAX_EVENTS. */
export function countableEvents(body: unknown): string[] {
  if (typeof body !== "object" || body === null || !Array.isArray((body as Record<string, unknown>)["events"])) {
    return [];
  }

  const events = (body as Record<string, unknown>)["events"] as unknown[];
  return [...new Set(events.filter((e): e is string => typeof e === "string" && KNOWN.has(e)))].slice(0, PAGE_COUNTS_MAX_EVENTS);
}

/** Counts kept by day ("2026-11-14") then event. */
export type DayCounts = Record<string, Record<string, number>>;

/**
 * The counts with this report added on `day`, days older than the keep
 * dropped, and anything stored that isn't a known event or a count left out.
 */
export function addCounts(was: unknown, day: string, events: readonly string[], oldest: string): DayCounts {
  const days: DayCounts = {};

  if (typeof was === "object" && was !== null) {
    for (const [key, slot] of Object.entries(was as Record<string, unknown>)) {
      if (!/^\d{4}-\d{2}-\d{2}$/.test(key) || key < oldest || typeof slot !== "object" || slot === null) continue;
      const kept: Record<string, number> = {};

      for (const [event, n] of Object.entries(slot as Record<string, unknown>)) {
        if (KNOWN.has(event) && typeof n === "number" && Number.isFinite(n) && n > 0) {
          kept[event] = Math.floor(n);
        }
      }

      days[key] = kept;
    }
  }

  const today = days[day] ?? {};

  for (const event of events) {
    today[event] = (today[event] ?? 0) + 1;
  }

  days[day] = today;
  return days;
}

/** Totals over the last `n` days up to and including `day`, for Manage. */
export function totalsOver(days: DayCounts, day: string, n: number): Record<string, number> {
  const from = new Date(`${day}T00:00:00Z`);
  from.setUTCDate(from.getUTCDate() - (n - 1));
  const first = from.toISOString().slice(0, 10);
  const totals: Record<string, number> = {};

  for (const [key, slot] of Object.entries(days)) {
    if (key < first || key > day) continue;
    for (const [event, count] of Object.entries(slot)) {
      totals[event] = (totals[event] ?? 0) + count;
    }
  }

  return totals;
}
