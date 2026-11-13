import assert from "node:assert/strict";
import { createContext, runInContext } from "node:vm";
import { describe, it } from "node:test";
import { CLOCK_JS, clockAttr } from "../src/clock.js";
import { lookFrom } from "../src/site.js";
import type { StoredDocument } from "../src/types.js";

/** The clock script run against an <html> carrying the given data-clock. */
function clockFor(attr: string | null): { h12: unknown; hm: (s: unknown) => string } {
  const context = createContext({
    document: { documentElement: { getAttribute: (name: string) => (name === "data-clock" ? attr : null) } },
  });

  runInContext(CLOCK_JS, context);

  return {
    h12: runInContext("HC_H12", context),
    hm: (s: unknown) => runInContext("hcHm(s)", Object.assign(context, { s })) as string,
  };
}

describe("the club's 12/24-hour choice on its pages (12 Nov 2026)", () => {
  it("comes from the site document, and is no choice when an older laptop sent none", () => {
    const site = (body: unknown) => ({ body } as unknown as StoredDocument);

    assert.equal(lookFrom(site({ clock24: true }), undefined).clock24, true);
    assert.equal(lookFrom(site({ clock24: false }), undefined).clock24, false);
    assert.equal(lookFrom(site({ clock24: "yes" }), undefined).clock24, undefined);
    assert.equal(lookFrom(site({}), undefined).clock24, undefined);
  });

  it("is written on the page's <html>", () => {
    assert.equal(clockAttr(true), ' data-clock="24"');
    assert.equal(clockAttr(false), ' data-clock="12"');
    assert.equal(clockAttr(undefined), "");
  });

  it("turns the laptop's HH:mm into 12-hour only when the club asked for it", () => {
    const twelve = clockFor("12");
    assert.equal(twelve.h12, true);
    assert.equal(twelve.hm("19:05"), "7:05 PM");
    assert.equal(twelve.hm("00:30"), "12:30 AM");
    assert.equal(twelve.hm("12:00"), "12:00 PM");
    assert.equal(twelve.hm("about 7"), "about 7");
    assert.equal(twelve.hm(undefined), "");

    const twentyFour = clockFor("24");
    assert.equal(twentyFour.h12, false);
    assert.equal(twentyFour.hm("19:05"), "19:05");

    const none = clockFor(null);
    assert.equal(none.h12, undefined);
    assert.equal(none.hm("19:05"), "19:05");
  });

  it("leaves the phone's locale alone when there is no page at all", () => {
    const context = createContext({});
    runInContext(CLOCK_JS, context);
    assert.equal(runInContext("HC_H12", context), undefined);
  });
});
