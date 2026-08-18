$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repo

$mainWindowPath = Join-Path $repo 'src\SystemPerformanceAccelerator.Desktop\MainWindow.xaml'
if (-not (Test-Path -LiteralPath $mainWindowPath)) {
    throw 'MainWindow.xaml was not found.'
}

$expectedChanged = @(
    'src/SystemPerformanceAccelerator.Desktop/MainWindow.xaml',
    'src/SystemPerformanceAccelerator.Desktop/Resources/Colors.xaml',
    'src/SystemPerformanceAccelerator.Desktop/Services/ThemeManager.cs'
)

$currentChanged = @(git diff --name-only)
foreach ($file in $currentChanged) {
    if ($expectedChanged -notcontains $file) {
        throw "Unexpected pre-existing changed file: $file"
    }
}
if ($currentChanged.Count -ne 3) {
    throw "Expected exactly 3 existing UI changes but found $($currentChanged.Count)."
}

$utf8 = New-Object System.Text.UTF8Encoding($false, $true)
$main = [System.IO.File]::ReadAllText($mainWindowPath, $utf8).Replace("`r`n", "`n")

function Replace-InSection {
    param(
        [string]$Source,
        [string]$StartAnchor,
        [string]$EndAnchor,
        [string]$OldText,
        [string]$NewText,
        [string]$Name
    )

    $start = $Source.IndexOf($StartAnchor, [System.StringComparison]::Ordinal)
    if ($start -lt 0) { throw "$Name start anchor was not found." }

    $end = $Source.IndexOf($EndAnchor, $start + $StartAnchor.Length, [System.StringComparison]::Ordinal)
    if ($end -lt 0) { throw "$Name end anchor was not found." }

    $section = $Source.Substring($start, $end - $start)
    $count = ([regex]::Matches($section, [regex]::Escape($OldText))).Count
    if ($count -ne 1) {
        throw "$Name expected exactly one target but found $count."
    }

    $section = $section.Replace($OldText, $NewText)
    return $Source.Remove($start, $end - $start).Insert($start, $section)
}

Write-Host ''
Write-Host 'VERIFYING CURRENT UNIFORM-LAYOUT BASELINE'
Write-Host '============================================'

foreach ($required in @(
    '<Setter Property="MinHeight" Value="90" />',
    'MinHeight="180"',
    'MinHeight="310" Style="{StaticResource ScheduleSurfaceCardStyle}"',
    'Background="{DynamicResource HelpSafetyBackgroundBrush}"'
)) {
    if (-not $main.Contains($required)) {
        throw "Expected current uniform-layout marker missing: $required"
    }
}

Write-Host 'Uniform-layout baseline : VERIFIED'

Write-Host ''
Write-Host 'COMPACTING SHARED STATUS PANEL'
Write-Host '============================================'

$statusOld = @'
        <Style x:Key="OperationStatusPanelStyle"
               TargetType="Border"
               BasedOn="{StaticResource FluentCardStyle}">
            <Setter Property="MinHeight" Value="90" />
            <Setter Property="Padding" Value="15,11" />
            <Setter Property="VerticalAlignment" Value="Bottom" />
        </Style>
'@.TrimEnd()

$statusNew = @'
        <Style x:Key="OperationStatusPanelStyle"
               TargetType="Border"
               BasedOn="{StaticResource FluentCardStyle}">
            <Setter Property="MinHeight" Value="84" />
            <Setter Property="Padding" Value="14,10" />
            <Setter Property="VerticalAlignment" Value="Bottom" />
        </Style>
'@.TrimEnd()

$statusCount = ([regex]::Matches($main, [regex]::Escape($statusOld))).Count
if ($statusCount -ne 1) {
    throw "Operation status style expected exactly once but found $statusCount."
}
$main = $main.Replace($statusOld, $statusNew)

Write-Host 'Shared status minimum : 90 -> 84'
Write-Host 'Shared status padding : 15,11 -> 14,10'

Write-Host ''
Write-Host 'COMPACTING RESULT / EMPTY-STATE REGIONS'
Write-Host '============================================'

$sections = @(
    @{ Name='Health Check'; Start='DataContext="{Binding HealthCheck}"'; End='Visibility="{Binding IsCleanerContentVisible'; Old='MinHeight="180"'; New='MinHeight="160"' },
    @{ Name='Custom Clean'; Start='DataContext="{Binding CustomClean}"'; End='DataContext="{Binding AutoCleanSchedule}"'; Old='MinHeight="180"'; New='MinHeight="160"' },
    @{ Name='Large File Finder'; Start='DataContext="{Binding LargeFileFinder}"'; End='DataContext="{Binding DuplicateFileFinder}"'; Old='MinHeight="180"'; New='MinHeight="160"' },
    @{ Name='Auto Clean'; Start='DataContext="{Binding AutoCleanSchedule}"'; End='DataContext="{Binding LargeFileFinder}"'; Old='MinHeight="310" Style="{StaticResource ScheduleSurfaceCardStyle}"'; New='MinHeight="285" Style="{StaticResource ScheduleSurfaceCardStyle}"' }
)

