$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repo

$mainWindowPath = Join-Path $repo 'src\SystemPerformanceAccelerator.Desktop\MainWindow.xaml'
$colorsPath = Join-Path $repo 'src\SystemPerformanceAccelerator.Desktop\Resources\Colors.xaml'
$themeManagerPath = Join-Path $repo 'src\SystemPerformanceAccelerator.Desktop\Services\ThemeManager.cs'
$testPath = Join-Path $repo 'tests\SystemPerformanceAccelerator.Tests\ResponsiveDesktopLayoutContractTests.cs'

foreach ($path in @($mainWindowPath, $colorsPath, $themeManagerPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required file missing: $path"
    }
}

$utf8 = New-Object System.Text.UTF8Encoding($false, $true)
$main = [System.IO.File]::ReadAllText($mainWindowPath, $utf8)
$colors = [System.IO.File]::ReadAllText($colorsPath, $utf8)
$theme = [System.IO.File]::ReadAllText($themeManagerPath, $utf8)

# 1. Replace legacy fixed viewport heights with MinHeight so the outer ScrollViewer
# can scroll instead of crushing star-sized result/empty-state rows.
$legacyHeight = 'Height="{Binding ViewportHeight, ElementName=ToolContentScrollViewer, Converter={StaticResource SubtractDoubleConverter}, ConverterParameter=34}"'
$newMinHeight = 'MinHeight="{Binding ViewportHeight, ElementName=ToolContentScrollViewer, Converter={StaticResource SubtractDoubleConverter}, ConverterParameter=34}"'
$heightMatches = ([regex]::Matches($main, [regex]::Escape($legacyHeight))).Count
if ($heightMatches -ne 7) {
    throw "Expected exactly 7 legacy fixed viewport-height bindings but found $heightMatches."
}
$main = $main.Replace($legacyHeight, $newMinHeight)

# 2. Give the three visually confirmed result/empty-state regions minimum usable height.
$customOld = '<Border Grid.Row="2"' + "`r`n" + '                        Padding="0"' + "`r`n" + '                        Style="{StaticResource FluentElevatedCardStyle}"' + "`r`n" + '                        ClipToBounds="True">'
$customNew = '<Border Grid.Row="2"' + "`r`n" + '                        MinHeight="190"' + "`r`n" + '                        Padding="0"' + "`r`n" + '                        Style="{StaticResource FluentElevatedCardStyle}"' + "`r`n" + '                        ClipToBounds="True">'
if (-not $main.Contains($customOld)) {
    $customOld = $customOld.Replace("`r`n", "`n")
    $customNew = $customNew.Replace("`r`n", "`n")
}
if (-not $main.Contains($customOld)) {
    throw 'Custom Clean result container anchor was not found.'
}
$main = $main.Replace($customOld, $customNew)

$autoOld = '<Border Grid.Row="2" Style="{StaticResource ScheduleSurfaceCardStyle}">'
$autoNew = '<Border Grid.Row="2" MinHeight="360" Style="{StaticResource ScheduleSurfaceCardStyle}">'
if (([regex]::Matches($main, [regex]::Escape($autoOld))).Count -ne 1) {
    throw 'Auto Clean schedule result container anchor was not found exactly once.'
}
$main = $main.Replace($autoOld, $autoNew)

$largeOld = '<Border Grid.Row="3" Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="8" ClipToBounds="True">'
$largeNew = '<Border Grid.Row="3" MinHeight="190" Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="8" ClipToBounds="True">'
if (([regex]::Matches($main, [regex]::Escape($largeOld))).Count -ne 1) {
    throw 'Large File Finder result container anchor was not found exactly once.'
}
$main = $main.Replace($largeOld, $largeNew)

# Compact the Auto Clean empty-state content slightly so it remains balanced at smaller heights.
$main = $main.Replace('<Border Padding="34"', '<Border Padding="20"')
$main = $main.Replace('<Border Width="62" Height="62" Background="{DynamicResource AccentSoftBrush}" CornerRadius="18" HorizontalAlignment="Center">', '<Border Width="54" Height="54" Background="{DynamicResource AccentSoftBrush}" CornerRadius="16" HorizontalAlignment="Center">')
$main = $main.Replace('<TextBlock Margin="0,18,0,0"' + "`r`n" + '                                                   Text="No schedules yet"', '<TextBlock Margin="0,12,0,0"' + "`r`n" + '                                                   Text="No schedules yet"')
$main = $main.Replace('<TextBlock Margin="0,18,0,0"' + "`n" + '                                                   Text="No schedules yet"', '<TextBlock Margin="0,12,0,0"' + "`n" + '                                                   Text="No schedules yet"')

