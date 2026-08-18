$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repo

$mainWindowPath = Join-Path $repo 'src\SystemPerformanceAccelerator.Desktop\MainWindow.xaml'
$colorsPath = Join-Path $repo 'src\SystemPerformanceAccelerator.Desktop\Resources\Colors.xaml'
$themeManagerPath = Join-Path $repo 'src\SystemPerformanceAccelerator.Desktop\Services\ThemeManager.cs'

foreach ($path in @($mainWindowPath, $colorsPath, $themeManagerPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required file missing: $path"
    }
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
    throw "Expected exactly 3 pre-existing responsive UI changes but found $($currentChanged.Count)."
}

$utf8 = New-Object System.Text.UTF8Encoding($false, $true)
$main = [System.IO.File]::ReadAllText($mainWindowPath, $utf8)

function Replace-ExactlyOnce {
    param(
        [string]$Source,
        [string]$OldText,
        [string]$NewText,
        [string]$Name
    )

    $count = ([regex]::Matches($Source, [regex]::Escape($OldText))).Count
    if ($count -ne 1) {
        throw "$Name expected exactly one target but found $count."
    }

    return $Source.Replace($OldText, $NewText)
}

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
    if ($start -lt 0) {
        throw "$Name start anchor was not found."
    }

    $end = $Source.IndexOf($EndAnchor, $start + $StartAnchor.Length, [System.StringComparison]::Ordinal)
    if ($end -lt 0) {
        throw "$Name end anchor was not found."
    }

    $section = $Source.Substring($start, $end - $start)
    $count = ([regex]::Matches($section, [regex]::Escape($OldText))).Count
    if ($count -ne 1) {
        throw "$Name expected exactly one target but found $count."
    }

    $updated = $section.Replace($OldText, $NewText)
    return $Source.Remove($start, $end - $start).Insert($start, $updated)
}

Write-Host ''
Write-Host 'VERIFYING RESPONSIVE BASELINE'
Write-Host '============================================'

if ($main -notmatch 'MinHeight="190"') {
    throw 'Expected responsive result minimum-height markers are missing.'
}
if (-not $main.Contains('MinHeight="360" Style="{StaticResource ScheduleSurfaceCardStyle}"')) {
    throw 'Expected Auto Clean responsive minimum height is missing.'
}
if (-not $main.Contains('Background="{DynamicResource HelpSafetyBackgroundBrush}"')) {
    throw 'Expected Help safety palette is missing.'
}
if (-not $main.Contains('BorderThickness="1.5"')) {
    throw 'Expected current Help safety border thickness 1.5 was not found.'
}

Write-Host 'Responsive baseline : VERIFIED'

Write-Host ''
Write-Host 'NORMALIZING SHARED TOOL RHYTHM'
Write-Host '============================================'

$summaryOld = @'
        <Style x:Key="ResponsiveSummaryCardStyle"
               TargetType="Border"
               BasedOn="{StaticResource FluentCardStyle}">
            <Setter Property="Margin" Value="4" />
            <Setter Property="Padding" Value="12,10" />
            <Setter Property="MinHeight" Value="96" />
        </Style>
'@
$summaryNew = @'
        <Style x:Key="ResponsiveSummaryCardStyle"
               TargetType="Border"
               BasedOn="{StaticResource FluentCardStyle}">
            <Setter Property="Margin" Value="4" />
            <Setter Property="Padding" Value="12,9" />
            <Setter Property="MinHeight" Value="90" />
        </Style>
'@
$main = Replace-ExactlyOnce -Source $main -OldText $summaryOld.TrimEnd() -NewText $summaryNew.TrimEnd() -Name 'Responsive summary-card style'

$statusOld = @'
        <Style x:Key="OperationStatusPanelStyle"
               TargetType="Border"
               BasedOn="{StaticResource FluentCardStyle}">
            <Setter Property="MinHeight" Value="98" />
            <Setter Property="Padding" Value="16,13" />
            <Setter Property="VerticalAlignment" Value="Bottom" />
        </Style>
'@
$statusNew = @'
        <Style x:Key="OperationStatusPanelStyle"
               TargetType="Border"
               BasedOn="{StaticResource FluentCardStyle}">
            <Setter Property="MinHeight" Value="90" />
            <Setter Property="Padding" Value="15,11" />
            <Setter Property="VerticalAlignment" Value="Bottom" />
        </Style>
'@
$main = Replace-ExactlyOnce -Source $main -OldText $statusOld.TrimEnd() -NewText $statusNew.TrimEnd() -Name 'Operation status-panel style'

Write-Host 'Summary cards : 90px minimum'
Write-Host 'Status panels : 90px minimum'

Write-Host ''
Write-Host 'NORMALIZING RESULT / EMPTY-STATE REGIONS'
Write-Host '============================================'

# Existing responsive corrections made Custom Clean and Large File 190px.
# Bring them to a common 180px result minimum so the status panel remains visible
# at the 1240x720 review viewport while preserving a fully readable empty state.
$main = Replace-InSection `
    -Source $main `
    -StartAnchor 'DataContext="{Binding CustomClean}"' `
    -EndAnchor 'DataContext="{Binding AutoCleanSchedule}"' `
    -OldText 'MinHeight="190"' `
    -NewText 'MinHeight="180"' `
    -Name 'Custom Clean result minimum'

