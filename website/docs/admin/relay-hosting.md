# Hosting the relay on Fly.io

The relay is the small server that gives your club a stable `https://` address for its results, standings, live race and online check-in — and, if you want them, the [HeatCaller voice](#the-heatcaller-voice), so clubs get a natural announcer with no ElevenLabs account of their own, and [AI commentary](#ai-commentary). This page takes you from nothing to a working relay on Fly.io, using a Windows PC, and then covers the day-to-day jobs: adding clubs, replacing a leaked token, updating, and moving to a new address.

You only need this page if you run the relay yourself. If someone else hosts it for your club, all you need from them is the address, your club id and a publish token; see [Setting up online publishing](../online/setup.md).

!!! note
    The laptop running HeatCaller stays the source of truth. The relay keeps the most recent thing each club published and hands it back to anyone with the address. It never connects to the laptop, and it can never control a race. See [Security and privacy](security-and-privacy.md).

## What you'll end up with

- One Fly.io app (the files ship set up for the name `heatcaller-relay`) running one small machine: `shared-cpu-1x` with 256 MB of memory.
- A 1 GB volume called `relay_data`, mounted at `/data`, holding everything the clubs have published. It survives restarts and redeploys.
- The club list and token hashes stored as a Fly secret, never in the image or on the volume.
- A machine that stops when nobody is using it and starts again on the next request.
- Optionally, an ElevenLabs key as a second Fly secret, so the clubs you switch on can use the HeatCaller voice, and an Anthropic key as a third, for AI commentary.

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

If `fly` isn't recognized but `flyctl` is, use `flyctl` wherever this page says `fly`; they are the same program. The `fly-set-config.cmd` script finds whichever one is installed.

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

This is optional, and the relay works exactly as it did without it. Leave the key out and the relay starts, says so in the log, and every club carries on with its own key or the free Kokoro voice.

!!! warning "Deploy again before Hear It and Preview do anything"
    The two buttons that let a club listen to a voice before choosing it — **Hear It** and **Preview**, beside the voice list at the top of **Voice → Cast** — are answered by routes a relay only has once it has been deployed again:

    ```
    fly deploy --ha=false
    ```

    Until you do, the app is told `This relay doesn't offer voice samples yet.`, **Hear It** disappears from the page and a preview is refused. Nothing else about the voice is affected, and a club that has already chosen a voice keeps hearing it. See [Hearing a voice before choosing it](#hearing-a-voice-before-choosing-it).

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
      "modelId": "eleven_v4_turbo",
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
[relay] HeatCaller voice: a catalog is configured but RELAY_VOICE_KEY is not set, so it is switched off
```

### Hearing a voice before choosing it

The catalog can only ever tell a club a voice's name and one short phrase, so a club used to pick the voice its members would hear all season by reading about it. Three routes answer that, and they are the only ones in the relay that sit outside `/api/v1/c/<club>/`:

| Route | Token | What it answers |
|---|---|---|
| `GET /voice/samples` | None | Every catalog voice there is a sample of, each as an `id`, a `name` and a `url`. A relay with no `"voice"` block answers an empty list rather than a 404, so the app can say "there is nothing" instead of "something went wrong". |
| `GET /voice/sample/<id>` | None | That one voice's sample, as MP3 audio. |
| `POST /voice/preview` | The club's publish token | One line of the club's own words, in one catalog voice, as MP3 audio. |

**The first two are open to the internet on purpose.** A club has to be able to hear a voice before it has signed up for anything, so neither takes a token and neither names a club; a laptop with nothing but your relay's address on its **Club Website** page can listen to every voice you offer.

What makes that safe is that nothing a caller sends decides what gets rendered. The words are one fixed, neutral line built into the relay — the same sentence in every voice, so two of them can actually be compared — and the voice has to be one your catalog names. The first visitor to ask for a voice nobody has asked for yet pays for one render of that line on your ElevenLabs account; it is kept in `dataDir/.voice/samples/` and everybody after that is served from there. The ceiling on the whole feature is one short line per catalog voice, ever, so it is a few hundred characters all told. Both routes are held to 60 requests a minute per address. Re-tuning a voice's model or settings changes its fingerprint, so that voice's sample is rendered once more.

A relay whose `RELAY_VOICE_KEY` has been taken away still lists and serves the samples already on its volume: that audio is paid for and still worth hearing.

**Preview carries the club's token, and the token is the club.** There is no club id in its address to check the token against, which is exactly what makes it safe: nothing in the address could name a different club, so there is no mismatch to get wrong, and the only thing the route writes is the preview count of the club its own token names.

Previews are charged to a small **daily** allowance of their own and never to the club's month. A club must not be able to arrive on a race night and find it spent its voice on Tuesday afternoon deciding which one to use. The day is UTC, and each club's is counted in `dataDir/.voice/preview/<club>.json`. Past it the relay answers 429 and says so in words — "That is this club's previews for today - 3,000 characters a day of trying voices out. Its race-night voice allowance has not been touched, and previews start again tomorrow." Nothing falls through to the month. **Hear It** keeps working, because it costs nothing.

Both numbers live in the `"voice"` block beside the catalog:

```json
"voice": {
  "monthlyChars": 100000,
  "maxChars": 600,
  "previewDailyChars": 3000,
  "previewMaxChars": 200,
  "voices": [ ... ]
}
```

`previewDailyChars` is what one club may spend auditioning in a day — by default 3,000 characters, which is about fifteen to thirty previews. Set it to `0` to switch previews off and leave the samples working. `previewMaxChars` is the longest a single preview may be, 200 by default; the relay uses whichever of it and `maxChars` is smaller, so a relay set to speak short lines never promises a longer preview than it will speak. A club that pastes in more than that is refused and told the number.

A preview goes through the same six-renders-at-once cap and the same 300-requests-a-minute-per-club budget as a spoken line, and a club asking for the same sentence in the same voice again is served from `dataDir/.voice/clubs/<club>/` for nothing, like any other cache hit.

### What it costs you

Only a render that isn't already on disk is charged to ElevenLabs and counted against a club's month. Everything else is free:

- **A line with no driver's name in it is rendered once for every club.** The laptop tells the relay when a line is one of the announcer's fixed lines, so "Green flag!" is paid for by the first club that asks and handed to all the rest. Most of a club's fixed lines cost nothing at all.
- **A line with a name in it is cached for that club alone.**
- **A re-roll is charged like a new line.** **Re-roll Line** on the club's **Voice → Voice** tab asks for a fresh take past every cache. It's counted against that club's month and kept in that club's own cache, so other clubs keep hearing the shared take; the club's own copy is used first from then on. An older relay ignores the request and hands back the cached take - redeploy with `fly deploy --ha=false`.
- **Each club's month is a calendar month (UTC)**, counted in `dataDir/.voice/usage/<club>.json`, so a deploy doesn't hand anybody a fresh allowance.
- **The allowance holds under load.** A club's characters are reserved before the render and given back if it fails, so a laptop with several lines in flight at once can't slip past the limit.

Rendered audio lives in `dataDir/.voice/` (a hidden folder, with a dot in front so it can never clash with a club's own folder) — `shared/` for the lines every club may have, `clubs/<club>/` for the rest — content-addressed and swept oldest-used-first at 200 MB shared and 25 MB per club. None of it counts against a club's 50 MB publishing quota.

Renders are capped at six at once across the whole relay, and a club may ask 300 times a minute, which is enough for a laptop's **Prepare Lines** to work through a club's whole set of announcer lines.

### What a club sees when it goes wrong

Every refusal has the same effect on the night: the free Kokoro voice says that line, on the club's own laptop, so a flag call is never silent. The app names the reason in its console and on **Sounds → Mixer**. The ones you can fix from here are "the HeatCaller voice isn't included for this club" (no `"enabled": true`), "the HeatCaller voice isn't switched on for this relay" (no `RELAY_VOICE_KEY`), "that HeatCaller voice isn't available any more" (a voice taken out of the catalog that a club had chosen) and "the relay didn't accept this club's token" (a token replaced but not yet pasted into the club's **Club Website** page).

## AI commentary

The relay can also write the announcer's commentary lines, with Claude on your Anthropic account, so the clubs you switch on need no account or key of their own. The laptop sends the facts it has already worked out (positions, gaps, rivalries) and the model only phrases them. The clubs' side is in [Commentary](../announcer/commentary.md#using-the-relay).

Like the voice, it's optional. A relay without it starts as normal, and each club's laptop writes with the model built into HeatCaller instead, or goes without.

Add a `"brain"` block beside `"clubs"` in `relay.config.json`:

```json
"brain": {
  "model": "claude-haiku-4-5-20251001",
  "monthlyLines": 1000
}
```

Leave out `model` and the relay uses `claude-haiku-4-5-20251001`. With no `"brain"` block at all, the relay doesn't offer commentary. Then switch each club on in its own entry, as with the voice:

```json
"brain": { "enabled": true }
```

A club's own `monthlyLines` is optional and falls back to the block's. Then run `fly-set-config.cmd`.

The Anthropic key is a Fly secret of its own, never written into the config:

```
fly secrets set RELAY_BRAIN_KEY=sk-ant-...
```

Give the key a spend limit in Anthropic's console. A race night of commentary costs cents, but the limit is what stops a mistake. The startup log says which you have:

```
[relay] commentary brain: claude-haiku-4-5-20251001, 3 club(s) included
[relay] commentary brain: a model is configured but RELAY_BRAIN_KEY is not set, so it is switched off
```

Lines are counted per club, a calendar month (UTC) at a time, in `dataDir/.brain/usage/<club>.json`. A line is taken off the month before the model is asked and given back if nothing came of it. Each club may have two lines being written at once. Whatever the relay refuses, the laptop's own model writes that line instead, so commentary never holds up a flag call. See [Monthly allowances and top-ups](#monthly-allowances-and-top-ups).

## The lap voice

**Lap times in my ear**, on the club's **Live** page and a driver's own night (see [Lap times in my ear](../online/index.md#lap-times-in-my-ear)), plays short clips in one ElevenLabs voice for every club. The clips aren't rendered on the fly: you make them once, on your own PC, and they go into the relay's image.

1. Put an ElevenLabs key (on a paid plan, for the commercial license) on the first line of `voice-test.key` in the relay folder.
2. `lap-voice-render.cmd count` says what it would make and what it would cost, and spends nothing. The full set is 2,744 clips and 45,755 characters: lap times, lap numbers, positions, places gained and lost, the gaps to the car ahead, the car behind and the leader, laps or time left, the target words, "off your best", and "Lap times on" and "Lap times off".
3. `lap-voice-render.cmd` makes the missing clips, after you type `yes`. They land in the `lap-voice` folder. Clips already there are never asked for twice.
4. `fly deploy --ha=false` carries the folder into the image.

The relay's start-up log says how many it has: `[lap voice] N of M clips in the Daniel voice.` Without them it says `... so spoken laps use the phone's voice.`, and phones read the laps in their own voice. Deploying from a copy of the relay folder without the clips does the same, so keep the clips in the `lap-voice` folder safe: Git leaves them out, all but one recut clip (`lap6.mp3`).

## Push alerts

A driver who turns alerts on from their own night page gets a notification on their phone, even with the page closed or the phone locked, when their race is next, when it's staged, when their marshal duty is next, and when a result of theirs is filed (a demo race or practice run says so). The drivers' side is in [The online relay](../online/index.md). On an iPhone, alerts only work from the club's pages added to the Home Screen; that is Apple's rule.

There's nothing to set up and no key to buy. The relay makes its own key pair the first time it's needed (the log says `[push] made the relay's push key`) and keeps it on the volume in `.push/vapid.json`. Keep that file: it's what lets a redeploy keep every phone signed up, so it's in every [backup](#backups). The push services are told who runs the relay: `https://<app-name>.fly.dev` on Fly, or whatever you set in `RELAY_PUSH_SUBJECT` elsewhere.

How it works, for anyone checking:

- **Only for clubs offering drivers' nights.** Signing a phone up needs the driver's badge code, as reading their night does, and is refused with the same "Not found" when the club has turned nights off or the code isn't a driver tonight. It shares the night page's limit of 600 requests a minute per address.
- **Only to the push services.** A phone hands over an address at its browser's push service. The relay only accepts `https` addresses at Google's (`fcm.googleapis.com`, `android.googleapis.com`), Mozilla's (`updates.push.services.mozilla.com`, `push.services.mozilla.com`), Apple's (`web.push.apple.com`, `*.push.apple.com`) and Microsoft's (`*.notify.windows.com`), so it never posts anywhere a caller chose.
- **What's kept.** For each driver signed up, under their badge code in the club's folder (`collection/pushsubs/`): each phone's push address and the two keys its browser gave, and when it signed up. No name. At most 5 phones a driver (a sixth replaces the oldest) and 2,000 a club; past that, the phone is told "This club cannot take any more phones for alerts."
- **What's sent.** The alert's title and a line such as the race and grid slot, or the place, encrypted for that one phone, so the push service carries words it can't read. A tap opens the club's `/c/<club>/mynight`; no badge code is in the alert. An alert waits at the push service for 10 minutes at most.
- **When a phone goes.** A phone the push service says has gone (404 or 410) is forgotten. A phone that turns alerts off is taken off. Otherwise sign-ups stay until the club uses **Delete Everything Published**.

The log has a line for each alert a push service accepts, by kind and count only, and one for each it refuses, with the push service's own reason:

```
[push] "acme-rc": hc-race accepted for 2 phones
[push] "acme-rc": web.push.apple.com answered 400 {"reason":"..."}
```

The kinds are `hc-race`, `hc-marshal` and `hc-result`.

## Day to day

### Adding a club

From the relay folder, one command does the build, the mint and the Fly secret:

```
mint-club.cmd new-club New Club
```

It builds the relay, mints the token, writes the club into `relay.config.json` (other clubs untouched), and runs `fly-set-config.cmd`, so the live relay knows the club as soon as Fly has restarted it. Then:

1. Prove the token from outside: `probe.cmd hg_THE_TOKEN new-club https://heatcaller-relay.fly.dev`.
2. Hand the token over the safe way - see [Handing a club its token](#handing-a-club-its-token).
3. On the club's laptop: **Club Website** page, the club id, the address and the token in **Publish token**, then **Test Connection**.

Doing it by hand still works: `node dist/src/index.js mint new-club "New Club" --write`, then `fly-set-config.cmd`.

### Handing a club its token

The token is the club: anyone who has it can publish as that club, delete everything it has published, and spend its voice and commentary allowances. It is shown once, when it is minted, and the relay keeps only its hash. Treat it like a password.

- **Best: type it in yourself.** If you are at the club's laptop (or on a screen share with it), paste the token straight into **Publish token** and click **Test Connection**. It never travels anywhere else.
- **Next best: a password manager's expiring share.** Bitwarden Send, 1Password's share link or similar, set to expire after one view or a day. The link can go by email or text; the token isn't in the message itself.
- **On the phone:** read it out. It's long, so read it in groups of four and have them read it back.
- **Never:** in the body of an email, a text, a chat or a screenshot. Those are kept, forwarded and backed up for years. If that happens, [replace the token](#replacing-a-leaked-or-lost-token) - it takes two minutes.

### Replacing a leaked or lost token

If a token has been shared somewhere it shouldn't be (a chat, a screenshot, an email), replace it:

1. Mint the same club id again: `mint-club.cmd acme-rc` (or by hand: `node dist/src/index.js mint acme-rc --write`, then `fly-set-config.cmd`). The new hash replaces the old one, and the club keeps its name and its roster setting. As soon as the relay restarts with the new config, the old token stops working.
2. Check it: `probe.cmd hg_THE_NEW_TOKEN acme-rc https://heatcaller-relay.fly.dev`.
3. On the laptop's **Club Website** page, paste the new token into **Publish token** and click **Test Connection**.

The club's published data isn't affected. Publishing pauses between step 2 and step 3; the laptop keeps its queue and catches up once the new token is in. Printed QR check-ins keep working, because they don't depend on the token.

### Turning on a club's public roster

A club's roster (who is entered and who is checked in) and its heat line-ups (who is in each heat, on the Schedule page) are only served if **both** the club ticks the roster option on its **Club Website** page **and** the relay's config has `"publishRoster": true` for that club. Without it, the relay takes the names out of the schedule before storing it, and the Schedule page shows the heats without who is in them. Mint never turns it on. To allow it, edit that club's entry in `relay.config.json` by hand, then run `fly-set-config.cmd`. Many drivers are children; turn this on only when the club has decided to.

### Listing a club in the directory

The relay's **Clubs** page (`/clubs`) lists a club only if **both** the club ticks **List this club in the HeatCaller directory** on its **Club Website** page **and** its entry in `relay.config.json` has `"listInDirectory": true`. Like the roster, mint never turns it on and keeps it when a token is re-minted; add the line by hand, then run `fly-set-config.cmd`. The page shows the club's name, a link to its pages, **Racing now** and its next night, never a driver. The relay admin's Clubs snapshot (`admin-snapshot`) reports `listInDirectory` for each club, and `directoryVisits`: how many times a day the **Clubs** page was opened, for the whole relay, over the last 90 days. A view is counted on the relay when the page is served; HEAD requests and bots, crawlers, link previews and monitors are left out. Only the day and the number are kept, in `.directory-visits` on the volume.

### Removing a club

Ask the club to click **Delete Everything Published** on its **Club Website** page first; that erases everything the club has on the relay. Then remove the club's entry from `relay.config.json` and run `fly-set-config.cmd`. Removing the entry alone stops the club publishing but leaves its files on the volume.

<!-- TODO: confirm whether there is a supported way to delete a removed club's files from the volume without its token -->

### Pausing a club

To switch a club off for a while without losing anything, pause it instead: add `"active": false` to its entry in `relay.config.json`, then run `fly-set-config.cmd`. While it's paused:

- every page of the club's shows a "paused" notice, and every request to its addresses is refused, whatever token or PIN it carries;
- its token doesn't work anywhere, the voice preview included; the club's laptop says `This club is paused on the HeatCaller relay. Ask HeatCaller to switch it back on.`;
- its results, roster, usage and top-ups stay on the volume untouched.

Take the line out, or set it to `true`, to switch the club back on with the same token. Anything but `true` or `false` stops the relay at start-up, so a hand-typed `"false"` can't quietly read as on.

### Copyright takedowns

When a copyright notice names something a club published on the relay - its logo, a sponsor's tile, a picture of a track, an achievement's artwork, a race video, a driver's race clip, a crew voice sample or a car photo in a driver's garage - you can take that one item down without touching the rest of the club, and put it back after a counter-notice. The relay stops serving it within a few seconds. Nothing the club's laptop sends is refused or rewritten: it goes on publishing as normal, and the relay leaves the item out wherever it would show, even after the laptop sends it again and even after the club presses **Delete Everything Published** and starts over.

The commands run on the Fly machine through `fly ssh console`, like the top-up commands. Each one takes `--json` too, for the relay admin app.

**1. Find what the notice points at.** Copy the address from the notice and ask the relay what it could be:

```
fly ssh console -C "node /app/dist/src/index.js takedown-find acme-rc https://heatcaller-relay.fly.dev/c/acme-rc/live"
```

It lists what can be taken down at that address, each with its kind and its name: the logo (which is on every page), the sponsors on Home and Live, the track pictures on Home and Tracks, the artwork on Badges, a race's video on that race's results page, and the one file for a clip's or a crew sample's address. A youtube.com or youtu.be address gives that video. It changes nothing.

**2. Take it down.** Give the kind, the name `takedown-find` showed, and a short reference for the notice:

```
fly ssh console -C "node /app/dist/src/index.js takedown acme-rc sponsor sp-7 --notice DMCA-2026-014"
```

The kinds are `logo` (nothing after it), `sponsor`, `track`, `badge`, `video`, `clip`, `crewsample` and `garagephoto`. It answers with the takedown's id, such as `td-3f9a1c2b4d5e`; keep it for a restore. Running it again for the same item gives the same id and changes nothing.

| Kind | What people stop seeing |
|---|---|
| `logo` | The logo on every page, at `logo.png`, and as the picture on shared links (the club's icon is used instead). |
| `sponsor` | That sponsor's whole tile - name, line, link and picture. The club's other sponsors stay. |
| `track` | That track's picture. The track, its records and its laps stay. |
| `badge` | That achievement's artwork. The achievement and who won it stay. |
| `video` | That YouTube video on every race, the live video, a driver's night and family links. The video itself stays on YouTube. |
| `clip` | The clip's address and its place on the driver's night. The file is moved aside, not deleted. |
| `crewsample` | The sample's address. The file is moved aside, not deleted. |
| `garagephoto` | The photo in that driver's garage. |

The notice reference is for your own records: type the reference you gave the notice, never the sender's name, address or email. The relay keeps nothing else about the notice.

Keep the reference to one word, like `DMCA-2026-014`, so nothing in it depends on how your terminal and `fly ssh console` pass quotes along. For a track name with a space or a quote in it, or a restore reason in words, send the text encoded instead: `b64:` and then the text in base64url. In PowerShell:

```
"b64:" + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("Tight Layout")).TrimEnd('=').Replace('+','-').Replace('/','_')
```

**3. Tell the club.** The relay never sends email. Email the club yourself: what was taken down, and a copy of the notice with the sender's telephone number and postal address taken out, as the copyright policy says. Tell them they can send a counter-notice if they think it was a mistake.

**4. Restore it after a counter-notice.** Once a valid counter-notice has been forwarded, and the sender hasn't told you within 10 business days that they have gone to court, put the item back with its id and a reason:

```
fly ssh console -C "node /app/dist/src/index.js takedown-restore acme-rc td-3f9a1c2b4d5e --reason b64:..."
```

It shows again within a few seconds, without the club publishing anything. The record stays, marked restored. A clip or crew sample taken down before the club pressed **Delete Everything Published** is gone with the rest of what the club deleted, so there's nothing to put back. A taken-down clip also goes when any clip would: after 7 days, when the club stops offering drivers their nights, or when its driver is forgotten.

**See every takedown.** `takedown-list` shows every club's takedowns, or one club's with `takedown-list acme-rc`: what, when, the notice reference, and whether each was restored. For each club it also counts the notices in the last 12 months that weren't countered. At three, the line says REPEAT: that is the policy's repeat-infringer line, and it is only a flag. The relay never closes or pauses a club by itself; that's your decision. One notice naming three items counts once.

`takedown` and `takedown-restore` each add a line to the shared audit log, which the relay admin app shows. The records are kept in `.takedowns/<club>.json` on the volume, and a taken-down clip or sample in `.takedowns/held/`.

!!! note
    Phones and browsers may still have a copy for a while. A logo or clip they already loaded can stay in their cache for up to an hour, a crew sample for up to a day, and the club's pages on a phone keep their own copy of what they last showed for up to 12 hours. The relay can't reach those copies; they go when they run out or the page next loads.

**Not covered one at a time:** the Listen audio and the captions (ask the club to switch Listen off, or pause the club), driver photos on the check-in list, and anything the club typed rather than uploaded. For those, ask the club to remove it, which is the fastest way anyway, or pause the club.

### Monthly allowances and top-ups

Each club's AI commentary is counted in lines and its HeatCaller voice in characters, a calendar month (UTC) at a time. The clubs see their own figures in HeatCaller's **This Month** window, on the pages that use them and on the manager's phone page, and can press **Ask for More** when one runs low. See [This month: every allowance in one place](../announcer/commentary.md#this-month-every-allowance-in-one-place). Those figures come from routes a relay only has once it has been deployed again; until then a laptop's This Month window says "This relay can't show the monthly allowances yet."

These commands, run from the relay folder, look after the allowances and the requests. Each runs the relay's own code on the Fly machine through `fly ssh console`, so all it needs is flyctl signed in to the account that runs the relay. It wakes the machine first if it has stopped itself. There's deliberately no web address for any of them: every other route names one club and answers only that club's token.

```
voice-usage.cmd                    every club with the voice: this month so far, and the three before
voice-usage.cmd --json             every club and its whole history, as JSON
voice-topup.cmd acme-rc 50000      50,000 more characters for acme-rc, this month only
voice-topup.cmd acme-rc -20000     takes 20,000 of that back (never below no top-up)
brain-usage.cmd                    every club with AI commentary: this month so far, and the three before
brain-usage.cmd --json             the same with every club's whole history, as JSON
brain-topup.cmd acme-rc 200        200 more lines for acme-rc, this month only
brain-topup.cmd acme-rc -100       takes 100 of that back (never below no top-up)
topup-requests.cmd                 the clubs that have asked for more this month and are still waiting
topup-requests.cmd --json          the same, as JSON
```

`brain-usage.cmd` prints, for example:

```
AI commentary - October 2026, so far. Counts go back to zero on Sun, 01 Nov 2026 00:00:00 UTC.
Read from /data.

club     brain  plan  top-up  used  left    %  Sep 26  Aug 26  Jul 26
acme-rc  on      400      50   212   238  47%     388     301       0

All clubs: 212 lines this month.
```

It says which data folder it read, so a report from the wrong one isn't mistaken for a quiet month.

`topup-requests.cmd` lists who has asked:

```
Top-up requests still open - October 2026.
Read from /data.

acme-rc  commentary  2026-10-19 20:01:00 UTC  "Big night on Friday"

Answer with brain-topup <club> <lines> or voice-topup <club> <characters>; either marks the club's request done.
```

To answer a request:

1. Run `topup-requests.cmd` to see who is waiting, and for what (`commentary` or `voice`).
2. Give the top-up: `brain-topup.cmd <club> <lines>` for commentary, `voice-topup.cmd <club> <characters>` for the voice. Write the number without commas: in a Windows command a comma splits it in two, so `brain-topup.cmd acme-rc 2,000` is refused rather than giving the club 2 lines.
3. The command says what the club may now use, and "Marked acme-rc's request for more lines done." when it has closed an open request. Only a positive top-up closes one; taking lines back doesn't.

A top-up lasts the month it's given in, on top of the club's plan (`brain.monthlyLines`, or `voice.monthlyChars`), and the relay picks it up on the club's next line with no restart. A laptop that ran out is back on the relay within minutes: it checks the figures every five minutes, and sends one line through to the relay about every half hour anyway. One commentary top-up is at most 1,000,000 lines either way, so a typo with extra zeros is refused. A club that needs more every month wants a bigger `brain.monthlyLines` on its own entry in the config instead (then `fly-set-config.cmd`). If commentary isn't switched on for the club, the command says so: the lines do nothing until it is.

**The relay never sends email.** A request is only noted on the relay. The person is told by the club's own HeatCaller, which emails support@heatcaller.com from the club's mailbox, or opens the club's email program with the request written out; the manager's phone page offers a ready-made email link beside its **Ask for More**. So `topup-requests.cmd` is the list to work from, and the emails are only a nudge. A club may make at most 10 requests a month, and a second request for the same allowance while one is still open is answered with the open one rather than stored again.

To turn a request down without giving anything, run `fly ssh console -C "node /app/dist/src/index.js topup-decline <club> <voice|commentary>"`. It marks the club's open request for that allowance answered.

**Automatic top-ups.** A club's entry can let the relay answer its requests by itself, up to a limit, so a club that runs out on a race night isn't left waiting on you:

```json
"autoTopUp": { "voice": { "amount": 25000, "perMonth": 2 },
               "commentary": { "amount": 200, "perMonth": 1 } }
```

When the club asks for more having used 90% or more of that allowance, the relay gives `amount` for the month at once, at most `perMonth` times a calendar month, and only when that allowance is switched on for the club. Requests after that wait for you as before. A rule outside 1 to 10 a month, or bigger than one top-up may give, stops the relay at start-up. What it gives is kept in `.requests/<club>.auto.json`, apart from your own top-ups.

The data, all under the relay's data folder:

| Path | What it holds | Written by |
|---|---|---|
| `.brain/usage/<club>.json` | The club's commentary lines this month, and the months before. | The relay |
| `.brain/topups/<club>.json` | Commentary lines given on top of the plan, by month. | `brain-topup` only. The relay only reads it. |
| `.voice/topups/<club>.json` | Voice characters given on top of the plan, by month. | `voice-topup` only. |
| `.requests/<club>.json` | The club's requests for more, by month (kept for a year). | The relay only. |
| `.requests/done/<club>.json` | Which of those requests have been answered. | `brain-topup`, `voice-topup` and `topup-decline` only. |
| `.requests/<club>.auto.json` | What automatic top-ups have given, by month. | The relay only. |
| `.usage-log/<YYYY-MM>.jsonl` | One line per voice and commentary request: the club, what it cost, how it went. No words, names or facts are ever kept. Kept 24 months. | The relay only. |
| `.app-usage/<YYYY-MM>.jsonl` | Laptops' daily usage totals, from clubs that tick the box: which pages and tabs were used and for how long. No names, nothing typed. Kept 13 months; `app-usage-forget <club>` deletes a club's. | The relay only. |
| `.clips/<club>/` | A driver's own race calls for their private night page: `<id>.mp3` beside `<id>.json`. Each is removed after 7 days. | The relay only. |
| `.push/vapid.json` | The relay's own key pair for [push alerts](#push-alerts), made the first time it's needed. | The relay only. |
| `<club>/collection/pushsubs/` | The phones signed up for each driver's alerts. | The relay only. |
| `<club>/snapshot/sponsorviews.json` | How many phones saw each sponsor on the club's pages, and how many tapped through, by the hour, for the last 45 days. | The relay only. |
| `<club>/collection/driverphotos/` | Car photos sent from drivers' phones, waiting for the laptop to collect them. | The relay only. |
| `.takedowns/<club>.json` | The club's [copyright takedowns](#copyright-takedowns): what, when, the notice reference, and whether each was restored. Kept when the club deletes everything. | `takedown` and `takedown-restore` only. The relay only reads it. |
| `.takedowns/held/<club>/` | Clips and crew samples taken down, kept for a restore. A clip still goes after 7 days, like any clip. | `takedown` and `takedown-restore`. The relay only removes: a club's, when it deletes everything, and clips, by the clip rules. |

Each file has one writer, so the running relay and a command run at the same moment can never save over each other. Run through ssh, the commands run as root; they hand every file and folder they make to the relay's own user, and refuse to run without `RELAY_DATA`, which is where the relay's data really is.

### Updating the relay

Get the new relay code, then from the relay folder:

```
npm install
npm test
fly deploy --ha=false
```

The volume, and every published result on it, survives the deploy. The lap voice's clips in the `lap-voice` folder go into the image with every deploy (see [The lap voice](#the-lap-voice)). Since 28 Oct 2026 the relay takes no podium videos: they stay on the club's laptop. The first start of that build deletes any it kept before, the whole `/data/.videos` folder, once, and says so in `fly logs` (`[relay] removed the old podium videos: ...`). If the laptop reports `The relay is an older build that cannot take this. Redeploy it.`, this is the fix.

!!! note "HeatCaller 0.59.0: redeploy the relay first"
    Deploy the relay before any laptop runs 0.59.0: the new relay reads both the old and the new, salted PIN hashes, and the old one can't read the new ones. It also brings driver sign-up, limits on bulk result reads, per-club voice limits and tighter file permissions, and it signs every check-in, manager and sign-up pass with a secret of its own - so check-in desks, race-day managers and drivers waiting on a sign-up enter their PIN or invite code once more afterwards. It also adds the two open voice-sample routes and the **Preview** button's own daily allowance, so **Hear It** and **Preview** at the top of **Voice → Cast** do nothing until it is deployed — see [Hearing a voice before choosing it](#hearing-a-voice-before-choosing-it). A relay still on an older Fly app name moves first: see [Moving to a new app name](#moving-to-a-new-app-name). From the relay folder, run `npm test`, then `fly deploy --ha=false`. See [0.59.0](../release-notes.md#0590) in the release notes.

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
| `[relay] WARNING "x": 150 wrong check-in PINs in a day, paced to stay under the ten-minute limit - ...` | Someone is guessing slowly on purpose. PIN sign-in is closed for 12 hours, and again after each wrong PIN until the day's guesses age out. Changing the staff PIN reopens it. |
| `[relay] "x": the staff PIN changed, so its wrong-PIN count starts again.` | A new staff PIN was published; any lockout is lifted. |
| `[relay] "x": could not save the wrong-PIN count (...)` | The volume is full or unwritable. The count is kept in memory until the relay restarts. |
| `[relay] HeatCaller voice: ...` / `[relay] commentary brain: ...` | Whether the voice and AI commentary are on, and for how many clubs. See [The key](#the-key) and [AI commentary](#ai-commentary). |
| `[lap voice] ...` | How many lap voice clips the relay has. See [The lap voice](#the-lap-voice). |
| `[push] ...` | An alert a push service accepted or refused, or the relay's push key being made. See [Push alerts](#push-alerts). |
| `[relay] forwarding every request to https://...` | This app is a forwarder (see below). |

### The machine sleeps when idle

The machine stops when there's no traffic and starts on the next request. The first request after a quiet spell takes a second or two longer. The laptop's publisher rides this out with its normal retries.

## Running it for several clubs

One relay serves every club; each one is a separate folder on the volume and a separate token. What changes as more clubs join is that the relay is now something other people rely on. Four things make that safe.

### The checklist for a new club

1. **Mint** the club: `mint-club.cmd <club-id> <Club Name>` (see [Adding a club](#adding-a-club)).
2. **Probe** it from outside: `probe.cmd hg_THE_TOKEN <club-id> https://heatcaller-relay.fly.dev`.
3. **Hand over the token** without putting it in an email - see [Handing a club its token](#handing-a-club-its-token).
4. **Decide the roster**: leave `publishRoster` off unless the club has decided to publish names (see [Turning on a club's public roster](#turning-on-a-clubs-public-roster)).
5. **Switch on what it gets** (`"voice": { "enabled": true }`, `"brain": { "enabled": true }`), and **set its allowances** if it isn't on the defaults (`voice.monthlyChars`, `brain.monthlyLines`, `autoTopUp`), then `fly-set-config.cmd`.
6. **Take a backup** (below), so the first copy with the new club in it exists before its first night.
7. **Note it down**: club id, name, contact, the date minted. The config holds the id and name; who to call when it breaks is yours to keep.

Re-minting the old `horizon-test` token, which appeared in a chat, is the same thing done once for the test club:

```
mint-club.cmd horizon-test
probe.cmd hg_THE_NEW_TOKEN horizon-test https://heatcaller-relay.fly.dev
```

Then paste the new token into **Publish token** on any laptop that publishes as `horizon-test`.

### Knowing when it's down

Fly checks the machine itself (`GET /healthz` every 30 seconds, in `fly.toml`) and restarts it if it stops answering, but it doesn't tell you. For that, the repository has a scheduled GitHub Action, `.github/workflows/relay-uptime.yml`, that asks `https://heatcaller-relay.fly.dev/healthz` and **fails when the relay doesn't answer** after three tries. GitHub emails you when a scheduled run fails, so the failure is the alert.

- It checks every 20 minutes from 21:00 to 04:59 UTC (5pm to 1am in New York in summer), when clubs race, and every four hours otherwise. Each check wakes the machine if it's asleep, and each counts as a GitHub Actions minute on a private repository - about 28 a day. Change the `cron` lines to check more or less often.
- If the relay moves, set a repository variable `RELAY_URL` (**Settings → Secrets and variables → Actions → Variables**) to the new address. No secret is needed: `/healthz` is public and says only `{"ok":true,"clubs":N}`.
- **Run it now** from the **Actions** tab (**Relay uptime → Run workflow**) to check it works.
- Make sure GitHub is allowed to email you about failed runs: **GitHub Settings → Notifications → Actions**.

### Backups

Two layers, because they protect against different things.

**Fly's own snapshots.** Fly takes a snapshot of the volume once a day and keeps it for 5 days by default. That covers a corrupted or deleted volume. Check them, and keep them longer:

```
fly volumes list
fly volumes snapshots list <volume-id>
fly volumes update <volume-id> --snapshot-retention 14
```

To restore one, make a new volume from it in the same region, then move the machine onto it:

```
fly volumes create relay_data --snapshot-id <snapshot-id> --region ewr --size 1
fly machine list
fly machine destroy <machine-id> --force
fly deploy --ha=false
```

The new deploy mounts a `relay_data` volume; with the old volume still there it may pick either, so destroy the old volume (`fly volumes destroy <old-volume-id>`) once you have checked the restored one has the data. Snapshots are stored by Fly and billed at its current rate.

**A copy on your own PC.** Snapshots live in the same Fly account as the relay, so they don't help if the account is the problem. `backup.cmd` in the relay folder copies the whole volume down:

```
backup.cmd
```

It wakes the machine, packs `/data` into one file there, downloads it to `relay-backups\<date>_<time>\relay-data.tgz` and deletes the packed file from the machine. That file is every club's published results, nights, garages, race calls, the phones signed up for alerts with the relay's push key, and the voice and commentary usage files clubs are billed by. Take one before a deploy that changes storage, once a week while clubs are racing, and before adding or removing a club; keep a copy somewhere other than the race PC.

To put a backup back onto a volume (it replaces what is there):

```
fly ssh sftp put relay-backups\<date>_<time>\relay-data.tgz /tmp/relay-data.tgz
fly ssh console -C "tar xzf /tmp/relay-data.tgz -C /data"
fly machine restart
```

### Sizing

One `shared-cpu-1x` machine with 256 MB and a 1 GB volume is enough for a handful of clubs racing on different nights. What to watch, and what to do:

| Watch | Where | When it's time | Do this |
|---|---|---|---|
| Memory | `fly logs` (an out-of-memory restart), `fly status` | Restarts during a race night | `fly scale memory 512` |
| CPU | Fly dashboard → Metrics | Pages slow while two or more clubs race at once | `fly scale vm shared-cpu-2x` |
| Volume space | `fly ssh console -C "df -h /data"` | Over about 70% | `fly volumes extend <volume-id> --size 3` |
| Requests at once | `fly logs` (busy or 503 answers) | Several clubs' Live Results open on TVs at once | Raise `[http_service.concurrency]` in `fly.toml`, then deploy |

Stay on **one machine** whatever happens (`--ha=false`): the relay's data is one volume, and two machines would each have half of it. Scale it up, not out.

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

Those three go into **Voice → Voice**, in the **Voice relay:**, **Club:** and **Token:** boxes under the HeatCaller voice — not the **Club Website** page. Only the voice moves; results, check-in and the race-day manager keep going to the hosted relay. See [Sending the voice to a different relay](../announcer/voice.md#sending-the-voice-to-a-different-relay).

Three things it's careful about, because it's your own credits:

- **The key is never written into the config.** `voice-test.key` is git-ignored, and the config holds only the club token's hash, the same as a real relay's.
- **Its own data folder** (`data-voice-test/`) and its own allowance — 20,000 characters, not 100,000 — so a runaway **Prepare Lines** stops long before it gets expensive. Both are in the config if you want more.
- **`eleven_v4_turbo`**, the model HeatCaller and the relay use, at half v4's price.

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
| `clubs[].publishRoster` | Whether the club's roster and heat line-ups may be served publicly. | `false` |
| `clubs[].active` | `false` pauses the club. See [Pausing a club](#pausing-a-club). | `true` |
| `clubs[].brain.enabled` | Whether this club may use AI commentary on the relay. | `false` |
| `clubs[].brain.monthlyLines` | This club's commentary lines a calendar month. 0 to 1,000,000. | `brain.monthlyLines` |
| `clubs[].autoTopUp` | Automatic top-ups: `voice` and/or `commentary`, each an `amount` and a `perMonth` from 1 to 10. See [Monthly allowances and top-ups](#monthly-allowances-and-top-ups). | none |
| `clubs[].voice.enabled` | Whether this club may use the HeatCaller voice. | `false` |
| `clubs[].voice.monthlyChars` | This club's characters a calendar month. 0 to 100,000,000. | `voice.monthlyChars` |
| `clubs[].voice.voices` | Catalog ids this club may use. Omitted or empty means the whole catalog. Every id must be in it. | all of them |
| `voice.voices[]` | The announcer voices on offer. Each needs an `id` (lower-case letters, digits and hyphens, up to 24 characters) and a `voiceId`; `name`, `what`, `modelId`, `stability`, `similarity`, `style`, `speakerBoost`, `speed` and `outputFormat` are optional. | none |
| `voice.monthlyChars` | The default allowance per club, a calendar month. 0 to 100,000,000. | `100000` |
| `voice.maxChars` | Longest line the relay will speak at once. 40 to 5,000. | `600` |
| `voice.previewDailyChars` | Characters one club may spend on **Preview** in a day (UTC), kept apart from its month. 0 to 1,000,000; `0` switches previews off and leaves the samples working. | `3000` |
| `voice.previewMaxChars` | Longest one preview may be. 40 to 600. The relay uses this or `maxChars`, whichever is smaller. | `200` |
| `brain.model` | The Anthropic model that writes commentary. With no `"brain"` block, the relay offers no commentary. | `claude-haiku-4-5-20251001` |
| `brain.monthlyLines` | The default commentary allowance per club, a calendar month. 0 to 1,000,000. | `1000` |
| `brain.maxWords` | Longest ordinary line, in words. 10 to 500. | `120` |
| `brain.maxLongWords` | Longest recap or preview, in words. 10 to 1,000. | `450` |

Leave `maxBodyBytes` at its default unless you have a reason. HeatCaller sends documents of up to 900 KB; a lower limit makes the relay refuse some of them.

A voice's optional settings default to `eleven_v4_turbo`, stability 0.5, similarity 0.75, style 0, speaker boost on, speed 1 and the account's own output format. `name` falls back to the `id`.

### Environment variables

| Variable | What it does |
|---|---|
| `RELAY_CONFIG_JSON` | The whole config as one JSON string. Used instead of the file when set. On Fly this is the secret `fly-set-config.cmd` sets. |
| `RELAY_CONFIG` | Path to the config file. Default `./relay.config.json`. |
| `RELAY_VOICE_KEY` | The ElevenLabs key behind the HeatCaller voice. Set on its own, never in the config. Without it the relay starts and doesn't offer the voice. See [The HeatCaller voice](#the-heatcaller-voice). |
| `RELAY_BRAIN_KEY` | The Anthropic key behind AI commentary. Set on its own, never in the config. Without it the relay starts and doesn't offer commentary. See [AI commentary](#ai-commentary). |
| `PORT` | Listen port. Overrides `port` in the config. |
| `RELAY_DATA` | Data folder. Overrides `dataDir` in the config. `fly.toml` sets it to `/data`. |
| `RELAY_UID` / `RELAY_GID` | The user and group the relay switches to after starting as root. Default 1000, the `node` user in the image. |
| `RELAY_REDIRECT_ORIGIN` | Turns the relay into a forwarder. See [How the forwarder behaves](#how-the-forwarder-behaves). |
| `RELAY_PUSH_SUBJECT` | Who runs the relay, as an `https://` address or a `mailto:`, for the push services behind [push alerts](#push-alerts). Not needed on Fly, where the relay uses `https://<app-name>.fly.dev` from Fly's own `FLY_APP_NAME`; elsewhere it falls back to `https://localhost`. |

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
| `Club "..." in ... names a voice "..." that the catalog does not have.` | Correct that club's `voice.voices`, or add the voice to the catalog. |
| `"voice" in ... must be an object.` / `"voice.voices" in ... must be a list.` / the same for a club | Correct the shape of the block. A club's `voice.voices` is a list of ids, not of objects. |
| `"voice.previewDailyChars" in ... must be a whole number from 0 to 1000000` / `"voice.previewMaxChars" in ... must be a whole number from 40 to 600` | Correct or remove that number. |
| `"brain" in ... must be an object.` / `"brain.monthlyLines" in ... must be a whole number from 0 to 1000000` (and the same for `maxWords`, `maxLongWords` and a club's `brain`) | Correct the block or the number. |
| `"active" for ... in ... must be true or false` | Write `true` or `false` without quotes, or take the line out. |
| `"autoTopUp" for ... in ...` / `"autoTopUp.voice" for ... needs a whole "amount" ...` | Correct the club's automatic top-up rule. |
| `Club N in ... needs at least "club" and "tokenSha256".` | Complete that club's entry, or re-mint it with `--write`. |

To check a config before you push it, run `node dist/src/index.js check-config relay.config.json` in the relay folder (after `npm run build`). It reads the file exactly as the relay does at start and prints `{"ok":true,"clubs":N,"active":N}`, or `{"ok":false,"error":"..."}` with the message the relay would have stopped on.

## Limits

| Limit | Value | What happens past it |
|---|---|---|
| Request body | `maxBodyBytes` (1 MiB by default) | Refused with 413. |
| One result document | 256 KB (or `maxBodyBytes` if smaller) | Refused with 413. |
| Results kept per club | Every one, until the club's storage is full | There's no count limit; only the 50 MB below makes old results go, oldest first. The laptop keeps every result. |
| Storage per club | 50 MB, not counting the race-day manager's requests and answers, driver sign-ups, what phones send (check-in taps, votes, shout-outs, driver requests and car photos), or anything with a budget of its own (the results index, drivers' nights and garages, the listen-along audio) | The oldest results are forgotten to make room. Only a document that wouldn't fit even then is refused, with 507. Live race frames and other snapshots are never refused for space. |
| Results index | 2 MB of its own, at most 1 MB for one year | The earliest year is forgotten to make room for a later one. An earlier year that wouldn't fit is refused with 507, rather than pushing out a later season. |
| Check-in taps waiting | 500 | New taps are refused with 503 until the laptop collects them. Taps older than 12 hours are dropped. |
| Race-day manager requests waiting | 300, or 952 KB in all | New requests are refused with 503 ("The race-day manager is busy - ...") until the laptop collects them. Requests older than 10 minutes are dropped. |
| Race-day manager answers waiting | 300, or 4 MB in all | The oldest answers are forgotten. Answers older than 10 minutes are dropped. |
| Drivers' nights | 4 MB of their own, at most 400, 64 KB each | The oldest are forgotten to make room. |
| Drivers' garages | 24 MB of their own, at most 400, 512 KB each | The oldest are forgotten to make room. |
| Car photos from phones | 450 KB each once the phone has shrunk it, JPEG only; 6 waiting per driver and 40 per club; 30 an hour per driver | Refused. Past the waiting limits the phone says "HeatCaller has not picked up the photos already sent - is the timing desk's laptop on? Try again in a minute." A photo waits until the laptop collects it. |
| Garage changes from one driver (packs, cars, setups) | 120 an hour | Refused with 429: "That is a lot of changes in one hour - wait a little and try again." |
| Phones signed up for alerts | 5 per driver, 2,000 per club | A sixth phone replaces a driver's oldest; past the club's ceiling, refused with 507. |
| Check-in request body | 4 KB | Refused. |
| Results listing | 8 MB | The listing holds the newest results that fit, and says how many older ones were left out (`omitted`). Each is still readable on its own. Old results are only forgotten after a new one has been saved. |
| A club's voice allowance | `voice.monthlyChars` (100,000 by default) a calendar month | The relay refuses to speak, and the club's announcer uses the free Kokoro voice for every line until the month turns. Characters are reserved before each render and given back if it fails, so concurrent lines can't overshoot. |
| A club's AI commentary | `brain.monthlyLines` plus this month's `brain-topup` and automatic top-ups, a calendar month | The relay refuses to write; the club's laptop switches to the model on that PC by itself, and stops sending lines to be refused. See [Monthly allowances and top-ups](#monthly-allowances-and-top-ups). |
| Top-up requests from one club | 10 a month, open and answered together | Refused with 429. A second request for an allowance that already has one open is answered with that one. |
| One spoken line | `voice.maxChars` (600 by default) | Refused. The app has a 600-character limit of its own, measured on the spoken words, and the Kokoro voice says that line. |
| Renders at once | 6, across the whole relay | Refused as busy; the Kokoro voice says that line and the next is asked for as normal. |
| Voice requests from one club | 300 a minute | Refused as busy. Previews are counted in the same budget. Enough for a laptop's **Prepare Lines** to work through a club's whole set of announcer lines. |
| A club's preview allowance | `voice.previewDailyChars` (3,000 by default) a day (UTC) | Refused with 429, in words the app shows. The club's monthly voice allowance is untouched, and **Hear It** still works. |
| One preview | `voice.previewMaxChars` (200 by default), or `voice.maxChars` if that is smaller | Refused, with the limit in the message. |
| The open voice-sample routes | 60 requests a minute per address | Refused with 429. No token is involved, so the address is all the relay knows about the caller. |
| Rendered audio kept | 200 MB shared, 25 MB per club | The least recently used are swept. None of it counts against a club's 50 MB. |

Rate limits on PINs and check-in are covered in [Security and privacy](security-and-privacy.md#pins-and-rate-limits).

## Costs

This setup is built to cost as little as Fly allows: one small machine that stops when idle, plus a 1 GB volume. Podium videos never go on it: since 28 Oct 2026 they stay on each club's laptop. The machine runs while the laptop is publishing or people have the club's pages open, and stops in between, so a club that races one night a week uses it for a small part of the week. The volume is kept whether or not the machine is running. Check Fly.io's current pricing for what that comes to; this page doesn't quote prices because they change.

The HeatCaller voice adds an ElevenLabs bill on top, on your account rather than the clubs'. Two things keep it down: a line is only ever rendered once — the fixed lines once for every club put together — and each club's monthly allowance caps how much new rendering it can ask for. Set a credit limit on the key as well, so the allowances aren't the only thing between a mistake and a large invoice.

AI commentary adds an Anthropic bill, also on your account. It's much the smaller of the two: a race night of commentary is cents. The per-club line allowance and a spend limit on the key keep it that way. The lap voice's clips cost one ElevenLabs render each, once (`lap-voice-render.cmd count` says how much before you spend anything).

<!-- TODO: confirm whether a small relay fits inside Fly's free allowance at time of publishing -->

## Related pages

- [What the online relay is](../online/index.md)
- [Setting up online publishing](../online/setup.md)
- [Online check-in](../online/check-in.md)
- [Security and privacy](security-and-privacy.md)
- [Troubleshooting](troubleshooting.md#online-relay)