# 3. Add dedicated theme-aware Help safety panel brushes to Colors.xaml.
$colorsAnchor = '    <SolidColorBrush x:Key="SuccessSoftBrush" presentationOptions:Freeze="False" Color="#15352A" />'
if (-not $colors.Contains($colorsAnchor)) {
    throw 'Colors.xaml success palette anchor was not found.'
}
$colorsReplacement = $colorsAnchor + "`n" +
'    <SolidColorBrush x:Key="HelpSafetyBackgroundBrush" presentationOptions:Freeze="False" Color="#0D261B" />' + "`n" +
'    <SolidColorBrush x:Key="HelpSafetyBorderBrush" presentationOptions:Freeze="False" Color="#32F58A" />' + "`n" +
'    <SolidColorBrush x:Key="HelpSafetyAccentBrush" presentationOptions:Freeze="False" Color="#78FFB6" />' + "`n" +
'    <SolidColorBrush x:Key="HelpSafetyTextBrush" presentationOptions:Freeze="False" Color="#CFF8DF" />'
$colors = $colors.Replace($colorsAnchor, $colorsReplacement)

# 4. Add the new resources to both ThemeManager palettes.
$lightAnchor = '            ["SuccessSoftBrush"] = Color.FromRgb(0xE8, 0xF5, 0xEE),'
if (-not $theme.Contains($lightAnchor)) {
    throw 'ThemeManager light success palette anchor was not found.'
}
$lightReplacement = $lightAnchor + "`n" +
'            ["HelpSafetyBackgroundBrush"] = Color.FromRgb(0xEC, 0xFF, 0xF5),' + "`n" +
'            ["HelpSafetyBorderBrush"] = Color.FromRgb(0x16, 0xC9, 0x6A),' + "`n" +
'            ["HelpSafetyAccentBrush"] = Color.FromRgb(0x0F, 0xAF, 0x58),' + "`n" +
'            ["HelpSafetyTextBrush"] = Color.FromRgb(0x26, 0x6A, 0x45),'
$theme = $theme.Replace($lightAnchor, $lightReplacement)

$darkAnchor = '            ["SuccessSoftBrush"] = Color.FromRgb(0x15, 0x35, 0x2A),'
if (-not $theme.Contains($darkAnchor)) {
    throw 'ThemeManager dark success palette anchor was not found.'
}
$darkReplacement = $darkAnchor + "`n" +
'            ["HelpSafetyBackgroundBrush"] = Color.FromRgb(0x0D, 0x26, 0x1B),' + "`n" +
'            ["HelpSafetyBorderBrush"] = Color.FromRgb(0x32, 0xF5, 0x8A),' + "`n" +
'            ["HelpSafetyAccentBrush"] = Color.FromRgb(0x78, 0xFF, 0xB6),' + "`n" +
'            ["HelpSafetyTextBrush"] = Color.FromRgb(0xCF, 0xF8, 0xDF),'
$theme = $theme.Replace($darkAnchor, $darkReplacement)

# 5. Apply dedicated Help safety palette without changing global success semantics.
$helpOld = '                                Background="{DynamicResource SuccessSoftBrush}"' + "`n" +
'                                BorderBrush="{DynamicResource SuccessBrush}"' + "`n" +
'                                BorderThickness="1"'
if (-not $main.Contains($helpOld)) {
    $helpOld = $helpOld.Replace("`n", "`r`n")
}
if (-not $main.Contains($helpOld)) {
    throw 'Help safety panel background/border anchor was not found.'
}
$helpNew = '                                Background="{DynamicResource HelpSafetyBackgroundBrush}"' + "`n" +
'                                BorderBrush="{DynamicResource HelpSafetyBorderBrush}"' + "`n" +
'                                BorderThickness="1.5"'
if ($helpOld.Contains("`r`n")) {
    $helpNew = $helpNew.Replace("`n", "`r`n")
}
$main = $main.Replace($helpOld, $helpNew)

$helpStart = $main.IndexOf('Text="Safety first"', [System.StringComparison]::Ordinal)
if ($helpStart -lt 0) {
    throw 'Help safety title was not found.'
}
$helpSliceStart = [Math]::Max(0, $helpStart - 800)
$helpSliceLength = [Math]::Min(2200, $main.Length - $helpSliceStart)
$helpSlice = $main.Substring($helpSliceStart, $helpSliceLength)
$helpSliceUpdated = $helpSlice.Replace('Foreground="{DynamicResource SuccessBrush}"', 'Foreground="{DynamicResource HelpSafetyAccentBrush}"')
$helpSliceUpdated = $helpSliceUpdated.Replace('Foreground="{DynamicResource TextSecondaryBrush}"', 'Foreground="{DynamicResource HelpSafetyTextBrush}"')
$main = $main.Remove($helpSliceStart, $helpSliceLength).Insert($helpSliceStart, $helpSliceUpdated)

# 6. Write source files using strict UTF-8 without BOM.
[System.IO.File]::WriteAllText($mainWindowPath, $main, $utf8)
[System.IO.File]::WriteAllText($colorsPath, $colors, $utf8)
[System.IO.File]::WriteAllText($themeManagerPath, $theme, $utf8)

# 7. Add regression coverage for the responsive and Help palette contract.
$testContent = @'
using Xunit;

