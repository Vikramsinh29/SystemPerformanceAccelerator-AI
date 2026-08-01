# Sprint 35B2 - Combined Guided Windows Repair

## Purpose

Sprint 35B2 turns an eligible Sprint 35B1 readiness preview into one explicitly
confirmed, foreground guided repair workflow.

The sequence is fixed:

1. repeat execution-time safety checks
2. run DISM RestoreHealth with NoRestart
3. run SFC Scannow
4. run the existing DISM CheckHealth verification
5. run the existing SFC VerifyOnly verification

## Command allowlist

Only these two state-changing commands are permitted:

- `DISM.exe /Online /English /Cleanup-Image /RestoreHealth /NoRestart`
- `sfc.exe /scannow`

The existing read-only assessment runner remains separate and unchanged.

No command shell, PowerShell, script, user-supplied argument, custom repair
source, component cleanup, registry repair, CHKDSK, restart scheduling, or
automatic restart is added.

## Evidence and consent gate

Execution requires:

- an assessment no more than 24 hours old
- an Attention result
- no blocked Sprint 35B1 preflight item
- Windows and administrator elevation
- DISM and SFC availability
- no supported pending-restart marker
- readable Windows-drive space
- at least 5 GB free under PC-SPA policy
- a new explicit confirmation immediately before execution

The execution service repeats readiness checks after the preview. A saved plan
is never treated as consent.

## Process safety

Once DISM or SFC starts, PC-SPA does not force-terminate it. Stop after current
step lets the active Microsoft process finish normally and skips only commands
that have not started.

DISM may use Windows Update as a repair source. PC-SPA does not claim that a
source is available and does not add a custom source in this sprint.

## Verification and honesty

A repair is not reported as healthy merely because DISM or SFC returned exit
code 0. The existing read-only assessment runs again after both repair commands.

Healthy verification produces Completed. Attention or Inconclusive verification
produces CompletedWithAttention. Failed, unsupported, or missing verification
fails closed.

## Local evidence

Sanitized repair execution records are stored locally with a maximum of 20
records and 90 days. Deleting Windows Repair history removes assessment,
readiness-preview, and repair-execution history.
