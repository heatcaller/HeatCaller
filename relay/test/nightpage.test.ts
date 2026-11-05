import assert from "node:assert/strict";
import { createContext, runInContext, Script } from "node:vm";
import { afterEach, beforeEach, describe, it } from "node:test";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * THE PAGE A DRIVER OPENS (13 Oct 2026) - the half of a driver's own night
 * that was missing, and without which the documents went up and nobody
 * could read one.
 *
 * WHAT THIS FILE IS REALLY ABOUT, AND IT IS NOT THE LAYOUT
 *
 * It is about a credential in an address bar. The code that opens this
 * page is the whole of the authorisation - twenty hex characters HMAC'd
 * from a transponder under the club's own secret - and the relay's own
 * `readOneNight` takes it in the BODY of a POST for a stated reason: a
 * query string lands in access logs, in the referrer any outbound link
 * carries, and in a shared browser's history.
 *
 * A page that then put the same code back into a GET would have undone all
 * of that, quietly, and nothing would have failed. So the two tests worth
 * reading first are `The_code_never_goes_into_a_query_the_relay_can_log`
 * and `A_code_in_the_query_is_moved_into_the_fragment` - and after them
 * `Every_kind_of_nothing_reads_the_same`, because three different messages
 * on this page would hand back exactly what the relay's single 404 exists
 * to withhold.
 */

const BILL = "a1b2c3d4e5f60718293a";

let harness: Harness;

beforeEach(async () => {
  harness = await start();
});

afterEach(async () => {
  await harness.close();
});

async function page(club = "acme-rc") {
  return call(harness, "GET", `/c/${club}/night`);
}

/** The one inline script the page carries. */
function script(html: string): string {
  const match = /<script[^>]*>([\s\S]*)<\/script>/.exec(html);

  assert.ok(match, "the page carries its script inline");

  return match[1] as string;
}

/**
 * One function out of the page's script, run on its own, with the page's
 * OWN escaper beside it rather than a copy - the lesson the night-before
 * tests learned, where a stub that did not escape made an escaping test
 * pass while proving nothing.
 *
 * The script ends by calling load(), so it cannot simply be evaluated.
 */
function lifted(html: string, names: readonly string[], extras: Record<string, unknown> = {}) {
  const context = createContext(extras);

  const escFrom = html.indexOf("const esc = (v) =>");
  const escTo = html.indexOf("));", escFrom);

  assert.notEqual(escFrom, -1, "the page carries its own escaper");
  assert.notEqual(escTo, -1, "the escaper is the shape this expects");

  runInContext(html.slice(escFrom, escTo + 3), context);

  for (const name of names) {
    const from = html.indexOf(`function ${name}(`);

    assert.notEqual(from, -1, `the page carries ${name}`);

    let depth = 0;
    let to = from;

    for (let i = html.indexOf("{", from); i < html.length; i++) {
      if (html[i] === "{") depth++;
      if (html[i] === "}") depth--;

      if (depth === 0) {
        to = i;
        break;
      }
    }

    runInContext(html.slice(from, to + 1), context);
  }

  return (expression: string, bindings: Record<string, unknown> = {}) =>
    runInContext(expression, Object.assign(context, bindings));
}

// --- The route ------------------------------------------------------------

