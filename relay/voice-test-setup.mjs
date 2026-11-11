// Makes the config a local voice-test relay needs, once.
//
// WHY THIS EXISTS. The HeatCaller voice is meant to run on HeatCaller's own
// ElevenLabs account, on the deployed relay, with the key as a Fly secret.
// None of that is a good way to try the thing out for the first time. This
// writes a config for a relay running on THIS laptop, with a club of its
// own and a catalogue built from the voices your own ElevenLabs account
// already has - so the whole path (app -> relay -> ElevenLabs -> saved
// line) can be heard before anything is deployed and before any account is
// upgraded.
//
// WHAT IT NEVER DOES. It never writes the ElevenLabs key anywhere. The key
// reaches the relay through the environment only, and this script uses it
// once, in memory, to ask which voices the account has. It also never
// touches an existing config: a config that is already there has a token in
// it that you have already pasted into HeatCaller, and re-minting that
// would silently break the setup it was written for.
//
// Run through voice-test.cmd, which sets the environment and starts the
// relay afterwards.

import { createHash, randomBytes } from "node:crypto";
import { existsSync, readFileSync, renameSync, writeFileSync } from "node:fs";

const CONFIG_PATH = process.argv[2] ?? "./relay.config.voice-test.json";
const CLUB = "voice-test";
const DEFAULT_PORT = 8787;

/** How many of the account's voices to put in the catalogue. Two or three is what a club will be offered. */
const CATALOGUE_SIZE = 3;

/**
 * The cheap model, deliberately.
 *
 * This is your own account paying for every line while you are testing,
 * and Flash is a fraction of the price of the v2 models for something you
 * are listening to for timing and pronunciation rather than judging on
 * absolute quality. Edit the config afterwards if you want to compare.
 */
const MODEL_ID = "eleven_v4_turbo";

function say(line = "") {
  console.log(line);
}

/** A catalogue id the relay will accept: lower-case letters, digits and hyphens, 24 at most. */
function slug(name, taken) {
  let base = name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 24)
    .replace(/-+$/g, "");

  if (!/^[a-z0-9]/.test(base)) {
    base = `voice-${base}`.slice(0, 24).replace(/-+$/g, "");
  }

  if (base.length === 0) {
    base = "voice";
  }

  let id = base;

  // Two voices called "Announcer (UK)" and "Announcer (US)" slug to the
  // same thing, and the relay refuses to start on a duplicate id.
  for (let n = 2; taken.has(id); n += 1) {
    const suffix = `-${n}`;
    id = base.slice(0, 24 - suffix.length) + suffix;
  }

  taken.add(id);
  return id;
}

async function readAccountVoices(key) {
  let response;

  try {
    response = await fetch("https://api.elevenlabs.io/v1/voices", {
      headers: { "xi-api-key": key },
    });
  } catch (error) {
    throw new Error(`Couldn't reach ElevenLabs (${String(error)}). Is this laptop online?`);
  }

  if (response.status === 401) {
    throw new Error("ElevenLabs turned the key down. Check the key in voice-test.key.");
  }

  if (!response.ok) {
    throw new Error(`ElevenLabs answered ${response.status} when asked for the voice list.`);
  }

  const body = await response.json();
  const list = Array.isArray(body?.voices) ? body.voices : [];

  // Defensive about the field name rather than trusting one spelling: a
  // config written with the wrong id would fail later, at the first line
  // somebody tried to speak, which is a much worse place to find out.
  const voices = list
    .map((voice) => ({
      voiceId: typeof voice?.voice_id === "string" ? voice.voice_id : typeof voice?.voiceId === "string" ? voice.voiceId : "",
      name: typeof voice?.name === "string" && voice.name.trim().length > 0 ? voice.name.trim() : "",
    }))
    .filter((voice) => voice.voiceId.length > 0);

  if (voices.length === 0) {
    throw new Error(
      "ElevenLabs listed no voices for this key. Add a voice to the account, or put the voiceId into the config by hand.",
    );
  }

  return voices;
}

