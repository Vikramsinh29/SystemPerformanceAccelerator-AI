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

$utf8 = New-Object System.Text.UTF8Encoding($false, $true)
$main = [System.IO.File]::ReadAllText($mainWindowPath, $utf8)
$colors = [System.IO.File]::ReadAllText($colorsPath, $utf8)
$theme = [System.IO.File]::ReadAllText($themeManagerPath, $utf8)

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

    $length = $end - $start
    $section = $Source.Substring($start, $length)
    $count = ([regex]::Matches($section, [regex]::Escape($OldText))).Count
    if ($count -ne 1) {
        throw "$Name expected exactly one target but found $count."
    }

    $section = $section.Replace($OldText, $NewText)
    return $Source.Remove($start, $length).Insert($start, $section)
}

Write-Host ''
Write-Host 'APPLYING RESPONSIVE VIEWPORT FIX'
Write-Host '============================================'

$legacyHeight = 'Height="{Binding ViewportHeight, ElementName=ToolContentScrollViewer, Converter={StaticResource SubtractDoubleConverter}, ConverterParameter=34}"'
$newMinHeight = 'MinHeight="{Binding ViewportHeight, ElementName=ToolContentScrollViewer, Converter={StaticResource SubtractDoubleConverter}, ConverterParameter=34}"'
$heightMatches = ([regex]::Matches($main, [regex]::Escape($legacyHeight))).Count
if ($heightMatches -ne 7) {
    throw "Expected exactly 7 legacy fixed viewport bindings but found $heightMatches."
}
$main = $main.Replace($legacyHeight, $newMinHeight)
Write-Host "Legacy fixed pages converted : $heightMatches"

Write-Host ''
Write-Host 'PROTECTING EMPTY/RESULT REGION HEIGHTS'
Write-Host '============================================'

$customOld = '<Border Grid.Row="2"' + "`n" + '                        Padding="0"' + "`n" + '                        Style="{StaticResource FluentElevatedCardStyle}"' + "`n" + '                        ClipToBounds="True">'
$customNew = '<Border Grid.Row="2"' + "`n" + '                        MinHeight="190"' + "`n" + '                        Padding="0"' + "`n" + '                        Style="{StaticResource FluentElevatedCardStyle}"' + "`n" + '                        ClipToBounds="True">'
$mainNormalized = $main.Replace("`r`n", "`n")
$mainNormalized = Replace-InSection `
    -Source $mainNormalized `
    -StartAnchor 'DataContext="{Binding CustomClean}"' `
    -EndAnchor 'DataContext="{Binding AutoCleanSchedule}"' `
    -OldText $customOld `
    -NewText $customNew `
    -Name 'Custom Clean result container'
$main = $mainNormalized

$autoOld = '<Border Grid.Row="2" Style="{StaticResource ScheduleSurfaceCardStyle}">'
$autoNew = '<Border Grid.Row="2" MinHeight="360" Style="{StaticResource ScheduleSurfaceCardStyle}">'
$main = Replace-InSection `
    -Source $main `
    -StartAnchor 'DataContext="{Binding AutoCleanSchedule}"' `
    -EndAnchor 'DataContext="{Binding LargeFileFinder}"' `
    -OldText $autoOld `
    -NewText $autoNew `
    -Name 'Auto Clean schedule result container'

$largeOld = '<Border Grid.Row="3" Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="8" ClipToBounds="True">'
$largeNew = '<Border Grid.Row="3" MinHeight="190" Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="8" ClipToBounds="True">'
$main = Replace-InSection `
    -Source $main `
    -StartAnchor 'DataContext="{Binding LargeFileFinder}"' `
    -EndAnchor 'DataContext="{Binding DuplicateFileFinder}"' `
    -OldText $largeOld `
    -NewText $largeNew `
    -Name 'Large File Finder result container'

