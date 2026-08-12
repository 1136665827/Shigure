param(
    [switch]$Force
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$assetRoot = Join-Path $repoRoot "Assets"
$manifestPath = Join-Path $assetRoot "IconManifest.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$downloaded = 0
$skipped = 0

foreach ($icon in $manifest.icons) {
    $target = Join-Path $assetRoot $icon.target
    if ((Test-Path -LiteralPath $target) -and -not $Force) {
        $skipped++
        continue
    }

    $directory = Split-Path -Parent $target
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $uri = "$($manifest.source.imageBaseUrl)$($icon.slug).jpg"
    $temporary = "$target.download"

    try {
        Invoke-WebRequest -Uri $uri -OutFile $temporary -UseBasicParsing
        if ((Get-Item -LiteralPath $temporary).Length -lt 512) {
            throw "Downloaded file is unexpectedly small."
        }
        Move-Item -LiteralPath $temporary -Destination $target -Force
        $downloaded++
    }
    finally {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Force
        }
    }
}

Write-Output "Downloaded: $downloaded; skipped existing: $skipped; total: $($manifest.icons.Count)"
