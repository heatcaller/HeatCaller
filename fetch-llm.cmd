@echo off
rem Fetches the commentary model into the folder the publish ships it from:
rem
rem   HeatCaller\HeatCaller\Assets\LocalModel\commentary.gguf   (about 440 MB)
rem
rem   fetch-llm.cmd         fetch it if it isn't already there
rem   fetch-llm.cmd force   fetch it again over the one that is
rem
rem Run this ONCE on the machine that cuts releases, before publish.cmd -
rem exactly like fetch-kokoro.cmd, and for the same reasons. The file is not
rem in git (see .gitignore): 440 MB of someone else's published weights
rem belongs in a download, not in every clone.
rem
rem WHY THIS IS BUNDLED RATHER THAN DOWNLOADED BY THE APP
rem
rem This model is the brain that writes commentary when the club has NO
rem INTERNET. The relay writes everything when there is internet, and writes
rem it better. So the one night this model exists for is the night it could
rem not possibly be downloaded - which is why it travels inside the
rem installer. Publishing without it is not an error, but it does mean a
rem club with no internet gets no colour at all, and the publish says so.
rem
rem WHICH MODEL, AND WHY THIS SMALL
rem
rem Qwen3 0.6B, quantised to Q5_K_M - about 440 MB on disk and well under a
rem gigabyte of memory while it runs. Three things picked it:
rem
rem   Licence. Apache 2.0, which permits shipping the weights inside a
rem   commercial installer provided the licence text travels with them. It
rem   does - Assets\Licences. Most of the better-known small models are
rem   under custom community licences that would have to be pushed down into
rem   HeatCaller's own terms, which is not a thing to do to a club.
rem
rem   Size. The installer is the constraint, and not vaguely: releases are
rem   served from the HeatCaller-releases GitHub repo (AppUpdates.FeedUrl)
rem   and GitHub refuses a release asset over 2 GiB. Setup.exe was 241 MB
rem   before this; a quantised model barely compresses, so this lands it
rem   near 690 MB and leaves well over a gigabyte of headroom. A 4B model
rem   would have put every release over the cap and off GitHub entirely.
rem
rem   Hardware nobody has seen. A club PC might be anything. 0.6B runs on
rem   a machine with 4 GB of RAM and no modern instruction sets; a larger
rem   one would miss the live budget on half the PCs this ships to. The app
rem   times itself on first run and switches off if it still can't keep up -
rem   see BundledLlmClient.WarmUpAsync.
rem
rem To ship a different model, change URL below and nothing else: the app
rem loads whatever commentary.gguf holds, and names the file it loaded in
rem the console. A model that is not Qwen-shaped will want
rem BundledLlmClient.BuildPrompt changed too - that method writes ChatML
rem with Qwen3's thinking switched off.
setlocal
cd /d "%~dp0"

set "URL=https://huggingface.co/unsloth/Qwen3-0.6B-GGUF/resolve/main/Qwen3-0.6B-Q5_K_M.gguf"
set "DIR=HeatCaller\HeatCaller\Assets\LocalModel"
set "MODEL=%DIR%\commentary.gguf"

rem Anything smaller than this is a broken download, not the model - the
rem same floor BundledLlmClient.SmallestRealModel uses (100 MB).
set /a FLOOR=100*1024*1024

if /i "%~1"=="force" goto :fetch

if not exist "%MODEL%" goto :fetch

for %%F in ("%MODEL%") do set "HAVE=%%~zF"
if %HAVE% LSS %FLOOR% (
  echo The model that is there is only %HAVE% bytes - too small to be real. Fetching it again.
  goto :fetch
)

echo.
echo Already there: %MODEL%
echo   %HAVE% bytes. Nothing to do - fetch-llm.cmd force to get it again.
exit /b 0

:fetch
echo.
echo === Fetching the commentary model (about 440 MB) ===
echo   from: %URL%
echo   to:   %MODEL%
echo.
echo This is wanted once per machine, not once per release.
echo.

if not exist "%DIR%\" mkdir "%DIR%"
if not exist "%DIR%\" goto :nodir

rem To a .part file and renamed only when it is whole, so a download cut off
rem part way never leaves a file that looks like the model - the publish
rem would pack it and the app would fail to load it on the night. Same
rem reasoning as fetch-kokoro.cmd.
rem
rem curl.exe ships with Windows 10 1803 and later. -L follows the redirect
rem Hugging Face answers with; -f makes an HTTP error an exit code instead
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
