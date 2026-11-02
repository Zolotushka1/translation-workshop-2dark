[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GamePath,
    [Parameter(Mandatory=$true)][string]$QuickBms
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$gameRoot = (Resolve-Path -LiteralPath $GamePath).Path
$toolPath = (Resolve-Path -LiteralPath $QuickBms).Path
$expected = Get-Content -LiteralPath (Join-Path $repoRoot 'data/supported-build.json') -Raw | ConvertFrom-Json
# Validate before writing generated data; never baseline an unknown EXE.
foreach ($entry in $expected) {
    $source = Join-Path $gameRoot $entry.path
    if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing game file: $source" }
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw "Unsupported or modified game build: $($entry.path)"
    }
}
$toolDirectory = Join-Path $repoRoot 'tools/quickbms'
New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null
$localTool = Join-Path $toolDirectory 'quickbms.exe'
if ([IO.Path]::GetFullPath($toolPath) -ne [IO.Path]::GetFullPath($localTool)) { Copy-Item -LiteralPath $toolPath -Destination $localTool }
foreach ($entry in $expected | Where-Object { $_.path.EndsWith('.bigfile') }) {
    $label = [IO.Path]::GetFileNameWithoutExtension($entry.path)
    $destination = Join-Path $repoRoot "original/$label"
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    & $localTool -R -q -o -f '{}texts{}.txt;fonts/{}.txt;{}script{}.txt' (Join-Path $repoRoot 'sources/2dark_extended.bms') (Join-Path $gameRoot $entry.path) $destination
    if ($LASTEXITCODE -ne 0) { throw "QuickBMS failed for $label (exit $LASTEXITCODE)" }
}
$inventory = Get-Content -LiteralPath (Join-Path $repoRoot 'data/corpus_inventory.json') -Raw | ConvertFrom-Json
foreach ($item in $inventory) {
    if (!(Test-Path -LiteralPath (Join-Path $repoRoot "original/$($item.file)"))) { throw "Missing extracted table: $($item.file)" }
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'data/corpus_inventory.json') -Destination (Join-Path $repoRoot 'corpus_inventory.json')
Copy-Item -LiteralPath (Join-Path $repoRoot 'data/supported-build.json') -Destination (Join-Path $repoRoot 'original_manifest.json')
$localConfig = @{ GamePath = $gameRoot } | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $repoRoot 'game.path.json'), $localConfig, [Text.UTF8Encoding]::new($false))
Write-Host 'Setup complete. Original game files and personal projects were not changed.'
