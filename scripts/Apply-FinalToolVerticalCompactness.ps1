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

function Replace-BlockInSection {
    param(
        [string]$Source,
        [string]$StartAnchor,
        [string]$EndAnchor,
        [string]$BlockStart,
        [string]$BlockEnd,
        [string]$NewBlock,
        [string]$Name
    )

    $sectionStart = $Source.IndexOf($StartAnchor, [System.StringComparison]::Ordinal)
    if ($sectionStart -lt 0) { throw "$Name section start was not found." }

    $sectionEnd = $Source.IndexOf($EndAnchor, $sectionStart + $StartAnchor.Length, [System.StringComparison]::Ordinal)
    if ($sectionEnd -lt 0) { throw "$Name section end was not found." }

    $blockStartIndex = $Source.IndexOf($BlockStart, $sectionStart, [System.StringComparison]::Ordinal)
    if ($blockStartIndex -lt 0 -or $blockStartIndex -ge $sectionEnd) {
        throw "$Name block start was not found in section."
    }

    $blockEndIndex = $Source.IndexOf($BlockEnd, $blockStartIndex + $BlockStart.Length, [System.StringComparison]::Ordinal)
    if ($blockEndIndex -lt 0 -or $blockEndIndex -gt $sectionEnd) {
        throw "$Name block end was not found in section."
    }

    return $Source.Remove($blockStartIndex, $blockEndIndex - $blockStartIndex).Insert($blockStartIndex, $NewBlock)
}

Write-Host ''
Write-Host 'VERIFYING CURRENT UNIFORM-LAYOUT BASELINE'
Write-Host '============================================'

