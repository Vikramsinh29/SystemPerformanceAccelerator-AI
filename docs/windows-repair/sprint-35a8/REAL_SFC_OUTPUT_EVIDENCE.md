# Sprint 35A.8 - Real SFC Output Evidence

## Source

This correction is based only on the user-exported, sanitized PC-SPA assessment:

`ASSESS-20260801085101-AA4DA4C1`

The exported package contained only:

- `README.txt`
- `manifest.json`
- `assessment.json`

## Observed result

DISM CheckHealth completed with exit code `0` and reported:

`No component store corruption detected.`

SFC VerifyOnly also completed with exit code `0`, but the captured output
contained a null character between ordinary text characters. Because the
classification phrase was not contiguous, Sprint 35A conservatively reported
the SFC result as `Inconclusive`.

After removing only those embedded null separators, the final Microsoft text
is:

`Windows Resource Protection did not find any integrity violations.`

## Narrow correction

Sprint 35A.8:

- removes embedded null characters from captured DISM/SFC text before
  sanitization, storage, and classification
- adds one regression test using the observed SFC output form
- keeps unknown and localized wording `Inconclusive`
- does not change executable paths, arguments, process lifetime, stop
  behaviour, history retention, report contents, or the Windows Repair UI

## Commands remain unchanged

Only these read-only commands remain permitted:

- `DISM.exe /Online /English /Cleanup-Image /CheckHealth`
- `sfc.exe /verifyonly`

No repair command is added.
