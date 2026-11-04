import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { nightPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer, type FakeNode } from "./driverexp-rp1-fakepage.js";

/**
 * A SECOND TRANSPONDER RACED AS A CAR OF ITS OWN (22 Oct 2026). The laptop
 * puts its races on the driver's own night with `cars[]`, and the page
 * draws one section per car - its facts, report card, races, improvement
 * and progress - while a night without `cars` draws exactly as before.
 */

const CODE = "a1b2c3d4e5f60718293a";
const HEAT1 = "11111111111111111111111111111111";
const HEAT2 = "22222222222222222222222222222222";

async function open(full: unknown): Promise<FakePage> {
  const answer: Answerer = async (url, body) => {
    if (url.endsWith("/night")) return { status: 200, body: body["part"] === "live" ? { ok: true } : full };
    return { status: 404 };
  };
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, answer, { hash: `#d=${CODE}` });
  page.run(script);
  await page.settle();
  return page;
}

const twoCars = {
  ok: true,
  night: {
    driver: "Sam Parker", title: "Club Night", when: "Thursday 22 October 2026", night: "2026-10-22",
    lines: ["Sam won Heat 1 in the main car."],
    coach: ["Main car card line."],
    races: [
      { sid: HEAT1, label: "Heat 1", at: "2026-10-22T18:30:00Z", pos: 1, of: 6, laps: 10, bestMs: 8500 },
      { sid: HEAT2, label: "Heat 2", at: "2026-10-22T19:00:00Z", pos: 4, of: 6, laps: 9, bestMs: 9100, car: "c2" },
    ],
    improve: "Main car improvement.",
    cars: [
      { id: "c1", label: "Main car", name: "B6.4" },
      { id: "c2", label: "Second car", name: "RC10 B7", lines: ["Sam was fourth in Heat 2 in the second car."],
        coach: ["Second car card line."], improve: "Second car improvement." },
    ],
  },
};

function sections(page: FakePage): FakeNode[] {
  return page.get("nx").find((n) => n.tagName === "SECTION");
}

describe("a night of two cars", () => {
  it("draws one section per car, in order, each with its own heading", async () => {
    const page = await open(twoCars);
    const cars = sections(page);

    assert.equal(cars.length, 2);
    assert.match(cars[0]!.textContent, /^Main car · B6\.4/);
    assert.match(cars[1]!.textContent, /^Second car · RC10 B7/);
  });

  it("keeps each car's facts, card, races and improvement in its own section", async () => {
    const page = await open(twoCars);
    const [main, second] = sections(page);

    assert.match(main!.textContent, /won Heat 1 in the main car/);
    assert.match(main!.textContent, /Main car card line/);
    assert.match(main!.textContent, /Heat 1/);
    assert.match(main!.textContent, /Main car improvement/);
    assert.doesNotMatch(main!.textContent, /Heat 2|Second car/);

    assert.match(second!.textContent, /fourth in Heat 2/);
    assert.match(second!.textContent, /Second car card line/);
    assert.match(second!.textContent, /4th of 6/);
    assert.match(second!.textContent, /Second car improvement/);
    assert.doesNotMatch(second!.textContent, /Heat 1|Main car/);
  });

  it("does not draw the first car's facts twice above the sections", async () => {
    const page = await open(twoCars);
    // The box's own markup (the heading) leaves them to the sections.
    const markup = page.get("nt").html;

    assert.doesNotMatch(markup, /won Heat 1 in the main car|Main car card line/);
    assert.match(page.get("nh").html, /Sam Parker/, "their name, at the top of the page");
    assert.equal(sections(page)[0]!.textContent.split("won Heat 1 in the main car").length - 1, 1);
  });

  it("builds every part as text", async () => {
    const evil = structuredClone(twoCars);
    evil.night.cars[1]!.name = "<img src=x onerror=alert(1)>";
    const page = await open(evil);

    assert.match(sections(page)[1]!.textContent, /<img src=x onerror=alert\(1\)>/);
    assert.equal(page.get("nx").find((n) => n.tagName === "IMG").length, 0);
  });

  it("a night of one car has no sections and reads as it always did", async () => {
    const one = structuredClone(twoCars);
    delete (one.night as Record<string, unknown>)["cars"];
    one.night.races = [one.night.races[0]!];
    const page = await open(one);

    assert.equal(sections(page).length, 0);
    assert.match(page.get("nx").textContent, /Your race/);
    assert.match(page.get("nt").html, /won Heat 1 in the main car/);
  });
});
