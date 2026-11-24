# Récupère les sources des dépendances (versions figées) dans third_party/.
# Usage : powershell -ExecutionPolicy Bypass -File tools\setup-deps.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$tp = Join-Path $root "third_party"
New-Item -ItemType Directory -Force -Path $tp | Out-Null

$deps = @(
    @{ Name = "CodeWalker";    Url = "https://github.com/dexyfex/CodeWalker";   Commit = "485d56bec00262ed7fa472261cce7bbc6202b96e" },
    @{ Name = "SharpDX";       Url = "https://github.com/sharpdx/SharpDX";      Commit = "ab36f12303e24aa60fe804866617716b6ded95db" },
    @{ Name = "SharpGLTF";     Url = "https://github.com/vpenades/SharpGLTF";   Commit = "0c01536ff919209d56426897f5605023c2445dbf" },
    @{ Name = "StbImageSharp"; Url = "https://github.com/StbSharp/StbImageSharp"; Commit = "274a325dafc7dd1ddb715c36b32a23fd18b2d9ab" },
    @{ Name = "meshoptimizer"; Url = "https://github.com/zeux/meshoptimizer"; Commit = "8547fc321b782a2235dfc14c65a346a640544cd8" },
    @{ Name = "miniPaint";     Url = "https://github.com/viliusle/miniPaint"; Commit = "c9192c15918eb61f7dd89e362cf8065a276b89e9" }
)

foreach ($d in $deps) {
    $dir = Join-Path $tp $d.Name
    if (-not (Test-Path $dir)) {
        Write-Host "==> clone $($d.Name)"
        git init -q $dir
        git -C $dir remote add origin $d.Url
    } else {
        Write-Host "==> maj $($d.Name)"
    }
    git -C $dir fetch -q --depth 1 origin $d.Commit
    git -C $dir checkout -q --force FETCH_HEAD
}
Write-Host "Dépendances prêtes dans $tp"
