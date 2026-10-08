# Runs the app's tests with a progress bar. Called by check.cmd.
#
#   test-progress.ps1                      every test
#   test-progress.ps1 -Filter Commentary   just the tests whose names contain that
#
# Builds once, counts the tests, then runs them and moves the bar as each
# one finishes. Passing tests are counted rather than printed; failures and
# the summary are printed in full, same as plain "dotnet test".
param(
    [string]$Solution = "HeatCaller\HeatCaller.sln",
    [string]$Filter = ""
)

$ErrorActionPreference = "Continue"

# The plain console logger, so the lines can be read as they come.
$env:MSBUILDTERMINALLOGGER = "off"

$filterArgs = @()
if ($Filter) { $filterArgs = @("--filter", "FullyQualifiedName~$Filter") }

dotnet build $Solution --nologo -v quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Counting the tests..."
$listed = dotnet test $Solution --no-build --nologo --list-tests @filterArgs 2>$null
$total = @($listed | Where-Object { $_ -match '^\s{4}\S' }).Count
if ($Filter) {
    # --list-tests ignores --filter, so count only the names that match.
    $total = @($listed | Where-Object { $_ -match '^\s{4}\S' -and $_ -like "*$Filter*" }).Count
}

$done = 0
$failed = 0
$started = Get-Date

dotnet test $Solution --no-build --nologo --logger "console;verbosity=normal" @filterArgs 2>&1 | ForEach-Object {
    $line = "$_"

    if ($line -match '^\s+(Passed|Failed|Skipped)\s+\S') {
        $done++
        if ($Matches[1] -eq "Failed") { $failed++ }

        if ($total -gt 0) {
            $percent = [math]::Min(100, [int](100 * $done / $total))
            $elapsed = (Get-Date) - $started
            $status = "$done of $total"
            if ($failed -gt 0) { $status += "  -  $failed failed" }
            $status += "  -  {0:mm\:ss}" -f $elapsed
            Write-Progress -Activity "Running the tests" -Status $status -PercentComplete $percent
        }

        if ($Matches[1] -eq "Passed") { return }
    }

    Write-Host $line
}

$code = $LASTEXITCODE
Write-Progress -Activity "Running the tests" -Completed
exit $code
