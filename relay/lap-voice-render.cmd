@echo off
rem Makes the lap voice's clips - every lap time from 3.00 to 19.99 and the
rem two halves of one with a thousandth, the lap numbers, places, gaps and
rem target words - in ElevenLabs' Daniel voice, on
rem YOUR ElevenLabs key, into the lap-voice folder here. See src\lapvoice.ts.
rem
rem   lap-voice-render.cmd count    what it would make and cost; spends nothing
rem   lap-voice-render.cmd          makes them, after you type yes
rem
rem The key is read from voice-test.key in this folder (the file the local
rem voice test uses), or from RELAY_VOICE_KEY. It goes to ElevenLabs and
rem nowhere else.
rem
rem Safe to run again: clips already made are never asked for twice, so a run
rem that stopped carries on and costs only what is left. Then run
rem fly deploy --ha=false from this folder and every club hears them.

setlocal
cd /d "%~dp0"

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

call npm run build >nul
if errorlevel 1 (
  echo The build failed, so nothing was made.
  exit /b 1
)

node dist\src\lapvoicerender.js count
if errorlevel 3 exit /b 0
if errorlevel 1 exit /b 1
if /i "%~1"=="count" exit /b 0

echo.
set "ANSWER="
set /p "ANSWER=Make the missing clips on your ElevenLabs key? Type yes to go ahead: "
if /i not "%ANSWER%"=="yes" (
  echo Nothing was made.
  exit /b 0
)

node dist\src\lapvoicerender.js
exit /b %errorlevel%
