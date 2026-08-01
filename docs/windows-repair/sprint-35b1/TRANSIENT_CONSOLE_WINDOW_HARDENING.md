# Sprint 35B1.2 — Transient Console Window Hardening

## Observed behaviour

During manual Windows Repair verification, the user observed an SFC console or
terminal window appear briefly and disappear after several seconds. The issue
was intermittent and had occurred twice.

The assessment itself completed normally. The defect is therefore limited to
professional desktop presentation and process-window suppression.

## Existing launch safeguards

Before this correction, PC-SPA already used:

- `UseShellExecute = false`
- `CreateNoWindow = true`
- redirected standard output
- redirected standard error

## Narrow hardening

This correction adds:

- `WindowStyle = ProcessWindowStyle.Hidden`

The existing `CreateNoWindow` setting remains in place. The two supported
settings now provide defense in depth against a transient console-window flash.

A dedicated regression test verifies both suppression settings and confirms
that output and error capture remain redirected.

## Unchanged safety boundary

This correction does not change:

- the executable paths
- DISM arguments
- SFC arguments
- output interpretation
- stop-after-current-check behaviour
- repair planning
- local history
- any repair execution capability

The only permitted commands remain:

- `DISM.exe /Online /English /Cleanup-Image /CheckHealth`
- `sfc.exe /verifyonly`
