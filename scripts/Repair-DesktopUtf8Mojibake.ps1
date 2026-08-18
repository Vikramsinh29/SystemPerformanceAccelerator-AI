& {
    $ErrorActionPreference = "Stop"

    $repo = Split-Path -Parent $PSScriptRoot
    Set-Location -LiteralPath $repo

    $targets = @(
        "src\SystemPerformanceAccelerator.Desktop\MainWindow.xaml",
        "src\SystemPerformanceAccelerator.Desktop\ViewModels\MainWindowViewModel.cs"
    )

    function Replace-ByteSequence {
        param(
            [byte[]]$Source,
            [byte[]]$Find,
            [byte[]]$Replace,
            [ref]$Count
        )

        $output = New-Object System.Collections.Generic.List[byte]
        $i = 0
        $matches = 0

        while ($i -lt $Source.Length) {
            $isMatch = $false

            if (($i + $Find.Length) -le $Source.Length) {
                $isMatch = $true
                for ($j = 0; $j -lt $Find.Length; $j++) {
                    if ($Source[$i + $j] -ne $Find[$j]) {
                        $isMatch = $false
                        break
                    }
                }
            }

            if ($isMatch) {
                foreach ($b in $Replace) {
                    $output.Add($b)
                }
                $i += $Find.Length
                $matches++
            }
            else {
                $output.Add($Source[$i])
                $i++
            }
        }

        $Count.Value = $matches
        return $output.ToArray()
    }

    function Contains-ByteSequence {
        param(
            [byte[]]$Source,
            [byte[]]$Find
        )

        if ($Find.Length -eq 0 -or $Source.Length -lt $Find.Length) {
            return $false
        }

        for ($i = 0; $i -le ($Source.Length - $Find.Length); $i++) {
            $match = $true
            for ($j = 0; $j -lt $Find.Length; $j++) {
                if ($Source[$i + $j] -ne $Find[$j]) {
                    $match = $false
                    break
                }
            }

            if ($match) {
                return $true
            }
        }

        return $false
    }

    $patterns = @(
        @{
            Name = "Bullet"
            Bad  = [byte[]]@(0xC3,0xA2,0xE2,0x82,0xAC,0xC2,0xA2)
            Good = [byte[]]@(0xE2,0x80,0xA2)
        },
        @{
            Name = "EmDash"
            Bad  = [byte[]]@(0xC3,0xA2,0xE2,0x82,0xAC,0xE2,0x80,0x9D)
            Good = [byte[]]@(0xE2,0x80,0x94)
        },
        @{
            Name = "EnDash"
            Bad  = [byte[]]@(0xC3,0xA2,0xE2,0x82,0xAC,0xE2,0x80,0x9C)
            Good = [byte[]]@(0xE2,0x80,0x93)
        },
        @{
            Name = "LeftArrow"
            Bad  = [byte[]]@(0xC3,0xA2,0xE2,0x80,0xA0,0xC2,0x90)
            Good = [byte[]]@(0xE2,0x86,0x90)
        }
    )

    $totals = @{}
    foreach ($pattern in $patterns) {
        $totals[$pattern.Name] = 0
    }

    foreach ($relativePath in $targets) {
        $fullPath = Join-Path $repo $relativePath
        if (-not (Test-Path -LiteralPath $fullPath)) {
            throw "Required source file is missing: $relativePath"
        }

        [byte[]]$bytes = [System.IO.File]::ReadAllBytes($fullPath)

        foreach ($pattern in $patterns) {
            $count = 0
            $bytes = Replace-ByteSequence `
                -Source $bytes `
                -Find $pattern.Bad `
                -Replace $pattern.Good `
                -Count ([ref]$count)
            $totals[$pattern.Name] += $count
        }

        [System.IO.File]::WriteAllBytes($fullPath, $bytes)
    }

    $totalRepairs = 0
    foreach ($pattern in $patterns) {
        $value = [int]$totals[$pattern.Name]
        $totalRepairs += $value
        Write-Host ("{0,-10}: {1}" -f $pattern.Name, $value)
    }

    if ($totalRepairs -le 0) {
        throw "No known UTF-8 mojibake byte sequences were found. Nothing was intentionally repaired."
    }

    foreach ($relativePath in $targets) {
        [byte[]]$bytes = [System.IO.File]::ReadAllBytes((Join-Path $repo $relativePath))
        foreach ($pattern in $patterns) {
            if (Contains-ByteSequence -Source $bytes -Find $pattern.Bad) {
                throw "Known mojibake remains after repair in $relativePath ($($pattern.Name))."
            }
        }
    }

    $utf8Strict = New-Object System.Text.UTF8Encoding($false, $true)
    $xaml = [System.IO.File]::ReadAllText(
        (Join-Path $repo $targets[0]),
        $utf8Strict)
    $viewModel = [System.IO.File]::ReadAllText(
        (Join-Path $repo $targets[1]),
        $utf8Strict)

    $bullet = [string][char]0x2022

    if (-not $xaml.Contains("Preview first $bullet confirmation required")) {
        throw "The Custom Clean confirmation badge was not restored correctly."
    }

    if (-not $viewModel.Contains("edition $bullet local system utility")) {
        throw "The edition status text was not restored correctly."
    }

    git diff --check
    if ($LASTEXITCODE -ne 0) {
        throw "git diff --check failed after UTF-8 repair."
    }

    $changedFiles = @(git diff --name-only)
    $allowedFiles = @(
        "src/SystemPerformanceAccelerator.Desktop/MainWindow.xaml",
        "src/SystemPerformanceAccelerator.Desktop/ViewModels/MainWindowViewModel.cs"
    )

    foreach ($changed in $changedFiles) {
        if ($allowedFiles -notcontains $changed) {
            throw "Unexpected file changed by UTF-8 repair: $changed"
        }
    }

    if ($changedFiles.Count -ne 2) {
        throw "Expected exactly 2 repaired source files; found $($changedFiles.Count)."
    }

    Write-Host ""
    Write-Host "UTF-8 SOURCE REPAIR COMPLETE"
    Write-Host "Changed files:"
    $changedFiles | ForEach-Object { Write-Host " - $_" }
    Write-Host "No commit or push was performed."
}
