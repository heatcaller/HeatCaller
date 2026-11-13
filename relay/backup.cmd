@echo off
setlocal
REM ------------------------------------------------------------------
REM  backup.cmd - copy the relay's whole data volume down to this PC.
REM
REM    backup.cmd            (the app named in fly.toml)
REM    backup.cmd <app-name> (another app)
REM
REM  Run it from the relay folder. It makes a dated folder under
REM  relay-backups\ holding relay-data.tgz: every club's published
REM  results, queues, nights, videos and the voice usage files - the
REM  things a club is billed by included.
REM
REM  It wakes the machine first (it stops when idle, and a stopped
REM  machine can't be reached over ssh), packs /data into one file on
REM  the machine, downloads that file, and deletes it from the machine.
REM ------------------------------------------------------------------

set APP=%~1
set APPFLAG=
if not "%APP%"=="" set APPFLAG=-a %APP%
if "%APP%"=="" set APP=heatcaller-relay

for /f %%d in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd_HHmm"') do set STAMP=%%d
set OUT=relay-backups\%STAMP%

echo Waking %APP%...
curl -s -o nul --max-time 60 https://%APP%.fly.dev/healthz
REM Give the machine a moment after its first answer.
powershell -NoProfile -Command "Start-Sleep -Seconds 5"

echo Packing /data on the machine...
fly ssh console %APPFLAG% -C "tar czf /tmp/relay-data.tgz -C /data ."
REM The relay is still writing while this packs: a file tar listed can be
REM renamed away before tar reads it, and tar then reports a failure although
REM nothing is wrong. A second go straight away settles that.
if errorlevel 1 (
  echo The pack did not finish cleanly. Trying once more...
  fly ssh console %APPFLAG% -C "tar czf /tmp/relay-data.tgz -C /data ."
)
if errorlevel 1 (
  REM Not left behind: a half-made copy of the whole volume on the machine.
  fly ssh console %APPFLAG% -C "rm -f /tmp/relay-data.tgz"
  echo Could not pack the data, so nothing was downloaded. If the machine is stopped, try: fly status %APPFLAG%
  exit /b 1
)

if not exist "%OUT%" mkdir "%OUT%"

echo Downloading...
fly ssh sftp get %APPFLAG% /tmp/relay-data.tgz "%OUT%\relay-data.tgz"
if errorlevel 1 (
  echo The download failed. The packed file is still at /tmp/relay-data.tgz on the machine.
  exit /b 1
)

fly ssh console %APPFLAG% -C "rm -f /tmp/relay-data.tgz"

echo.
echo Saved: %CD%\%OUT%\relay-data.tgz
for %%f in ("%OUT%\relay-data.tgz") do echo Size: %%~zf bytes
echo Keep a copy somewhere other than this PC.
endlocal
