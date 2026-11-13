#Requires -Version 7
<#
.SYNOPSIS
    Runs every CI step locally, in the same order, and stops at the first failure.
.DESCRIPTION
    Mirrors .github/workflows/ci.yml; change both together. Pass -Only to run one part.
.EXAMPLE
    ./verify.ps1
    ./verify.ps1 -Only ui
#>
param(
    [ValidateSet('host', 'ui', 'firmware')]
    [string[]] $Only = @('host', 'ui', 'firmware')
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

function Step([string] $dir, [string] $command) {
    Write-Host "`n>> [$dir] $command" -ForegroundColor Cyan
    Push-Location (Join-Path $root $dir)
    try {
        Invoke-Expression $command
        if ($LASTEXITCODE -ne 0) { throw "[$dir] '$command' failed with exit code $LASTEXITCODE" }
    }
    finally { Pop-Location }
}

$timer = [Diagnostics.Stopwatch]::StartNew()

if ('host' -in $Only) {
    Step '.' 'dotnet tool restore'
    Step '.' 'dotnet csharpier check .'
    Step '.' 'dotnet build host/WwvbDecoder.slnx -c Release -p:BuildUi=false'
    # Fresh results so ReportGenerator never merges a stale report from an earlier run.
    Remove-Item -Recurse -Force (Join-Path $root 'host/TestResults'), (Join-Path $root 'coverage/host') -ErrorAction Ignore
    Step '.' "dotnet test host/WwvbDecoder.slnx -c Release --no-build -p:BuildUi=false --coverage --coverage-output-format cobertura --report-xunit-trx --results-directory '$root/host/TestResults'"
    Step '.' 'dotnet reportgenerator "-reports:host/TestResults/*.cobertura.xml" "-targetdir:coverage/host" "-reporttypes:MarkdownSummaryGithub;Cobertura;TextSummary" "minimumCoverageThresholds:lineCoverage=90" "minimumCoverageThresholds:branchCoverage=88"'
}

if ('ui' -in $Only) {
    Step 'ui' 'npm ci'
    Step 'ui' 'npm run format:check'
    Step 'ui' 'npm run lint'
    Step 'ui' 'npm test'   # vitest with coverage; thresholds are in vite.config.ts
    Step 'ui' 'npm run build'
}

if ('firmware' -in $Only) {
    Step 'firmware' 'rustup toolchain install'
    Step 'firmware' 'cargo fmt --check'
    Step 'firmware' 'cargo build --release'
    Step 'firmware' 'cargo clippy --release -- -D warnings'
}

Write-Host "`nAll checks passed ($($Only -join ', ')) in $([int]$timer.Elapsed.TotalSeconds)s." -ForegroundColor Green
