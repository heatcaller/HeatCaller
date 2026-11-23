/**
 * The club's 12/24-hour choice on its phone pages (12 Nov 2026): the laptop's
 * 24-hour tick on Themes rides on the site document as `clock24`, the shell
 * writes it on <html> as data-clock="24" or "12", and every time a page
 * draws reads it from there. With no choice published - an older laptop -
 * HC_H12 is undefined and the phone's own locale decides, as before.
 *
 * hcHm turns the laptop's "HH:mm" (a schedule time, entries close, first
 * race) into the club's choice; anything that is not exactly that is
 * shown as it came.
 */
export const CLOCK_JS = `
var HC_H12 = (function () { try { return ({ '24': false, '12': true })[document.documentElement.getAttribute('data-clock') || '']; } catch (e) { return undefined; } })();
// THE HOUR ON A 12-HOUR CLOCK (22 Nov 2026, the owner, from the Voice of the
// Night video): times were drawn with hour: "2-digit", which on a 12-hour
// clock reads "06:08 PM" - nobody writes a 12-hour time that way. A 12-hour
// clock now gets "6:08 PM"; a 24-hour one keeps "18:08" and "06:08".
var HC_HOUR = HC_H12 === false ? "2-digit" : HC_H12 === true ? "numeric" : (function () { try { return new Intl.DateTimeFormat([], { hour: "numeric" }).resolvedOptions().hour12 ? "numeric" : "2-digit"; } catch (e) { return "2-digit"; } })();
// THE CLUB'S TIME ZONE (14 Nov 2026, driver phone plan item 15). The laptop
// says which zone the club is in (data-tz on <html>, an IANA name); every
// date and time the pages draw is then in the club's zone, not the phone's
// - so a parent at home in another zone, or a driver away at an event, sees
// the start, the heats and "last race" as the club means them. Done once,
// here: toLocaleTimeString, toLocaleDateString and toLocaleString are given
// the club's zone whenever a page didn't ask for one itself. A zone the
// phone doesn't know, or none sent (an older laptop), leaves the phone's.
var HC_TZ = (function () {
  try {
    var tz = document.documentElement.getAttribute('data-tz') || '';
    if (!tz) return undefined;
    new Intl.DateTimeFormat('en-US', { timeZone: tz });
    return tz;
  } catch (e) { return undefined; }
})();
var HC_PHONE_TZ = (function () { try { return Intl.DateTimeFormat().resolvedOptions().timeZone; } catch (e) { return ''; } })();
if (HC_TZ && !Date.prototype.hcTz) {
  ['toLocaleTimeString', 'toLocaleDateString', 'toLocaleString'].forEach(function (name) {
    var own = Date.prototype[name];
    Date.prototype[name] = function (locales, options) {
      var o = {};
      if (options) for (var k in options) o[k] = options[k];
      if (!o.timeZone) o.timeZone = HC_TZ;
      try { return own.call(this, locales, o); } catch (e) { return own.call(this, locales, options); }
    };
  });
  Date.prototype.hcTz = HC_TZ;
}
// "EST" after a time, only when the phone is somewhere other than the club.
function hcZoneWord(d) {
  if (!HC_TZ || HC_TZ === HC_PHONE_TZ) return '';
  try {
    var part = new Intl.DateTimeFormat('en-US', { timeZone: HC_TZ, timeZoneName: 'short' }).formatToParts(d).filter(function (p) { return p.type === 'timeZoneName'; })[0];
    return part ? ' ' + part.value : '';
  } catch (e) { return ''; }
}
function hcHm(s) {
  s = String(s == null ? '' : s);
  var m = /^([0-9]{1,2}):([0-9]{2})$/.exec(s);
  if (!m || HC_H12 !== true) return s;
  var h = Number(m[1]);
  return ((h % 12) || 12) + ':' + m[2] + (h < 12 ? ' AM' : ' PM');
}
`;

/** The data-tz attribute for <html> (the club's IANA zone), or nothing when the laptop has not said. */
export function tzAttr(tz: string | undefined): string {
  return tz === undefined ? "" : ` data-tz="${tz.replace(/[^A-Za-z0-9_+\-/]/g, "")}"`;
}

/** The data-clock attribute for <html>, or nothing when the club has not said. */
export function clockAttr(clock24: boolean | undefined): string {
  return clock24 === undefined ? "" : ` data-clock="${clock24 ? "24" : "12"}"`;
}
