#Requires -Version 7
<#
.SYNOPSIS
    Builds the folder that goes to the presentation laptop: the app, the tools, and the captures.
.DESCRIPTION
    Publishes WwvbDecoder.Host and WwvbDecoder.Tools for win-x64, each as one self-contained
    exe, so the laptop needs no .NET SDK or runtime. The page is built by the Host's publish
    (npm run build) and lands in wwwroot beside the exe. The captures are copied beside it too,
    because the app lists the captures folder it finds at or above the exe.

    The folder is cleared first, all but its captures folder: a capture recorded there with the
    published tools is kept. A folder that is not empty and holds no earlier publish is left
    alone, and the script stops.
.EXAMPLE
    ./publish.ps1
    ./publish.ps1 -Output D:\demo\wwvb-decoder
#>
param(
    [string] $Output = (Join-Path $PSScriptRoot 'publish')
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$hostExe = 'WwvbDecoder.Host.exe'
$toolsExe = 'WwvbDecoder.Tools.exe'

function Step([string] $name, [scriptblock] $command) {
    Write-Host "`n>> $name" -ForegroundColor Cyan
    & $command
    if ($LASTEXITCODE -ne 0) { throw "'$name' failed with exit code $LASTEXITCODE" }
}

function Test-Running([string] $exe) {
    # Windows refuses write access to the file of a running program.
    if (-not (Test-Path $exe)) { return $false }
    try { [IO.File]::Open($exe, 'Open', 'ReadWrite', 'None').Dispose(); return $false }
    catch [IO.IOException] { return $true }
}

$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
if (Test-Path $Output) {
    $isEarlierPublish = Test-Path (Join-Path $Output $hostExe)
    $isEmpty = $null -eq (Get-ChildItem -Force $Output | Select-Object -First 1)
    if (-not ($isEarlierPublish -or $isEmpty)) {
        throw "$Output is not empty and holds no $hostExe. Not clearing it; name another folder."
    }
    # Checked before anything is deleted, so a running app never leaves the folder half cleared.
    $running = @($hostExe, "tools/$toolsExe" | Where-Object { Test-Running (Join-Path $Output $_) })
    if ($running) {
        throw "$($running -join ' and ') is running from $Output. Close it, then publish again."
    }
    Get-ChildItem -Force $Output | Where-Object Name -ne 'captures' | Remove-Item -Recurse -Force
}

# The publish restores from the committed packages.lock.json files, as a build does, and in
# locked mode: it fails when they do not fit and never rewrites them.
# - win-x64 is in those files because host/src/Directory.Build.props names it.
# - PublishSingleFile would switch on the single-file analyzer, which is one more package
#   (Microsoft.NET.ILLink.Tasks, a new version with every SDK patch) that a build does not
#   restore. It is switched off here so that both restore the same packages.
$publishArgs = @(
    '-c', 'Release'
    '-r', 'win-x64'
    '--self-contained'
    '-p:PublishSingleFile=true'
    '-p:IncludeNativeLibrariesForSelfExtract=true'
    '-p:EnableSingleFileAnalyzer=false'
    '-p:PublishDocumentationFiles=false'
    '-p:RestoreLockedMode=true'
)

$timer = [Diagnostics.Stopwatch]::StartNew()
Push-Location $root
try {
    # Release builds the page first: npm ci when the lock file changed, then npm run build.
    Step 'publish the app' {
        dotnet publish host/src/WwvbDecoder.Host @publishArgs -o $Output
    }
    Step 'publish the tools' {
        dotnet publish host/src/WwvbDecoder.Tools @publishArgs -o (Join-Path $Output 'tools')
    }

    # The repo's captures go in beside whatever was recorded in the folder itself.
    $captures = Join-Path $Output 'captures'
    New-Item -ItemType Directory -Force $captures | Out-Null
    Copy-Item (Join-Path $root 'captures/*.edges.txt') $captures

    $commit = git describe --always --dirty
    $built = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
    Set-Content (Join-Path $Output 'version.txt') "wwvb-decoder $commit, published $built"
}
finally { Pop-Location }

# What the laptop cannot do without.
$needed = @(
    $hostExe
    'wwwroot/index.html'
    "tools/$toolsExe"
    'version.txt'
)
$missing = @($needed | Where-Object { -not (Test-Path (Join-Path $Output $_)) })
if ($missing) { throw "The publish folder is missing: $($missing -join ', ')" }
$captureCount = @(Get-ChildItem (Join-Path $Output 'captures') -Filter '*.edges.txt').Count
if ($captureCount -eq 0) { throw 'No capture was copied, so the laptop would have nothing to replay.' }

$megabytes = [int]((Get-ChildItem -Recurse -File $Output | Measure-Object Length -Sum).Sum / 1MB)
Write-Host "`nPublished to $Output in $([int]$timer.Elapsed.TotalSeconds)s: $megabytes MB, $captureCount captures." -ForegroundColor Green
Write-Host (Get-Content (Join-Path $Output 'version.txt'))
Write-Host 'Copy the whole folder to the laptop. The exe does not run without wwwroot beside it.'
