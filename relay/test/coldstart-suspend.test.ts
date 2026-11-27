import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";

// WHY (27 Nov 2026, Asher: the home-screen app "usually loads a white
// screen"): an idle relay that is stopped takes about 6 seconds to boot, and
// a phone opening the app waits on it with a blank screen. Suspended, it
// wakes in under a second. See the comment in fly.toml.
describe("Relay machine: wakes fast after being idle", () => {
  const toml = readFileSync(fileURLToPath(new URL("../../fly.toml", import.meta.url)), "utf8");
  const setting = (name: string): string | undefined => {
    const line = toml.split(/\r?\n/).find((l) => new RegExp(`^\\s*${name}\\s*=`).test(l));
    return line?.split("=")[1]?.trim().replace(/^'|'$/g, "");
  };

  it("suspends an idle machine instead of stopping it", () => {
    assert.equal(setting("auto_stop_machines"), "suspend");
  });

  it("still starts a machine when a request comes in", () => {
    assert.equal(setting("auto_start_machines"), "true");
  });
});
