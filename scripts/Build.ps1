$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
& dotnet publish (Join-Path $repoRoot 'workshop/Workshop.App') -c Release --no-self-contained -o (Join-Path $repoRoot 'workshop/app')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
