# Pack the three packages this repository produces into a local folder feed:
# Fuaran.Core.DataFrame, Fuaran.Core.Column.Ops and Fuaran.Core.DataFrame.Conformance, at the
# standing <Version> in Directory.Build.props.
#
#   pwsh ./pack.ps1                 pack into ../../local-nuget-feed (the development loop's feed)
#   pwsh ./pack.ps1 -Feed <path>    pack elsewhere
#
# Releases do not come from here: a `v*` tag runs .github/workflows/publish-packages.yml.
#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $Feed = (Join-Path $PSScriptRoot '../../local-nuget-feed'),
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$global:LASTEXITCODE = 0

if (-not (Test-Path $Feed)) { New-Item -ItemType Directory -Force $Feed | Out-Null }
$Feed = (Resolve-Path $Feed).Path

$projects = @(
    'src/Fuaran.Core.DataFrame/Fuaran.Core.DataFrame.fsproj'
    'src/Fuaran.Core.Column.Ops/Fuaran.Core.Column.Ops.fsproj'
    'src/Fuaran.Core.DataFrame.Conformance/Fuaran.Core.DataFrame.Conformance.fsproj'
)

foreach ($project in $projects) {
    dotnet pack $project -c $Configuration -o $Feed --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "packed $($projects.Count) packages to $Feed"
exit 0
