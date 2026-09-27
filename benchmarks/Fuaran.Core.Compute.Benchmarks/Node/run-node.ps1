#Requires -Version 7.0
<#
.SYNOPSIS
  The node leg of the compute benchmarks (Phase 262): compile the timing harness beside this
  script with Fable, run it under node, and print its median tables.

.DESCRIPTION
  The harness (Program.fs, over ../Corpus.fs) times the same corpus as the BenchmarkDotNet
  suites, through the dataframe package's own sources as Fable compiles them. This repository
  carries no Fable toolchain of its own, so the script borrows one: -FableFrom names a directory
  whose dotnet tool manifest provides `fable` (a consumer that already compiles these packages
  under Fable has one). Nothing here is run by the verify gate.

  The JavaScript goes to a scratch root outside the repository. Its layout mirrors the
  repository's, because Fable writes each source at its path relative to the project: ../Corpus.fs
  and the src/ projects land beside the output directory, not under it, and a mirrored root keeps
  them inside the scratch area.

    pwsh ./run-node.ps1 -FableFrom <dir with a fable tool manifest>
    pwsh ./run-node.ps1 -FableFrom <dir> -Runs 5 -KeepOutput

  Exit 0 = the harness compiled, every corpus agreement held, and the tables printed.
#>
[CmdletBinding()]
param(
    # A directory whose .config/dotnet-tools.json provides the `fable` command.
    [Parameter(Mandatory = $true)]
    [string] $FableFrom,
    # Measured runs per case, after two warm-up runs.
    [int] $Runs = 10,
    # Keep the emitted JavaScript for inspection.
    [switch] $KeepOutput
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$global:LASTEXITCODE = 0

$project = Join-Path $PSScriptRoot 'Fuaran.Core.Compute.Benchmarks.Node.fsproj'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$relative = [IO.Path]::GetRelativePath($repoRoot, $PSScriptRoot)

$node = Get-Command node -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $node) { throw 'node is not on PATH: the node leg cannot run without it' }
if (-not (Test-Path -LiteralPath $FableFrom -PathType Container)) { throw "-FableFrom '$FableFrom' is not a directory" }

# Keyed on this script's location, so two checkouts never wipe each other's output.
$key = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($PSScriptRoot))).Substring(0, 12).ToLowerInvariant()
$scratch = Join-Path ([IO.Path]::GetTempPath()) "fuaran-compute-bench-node-$key"
Remove-Item -Recurse -Force $scratch -ErrorAction SilentlyContinue
$outDir = Join-Path $scratch $relative
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

try {
    Push-Location $FableFrom
    try {
        # Assignment, never a pipe: a pipe reports the LAST command's status.
        $fableOutput = & dotnet fable $project -o $outDir --noCache 2>&1
        $fableExit = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    if ($fableExit -ne 0) {
        foreach ($line in @($fableOutput | ForEach-Object { [string] $_ })) { Write-Host "  $line" }
        throw "the Fable compile of the node harness failed (exit $fableExit)"
    }

    $entry = Join-Path $outDir 'Program.js'
    if (-not (Test-Path -LiteralPath $entry)) { throw "no emitted entry point at $entry" }

    Write-Host "node $(& $node.Source --version), $Runs measured runs per case"
    & $node.Source $entry $Runs
    if ($LASTEXITCODE -ne 0) { throw "the node run of the harness failed (exit $LASTEXITCODE)" }
}
finally {
    if ($KeepOutput) { Write-Host "  kept: $scratch" }
    else { Remove-Item -Recurse -Force $scratch -ErrorAction SilentlyContinue }
}
exit 0
