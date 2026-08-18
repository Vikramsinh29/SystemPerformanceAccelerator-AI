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

$customPattern = '(?s)<Border Grid.Row="2"\s+Padding="0"\s+Style="\{StaticResource FluentElevatedCardStyle\}"\s+ClipToBounds="True">'
$customMatches = [regex]::Matches($main, $customPattern).Count
if ($customMatches -ne 1) {
    throw "Expected exactly one Custom Clean result container but found $customMatches."
}
$main = [regex]::Replace(
    $main,
    $customPattern,
    '<Border Grid.Row="2"' + "`n" + '                        MinHeight="190"' + "`n" + '                        Padding="0"' + "`n" + '                        Style="{StaticResource FluentElevatedCardStyle}"' + "`n" + '                        ClipToBounds="True">',
    1)

$autoOld = '<Border Grid.Row="2" Style="{StaticResource ScheduleSurfaceCardStyle}">'
if (([regex]::Matches($main, [regex]::Escape($autoOld))).Count -ne 1) {
    throw 'Auto Clean schedule result container was not found exactly once.'
}
$main = $main.Replace(
    $autoOld,
    '<Border Grid.Row="2" MinHeight="360" Style="{StaticResource ScheduleSurfaceCardStyle}">')

$largeOld = '<Border Grid.Row="3" Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="8" ClipToBounds="True">'
if (([regex]::Matches($main, [regex]::Escape($largeOld))).Count -ne 1) {
    throw 'Large File Finder result container was not found exactly once.'
}
$main = $main.Replace(
    $largeOld,
    '<Border Grid.Row="3" MinHeight="190" Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="8" ClipToBounds="True">')

# Compact only the Auto Clean empty-state block, anchored by its unique title.
$autoEmptyPattern = '(?s)<Border Padding="34"\s+Background="Transparent"\s+Visibility="\{Binding IsEmptyStateVisible, Converter=\{StaticResource BooleanToVisibilityConverter\}\}">(?<body>.*?)Text="No schedules yet"(?<tail>.*?)</Border>\s+</Grid>\s+<Border Grid.Row="2"'
$autoEmptyMatch = [regex]::Match($main, $autoEmptyPattern)
if (-not $autoEmptyMatch.Success) {
    throw 'Auto Clean empty-state block was not found safely.'
}
$autoEmptyOriginal = $autoEmptyMatch.Value
$autoEmptyUpdated = $autoEmptyOriginal.Replace('Padding="34"', 'Padding="20"')
$autoEmptyUpdated = $autoEmptyUpdated.Replace('Width="62" Height="62"', 'Width="54" Height="54"')
$autoEmptyUpdated = $autoEmptyUpdated.Replace('CornerRadius="18" HorizontalAlignment="Center"', 'CornerRadius="16" HorizontalAlignment="Center"')
$autoEmptyUpdated = $autoEmptyUpdated.Replace('Margin="0,18,0,0"' + "`r`n" + '                                                   Text="No schedules yet"', 'Margin="0,12,0,0"' + "`r`n" + '                                                   Text="No schedules yet"')
$autoEmptyUpdated = $autoEmptyUpdated.Replace('Margin="0,18,0,0"' + "`n" + '                                                   Text="No schedules yet"', 'Margin="0,12,0,0"' + "`n" + '                                                   Text="No schedules yet"')
$main = $main.Replace($autoEmptyOriginal, $autoEmptyUpdated)

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

$helpPattern = '(?s)<Border Margin="0,0,0,14"\s+Padding="18"\s+Background="\{DynamicResource SuccessSoftBrush\}"\s+BorderBrush="\{DynamicResource SuccessBrush\}"\s+BorderThickness="1"\s+CornerRadius="10">(?<body>.*?Text="Safety first".*?</Border>)'
$helpMatch = [regex]::Match($main, $helpPattern)
if (-not $helpMatch.Success) {
    throw 'Help Safety first panel was not found safely.'
}
$helpOriginal = $helpMatch.Value
$helpUpdated = $helpOriginal.Replace('Background="{DynamicResource SuccessSoftBrush}"', 'Background="{DynamicResource HelpSafetyBackgroundBrush}"')
$helpUpdated = $helpUpdated.Replace('BorderBrush="{DynamicResource SuccessBrush}"', 'BorderBrush="{DynamicResource HelpSafetyBorderBrush}"')
$helpUpdated = $helpUpdated.Replace('BorderThickness="1"', 'BorderThickness="1.5"')
$helpUpdated = $helpUpdated.Replace('Foreground="{DynamicResource SuccessBrush}"', 'Foreground="{DynamicResource HelpSafetyAccentBrush}"')
$helpUpdated = $helpUpdated.Replace('Foreground="{DynamicResource TextSecondaryBrush}"', 'Foreground="{DynamicResource HelpSafetyTextBrush}"')
$main = $main.Replace($helpOriginal, $helpUpdated)

[System.IO.File]::WriteAllText($mainWindowPath, $main, $utf8)
[System.IO.File]::WriteAllText($colorsPath, $colors, $utf8)
[System.IO.File]::WriteAllText($themeManagerPath, $theme, $utf8)

Write-Host 'Dark panel  : deep emerald + neon/light green accents'
Write-Host 'Light panel : pale mint + strong green accents'
Write-Host 'Global success colors: unchanged'

Write-Host ''
Write-Host 'VERIFYING SOURCE CONTRACT'
Write-Host '============================================'

if ($main.Contains($legacyHeight)) {
    throw 'A legacy fixed viewport-height binding remains.'
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