foreach ($item in $sections) {
    $main = Replace-InSection -Source $main -StartAnchor $item.Start -EndAnchor $item.End -OldText $item.Old -NewText $item.New -Name ($item.Name + ' minimum')
}

# Duplicate Finder had no protected result minimum yet.
$duplicateResultOld = '<Border Grid.Row="3" Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="8" ClipToBounds="True">'
$duplicateResultNew = '<Border Grid.Row="3" MinHeight="160" Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="8" ClipToBounds="True">'
$main = Replace-InSection `
    -Source $main `
    -StartAnchor 'DataContext="{Binding DuplicateFileFinder}"' `
    -EndAnchor 'DataContext="{Binding StartupManager}"' `
    -OldText $duplicateResultOld `
    -NewText $duplicateResultNew `
    -Name 'Duplicate Finder result minimum'

Write-Host 'Health result    : 160'
Write-Host 'Custom result    : 160'
Write-Host 'Large result     : 160'
Write-Host 'Duplicate result : 160'
Write-Host 'Auto result      : 285 (includes header/footer)'

Write-Host ''
Write-Host 'COMPACTING DUPLICATE FINDER ACTION CARD'
Write-Host '============================================'

$main = Replace-InSection `
    -Source $main `
    -StartAnchor 'DataContext="{Binding DuplicateFileFinder}"' `
    -EndAnchor 'DataContext="{Binding StartupManager}"' `
    -OldText '<Border Grid.Row="2" Margin="0,12,0,12" Padding="18,15" Style="{StaticResource FluentCardStyle}">' `
    -NewText '<Border Grid.Row="2" Margin="0,10,0,10" Padding="16,11" Style="{StaticResource FluentCardStyle}">' `
    -Name 'Duplicate Finder action card'

$main = Replace-InSection `
    -Source $main `
    -StartAnchor 'DataContext="{Binding DuplicateFileFinder}"' `
    -EndAnchor 'DataContext="{Binding StartupManager}"' `
    -OldText '<Grid Margin="0,7,0,0">' `
    -NewText '<Grid Margin="0,6,0,0">' `
    -Name 'Duplicate Finder folder-row gap'

$main = Replace-InSection `
    -Source $main `
    -StartAnchor 'DataContext="{Binding DuplicateFileFinder}"' `
    -EndAnchor 'DataContext="{Binding StartupManager}"' `
    -OldText '<Grid Grid.Row="1" Margin="0,11,0,0">' `
    -NewText '<Grid Grid.Row="1" Margin="0,8,0,0">' `
    -Name 'Duplicate Finder action-row gap'

Write-Host 'Duplicate card padding : 18,15 -> 16,11'
Write-Host 'Duplicate outer gap    : 12 -> 10'
Write-Host 'Duplicate row gaps     : reduced'

Write-Host ''
Write-Host 'REDUCING RESULT-TO-STATUS GAPS'
Write-Host '============================================'

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

Write-Host 'Normal result/status gap : 14 -> 10'

Write-Host ''
Write-Host 'VERIFYING FINAL VERTICAL CONTRACT'
Write-Host '============================================'

foreach ($required in @(
    '<Setter Property="MinHeight" Value="84" />',
    'MinHeight="160"',
    'MinHeight="285" Style="{StaticResource ScheduleSurfaceCardStyle}"',
    'Grid.Row="3" MinHeight="160" Background="{DynamicResource SurfaceBrush}"'
)) {
    if (-not $main.Contains($required)) {
        throw "Required final compactness marker missing: $required"
    }
}

[System.IO.File]::WriteAllText($mainWindowPath, $main, $utf8)

git diff --check
if ($LASTEXITCODE -ne 0) {
    throw 'git diff --check failed.'
}

$afterChanged = @(git diff --name-only)
foreach ($file in $afterChanged) {
    if ($expectedChanged -notcontains $file) {
        throw "Unexpected changed file after compactness correction: $file"
    }
}
if ($afterChanged.Count -ne 3) {
    throw "Expected exactly 3 changed UI files but found $($afterChanged.Count)."
}

Write-Host ''
Write-Host 'FINAL TOOL VERTICAL COMPACTNESS COMPLETE'
Write-Host '============================================'
Write-Host 'Hero cards            : UNCHANGED'
Write-Host 'Summary cards         : UNCHANGED (90)'
Write-Host 'Status panel minimum  : 84'
Write-Host 'Result minimum        : 160'
Write-Host 'Auto result minimum   : 285'
Write-Host 'Duplicate action card : COMPACTED'
Write-Host 'Result/status gap     : 10'
Write-Host 'Help green palette    : PRESERVED'
Write-Host 'Help border           : 1.0 PRESERVED'
Write-Host 'Changed files         : 3'
Write-Host 'No commit or push was performed.'
