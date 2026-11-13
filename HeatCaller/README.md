# HeatCaller

## What this is

A Windows Forms (.NET 10) race-timing and announcing app for RC car clubs.
It reads the lap decoder itself, runs the race (staging, starts, flags,
penalties, qualifying, mains, championships), keeps and corrects the
results, publishes them to phones and the club's website, and calls the
race out loud with an announcer, AI commentary and soundscapes.

Version **0.62.0 BETA** (the one number is `<Version>` in
`HeatCaller\HeatCaller.csproj`; `AppVersion` reads it back from the
assembly). What changed in each release is in
`..\website\docs\release-notes.md`.

This file began as the notes for a read-only proof of concept that dumped
messages from Next Level Timing's Neon Timing API. That connection was
retired in 0.50.0 (see `DecoderConnection.RetiredNeonApi`, kept only so an
old saved `0` still loads); HeatCaller has talked to the decoder directly
ever since.

## Decoders

Chosen on the Connection page; the list is `DecoderConnection.Offered` in
`HeatCaller.Timing\DecoderConnection.cs`.

| Decoder | How | Status |
|---|---|---|
| EasyLap | USB HID (`DirectDecoderClient`, `EasyLapFramer`) | Proven at home rehearsals |
| I-Lap | Serial (`ILapClient`) | Proven on real hardware, split times and all |
| MyLaps / AMB P3 (AMBrc, RC4, TranX3, ChipX) | Network (port 5403) or serial | Beta |
| ZRound open protocol / RiCino | Serial or network | Beta |
| Cano / RCHourglass | Serial | Beta |
| OpenStint | ZeroMQ (HeatCaller's own ZMTP 3.0 subscriber) | Beta |
| EasyLap / Robitronic serial | Serial | Beta |

The five Beta decoders (added 1 Nov 2026, `plans\decoder-support-plan.md`)
were written from published formats and run by `StreamDecoderClient`; each
keeps "(Beta)" until it has timed a real session on real hardware
(`DecoderConnection.Beta`). Each also has a simulated mode for rehearsing
without hardware, and their raw traffic is captured to `decoder-raw-*.log`
for reports.

## Project layout

```
HeatCaller.sln
HeatCaller/            the WinForms app (net10.0-windows), ~600 source files
  ROADMAP.md           the live roadmap: where things stand, what's next, gotchas
  ROADMAP-1.md         archived snapshot of 27 Sep 2026, not maintained
  Assets/              fonts, sound packs, music, icons, legal text; Kokoro and
                       the commentary model are fetched, not in git
HeatCaller.Timing/     timing in a plain net10.0 library with no WinForms:
                       RaceControlSession, the decoder protocols, journal, host
HeatCaller.Tests/      xUnit tests (see its README)
```

Beside this folder, at the top of the repo: `relay\` (the TypeScript relay
on Fly.io that gives a club its https address and phone pages), `website\`
(the MkDocs user guide), `plans\` (design plans) and the scripts below.

Main packages: NAudio, HidSharp, System.IO.Ports, QRCoder, KokoroSharp
(the free local voice), LLamaSharp (the built-in commentary model),
LibVLCSharp (video) and Velopack (installer and updates).

## Building

Requires the .NET 10 SDK and Windows (WinForms). From the repo root:

```
dotnet run --project HeatCaller\HeatCaller\HeatCaller.csproj
```

The first build pulls the NuGet packages. A source build looks for the
Kokoro voice in `%LocalAppData%\HeatCaller\Kokoro` and offers to download
it.

Scripts at the repo root:

- `check.cmd` builds and tests the app and the relay (`check.cmd app`,
  `check.cmd relay` for one). A change isn't done until it ends with
  **ALL GREEN**. The full app suite is long (tens of minutes).
- `layout.cmd` writes the layout report with pictures of every page and
  window (see `HeatCaller.Tests\README.md`).
- `fetch-kokoro.cmd` and `fetch-llm.cmd` fetch the voice (~310 MB) and the
  commentary model (~440 MB) into `Assets\` once, before publishing.
- `publish.cmd` cuts the self-contained build; `pack.cmd` packs and signs it
  as a Velopack release. Only ever publish from a green `check.cmd`.

## Using it

The user guide is in `..\website\docs` (start with
`getting-started\install.md` and `first-race-night.md`). Where things stand,
and what to read before changing anything, is in `HeatCaller\ROADMAP.md`.
