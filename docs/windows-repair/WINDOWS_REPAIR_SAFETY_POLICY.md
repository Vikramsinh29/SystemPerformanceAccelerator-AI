# PC-SPA Windows Repair Safety Policy

## Scope

Sprint 35A provides read-only Windows repair assessment. It does not repair,
replace, clean, schedule, restart, or modify Windows.

## Approved commands

Only these exact Microsoft command forms are approved:

```text
%WINDIR%\System32\DISM.exe /Online /English /Cleanup-Image /CheckHealth
%WINDIR%\System32\sfc.exe /verifyonly
```

PC-SPA starts the executables directly with `UseShellExecute = false` and
individual argument-list entries. It does not invoke Command Prompt,
PowerShell, a batch file, or a user-supplied command.

## Prohibited operations

Sprint 35A must never generate or execute:

- DISM `/RestoreHealth`
- DISM `/ScanHealth`
- DISM `/StartComponentCleanup`
- SFC `/scannow`
- CHKDSK or any disk-repair switch
- restart or shutdown commands
- registry repair
- service disabling
- component-store cleanup
- downloaded repair sources

## Stop behaviour

After a Microsoft process starts, PC-SPA does not force-terminate it.
`Stop after current check` allows the current read-only check to finish and
skips remaining checks.

## Claims

A completed check is not a speed-improvement claim. Unknown or localized
output is classified as Inconclusive rather than guessed.
