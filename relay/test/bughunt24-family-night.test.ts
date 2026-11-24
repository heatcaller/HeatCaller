import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, it } from "node:test";

/**
 * BUG HUNT 24 (23 Nov 2026) #356: the family page headed the last filed night
 * "Tonight" even when it was last week. The page now heads an older night
 * "Last time out", but only if family/read tells it which night it is.
 */

const server = () => readFileSync(fileURLToPath(new URL("../src/server.js", import.meta.url)), "utf8");

describe("bug hunt 24 #356: family/read says which night it is", () => {
  it("the 200 answer carries the night's day, checked as a plain date", () => {
    const src = server();
    const at = src.indexOf("familyLabelFrom(link");
    assert.ok(at > 0, "family/read's answer is where it was");
    const answer = src.slice(at, at + 1500);
    assert.match(answer, /night: typeof nb\["night"\] === "string"/);
    assert.match(answer, /\\d\{4\}-\\d\{2\}-\\d\{2\}/);
  });
});
