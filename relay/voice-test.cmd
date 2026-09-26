@echo off
setlocal

rem Runs a relay on THIS laptop with the HeatCaller voice switched on, using
rem your own ElevenLabs key, so the whole voice path can be heard before
rem anything is deployed.
rem
rem WHAT IT IS FOR. The HeatCaller voice normally runs on the deployed relay
rem with HeatCaller's own key as a Fly secret. That is the wrong thing to
rem set up first: it needs an upgraded account, a deploy, and a club to test
rem against. This runs the same code here, against a club of its own, and
rem HeatCaller points only its VOICE at it - results, check-in and the
rem race-day manager keep going wherever the Online page says.
rem
rem WHAT IT NEEDS. Node 20 or newer, and your ElevenLabs key on the first
rem line of a file called voice-test.key in this folder. That file is
rem git-ignored and is read into this window only - the key is never written
rem into the config, and the config holds only the hash of the club token.
rem
rem FIRST RUN. It writes relay.config.voice-test.json, builds a catalogue
rem from the voices your ElevenLabs account already has, mints a club token
rem and prints the three values to paste into HeatCaller's Voice Settings.
rem LATER RUNS leave all of that alone and just start the relay.
rem
rem TO THROW IT AWAY: stop it, then delete relay.config.voice-test.json and
rem the data-voice-test folder. Nothing else on this laptop is touched.

cd /d "%~dp0"

rem Cleared, not assumed absent: the relay reads RELAY_CONFIG_JSON in
rem preference to RELAY_CONFIG, so one left in this window from setting
rem the Fly secret would start the REAL config here - against the key
rem below. setlocal keeps this to this window.
set "RELAY_CONFIG_JSON="

where node >nul 2>nul || (
  echo Node isn't on the PATH. Install Node 20 or newer - the README shows how -
  echo then open a NEW terminal window and run this again.
  exit /b 1
)

if not exist node_modules (
  echo Installing the relay's build tools, once...
  call npm install
  if errorlevel 1 (
    echo.
    echo npm install failed, so there is nothing to build. Is this laptop online?
    exit /b 1
  )
)

echo Building...
call npm run build

if errorlevel 1 (
  echo.
  echo The build failed, so the relay was not started.
  exit /b 1
)

rem The key is read HERE, after npm has finished, and not a line earlier.
rem npm hands the whole environment to every package's install scripts, and
rem there is no reason for a third party's postinstall to be given an
rem ElevenLabs key. From here on, only node and the relay see it.
rem
rem Already set in this window? Then that is the key, and the file is not
rem read at all - handy when you would rather not have it on disk.
if not defined RELAY_VOICE_KEY (
  if not exist voice-test.key (
    echo.
    echo No voice-test.key here.
    echo.
    echo Put your ElevenLabs API key on the first line of a file called
    echo voice-test.key in this folder, then run this again. It stays on this
    echo laptop: it is git-ignored, and nothing copies it anywhere.
    echo.
    echo   echo sk-your-key-here^> voice-test.key
    echo.
    exit /b 1
  )

  rem "if defined" is checked as each line runs, not when the block is
  rem parsed, so this takes the FIRST line and ignores the rest. Blank
  rem lines are skipped by for /f already.
  for /f "usebackq delims=" %%K in ("voice-test.key") do (
    if not defined RELAY_VOICE_KEY set "RELAY_VOICE_KEY=%%K"
  )
)

if not defined RELAY_VOICE_KEY (
  echo voice-test.key has nothing in it. Put your ElevenLabs API key on the first line.
  exit /b 1
)

rem Writes the config the first time and prints the token; on later runs it
rem only says what is already there.
node voice-test-setup.mjs relay.config.voice-test.json

if errorlevel 1 (
  echo.
  echo Setting up the test config failed, so the relay was not started.
  exit /b 1
)

set "RELAY_CONFIG=relay.config.voice-test.json"

echo Starting the test relay. Leave this window open - Ctrl+C stops it.
echo.

rem node directly rather than "npm start": Ctrl+C then reaches the relay
rem itself, which closes what is in flight instead of being killed.
node dist/src/index.js
