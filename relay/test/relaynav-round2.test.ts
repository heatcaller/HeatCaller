import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { myNightPage, nightPage, resultsPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * RELAY NAVIGATION, ROUND 2 (29 Oct 2026): Me remembers the driver on
 * this phone until they sign out (Asher's pick). The night page keeps the
 * code once the relay has answered it with a night, Me goes straight there
 * next time, and Sign out on this phone forgets it.
 */

const CODE = "a1b2c3d4e5f60718293a";
const ME = "hg-me-acme-rc";

function night(): Record<string, unknown> {
  return { driver: "Bill Hargrove", title: "Club Night", when: "Thursday 22 October 2026", night: "2026-10-22", lines: ["Bill raced twice."] };
}

function relay(full: unknown): Answerer {
  return async (url, body) => {
    if (url.endsWith("/night")) {
      if (body["part"] === "live") return { status: 200, body: { ok: true } };
      return full === 404 ? { status: 404, body: { ok: false } } : { status: 200, body: full };
    }

    if (url.endsWith("/checkin/one")) return { status: 409, body: { ok: false } };
    return { status: 404 };
  };
}

async function openNight(answer: Answerer, hash = `#d=${CODE}`, stored: Record<string, string> = {}): Promise<FakePage> {
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, answer, { hash });
  for (const [k, v] of Object.entries(stored)) page.eval(`localStorage.setItem(${JSON.stringify(k)}, ${JSON.stringify(v)})`);
  page.run(script);
  await page.settle();
  return page;
}

function stored(page: FakePage, key: string): unknown {
  return page.eval(`localStorage.getItem(${JSON.stringify(key)})`);
}

describe("relay navigation, round 2: the night page remembers the driver", () => {
  it("keeps the code once the relay has answered it with a night", async () => {
    const page = await openNight(relay({ ok: true, night: night() }));

    assert.equal(stored(page, ME), CODE);
    assert.equal(page.get("meout").hidden, false, "Sign out on this phone shows");
  });

  it("keeps nothing for a code the relay has no night for, or a page with no code", async () => {
    assert.equal(stored(await openNight(relay(404)), ME), null);
    assert.equal(stored(await openNight(relay({ ok: true, night: night() }), ""), ME), null);
  });

  it("never keeps anything but the code: no PIN and no pass in this phone's long-term storage", async () => {
    const page = await openNight(relay({ ok: true, night: night() }));

    assert.deepEqual([...page.store.entries()], [[ME, CODE]]);
  });

  it("forgets the driver, and the details sign-in, on Sign out on this phone, and goes back to Me", async () => {
    const page = await openNight(relay({ ok: true, night: night() }), `#d=${CODE}`, { "hg-night-pass-acme-rc": "ef".repeat(32) });

    page.get("meoutlink").click();

    assert.equal(stored(page, ME), null);
    assert.equal(page.eval("sessionStorage.getItem('hg-night-pass-acme-rc')"), null);
    assert.equal(page.eval("location.href"), "/c/acme-rc/mynight");
  });

  it("draws Sign out on this phone on the page, hidden until there is something to forget", () => {
    assert.match(nightPage("acme-rc", "Acme RC"), /<p class="note" id="meout" hidden><a class="more" href="#" id="meoutlink">Sign out on this phone<\/a><\/p>/);
  });
});

describe("relay navigation, round 2: Me goes straight to a remembered night", () => {
  async function openMe(stored: Record<string, string>): Promise<FakePage> {
    const { html, script } = pageParts(myNightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async () => ({ status: 404 }), {});
    for (const [k, v] of Object.entries(stored)) page.eval(`localStorage.setItem(${JSON.stringify(k)}, ${JSON.stringify(v)})`);
    page.run(script);
    await page.settle();
    return page;
  }

  it("opens the remembered night without asking for the PIN", async () => {
    const page = await openMe({ [ME]: CODE });

    assert.equal(page.eval("location.href"), `/c/acme-rc/night#d=${CODE}`);
    assert.doesNotMatch(page.get("mn").drawn(), /Your personal PIN/);
    assert.equal(page.sent.length, 0, "nothing is sent to the relay on the way");
  });

  it("asks for the PIN when nothing is remembered, or what is kept is not a code", async () => {
    for (const kept of [{}, { [ME]: "javascript:alert(1)" }, { [ME]: "A1B2C3D4E5F60718293A" }]) {
      const page = await openMe(kept);

      assert.equal(page.eval("location.href"), undefined);
      assert.match(page.get("mn").drawn(), /Your personal PIN/);
      assert.match(page.get("mn").drawn(), /This phone remembers you once your night opens/);
    }
  });

  it("points the Me tab at the remembered night on every page", () => {
    const html = resultsPage("acme-rc", "Acme RC");

    assert.match(html, /document\.querySelector\('nav\.tabs a\[href\$="\/mynight"\]'\)/);
    assert.match(html, /me\.setAttribute\('href', '\/c\/' \+ encodeURIComponent\(CLUB\) \+ '\/night#d=' \+ code\)/);
  });
});
