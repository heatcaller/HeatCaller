import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { nightPage } from "../src/pages.js";
import { askFieldsFrom } from "../src/driverasks.js";
import { FakePage, pageParts, type FakeNode } from "./driverexp-rp1-fakepage.js";

/**
 * BUG HUNT 17: Me's "I'm coming" card (mespaces.ts).
 *
 * A class with a comma in its name ("Buggy, 2WD") could never be entered
 * from the phone: the page sent it with the comma as a space, and the laptop
 * looked for the name as the club typed it. And after a reload the answer to
 * "Not coming after all" was headlined "You're down for the next race
 * night", because only the page that sent it remembered it carried no
 * classes.
 */

const CODE = "a1b2c3d4e5f60718293a";
// The fake page's own clock is 2026-10-22T19:00Z.
const PAGE_LATER = "2026-10-23T19:00:00Z";

function night(over: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    day: "2026-11-18",
    title: "Club Night",
    when: "Tue 18 Nov",
    venue: "Clubhouse",
    classes: ["Juniors", "Stock", "Mod"],
    firstRace: "19:00",
    entriesClose: "18:30",
    off: false,
    offText: "",
    takingEntries: true,
    until: PAGE_LATER,
    ...over,
  };
}

async function openMe(nn: Record<string, unknown>, asks: Record<string, unknown>[] = []): Promise<{ page: FakePage; sent: Record<string, unknown>[] }> {
  const sent: Record<string, unknown>[] = [];
  const live = { at: "2026-10-22T19:00:00Z", next: { race: "Q3 Heat 1", away: 2, slot: 4 }, asks };
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, async (url, body) => {
    if (url.endsWith("/snapshot/nextnight")) return { status: 200, body: { body: nn } };
    if (url.endsWith("/night")) {
      if (body["part"] === "live") return { status: 200, body: { ok: true, live } };
      return { status: 200, body: { ok: true, night: null, live, me: { name: "Sam Parker" }, asksOpen: true } };
    }
    if (url.endsWith("/driver/ask")) {
      sent.push(body);
      return { status: 200, body: { ok: true, ref: "C0FFEE" } };
    }
    return { status: 404 };
  }, { hash: `#d=${CODE}` });
  page.run(script);
  await page.settle();
  page.eval("meRefresh()");
  await page.settle();
  return { page, sent };
}

function card(page: FakePage): FakeNode {
  return page.get("me-coming");
}

function headline(page: FakePage): string {
  const st = card(page).find((n) => n.getAttribute("role") === "status")[0];
  assert.ok(st, "the card says where the last one stands");
  return st.find((n) => n.tagName === "B")[0]?.textContent ?? "";
}

describe("bug hunt 17: a class with a comma in its name", () => {
  it("goes as one name, its comma a single space, and comes through the relay whole", async () => {
    const { page, sent } = await openMe(night({ classes: ["Buggy, 2WD", "Stock"] }));
    const ticks = card(page).find((n) => n.tagName === "INPUT");
    assert.equal(ticks.length, 2);

    ticks[0]!.checked = true;
    ticks[0]!.fire("change");
    card(page).find((n) => n.tagName === "BUTTON" && n.textContent === "I'm coming")[0]!.click();
    await page.settle();

    // What the laptop's ClassesOf matches "Buggy, 2WD" against.
    assert.deepEqual(sent[0]!["f"], { day: "2026-11-18", classes: "Buggy 2WD" });
    assert.deepEqual(askFieldsFrom("coming", sent[0]!["f"] as Record<string, unknown>, false), {
      ok: true,
      f: { day: "2026-11-18", classes: "Buggy 2WD" },
    });
  });
});

describe("bug hunt 17: the headline over an answer this page did not send", () => {
  it("an approved 'not coming after all' reads as off the list after a reload", async () => {
    const { page } = await openMe(night(), [
      { ref: "AAAAAA", kind: "coming", state: "approved", said: "You're down for Tue 18 Nov: Stock. Check in when you arrive.", at: "2026-10-22T18:50:00Z" },
      { ref: "BBBBBB", kind: "coming", state: "approved", said: "Taken off Tue 18 Nov. Hope to see you at the next one.", at: "2026-10-22T18:55:00Z" },
    ]);

    assert.equal(headline(page), "You're off the list");
    assert.match(card(page).textContent, /Taken off Tue 18 Nov/);
  });

  it("an approved 'I'm coming' still reads as down", async () => {
    const { page } = await openMe(night(), [
      { ref: "AAAAAA", kind: "coming", state: "approved", said: "You're down for Tue 18 Nov: Stock. Check in when you arrive.", at: "2026-10-22T18:50:00Z" },
    ]);

    assert.equal(headline(page), "You're down for the next race night");
  });

  it("what this page sent still wins over the words", async () => {
    const { page } = await openMe(night());

    card(page).find((n) => n.tagName === "BUTTON" && n.textContent === "Not coming after all")[0]!.click();
    await page.settle();
    // The laptop's answer arrives for the ask this page sent, in words this page does not know.
    page.eval("live.asks = [{ ref: 'C0FFEE', kind: 'coming', state: 'approved', said: 'Done.' }]; meComingCard();");

    assert.equal(headline(page), "You're off the list");
  });
});
