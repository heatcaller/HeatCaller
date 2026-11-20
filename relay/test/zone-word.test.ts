import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { nextNightWords } from "../src/directory.js";
import { homePage } from "../src/pages.js";

/**
 * THE CLUB'S ZONE NAMED (20 Nov 2026, look-back on the time-zone picker).
 * Checked at phone size: with the club in Auckland and the phone in New York,
 * Home's "Updated" stamp and the next night's times were in Auckland's clock
 * but said nothing about it. The phone pages now add the zone ("GMT+13")
 * whenever the phone is somewhere else (hcZoneWord), and /clubs - read from
 * anywhere - always names it when the laptop sent one.
 */
describe("the club's zone is named next to its times", () => {
  it("on Home's Updated stamp and the next night's times", () => {
    const html = homePage("acme-rc", "Acme RC");
    assert.match(html, /stamp\.textContent = 'Updated ' \+ [^\n]*hcZoneWord\(at\)/);
    assert.match(html, /\.test\(word\(b\.day\)\)[^\n]*\n[^\n]*hcZoneWord\(new Date\(word\(b\.day\) \+ 'T12:00:00Z'\)\)/);
    // The day check must survive the template string it lives in.
    assert.match(html, /\/\^\[0-9\]\{4\}-\[0-9\]\{2\}-\[0-9\]\{2\}\$\//);
  });

  it("on /clubs, for the zone on the night's own day", () => {
    const night = { day: "2026-11-26", firstRace: "19:00", title: "Corn Dog Day", until: "2026-11-27T06:00:00Z" };
    assert.equal(nextNightWords(night, false, "America/New_York")?.words, "Thu 26 Nov from 7:00pm EST · Corn Dog Day");
    assert.equal(nextNightWords(night, true, "Pacific/Auckland")?.words, "Thu 26 Nov from 19:00 GMT+13 · Corn Dog Day");
    // Summer time on the night itself, not on the day the page is read.
    assert.equal(nextNightWords({ ...night, day: "2026-07-09", until: "2026-07-10T06:00:00Z" }, false, "America/New_York")?.words,
      "Thu 9 Jul from 7:00pm EDT · Corn Dog Day");
  });

  it("says no zone with none sent, an unknown one, or no time", () => {
    const night = { day: "2026-11-26", firstRace: "19:00", until: "2026-11-27T06:00:00Z" };
    assert.equal(nextNightWords(night, false)?.words, "Thu 26 Nov from 7:00pm");
    assert.equal(nextNightWords(night, false, "Mars/Olympus_Mons")?.words, "Thu 26 Nov from 7:00pm");
    assert.equal(nextNightWords({ ...night, firstRace: "" }, false, "America/New_York")?.words, "Thu 26 Nov");
  });
});
