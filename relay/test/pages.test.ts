import assert from "node:assert/strict";
import { Script, createContext, runInContext } from "node:vm";
import { after, before, describe, it } from "node:test";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

const PAGES = ["live", "schedule", "results", "standings", "badges", "tracks"];

/** The one inline script a page carries. */
function scriptOf(html: string): string {
  const match = /<script[^>]*>([\s\S]*)<\/script>/.exec(html);
  assert.ok(match, "every page carries its script inline");
  return match[1] as string;
}

describe("public pages", () => {
  let harness: Harness;

  before(async () => {
    harness = await start();
  });

  after(async () => {
    await harness.close();
  });

  it("serves a home page at the short address rather than redirecting", async () => {
    const answer = await call(harness, "GET", "/c/acme-rc", { noRedirect: true });

    assert.equal(answer.status, 200);
    assert.match(answer.headers["content-type"] ?? "", /text\/html/);
    assert.match(answer.text, /Acme RC/);
    assert.match(answer.text, /href="\/c\/acme-rc\/schedule"/);
  });

  for (const page of PAGES) {
    it(`serves /c/<club>/${page}`, async () => {
      const answer = await call(harness, "GET", `/c/acme-rc/${page}`);

      assert.equal(answer.status, 200);
      assert.match(answer.text, /aria-current="page"/);

      // Every tab on every page, so nobody is ever stranded on one - and
      // since 30 Oct 2026 (relay navigation round 5) Standings, Tracks and
      // Achievements are Results' own tabs, on Results and on each other.
      const ofResults = ["results", "standings", "badges", "tracks"];
      const expected = ofResults.includes(page) ? PAGES : PAGES.filter((other) => !ofResults.includes(other) || other === "results");

      for (const other of expected) {
        assert.ok(answer.text.includes(`href="/c/acme-rc/${other}"`), `${page} links to ${other}`);
      }
    });
  }

  // The pages' JavaScript lives inside TypeScript template literals, which
  // eat one level of backslashes. A regex written the obvious way arrives
  // in the browser broken, and a syntax error blanks the entire page -
  // so every page's script is parsed here, exactly as a browser gets it.
  for (const page of ["", ...PAGES]) {
    it(`ships a script that parses: /c/<club>/${page}`, async () => {
      const answer = await call(harness, "GET", `/c/acme-rc${page === "" ? "" : "/" + page}`);

      assert.doesNotThrow(() => new Script(scriptOf(answer.text)));
    });
  }

  it("refuses anything but a data: image as an image source", async () => {
    const answer = await call(harness, "GET", "/c/acme-rc/badges");
    const script = scriptOf(answer.text);

    const esc = /const esc = [\s\S]*?\)\);\n/.exec(script)?.[0];
    const img = /function img\([\s\S]*?\n\}\n/.exec(script)?.[0];

    assert.ok(esc && img, "the escaping and image helpers are where the test expects them");

    const context = createContext({});
    runInContext(`${esc}\n${img}\nthis.img = img;`, context);
    const render = (context as { img: (src: unknown, cls: string, alt: string) => string }).img;

    assert.match(render("data:image/png;base64,iVBORw0KGgo=", "art", "x"), /^<img /);
    assert.match(render("data:image/jpeg;base64,/9j/4AAQ", "", "x"), /^<img /);
    assert.equal(render("javascript:alert(1)", "art", "x"), "");
    assert.equal(render('data:image/png;base64,AAAA" onerror="alert(1)', "art", "x"), "");
    assert.equal(render("https://example.com/a.png", "art", "x"), "");
    assert.equal(render(undefined, "art", "x"), "");
  });

  it("answers an unknown page or club with a 404", async () => {
    assert.equal((await call(harness, "GET", "/c/acme-rc/nope")).status, 404);
    assert.equal((await call(harness, "GET", "/c/acme-rc/toString")).status, 404);
    assert.equal((await call(harness, "GET", "/c/nobody-here")).status, 404);
  });
});

describe("the new channels", () => {
  let harness: Harness;

  before(async () => {
    harness = await start();
  });

  after(async () => {
    await harness.close();
  });

  for (const channel of ["programme", "badges", "achievements", "tracks"]) {
    it(`publishes and serves ${channel}`, async () => {
      const posted = await call(harness, "POST", `/api/v1/c/acme-rc/publish/${channel}?seq=1`, {
        token: ACME_TOKEN,
        json: { hello: channel },
      });

      assert.equal(posted.status, 200);

      const read = await call(harness, "GET", `/api/v1/c/acme-rc/snapshot/${channel}`);

      assert.equal(read.status, 200);
      assert.deepEqual(read.body.body, { hello: channel });
      assert.equal(read.headers["access-control-allow-origin"], "*");
    });
  }
});
