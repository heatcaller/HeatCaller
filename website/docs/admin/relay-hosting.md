# Hosting the relay on Fly.io

The relay is the small server that gives your club a stable `https://` address for its results, standings, live race and online check-in — and, if you want it, the [HeatCaller voice](#the-heatcaller-voice), so clubs get a natural announcer with no ElevenLabs account of their own. This page takes you from nothing to a working relay on Fly.io, using a Windows PC, and then covers the day-to-day jobs: adding clubs, replacing a leaked token, updating, and moving to a new address.

You only need this page if you run the relay yourself. If someone else hosts it for your club, all you need from them is the address, your club id and a publish token; see [Setting up online publishing](../online/setup.md).

!!! note
    The laptop running HeatCaller stays the source of truth. The relay keeps the most recent thing each club published and hands it back to anyone with the address. It never connects to the laptop, and it can never control a race. See [Security and privacy](security-and-privacy.md).

## What you'll end up with

- One Fly.io app (the files ship set up for the name `heatcaller-relay`) running one small machine: `shared-cpu-1x` with 256 MB of memory.
- A 1 GB volume called `relay_data`, mounted at `/data`, holding everything the clubs have published. It survives restarts and redeploys.
- The club list and token hashes stored as a Fly secret, never in the image or on the volume.
- A machine that stops when nobody is using it and starts again on the next request.
- Optionally, an ElevenLabs key as a second Fly secret, so the clubs you switch on can use the HeatCaller voice.

## Before you start

You need:

- **A Fly.io account.** Sign up at fly.io.
- **Node.js 20 or newer**, which includes npm. Check with `node --version` in a Command Prompt.
- **The `relay` folder** from the HeatCaller source. Everything below is run from inside that folder. It contains `package.json`, `fly.toml`, `Dockerfile`, `fly-set-config.cmd`, `probe.cmd`, `published.cmd`, `relay.config.example.json` and the `src` folder.
- **curl**, which `probe.cmd` and `published.cmd` use. It is built into current versions of Windows 10 and 11; check with `curl --version`.

### Install flyctl

flyctl is Fly's command-line tool. Install it from a Command Prompt:

```
powershell -Command "iwr https://fly.io/install.ps1 -useb | iex"
```

The installer changes your PATH, and only **new** terminal windows see the change. Close the window, open a new one, and check:

```
fly version
```

If `fly` isn't recognised but `flyctl` is, use `flyctl` wherever this page says `fly`; they are the same program. The `fly-set-config.cmd` script finds whichever one is installed.

Sign in once:

```
fly auth login
```

## Step 1: install and test the relay

```
cd relay
npm install
npm test
```

`npm install` fetches TypeScript and the Node type definitions. Both are build-time only; the deployed server has no runtime dependencies. `npm test` compiles the code and runs the test suite. Don't carry on if any test fails.

## Step 2: mint a club and its token

Each club on the relay has an id, a display name, and a publish token. Minting creates the token and writes the club into `relay.config.json`.

Build first, then call Node directly:

```
npm run build
node dist/src/index.js mint acme-rc "Acme RC" --write
```

Replace `acme-rc` with your club id and `Acme RC` with your club's name.

The club id goes in every address the relay serves (`/c/acme-rc/...`), so choose it with care. It must be 3 to 32 characters of lower-case letters, digits and single hyphens, and start and end with a letter or digit. `acme-rc` is fine; `Acme_RC` and `acme--rc` are not.

!!! tip "Why call Node directly?"
    `npm run mint -- acme-rc "Acme RC" --write` also works and builds first. On Windows, though, npm re-escapes arguments, and a quoted name can arrive as `^Acme^ RC^`. The mint command strips the carets and prints a note saying it did, but calling Node directly avoids the problem.

### What mint prints

Mint prints the club, then the token in a box of its own:

```
  Club: acme-rc  (Acme RC)

  ----------------------------------------------------------------
  THE TOKEN. This is the one you paste into HeatCaller, and into
  probe.cmd. It starts with hg_. It is shown once and is not stored
  anywhere - not even in the config, which holds only its hash.

      hg_...
  ----------------------------------------------------------------

  Added "acme-rc" in ./relay.config.json.
  Restart the relay to pick it up: the config is read once, at start.
```

**Copy the token now.** It is shown once and never stored. The config file holds only its SHA-256 hash, so if you lose the token, you mint a new one (see [Replacing a token](#replacing-a-leaked-or-lost-token)).

Two different strings come out of minting, and they go to different places:

| String | Looks like | Goes into |
|---|---|---|
| Token | `hg_` followed by 32 characters | HeatCaller's **Club Website** page, and `probe.cmd` |
| Hash | 64 hexadecimal characters | `relay.config.json` (`--write` puts it there for you) |

Hand the token to the club over a private channel. Anyone who has it can publish to, or delete, that club's pages.

### What `--write` does

With `--write`, mint creates `relay.config.json` if it doesn't exist, or updates it if it does:

- A new club is added. An existing club with the same id gets the new hash, which replaces its old token.
- Every other club is left exactly as it was.
- A club that already publishes its roster keeps that setting. A new token doesn't switch the roster on.
- Any entry without a usable hash (such as the example config's placeholder) is removed, and mint names it.
- If `relay.config.json` isn't valid JSON, mint leaves it untouched and stops. Fix the file, or delete it and run mint again.

Without `--write`, mint prints the block to paste into the `clubs` array yourself. `--config <path>` points mint at a different config file.

### The config file

`relay.config.json` looks like this:

```json
{
  "port": 8787,
  "dataDir": "./data",
  "maxBodyBytes": 1048576,
  "clubs": [
    {
      "club": "acme-rc",
      "name": "Acme RC",
      "tokenSha256": "<64 hex characters written by mint>",
      "publishRoster": false
    }
  ]
}
```

It never contains a token, only hashes, but treat it as private anyway. It is excluded from Git and from the Docker image. All the options are described in [Configuration reference](#configuration-reference).

A config with no `"voice"` block, as above, is a relay that publishes pages and nothing else. To let clubs speak with the [HeatCaller voice](#the-heatcaller-voice) as well, add a voice catalog and switch each club on.

## Step 3: check fly.toml

`fly.toml` in the relay folder is already set up. The parts that matter:

| Setting | Value | Why |
|---|---|---|
| `app` | `heatcaller-relay` | The Fly app name. Change it if you use a different name (Step 4). |
| `primary_region` | `ewr` | Where the machine runs. Pick a region near your track if you like, and create the volume in the same region. |
| `[env] RELAY_DATA` | `/data` | Tells the relay to keep its data on the volume. |
| `[[mounts]]` | `relay_data` at `/data` | The persistent volume. |
| `internal_port` | `8787` | The port the relay listens on inside the machine. |
| `force_https` | `true` | Plain-http visitors are sent to https. |
| `auto_stop_machines` / `auto_start_machines` / `min_machines_running` | `'stop'` / `true` / `0` | The machine stops when idle and wakes on the next request. |
| `[http_service.concurrency]` | requests, soft 200, hard 250 | Limits how many requests one machine takes at once. |
| `[[http_service.checks]]` | `GET /healthz` every 30s, 5s timeout, 10s grace | Fly's health check. |
| `[[vm]]` | `shared-cpu-1x`, 256 MB | One small machine is plenty. |

## Step 4: create the app and its volume

Create the app:

```
fly apps create heatcaller-relay
```

App names are shared by everyone on Fly. If `heatcaller-relay` is taken, pick another name, use it here, and change the `app = '...'` line in `fly.toml` to match.

Create the volume, in the same region as `primary_region`:

```
fly volumes create relay_data --size 1 --region ewr --yes
```

## Step 5: put the config into a Fly secret

```
fly-set-config.cmd
```

This script:

1. Checks that `relay.config.json` exists in the current folder.
2. Finds `fly` or `flyctl` on the PATH.
3. Uses Node to read the file, check it is valid JSON, and turn it into one line.
4. Passes that line to `fly secrets import` as the `RELAY_CONFIG_JSON` secret.

When it works, it prints `Done - the relay restarts itself with the new config in a few seconds.` If it prints `The secret was NOT set.`, check that flyctl is installed and signed in, and that you are in the relay folder (flyctl uses the `fly.toml` there to know which app you mean).

The relay reads `RELAY_CONFIG_JSON` in preference to a config file, so the club list and hashes never sit in an image layer or on the volume.

## Step 6: deploy

```
fly deploy --ha=false
```

!!! warning "Always use `--ha=false`"
    By default Fly may create two machines for high availability. For this relay that would be wrong, not just more expensive: each machine would have its own volume holding half of the club's history, and every request would be answered by whichever half happened to wake up. The relay's data lives on one disk, so it runs on one machine. Use `--ha=false` on every deploy.

If the deploy says the `relay_data` volume doesn't exist, run the `fly volumes create` command from Step 4 and deploy again.

Your relay is now at `https://<app-name>.fly.dev` (for example `https://heatcaller-relay.fly.dev`).

## Step 7: check it from the outside

### probe.cmd

`probe.cmd` publishes a test frame to a running relay, reads it back, and checks the cases that matter:

```
probe.cmd hg_yourtoken acme-rc https://heatcaller-relay.fly.dev
```

The arguments are the token, the club id and the relay address. Leave the token off and the script asks for it, which keeps it out of your command history. With no address it tests `http://localhost:8787`, and with no club it uses `horizon-test`, so pass both for a hosted relay.

What each step should show:

| Step | Expect |
|---|---|
| 1. health | `ok:true` and the number of clubs |
| 2. publish state seq=1 | `applied:true` |
| 3. read it back | `seq`, `publishedAt` and `body` |
| 4. same seq again | `applied:false`. This is correct, not an error. |
| 5. a token that is not yours | `HTTP 401` |
| 6. the roster | `HTTP 403` unless the club publishes its roster |
| 7. a club that does not exist | `HTTP 404` |
| 8. what landed on disk | Only meaningful for a local relay; it lists the local `data` folder. |

If you paste the 64-character hash instead of the token, the script stops and explains the difference.

!!! note
    The probe writes a small test document to the club's live race channel. The laptop replaces it the next time it publishes.

### published.cmd

`published.cmd` needs no token. It reads the club's public addresses (live race, standings, roster and finished sessions) and prints what is there:

```
published.cmd acme-rc https://heatcaller-relay.fly.dev
```

A `seq` with 13 digits means HeatCaller published it (the app uses the time in milliseconds). A small number came from `probe.cmd`.

## Step 8: point HeatCaller at it

On the club's laptop, open the **Club Website** page and enter:

- **Address:** `https://heatcaller-relay.fly.dev` (your app's address)
- **Club id:** the id you minted
- **Publish token:** the `hg_` token

Click **Test Connection**. A good result says `Connected as Acme RC.` (your club's name), and a successful test saves the settings straight away. Then tick **Publish to the relay**. The full walkthrough is in [Setting up online publishing](../online/setup.md).

<!-- screenshot: Club Website page, Relay section, after a successful Test Connection -->

## The HeatCaller voice

The relay can also speak. With an ElevenLabs key of your own on the relay, the clubs you switch on get natural announcer voices with no account, no key and no bill of their own; the laptop sends the words and the name of a voice, and gets audio back. The clubs' side of it is in [Voice announcements](../announcer/voice.md#the-heatcaller-voice).

This is optional, and the relay works exactly as it did without it. Leave the key out and the relay starts, says so in the log, and every club carries on with its own key or the free Windows voice.

### The catalog

Add a `"voice"` block beside `"clubs"` in `relay.config.json`. Each entry is one announcer voice as the clubs will see it:

```json
"voice": {
  "monthlyChars": 100000,
  "maxChars": 600,
  "voices": [
    {
      "id": "clubhouse",
      "name": "Clubhouse",
      "what": "Warm and clear, easy over a PA",
      "voiceId": "<the ElevenLabs voice id>",
      "modelId": "eleven_flash_v2_5",
      "stability": 0.5,
      "similarity": 0.75,
      "style": 0.3,
      "speakerBoost": true,
      "speed": 1,
      "outputFormat": "mp3_44100_128"
    }
  ]
}
```

A club is only ever told a voice's `id`, `name` and `what`. The ElevenLabs voice id, the model and the settings stay on the relay and are never served — that is what keeps every club's night sounding the way it was designed to, and the bill predictable.

Pick voices that read well over a PA at a noisy track, and write `what` as one short phrase: it is all a race director has to go on when choosing, since the app's **Test** buttons are hidden on this voice.

!!! note
    Changing a voice's model or any of its settings changes its fingerprint, and every laptop renders that voice's lines again rather than playing takes that no longer match. That costs each club a little of its allowance, so re-tune between seasons rather than mid-season.

### Switching a club on

Clubs start with no voice. Give each one its own `"voice"` entry:

```json
{
  "club": "acme-rc",
  "name": "Acme RC",
  "tokenSha256": "<64 hex characters written by mint>",
  "publishRoster": false,
  "voice": { "enabled": true, "monthlyChars": 100000 }
}
```

`monthlyChars` is optional and falls back to the catalog's. Add `"voices": ["clubhouse", "paddock"]` to hold a club to part of the catalog; leave it out, or empty, and the club may use all of it. The relay refuses to start if a club names a voice the catalog doesn't have.

Then `fly-set-config.cmd`, and the relay restarts with it.

### The key

The ElevenLabs key is a Fly secret of its own, never written into the config:

```
fly secrets set RELAY_VOICE_KEY=sk_...
```

Restrict the key in ElevenLabs to the text-to-speech endpoints, and give it a credit limit. That limit is the circuit breaker behind the per-club allowances: the allowances are what a club sees, the credit limit is what stops a mistake costing you a month's budget.

Without the secret the relay starts and simply does not offer the voice, whatever the catalog says. The startup log tells you which you have:

```
[relay] HeatCaller voice: 4 voice(s), 7 club(s) included
[relay] HeatCaller voice: a catalogue is configured but RELAY_VOICE_KEY is not set, so it is switched off
```

### What it costs you

Only a render that isn't already on disk is charged to ElevenLabs and counted against a club's month. Everything else is free:

- **A line with no driver's name in it is rendered once for every club.** The laptop tells the relay when a line is one of the announcer's fixed lines, so "Green flag!" is paid for by the first club that asks and handed to all the rest. Most of a club's fixed lines cost nothing at all.
- **A line with a name in it is cached for that club alone.**
- **Each club's month is a calendar month (UTC)**, counted in `dataDir/voice/usage/<club>.json`, so a deploy doesn't hand anybody a fresh allowance.
- **The allowance holds under load.** A club's characters are reserved before the render and given back if it fails, so a laptop with several lines in flight at once can't slip past the limit.

Rendered audio lives in `dataDir/voice/` — `shared/` for the lines every club may have, `clubs/<club>/` for the rest — content-addressed and swept oldest-used-first at 200 MB shared and 25 MB per club. None of it counts against a club's 50 MB publishing quota.

Renders are capped at six at once across the whole relay, and a club may ask 300 times a minute, which is enough for a laptop's **Prepare Lines** to walk a whole announcer style.

### What a club sees when it goes wrong

Every refusal has the same effect on the night: the free Windows voice says that line, so a flag call is never silent. The app names the reason in its console and on **Sounds → Mixer**. The ones you can fix from here are "the HeatCaller voice isn't included for this club" (no `"enabled": true`), "the HeatCaller voice isn't switched on for this relay" (no `RELAY_VOICE_KEY`), "that HeatCaller voice isn't available any more" (a voice taken out of the catalog that a club had chosen) and "the relay didn't accept this club's token" (a token replaced but not yet pasted into the club's **Club Website** page).

## Day to day

### Adding a club

1. From the relay folder: `node dist/src/index.js mint new-club "New Club" --write` (run `npm run build` first if you've updated the code).
2. `fly-set-config.cmd`. Fly restarts the machine with the new config.
3. Give the new club its token, privately.

### Replacing a leaked or lost token

If a token has been shared somewhere it shouldn't be (a chat, a screenshot, an email), replace it:

1. Mint the same club id again: `node dist/src/index.js mint acme-rc "Acme RC" --write`. The new hash replaces the old one.
2. `fly-set-config.cmd`. As soon as the relay restarts with the new config, the old token stops working.
3. On the laptop's **Club Website** page, paste the new token into **Publish token** and click **Test Connection**.

The club's published data isn't affected. Publishing pauses between step 2 and step 3; the laptop keeps its queue and catches up once the new token is in. Printed QR check-ins keep working, because they don't depend on the token.

### Turning on a club's public roster

A club's roster (who is entered and who is checked in) is only served if **both** the club ticks the roster option on its **Club Website** page **and** the relay's config has `"publishRoster": true` for that club. Mint never turns it on. To allow it, edit that club's entry in `relay.config.json` by hand, then run `fly-set-config.cmd`. Many drivers are children; turn this on only when the club has decided to.

### Removing a club

Ask the club to click **Delete Everything Published** on its **Club Website** page first; that erases everything the club has on the relay. Then remove the club's entry from `relay.config.json` and run `fly-set-config.cmd`. Removing the entry alone stops the club publishing but leaves its files on the volume.

<!-- TODO: confirm whether there is a supported way to delete a removed club's files from the volume without its token -->

### Updating the relay

Get the new relay code, then from the relay folder:

```
npm install
npm test
fly deploy --ha=false
```

The volume, and every published result on it, survives the deploy. If the laptop reports `The relay is an older build that cannot take this. Redeploy it.`, this is the fix.

!!! note "HeatCaller 0.52.0: redeploy the relay"
    The 0.52.0 relay adds the HeatCaller voice: a voice catalog in the config, a per-club allowance, and two endpoints the laptop uses to list voices and have a line spoken. Clubs start with it switched off, and a relay with no `RELAY_VOICE_KEY` secret starts and simply doesn't offer it, so redeploying changes nothing until you set one up. See [The HeatCaller voice](#the-heatcaller-voice). From the relay folder, run `npm test`, then `fly deploy --ha=false`.

!!! note "HeatCaller 0.51.0: redeploy the relay"
    The 0.51.0 relay gives the race-day manager's requests and answers an allowance of their own (about 1 MB waiting for the laptop, 4 MB waiting for phones) that no longer counts toward the club's 50 MB, so a busy manager can't make result publishing fail or push old results off. Its manager page shows messages at the foot of the screen, masks the PIN and empties it after every sign-in answer, takes Manager PINs of up to 32 digits, shows each result's winner under a small caption, and gives the **Program** tab's class list a line of its own with **Seed Mains** and **Bump Ups** side by side under it. Check-in passes are compared in constant time, like every other secret. From the relay folder, run `npm test`, then `fly deploy --ha=false`. The app keeps working with an older relay until you do.

!!! note "HeatCaller 0.48.0: redeploy the relay"
    The 0.48.0 relay shows the club logo much bigger, in a block of its own beside the club's name (72 to 180 pixels across, depending on the screen), puts it on a plate when the app says it would vanish on the page background, and gives the logo and the home-screen icon a new address whenever they change, so a new logo shows straight away instead of after up to an hour. From the relay folder, run `npm test`, then `fly deploy --ha=false`. The app keeps working with an older relay until you do: the pages keep their colors and show the logo the old way.

!!! note "HeatCaller 0.47.0: redeploy the relay"
    The 0.47.0 relay makes the PIN limits hold even when guesses arrive together while the machine wakes, stops many page loads at once from running it out of memory, answers a storage hiccup with "try again" (HTTP 503) instead of a check-in page stuck on "not open", and clears a club's PIN lockout when the club deletes everything it published. From the relay folder, run `npm test`, then `fly deploy --ha=false`. The app keeps working with an older relay until you do.

!!! note "HeatCaller 0.46.0 needs a relay redeploy"
    The 0.46.0 relay adds the growing club-wide PIN lockout, sign-in passes that keep signed-in phones working through a lockout, the lockout time the **Club Website** page shows, and the 8 MB cap on results listings. Update the relay code and run `fly deploy --ha=false` as above. Until you do, the app still works with the old relay, but PIN sign-in only closes for 10 minutes at a time and the **Club Website** page can't show a lockout.

### Logs and status

```
fly logs
fly status
```

`fly logs` streams the relay's log live. Lines worth knowing:

| Log line | Meaning |
|---|---|
| `[relay] listening on :8787, 1 club(s), data in /data` | Started normally. |
| `[relay] running as uid 1000, not root` | It gave up root after taking ownership of `/data`. |
| `[relay] refused: a token published to "x", which is not its club.` | A laptop is using another club's token, or someone is trying tokens. |
| `[relay] "x" is at its storage quota; a "results" document was refused.` | The club hit its 50 MB limit (see [Limits](#limits)). |
| `[relay] "x": the check-in queue is full; a tap was refused.` | 500 check-in taps are waiting and the laptop isn't collecting them. |
| `[relay] "x": the manager's request queue is full; a request was refused.` | The race-day manager's requests are waiting and the laptop isn't collecting them (see [Limits](#limits)). |
| `[relay] "x": 50 wrong check-in PINs - the PIN list is closed for N minutes (lockout N in a row).` | Someone is guessing PINs; the club's PIN sign-in is closed. Changing the staff PIN in HeatCaller reopens it. See [Security and privacy](security-and-privacy.md#online-check-in-pins). |
| `[relay] "x": the staff PIN changed, so its wrong-PIN count starts again.` | A new staff PIN was published; any lockout is lifted. |
| `[relay] "x": could not save the wrong-PIN count (...)` | The volume is full or unwritable. The count is kept in memory until the relay restarts. |
| `[relay] forwarding every request to https://...` | This app is a forwarder (see below). |

### The machine sleeps when idle

The machine stops when there's no traffic and starts on the next request. The first request after a quiet spell takes a second or two longer. The laptop's publisher rides this out with its normal retries.

## Moving to a new app name

Use these steps to move the relay to a different Fly app name. Do it when online check-in isn't open, so no check-in tap is left waiting in the old relay's queue.

1. `fly apps create <new-name>`, and set `app = '<new-name>'` in `fly.toml`.
2. `fly volumes create relay_data --size 1 --region ewr --yes`
3. `fly-set-config.cmd`. Using the same `relay.config.json` means the same clubs and tokens work on the new app unchanged.
4. `npm test`, then `fly deploy --ha=false`
5. `probe.cmd hg_yourtoken your-club https://<new-name>.fly.dev`
6. Turn the old app into a forwarder, so QR check-ins, posters and bookmarks with the old address still work:

    ```
    fly secrets set RELAY_REDIRECT_ORIGIN=https://<new-name>.fly.dev -a <old-name>
    fly deploy --ha=false -a <old-name>
    ```

7. On each club's laptop, change the **Address** on the **Club Website** page to the new one and click **Test Connection**.

### How the forwarder behaves

With `RELAY_REDIRECT_ORIGIN` set, the relay answers every request with a permanent redirect (HTTP 308) to the same path on the new origin, and stores nothing. A 308 keeps a POST as a POST, so check-in taps from old QR check-in links still arrive.

- `/healthz` is the exception: the forwarder answers it itself, so Fly's health check keeps passing.
- The value must be a plain `https://` origin, such as `https://heatcaller-relay.fly.dev`, with no path, query or trailing parts. Anything else stops the relay at start-up, on purpose: a typo would send every badge in the club somewhere else. (`http://` is accepted only for `localhost` or `127.0.0.1`, for testing.)
- The forwarder still loads its club config at start-up, so leave its `RELAY_CONFIG_JSON` secret in place.
- Keep the old app running for as long as old QR check-ins are in drivers' boxes. It stops when idle, like any other.

!!! warning "The laptop doesn't follow redirects"
    HeatCaller never follows a redirect, because its token would go wherever the redirect pointed. A laptop still set to the old address gets `The relay answered HTTP 308.` from **Test Connection** and from publishing. Change the address on the **Club Website** page (step 7). HeatCaller moves an address on one of the relay's earlier hosted names to `heatcaller-relay.fly.dev` by itself; any other move needs the address changed by hand.

The new relay starts empty. When HeatCaller starts, and whenever its relay settings change, it re-sends the championship standings and every session from the last 36 hours. Older results stay on the laptop.

## Running a relay locally for testing

You can run the relay on your own PC to try things out before hosting it.

```
cd relay
npm install
npm run build
node dist/src/index.js mint test-club "Test Club" --write
npm start
```

The relay reads `./relay.config.json`, listens on port 8787 and keeps its data in `./data`. <kbd>Ctrl</kbd>+<kbd>C</kbd> stops it; it lets any publish in progress finish first. Restart it after every mint, because the config is read once, at start.

In HeatCaller, use `http://localhost:8787` as the **Address**. Plain `http://` is allowed only for `localhost` and private IP addresses (10.x, 172.16–31.x, 192.168.x, 169.254.x). For a relay on another machine on your network, use its IP address; a name ending in `.local`, `.lan` or `.home.arpa` needs `https://`, because any device on the Wi-Fi can answer to such a name. Anywhere else, HeatCaller insists on `https://` so the token is never sent unencrypted.

Check it with `probe.cmd` and `published.cmd`, which default to `http://localhost:8787`:

```
probe.cmd hg_yourtoken test-club
published.cmd test-club
```

### Trying the HeatCaller voice out on your own laptop

The voice is the one part you can't sensibly test by deploying first: it needs a key with real credits behind it. `voice-test.cmd` runs a relay here, with the voice switched on, against an ElevenLabs key of your own — no deploy, no club touched, nothing on the hosted relay changed.

Put the key on the first line of a file called `voice-test.key` in the `relay` folder, then:

```
voice-test.cmd
```

The first run builds, writes `relay.config.voice-test.json` with a catalog made from the voices that ElevenLabs account already has, mints a club token, and prints the three values to paste into HeatCaller. Later runs just start it.

Those three go into **Voice Announcements → Voice Settings**, in the **Voice relay:**, **Club:** and **Token:** boxes under the HeatCaller voice — not the **Club Website** page. Only the voice moves; results, check-in and the race-day manager keep going to the hosted relay. See [Sending the voice to a different relay](../announcer/voice.md#sending-the-voice-to-a-different-relay).

Three things it's careful about, because it's your own credits:

- **The key is never written into the config.** `voice-test.key` is git-ignored, and the config holds only the club token's hash, the same as a real relay's.
- **Its own data folder** (`data-voice-test/`) and its own allowance — 20,000 characters, not 100,000 — so a runaway **Prepare Lines** stops long before it gets expensive. Both are in the config if you want more.
- **`eleven_flash_v2_5`**, the cheapest model, for the same reason.

To throw it away: <kbd>Ctrl</kbd>+<kbd>C</kbd>, then delete `relay.config.voice-test.json` and `data-voice-test/`. Empty the three boxes in HeatCaller and the voice goes back to the relay on the **Club Website** page.

## Configuration reference

### Config file options

| Setting | What it does | Default |
|---|---|---|
| `port` | Port to listen on. The `PORT` environment variable wins over it. | `8787` |
| `dataDir` | Folder for published data. The `RELAY_DATA` environment variable wins over it. | `./data` |
| `maxBodyBytes` | Largest request body accepted, in bytes. A whole number from 1024 to 16777216 (16 MiB). | `1048576` (1 MiB) |
| `clubs[].club` | The club id. Lower-case letters, digits and single hyphens, 3 to 32 characters. | (required) |
| `clubs[].name` | The club's name, for page headings. | The club id |
| `clubs[].tokenSha256` | SHA-256 of the club's publish token, as 64 hex characters. Written by mint. | (required) |
| `clubs[].publishRoster` | Whether the club's roster may be served publicly. | `false` |
| `clubs[].voice.enabled` | Whether this club may use the HeatCaller voice. | `false` |
| `clubs[].voice.monthlyChars` | This club's characters a calendar month. 0 to 100,000,000. | `voice.monthlyChars` |
| `clubs[].voice.voices` | Catalog ids this club may use. Omitted or empty means the whole catalog. Every id must be in it. | all of them |
| `voice.voices[]` | The announcer voices on offer. Each needs an `id` (lower-case letters, digits and hyphens, up to 24 characters) and a `voiceId`; `name`, `what`, `modelId`, `stability`, `similarity`, `style`, `speakerBoost`, `speed` and `outputFormat` are optional. | none |
| `voice.monthlyChars` | The default allowance per club, a calendar month. 0 to 100,000,000. | `100000` |
| `voice.maxChars` | Longest line the relay will speak at once. 40 to 5,000. | `600` |

Leave `maxBodyBytes` at its default unless you have a reason. HeatCaller sends documents of up to 900 KB; a lower limit makes the relay refuse some of them.

A voice's optional settings default to `eleven_flash_v2_5`, stability 0.5, similarity 0.75, style 0, speaker boost on, speed 1 and the account's own output format. `name` falls back to the `id`.

### Environment variables

| Variable | What it does |
|---|---|
| `RELAY_CONFIG_JSON` | The whole config as one JSON string. Used instead of the file when set. On Fly this is the secret `fly-set-config.cmd` sets. |
| `RELAY_CONFIG` | Path to the config file. Default `./relay.config.json`. |
| `RELAY_VOICE_KEY` | The ElevenLabs key behind the HeatCaller voice. Set on its own, never in the config. Without it the relay starts and doesn't offer the voice. See [The HeatCaller voice](#the-heatcaller-voice). |
| `PORT` | Listen port. Overrides `port` in the config. |
| `RELAY_DATA` | Data folder. Overrides `dataDir` in the config. `fly.toml` sets it to `/data`. |
| `RELAY_UID` / `RELAY_GID` | The user and group the relay switches to after starting as root. Default 1000, the `node` user in the image. |
| `RELAY_REDIRECT_ORIGIN` | Turns the relay into a forwarder. See [How the forwarder behaves](#how-the-forwarder-behaves). |

`PORT` and `RELAY_DATA` override the config on purpose. If you paste a laptop config (with `"dataDir": "./data"`) into the Fly secret and the file won, the relay would write inside the container instead of on the volume, and every deploy would erase the club's history.

### The relay refuses to start when...

The relay would rather not start than start wrong. You'll see one of these in `fly logs` (or the terminal):

| Message begins | Fix |
|---|---|
| `No config at ...` | Mint a club with `--write`, or set `RELAY_CONFIG_JSON`. |
| `RELAY_CONFIG_JSON is set but is not readable JSON` / `The config at ... is not readable JSON` | Fix the JSON, then run `fly-set-config.cmd` again. |
| `The config in ... lists no clubs.` | Add at least one club. |
| `Refusing to start: "..." is not a usable club id.` | Correct the club id. |
| `Refusing to start: the club id "..." is configured twice.` | Remove the duplicate entry. |
| `Refusing to start: "..." has no usable token hash.` | Re-mint that club with `--write`. You probably pasted the token, or the example's placeholder is still there. |
| `Refusing to start: two clubs share a publish token.` | Re-mint one of the clubs. |
| `"maxBodyBytes" in ... must be a whole number of bytes from 1024 to 16777216` | Correct or remove `maxBodyBytes`. |
| `RELAY_REDIRECT_ORIGIN must be ...` / `RELAY_REDIRECT_ORIGIN is not an address` | Set it to a plain origin such as `https://heatcaller-relay.fly.dev`. |
| `Voice ... needs an "id" of lower-case letters, digits and hyphens, up to 24 characters.` | Correct the catalog entry's `id`. |
| `Voice "..." in ... needs the ElevenLabs "voiceId".` | Add the ElevenLabs voice id to that catalog entry. |
| `Voice "..." is listed twice in ...` | Remove the duplicate catalog entry. |
| `Club "..." in ... names a voice "..." that the catalogue does not have.` | Correct that club's `voice.voices`, or add the voice to the catalogue. |
| `"voice" in ... must be an object.` / `"voice.voices" in ... must be a list.` / the same for a club | Correct the shape of the block. A club's `voice.voices` is a list of ids, not of objects. |

## Limits

| Limit | Value | What happens past it |
|---|---|---|
| Request body | `maxBodyBytes` (1 MiB by default) | Refused with 413. |
| One result document | 256 KB (or `maxBodyBytes` if smaller) | Refused with 413. |
| Results kept per club | The newest 300 | Publishing the 301st forgets the oldest. The laptop keeps every result. |
| Storage per club | 50 MB, not counting the race-day manager's requests and answers | The oldest results are forgotten to make room. Only a document that wouldn't fit even then is refused, with 507. Live race frames and other snapshots are never refused for space. |
| Check-in taps waiting | 500 | New taps are refused with 503 until the laptop collects them. Taps older than 12 hours are dropped. |
| Race-day manager requests waiting | 300, or 1,088 KB in all | New requests are refused with 503 ("The race-day manager is busy - ...") until the laptop collects them. Requests older than 10 minutes are dropped. |
| Race-day manager answers waiting | 300, or 4 MB in all | The oldest answers are forgotten. Answers older than 10 minutes are dropped. |
| Check-in request body | 4 KB | Refused. |
| Results listing | 8 MB | The listing holds the newest results that fit, and says how many older ones were left out (`omitted`). Each is still readable on its own. Old results are only forgotten after a new one has been saved. |
| A club's voice allowance | `voice.monthlyChars` (100,000 by default) a calendar month | The relay refuses to speak, and the club's announcer uses the free Windows voice for every line until the month turns. Characters are reserved before each render and given back if it fails, so concurrent lines can't overshoot. |
| One spoken line | `voice.maxChars` (600 by default) | Refused. The app has a 600-character limit of its own, measured on the spoken words, and the Windows voice says that line. |
| Renders at once | 6, across the whole relay | Refused as busy; the Windows voice says that line and the next is asked for as normal. |
| Voice requests from one club | 300 a minute | Refused as busy. Enough for a laptop's **Prepare Lines** to walk a whole announcer style. |
| Rendered audio kept | 200 MB shared, 25 MB per club | The least recently used are swept. None of it counts against a club's 50 MB. |

Rate limits on PINs and check-in are covered in [Security and privacy](security-and-privacy.md#pins-and-rate-limits).

## Costs

This setup is built to cost as little as Fly allows: one small machine that stops when idle, plus a 1 GB volume. The machine runs while the laptop is publishing or people have the club's pages open, and stops in between, so a club that races one night a week uses it for a small part of the week. The volume is kept whether or not the machine is running. Check Fly.io's current pricing for what that comes to; this page doesn't quote prices because they change.

The HeatCaller voice adds an ElevenLabs bill on top, on your account rather than the clubs'. Two things keep it down: a line is only ever rendered once — the fixed lines once for every club put together — and each club's monthly allowance caps how much new rendering it can ask for. Set a credit limit on the key as well, so the allowances aren't the only thing between a mistake and a large invoice.

<!-- TODO: confirm whether a small relay fits inside Fly's free allowance at time of publishing -->

## Related pages

- [What the online relay is](../online/index.md)
- [Setting up online publishing](../online/setup.md)
- [Online check-in](../online/check-in.md)
- [Security and privacy](security-and-privacy.md)
- [Troubleshooting](troubleshooting.md#online-relay)
