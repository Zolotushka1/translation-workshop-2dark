$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$application = Join-Path $repoRoot 'workshop/app/2DarkWorkshop.exe'
if (!(Test-Path -LiteralPath (Join-Path $repoRoot 'game.path.json'))) { throw 'Run scripts/Setup.ps1 first.' }
if (!(Test-Path -LiteralPath $application)) { throw 'Run scripts/Build.ps1 first.' }
Start-Process -FilePath $application -WorkingDirectory $repoRoot -WindowStyle Normal