$main = Replace-InSection `
    -Source $main `
    -StartAnchor 'DataContext="{Binding LargeFileFinder}"' `
    -EndAnchor 'DataContext="{Binding DuplicateFileFinder}"' `
    -OldText 'MinHeight="190"' `
    -NewText 'MinHeight="180"' `
    -Name 'Large File Finder result minimum'

# Auto Clean's result card includes its own header and footer, so it needs more
# height than a plain DataGrid empty state, but 360px is visually excessive.
$main = Replace-InSection `
    -Source $main `
    -StartAnchor 'DataContext="{Binding AutoCleanSchedule}"' `
    -EndAnchor 'DataContext="{Binding LargeFileFinder}"' `
    -OldText 'MinHeight="360" Style="{StaticResource ScheduleSurfaceCardStyle}"' `
    -NewText 'MinHeight="310" Style="{StaticResource ScheduleSurfaceCardStyle}"' `
    -Name 'Auto Clean schedule result minimum'

# Health Check had no protected minimum at all. Add the same 180px result minimum.
$healthOld = '<Border Grid.Row="2"' + "`n" + '                            Style="{StaticResource FluentElevatedCardStyle}"' + "`n" + '                            ClipToBounds="True">'
$healthNew = '<Border Grid.Row="2"' + "`n" + '                            MinHeight="180"' + "`n" + '                            Style="{StaticResource FluentElevatedCardStyle}"' + "`n" + '                            ClipToBounds="True">'
$normalized = $main.Replace("`r`n", "`n")
$normalized = Replace-InSection `
    -Source $normalized `
    -StartAnchor 'DataContext="{Binding HealthCheck}"' `
    -EndAnchor 'Visibility="{Binding IsCleanerContentVisible' `
    -OldText $healthOld `
    -NewText $healthNew `
    -Name 'Health Check result minimum'
$main = $normalized

Write-Host 'Health Check result : 180px minimum'
Write-Host 'Custom Clean result : 180px minimum'
Write-Host 'Large File result   : 180px minimum'
Write-Host 'Auto Clean result   : 310px minimum (header/footer included)'

Write-Host ''
Write-Host 'REFINING HELP SAFETY BORDER'
Write-Host '============================================'

$helpStart = $main.IndexOf('DataContext="{Binding Help}"', [System.StringComparison]::Ordinal)
$helpEnd = $main.IndexOf('DataContext="{Binding Settings}"', $helpStart + 1, [System.StringComparison]::Ordinal)
if ($helpStart -lt 0 -or $helpEnd -lt 0) {
    throw 'Help section bounds were not found.'
}
$help = $main.Substring($helpStart, $helpEnd - $helpStart)
if (-not $help.Contains('Text="Safety first"')) {
    throw 'Help Safety first panel was not found.'
}
$borderCount = ([regex]::Matches($help, [regex]::Escape('BorderThickness="1.5"'))).Count
if ($borderCount -ne 1) {
    throw "Expected exactly one 1.5 Help safety border but found $borderCount."
}
$help = $help.Replace('BorderThickness="1.5"', 'BorderThickness="1"')
$main = $main.Remove($helpStart, $helpEnd - $helpStart).Insert($helpStart, $help)

Write-Host 'Help safety border : 1.5 -> 1.0'
Write-Host 'Green palette      : UNCHANGED'

Write-Host ''
Write-Host 'VERIFYING UNIFORM LAYOUT CONTRACT'
Write-Host '============================================'

foreach ($required in @(
    '<Setter Property="MinHeight" Value="90" />',
    'MinHeight="180"',
    'MinHeight="310" Style="{StaticResource ScheduleSurfaceCardStyle}"',
    'Background="{DynamicResource HelpSafetyBackgroundBrush}"',
    'BorderBrush="{DynamicResource HelpSafetyBorderBrush}"'
)) {
    if (-not $main.Contains($required)) {
        throw "Required uniform-layout marker missing: $required"
    }
}

if ($main.Contains('MinHeight="360" Style="{StaticResource ScheduleSurfaceCardStyle}"')) {
    throw 'Old oversized Auto Clean result minimum remains.'
}

[System.IO.File]::WriteAllText($mainWindowPath, $main, $utf8)

$afterChanged = @(git diff --name-only)
foreach ($file in $afterChanged) {
    if ($expectedChanged -notcontains $file) {
        throw "Unexpected changed file after uniform correction: $file"
    }
}
if ($afterChanged.Count -ne 3) {
    throw "Expected exactly 3 changed files after correction but found $($afterChanged.Count)."
}

git diff --check
if ($LASTEXITCODE -ne 0) {
    throw 'git diff --check failed.'
}

Write-Host ''
Write-Host 'UNIFORM TOOL LAYOUT SOURCE CORRECTION COMPLETE'
Write-Host '============================================'
Write-Host 'Reference rhythm      : Startup Manager-style balance'
Write-Host 'Summary-card minimum  : 90'
Write-Host 'Status-panel minimum  : 90'
Write-Host 'Health result minimum : 180'
Write-Host 'Custom result minimum : 180'
Write-Host 'Large result minimum  : 180'
Write-Host 'Auto result minimum   : 310'
Write-Host 'Help safety border    : 1.0'
Write-Host 'Help green palette    : PRESERVED'
Write-Host 'Changed files         : 3'
Write-Host 'No commit or push was performed.'
