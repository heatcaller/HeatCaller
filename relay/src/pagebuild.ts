import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

/**
 * WHICH BUILD OF THE CLUB'S PAGES THIS IS (14 Nov 2026, driver phone plan
 * items 1 and 21).
 *
 * Once a phone keeps the club's pages for when it has no signal (the
 * service worker in push.ts), it can also keep showing an old page after
 * a deploy. So every page carries this fingerprint (data-build), every
 * snapshot answer says it (x-hc-build), and the worker names its cache
 * after it. When they differ the page offers "Updated - tap to reload",
 * never in the middle of a race; when the worker changes it throws the old
 * cache away.
 *
 * It's a hash of the compiled files the pages are made from, read once
 * when the relay starts, so any change to them is a new build and nothing
 * has to remember to bump a number. Twelve hex characters: enough to tell
 * builds apart, short enough to sit in a header on every answer.
 */
const PAGE_FILES = ["./pages.js", "./push.js", "./mespaces.js", "./garagepage.js"];

export const PAGES_BUILD: string = (() => {
  try {
    const hash = createHash("sha256");

    for (const file of PAGE_FILES) {
      hash.update(readFileSync(fileURLToPath(new URL(file, import.meta.url))));
    }

    return hash.digest("hex").slice(0, 12);
  } catch {
    // Files that cannot be read (an unusual install): one fingerprint per
    // start, which is wrong only in offering a reload after a restart.
    return "s" + Date.now().toString(36);
  }
})();
