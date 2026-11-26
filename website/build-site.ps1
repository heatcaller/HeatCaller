# Builds the user guide into web pages and puts them in a folder, ready to
# copy into the heatcaller-site repo's docs/ folder (heatcaller.com/docs/).
#
#   .\build-site.ps1 -Out C:\Users\asher\heatcaller-site\docs
#
# WHY (25 Nov 2026, Asher: "put the guide online and link the app to it"):
# one step, so the guide can be put up again after every release. It stops
# on any broken link or missing page (--strict), and it strips the HTML
# comments - the <!-- TODO --> and <!-- screenshot: --> notes - which can't
# be seen on a page but can be read in its source.
#
# Needs Python with mkdocs-material 9.6:
#   python -m pip install --user "mkdocs-material==9.6.*"

param(
    [Parameter(Mandatory = $true)][string]$Out
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

$python = (Get-Command python -ErrorAction SilentlyContinue).Source
$installed = Join-Path $env:LOCALAPPDATA 'Programs\Python\Python313\python.exe'
if (-not $python -or $python -like '*WindowsApps*') { $python = $installed }
if (-not (Test-Path $python)) { throw "Python isn't installed. See the top of this script." }

$build = Join-Path ([System.IO.Path]::GetTempPath()) ("heatcaller-guide-" + [guid]::NewGuid().ToString('N'))

Push-Location $here
try {
    # MkDocs writes its progress to stderr, which Windows PowerShell 5.1
    # turns into errors under 'Stop' - so it is judged by its exit code.
    $ErrorActionPreference = 'Continue'
    & $python -m mkdocs build --strict --clean --site-dir $build 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($code -ne 0) { throw "The guide didn't build (exit $code)." }
}
finally {
    Pop-Location
}

# The notes for whoever writes the guide, out of every page's source.
$comment = [regex]::new('<!--(?!\[if).*?-->', [System.Text.RegularExpressions.RegexOptions]::Singleline)
$utf8 = [System.Text.UTF8Encoding]::new($false)

Get-ChildItem $build -Recurse -Filter *.html | ForEach-Object {
    $text = [System.IO.File]::ReadAllText($_.FullName)
    $clean = $comment.Replace($text, '')
    if ($clean -ne $text) { [System.IO.File]::WriteAllText($_.FullName, $clean, $utf8) }
}

# The search index carries each page's text too.
$index = Join-Path $build 'search\search_index.json'
if (Test-Path $index) {
    $text = [System.IO.File]::ReadAllText($index)
    $clean = [regex]::Replace($text, '\\u003c!--.*?--\\u003e|<!--.*?-->', '')
    [System.IO.File]::WriteAllText($index, $clean, $utf8)
}

$left = Get-ChildItem $build -Recurse -Include *.html, *.json | Select-String -Pattern 'TODO:|screenshot:' -SimpleMatch:$false -List
if ($left) { throw "Notes are still in the built pages: $($left.Path -join ', ')" }

if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force (Split-Path -Parent $Out) | Out-Null
Move-Item $build $Out

$pages = (Get-ChildItem $Out -Recurse -Filter index.html).Count
Write-Host "The guide is built: $pages pages in $Out"
