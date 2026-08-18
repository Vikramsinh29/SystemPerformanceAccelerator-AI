# PC-SPA Operator and Encoding Safety Rules

These rules apply to all future development, verification, and handover work.

## Prompt and screenshot discipline

- Read the user's full instruction before taking any tool action.
- When screenshots are supplied for inspection, inspect the marked area and surrounding UI first.
- Do not generate, edit, or transform images unless the user explicitly asks for image creation or image editing.
- Do not substitute a visually similar mock-up for inspection of the user's actual screenshot.
- If the requested action is code, repository, documentation, or diagnostic work, stay within that scope unless an additional action is clearly required.
- Before any destructive, publishing, deployment, packaging, merge, or production action, verify that the user's instruction actually authorizes it.

## PowerShell execution safety

- Prefer one complete copy-paste-ready PowerShell block per operation.
- Never provide `else`, `elseif`, `catch`, or continuation fragments separately from the opening block they belong to.
- A verification block must fail closed. It must not print a success summary after a build, test, or validation failure.
- Do not mix captured terminal output with commands.

## UTF-8 file safety

Windows PowerShell 5.1 can misread UTF-8 files without a BOM when `Get-Content` is used without an explicit encoding. This can permanently convert characters such as `•`, `—`, `–`, and `←` into mojibake such as `â€¢`, `â€”`, `â€“`, and `â†...` when the file is subsequently rewritten.

For repository source files containing customer-facing text:

- Do not use an implicit `Get-Content` -> `Set-Content` round trip in Windows PowerShell 5.1.
- Prefer .NET APIs with an explicit strict UTF-8 encoding, for example `System.IO.File.ReadAllText` and `System.IO.File.WriteAllText` with `UTF8Encoding`.
- Preserve the original text encoding intentionally.
- Run `git diff --check` after scripted edits.
- Inspect the diff for unexpected non-ASCII substitutions before committing.
- Run `TextEncodingIntegrityTests` and the full Release test suite before accepting a desktop text change.

## Commercial least-privilege rule

- PC-SPA desktop runs as `asInvoker`.
- Only the dedicated privileged helper requests Administrator elevation.
- Windows UAC is requested only when a protected operation is deliberately started.
- The helper must remain allowlisted and must not expose a generic shell, arbitrary executable path, or arbitrary argument execution surface.
