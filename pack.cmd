@echo off
rem Packs the published folder into a Velopack release AND signs it, so what
rem a club downloads is signed by "Asher Kent" instead of warning them off.
rem
rem   pack.cmd              sign with the real Public Trust certificate
rem   pack.cmd --test       sign with the TEST certificate (see below)
rem   pack.cmd --unsigned   no signing at all, the old behaviour
rem
rem RUN publish.cmd FIRST. This packs what is already in the publish folder;
rem it does not build anything. Publish, then pack.
rem
rem --test signs against heatcallertestprofile, whose certificates chain to a
rem TEST root no normal Windows machine trusts. The signature will not
rem validate on the track PC and SmartScreen will still object - that is the
rem point. It proves the whole signing path works (Azure login, the dlib,
rem signtool, Velopack) without spending a real signing operation or putting
rem a half-tested release in front of a club. Use it the first time, and
rem after any change to the signing setup.
rem
rem WHAT HAS TO BE TRUE BEFORE THIS WORKS
rem
rem   1. vpk is installed: dotnet tool install -g vpk. It is the Velopack
rem      CLI and it is not part of the .NET SDK.
rem
rem   2. az login has been run on this machine, as asherkent@yahoo.com.
rem      The signing client gets its token from the Azure CLI's cached
rem      login. No secret is stored in this folder - that is deliberate,
rem      and it is why signing-metadata.json is safe to commit.
rem
rem   3. That account holds the "Artifact Signing Certificate Profile
rem      Signer" role on the heatcallersigning account in Azure. Owner is
rem      NOT enough; signing is a data-plane operation with its own role,
rem      and without it every sign fails 403 Forbidden.
rem
rem   4. The .NET 8 runtime is installed. Velopack's bundled signtool and
rem      dlib are built against it, and the .NET 10 SDK this app builds with
rem      does not supply it.
rem
rem The version is read from HeatCaller.csproj rather than typed, because the
rem updater compares against that number and a release packed under a version
rem that does not match it is a release that will never be offered to anyone.
rem
rem -i gives Setup.exe and the updater the app's own icon (the Sunset Grid,
rem 24 Oct 2026), the same file the exe is built with.
rem
rem --msi also builds HeatCallerApp-beta.msi beside Setup.exe, and THE .MSI
rem IS WHAT A CLUB GETS. It opens on a license page with the beta license
rem agreement and the privacy notice (--instLicense, the same file the app
rem embeds and asks for at first launch - see Terms.cs), and Next stays
rem greyed out until "I accept" is ticked, so it cannot be installed without
rem accepting them. Velopack's Setup.exe has no license page at all - checked
rem in the vpk 1.2.161 source, 28 Oct 2026 - so a club given Setup.exe is
rem asked at first launch instead, and can't use HeatCaller until it accepts.
rem --instLocation PerUser installs it where Setup.exe does, for this Windows
rem user under %LocalAppData%, with no administrator prompt; updates reach an
rem .msi install exactly as they reach a Setup.exe one.
rem
rem -r win-x64 matches publish.cmd. Without it vpk defaults to x86 and
rem marks an x64-only self-contained build as an x86 release - it warns, but
rem only in passing, and the wrong architecture is baked into the installer.
rem
rem Releases land OUTSIDE this folder for the same reason the publish does.
setlocal
cd /d "%~dp0"

set "PUBDIR=%USERPROFILE%\HeatCaller-publish"
set "RELDIR=%USERPROFILE%\HeatCaller-releases"
set "SIGNFILE=signing-metadata.json"
set "SIGNLABEL=Public Trust - real, clubs will trust this"
set "TERMS=%CD%\HeatCaller\HeatCaller\Assets\Legal\HeatCaller-Terms.md"

:args
if "%~1"=="" goto :ready
if /i "%~1"=="--test" (
  set "SIGNFILE=signing-metadata-test.json"
  set "SIGNLABEL=Public Trust TEST - will NOT validate on a normal machine"
  shift
  goto :args
)
if /i "%~1"=="--unsigned" (
  set "SIGNFILE="
  set "SIGNLABEL=NOT SIGNED"
  shift
  goto :args
)
if /i "%~1"=="--pub" (
  set "PUBDIR=%~2"
  shift
  shift
  goto :args
)
if /i "%~1"=="--out" (
  set "RELDIR=%~2"
  shift
  shift
  goto :args
)
echo.
echo *** FAILED - unknown option: %~1
echo *** Use: pack.cmd [--test ^| --unsigned] [--pub ^<folder^>] [--out ^<folder^>]
exit /b 1

