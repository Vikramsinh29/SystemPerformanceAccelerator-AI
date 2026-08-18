$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repo

$sourcePath = Join-Path $PSScriptRoot 'Apply-FinalToolVerticalCompactness.ps1'
$localPath = Join-Path $PSScriptRoot 'Apply-FinalToolVerticalCompactness.LOCAL.ps1'

if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw 'Apply-FinalToolVerticalCompactness.ps1 was not found.'
}

$utf8 = New-Object System.Text.UTF8Encoding($false, $true)
$script = [System.IO.File]::ReadAllText($sourcePath, $utf8).Replace("`r`n", "`n")

$oldBlock = @'
$gapSections = @(
    @{ Name='Health Check status gap'; Start='DataContext="{Binding HealthCheck}"'; End='Visibility="{Binding IsCleanerContentVisible' },
    @{ Name='Custom Clean status gap'; Start='DataContext="{Binding CustomClean}"'; End='DataContext="{Binding AutoCleanSchedule}"' },
    @{ Name='Large File status gap'; Start='DataContext="{Binding LargeFileFinder}"'; End='DataContext="{Binding DuplicateFileFinder}"' },
    @{ Name='Duplicate status gap'; Start='DataContext="{Binding DuplicateFileFinder}"'; End='DataContext="{Binding StartupManager}"' }
)

foreach ($item in $gapSections) {
    $main = Replace-InSection `
        -Source $main `
        -StartAnchor $item.Start `
        -EndAnchor $item.End `
        -OldText 'Margin="0,14,0,0"' `
        -NewText 'Margin="0,10,0,0"' `
        -Name $item.Name
}
'@.TrimEnd()

$newBlock = @'
$gapSections = @(
    @{ Name='Health Check status gap'; Start='DataContext="{Binding HealthCheck}"'; End='Visibility="{Binding IsCleanerContentVisible'; Row='4' },
    @{ Name='Custom Clean status gap'; Start='DataContext="{Binding CustomClean}"'; End='DataContext="{Binding AutoCleanSchedule}"'; Row='4' },
    @{ Name='Large File status gap'; Start='DataContext="{Binding LargeFileFinder}"'; End='DataContext="{Binding DuplicateFileFinder}"'; Row='4' },
    @{ Name='Duplicate status gap'; Start='DataContext="{Binding DuplicateFileFinder}"'; End='DataContext="{Binding StartupManager}"'; Row='4' }
)

foreach ($item in $gapSections) {
    $sectionStart = $main.IndexOf($item.Start, [System.StringComparison]::Ordinal)
    if ($sectionStart -lt 0) { throw "$($item.Name) start anchor was not found." }

    $sectionEnd = $main.IndexOf($item.End, $sectionStart + $item.Start.Length, [System.StringComparison]::Ordinal)
    if ($sectionEnd -lt 0) { throw "$($item.Name) end anchor was not found." }

    $section = $main.Substring($sectionStart, $sectionEnd - $sectionStart)
    $statusStyle = 'Style="{StaticResource OperationStatusPanelStyle}"'
    $styleIndex = $section.IndexOf($statusStyle, [System.StringComparison]::Ordinal)
    if ($styleIndex -lt 0) { throw "$($item.Name) status panel style anchor was not found." }

    $borderIndex = $section.LastIndexOf('<Border', $styleIndex, [System.StringComparison]::Ordinal)
    if ($borderIndex -lt 0) { throw "$($item.Name) status panel border was not found." }

    $tagEnd = $section.IndexOf('>', $styleIndex, [System.StringComparison]::Ordinal)
    if ($tagEnd -lt 0) { throw "$($item.Name) status panel opening tag was not terminated." }

    $openingTag = $section.Substring($borderIndex, $tagEnd - $borderIndex + 1)
    $rowAnchor = 'Grid.Row="' + $item.Row + '"'
    if (-not $openingTag.Contains($rowAnchor)) {
        throw "$($item.Name) status panel did not match expected $rowAnchor."
    }

    $oldMargin = 'Margin="0,14,0,0"'
    $marginCount = ([regex]::Matches($openingTag, [regex]::Escape($oldMargin))).Count
    if ($marginCount -ne 1) {
        throw "$($item.Name) expected exactly one status-panel margin but found $marginCount."
    }

    $newOpeningTag = $openingTag.Replace($oldMargin, 'Margin="0,10,0,0"')
    $section = $section.Remove($borderIndex, $openingTag.Length).Insert($borderIndex, $newOpeningTag)
    $main = $main.Remove($sectionStart, $sectionEnd - $sectionStart).Insert($sectionStart, $section)
}
'@.TrimEnd()

$count = ([regex]::Matches($script, [regex]::Escape($oldBlock))).Count
if ($count -ne 1) {
    throw "Expected exactly one legacy result-to-status gap block but found $count."
}

$script = $script.Replace($oldBlock, $newBlock)

try {
    [System.IO.File]::WriteAllText($localPath, $script, $utf8)

    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($localPath, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count -ne 0) {
        throw ('Safe local script parse failed: ' + ($errors | ForEach-Object Message -join '; '))
    }

    Write-Host ''
    Write-Host 'SAFE FINAL TOOL LAYOUT WRAPPER'
    Write-Host '============================================'
    Write-Host 'Legacy ambiguous gap matcher : REPLACED IN TEMP COPY'
    Write-Host 'Status gap targeting         : GRID ROW + STATUS STYLE'
    Write-Host 'Tracked source script        : UNCHANGED'
    Write-Host ''

    & $localPath
    if ($LASTEXITCODE -ne 0) {
        throw "Final tool compactness script failed with exit code $LASTEXITCODE."
    }
}
finally {
    if (Test-Path -LiteralPath $localPath) {
        Remove-Item -LiteralPath $localPath -Force
    }
}

if (Test-Path -LiteralPath $localPath) {
    throw 'Temporary local script was not removed.'
}

Write-Host ''
Write-Host 'SAFE FINAL TOOL LAYOUT WRAPPER COMPLETE'
Write-Host '============================================'
Write-Host 'Temporary script : REMOVED'
Write-Host 'Commit           : NOT PERFORMED'
Write-Host 'Push             : NOT PERFORMED'
