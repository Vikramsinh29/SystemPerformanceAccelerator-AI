# PC-SPA Diagnostic Privacy Review Checklist

Complete this checklist before every release that changes diagnostic behaviour.

## Collection

- [ ] Local diagnostics remain disabled by default.
- [ ] Enabling diagnostics requires a deliberate Settings change and Save.
- [ ] No remote endpoint, telemetry SDK, or automatic upload was added.
- [ ] Every new field has a documented support purpose.
- [ ] No document contents, browser history, credentials, cookies, licence keys, or machine serial numbers are collected.
- [ ] No unrelated process command lines are collected.

## Sanitization

- [ ] `%USERPROFILE%`, `%LOCALAPPDATA%`, `%APPDATA%`, `%TEMP%`, `%WINDIR%`, `%PROGRAMFILES%`, `%PROGRAMDATA%`, and `%APPDIR%` replacements are tested.
- [ ] Email-like values are redacted.
- [ ] Unknown absolute paths are redacted.
- [ ] Sanitization is applied when the event is written.
- [ ] Sanitization is applied again during export.
- [ ] Corrupted diagnostic files are skipped safely.

## Storage and retention

- [ ] Event files use atomic temporary-file replacement.
- [ ] Retention remains bounded by age and count.
- [ ] Delete Diagnostic History removes local event history.
- [ ] Reset Installation ID deletes old history before creating a new ID.
- [ ] Generated diagnostic ZIP files are ignored by Git.
- [ ] Real diagnostic evidence is not committed to the repository.

## Export

- [ ] Export shows a preview before file selection.
- [ ] Export requires explicit confirmation.
- [ ] The user chooses the destination.
- [ ] The package contains a plain-language README.
- [ ] Hardware summary inclusion follows the saved user preference.
- [ ] The user is told to inspect the ZIP before sharing.

## Verification

- [ ] Sanitizer tests pass.
- [ ] Disabled-diagnostics tests pass.
- [ ] Retention tests pass.
- [ ] Corrupted-file tests pass.
- [ ] Export-content tests pass.
- [ ] Manual review confirms no network activity.