foreach ($required in @(
    '<Setter Property="MinHeight" Value="90" />',
    'MinHeight="180"',
    'MinHeight="310" Style="{StaticResource ScheduleSurfaceCardStyle}"',
    'Background="{DynamicResource HelpSafetyBackgroundBrush}"',
    'BorderThickness="1"'
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
Write-Host 'NORMALIZING STARTUP MANAGER'
Write-Host '============================================'

$startupItemsCard = @'
<Border Grid.Column="0"
                        Padding="12,9"
                        MinHeight="90"
                        Style="{StaticResource FluentCardStyle}">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="44" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>
                            <Border Style="{StaticResource SummaryMetricIconStyle}"
                                    Background="{DynamicResource AccentSoftBrush}">
                                <TextBlock Text="&#xE8FD;"
                                           Foreground="{DynamicResource AccentBrush}"
                                           Style="{StaticResource SummaryMetricGlyphStyle}" />
                            </Border>
                            <StackPanel Grid.Column="1" VerticalAlignment="Center">
                                <TextBlock Text="ITEMS FOUND" Style="{StaticResource FluentCaptionStyle}" />
                                <TextBlock Text="{Binding ItemsFound}"
                                           Foreground="{DynamicResource TextPrimaryBrush}"
                                           Style="{StaticResource SummaryMetricValueStyle}" />
                            </StackPanel>
                        </Grid>
                    </Border>
                    '
'@.TrimEnd("`r","`n")

$main = Replace-BlockInSection `
    -Source $main `
    -StartAnchor 'DataContext="{Binding StartupManager}"' `
    -EndAnchor 'DataContext="{Binding WindowsRepairAssessment}"' `
    -BlockStart '<Border Grid.Column="0"' `
    -BlockEnd '<Border Grid.Column="2"' `
    -NewBlock $startupItemsCard `
    -Name 'Startup Manager first summary card'

$startupStart = $main.IndexOf('DataContext="{Binding StartupManager}"', [System.StringComparison]::Ordinal)
$startupEnd = $main.IndexOf('DataContext="{Binding WindowsRepairAssessment}"', $startupStart + 1, [System.StringComparison]::Ordinal)
if ($startupStart -lt 0 -or $startupEnd -lt 0) { throw 'Startup Manager section bounds were not found.' }
$startup = $main.Substring($startupStart, $startupEnd - $startupStart)

$oldStartupMetric = 'Padding="16,14" Style="{StaticResource FluentCardStyle}"'
$startupMetricCount = ([regex]::Matches($startup, [regex]::Escape($oldStartupMetric))).Count
if ($startupMetricCount -ne 3) {
    throw "Expected 3 Startup Manager metric cards but found $startupMetricCount."
}
$startup = $startup.Replace($oldStartupMetric, 'Padding="12,9" MinHeight="90" Style="{StaticResource FluentCardStyle}"')

$startupResultOld = '<Border Grid.Row="2" Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="8" ClipToBounds="True">'
$startupResultCount = ([regex]::Matches($startup, [regex]::Escape($startupResultOld))).Count
if ($startupResultCount -ne 1) { throw "Expected one Startup Manager result card but found $startupResultCount." }
$startup = $startup.Replace($startupResultOld, '<Border Grid.Row="2" MinHeight="160" Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="8" ClipToBounds="True">')

$startupHeaderOld = 'Padding="14,10"'
$startupHeaderCount = ([regex]::Matches($startup, [regex]::Escape($startupHeaderOld))).Count
if ($startupHeaderCount -lt 1) { throw 'Startup Manager info-strip padding target was not found.' }
$startup = $startup.Replace($startupHeaderOld, 'Padding="14,8"')

if (-not $startup.Contains('Text="Administrator permission active"')) {
    throw 'Startup Manager legacy administrator badge text was not found.'
}
$startup = $startup.Replace('Text="Administrator permission active"', 'Text="UAC only when required"')

if (-not $startup.Contains('Margin="0,14,0,0"')) {
    throw 'Startup Manager status gap target was not found.'
}
$startup = $startup.Replace('Margin="0,14,0,0"', 'Margin="0,10,0,0"')

$main = $main.Remove($startupStart, $startupEnd - $startupStart).Insert($startupStart, $startup)

Write-Host 'Startup first summary : ITEMS FOUND (correct domain data)'
Write-Host 'Startup metric cards  : 90px uniform'
Write-Host 'Startup result area   : 160px minimum'
Write-Host 'Startup info strip    : compacted'
Write-Host 'Startup UAC badge     : least-privilege wording'
Write-Host 'Startup status gap    : 10px'

Write-Host ''
Write-Host 'NORMALIZING WINDOWS REPAIR'
Write-Host '============================================'

$repairChecksCard = @'
<Border Grid.Column="0"
                Padding="12,9"
                MinHeight="90"
                Style="{StaticResource FluentCardStyle}">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                </Grid.RowDefinitions>

                <TextBlock Text="ASSESSMENT CHECKS"
                           Style="{StaticResource FluentCaptionStyle}" />

                <Grid Grid.Row="1" Margin="0,5,0,0">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="10" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>

                    <CheckBox Grid.Column="0"
                              IsChecked="{Binding CheckComponentStore, Mode=TwoWay}"
                              Foreground="{DynamicResource TextPrimaryBrush}"
                              VerticalContentAlignment="Center">
                        <StackPanel Margin="4,0,0,0">
                            <TextBlock Text="DISM CheckHealth"
                                       FontSize="11.5"
                                       FontWeight="SemiBold" />
                            <TextBlock Margin="0,1,0,0"
                                       Text="Windows component store"
                                       Foreground="{DynamicResource TextSecondaryBrush}"
                                       FontSize="9.5"
                                       TextWrapping="Wrap" />
                        </StackPanel>
                    </CheckBox>

                    <CheckBox Grid.Column="2"
                              IsChecked="{Binding VerifyProtectedSystemFiles, Mode=TwoWay}"
                              Foreground="{DynamicResource TextPrimaryBrush}"
                              VerticalContentAlignment="Center">
                        <StackPanel Margin="4,0,0,0">
                            <TextBlock Text="SFC VerifyOnly"
                                       FontSize="11.5"
                                       FontWeight="SemiBold" />
                            <TextBlock Margin="0,1,0,0"
                                       Text="Protected Windows files"
                                       Foreground="{DynamicResource TextSecondaryBrush}"
                                       FontSize="9.5"
                                       TextWrapping="Wrap" />
                        </StackPanel>
                    </CheckBox>
                </Grid>
            </Grid>
        </Border>

        '
'@.TrimEnd("`r","`n")

$main = Replace-BlockInSection `
    -Source $main `
    -StartAnchor 'DataContext="{Binding WindowsRepairAssessment}"' `
    -EndAnchor 'DataContext="{Binding SystemMonitor}"' `
    -BlockStart '<Border Grid.Column="0"' `
    -BlockEnd '<Border Grid.Column="2"' `
    -NewBlock $repairChecksCard `
    -Name 'Windows Repair assessment-check summary card'

$repairStart = $main.IndexOf('DataContext="{Binding WindowsRepairAssessment}"', [System.StringComparison]::Ordinal)
$repairEnd = $main.IndexOf('DataContext="{Binding SystemMonitor}"', $repairStart + 1, [System.StringComparison]::Ordinal)
if ($repairStart -lt 0 -or $repairEnd -lt 0) { throw 'Windows Repair section bounds were not found.' }
$repair = $main.Substring($repairStart, $repairEnd - $repairStart)

$repairMetricOld = 'Padding="12,6"' + "`n" + '                MinHeight="66"' + "`n" + '                Style="{StaticResource FluentCardStyle}">'
$repairMetricCount = ([regex]::Matches($repair, [regex]::Escape($repairMetricOld))).Count
if ($repairMetricCount -ne 3) {
    throw "Expected 3 Windows Repair summary metric cards but found $repairMetricCount."
}
$repair = $repair.Replace($repairMetricOld, 'Padding="12,9"' + "`n" + '                MinHeight="90"' + "`n" + '                Style="{StaticResource FluentCardStyle}">')

if (-not $repair.Contains('<ColumnDefinition Width="0.82*" MinWidth="220" />')) {
    throw 'Windows Repair safeguards column target was not found.'
}
$repair = $repair.Replace('<ColumnDefinition Width="0.82*" MinWidth="220" />', '<ColumnDefinition Width="1.0*" MinWidth="250" />')
$repair = $repair.Replace('<ColumnDefinition Width="10" />' + "`n" + '            <ColumnDefinition Width="2.55*" />', '<ColumnDefinition Width="10" />' + "`n" + '            <ColumnDefinition Width="2.4*" />')

$compactRepairs = @(
    @{ Old='Padding="13"' + "`n" + '                Style="{StaticResource FluentElevatedCardStyle}"'; New='Padding="12"' + "`n" + '                Style="{StaticResource FluentElevatedCardStyle}"'; Name='safeguards padding' },
    @{ Old='<StackPanel Grid.Row="1" Margin="0,7,0,0">'; New='<StackPanel Grid.Row="1" Margin="0,5,0,0">'; Name='safeguards intro gap' },
    @{ Old='<StackPanel Grid.Row="2" Margin="0,7,0,0">'; New='<StackPanel Grid.Row="2" Margin="0,5,0,0">'; Name='safeguards bullet gap' },
    @{ Old='Margin="0,14,0,0"' + "`n" + '                        Padding="9,6"' + "`n" + '                        Background="{DynamicResource WarningSoftBrush}"'; New='Margin="0,8,0,0"' + "`n" + '                        Padding="8,5"' + "`n" + '                        Background="{DynamicResource WarningSoftBrush}"'; Name='safe-stop compacting' },
    @{ Old='<StackPanel Grid.Row="4" Margin="0,7,0,0">'; New='<StackPanel Grid.Row="4" Margin="0,5,0,0">'; Name='latest-reference gap' },
    @{ Old='Text="Active Microsoft repair or check is never force-closed"'; New='Text="Active Microsoft repair/check is never force-closed"'; Name='safeguard wrapping' }
)

foreach ($item in $compactRepairs) {
    $count = ([regex]::Matches($repair, [regex]::Escape($item.Old))).Count
    if ($count -ne 1) {
        throw "Windows Repair $($item.Name) expected one target but found $count."
    }
    $repair = $repair.Replace($item.Old, $item.New)
}

$main = $main.Remove($repairStart, $repairEnd - $repairStart).Insert($repairStart, $repair)

Write-Host 'Repair summary cards   : 90px uniform'
Write-Host 'Repair assessment checks: horizontal / compact'
Write-Host 'Repair safeguards width: increased'
Write-Host 'Repair safeguards      : compacted without removing safety content'
Write-Host 'Repair results panel   : preserved'

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
    'Grid.Row="3" MinHeight="160" Background="{DynamicResource SurfaceBrush}"',
    'Text="ITEMS FOUND"',
    'Text="UAC only when required"',
    'DataContext="{Binding WindowsRepairAssessment}"',
    '<ColumnDefinition Width="1.0*" MinWidth="250" />'
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
Write-Host 'Hero cards             : UNCHANGED'
Write-Host 'Summary cards          : UNIFORM 90'
Write-Host 'Status panel minimum   : 84'
Write-Host 'Result minimum         : 160'
Write-Host 'Auto result minimum    : 285'
Write-Host 'Duplicate action card  : COMPACTED'
Write-Host 'Startup first summary  : ITEMS FOUND'
Write-Host 'Startup metric cards   : UNIFORM'
Write-Host 'Startup UAC wording    : LEAST-PRIVILEGE'
Write-Host 'Windows Repair summaries: UNIFORM'
Write-Host 'Windows Repair safeguards: COMPACT / READABLE'
Write-Host 'Result/status gap      : 10'
Write-Host 'Help green palette     : PRESERVED'
Write-Host 'Help border            : 1.0 PRESERVED'
Write-Host 'Changed files          : 3'
Write-Host 'No commit or push was performed.'
