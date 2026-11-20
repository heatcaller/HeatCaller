import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { nightsFeedWith, nightsIcs } from "../src/ics.js";
import { PAGE_COUNT_EVENTS } from "../src/pagecounts.js";
import { DEFAULT_CONTEXT, homePage, livePage, myNightPage, nightPage } from "../src/pages.js";
import { lookFrom } from "../src/site.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start } from "./helpers.js";

/**
 * DRIVER PHONE PLAN, THE LEFTOVERS (19 Nov 2026): what steps A-H left that
 * needed no phone in hand. Each describe names its item.
 */

const NIGHT = (day: string, extra: Record<string, unknown> = {}) => ({
  day, title: "Club Night", when: day, venue: "Kent Hobbies", classes: ["Stock"], firstRace: "19:00",
  entriesClose: "", off: false, offText: "", takingEntries: true, until: `${day}T23:59:00.000Z`, ...extra,
});

describe("every race night as a feed (item 10 step 3)", () => {
  const now = new Date("2026-11-19T12:00:00.000Z");

  it("adds each announcement, keeps a called-off night marked off, and drops a moved one", () => {
    let feed = nightsFeedWith(undefined, NIGHT("2026-11-26"), now);
    assert.deepEqual(feed.nights.map((n) => n["day"]), ["2026-11-26"]);

    // Moved to the Friday before it happened: the Thursday isn't on any more.
    feed = nightsFeedWith(feed, NIGHT("2026-11-27"), now);
    assert.deepEqual(feed.nights.map((n) => n["day"]), ["2026-11-27"]);

    // Called off: still there, marked so.
    feed = nightsFeedWith(feed, NIGHT("2026-11-27", { off: true, offText: "Flooded" }), now);
    assert.equal(feed.nights.length, 1);
    assert.equal(feed.nights[0]!["off"], true);

    // A week on, the next night joins it; the one called off stays.
    const later = new Date("2026-11-28T12:00:00.000Z");
    feed = nightsFeedWith(feed, NIGHT("2026-12-04"), later);
    assert.deepEqual(feed.nights.map((n) => n["day"]), ["2026-11-27", "2026-12-04"]);
  });

  it("lets nights more than 60 days gone go, and keeps nothing about a driver", () => {
    const feed = nightsFeedWith({ nights: [NIGHT("2026-08-01"), { day: "2026-11-20", secret: "x", drivers: ["Ella"] }] }, NIGHT("2026-11-26"), now);
    // August is over 60 days gone; the 20th, still to come and not the one
    // announced now, was moved.
    assert.deepEqual(feed.nights.map((n) => n["day"]), ["2026-11-26"]);
    assert.doesNotMatch(JSON.stringify(feed), /secret|drivers|Ella/);
  });

  it("serves one calendar with an event per night, a called-off one cancelled, the same UID as nextnight.ics", () => {
    const feed = { nights: [NIGHT("2026-11-26"), NIGHT("2026-12-03", { off: true })] };
    const ics = nightsIcs("acme-rc", "Acme RC", feed, "America/New_York", now);
    assert.match(ics, /^BEGIN:VCALENDAR\r\n/);
    assert.match(ics, /\r\nX-WR-CALNAME:Acme RC race nights\r\n/);
    assert.match(ics, /\r\nREFRESH-INTERVAL;VALUE=DURATION:PT6H\r\n/);
    assert.equal((ics.match(/BEGIN:VEVENT/g) ?? []).length, 2);
    assert.match(ics, /UID:20261126-acme-rc@heatcaller/);
    assert.match(ics, /UID:20261203-acme-rc@heatcaller[\s\S]*STATUS:CANCELLED/);
    assert.match(ics, /\r\nEND:VCALENDAR\r\n$/);
  });

  it("is kept by the relay from each nextnight publish, and served at /c/<club>/nights.ics", async () => {
    const h = await start();
    try {
      const empty = await call(h, "GET", "/c/acme-rc/nights.ics");
      assert.equal(empty.status, 200, "an empty calendar before the first night, so a subscription keeps working");
      assert.doesNotMatch(empty.text, /BEGIN:VEVENT/);

      await call(h, "POST", "/api/v1/c/acme-rc/publish/nextnight?seq=1", { token: ACME_TOKEN, json: NIGHT("2026-09-24") });
      const one = await call(h, "GET", "/c/acme-rc/nights.ics");
      assert.equal(one.status, 200);
      assert.match(one.headers["content-type"] ?? "", /text\/calendar/);
      assert.match(one.text, /UID:20260924-acme-rc@heatcaller/);

      // Never served as a snapshot of its own.
      const raw = await call(h, "GET", "/api/v1/c/acme-rc/snapshot/nightsfeed");
      assert.notEqual(raw.status, 200);
    } finally {
      await h.close();
    }
  });

  it("is offered on Home as a subscription, and Add my races is made on the phone", () => {
    const home = homePage("acme-rc", "Acme RC");
    assert.match(home, /Every race night in your calendar/);
    assert.match(home, /'webcal:\/\/' \+ location\.host \+ path/);
    const night = nightPage("acme-rc", "Acme RC");
    assert.match(night, /Add my races to my calendar/);
    assert.match(night, /new Blob\(\[myRacesIcs\(/);
    assert.match(night, /a\.download = 'my-races\.ics'/);
  });
});

describe("a club's short address, checked (item 5)", () => {
  it("says yours, free, taken or bad, with the token only", async () => {
    const h = await start();
    try {
      const none = await call(h, "GET", "/api/v1/c/acme-rc/alias?name=acmeracing");
      assert.equal(none.status, 401);
      const wrong = await call(h, "GET", "/api/v1/c/acme-rc/alias?name=acmeracing", { token: BROOK_TOKEN });
      assert.equal(wrong.status, 403);

      const ask = async (name: string, token = ACME_TOKEN) =>
        (await call(h, "GET", `/api/v1/c/acme-rc/alias?name=${encodeURIComponent(name)}`, { token })).body.state;

      assert.equal(await ask("acmeracing"), "free");
      assert.equal(await ask("brookside"), "taken", "another club's id");
      assert.equal(await ask("No Spaces!"), "bad");

      await call(h, "POST", "/api/v1/c/brookside/publish/site?seq=1", { token: BROOK_TOKEN, json: { alias: "brooky" } });
      assert.equal(await ask("brooky"), "taken", "another club claimed it first");

      await call(h, "POST", "/api/v1/c/acme-rc/publish/site?seq=1", { token: ACME_TOKEN, json: { alias: "acmeracing" } });
      assert.equal(await ask("acmeracing"), "yours");
    } finally {
      await h.close();
    }
  });
});

describe("the 192 px icon (item 11)", () => {
  it("is drawn at its own size and listed in the manifest", async () => {
    const h = await start();
    try {
      const icon = await call(h, "GET", "/c/acme-rc/icon-192.png");
      assert.equal(icon.status, 200);
      assert.match(icon.headers["content-type"] ?? "", /image\/png/);
      const m = (await call(h, "GET", "/c/acme-rc/manifest.webmanifest")).body;
      const small = m.icons.filter((i: { sizes: string }) => i.sizes === "192x192");
      assert.deepEqual(small.map((i: { purpose: string }) => i.purpose), ["any", "maskable"]);
      assert.match(small[0].src, /^\/c\/acme-rc\/icon-192\.png\?v=/);
    } finally {
      await h.close();
    }
  });
});

describe("the club in each social icon's name (item 23)", () => {
  it("reads \"Acme RC on Facebook\", the tooltip still the service", () => {
    const look = lookFrom({ seq: 1, publishedAt: "2026-11-19T00:00:00Z", body: { social: [{ kind: "facebook", url: "https://facebook.com/acme" }] } }, undefined);
    const live = livePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look });
    assert.match(live, /aria-label="Acme RC on Facebook" title="Facebook"/);
  });
});

describe("the phone board at 130% text (item 6)", () => {
  it("lets the driver's name give way and keeps the last column off the card's edge", () => {
    const live = livePage("acme-rc", "Acme RC");
    assert.match(live, /#board \.card:not\(\.all\) td\.kd \{ max-width: 0; width: 100%; overflow: hidden; text-overflow: ellipsis; \}/);
    assert.match(live, /html\[data-ts\] #board \.card:not\(\.all\) \.kl \{ padding-right: 14px; \}/);
    assert.match(live, /function boardLastKept\(roles, n\)/);
  });
});

describe("Listen fetches the next clip ahead (item 19 step 5)", () => {
  const html = livePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, listening: true } });

  it("asks for every new line at once while the signal is good, and takes them in order", () => {
    assert.match(html, /if \(!listenPaused\) \{\s*for \(const one of lines\) \{\s*if \(isNew\(one\.id, one\.at\) && !ahead\.has\(one\.id\)\) \{\s*ahead\.set\(one\.id, fetchOne\(one\.id\)\);/);
    assert.match(html, /const fetched = await \(ahead\.get\(one\.id\) \|\| fetchOne\(one\.id\)\);/);
  });

  it("still drops a line more than 8 s late when its turn comes", () => {
    assert.match(html, /if \(relayNowAt - one\.at > 8000\) \{/);
  });
});

describe("Live: All columns in a sheet, and a followed driver elsewhere (item 4)", () => {
  const live = livePage("acme-rc", "Acme RC");

  it("opens All columns in a sheet that Back or Done closes", () => {
    assert.match(live, /if \(boardAllSheet\(\)\) return;/);
    assert.match(live, /<h2 id="hcallcols-h">All columns<\/h2>/);
    assert.match(live, /hcSheetOpened\(\(\) => \{ const s = document\.getElementById\('hcallcols'\)/);
  });

  it("gives a followed driver who isn't in this race their next race from the running order", () => {
    assert.match(live, /function youElsewhere\(who\)/);
    assert.match(live, /grab\('\/snapshot\/programme'\)/);
    assert.match(live, /'Not in this race\. Next: '/);
  });
});

describe("Back closes a sheet (item 18 step 3)", () => {
  it("adds a history step for a sheet, at the same address, and Back closes it", () => {
    const html = nightPage("acme-rc", "Acme RC");
    assert.match(html, /history\.pushState\(\{ hcsheet: 1 \}, '', location\.href\)/);
    assert.match(html, /if \(typeof hcSheetOpened === 'function'\) hcSheetOpened\(meCloseSheet\);/);
    assert.match(html, /if \(was && typeof hcSheetClosed === 'function'\) hcSheetClosed\(\);/);
  });
});

describe("Me (items 2, 8 and 9)", () => {
  const html = nightPage("acme-rc", "Acme RC");

  it("says who this phone follows under the driver's own name, apart from who they are", () => {
    assert.match(html, /'<div class="me-following">Following: <b>' \+ esc\(who\) \+ '<\/b>/);
    assert.match(html, /data-unfollow/);
  });

  it("offers the other drivers on this phone a tick to check in too", () => {
    assert.match(html, /'Also check in ' \+ name \+ '\?'/);
    assert.match(html, /void meAlsoCheckIn\(ticked\)/);
  });

  it("opens a race or marshal alert at its own card", () => {
    assert.match(html, /meGoAlert = \(\/\[#&\]go=\(race\|duty\)\(&\|\$\)\/\.exec/);
    assert.match(html, /meGoAlert === 'duty' \? document\.querySelector\('\.lc-duty'\) : document\.querySelector\('\.me-hero'\)/);
    const my = myNightPage("acme-rc", "Acme RC");
    assert.match(my, /HC_ALERT_FROM === 'marshal' \? '&go=duty'/);
  });
});

describe("in-app browsers on Android (item 16 step 3)", () => {
  it("offers Chrome by an intent: address, never with the fragment", () => {
    const html = nightPage("acme-rc", "Acme RC");
    assert.match(html, /'intent:\/\/' \+ location\.host \+ location\.pathname \+ location\.search \+ '#Intent;scheme=https;package=com\.android\.chrome;end'/);
    assert.doesNotMatch(html, /location\.hash \+ '#Intent/);
  });
});

describe("Racing tonight? (item 22)", () => {
  it("counts a visit from the poster, links sign-up only while the club takes them, and folds once all set", () => {
    assert.ok(PAGE_COUNT_EVENTS.includes("poster_opened"));
    const home = homePage("acme-rc", "Acme RC");
    assert.match(home, /hcCount\('poster_opened'\)/);
    assert.match(home, /document\.body\.dataset\.signup === '1'/);
    assert.match(home, /You\\u2019re all set for tonight \\u2713/);
  });

  it("marks the page when sign-up is open", async () => {
    const h = await start();
    try {
      assert.doesNotMatch((await call(h, "GET", "/c/brookside")).text, /data-signup="1"/);
      await call(h, "POST", "/api/v1/c/acme-rc/publish/signup?seq=1", { token: ACME_TOKEN, json: { enabled: true, classes: [], invites: [] } });
      assert.match((await call(h, "GET", "/c/acme-rc")).text, /data-signup="1"/);
    } finally {
      await h.close();
    }
  });
});

describe("an old page after a deploy (item 21 step 5)", () => {
  it("asks the relay's build once on a plain 404, and offers the reload when it differs", () => {
    const html = homePage("acme-rc", "Acme RC");
    assert.match(html, /function hcRouteGone\(response\)/);
    assert.match(html, /\.then\(hcBuildSeen\)/);
    assert.match(html, /if \(response\.status === 404\) \{ hcNetOk\(\); seenKeep\(path, undefined, true\); noteRead\(path, page\); hcRouteGone\(response\); \}/);
  });
});
