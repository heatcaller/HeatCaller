import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { icsFold, icsText, nextNightIcs } from "../src/ics.js";
import { lookFrom, timeZoneFrom } from "../src/site.js";
import { tzAttr } from "../src/clock.js";
import { homePage } from "../src/pages.js";
import { ACME_TOKEN, call, start } from "./helpers.js";

/**
 * DRIVER PHONE PLAN, STEP F (14 Nov 2026): the club's time zone (item 15)
 * and the next race night in a phone's calendar (item 10).
 */

const NOW = new Date("2026-11-14T12:00:00.000Z");
const NIGHT = {
  day: "2026-11-26", title: "Corn Dog Day", when: "Thu 26 Nov", venue: "Upstate Hobbies, Main St; back room",
  classes: ["50%", "Box Stock", "Mod"], firstRace: "19:00", entriesClose: "18:30", off: false, offText: "",
  until: "2026-11-27T06:00:00.000Z",
};

describe("the club's time zone (item 15)", () => {
  it("takes a real IANA zone and nothing else", () => {
    assert.equal(timeZoneFrom("America/New_York"), "America/New_York");
    assert.equal(timeZoneFrom("Eastern Standard Time"), undefined);
    assert.equal(timeZoneFrom("Mars/Olympus_Mons"), undefined);
    assert.equal(timeZoneFrom("<script>"), undefined);
    assert.equal(timeZoneFrom(7), undefined);
  });

  it("puts it on every page's <html>, and leaves it off when the laptop didn't say", () => {
    assert.equal(tzAttr("America/New_York"), ' data-tz="America/New_York"');
    assert.equal(tzAttr(undefined), "");
    const look = lookFrom({ seq: 1, publishedAt: "2026-11-14T00:00:00Z", body: { clock24: false, timeZone: "America/New_York" } }, undefined);
    assert.equal(look.timeZone, "America/New_York");
    assert.match(homePage("acme-rc", "Acme RC", { look, origin: "", nonce: "" }), /<html lang="en"[^>]* data-tz="America\/New_York">/);
  });

  it("gives every date and time the club's zone unless a page asked for one", () => {
    const html = homePage("acme-rc", "Acme RC");
    assert.match(html, /if \(!o\.timeZone\) o\.timeZone = HC_TZ;/);
    assert.match(html, /function hcZoneWord\(d\)/);
  });
});

describe("the next race night in a phone's calendar (item 10)", () => {
  it("is one event at the first race, in the club's zone, with a reminder", () => {
    const ics = nextNightIcs("acme-rc", "Acme RC", NIGHT, "America/New_York", NOW)!;
    assert.match(ics, /^BEGIN:VCALENDAR\r\nVERSION:2\.0\r\n/);
    assert.match(ics, /\r\nDTSTART;TZID=America\/New_York:20261126T190000\r\n/);
    assert.match(ics, /\r\nDURATION:PT4H\r\n/);
    assert.match(ics, /\r\nSUMMARY:Corn Dog Day at Acme RC\r\n/);
    assert.match(ics, /\r\nLOCATION:Upstate Hobbies\\, Main St\\; back room\r\n/);
    assert.match(ics, /\r\nSTATUS:CONFIRMED\r\n/);
    assert.match(ics, /\r\nTRIGGER:-PT2H\r\n/);
    assert.match(ics, /\r\nEND:VCALENDAR\r\n$/);
  });

  it("is all day with no first race, floating with no zone, and struck out when called off", () => {
    assert.match(nextNightIcs("acme-rc", "Acme RC", { ...NIGHT, firstRace: null }, "America/New_York", NOW)!, /DTSTART;VALUE=DATE:20261126/);
    assert.match(nextNightIcs("acme-rc", "Acme RC", NIGHT, undefined, NOW)!, /\r\nDTSTART:20261126T190000\r\n/);
    const off = nextNightIcs("acme-rc", "Acme RC", { ...NIGHT, off: true, offText: "Rained out" }, "America/New_York", NOW)!;
    assert.match(off, /SUMMARY:Called off: Corn Dog Day at Acme RC/);
    assert.match(off, /STATUS:CANCELLED/);
    assert.doesNotMatch(off, /VALARM/);
  });

  it("offers nothing for a night that is past or withdrawn", () => {
    assert.equal(nextNightIcs("acme-rc", "Acme RC", { ...NIGHT, until: "2026-11-01T00:00:00Z" }, undefined, NOW), undefined);
    assert.equal(nextNightIcs("acme-rc", "Acme RC", { day: "", until: "1970-01-01T00:00:00Z" }, undefined, NOW), undefined);
    assert.equal(nextNightIcs("acme-rc", "Acme RC", null, undefined, NOW), undefined);
  });

  it("escapes text and folds long lines", () => {
    assert.equal(icsText("a,b;c\\d\ne"), "a\\,b\\;c\\\\d\\ne");
    const folded = icsFold("DESCRIPTION:" + "x".repeat(100));
    assert.ok(folded.split("\r\n").every((l) => Buffer.byteLength(l) <= 75));
    assert.equal(folded.replace(/\r\n /g, ""), "DESCRIPTION:" + "x".repeat(100));
  });

  it("is served at /c/<club>/nextnight.ics, and 404 with no night", async () => {
    const h = await start(undefined, undefined, () => NOW);
    try {
      assert.equal((await call(h, "GET", "/c/acme-rc/nextnight.ics")).status, 404);
      await call(h, "POST", "/api/v1/c/acme-rc/publish/nextnight?seq=1", { token: ACME_TOKEN, json: NIGHT });
      const got = await call(h, "GET", "/c/acme-rc/nextnight.ics");
      assert.equal(got.status, 200);
      assert.match(got.headers["content-type"] ?? "", /text\/calendar/);
      assert.match(got.text, /SUMMARY:Corn Dog Day at Acme RC/);
    } finally {
      await h.close();
    }
  });
});
