import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { displayName, parseMintArgs, withClub } from "../src/mint.js";

/**
 * These exist because of a real evening at a real keyboard: on Windows,
 * `npm run mint -- acme-rc "Acme RC"` printed a config block
 * naming the club "^Acme^ RC^". The token was fine; the name was not.
 */
describe("the club name the shell hands over", () => {
  it("passes a clean name through untouched", () => {
    assert.deepEqual(displayName(["Acme RC"]), { name: "Acme RC", mangled: false });
  });

  it("joins a name the shell split on spaces", () => {
    // Quotes eaten, arguments split: still one club, still one name.
    assert.deepEqual(displayName(["Acme", "RC"]), { name: "Acme RC", mangled: false });
  });

  it("removes the carets npm on Windows inserts, and says it did", () => {
    assert.deepEqual(displayName(["^Acme^ RC^"]), { name: "Acme RC", mangled: true });
  });

  it("handles carets across split arguments too", () => {
    assert.deepEqual(displayName(["^Acme^", "RC^"]), { name: "Acme RC", mangled: true });
  });

  it("collapses the run of spaces stripping leaves behind", () => {
    assert.deepEqual(displayName(["^", "Acme", "^", "RC"]), { name: "Acme RC", mangled: true });
  });

  it("reports no name at all rather than an empty one", () => {
    // The caller falls back to the club id, which must not become "".
    assert.deepEqual(displayName([]), { name: undefined, mangled: false });
    assert.deepEqual(displayName(["   "]), { name: undefined, mangled: false });
    assert.deepEqual(displayName(["^^^"]), { name: undefined, mangled: true });
  });
});

describe("mint arguments", () => {
  it("keeps flags out of the club name", () => {
    const args = parseMintArgs(["Acme", "RC", "--write"]);
    assert.deepEqual(args.nameArgs, ["Acme", "RC"]);
    assert.equal(args.write, true);
  });

  it("takes a config path either way round", () => {
    assert.equal(parseMintArgs(["--config", "other.json"]).configPath, "other.json");
    assert.equal(parseMintArgs(["--config=other.json"]).configPath, "other.json");
  });

  it("defaults to no write and the usual config", () => {
    const args = parseMintArgs([]);
    assert.equal(args.write, false);
    assert.equal(args.configPath, "./relay.config.json");
  });
});

const HASH_A = "a".repeat(64);
const HASH_B = "b".repeat(64);

const entry = {
  club: "acme-rc",
  name: "Acme RC",
  tokenSha256: HASH_B,
  publishRoster: false,
};

describe("putting a club into a config", () => {
  it("starts a usable config from nothing", () => {
    const { config, replaced } = withClub(undefined, entry);
    assert.equal(replaced, false);
    assert.equal(config["port"], 8787);
    assert.equal(config["dataDir"], "./data");
    assert.deepEqual(config["clubs"], [entry]);
  });

  it("leaves every other club exactly as it was", () => {
    const other = { club: "other-rc", name: "Other", tokenSha256: HASH_A, publishRoster: true };
    const { config, replaced } = withClub({ port: 9000, clubs: [other] }, entry);

    assert.equal(replaced, false);
    assert.equal(config["port"], 9000, "an existing port is not reset to the default");
    assert.deepEqual(config["clubs"], [other, entry]);
  });

  it("re-minting a token does not start publishing a roster", () => {
    // The club already chose to publish names. A new token must not
    // silently revoke that - nor, the other way round, grant it.
    const before = { club: "acme-rc", name: "Acme RC", tokenSha256: HASH_A, publishRoster: true };
    const { config, replaced } = withClub({ clubs: [before] }, entry);

    assert.equal(replaced, true);
    const after = (config["clubs"] as Record<string, unknown>[])[0];
    assert.equal(after?.["tokenSha256"], HASH_B, "the hash is the thing that changes");
    assert.equal(after?.["publishRoster"], true, "the roster decision is the thing that does not");
  });

  it("drops a club whose hash is the example placeholder, and names it", () => {
    const placeholder = { club: "example-rc", name: "Example", tokenSha256: "paste the hash here" };
    const { config, dropped } = withClub({ clubs: [placeholder] }, entry);

    assert.deepEqual(dropped, ["example-rc"]);
    assert.deepEqual(config["clubs"], [entry]);
  });
});