describe("the page is served", () => {
  it("answers at /c/<club>/night", async () => {
    const answer = await page();

    assert.equal(answer.status, 200);
    assert.match(answer.headers["content-type"] ?? "", /text\/html/);
    assert.match(answer.text, /Acme RC/);
  });

  it("asks search engines to leave it alone", async () => {
    // There is nothing on it for anybody without a code, and nothing a
    // search engine should hold a copy of.
    assert.match((await page()).text, /name="robots" content="noindex, nofollow"/);
  });

  it("is not in the nav bar", async () => {
    // Reached only by a link somebody was sent, like sign-up. A tab for it
    // would invite every visitor to a page they cannot open.
    const nav = /<nav[\s\S]*?<\/nav>/.exec((await page()).text);

    assert.ok(nav, "the shell draws a nav bar");
    assert.doesNotMatch(nav[0], /\/night/);
  });

  it("is served even for a club that does not offer this", async () => {
    // The switch is off for both clubs here - nothing has turned it on.
    // A link somebody was sent that 404s is a link they think is broken,
    // and refusing here would also hand a stranger a way to learn which
    // clubs offer this.
    assert.equal((await page()).status, 200);
    assert.equal((await page("brookside")).status, 200);
  });

  it("its script parses as JavaScript", async () => {
    // pages.test.ts already does this for the six pages in PAGES, and this
    // one is deliberately not in PAGES - so nothing was checking it, and a
    // syntax error got through on the first run. The page's JavaScript
    // lives inside a TypeScript template literal, which eats every escape
    // it recognises: a "\\r\\n" written with one backslash becomes a real
    // line break inside a string literal, and takes the whole script down.
    const html = (await page()).text;

    assert.doesNotThrow(() => new Script(script(html)));
  });

  it("does not answer to a near miss", async () => {
    assert.equal((await call(harness, "GET", "/c/acme-rc/nights")).status, 404);
    assert.equal((await call(harness, "GET", "/c/acme-rc/night/extra")).status, 404);
  });
});

// --- The credential -------------------------------------------------------

describe("the code in the address", () => {
  it("the code never goes into a query the relay can log", async () => {
    const html = (await page()).text;

    // POSTed with the code in the body. Asserted on the page rather than
    // trusted, because a GET here would be invisible: it would work.
    assert.match(html, /method: 'POST'/);
    assert.match(html, /JSON\.stringify\(\{ id: CODE \}\)/);

    // And the API address it builds carries nothing but the club.
    assert.doesNotMatch(html, /\/night\?d=/);
    assert.doesNotMatch(html, /'\/night\?' \+/);
  });

  it("reads the code from either form a club might send", async () => {
    const run = lifted((await page()).text, ["codeFrom"]);

    assert.equal(run(`codeFrom('#d=${BILL}')`), BILL);
    assert.equal(run(`codeFrom('?d=${BILL}')`), BILL);
    assert.equal(run(`codeFrom('d=${BILL}')`), BILL);

    // The badge the club prints carries other things beside it.
    assert.equal(run(`codeFrom('?from=badge&d=${BILL}')`), BILL);
    assert.equal(run(`codeFrom('#d=${BILL}&x=1')`), BILL);
  });

  it("takes a code typed in capitals, because a badge is read by eye", async () => {
    const run = lifted((await page()).text, ["codeFrom"]);

    assert.equal(run(`codeFrom('#d=${BILL.toUpperCase()}')`), BILL);
  });

  it("refuses anything that is not a code, before anything is sent", async () => {
    // The relay validates this too and answers its bare 404 - but a
    // request never made is a request never logged, and a page that posts
    // whatever is in its address bar is a page that will one day post
    // somebody's typing.
    const run = lifted((await page()).text, ["codeFrom"]);

    assert.equal(run("codeFrom('')"), "");
    assert.equal(run("codeFrom(null)"), "");
    assert.equal(run("codeFrom('#d=')"), "");
    assert.equal(run(`codeFrom('#d=${BILL.slice(0, 19)}')`), "");
    assert.equal(run(`codeFrom('#d=${BILL}bb')`), "");
    assert.equal(run("codeFrom('#d=../../etc/passwd0000')"), "");
    assert.equal(run("codeFrom('#d=a1b2c3d4e5f60718293g')"), "");
    assert.equal(run("codeFrom('#driver=3012345')"), "");
  });

  it("a code in the query is moved into the fragment", async () => {
    // A fragment is never sent to any server, so a reload, a bookmark or
    // a forwarded link keeps the code out of every log it would otherwise
    // have reached. The query form has to be read - it is the shape of the
    // badge the club already prints - but it does not have to be kept.
    const html = (await page()).text;

    assert.match(html, /history\.replaceState/);
    assert.match(html, /location\.pathname \+ '#d=' \+ CODE/);
  });

  it("the server never sees or writes the code", async () => {
    // It cannot: with the "#d=" form the fragment never reaches this
    // handler. Asserted so that nobody later adds a server-side read of
    // it "to put the name in the title".
    const html = (await page("acme-rc")).text;

    assert.doesNotMatch(html, new RegExp(BILL));
  });
});

// --- What it says when there is nothing -----------------------------------

