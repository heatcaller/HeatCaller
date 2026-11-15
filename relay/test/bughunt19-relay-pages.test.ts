import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { after, before, describe, it } from "node:test";
import { fileURLToPath } from "node:url";
import { createContext, runInContext } from "node:vm";
import { CLOCK_JS } from "../src/clock.js";
import { ME_JS } from "../src/mespaces.js";
import { PAGE_FILES } from "../src/pagebuild.js";
import { homePage } from "../src/pages.js";
import { socialFrom } from "../src/site.js";

function fn(source: string, name: string): string {
  const match = new RegExp(`function ${name}\\([^)]*\\) \\{[\\s\\S]*?\\n\\}`).exec(source);
  assert.ok(match, `the script has ${name}()`);
  return match[0];
}

/**
 * BUG HUNT 19, #36: Me's first tab (and Home's "Last race night" heading)
 * built a night's day as noon in the phone's zone, then CLOCK_JS drew it in
 * the club's. A phone in Sydney looking at a Los Angeles club's 18 Nov
 * night read "Tue 17 Nov".
 */
describe("a past night's day when the phone is 12+ hours from the club", () => {
  let was: string | undefined;

  before(() => {
    was = process.env.TZ;
    process.env.TZ = "Australia/Sydney";
  });

  after(() => {
    if (was === undefined) delete process.env.TZ;
    else process.env.TZ = was;
  });

  function clubZone(extra: Record<string, unknown>, source: string): Record<string, unknown> {
    const context = createContext({
      ...extra,
      document: {
        documentElement: { getAttribute: (name: string) => (name === "data-tz" ? "America/Los_Angeles" : null) },
        querySelector: (extra as { querySelector?: unknown }).querySelector ?? (() => null),
      },
    });
    runInContext(CLOCK_JS + "\n" + source, context);
    return context as Record<string, unknown>;
  }

  it("labels Me's first tab with the night's own day", () => {
    const button = { textContent: "Tonight" };
    const page = clubZone(
      { querySelector: () => button, night: { night: "2026-11-18" }, hcIsTonight: () => false },
      fn(ME_JS, "meTonightLabel"),
    );
    (page.meTonightLabel as () => void)();

    assert.match(button.textContent, /18/);
    assert.match(button.textContent, /Wed/);
  });

  it("heads Home's last race night with the night's own day", () => {
    const page = clubZone({}, fn(homePage("acme-rc", "Acme RC"), "nightName"));
    const name = (page.nightName as (iso: string) => string)("2026-11-18");

    assert.match(name, /18/);
    assert.match(name, /Wed/);
  });
});

/**
 * BUG HUNT 19, #37: the build fingerprint hashed only the page files, not
 * the modules they join into their scripts when the relay runs (clock.js,
 * site.js, sun.js), so a deploy changing only CLOCK_JS was not a new build.
 */
describe("the pages' build fingerprint", () => {
  // push.js is left out: its own imports (the store) are relay code, not
  // anything a page draws with.
  it("covers every module the page builders import", () => {
    for (const file of ["./pages.js", "./mespaces.js", "./garagepage.js"]) {
      const text = readFileSync(fileURLToPath(new URL(`../src/${file}`, import.meta.url)), "utf8");
      for (const m of text.matchAll(/^import\s+(?!type\b)[^;]*?from\s+"(\.\/[^"]+)"/gm)) {
        const dep = m[1]!;
        if (dep === "./pagebuild.js") continue;
        assert.ok(PAGE_FILES.includes(dep), `${file} imports ${dep}, which the fingerprint must hash`);
      }
    }
  });
});

/**
 * BUG HUNT 19, #42: Discord still redirects its older discordapp.com invite
 * address, but the relay dropped those links, so the club's Discord icon
 * vanished without a word.
 */
describe("a club's Discord link on discordapp.com", () => {
  it("is kept", () => {
    const links = socialFrom([{ kind: "discord", url: "https://discordapp.com/invite/abc123" }]);

    assert.equal(links.length, 1);
    assert.equal(links[0]?.kind, "discord");
  });
});