namespace SystemPerformanceAccelerator.Tests;

public sealed class ResponsiveDesktopLayoutContractTests
{
    [Fact]
    public void ToolPages_UseScrollableMinimumViewportHeightInsteadOfFixedViewportHeight()
    {
        var xaml = ReadRepositoryFile("src", "SystemPerformanceAccelerator.Desktop", "MainWindow.xaml");

        Assert.DoesNotContain(
            "Height=\"{Binding ViewportHeight, ElementName=ToolContentScrollViewer, Converter={StaticResource SubtractDoubleConverter}, ConverterParameter=34}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(
                xaml,
                "MinHeight=\"{Binding ViewportHeight, ElementName=ToolContentScrollViewer, Converter={StaticResource SubtractDoubleConverter}, ConverterParameter=34}\"") >= 7);
    }

    [Fact]
    public void ConfirmedEmptyStateRegions_HaveMinimumUsableHeight()
    {
        var xaml = ReadRepositoryFile("src", "SystemPerformanceAccelerator.Desktop", "MainWindow.xaml");

        Assert.Contains("Grid.Row=\"2\"\n                        MinHeight=\"190\"", NormalizeNewLines(xaml), StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"2\" MinHeight=\"360\" Style=\"{StaticResource ScheduleSurfaceCardStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"3\" MinHeight=\"190\" Background=\"{DynamicResource SurfaceBrush}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpSafetyPanel_UsesDedicatedThemeAwareGreenPalette()
    {
        var xaml = ReadRepositoryFile("src", "SystemPerformanceAccelerator.Desktop", "MainWindow.xaml");
        var colors = ReadRepositoryFile("src", "SystemPerformanceAccelerator.Desktop", "Resources", "Colors.xaml");
        var themeManager = ReadRepositoryFile("src", "SystemPerformanceAccelerator.Desktop", "Services", "ThemeManager.cs");

        Assert.Contains("HelpSafetyBackgroundBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("HelpSafetyBorderBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("HelpSafetyAccentBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("HelpSafetyTextBrush", xaml, StringComparison.Ordinal);

        Assert.Contains("x:Key=\"HelpSafetyBackgroundBrush\"", colors, StringComparison.Ordinal);
        Assert.Contains("[\"HelpSafetyBackgroundBrush\"]", themeManager, StringComparison.Ordinal);
        Assert.Contains("[\"HelpSafetyBorderBrush\"]", themeManager, StringComparison.Ordinal);
        Assert.Contains("[\"HelpSafetyAccentBrush\"]", themeManager, StringComparison.Ordinal);
        Assert.Contains("[\"HelpSafetyTextBrush\"]", themeManager, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string NormalizeNewLines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string ReadRepositoryFile(params string[] relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file '{Path.Combine(relativePath)}'.");
    }
}
'@
[System.IO.File]::WriteAllText($testPath, $testContent.TrimStart(), $utf8)

# 8. Validate scope and known markers.
if ($main.Contains($legacyHeight)) {
    throw 'A legacy fixed viewport-height binding remains.'
}
if (-not $main.Contains('Background="{DynamicResource HelpSafetyBackgroundBrush}"')) {
    throw 'Help safety background resource was not applied.'
}
if (-not $main.Contains('BorderBrush="{DynamicResource HelpSafetyBorderBrush}"')) {
    throw 'Help safety border resource was not applied.'
}
if (-not $main.Contains('MinHeight="360" Style="{StaticResource ScheduleSurfaceCardStyle}"')) {
    throw 'Auto Clean minimum result height was not applied.'
}

$changed = @(git diff --name-only)
$expected = @(
    'src/SystemPerformanceAccelerator.Desktop/MainWindow.xaml',
    'src/SystemPerformanceAccelerator.Desktop/Resources/Colors.xaml',
    'src/SystemPerformanceAccelerator.Desktop/Services/ThemeManager.cs',
    'tests/SystemPerformanceAccelerator.Tests/ResponsiveDesktopLayoutContractTests.cs'
)
foreach ($file in $changed) {
    if ($expected -notcontains $file) {
        throw "Unexpected changed file: $file"
    }
}
if ($changed.Count -ne 4) {
    Write-Host 'Changed files:'
    $changed | ForEach-Object { Write-Host " - $_" }
    throw "Expected exactly 4 changed files but found $($changed.Count)."
}

git diff --check
if ($LASTEXITCODE -ne 0) {
    throw 'git diff --check failed.'
}

Write-Host ''
Write-Host 'RESPONSIVE UI SOURCE CORRECTION COMPLETE'
Write-Host '============================================'
Write-Host 'Fixed viewport pages : converted to MinHeight'
Write-Host 'Custom Clean result  : minimum height protected'
Write-Host 'Auto Clean empty     : minimum height protected'
Write-Host 'Large File empty     : minimum height protected'
Write-Host 'Help safety palette  : emerald + light/neon green, theme-aware'
Write-Host 'Regression test      : added'
Write-Host 'No commit or push was performed.'
