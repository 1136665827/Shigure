param(
    [switch]$Force,
    [switch]$PruneLegacyIdFiles,
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
    spells = $null
}
$manifestSpells = New-Object 'System.Collections.Generic.List[object]'

$webHeaders = @{ "User-Agent" = "Shigure Icon Catalog/1.0" }
$oldById = @{}
if (Test-Path -LiteralPath $manifestPath) {
    try {
        # Windows PowerShell 5 treats UTF-8 files without a BOM as the active ANSI
        # code page. Read explicitly as UTF-8 so Chinese spell names remain valid JSON.
        $manifestJson = [System.IO.File]::ReadAllText($manifestPath, [System.Text.Encoding]::UTF8)
        $old = $manifestJson | ConvertFrom-Json -ErrorAction Stop
        foreach ($entry in @($old.spells)) {
            $entryId = [long]$entry.spellId
            $oldById[$entryId] = $entry
            # Keep the rebuilt DB2 class catalog when this incremental downloader is rerun.
            if (-not $idToName.ContainsKey($entryId) -and -not [string]::IsNullOrWhiteSpace([string]$entry.name)) {
                $idToName[$entryId] = [string]$entry.name
            }
        }
        if ($null -ne $old.source) {
            foreach ($propertyName in @('classificationBuild', 'locale', 'db2', 'listfile', 'localIcons')) {
                $property = $old.source.PSObject.Properties[$propertyName]
                if ($null -ne $property -and $null -ne $property.Value) {
                    $manifest.source[$propertyName] = $property.Value
                }
            }
        }
    } catch {
        throw "Failed to parse existing spell icon manifest '$manifestPath': $($_.Exception.Message)"
    }
}

$downloaded = 0
$skipped = 0
$reused = 0
$failed = 0
foreach ($id in ($idToName.Keys | Sort-Object)) {
    $icon = $null
    $oldTarget = $null
    $targetName = $null
    $usedNetwork = $false
    if (-not $Force -and $oldById.ContainsKey($id)) {
        $icon = [string]$oldById[$id].icon
        $oldTargetValue = [string]$oldById[$id].target
        if (-not [string]::IsNullOrWhiteSpace($oldTargetValue)) {
            $oldTargetCandidate = [System.IO.Path]::GetFullPath(
                (Join-Path $assetRoot ($oldTargetValue -replace '/', '\'))
            )
            $oldTargetParent = Split-Path -Parent $oldTargetCandidate
            if (
                $oldTargetParent.Equals([System.IO.Path]::GetFullPath($spellRoot), [System.StringComparison]::OrdinalIgnoreCase) -and
                [System.IO.Path]::GetExtension($oldTargetCandidate).Equals('.jpg', [System.StringComparison]::OrdinalIgnoreCase) -and
                [System.IO.Path]::GetFileName($oldTargetCandidate).StartsWith('icon-', [System.StringComparison]::OrdinalIgnoreCase)
            ) {
                $oldTarget = $oldTargetCandidate
                $targetName = [System.IO.Path]::GetFileName($oldTargetCandidate)
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($icon) -and -not [string]::IsNullOrWhiteSpace($targetName)) {
        $icon = $targetName.Substring('icon-'.Length, $targetName.Length - 'icon-'.Length - '.jpg'.Length)
    }

    if ([string]::IsNullOrWhiteSpace($icon)) {
        try {
            $usedNetwork = $true
            $tooltip = Invoke-RestMethod -Uri "$($manifest.source.tooltipBaseUrl)$id`?dataEnv=1" -Headers $webHeaders -Method Get
            $icon = [string]$tooltip.icon
            if ([string]::IsNullOrWhiteSpace($icon)) {
                throw "tooltip did not contain an icon name"
            }

        } catch {
            $failed++
            Write-Warning "spell $id ($($idToName[$id])): $($_.Exception.Message)"
            $icon = $null
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($icon)) {
        if ([string]::IsNullOrWhiteSpace($targetName)) {
            if ($icon -notmatch '^[A-Za-z0-9_-]+$') {
                $failed++
                Write-Warning "spell $id ($($idToName[$id])): invalid downloaded icon name '$icon'"
                continue
            }
            $targetName = "icon-$($icon.ToLowerInvariant()).jpg"
        }

        $target = Join-Path $spellRoot $targetName
        if ((Test-Path -LiteralPath $target) -and -not $Force) {
            $skipped++
        } elseif ($null -ne $oldTarget -and (Test-Path -LiteralPath $oldTarget) -and -not $Force) {
            Copy-Item -LiteralPath $oldTarget -Destination $target -Force
            $reused++
        } else {
            $temporary = "$target.download"
            try {
                $usedNetwork = $true
                Invoke-WebRequest -Uri "$($manifest.source.imageBaseUrl)$icon.jpg" -OutFile $temporary -UseBasicParsing -Headers $webHeaders
                if ((Get-Item -LiteralPath $temporary).Length -lt 512) {
                    throw "downloaded file is unexpectedly small"
                }
                Move-Item -LiteralPath $temporary -Destination $target -Force
                $downloaded++
            } catch {
                $failed++
                Write-Warning "spell $id ($($idToName[$id])): $($_.Exception.Message)"
                if (Test-Path -LiteralPath $temporary) {
                    Remove-Item -LiteralPath $temporary -Force
                }
                continue
            } finally {
                if (Test-Path -LiteralPath $temporary) {
                    Remove-Item -LiteralPath $temporary -Force
                }
            }
        }

        $manifestEntry = [ordered]@{
            spellId = $id
            name = $idToName[$id]
            target = "Spell/$targetName"
        }
        if ($oldById.ContainsKey($id)) {
            foreach ($propertyName in @('classes', 'specializations', 'skillLines', 'traitSubtrees', 'classificationSources', 'configuredAura')) {
                $property = $oldById[$id].PSObject.Properties[$propertyName]
                if ($null -ne $property -and $null -ne $property.Value) {
                    $manifestEntry[$propertyName] = $property.Value
                }
            }
        }
        $manifestSpells.Add($manifestEntry)
    }

    if ($usedNetwork -and $DelayMilliseconds -gt 0) {
        Start-Sleep -Milliseconds $DelayMilliseconds
    }
}

$manifest.spells = $manifestSpells

$manifestJson = ($manifest | ConvertTo-Json -Depth 5 -Compress) + [Environment]::NewLine
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($manifestPath, $manifestJson, $utf8WithoutBom)

if ($PruneLegacyIdFiles -and $failed -eq 0 -and $manifest.spells.Count -eq $idToName.Count) {
    $resolvedSpellRoot = (Resolve-Path -LiteralPath $spellRoot).Path
    if ($resolvedSpellRoot -ne (Join-Path $assetRoot "Spell")) {
        throw "Refusing to prune unexpected path: $resolvedSpellRoot"
    }

    Get-ChildItem -LiteralPath $resolvedSpellRoot -File |
        Where-Object { $_.Name -match '^spell-\d+\.jpg$' } |
        Remove-Item -Force
}

$uniqueIconCount = @($manifest.spells.icon | Sort-Object -Unique).Count
Write-Output "Downloaded: $downloaded; reused: $reused; skipped existing: $skipped; failed: $failed; spell IDs: $($idToName.Count); unique icons: $uniqueIconCount"
