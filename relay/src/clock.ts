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
function hcHm(s) {
  s = String(s == null ? '' : s);
  var m = /^([0-9]{1,2}):([0-9]{2})$/.exec(s);
  if (!m || HC_H12 !== true) return s;
  var h = Number(m[1]);
  return ((h % 12) || 12) + ':' + m[2] + (h < 12 ? ' AM' : ' PM');
}
`;

/** The data-clock attribute for <html>, or nothing when the club has not said. */
export function clockAttr(clock24: boolean | undefined): string {
  return clock24 === undefined ? "" : ` data-clock="${clock24 ? "24" : "12"}"`;
}
