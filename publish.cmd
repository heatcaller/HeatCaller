@echo off
rem Cuts the self-contained Windows build the installer is made from: one
rem folder holding HeatCaller.exe, the .NET runtime inside it, and every
rem asset the app reads beside the exe. A club installs nothing else - no
rem .NET SDK, no .NET runtime.
rem
rem   publish.cmd                     to %USERPROFILE%\HeatCaller-publish
rem   publish.cmd D:\HeatCaller-out   to a folder of your choosing
rem
rem ONLY EVER PUBLISH FROM A GREEN BUILD. Run check.cmd first and wait for
rem ALL GREEN. This script cannot tell whether you did - it builds the app,
rem but it does not run the tests - and a release cut from a red build is a
rem release that was never tested.
rem
rem AND RUN fetch-kokoro.cmd ONCE on this machine first. The 310 MB Kokoro
rem model is not in git; it is fetched into HeatCaller\HeatCaller\Assets\Kokoro
rem and shipped from there. Without it this publish still works and the app
rem still works - it falls back to downloading the model from Voice Settings -
rem but the publish prints a warning, and an installer cut from it asks a club
rem on the track's Wi-Fi to fetch 310 MB before the natural voice can speak.
rem
rem The folder it writes is OUTSIDE this one on purpose: half a gigabyte of
rem build output has no business anywhere git can see it.
rem
rem It ends by printing the folder's size and file count. That number is the
rem installer's cost before compression, and it is worth reading every time:
rem what goes beside the exe is the whole argument about how big a club's
rem first download is.
rem
rem No trimming: Windows Forms does not support it. The trimmer cannot see
rem what reflection reaches for, so a trimmed build fails on the night
rem rather than at publish time.
setlocal
cd /d "%~dp0"

set "OUT=%~1"
if "%OUT%"=="" set "OUT=%USERPROFILE%\HeatCaller-publish"

rem An absolute path, so the folder is the same one whatever directory this
rem was called from.
for %%P in ("%OUT%") do set "OUT=%%~fP"

echo.
echo === HeatCaller: self-contained publish ===
echo   from: %~dp0
echo   to:   %OUT%
echo.
echo Reminder: only ever publish after check.cmd has said ALL GREEN.
echo.

rem A stale file from an older version left in the folder would be packed
rem into the installer as if it belonged there, so the folder is emptied
rem first. Only if it is already empty or is plainly a previous publish,
rem though: anything else and this stops rather than delete a folder it
rem does not recognize.
if not exist "%OUT%\" goto :publish
if exist "%OUT%\HeatCaller.exe" goto :clear
for /f %%C in ('dir /b /a "%OUT%" 2^>nul ^| find /c /v ""') do set "ENTRIES=%%C"
if "%ENTRIES%"=="0" goto :publish
goto :notours

:clear
echo Clearing the previous publish...
rmdir /s /q "%OUT%"
if exist "%OUT%\" goto :cannotclear

:publish
rem -r win-x64 --self-contained true: the runtime travels inside the folder.
rem No -p:UseAppHost=false here - that switch belongs to the cloud build
rem script, which only compiles. This publish has to produce HeatCaller.exe,
rem because that is what the installer installs and what a club clicks.
rem
rem ReadyToRun: recommended ON, once measured both ways. The app's startup
rem is already heavy (fonts, settings, every page built in code), the .NET
rem runtime in a self-contained publish is pre-compiled already so this only
rem adds to HeatCaller's own code and its libraries, and Velopack's delta
rem updates mean the extra size is paid once. Left commented out rather than
rem guessed at: publish both ways, read the size line below, and time a cold
rem start before committing to it. To try it, add to the line below:
rem   -p:PublishReadyToRun=true
dotnet publish "HeatCaller\HeatCaller\HeatCaller.csproj" -c Release -r win-x64 --self-contained true -o "%OUT%"
if errorlevel 1 goto :fail

if not exist "%OUT%\HeatCaller.exe" goto :noexe

echo.
echo === What is in it ===
powershell -NoProfile -ExecutionPolicy Bypass -Command "$root = '%OUT%'; $all = Get-ChildItem -LiteralPath $root -Recurse -File -Force; '{0:N0} files, {1:N1} MB in total' -f $all.Count, (($all | Measure-Object -Sum Length).Sum / 1MB); ''; 'The biggest folders:'; Get-ChildItem -LiteralPath $root -Directory | ForEach-Object { '{0,10:N1} MB  {1}' -f ((Get-ChildItem -LiteralPath $_.FullName -Recurse -File -Force | Measure-Object -Sum Length).Sum / 1MB), $_.Name } | Sort-Object -Descending"
if errorlevel 1 echo (could not measure the folder - look at its properties in Explorer instead)

echo.
echo PUBLISHED - %OUT%
echo.
echo Next: pack it. From this folder, with the version from the csproj:
echo.
echo   vpk pack -u HeatCallerApp -v ^<version^> -p "%OUT%" -e HeatCaller.exe ^^
echo            --packTitle "HeatCaller" --packAuthors "Asher Kent" -c beta
echo.
echo   -u HeatCallerApp, NEVER HeatCaller. Velopack installs into
echo   %%LocalAppData%%\^<packId^>, and %%LocalAppData%%\HeatCaller is already the
echo   voice-line cache (AppPaths.LocalRoot). The same name would mix the app
echo   into it and uninstalling would delete lines the club paid for.
echo.
echo   -v must match ^<Version^> in HeatCaller.csproj - that is the one number,
echo   and the updater compares against it.
echo.
echo   -c beta is the channel clubs install from now; stable comes later.
exit /b 0

:notours
echo.
echo *** FAILED - %OUT% already has files in it and no HeatCaller.exe, so it
echo *** is not a previous publish. Empty it yourself, or name another
echo *** folder: publish.cmd D:\somewhere-else ***
exit /b 1

:cannotclear
echo.
echo *** FAILED - could not clear %OUT%. Close anything still open from that
echo *** folder (Explorer, a running HeatCaller.exe) and try again. ***
exit /b 1

:noexe
echo.
echo *** FAILED - the publish finished but there is no HeatCaller.exe in
echo *** %OUT%. Check that -p:UseAppHost=false has not found its way into
echo *** the command above or into a Directory.Build.props. ***
exit /b 1

:fail
echo.
echo *** FAILED - nothing was published. Paste the errors above to Claude. ***
exit /b 1
