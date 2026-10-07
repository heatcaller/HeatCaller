@echo off
rem Fetches the Kokoro voice model into the folder the publish ships it from:
rem
rem   HeatCaller\HeatCaller\Assets\Kokoro\kokoro.onnx   (about 310 MB)
rem
rem   fetch-kokoro.cmd         fetch it if it isn't already there
rem   fetch-kokoro.cmd force   fetch it again over the one that is
rem
rem Run this ONCE on the machine that cuts releases, before publish.cmd. The
rem file is not in git (see .gitignore) - 310 MB of someone else's published
rem binary belongs in a download, not in every clone - so a fresh clone needs
rem this one command before the installer it cuts has the natural voice in it.
rem Publishing without it is not an error: the app falls back to downloading
rem the model from Voice Settings exactly as it did before it was bundled, and
rem the publish prints a warning saying so.
rem
rem The same file, from the same place, that KokoroSpeech's own Download
rem button fetches - the KokoroSharp project's full-precision Kokoro v1.0.
rem If that URL ever moves, it is spelled out in KokoroSpeech.ModelUrl too,
rem and both have to move together.
setlocal
cd /d "%~dp0"

set "URL=https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro.onnx"
set "DIR=HeatCaller\HeatCaller\Assets\Kokoro"
set "MODEL=%DIR%\kokoro.onnx"

rem Anything smaller than this is a broken download, not the model - the same
rem floor KokoroSpeech.SmallestRealModel uses (50 MB).
set /a FLOOR=50*1024*1024

if /i "%~1"=="force" goto :fetch

if not exist "%MODEL%" goto :fetch

for %%F in ("%MODEL%") do set "HAVE=%%~zF"
if %HAVE% LSS %FLOOR% (
  echo The model that is there is only %HAVE% bytes - too small to be real. Fetching it again.
  goto :fetch
)

echo.
echo Already there: %MODEL%
echo   %HAVE% bytes. Nothing to do - fetch-kokoro.cmd force to get it again.
exit /b 0

:fetch
echo.
echo === Fetching the Kokoro voice model (about 310 MB) ===
echo   from: %URL%
echo   to:   %MODEL%
echo.
echo On a phone hotspot this is a long download. It is wanted once per machine.
echo.

if not exist "%DIR%\" mkdir "%DIR%"
if not exist "%DIR%\" goto :nodir

rem To a .part file and renamed only when it is whole, so a download cut off
rem part way never leaves a file that looks like the model - the publish would
rem pack it and the app would fail to load it on the night. Same reasoning as
rem KokoroSpeech.SaveModelAsync, which does this for the in-app download.
rem
rem curl.exe ships with Windows 10 1803 and later. -L follows the redirect
rem GitHub releases answer with; -f makes an HTTP error an exit code instead
rem of a saved error page.
curl.exe -L -f --progress-bar -o "%MODEL%.part" "%URL%"
if errorlevel 1 goto :failed

for %%F in ("%MODEL%.part") do set "GOT=%%~zF"
if %GOT% LSS %FLOOR% goto :short

move /y "%MODEL%.part" "%MODEL%" >nul
if errorlevel 1 goto :cannotmove

echo.
echo FETCHED - %MODEL%
echo   %GOT% bytes. publish.cmd will now ship it beside HeatCaller.exe.
exit /b 0

:short
echo.
echo *** FAILED - the download stopped after %GOT% bytes, which is not the
echo *** whole model. Nothing was kept. Try again.
del /q "%MODEL%.part" 2>nul
exit /b 1

:failed
echo.
echo *** FAILED - could not fetch the model. Check the internet connection
echo *** and that the URL above still exists, then try again. Nothing was
echo *** kept.
del /q "%MODEL%.part" 2>nul
exit /b 1

:cannotmove
echo.
echo *** FAILED - the download finished but could not be moved into place.
echo *** Close anything holding %MODEL% (a running HeatCaller) and run this
echo *** again. The download is at %MODEL%.part
exit /b 1

:nodir
echo.
echo *** FAILED - could not make the folder %DIR%
exit /b 1
