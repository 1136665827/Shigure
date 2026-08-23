param(
    [switch]$Force,
    [int]$DelayMilliseconds = 80
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$assetRoot = Join-Path $repoRoot "Assets"
$spellRoot = Join-Path $assetRoot "Spell"
$manifestPath = Join-Path $assetRoot "SpellIconManifest.json"
$classRoot = Join-Path $repoRoot "Fuyutsui\class"

New-Item -ItemType Directory -Path $spellRoot -Force | Out-Null

$idToName = @{}
foreach ($file in Get-ChildItem -LiteralPath $classRoot -Filter "*.lua") {
    $source = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    foreach ($match in [regex]::Matches($source, 'spellId\s*=\s*(?<id>\d+).*?name\s*=\s*["''](?<name>[^"'']+)["'']')) {
        $id = [long]$match.Groups['id'].Value
        $name = $match.Groups['name'].Value.Trim()
        if ($name.Length -gt 0 -and -not $idToName.ContainsKey($id)) {
            $idToName[$id] = $name
        }
    }
    # spellsList entries contain index; requiring it avoids treating array positions as spell IDs.
    foreach ($match in [regex]::Matches($source, '\[(?<id>\d+)\]\s*=\s*\{\s*index\s*=\s*\d+\s*,\s*name\s*=\s*["''](?<name>[^"'']+)["'']')) {
        $id = [long]$match.Groups['id'].Value
        $name = $match.Groups['name'].Value.Trim()
        if ($name.Length -gt 0 -and -not $idToName.ContainsKey($id)) {
            $idToName[$id] = $name
        }
    }
}

$manifest = [ordered]@{
    game = "World of Warcraft"
    updated = (Get-Date).ToString("yyyy-MM-dd")
    source = [ordered]@{
        tooltipBaseUrl = "https://nether.wowhead.com/tooltip/spell/"
        imageBaseUrl = "https://wow.zamimg.com/images/wow/icons/large/"
    }
    spells = @()
}

$webHeaders = @{ "User-Agent" = "Shigure Icon Catalog/1.0" }
$oldById = @{}
if (Test-Path -LiteralPath $manifestPath) {
    try {
        $old = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        foreach ($entry in @($old.spells)) {
            $oldById[[long]$entry.spellId] = $entry
        }
    } catch {
        $oldById = @{}
    }
}

$downloaded = 0
$skipped = 0
$failed = 0
foreach ($id in ($idToName.Keys | Sort-Object)) {
    $target = Join-Path $spellRoot "spell-$id.jpg"
    $icon = $null
    if ((Test-Path -LiteralPath $target) -and -not $Force -and $oldById.ContainsKey($id)) {
        $icon = [string]$oldById[$id].icon
        $skipped++
    }

    if ([string]::IsNullOrWhiteSpace($icon)) {
        try {
            $tooltip = Invoke-RestMethod -Uri "$($manifest.source.tooltipBaseUrl)$id`?dataEnv=1" -Headers $webHeaders -Method Get
            $icon = [string]$tooltip.icon
            if ([string]::IsNullOrWhiteSpace($icon)) {
                throw "tooltip did not contain an icon name"
            }

            $temporary = "$target.download"
            try {
                Invoke-WebRequest -Uri "$($manifest.source.imageBaseUrl)$icon.jpg" -OutFile $temporary -UseBasicParsing -Headers $webHeaders
                if ((Get-Item -LiteralPath $temporary).Length -lt 512) {
                    throw "downloaded file is unexpectedly small"
                }
                Move-Item -LiteralPath $temporary -Destination $target -Force
                $downloaded++
            } finally {
                if (Test-Path -LiteralPath $temporary) {
                    Remove-Item -LiteralPath $temporary -Force
                }
            }
        } catch {
            $failed++
            Write-Warning "spell $id ($($idToName[$id])): $($_.Exception.Message)"
            $icon = $null
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($icon) -and (Test-Path -LiteralPath $target)) {
        $manifest.spells += [ordered]@{
            spellId = $id
            name = $idToName[$id]
            icon = $icon
            target = "Spell/spell-$id.jpg"
        }
    }

    if ($DelayMilliseconds -gt 0) {
        Start-Sleep -Milliseconds $DelayMilliseconds
    }
}

$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Output "Downloaded: $downloaded; skipped existing: $skipped; failed: $failed; total: $($idToName.Count)"
