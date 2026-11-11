import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { runInNewContext } from "node:vm";
import { SERVICE_WORKER_JS } from "../src/push.js";

/**
 * BUG HUNT 15 (10 Nov 2026): tapping an alert while a club page was open
 * only focused that page, so a phone left on Live or Results never showed
 * the night the alert was about. The worker is run here against fake
 * window clients.
 */

const SCOPE = "https://relay.example/c/acme-rc/";
const MYNIGHT = "https://relay.example/c/acme-rc/mynight";

interface FakeClient {
  url: string;
  focused: number;
  navigatedTo: string[];
  focus(): Promise<FakeClient>;
  navigate(to: string): Promise<FakeClient>;
}

function client(url: string, navigateFails = false): FakeClient {
  const c: FakeClient = {
    url,
    focused: 0,
    navigatedTo: [],
    focus: async () => {
      c.focused += 1;
      return c;
    },
    navigate: async (to: string) => {
      if (navigateFails) {
        throw new TypeError("not controlled");
      }

      c.navigatedTo.push(to);
      c.url = to;
      return c;
    },
  };
  return c;
}

/** Taps an alert for `url` with these windows open; what was opened in a new window. */
async function tap(windows: FakeClient[], url = "/c/acme-rc/mynight"): Promise<string[]> {
  const listeners = new Map<string, (e: unknown) => void>();
  const opened: string[] = [];
  const self = {
    addEventListener: (name: string, fn: (e: unknown) => void) => listeners.set(name, fn),
    registration: { scope: SCOPE },
    clients: {
      matchAll: async () => windows,
      openWindow: async (to: string) => {
        opened.push(to);
        return null;
      },
    },
  };
  runInNewContext(SERVICE_WORKER_JS, { self, URL, Promise });
  let waited: Promise<unknown> = Promise.resolve();
  listeners.get("notificationclick")!({
    notification: { close: () => undefined, data: { url } },
    waitUntil: (p: Promise<unknown>) => {
      waited = p;
    },
  });
  await waited;
  return opened;
}

describe("push: a tap on an alert opens the alert's page (Bug Hunt 16)", () => {
  it("takes an open club page to my night, not just back to where it was", async () => {
    const live = client(`${SCOPE}live`);
    const opened = await tap([live]);
    assert.equal(live.focused, 1);
    assert.deepEqual(live.navigatedTo, [MYNIGHT]);
    assert.deepEqual(opened, []);
  });

  it("leaves a page already on my night where it is", async () => {
    const mine = client(MYNIGHT);
    const opened = await tap([mine]);
    assert.equal(mine.focused, 1);
    assert.deepEqual(mine.navigatedTo, []);
    assert.deepEqual(opened, []);
  });

  it("opens my night in a new window when the open page cannot be moved", async () => {
    const stuck = client(`${SCOPE}results`, true);
    const opened = await tap([stuck]);
    assert.deepEqual(opened, [MYNIGHT]);
  });

  it("opens my night when no club page is open", async () => {
    const other = client("https://relay.example/c/other-club/live");
    const opened = await tap([other]);
    assert.equal(other.focused, 0);
    assert.deepEqual(opened, [MYNIGHT]);
  });
});
