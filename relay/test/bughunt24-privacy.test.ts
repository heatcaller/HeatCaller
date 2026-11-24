import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { isJuniorName, namesAJunior, privacyRulesFrom, resultTakenDown } from "../src/privacy.js";

/**
 * BUG HUNT 24 (23 Nov 2026): the relay's own privacy rules.
 */

describe("bug hunt 24: a junior's name straight after a line break is still seen (F369)", () => {
  const rules = privacyRulesFrom({ juniors: ["Mia Kent"] });

  it("finds the name after a line break, a carriage return and a tab", () => {
    assert.equal(namesAJunior(rules, { offText: "Racing is off.\r\nMia Kent has the keys" }), true);
    assert.equal(namesAJunior(rules, { offText: "Winners tonight:\nMia Kent" }), true);
    assert.equal(namesAJunior(rules, { offText: "Up next\tMia Kent" }), true);
    assert.equal(namesAJunior(rules, { offText: "Up next\nmia  kent" }), true);
  });

  it("finds the name after other escaped characters", () => {
    assert.equal(namesAJunior(rules, { caption: "\"Mia Kent\" wins" }), true);
    assert.equal(namesAJunior(rules, { caption: "a\\Mia Kent" }), true);
    assert.equal(namesAJunior(rules, { caption: "x\u0001Mia Kent" }), true);
  });

  it("still keeps to whole words", () => {
    assert.equal(namesAJunior(rules, { offText: "Racing is off.\nMia Kowalski" }), false);
    assert.equal(namesAJunior(rules, { offText: "Racing is off.\nAmia Kent" }), false);
    assert.equal(namesAJunior(rules, { rows: [{ name: "Mia Kent" }] }), true);
  });
});

describe("bug hunt 24: a numbered junior's initials are a junior's name", () => {
  const rules = privacyRulesFrom({ juniors: ["Mia Kent", "M.K."] });

  it("drops a trailing number before looking the name up", () => {
    assert.equal(isJuniorName(rules, "M.K. 2"), true);
    assert.equal(isJuniorName(rules, "M.K. 27"), true);
    assert.equal(isJuniorName(rules, "Mia Kent 3"), true);
    assert.equal(isJuniorName(rules, "M.K."), true);
  });

  it("does not make other names juniors", () => {
    assert.equal(isJuniorName(rules, "J.K. 2"), false);
    assert.equal(isJuniorName(rules, "M.K.2"), false);
    assert.equal(isJuniorName(rules, "7"), false);
    assert.equal(isJuniorName(rules, "Sam Lee"), false);
  });
});

describe("bug hunt 24: a result is judged by its race-night year (F370)", () => {
  const rules = privacyRulesFrom({ keepYears: 2, fromYear: 2025 });

  it("takes down a New Year's Eve result whose UTC start is the next year", () => {
    // 8pm EST on 31 Dec 2024 is 1am UTC on 1 Jan 2025.
    assert.equal(resultTakenDown(rules, { startedUtc: "2025-01-01T01:00:00Z", year: "2024" }), true);
  });

  it("keeps a New Year's Day result whose UTC start is the year before", () => {
    // 9am AEDT on 1 Jan 2025 is 10pm UTC on 31 Dec 2024.
    assert.equal(resultTakenDown(rules, { startedUtc: "2024-12-31T22:00:00Z", year: "2025" }), false);
  });

  it("reads a year sent as a number too", () => {
    assert.equal(resultTakenDown(rules, { startedUtc: "2025-01-01T01:00:00Z", year: 2024 }), true);
    assert.equal(resultTakenDown(rules, { startedUtc: "2024-12-31T22:00:00Z", year: 2025 }), false);
  });

  it("falls back to the UTC year for a result without one (an older laptop)", () => {
    assert.equal(resultTakenDown(rules, { startedUtc: "2025-01-01T01:00:00Z" }), false);
    assert.equal(resultTakenDown(rules, { startedUtc: "2024-12-31T22:00:00Z" }), true);
    assert.equal(resultTakenDown(rules, { startedUtc: "2024-12-31T22:00:00Z", year: "24" }), true);
  });

  it("treats year 1 as unknown, not old", () => {
    assert.equal(resultTakenDown(rules, { startedUtc: "0001-01-01T00:00:00Z", year: "0001" }), false);
  });
});
