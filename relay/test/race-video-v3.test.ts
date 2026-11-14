import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { homePage, nightPage } from "../src/pages.js";
import { VideoCheck } from "../src/video.js";
import { ACME_TOKEN, call } from "./helpers.js";
import { API, SAM, openEverything, publish, startDriver } from "./driverexp-r1-helpers.js";
import { FakePage, pageParts, type FakeNode } from "./driverexp-rp1-fakepage.js";

/**
 * RACE VIDEO, V3 AND V7 (14 Nov 2026, plans/race-video-access-plan.md): the
 * player sheet a tap opens on the driver's night, the "wasn't on the
 * stream" line, the relay's check with YouTube, and the frame policy.
 */

const CODE = "a1b2c3d4e5f60718293a";
const RACE = "fedcba9876543210fedcba9876543210";
const MISSED = "11111111111111111111111111111111";
const VIDEO = { id: "AAAAAAAAAAA", t: 710, end: 970, laps: [724, 733, 741.5], best: 1 };

async function openNight(videoAnswer: unknown): Promise<FakePage> {
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, async (url) => {
    if (url.endsWith("/video/night")) return { status: 200, body: videoAnswer };
    if (url.endsWith("/night")) {
      return {
        status: 200,
        body: {
          ok: true,
          night: {
            driver: "Bill Hargrove", title: "Club Night", when: "Thursday 22 October 2026", night: "2026-10-22",
            races: [
              { sid: MISSED, label: "Q1 Heat 1", at: "2026-10-22T18:10:00Z", pos: 4, of: 8, laps: 7, bestMs: 8800, lapsMs: [9000, 8800] },
              { sid: RACE, label: "Q1 Heat 2", at: "2026-10-22T18:30:00Z", pos: 3, of: 8, laps: 7, bestMs: 8500, lapsMs: [9000, 8500, 8800] },
            ],
          },
        },
      };
    }
    return { status: 404 };
  }, { hash: `#d=${CODE}` });
  page.run(script);
  await page.settle();
  return page;
}

function button(root: FakeNode, words: RegExp): FakeNode {
  const b = root.find((n) => n.tagName === "BUTTON" && words.test(n.textContent))[0];
  assert.ok(b, `a button ${words}`);
  return b;
}

describe("Watch my race on the driver's night (V2, V3)", () => {
  it("asks for the night's videos with the code in the body only, and draws the buttons on the race", async () => {
    const page = await openNight({ ok: true, videos: { [RACE]: VIDEO }, missed: [] });
    const ask = page.sent.find((s) => s.url.endsWith("/video/night"));
    assert.ok(ask, "asked");
    assert.equal(ask.body["id"], CODE);
    assert.doesNotMatch(ask.url, new RegExp(CODE));

    const nx = page.get("nx");
    assert.match(nx.textContent, /Watch my race/);
    assert.match(nx.textContent, /Jump to my best lap/);
  });

  it("opens the sheet with YouTube's privacy-enhanced player at the race, playing inline", async () => {
    const page = await openNight({ ok: true, videos: { [RACE]: VIDEO }, missed: [] });
    button(page.get("nx"), /Watch my race/).click();

    const sheet = page.body.find((n) => n.className === "hcvideo")[0];
    assert.ok(sheet, "the sheet is open");
    assert.equal(sheet.getAttribute("role"), "dialog");
    const frame = sheet.find((n) => n.tagName === "IFRAME")[0]!;
    const src = String((frame as unknown as { src: string }).src);
    assert.match(src, /^https:\/\/www\.youtube-nocookie\.com\/embed\/AAAAAAAAAAA\?/);
    for (const flag of ["start=710", "end=970", "playsinline=1", "rel=0", "enablejsapi=1", "autoplay=1"]) assert.ok(src.includes(flag), flag);
    assert.equal(frame.getAttribute("allow"), "autoplay; encrypted-media; picture-in-picture; fullscreen");
    assert.match(sheet.textContent, /Q1 Heat 2/);
    assert.match(sheet.textContent, /You finished P3/);
    assert.match(sheet.textContent, /Plays from YouTube/);

    const yt = sheet.find((n) => n.tagName === "A")[0]!;
    assert.equal((yt as unknown as { href: string }).href, "https://www.youtube.com/watch?v=AAAAAAAAAAA&t=710s");
  });

  it("seeks the player to a lap by postMessage, with the best one starred", async () => {
    const page = await openNight({ ok: true, videos: { [RACE]: VIDEO }, missed: [] });
    button(page.get("nx"), /Watch my race/).click();
    const sheet = page.body.find((n) => n.className === "hcvideo")[0]!;
    const frame = sheet.find((n) => n.tagName === "IFRAME")[0]! as unknown as { contentWindow: unknown };
    const posted: [string, string][] = [];
    frame.contentWindow = { postMessage: (m: string, to: string) => posted.push([m, to]) };

    button(sheet, /★ Lap 2/).click();
    assert.equal(posted.length, 1);
    assert.deepEqual(JSON.parse(posted[0]![0]), { event: "command", func: "seekTo", args: [733, true] });
    assert.equal(posted[0]![1], "https://www.youtube-nocookie.com");
  });

  it("opens at the best lap from Jump to my best lap", async () => {
    const page = await openNight({ ok: true, videos: { [RACE]: VIDEO }, missed: [] });
    button(page.get("nx"), /Jump to my best lap/).click();
    const frame = page.body.find((n) => n.tagName === "IFRAME")[0]! as unknown as { src: string };
    assert.match(frame.src, /start=733&/);
  });

  it("closes with its Close button", async () => {
    const page = await openNight({ ok: true, videos: { [RACE]: VIDEO }, missed: [] });
    button(page.get("nx"), /Watch my race/).click();
    button(page.body.find((n) => n.className === "hcvideo")[0]!, /^Close$/).click();
    assert.equal(page.body.find((n) => n.className === "hcvideo").length, 0);
  });
});

