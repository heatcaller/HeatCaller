import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { homePage, livePage } from "../src/pages.js";
import { socialFrom, socialHostFits } from "../src/site.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";

/**
 * DRIVER PHONE PLAN, STEP E (14 Nov 2026): pinch-zoom, text size and high
 * contrast (item 6), the accessibility checks (item 12) and each social link
 * once, where it says (item 23).
 */

/** WCAG contrast of two #RRGGBB colours. */
function contrast(a: string, b: string): number {
  const lum = (hex: string) => {
    const [r, g, bl] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
    return 0.2126 * r! + 0.7152 * g! + 0.0722 * bl!;
  };
  const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p);
  return (x! + 0.05) / (y! + 0.05);
}

/** The tokens one high-contrast block of the page's style sets. */
function tokens(html: string, selector: RegExp): Record<string, string> {
  const block = selector.exec(html)?.[1] ?? "";
  return Object.fromEntries([...block.matchAll(/--([a-z-]+):\s*(#[0-9A-Fa-f]{6})/g)].map((m) => [m[1]!, m[2]!]));
}

describe("high contrast passes on every pair (item 6)", () => {
  const html = livePage("acme-rc", "Acme RC");
  for (const [name, re] of [["dark", /html\.hc \{([^}]*)\}/], ["light", /html\.hc\.light, html\.hc\.hcl \{([^}]*)\}/]] as const) {
    it(`${name}: text 7:1, colours and controls 4.5:1`, () => {
      const t = tokens(html, re);
      assert.ok(t["bg"] && t["text"], "the block is there");
      assert.ok(contrast(t["text"]!, t["bg"]!) >= 7, "text");
      assert.ok(contrast(t["muted"]!, t["bg"]!) >= 7, "muted text");
      for (const k of ["primary", "accent", "good", "bad", "warn", "border"]) {
        assert.ok(contrast(t[k]!, t["bg"]!) >= 4.5, `${k} on the background: ${contrast(t[k]!, t["bg"]!).toFixed(2)}`);
      }
      assert.ok(contrast(t["on-primary"]!, t["primary"]!) >= 4.5, "words on a primary button");
    });
  }
});

describe("text size and high contrast are the phone's choice (item 6)", () => {
  async function open(stored: Record<string, string>): Promise<FakePage> {
    const { html, script } = pageParts(homePage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async () => ({ status: 404 }), {});
    for (const [k, v] of Object.entries(stored)) page.store.set(k, v);
    page.run(script);
    await page.settle();
    return page;
  }

  it("puts the reading controls in every footer", () => {
    assert.match(homePage("acme-rc", "Acme RC"), /<span class="hcread" id="hcread" role="group" aria-label="Reading">/);
    assert.match(livePage("acme-rc", "Acme RC"), /data-ts="130"[^>]*>A\+\+<\/button><button type="button" data-hc="1">High contrast<\/button>/);
  });

  it("applies what the phone chose", async () => {
    const page = await open({ "hg-textsize": "130", "hg-contrast": "light" });
    assert.equal(page.eval("document.documentElement.getAttribute('data-ts')"), "130");
    assert.equal(page.eval("hcReading().hc"), "light");
  });

  it("scales the page's text with the choice, and has a way back from a zoom", () => {
    const html = homePage("acme-rc", "Acme RC");
    assert.match(html, /html\[data-ts="130"\] \{ --ts: 1\.3; \}/);
    assert.match(html, /body \{ font-size: calc\(16px \* var\(--ts, 1\)\); \}/);
    assert.match(html, /button\.textContent = 'Fit to screen';/);
  });
});

describe("each social link goes where it says, once (item 23)", () => {
  it("drops a link not on its service's own address, and an address already shown", () => {
    const fb = "https://facebook.com/upstateminiz";
    const links = socialFrom([
      { kind: "website", url: "https://upstateminiz.com/" },
      { kind: "facebook", url: fb },
      { kind: "instagram", url: fb },
      { kind: "youtube", url: fb },
      { kind: "x", url: "https://twitter.com/upstateminiz" },
      { kind: "discord", url: fb },
    ]);
    assert.deepEqual(links.map((l) => l.kind), ["website", "facebook", "x"]);
  });

  it("knows each service's hosts, subdomains included", () => {
    assert.equal(socialHostFits("facebook", "https://m.facebook.com/club"), true);
    assert.equal(socialHostFits("youtube", "https://youtu.be/abc"), true);
    assert.equal(socialHostFits("instagram", "https://facebook.com/club"), false);
    assert.equal(socialHostFits("instagram", "https://notinstagram.com/club"), false);
    assert.equal(socialHostFits("website", "https://anywhere.example/"), true);
  });
});

describe("the pages a screen reader reads (item 12)", () => {
  it("marks the tab of the page it is on, and says a driver's race when it changes", () => {
    assert.match(livePage("acme-rc", "Acme RC"), /<a href="\/c\/acme-rc\/live" class="lv" aria-current="page">/);
    assert.match(livePage("acme-rc", "Acme RC"), /aria-live="polite"/);
  });
});
