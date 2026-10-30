@echo off
rem Puts relay.config.json into the Fly app as the RELAY_CONFIG_JSON secret.
rem
rem Run it from the relay folder after every change to relay.config.json -
rem adding a club, re-minting a token - and Fly restarts the machine with
rem the new config on its own. The config never touches the image or the
rem volume; a token hash belongs in a secret.
rem
rem Node does the quoting: it reads the file, checks it parses, and prints
rem it as ONE line for "fly secrets import" - which is the only way to get
rem JSON through a Windows shell with its quotes intact.
rem
rem THE TWO KEYS ARE NOT IN HERE AND ARE NOT SET BY THIS.
rem
rem RELAY_VOICE_KEY (ElevenLabs, for the HeatCaller voice) and
rem RELAY_BRAIN_KEY (Anthropic, for the commentary the relay writes) are
rem secrets of their own, set once by hand:
rem
rem   fly secrets set RELAY_VOICE_KEY=sk_...
rem   fly secrets set RELAY_BRAIN_KEY=sk-ant-...
rem
rem They are rotated on their own schedule and must never land in the file
rem a club's token hash is pasted into - which is the file this script
rem sends. Without either key the relay still starts and says which of
rem them is missing; clubs then speak with their own ElevenLabs key or the
rem free Kokoro voice, and write commentary with the model built into
rem HeatCaller or not at all.

if not exist relay.config.json (
  echo No relay.config.json here. Run this from the relay folder, and mint a club first if you have none.
  exit /b 1
)

rem "fly" and "flyctl" are the same program; installers differ in which
rem name they put on the PATH, so this takes whichever is there. A PATH
rem the installer just changed is only seen by NEW terminal windows.
set "FLY=fly"
where fly >nul 2>nul || set "FLY=flyctl"
where %FLY% >nul 2>nul || (
  echo Neither "fly" nor "flyctl" is on the PATH. Install flyctl - the README shows how -
  echo then open a NEW terminal window and run this again.
  exit /b 1
)

node -e "const fs=require('fs');let cfg;try{cfg=JSON.parse(fs.readFileSync('relay.config.json','utf8'))}catch(e){console.error('relay.config.json is not readable JSON: '+e.message);process.exit(1)}process.stdout.write('RELAY_CONFIG_JSON='+JSON.stringify(cfg)+String.fromCharCode(10))" | %FLY% secrets import

if errorlevel 1 (
  echo.
  echo The secret was NOT set. Is flyctl installed and signed in, and is this the right app folder?
  exit /b 1
)

echo.
echo Done - the relay restarts itself with the new config in a few seconds.
echo.
echo Watch it come up with: fly logs
echo It prints a line for each of the voice and the commentary brain,
echo saying how many clubs are included - or that the key is not set.