describe("when there's no video (V7)", () => {
  it("says a race the stream missed wasn't on it, and shows nothing for the rest", async () => {
    const page = await openNight({ ok: true, videos: {}, missed: [MISSED] });
    const nx = page.get("nx");
    assert.doesNotMatch(nx.textContent, /Watch my race|wasn’t on the stream/, "the open race has neither");
    const row = nx.find((n) => n.className === "rbtn" && n.textContent.startsWith("Q1 Heat 1"))[0]!;
    row.click();
    assert.match(page.get("nx").textContent, /This race wasn’t on the stream\./);
  });

  it("draws nothing at all when the club didn't stream", async () => {
    const page = await openNight({ ok: false });
    assert.doesNotMatch(page.get("nx").textContent, /Watch|stream/);
  });
});

describe("the relay's check with YouTube (V7)", () => {
  function checker(answers: (number | undefined)[]) {
    let now = Date.parse("2026-11-14T23:00:00Z");
    const asked: string[] = [];
    const check = new VideoCheck(async (id) => { asked.push(id); return answers.shift(); }, () => new Date(now));
    return { check, asked, later: (ms: number) => { now += ms; } };
  }

  it("asks about a new video once, and a 404 makes it dead", async () => {
    const { check, asked } = checker([404]);
    await check.saw("acme-rc", ["AAAAAAAAAAA", "", "not-an-id"]);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.deepEqual(asked, ["AAAAAAAAAAA"]);
    assert.deepEqual([...check.dead("acme-rc")], ["AAAAAAAAAAA"]);
    assert.equal(check.dead("brookside").size, 0, "one club's check is its own");
  });

  it("asks again once a day for a week, and a 200 brings it back", async () => {
    const { check, asked, later } = checker([401, 200]);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(check.dead("acme-rc").size, 1, "embedding turned off");
    later(25 * 60 * 60 * 1000);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(check.dead("acme-rc").size, 0);
    for (let day = 0; day < 8; day++) {
      later(24 * 60 * 60 * 1000);
      await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    }
    assert.ok(asked.length <= 7, `stops after a week: ${asked.length} asks`);
  });

  it("changes nothing when YouTube doesn't answer", async () => {
    const { check } = checker([undefined]);
    await check.saw("acme-rc", ["AAAAAAAAAAA"]);
    assert.equal(check.dead("acme-rc").size, 0);
  });
});

describe("the relay's video answers and policy (V3, V7)", () => {
  const STREAM = {
    on: true, publicResults: true, leadIn: 10,
    pieces: [{ videoId: "AAAAAAAAAAA", startedUtc: "2026-10-22T18:20:00.000Z", endedUtc: null, nudge: 0 }],
    hidden: [], juniors: [],
  };

  it("lets a page frame YouTube's privacy-enhanced player and nothing else from outside", async () => {
    const h = await startDriver();
    try {
      const page = await call(h, "GET", "/c/acme-rc/night");
      const policy = page.headers["content-security-policy"] ?? "";
      assert.match(policy, /frame-src 'self' https:\/\/www\.youtube-nocookie\.com(;|$)/);
      assert.doesNotMatch(policy, /script-src[^;]*youtube|img-src[^;]*youtube/);
    } finally {
      await h.close();
    }
  });

  it("lets the shell's frames hand fullscreen and picture-in-picture down to the player", () => {
    assert.match(homePage("acme-rc", "Acme RC"), /f\.allow = 'fullscreen; picture-in-picture; autoplay; encrypted-media';/);
  });

  it("answers a driver's night with their videos and the races the stream missed", async () => {
    const h = await startDriver();
    try {
      await openEverything(h);
      assert.equal((await publish(h, "stream", STREAM)).status, 200);
      for (const [sid, green] of [[RACE, "2026-10-22T18:30:00.000Z"], [MISSED, "2026-10-22T18:10:00.000Z"]] as const) {
        await publish(h, `results/${sid}`, { id: sid, startedUtc: green, endedUtc: new Date(Date.parse(green) + 4 * 60_000).toISOString(), results: [] });
      }
      await publish(h, `nights/${SAM}`, { name: "Sam Parker", races: [{ sid: RACE, startMs: 4000, lapsMs: [9000, 8500] }, { sid: MISSED }] });

      const answer = await call(h, "POST", `${API}/video/night`, { json: { id: SAM } });
      assert.equal(answer.status, 200);
      assert.equal(answer.body.videos[RACE].id, "AAAAAAAAAAA");
      assert.equal(answer.body.videos[RACE].t, 590);
      assert.deepEqual(answer.body.missed, [MISSED], "before the stream began");
    } finally {
      await h.close();
    }
  });

  it("tells only the laptop which of its videos YouTube refused", async () => {
    const h = await startDriver();
    try {
      assert.equal((await call(h, "GET", `${API}/video/check`)).status, 401);
      const mine = await call(h, "GET", `${API}/video/check`, { token: ACME_TOKEN });
      assert.equal(mine.status, 200);
      assert.deepEqual(mine.body.dead, [], "this relay checks nothing");
    } finally {
      await h.close();
    }
  });
});