$autoStart = $main.IndexOf('DataContext="{Binding AutoCleanSchedule}"', [System.StringComparison]::Ordinal)
$autoEnd = $main.IndexOf('DataContext="{Binding LargeFileFinder}"', $autoStart + 1, [System.StringComparison]::Ordinal)
if ($autoStart -lt 0 -or $autoEnd -lt 0) {
    throw 'Auto Clean section bounds were not found.'
}
$autoSection = $main.Substring($autoStart, $autoEnd - $autoStart)
if (-not $autoSection.Contains('Text="No schedules yet"')) {
    throw 'Auto Clean empty-state title was not found.'
}
$autoSection = $autoSection.Replace('Padding="34"', 'Padding="20"')
$autoSection = $autoSection.Replace('Width="62" Height="62"', 'Width="54" Height="54"')
$autoSection = $autoSection.Replace('CornerRadius="18" HorizontalAlignment="Center"', 'CornerRadius="16" HorizontalAlignment="Center"')
$autoSection = $autoSection.Replace('Margin="0,18,0,0"' + "`n" + '                                                   Text="No schedules yet"', 'Margin="0,12,0,0"' + "`n" + '                                                   Text="No schedules yet"')
$main = $main.Remove($autoStart, $autoEnd - $autoStart).Insert($autoStart, $autoSection)

Write-Host 'Custom Clean minimum result height : 190'
Write-Host 'Auto Clean schedule card minimum  : 360'
Write-Host 'Large File result minimum         : 190'

Write-Host ''
Write-Host 'ADDING THEME-AWARE HELP SAFETY PALETTE'
Write-Host '============================================'

$colorsAnchor = '    <SolidColorBrush x:Key="SuccessSoftBrush" presentationOptions:Freeze="False" Color="#15352A" />'
if (-not $colors.Contains($colorsAnchor)) {
    throw 'Colors.xaml success palette anchor was not found.'
}
if ($colors.Contains('x:Key="HelpSafetyBackgroundBrush"')) {
    throw 'Help safety palette already exists in Colors.xaml.'
}
$colors = $colors.Replace(
    $colorsAnchor,
    $colorsAnchor + "`n" +
    '    <SolidColorBrush x:Key="HelpSafetyBackgroundBrush" presentationOptions:Freeze="False" Color="#0D261B" />' + "`n" +
    '    <SolidColorBrush x:Key="HelpSafetyBorderBrush" presentationOptions:Freeze="False" Color="#32F58A" />' + "`n" +
    '    <SolidColorBrush x:Key="HelpSafetyAccentBrush" presentationOptions:Freeze="False" Color="#78FFB6" />' + "`n" +
    '    <SolidColorBrush x:Key="HelpSafetyTextBrush" presentationOptions:Freeze="False" Color="#CFF8DF" />')

$lightAnchor = '            ["SuccessSoftBrush"] = Color.FromRgb(0xE8, 0xF5, 0xEE),'
$darkAnchor = '            ["SuccessSoftBrush"] = Color.FromRgb(0x15, 0x35, 0x2A),'
if (-not $theme.Contains($lightAnchor) -or -not $theme.Contains($darkAnchor)) {
    throw 'ThemeManager success palette anchors were not found.'
}
if ($theme.Contains('["HelpSafetyBackgroundBrush"]')) {
    throw 'Help safety palette already exists in ThemeManager.'
}
$theme = $theme.Replace(
    $lightAnchor,
    $lightAnchor + "`n" +
    '            ["HelpSafetyBackgroundBrush"] = Color.FromRgb(0xEC, 0xFF, 0xF5),' + "`n" +
    '            ["HelpSafetyBorderBrush"] = Color.FromRgb(0x16, 0xC9, 0x6A),' + "`n" +
    '            ["HelpSafetyAccentBrush"] = Color.FromRgb(0x0F, 0xAF, 0x58),' + "`n" +
    '            ["HelpSafetyTextBrush"] = Color.FromRgb(0x26, 0x6A, 0x45),')
$theme = $theme.Replace(
    $darkAnchor,
    $darkAnchor + "`n" +
    '            ["HelpSafetyBackgroundBrush"] = Color.FromRgb(0x0D, 0x26, 0x1B),' + "`n" +
    '            ["HelpSafetyBorderBrush"] = Color.FromRgb(0x32, 0xF5, 0x8A),' + "`n" +
    '            ["HelpSafetyAccentBrush"] = Color.FromRgb(0x78, 0xFF, 0xB6),' + "`n" +
    '            ["HelpSafetyTextBrush"] = Color.FromRgb(0xCF, 0xF8, 0xDF),')

