import assert from "node:assert/strict";
import { createContext, runInContext } from "node:vm";
import { afterEach, beforeEach, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";
import { pageParts } from "./driverexp-rp1-fakepage.js";

/**
 * MESSAGE THE ANNOUNCER AND WHO WINS? ON THE CLUB'S OWN PAGE (2 Nov 2026,
 * docs audit). The app's Room & Web card offered "Take them from the
 * club's own web page" and "Let the club's own web page vote", and this
 * relay had the routes for both - but no page ever called them, so ticking
 * either box did nothing anybody could see.
 *
 * These tests hold the two promises the fix makes: Live offers each form
 * only while the club has it switched on (and the vote only while a vote
 * is open), and what is sent from the page reaches the laptop's inbox. The
 * page's own script is run against the real relay, so a route renamed on
 * either side fails here.
 */

const API = "/api/v1/c/acme-rc";

let harness: Harness;

beforeEach(async () => {
  harness = await start(new MemoryStore());
});

afterEach(async () => {
  await harness.close();
});

async function live(): Promise<string> {
  const answer = await call(harness, "GET", "/c/acme-rc/live");
  assert.equal(answer.status, 200);
  return answer.text;
}

async function notesOpen(open: boolean): Promise<void> {
  const answer = await call(harness, "POST", `${API}/notes/open`, { token: ACME_TOKEN, json: { open } });
  assert.equal(answer.status, 200, JSON.stringify(answer.body));
}

const BILL = { id: "t1", name: "Bill Hargrove" };
const SAM = { id: "t2", name: "Sam Teague" };

async function ballot(body: Record<string, unknown>): Promise<void> {
  const answer = await call(harness, "POST", `${API}/vote/open`, { token: ACME_TOKEN, json: body });
  assert.equal(answer.status, 200, JSON.stringify(answer.body));
}

// ------------------------------------------------------------ a tiny page

/** An element as these two scripts use one, and no more. */
class El {
  private text = "";
  value = "";
  className = "";
  type = "";
  hidden = false;
  disabled = false;
  readonly children: El[] = [];
  private readonly clicks: (() => unknown)[] = [];
  private readonly inputs: (() => unknown)[] = [];

  get textContent(): string {
    return this.text;
  }

  /** As in a browser: setting the text takes everything else out. */
  set textContent(value: string) {
    this.text = String(value);
    this.children.length = 0;
  }

  addEventListener(kind: string, run: () => unknown): void {
    (kind === "click" ? this.clicks : this.inputs).push(run);
  }

  appendChild(child: El): void {
    this.children.push(child);
  }

  set innerHTML(_value: string) {
    throw new Error("these cards are built with textContent, never markup");
  }

  click(): void {
    for (const run of this.clicks) run();
  }

  type_(text: string): void {
    this.value = text;
    for (const run of this.inputs) run();
  }
}

/** Waits for the page's fetches to the real relay to come back and be acted on. */
async function settle(): Promise<void> {
  for (let i = 0; i < 6; i++) {
    await new Promise((done) => setTimeout(done, 15));
  }
}

/** One of the page's cards' scripts, run with the given elements and a fetch that reaches the test's relay. */
function run(html: string, opening: string, elements: Record<string, El>): { poll: () => void } {
  const { script } = pageParts(html);
  const from = script.indexOf(opening);
  assert.notEqual(from, -1, "the page carries the card's script");
  const to = script.indexOf("\n})();\n", from);
  assert.notEqual(to, -1);

  const intervals: (() => void)[] = [];

  const context = createContext({
    CLUB: "acme-rc",
    JSON,
    Array,
    String,
    document: {
      hidden: false,
      getElementById: (id: string) => elements[id] ?? null,
      createElement: () => new El(),
      addEventListener: () => {},
    },
    setInterval: (step: () => void) => intervals.push(step),
    fetch: (url: string, init?: RequestInit) => fetch(harness.base + url, init),
  });

  runInContext(script.slice(from, to + "\n})();".length), context);

  return { poll: () => intervals.forEach((step) => step()) };
}

// ------------------------------------------------------------ the notes

const NOTE_OPENING = "(function () {\n  const text = document.getElementById('shouttext');";

function noteElements() {
  return {
    shouttext: new El(),
    shoutwho: new El(),
    shoutsend: new El(),
    shoutsaid: new El(),
    shoutleft: new El(),
  };
}

describe("Message the announcer on Live", () => {
  it("is not there for a club that has not switched it on", async () => {
    const html = await live();

    assert.doesNotMatch(html, /id="shout"/);
    assert.doesNotMatch(html, /shouttext/);
  });

  it("is there while it is on, and goes again when it is switched off", async () => {
    await notesOpen(true);

    const html = await live();
    assert.match(html, /<div class="card shout" id="shout">/);
    assert.match(html, /<textarea id="shouttext" maxlength="200"/);
    assert.match(html, /<input id="shoutwho" maxlength="40"/);
    assert.match(html, /The race director reads every message before the announcer says anything/);

    await notesOpen(false);
    assert.doesNotMatch(await live(), /id="shout"/);
  });

  it("sends the message and name to the laptop's inbox, as the Wi-Fi page does", async () => {
    await notesOpen(true);

    const els = noteElements();
    run(await live(), NOTE_OPENING, els);

    assert.equal(els.shoutleft.textContent, "200 left");
    els.shouttext.type_("  It's Jack's first race since his operation  ");
    els.shoutwho.value = "Dave";
    assert.equal(els.shoutleft.textContent, `${200 - "  It's Jack's first race since his operation  ".length} left`);

    els.shoutsend.click();
    await settle();

    assert.equal(els.shoutsaid.textContent, "Sent to the race director. Thanks!");
    assert.equal(els.shouttext.value, "", "the box is emptied for the next one");

    const inbox = await call(harness, "GET", `${API}/notes/inbox`, { token: ACME_TOKEN });
    assert.equal(inbox.body.items.length, 1);
    assert.equal(inbox.body.items[0].m, "It's Jack's first race since his operation");
    assert.equal(inbox.body.items[0].n, "Dave");
  });

  it("asks for something before sending nothing", async () => {
    await notesOpen(true);

    const els = noteElements();
    run(await live(), NOTE_OPENING, els);

    els.shouttext.type_("   ");
    els.shoutsend.click();
    await settle();

    assert.equal(els.shoutsaid.textContent, "Type something first.");
    assert.deepEqual((await call(harness, "GET", `${API}/notes/inbox`, { token: ACME_TOKEN })).body.items, []);
  });

  it("says so in plain words when the club switched it off after the page was drawn", async () => {
    await notesOpen(true);

    const els = noteElements();
    run(await live(), NOTE_OPENING, els);
    await notesOpen(false);

    els.shouttext.type_("Happy birthday Dad");
    els.shoutsend.click();
    await settle();

    assert.equal(els.shoutsaid.textContent, "Messages to the announcer aren’t being taken right now.");
    assert.equal(els.shoutsend.disabled, false);
  });

  it("passes the relay's own refusal on, so a sender over the limit is told why", async () => {
    await notesOpen(true);

    const els = noteElements();
    run(await live(), NOTE_OPENING, els);

    for (let i = 0; i < 5; i++) {
      els.shouttext.type_(`Note ${i}`);
      els.shoutsend.click();
      await settle();
    }

    assert.equal(els.shoutsaid.textContent, "You have sent a few already - give the announcer a chance.");
  });
});

// ------------------------------------------------------------ the vote

const VOTE_OPENING = "(function () {\n  const box = document.getElementById('vote');";

function voteElements() {
  const box = new El();
  box.hidden = true;

  return { vote: box, voterace: new El(), votelist: new El(), votesaid: new El() };
}

describe("Who wins? on Live", () => {
  it("is not there for a club that has not switched the vote on", async () => {
    assert.doesNotMatch(await live(), /id="vote"/);
  });

  it("is drawn hidden while the vote is on, and goes when it is switched off", async () => {
    await ballot({ enabled: true, open: false, race: "", drivers: [] });

    const html = await live();
    assert.match(html, /<div class="card vote" id="vote" hidden>/);
    assert.match(html, /it never says who you picked/);

    await ballot({ enabled: false });
    assert.doesNotMatch(await live(), /id="vote"/);
  });

  it("stays hidden until a vote opens, then shows the grid", async () => {
    await ballot({ enabled: true, open: false, race: "", drivers: [] });

    const els = voteElements();
    const page = run(await live(), VOTE_OPENING, els);
    await settle();

    assert.equal(els.vote.hidden, true, "no vote running, nothing to show");

    await ballot({ enabled: true, open: true, race: "Stock A-Main", raceKey: "r1", drivers: [BILL, SAM] });
    page.poll();
    await settle();

    assert.equal(els.vote.hidden, false);
    assert.equal(els.voterace.textContent, "Stock A-Main");
    assert.deepEqual(els.votelist.children.map((b) => b.textContent), ["Bill Hargrove", "Sam Teague"]);
    assert.equal(els.votesaid.textContent, "Tap who you reckon wins.");
  });

  it("sends a tap to the laptop's inbox, and shows the pick until voting closes", async () => {
    await ballot({ enabled: true, open: true, race: "Stock A-Main", raceKey: "r1", drivers: [BILL, SAM] });

    const els = voteElements();
    const page = run(await live(), VOTE_OPENING, els);
    await settle();

    els.votelist.children[1]?.click();
    await settle();

    assert.equal(els.votesaid.textContent, "You have Sam Teague. Good luck.");
    assert.equal(els.votelist.children[1]?.className, "pick mine");

    const inbox = await call(harness, "GET", `${API}/vote/inbox`, { token: ACME_TOKEN });
    assert.equal(inbox.body.items.length, 1);
    assert.equal(inbox.body.items[0].d, SAM.id);

    // Closed by the race director: the pick stays, and the grid stops taking taps.
    await ballot({ enabled: true, open: false, race: "Stock A-Main", raceKey: "r1", drivers: [BILL, SAM] });
    page.poll();
    await settle();

    assert.equal(els.vote.hidden, false);
    assert.equal(els.votesaid.textContent, "Voting has closed. Your pick is in.");
    assert.ok(els.votelist.children.every((b) => b.disabled));

    // The next race is a new ballot, and this phone has picked nobody in it.
    await ballot({ enabled: true, open: true, race: "Stock B-Main", raceKey: "r2", drivers: [BILL] });
    page.poll();
    await settle();

    assert.equal(els.voterace.textContent, "Stock B-Main");
    assert.equal(els.votesaid.textContent, "Tap who you reckon wins.");
    assert.equal(els.votelist.children[0]?.className, "pick");
  });

  it("hides again when the vote closes on a phone that did not vote", async () => {
    await ballot({ enabled: true, open: true, race: "Stock A-Main", raceKey: "r1", drivers: [BILL, SAM] });

    const els = voteElements();
    const page = run(await live(), VOTE_OPENING, els);
    await settle();
    assert.equal(els.vote.hidden, false);

    await ballot({ enabled: true, open: false, race: "Stock A-Main", raceKey: "r1", drivers: [BILL, SAM] });
    page.poll();
    await settle();

    assert.equal(els.vote.hidden, true);
  });

  it("hides when the club switches the vote off while the page is open", async () => {
    await ballot({ enabled: true, open: true, race: "Stock A-Main", raceKey: "r1", drivers: [BILL, SAM] });

    const els = voteElements();
    const page = run(await live(), VOTE_OPENING, els);
    await settle();

    await ballot({ enabled: false });
    page.poll();
    await settle();

    assert.equal(els.vote.hidden, true);
  });
});
