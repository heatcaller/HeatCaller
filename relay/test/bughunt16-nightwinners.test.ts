import assert from "node:assert/strict";
import { createContext, runInContext } from "node:vm";
import { describe, it } from "node:test";
import { homePage } from "../src/pages.js";
import { pageParts } from "./driverexp-rp1-fakepage.js";

/**
 * Bug hunt 16 (10 Nov 2026): Home's night winners keep the order the races
 * ran, even on a night that runs past midnight. The index lists a night's
 * sessions oldest first; re-sorting by the "HH:mm" start put a 00:15 race
 * ahead of the 19:00 heats.
 */

function winnersOf(sessions: unknown[]): Array<{ label: string }> {
  const { script } = pageParts(homePage("acme-rc", "Acme RC"));
  const helper = /\nfunction winners\(night\) \{[\s\S]*?\n\}\n/.exec(script)?.[0];
  assert.ok(helper, "winners is where the test expects it");
  const context = createContext({});
  runInContext(`${helper}\nthis.winners = winners;`, context);
  const winners = (context as { winners: (n: unknown) => Array<{ label: string }> }).winners;
  // Copy out of the vm's realm so deepEqual compares plain values.
  return JSON.parse(JSON.stringify(winners({ sessions })));
}

describe("Home: the night's winners on a night past midnight", () => {
  it("lists the finals in the order they ran", () => {
    const got = winnersOf([
      { type: "Race", won: "Ann", label: "Heat 1", at: "19:00" },
      { type: "Race", won: "Bo", label: "A Main", at: "23:50" },
      { type: "Race", won: "Cy", label: "B Main", at: "00:15" },
    ]);
    assert.deepEqual(got.map((x) => x.label), ["A Main", "B Main"]);
  });

  it("keeps the last four races, after midnight included, when no race is a main", () => {
    const got = winnersOf([
      { type: "Race", won: "A", label: "Round 1", at: "19:00" },
      { type: "Race", won: "B", label: "Round 2", at: "20:00" },
      { type: "Race", won: "C", label: "Round 3", at: "22:00" },
      { type: "Race", won: "D", label: "Round 4", at: "23:00" },
      { type: "Race", won: "E", label: "Feature", at: "00:10" },
    ]);
    assert.deepEqual(got.map((x) => x.label), ["Round 2", "Round 3", "Round 4", "Feature"]);
  });
});
