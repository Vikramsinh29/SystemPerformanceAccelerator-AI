# Windows Repair Assessment Result Interpretation

## Healthy

PC-SPA uses Healthy only when approved English Microsoft output explicitly
states that no component-store corruption or protected-file integrity
violation was found.

## Attention

Attention is used only when recognized Microsoft output explicitly reports a
repairable or detected integrity condition.

## Inconclusive

Inconclusive is used when the command exits successfully but output cannot be
classified confidently. This commonly includes localized or changed Windows
wording.

## Failed

Failed means the command did not start or returned a non-zero exit code. It
does not automatically mean Windows is corrupted.

## Unsupported

Unsupported means environment preflight blocked execution, such as non-Windows
execution, missing elevation, missing Microsoft tools, or insufficient local
evidence space.

## Skipped

Skipped means the Microsoft command was not started, usually because the user
requested Stop after current check or preflight failed.
