/**
 * "EMAIL ME A SIGN-IN LINK" (14 Nov 2026, driver phone plan item 17, Asher:
 * "yes to email me a sign in link. make it optional").
 *
 * A driver whose phone forgot them, with no PIN to hand, types the email
 * the club has for them on Me. The relay queues it; the laptop collects the
 * queue, looks the address up among its drivers, and - only if it is one of
 * theirs - emails that driver their own night's link from the club's own
 * mailbox. No account, no password, and nothing new to remember.
 *
 * OPTIONAL PER CLUB. The laptop's check-in document says whether the club
 * has turned it on (emailLink: true); off, there is no link on Me and the
 * route answers 404 like any route the club doesn't offer.
 *
 * SAYS NOTHING ABOUT WHO IS A MEMBER. The answer is the same whether or not
 * the address is one the club knows: "If that address is on the club's
 * list, a link is on its way". The relay never knows either - it holds no
 * driver's email - and the laptop answers nobody.
 *
 * HELD BRIEFLY. An address waits at most SIGNIN_MAIL_KEEP_MS for the laptop
 * (which has to be running to send it), at most SIGNIN_MAIL_MAX at once,
 * and is dropped once collected. Three asks an hour per address and per
 * connection.
 */

/** The relay's own queue document for these. */
export const SIGNIN_MAIL = "signinmail";

/** How long an ask waits for the laptop before it is dropped. */
export const SIGNIN_MAIL_KEEP_MS = 30 * 60 * 1000;

/** The most asks waiting at once for one club. */
export const SIGNIN_MAIL_MAX = 50;

/** Asks one email address, or one connection, may make in an hour. */
export const SIGNIN_MAIL_PER_HOUR = 3;

/** A request body is one address. */
export const SIGNIN_MAIL_MAX_BODY = 512;

/** One waiting ask: a number the laptop acks up to, the address, and when. */
export interface SigninMailItem {
  readonly n: number;
  readonly email: string;
  readonly at: string;
}

/** An address that looks like one, lower-cased and trimmed, or "". Deliberately loose: the laptop is what decides. */
export function signinEmailFrom(value: unknown): string {
  const v = typeof value === "string" ? value.trim().toLowerCase() : "";
  return v.length >= 3 && v.length <= 254 && /^[^\s@<>()",;:]+@[^\s@<>()",;:]+\.[^\s@<>()",;:]+$/.test(v) ? v : "";
}

/** The waiting asks as stored, cleaned, with those older than the keep dropped. */
export function signinItemsFrom(body: unknown, nowMs: number): SigninMailItem[] {
  const items = typeof body === "object" && body !== null && Array.isArray((body as Record<string, unknown>)["items"])
    ? ((body as Record<string, unknown>)["items"] as unknown[])
    : [];

  return items
    .filter((i): i is Record<string, unknown> => typeof i === "object" && i !== null)
    .map((i) => ({ n: Number(i["n"]), email: signinEmailFrom(i["email"]), at: typeof i["at"] === "string" ? i["at"] : "" }))
    .filter((i) => Number.isInteger(i.n) && i.n > 0 && i.email !== "" && nowMs - Date.parse(i.at) < SIGNIN_MAIL_KEEP_MS && Date.parse(i.at) <= nowMs + 60000);
}
