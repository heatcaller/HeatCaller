import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { ME_JS } from "../src/mespaces.js";

// 15 Nov 2026, Asher: "can we theme the popup on relay ask the desk to hold
// your next race for 5 more minutes and my car is broken".
describe("Me: Need 5 more minutes and Car broken ask on Me's own sheet", () => {
  const at = ME_JS.indexOf("function meRaceAsk(kind)");
  const body = ME_JS.slice(at, ME_JS.indexOf("\n}\n", at) + 2);

  const run = (kind: string, sheetOnPage: boolean) => {
    const opened: { title: string; buttons: { text: string; cls: string; onTap: () => void }[]; words: string[] }[] = [];
    const sent: string[] = [];
    let confirmed = 0;
    const el = (_tag: string, _cls: string, text: string) => ({ text });
    const tapButton = (text: string, cls: string, onTap: () => void) => ({ text, cls, onTap });
    const meOpenSheet = (title: string, fill: (b: unknown) => void) => {
      const got = { title, buttons: [] as { text: string; cls: string; onTap: () => void }[], words: [] as string[] };
      fill({ appendChild: (x: { text: string; cls?: string; onTap?: () => void }) => {
        if (x.onTap) got.buttons.push(x as { text: string; cls: string; onTap: () => void }); else got.words.push(x.text);
      } });
      opened.push(got);
    };
    new Function("document", "window", "el", "tapButton", "meOpenSheet", "meCloseSheet", "meRaceAskSend",
      body + "\nmeRaceAsk(" + JSON.stringify(kind) + ");")(
      { getElementById: () => (sheetOnPage ? {} : null) },
      { confirm: () => { confirmed++; return true; } },
      el, tapButton, meOpenSheet, () => {}, (k: string) => sent.push(k));
    return { opened, sent, confirmed };
  };

  it("asks for 5 more minutes on the sheet, in the page's own colours, not the browser's confirm box", () => {
    const r = run("late", true);
    assert.equal(r.confirmed, 0);
    assert.equal(r.opened.length, 1);
    assert.equal(r.opened[0]!.title, "Need 5 more minutes?");
    assert.match(r.opened[0]!.words[0]!, /hold your next race for 5 more minutes/);
    assert.deepEqual(r.opened[0]!.buttons.map((b) => b.text), ["Yes, hold my race", "Never mind"]);
    assert.deepEqual(r.sent, []);
    r.opened[0]!.buttons[0]!.onTap();
    assert.deepEqual(r.sent, ["late"]);
  });

  it("asks Car broken on the sheet too, and Never mind sends nothing", () => {
    const r = run("skip", true);
    assert.equal(r.opened[0]!.title, "Car broken?");
    assert.deepEqual(r.opened[0]!.buttons.map((b) => b.text), ["Yes, skip my next race", "Never mind"]);
    r.opened[0]!.buttons[1]!.onTap();
    assert.deepEqual(r.sent, []);
  });

  it("falls back to the confirm box only where the sheet isn't on the page", () => {
    const r = run("late", false);
    assert.equal(r.opened.length, 0);
    assert.equal(r.confirmed, 1);
    assert.deepEqual(r.sent, ["late"]);
  });
});
