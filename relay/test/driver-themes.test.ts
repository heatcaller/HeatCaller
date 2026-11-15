import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { createContext, runInContext } from "node:vm";
import { PAGE_COUNT_EVENTS } from "../src/pagecounts.js";
import { appShellPage, DEFAULT_CONTEXT, DRIVER_THEMES, driverThemeOf, familyPage, homePage, livePage, TEAM_SWATCHES, type PageContext } from "../src/pages.js";
import { themeCookieOf } from "../src/server.js";
import { DEFAULT_LOOK, lookFrom } from "../src/site.js";
import type { StoredDocument } from "../src/types.js";

/**
 * DRIVER THEMES (15 Nov 2026, Asher: "plan the ability for the driver to
 * theme the relay. add a button on the bottom of the screens next to the
 * high contrast", "theme doesn't follow", "club driver can change the look
 * should be on by default").
 */

function luminance(hex: string): number {
  const n = parseInt(hex.slice(1), 16);
  const c = [(n >> 16) & 255, (n >> 8) & 255, n & 255].map((v) => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * c[0]! + 0.7152 * c[1]! + 0.0722 * c[2]!;
}

function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi! + 0.05) / (lo! + 0.05);
}

const TEXT_ON = ["--text", "--muted", "--primary", "--accent", "--good", "--bad", "--warn"];

describe("every driver theme reads at 4.5:1 or more", () => {
  for (const theme of DRIVER_THEMES) {
    it(theme.name, () => {
      const v = theme.vars;
      for (const fg of TEXT_ON) {
        for (const bg of ["--bg", "--surface", "--row-alt"]) {
          const ratio = contrast(v[fg]!, v[bg]!);
          assert.ok(ratio >= 4.5, `${theme.id}: ${fg} ${v[fg]} on ${bg} ${v[bg]} is ${ratio.toFixed(2)}:1`);
        }
      }
      const button = contrast(v["--on-primary"]!, v["--primary"]!);
      assert.ok(button >= 4.5, `${theme.id}: on-primary on primary is ${button.toFixed(2)}:1`);
    });
  }

  it("every team colour, on Night, with black text on it", () => {
    const night = DRIVER_THEMES.find((t) => t.id === "night")!.vars;
    for (const s of TEAM_SWATCHES) {
      for (const bg of ["--bg", "--surface", "--row-alt"]) {
        const ratio = contrast(s.hex, night[bg]!);
        assert.ok(ratio >= 4.5, `${s.id} on ${bg} is ${ratio.toFixed(2)}:1`);
      }
      const on = contrast("#000000", s.hex);
      assert.ok(on >= 4.5, `black on ${s.id} is ${on.toFixed(2)}:1`);
    }
  });
});

describe("the Look button and the club's switch", () => {
  const site = (body: unknown) => ({ body } as unknown as StoredDocument);
  const off: PageContext = { ...DEFAULT_CONTEXT, look: { ...DEFAULT_LOOK, driverThemes: false } };

  it("is on unless the club's site document says false", () => {
    assert.equal(lookFrom(site({}), undefined).driverThemes, true);
    assert.equal(lookFrom(site({ driverThemes: true }), undefined).driverThemes, true);
    assert.equal(lookFrom(site({ driverThemes: false }), undefined).driverThemes, false);
    assert.equal(lookFrom(undefined, undefined).driverThemes, true);
    assert.equal(DEFAULT_LOOK.driverThemes, true);
  });

  it("sits next to High contrast on every club page", () => {
    for (const html of [homePage("acme-rc", "Acme RC"), livePage("acme-rc", "Acme RC")]) {
      assert.match(html, /data-hc="1" aria-label="High contrast">Contrast<\/button><button type="button" data-look="1" aria-haspopup="dialog">Look<\/button><\/span>/);
      assert.doesNotMatch(html, /<html [^>]*data-nothemes/);
    }
  });

  it("is gone when the club turns it off, and the page says so to its script", () => {
    const html = homePage("acme-rc", "Acme RC", off);
    assert.doesNotMatch(html, /data-look="1"/);
    assert.match(html, /<html [^>]*data-nothemes/);
    assert.match(appShellPage("acme-rc", "Acme RC", off), /<html [^>]*data-nothemes/);
  });

  it("counts which look a phone picked, nothing more", () => {
    for (const e of ["theme_club", "theme_night", "theme_daylight", "theme_pitlane", "theme_paddock", "theme_carbon", "theme_team"]) {
      assert.ok(PAGE_COUNT_EVENTS.includes(e), e);
    }
  });
});