$helpStart = $main.IndexOf('DataContext="{Binding Help}"', [System.StringComparison]::Ordinal)
$helpEnd = $main.IndexOf('DataContext="{Binding Settings}"', $helpStart + 1, [System.StringComparison]::Ordinal)
if ($helpStart -lt 0 -or $helpEnd -lt 0) {
    throw 'Help section bounds were not found.'
}
$helpSection = $main.Substring($helpStart, $helpEnd - $helpStart)
if (-not $helpSection.Contains('Text="Safety first"')) {
    throw 'Help Safety first panel was not found.'
}
$helpSection = $helpSection.Replace('Background="{DynamicResource SuccessSoftBrush}"', 'Background="{DynamicResource HelpSafetyBackgroundBrush}"')
$helpSection = $helpSection.Replace('BorderBrush="{DynamicResource SuccessBrush}"', 'BorderBrush="{DynamicResource HelpSafetyBorderBrush}"')
$helpSection = $helpSection.Replace('BorderThickness="1"', 'BorderThickness="1.5"')
$helpSection = $helpSection.Replace('Foreground="{DynamicResource SuccessBrush}"', 'Foreground="{DynamicResource HelpSafetyAccentBrush}"')
$helpSection = $helpSection.Replace('Foreground="{DynamicResource TextSecondaryBrush}"', 'Foreground="{DynamicResource HelpSafetyTextBrush}"')
$main = $main.Remove($helpStart, $helpEnd - $helpStart).Insert($helpStart, $helpSection)

[System.IO.File]::WriteAllText($mainWindowPath, $main, $utf8)
[System.IO.File]::WriteAllText($colorsPath, $colors, $utf8)
[System.IO.File]::WriteAllText($themeManagerPath, $theme, $utf8)

Write-Host 'Dark panel  : deep emerald + neon/light green accents'
Write-Host 'Light panel : pale mint + strong green accents'
Write-Host 'Global success colors: unchanged'

Write-Host ''
Write-Host 'VERIFYING SOURCE CONTRACT'
Write-Host '============================================'

$legacyHeightPattern = '(?<!Min)Height="\{Binding ViewportHeight, ElementName=ToolContentScrollViewer, Converter=\{StaticResource SubtractDoubleConverter\}, ConverterParameter=34\}"'
$remainingLegacyHeightMatches = [regex]::Matches($main, $legacyHeightPattern).Count
if ($remainingLegacyHeightMatches -ne 0) {
    throw "A standalone legacy fixed viewport-height binding remains. Count: $remainingLegacyHeightMatches"
}
foreach ($required in @(
    'MinHeight="190"',
    'MinHeight="360" Style="{StaticResource ScheduleSurfaceCardStyle}"',
    'Background="{DynamicResource HelpSafetyBackgroundBrush}"',
    'BorderBrush="{DynamicResource HelpSafetyBorderBrush}"',
    'Foreground="{DynamicResource HelpSafetyAccentBrush}"',
    'Foreground="{DynamicResource HelpSafetyTextBrush}"'
)) {
    if (-not $main.Contains($required)) {
        throw "Required responsive/help marker missing: $required"
    }
}

$changed = @(git diff --name-only)
$expected = @(
    'src/SystemPerformanceAccelerator.Desktop/MainWindow.xaml',
    'src/SystemPerformanceAccelerator.Desktop/Resources/Colors.xaml',
    'src/SystemPerformanceAccelerator.Desktop/Services/ThemeManager.cs'
)
foreach ($file in $changed) {
    if ($expected -notcontains $file) {
        throw "Unexpected changed file: $file"
    }
}
if ($changed.Count -ne 3) {
    Write-Host 'Changed files:'
    $changed | ForEach-Object { Write-Host " - $_" }
    throw "Expected exactly 3 changed files but found $($changed.Count)."
}

git diff --check
if ($LASTEXITCODE -ne 0) {
    throw 'git diff --check failed.'
}

Write-Host ''
Write-Host 'RESPONSIVE UI SOURCE CORRECTION COMPLETE'
Write-Host '============================================'
Write-Host 'Viewport clipping    : STRUCTURALLY CORRECTED'
Write-Host 'Custom Clean empty   : PROTECTED'
Write-Host 'Auto Clean empty     : PROTECTED'
Write-Host 'Large File empty     : PROTECTED'
Write-Host 'Other legacy pages   : MINHEIGHT SCROLL BEHAVIOR'
Write-Host 'Help safety palette  : THEME-AWARE EMERALD + NEON/LIGHT GREEN'
Write-Host 'Changed files        : 3'
Write-Host 'No commit or push was performed.'