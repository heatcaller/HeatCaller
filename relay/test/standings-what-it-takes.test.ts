import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { standingsPage } from "../src/pages.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";

/**
 * WHAT IT TAKES (4 Nov 2026, from the relay roadmap): with the season's
 * length set, the points still to win and who can still catch the leader;
 * nothing for a season whose length the club has not set.
 */
describe("Standings: what it takes", () => {
  async function takes(season: Record<string, unknown>): Promise<string> {
    const { html, script } = pageParts(standingsPage("acme-rc", "Acme RC"));
    const p = new FakePage(html, async () => ({ status: 404 }));
    p.run(script);
    await p.settle();
    return String(p.eval(`whatItTakes(${JSON.stringify(season)})`));
  }

  const rows = [
    { pos: 1, name: "Sam P.", points: 180 },
    { pos: 2, name: "Jordan M.", points: 150 },
    { pos: 3, name: "Riley T.", points: 80 },
  ];

  it("says the points left and who can still win it", async () => {
    const said = await takes({ planned: 8, held: 6, most: 50, bestOf: 0, standings: rows });
    assert.match(said, /2 rounds to go, up to 100 points still to win\./);
    assert.match(said, /Jordan M\.<\/span> <span class="wt-gap">30 behind<\/span> <b class="wt-in">can still win it/);
    assert.match(said, /Riley T\.<\/span> <span class="wt-gap">100 behind<\/span> <b class="wt-in">can still win it/, "level on points with all to play for");
    assert.doesNotMatch(said, /has the title won/);
  });

  it("crowns the leader when nobody can catch them", async () => {
    const said = await takes({ planned: 8, held: 7, most: 25, bestOf: 0, standings: rows });
    assert.match(said, /has the title won: nobody can make up 30 points/);
    assert.match(said, /Riley T\.[\s\S]*cannot catch the leader/);
  });

  it("says nothing without the season's length, and warns about dropped rounds", async () => {
    assert.equal(await takes({ planned: 0, held: 6, most: 50, standings: rows }), "");
    assert.match(await takes({ planned: 8, held: 6, most: 50, bestOf: 6, standings: rows }), /Best 6 rounds count/);
  });
});