describe("the page's script puts the theme on <html>", () => {
  function fn(source: string, name: string): string {
    const match = new RegExp(`function ${name}\\([^)]*\\) \\{[\\s\\S]*?\\n\\}`).exec(source);
    assert.ok(match, `the script has ${name}()`);
    return match[0];
  }

  const script = /<script[^>]*>([\s\S]*)<\/script>/.exec(homePage("acme-rc", "Acme RC"))![1]!;
  const list = /const HC_THEMES = (\{[\s\S]*?\});\n/.exec(script)![1]!;

  function run(stored: string | null, opts: { hc?: boolean; nothemes?: boolean; scheme?: string; cookie?: string } = {}) {
    const jar = { cookie: opts.cookie ?? "" };
    const set = new Set([opts.scheme === "light" ? "light" : "dark"]);
    const metas: Record<string, Record<string, string>> = {
      'meta[name="theme-color"]': { content: "#0F0722" },
      'meta[name="color-scheme"]': { content: opts.scheme ?? "dark" },
    };
    const meta = (attrs: Record<string, string>) => ({
      hasAttribute: (n: string) => n in attrs,
      getAttribute: (n: string) => (n in attrs ? attrs[n] : null),
      setAttribute: (n: string, v: string) => { attrs[n] = v; },
    });
    const context = createContext({
      localStorage: { getItem: (k: string) => (k === "hg-theme" ? stored : null) },
      document: {
        documentElement: {
          hasAttribute: (n: string) => n === "data-nothemes" && opts.nothemes === true,
          getAttribute: () => null,
          classList: {
            contains: (c: string) => set.has(c),
            toggle: (c: string, on: boolean) => (on ? set.add(c) : set.delete(c)),
          },
        },
        querySelector: (q: string) => (q in metas ? meta(metas[q]!) : null),
        get cookie() { return jar.cookie; },
        set cookie(v: string) { jar.cookie = v; },
      },
    });
    runInContext(`const HC_TH_KEY = 'hg-theme'; const HC_THEMES = ${list};\n`
      + [/function hcThemesOn\(\) \{[^\n]*\}/.exec(script)![0], `let HC_CLUB_LIGHT = ${opts.scheme === "light"};`, fn(script, "hcTheme"), fn(script, "hcThemeCookie"), fn(script, "hcApplyTheme")].join("\n")
      + `\nhcApplyTheme(${opts.hc === true});`, context);
    return {
      classes: [...set].sort(),
      themeColor: metas['meta[name="theme-color"]']!["content"],
      scheme: metas['meta[name="color-scheme"]']!["content"],
      cookie: jar.cookie,
    };
  }

  it("puts a team colour on Daylight too", () => {
    assert.deepEqual(run("daylight:teal").classes, ["light", "tc-teal", "th-daylight"]);
    assert.deepEqual(run("pitlane:teal").classes, ["dark", "th-pitlane"]);
  });

  it("keeps the theme in a cookie too, so the next page is drawn in it, and clears it for the club's colours", () => {
    assert.match(run("daylight:teal").cookie, /^hg-theme=daylight:teal; path=\/; max-age=31536000; SameSite=Lax$/);
    assert.match(run(null, { cookie: "hg-theme=pitlane" }).cookie, /^hg-theme=; path=\/; max-age=0/);
    assert.equal(run("pitlane", { cookie: "a=1; hg-theme=pitlane" }).cookie, "a=1; hg-theme=pitlane");
    // Where themes are off (the family page) the phone's cookie is left alone.
    assert.equal(run(null, { nothemes: true, cookie: "hg-theme=pitlane" }).cookie, "hg-theme=pitlane");
  });


  it("puts the phone's theme on, light or dark to match, with the phone's bar in its colour", () => {
    assert.deepEqual({ ...run("pitlane"), cookie: undefined }, { cookie: undefined, classes: ["dark", "th-pitlane"], themeColor: "#17181A", scheme: "dark" });
    assert.deepEqual({ ...run("daylight"), cookie: undefined }, { cookie: undefined, classes: ["light", "th-daylight"], themeColor: "#F7F7F4", scheme: "light" });
    assert.deepEqual(run("night:teal").classes, ["dark", "tc-teal", "th-night"]);
  });

  it("a dark theme on a light club's pages goes dark", () => {
    assert.deepEqual(run("carbon", { scheme: "light" }).classes, ["dark", "th-carbon"]);
  });

  it("leaves the club's look alone with nothing kept, something unknown, or themes off", () => {
    for (const r of [run(null), run("neon"), run("pitlane", { nothemes: true })]) {
      assert.equal(r.themeColor, "#0F0722");
    }
    assert.deepEqual(run(null).classes, ["dark"]);
    assert.deepEqual(run("neon").classes, ["dark"]);
    assert.deepEqual(run("night:mauve").classes, ["dark", "th-night"]);
    assert.deepEqual(run("pitlane", { nothemes: true }).classes, ["dark"]);
  });

  it("under High contrast keeps the club's own light or dark and the club's bar colour", () => {
    assert.deepEqual({ ...run("daylight", { hc: true }), cookie: undefined }, { cookie: undefined, classes: ["dark", "th-daylight"], themeColor: "#0F0722", scheme: "dark" });
  });
});