:ready
rem The one number. Read it, never type it.
rem
rem No pipe inside the PowerShell below, on purpose: in a double-quoted batch
rem string a caret is not an escape character, so "^|" would reach PowerShell
rem as two literal characters, the command would fail to parse, and the
rem version would come back empty with nothing said about it.
set "VER="
for /f "usebackq delims=" %%V in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$p=[xml](Get-Content -Raw 'HeatCaller\HeatCaller\HeatCaller.csproj'); foreach ($g in $p.Project.PropertyGroup) { if ($g.Version) { Write-Output ([string]$g.Version).Trim(); break } }"`) do set "VER=%%V"
if not defined VER goto :noversion

rem And it has to look like one. An empty or garbled read would pack a
rem release under a version the updater could never match.
echo %VER%| findstr /r "^[0-9][0-9]*\.[0-9][0-9]*" >nul
if errorlevel 1 goto :noversion

rem vpk is the Velopack CLI, a .NET global tool, and not part of the SDK.
where vpk >nul 2>&1
if errorlevel 1 goto :novpk

if not exist "%PUBDIR%\HeatCaller.exe" goto :nopublish
if defined SIGNFILE if not exist "%SIGNFILE%" goto :noterms
echo.
echo *** FAILED - the license and privacy notice is not where it should be:
echo ***   %TERMS%
echo *** The installer shows it, and the app will not start without it. It
echo *** belongs in git - if it has gone missing, git checkout it.
exit /b 1

:nosignfile
if not exist "%TERMS%" goto :noterms

echo.
echo === HeatCaller: pack %VER% ===
echo   from:      %PUBDIR%
echo   to:        %RELDIR%
echo   channel:   beta
echo   signing:   %SIGNLABEL%
echo.

if defined SIGNFILE (
  vpk pack -u HeatCallerApp -v %VER% -r win-x64 -p "%PUBDIR%" -e HeatCaller.exe --packTitle "HeatCaller" --packAuthors "Asher Kent" -i "%CD%\HeatCaller\HeatCaller\Assets\HeatCaller.ico" -c beta -o "%RELDIR%" --msi --instLocation PerUser --instLicense "%TERMS%" --azureTrustedSignFile "%CD%\%SIGNFILE%"
) else (
  vpk pack -u HeatCallerApp -v %VER% -r win-x64 -p "%PUBDIR%" -e HeatCaller.exe --packTitle "HeatCaller" --packAuthors "Asher Kent" -i "%CD%\HeatCaller\HeatCaller\Assets\HeatCaller.ico" -c beta -o "%RELDIR%" --msi --instLocation PerUser --instLicense "%TERMS%"
)
if errorlevel 1 goto :fail

echo.
echo PACKED %VER% - %RELDIR%
echo.
echo Send clubs the .msi (HeatCallerApp-beta.msi), not Setup.exe: the .msi
echo makes them accept the license and privacy notice before it installs.
echo.
if not defined SIGNFILE goto :done
echo Check the signature took. Right-click the Setup exe in %RELDIR%,
echo Properties, Digital Signatures - it should name Asher Kent. Or:
echo.
echo   powershell -NoProfile -Command "Get-AuthenticodeSignature '%RELDIR%\HeatCallerApp-beta-Setup.exe' ^| Format-List Status, SignerCertificate"
echo.
echo Status Valid is what you want. NotTrusted on a --test pack is correct
echo and expected - that is the test root doing its job.
:done
exit /b 0

:novpk
echo.
echo *** FAILED - vpk is not installed. It is the Velopack CLI, a .NET
echo *** global tool, and it does not come with the SDK:
echo ***
echo ***   dotnet tool install -g vpk
echo ***
echo *** Then open a new terminal so it is on PATH, and run this again.
exit /b 1

:nopublish
echo.
echo *** FAILED - no HeatCaller.exe in %PUBDIR%
echo *** Run publish.cmd first, or point this at the right folder:
echo ***   pack.cmd --pub D:\somewhere-else
exit /b 1

:noversion
echo.
echo *** FAILED - could not read a sensible ^<Version^> from
echo *** HeatCaller\HeatCaller\HeatCaller.csproj. It read: "%VER%"
echo *** Pack by hand this once and say so to Claude - the csproj has
echo *** probably moved or changed shape.
exit /b 1

:nosignfile
echo.
echo *** FAILED - %SIGNFILE% is not in this folder. It names the Azure
echo *** signing account and certificate profile; without it nothing can be
echo *** signed. It belongs in git - if it has gone missing, git checkout it.
exit /b 1

:fail
echo.
echo *** FAILED - nothing was packed.
echo ***
echo *** 403 Forbidden            the signing account is missing the
echo ***                          "Artifact Signing Certificate Profile
echo ***                          Signer" role. Owner does not cover it.
echo *** AuthenticationFailed     run az login again, this login expires.
echo *** no certificates found    the certificate profile named in
echo ***                          %SIGNFILE% does not exist in Azure.
echo *** a .NET 8 runtime error   install it: winget install Microsoft.DotNet.Runtime.8
echo ***
echo *** Anything else, paste the errors above to Claude.
exit /b 1
