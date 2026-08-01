# Sprint 35A Manual Windows Test Matrix

| ID | Scenario | Expected result | Evidence |
|---|---|---|---|
| WR-01 | Windows 10 x64, elevated | Preflight passes | Screenshot/report |
| WR-02 | Windows 11 x64, elevated | Preflight passes | Screenshot/report |
| WR-03 | DISM CheckHealth only | Only approved DISM command runs | Exported report |
| WR-04 | SFC VerifyOnly only | Only approved SFC command runs | Exported report |
| WR-05 | Both selected | DISM then SFC | Exported report |
| WR-06 | Stop during first check | First finishes; second skipped | Screenshot/report |
| WR-07 | Light theme | Layout readable | Screenshot |
| WR-08 | Dark theme | Layout readable | Screenshot |
| WR-09 | Export report | ZIP has three documented files | ZIP inspection |
| WR-10 | Delete history | Local records removed | Folder inspection |
| WR-11 | Non-English Windows | Unknown wording becomes Inconclusive | Report |
| WR-12 | Regression | Existing PC-SPA modules remain functional | Checklist |

Do not mark a row Passed without physical Windows evidence.

## Long-running activity and close protection

- While DISM or SFC is running, the bottom progress bar must animate continuously.
- The active Microsoft check name must remain visible.
- Elapsed time must update once per second.
- Status text must state that PC-SPA is still working and ask the user to keep the window open.
- A stop request must remain visible until the current Microsoft check finishes normally.
- The Windows taskbar button must show indeterminate progress while an assessment is running.
- Closing the main window during an active Microsoft check must be blocked with a clear explanation.
- After the check finishes, normal close behaviour and taskbar state must return.