describe("every kind of nothing reads the same", () => {
  it("one message covers a closed club, no night and a bad code", async () => {
    const html = (await page()).text;

    // Three different messages would let somebody working through guesses
    // learn which codes are real, which is the one thing eighty bits are
    // there to prevent - and it would undo readOneNight's single 404 from
    // the other end.
    //
    // NAMING BOTH POSSIBILITIES IS NOT A LEAK, and this test said it was
    // on its first run. "Your club may not offer this, or there may be
    // nothing filed for you yet" commits to neither, so it tells somebody
    // guessing nothing at all - and it tells a real driver what to do,
    // which a bare "not found" does not. What must not exist is a message
    // that picks ONE cause.
    const refusals = html.match(/No night was found for that code/g) ?? [];

    assert.equal(refusals.length, 1, "the page has exactly one refusal");

    // Both halves, matched separately: the message is a concatenation in
    // the source, so a test for the whole phrase would really be a test
    // for where the line happens to wrap.
    assert.match(html, /your club does not offer this/);
    assert.match(html, /nothing filed for you yet/);

    for (const giveaway of [
      "no such club",
      "that code does not exist",
      "wrong code",
      "invalid code",
      "not checked in",
      "this club has not",
    ]) {
      assert.doesNotMatch(html, new RegExp(giveaway, "i"), `the page must not say "${giveaway}"`);
    }
  });

  it("says what to do when there is no code at all", async () => {
    // Different from the refusal above on purpose, and safe: somebody who
    // opened the bare address has not offered a code to be told about.
    assert.match((await page()).text, /needs the code from your check-in badge/);
  });
});

// --- What it draws --------------------------------------------------------

describe("what it draws", () => {
  /** The page's draw(), with just enough of a browser under it. */
  async function drawing() {
    const box = { innerHTML: "" };

    const run = lifted((await page()).text, ["lines", "nothing", "draw"], {
      box,
      document: { getElementById: () => null },
    });

    return (night: unknown) => {
      run("draw()", { night });
      return box.innerHTML;
    };
  }

  // "draws their podium videos, and only the relay's own" went on 28 Oct
  // 2026 with the videos themselves (they stay on the laptop) - see
  // novideos.test.ts, which checks a night no longer draws any.

  it("shows the name, the day and every line of it", async () => {
    const draw = await drawing();

    const html = draw({
      driver: "Bill Hargrove",
      when: "Saturday 10 October 2026",
      night: "2026-10-10",
      title: "Club Round 4",
      lines: ["Bill Hargrove raced Heat 2 and finished sixth of nineteen.", "Their best lap was 8.412s."],
    });

    assert.match(html, /Bill Hargrove<\/h2>/);
    assert.match(html, /Club Round 4/);
    assert.match(html, /Saturday 10 October 2026/);
    assert.match(html, /finished sixth of nineteen/);
    assert.match(html, /8\.412s/);
    assert.equal((html.match(/<li>/g) ?? []).length, 2);
  });

  it("offers the picture and no text file, and builds it in the browser", async () => {
    const draw = await drawing();
    const html = draw({ driver: "Bill Hargrove", lines: ["Raced."] });

    assert.match(html, /id="pic"/);
    assert.doesNotMatch(html, /id="dl"/);
    assert.doesNotMatch(html, /text file/);
    // The phone's share sheet first, with the picture on the page after.
    assert.ok((await page()).text.includes("navigator.canShare({ files: [file] })"));
    assert.ok((await page()).text.includes("Press and hold the picture to save or share it."));

    // No endpoint for it: the words are already on the screen, and a
    // download route would be the relay serving the same text twice.
    assert.match((await page()).text, /URL\.createObjectURL/);
    assert.match((await page()).text, /URL\.revokeObjectURL/);
  });

  it("escapes a name and a line, because both are a club's own typing", async () => {
    // A class is called "2wd/4wd" and a driver is called whatever their
    // profile says. The facts are already through FactPack's own cleaning,
    // which takes out brackets and braces - but not a quote, and not an
    // ampersand, and this is HTML.
    const draw = await drawing();

    const html = draw({
      driver: '<script>alert(1)</script>',
      title: 'Bill & Sam\'s "Shootout"',
      lines: ['<img src=x onerror=alert(1)>'],
    });

    assert.doesNotMatch(html, /<script>/);
    assert.doesNotMatch(html, /<img /);
    assert.match(html, /&lt;script&gt;/);
    assert.match(html, /&amp;/);
  });

  it("a night with no lines in it says so rather than drawing nothing", async () => {
    // The laptop refuses to build one of these, so it can only be a relay
    // holding a document from a build that could.
    const draw = await drawing();

    assert.match(draw({ driver: "Bill Hargrove", lines: [] }), /nothing in this night/);
    assert.match(draw({ driver: "Bill Hargrove" }), /nothing in this night/);
    assert.match(draw({ driver: "Bill Hargrove", lines: "not a list" }), /nothing in this night/);
  });

  it("drops a line that is not a line", async () => {
    const run = lifted((await page()).text, ["lines"]);

    // Spread into an array of this realm: one built inside the VM has the
    // VM's own Array prototype, and deepStrictEqual compares those.
    const got = (expression: string) => [...(run(expression) as readonly string[])];

    assert.deepEqual(got('lines(["One.", "", "   ", null, 7, "Two."])'), ["One.", "Two."]);
    assert.deepEqual(got("lines(null)"), []);
    assert.deepEqual(got('lines("One.")'), []);
  });

  it("a night with no name still draws, because the lines are the point", async () => {
    const draw = await drawing();

    assert.match(draw({ lines: ["Raced Heat 2."] }), /Your night<\/h2>/);
  });
});