function describeExisting() {
  let config;

  try {
    config = JSON.parse(readFileSync(CONFIG_PATH, "utf8"));
  } catch (error) {
    say(`  ${CONFIG_PATH} is not readable JSON, so it was left alone.`);
    say(`  ${String(error)}`);
    say("  Fix it, or delete it and run this again to start over.");
    return 1;
  }

  const port = Number.isInteger(config?.port) ? config.port : DEFAULT_PORT;
  const club = config?.clubs?.[0]?.club ?? CLUB;
  const voices = Array.isArray(config?.voice?.voices) ? config.voice.voices : [];

  say(`  Using the ${CONFIG_PATH} that is already here - nothing was changed.`);
  say("");
  say(`  Relay:  http://localhost:${port}`);
  say(`  Club:   ${club}`);
  say(`  Voices: ${voices.map((voice) => voice.id).join(", ") || "(none)"}`);
  say("");
  say("  The token was printed the first time and is not stored anywhere. If it is");
  say(`  lost, delete ${CONFIG_PATH} and run this again for a new one.`);
  say("");

  return 0;
}

async function main() {
  const key = process.env["RELAY_VOICE_KEY"] ?? "";

  if (existsSync(CONFIG_PATH)) {
    return describeExisting();
  }

  if (key.trim().length === 0) {
    say("  RELAY_VOICE_KEY is not set, so there is no key to ask ElevenLabs with.");
    say("  Run voice-test.cmd, which reads it from voice-test.key.");
    return 1;
  }

  const account = await readAccountVoices(key.trim());
  const chosen = account.slice(0, CATALOGUE_SIZE);
  const taken = new Set();

  const voices = chosen.map((voice) => ({
    id: slug(voice.name.length > 0 ? voice.name : voice.voiceId, taken),
    name: voice.name.length > 0 ? voice.name : "Voice",
    what: "From your own ElevenLabs account",
    voiceId: voice.voiceId,
    modelId: MODEL_ID,
    stability: 0.5,
    similarity: 0.75,
    style: 0.3,
    speakerBoost: true,
    speed: 1,
    outputFormat: "mp3_44100_128",
  }));

  // Shown once, stored nowhere: the config holds only the hash, exactly as
  // a real relay's does.
  const token = `hg_${randomBytes(24).toString("base64url")}`;

  const config = {
    port: DEFAULT_PORT,

    // Its own folder, not ./data: a test relay's cached audio and usage
    // counts have no business mixing with anything real, and the whole
    // thing can be thrown away by deleting one directory.
    dataDir: "./data-voice-test",
    maxBodyBytes: 1048576,
    voice: {
      // Small on purpose. This is your own account's credits, and a stuck
      // Prepare Lines should run out of allowance long before it runs out
      // of money. Raise it here when you want a longer test.
      monthlyChars: 20000,
      maxChars: 600,
      voices,
    },
    clubs: [
      {
        club: CLUB,
        name: "Voice test",
        tokenSha256: createHash("sha256").update(token, "utf8").digest("hex"),
        publishRoster: false,
        voice: {
          enabled: true,
          monthlyChars: 20000,

          // Empty means the whole catalogue.
          voices: [],
        },
      },
    ],
  };

  const temporary = `${CONFIG_PATH}.tmp-${process.pid}`;

  writeFileSync(temporary, `${JSON.stringify(config, null, 2)}\n`, "utf8");
  renameSync(temporary, CONFIG_PATH);

  say(`  Wrote ${CONFIG_PATH}.`);
  say("");
  say(`  Catalogue: ${voices.map((voice) => `${voice.id} (${voice.name})`).join(", ")}`);
  say("  Edit that file if you want different voices, settings or a bigger allowance.");
  say("");
  say("  ----------------------------------------------------------------");
  say("  PASTE THESE THREE INTO HEATCALLER");
  say("  Voice Announcements -> Voice Settings -> the Voice relay boxes:");
  say("");
  say(`      Voice relay:  http://localhost:${DEFAULT_PORT}`);
  say(`      Club:         ${CLUB}`);
  say(`      Token:        ${token}`);
  say("");
  say("  The token is shown once and is not stored anywhere - the config");
  say("  holds only its hash. Leave the Online page exactly as it is: only");
  say("  the voice uses these.");
  say("  ----------------------------------------------------------------");
  say("");

  return 0;
}

// process.exitCode, never process.exit: exit() abandons anything still
// queued on stdout, and when this is run through a pipe - "voice-test.cmd >
// setup.txt", or into a log viewer - stdout is not synchronous. The block
// it would cut off is the one carrying the token, which is printed once and
// cannot be recovered.
main()
  .then((code) => {
    process.exitCode = code;
  })
  .catch((error) => {
    say(`  ${error instanceof Error ? error.message : String(error)}`);
    say("");
    process.exitCode = 1;
  });