describe("team colours on Daylight", () => {
  it("each deeper shade reads on Daylight and takes white text", () => {
    const day = DRIVER_THEMES.find((t) => t.id === "daylight")!.vars;
    for (const s of TEAM_SWATCHES) {
      for (const bg of ["--bg", "--surface", "--row-alt"]) {
        const ratio = contrast(s.onLight, day[bg]!);
        assert.ok(ratio >= 4.5, `${s.id} on Daylight ${bg} is ${ratio.toFixed(2)}:1`);
      }
      const on = contrast("#FFFFFF", s.onLight);
      assert.ok(on >= 4.5, `white on ${s.id} is ${on.toFixed(2)}:1`);
    }
    assert.match(homePage("acme-rc", "Acme RC"), /html\.th-daylight\.tc-teal \{ --primary: #00695C; --on-primary: #FFFFFF;/);
  });
});

describe("drawn in the phone's theme from the start", () => {
  const themed = (theme: string): PageContext => ({ ...DEFAULT_CONTEXT, theme });

  it("reads only the theme cookie's own words", () => {
    assert.equal(themeCookieOf("hg-theme=pitlane"), "pitlane");
    assert.equal(themeCookieOf("a=1; hg-theme=daylight:teal; b=2"), "daylight:teal");
    assert.equal(themeCookieOf("hg-theme=<script>"), undefined);
    assert.equal(themeCookieOf(undefined), undefined);
    assert.deepEqual(driverThemeOf("daylight:teal"), { id: "daylight", team: "teal" });
    assert.deepEqual(driverThemeOf("carbon:teal"), { id: "carbon", team: "" });
    assert.equal(driverThemeOf("neon"), null);
  });

  it("puts the theme on <html>, its light or dark, and the club's own scheme beside it", () => {
    const html = homePage("acme-rc", "Acme RC", themed("daylight:teal"));
    assert.match(html, /<html lang="en" class="light synthwave th-daylight tc-teal"[^>]* data-club-scheme="dark">/);
    assert.match(appShellPage("acme-rc", "Acme RC", themed("pitlane")), /<html lang="en" class="hcshell dark th-pitlane"[^>]* data-club-scheme="dark">/);
  });

  it("draws a page as it always was with no cookie or one it doesn't know", () => {
    assert.match(homePage("acme-rc", "Acme RC"), /<html lang="en" class="dark synthwave"[^>]*>/);
    assert.doesNotMatch(homePage("acme-rc", "Acme RC", themed("neon")), /<html [^>]*(data-club-scheme|th-neon)/);
  });

  it("ignores it where the club has themes off", () => {
    const off: PageContext = { ...themed("pitlane"), look: { ...DEFAULT_LOOK, driverThemes: false } };
    assert.doesNotMatch(homePage("acme-rc", "Acme RC", off), /class="[^"]*th-pitlane/);
  });
});

describe("the family page keeps the club's own look", () => {
  it("has no Look button and no theme, cookie or not", () => {
    const html = familyPage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, theme: "pitlane" });
    assert.doesNotMatch(html, /data-look="1"/);
    assert.match(html, /<html [^>]*data-nothemes/);
    assert.doesNotMatch(html, /class="[^"]*th-pitlane/);
  });
});

describe("the Look sheet keeps Tab inside it", () => {
  it("goes round from the last button to the first and back", () => {
    const html = homePage("acme-rc", "Acme RC");
    assert.match(html, /if \(e\.key !== 'Tab'\) return;/);
    assert.match(html, /if \(e\.shiftKey && document\.activeElement === first\) \{ e\.preventDefault\(\); last\.focus\(\); \}/);
  });
});

describe("the theme CSS sits under High contrast", () => {
  it("comes after the club's own and before html.hc", () => {
    const html = homePage("acme-rc", "Acme RC");
    const th = html.indexOf("html.th-night {");
    const hc = html.indexOf("html.hc {");
    const light = html.indexOf("html.light { --warn");
    assert.ok(th > 0 && hc > th, "themes before High contrast");
    assert.ok(light > 0 && th > light, "themes after the club's light rule");
    assert.match(html, /html\.th-night\.tc-teal \{ --primary: #2ED3C6;/);
  });
});