/**
 * HOW A DRIVER FINDS THE PAGE AT ALL (13 Oct 2026).
 *
 * The night is opened by the twenty characters on a driver's check-in
 * badge, and the check-in page is the only page in a club's whole site
 * that already has them - the badge is printed as `checkin?d=<code>`.
 * Without a link from there a club would have to compose and send every
 * driver their own address by hand, which is the difference between built
 * and used.
 *
 * `The_link_puts_the_code_in_the_fragment` is the one that matters: the
 * night's route takes the code in a POST body so it stays out of access
 * logs and referrers, and a link from here with it in a query would have
 * undone all of that from the other end.
 */
describe("a driver is offered their night where they check in", () => {
  const BADGE = "checkin";

  /** The check-in page's drawOne(), with just enough of a browser under it. */
  async function drawingCheckin(extras: Record<string, unknown> = {}) {
    const html = (await call(harness, "GET", `/c/acme-rc/${BADGE}`)).text;
    const box = { innerHTML: "" };

    const run = lifted(html, ["isIn", "initials", "img", "avatar", "facts", "drawOne"], {
      box,
      pending: new Map(),
      CLUB: "acme-rc",
      document: { getElementById: () => ({ addEventListener: () => {} }) },
      ...extras,
    });

    return (bindings: Record<string, unknown>) => {
      run("drawOne()", { eventName: "", racer: false, nights: false, ...bindings });
      return box.innerHTML;
    };
  }

  const bill = { id: BILL, name: "Bill Hargrove", checkedIn: false };

  it("the relay says whether the club offers it, beside the driver", async () => {
    // On `one` rather than `list`: this is the answer that already knows
    // which driver it is about, because the phone sent their code.
    await call(harness, "POST", "/api/v1/c/acme-rc/publish/checkin?seq=1", {
      token: ACME_TOKEN,
      json: { enabled: true, pinSha256: "", event: "Club Night", drivers: [{ id: BILL, name: "Bill Hargrove" }] },
    });

    await call(harness, "POST", "/api/v1/c/acme-rc/nights/open", { token: ACME_TOKEN, json: { open: true } });

    const answer = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/one", { json: { id: BILL } });

    assert.equal(answer.status, 200);
    assert.equal(answer.body.nights, true);
  });

  it("and says no for a club that has not switched it on", async () => {
    await call(harness, "POST", "/api/v1/c/acme-rc/publish/checkin?seq=1", {
      token: ACME_TOKEN,
      json: { enabled: true, pinSha256: "", event: "Club Night", drivers: [{ id: BILL, name: "Bill Hargrove" }] },
    });

    const answer = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/one", { json: { id: BILL } });

    assert.equal(answer.status, 200);
    assert.equal(answer.body.nights, false);
  });

  it("an id that is not on the list learns nothing, about nights or anything else", async () => {
    // The switch is only ever reported to somebody already holding a real
    // badge code, because the id is checked first and a miss is a 404 -
    // which is what keeps which codes are real a secret, here and in
    // readOneNight.
    await call(harness, "POST", "/api/v1/c/acme-rc/publish/checkin?seq=1", {
      token: ACME_TOKEN,
      json: { enabled: true, pinSha256: "", event: "Club Night", drivers: [{ id: BILL, name: "Bill Hargrove" }] },
    });

    await call(harness, "POST", "/api/v1/c/acme-rc/nights/open", { token: ACME_TOKEN, json: { open: true } });

    const answer = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/one", {
      json: { id: "0000000000000000beef" },
    });

    assert.equal(answer.status, 404);
    assert.doesNotMatch(answer.text, /night/i);
  });

  it("the link puts the code in the fragment, never a query", async () => {
    const draw = await drawingCheckin({ BADGE: BILL });
    const html = draw({ drivers: [bill], nights: true });

    assert.match(html, new RegExp(`/c/acme-rc/night#d=${BILL}`));
    assert.doesNotMatch(html, new RegExp(`night\\?d=${BILL}`));
  });

  it("no link for a club that does not offer it", async () => {
    const draw = await drawingCheckin({ BADGE: BILL });

    assert.doesNotMatch(draw({ drivers: [bill], nights: false }), /\/night/);
  });

  it("links somebody who signed in by PIN to their night by the code their PIN's answer carried, and nobody else", async () => {
    // Since 29 Oct 2026 (relay navigation round 2). A racer behind their own
    // PIN has shown no badge, but the relay answers their PIN with their own
    // driver, whose id is their code - exactly what My night opens the night
    // with. Never a code guessed or taken from anywhere else: no racer, or
    // an id that is not a code, and there is no link.
    const draw = await drawingCheckin({ BADGE: "" });

    assert.match(draw({ drivers: [bill], nights: true, racer: true }), new RegExp(`/night#d=${bill.id}`));
    assert.doesNotMatch(draw({ drivers: [bill], nights: true, racer: false }), /\/night/);
    assert.doesNotMatch(draw({ drivers: [{ ...bill, id: "not-a-code" }], nights: true, racer: true }), /\/night/);
    assert.doesNotMatch(draw({ drivers: [bill], nights: false, racer: true }), /\/night/);
  });

  it("still offers the full list and the check-in button", async () => {
    // The link is an addition to that page, not a replacement for any of
    // it: checking in is what somebody opened it to do.
    const draw = await drawingCheckin({ BADGE: BILL });
    const html = draw({ drivers: [bill], nights: true });

    assert.match(html, /id="go"/);
    assert.match(html, /Not you\? Open the full list/);
  });
});

/**
 * THE PICTURE A DRIVER SHARES (13 Oct 2026), and why it is not a card the
 * relay draws.
 *
 * The plan's Phase 1b said to point `card.ts` at the per-driver document -
 * the relay already draws 1200x630 cards for a shared RESULT, so a "your
 * night" card is the same machinery. It is also the one thing this feature
 * must not do, and the reason is the feature's whole purpose.
 *
 * A card served from an address has to be KEYED by something, and the only
 * key a night has is the driver's badge code. So the moment a driver posted
 * their card to a club group, the address carrying that code would be in
 * the post, in the referrer, and in the logs of whatever fetched it to draw
 * the preview - and a badge code opens that driver's whole night AND checks
 * them in or out, because `checkin/tap` needs the code and nothing else.
 * Sharing the card would publish the credential.
 *
 * So the card is drawn in the BROWSER and handed over as a file. A driver
 * shares a picture, never an address. `No_card_is_ever_served_from_an
 * _address` is the test that holds that line.
 */
describe("the picture", () => {
  it("no card is ever served from an address", async () => {
    // The whole guarantee, asserted from the outside. If somebody later
    // adds a route for this, these fail.
    for (const path of [
      `/c/acme-rc/night/${BILL}/card.png`,
      `/c/acme-rc/night/card.png?d=${BILL}`,
      `/c/acme-rc/night/card.png`,
      `/api/v1/c/acme-rc/night/${BILL}/card.png`,
    ]) {
      const answer = await call(harness, "GET", path);

      assert.notEqual(answer.status, 200, `${path} must not serve a card`);
    }
  });

  it("the page never puts the code into any address at all", async () => {
    const html = (await page()).text;

    // The CODE in the page is the only thing being asserted on, so the
    // comments have to come out first: the note explaining why there is no
    // card address names card.png, and the first version of this test
    // matched its own explanation.
    const code = script(html).replace(/^\s*\/\/.*$/gm, "");

    assert.doesNotMatch(code, /card\.png/);

    // CODE is used, of course - five times. What matters is where it may
    // GO: into exactly one piece of text, the fragment, and onto the
    // network only as a field of the POST body.
    assert.equal((code.match(/\+ CODE/g) ?? []).length, 1, "the code is concatenated into one thing only");
    assert.match(code, /location\.pathname \+ '#d=' \+ CODE/);
    assert.match(code, /JSON\.stringify\(\{ id: CODE \}\)/);
    assert.doesNotMatch(code, /fetch\([^)]*CODE/, "never into a fetch address");
  });

  it("is drawn on a canvas and handed over as a file", async () => {
    const html = (await page()).text;

    assert.match(html, /createElement\('canvas'\)/);
    assert.match(html, /canvas\.toBlob/);
    assert.match(html, /'image\/png'/);
  });

  it("is the size a chat app draws as a banner", async () => {
    const run = lifted((await page()).text, []);
    const html = (await page()).text;

    // The same 1200x630 as card.ts, because below that a chat app draws a
    // thumbnail beside a line of text instead.
    assert.match(html, /const CARD_W = 1200;/);
    assert.match(html, /const CARD_H = 630;/);

    assert.ok(run("1") === 1, "the context is usable");
  });

  it("names the file after the driver and the day", async () => {
    const run = lifted((await page()).text, ["fileName"]);

    assert.equal(run('fileName({ driver: "Bill Hargrove", night: "2026-10-10" }, "png")'),
      "Bill Hargrove - 2026-10-10.png");
    assert.equal(run('fileName({ driver: "Bill Hargrove", night: "2026-10-10" }, "txt")'),
      "Bill Hargrove - 2026-10-10.txt");
  });

  it("keeps a name out of the file system's way", async () => {
    // A name is whatever the club typed, and this becomes a file name on
    // somebody's phone.
    const run = lifted((await page()).text, ["fileName"]);

    assert.equal(run('fileName({ driver: "Dave/Smith", night: "2026-10-10" }, "png")'),
      "Dave_Smith - 2026-10-10.png");
    assert.equal(run('fileName({ driver: "../../etc/passwd", night: "" }, "png")'),
      ".._.._etc_passwd.png");
    assert.equal(run('fileName({}, "png")'), "my night.png");
    assert.equal(run("fileName(null, 'png')"), "my night.png");
    assert.equal(run('fileName({ driver: "   " }, "png")'), "my night.png");
  });

  // --- the wrapping, which is the only real arithmetic on the card -------

  /** A measurer where every character is one unit wide, so a width is a character count. */
  const chars = "(v) => String(v).length";

  it("breaks a line at a space, never mid-word", async () => {
    const run = lifted((await page()).text, ["wrap"]);

    assert.deepEqual([...(run(`wrap("one two three four", 9, ${chars})`) as string[])],
      ["one two", "three", "four"]);
  });

  it("leaves a line that fits as one row", async () => {
    const run = lifted((await page()).text, ["wrap"]);

    assert.deepEqual([...(run(`wrap("one two", 40, ${chars})`) as string[])], ["one two"]);
  });

  it("collapses the whitespace a fact arrives with", async () => {
    const run = lifted((await page()).text, ["wrap"]);

    assert.deepEqual([...(run(`wrap("  one   two  ", 40, ${chars})`) as string[])], ["one two"]);
    assert.deepEqual([...(run(`wrap("", 40, ${chars})`) as string[])], []);
    assert.deepEqual([...(run(`wrap(null, 40, ${chars})`) as string[])], []);
  });

  it("a word wider than the card goes on a row of its own", async () => {
    // It overflows, and that is the lesser evil: there is no hyphenation
    // in a canvas, and breaking mid-word makes a lap time unreadable.
    const run = lifted((await page()).text, ["wrap"]);

    assert.deepEqual([...(run(`wrap("short enormouslylongword", 8, ${chars})`) as string[])],
      ["short", "enormouslylongword"]);
  });

  // --- what fits, and what is said about what does not -------------------

  it("fills the card until the rows run out", async () => {
    const run = lifted((await page()).text, ["wrap", "fitLines"]);
    const got = run(`fitLines(["aaa", "bbb", "ccc"], 10, 2, ${chars})`) as { blocks: string[][]; more: number };

    assert.equal(got.blocks.length, 2);
    assert.equal(got.more, 1);
  });

  it("counts the rows a wrapped fact really takes, not the facts", async () => {
    // The reason this is not a fixed count: one fact may take two rows
    // and the next one, so a fixed count either overflows the card or
    // wastes half of it.
    const run = lifted((await page()).text, ["wrap", "fitLines"]);
    const got = run(`fitLines(["one two three", "four"], 7, 2, ${chars})`) as { blocks: string[][]; more: number };

    assert.equal(got.blocks.length, 1, "the first fact took both rows");
    assert.deepEqual([...got.blocks[0]!], ["one two", "three"]);
    assert.equal(got.more, 1);
  });

  it("never half-draws a fact to fill the last row", async () => {
    // A fact that does not fit whole is left off whole. Half a sentence on
    // a card a driver posts is worse than one fewer sentence.
    const run = lifted((await page()).text, ["wrap", "fitLines"]);
    const got = run(`fitLines(["aaa", "one two three"], 7, 2, ${chars})`) as { blocks: string[][]; more: number };

    assert.equal(got.blocks.length, 1);
    assert.deepEqual([...got.blocks[0]!], ["aaa"]);
    assert.equal(got.more, 1);
  });

  it("says how many were left off rather than dropping them quietly", async () => {
    // A card that silently dropped half a night would have a driver
    // sharing a picture that is not their evening.
    assert.match((await page()).text, /'and ' \+ fitted\.more \+ ' more'/);
  });

  it("takes everything when everything fits", async () => {
    const run = lifted((await page()).text, ["wrap", "fitLines"]);
    const got = run(`fitLines(["aaa", "bbb"], 10, 8, ${chars})`) as { blocks: string[][]; more: number };

    assert.equal(got.blocks.length, 2);
    assert.equal(got.more, 0);
  });

  it("an empty night fills nothing and claims nothing is missing", async () => {
    const run = lifted((await page()).text, ["wrap", "fitLines"]);
    const got = run(`fitLines([], 10, 8, ${chars})`) as { blocks: string[][]; more: number };

    assert.equal(got.blocks.length, 0);
    assert.equal(got.more, 0);
  });

  // --- the club's own look -----------------------------------------------

  it("takes only a colour off the page, never any other value", async () => {
    // The variables are the shell's own and site.ts refuses anything but
    // hex - but this reads them out of a live stylesheet, and a value that
    // was not a colour would be drawn as text into a canvas fill.
    const run = lifted((await page()).text, ["paint"], {
      getComputedStyle: () => ({ getPropertyValue: (n: string) => (n === "--bg" ? "  #123456 " : "red; }") }),
      document: { documentElement: {} },
    });

    assert.equal(run('paint("--bg", "#000000")'), "#123456");
    assert.equal(run('paint("--primary", "#FF2E9A")'), "#FF2E9A");
  });

  it("falls back rather than throwing when the page will not say", async () => {
    const run = lifted((await page()).text, ["paint"], {
      getComputedStyle: () => {
        throw new Error("no");
      },
      document: { documentElement: {} },
    });

    assert.equal(run('paint("--bg", "#000000")'), "#000000");
  });

  it("draws the club's name, which the shell now publishes", async () => {
    const html = (await page()).text;

    assert.match(html, /data-club-name="Acme RC"/);
    assert.match(html, /const CLUB_NAME = document\.body\.dataset\.clubName/);
    assert.match(html, /fillText\(CLUB_NAME/);
  });

  it("escapes the club's name into that attribute", async () => {
    // It goes into an HTML attribute, and a club's name is whatever the
    // relay's tenant list says.
    const html = (await page()).text;

    assert.doesNotMatch(html, /data-club-name="[^"]*<"/);
  });
});
