# PC-SPA Operator and Encoding Safety Rules

These rules apply to all future development, verification, and handover work.

## CRITICAL prompt and screenshot discipline

### Absolute image-generation prohibition unless explicitly requested

- **DO NOT invoke any image-generation, image-editing, image-redesign, image-rendering, or image-transformation tool unless the user's CURRENT prompt explicitly asks to create, generate, render, redesign, or edit an image.**
- The presence of one or more screenshots does **not** authorize image generation or image editing.
- Screenshots are **inspection/reference evidence by default**.
- Requests such as **check, inspect, audit, review, compare, correct, fix, proceed, do the needful, make the UI uniform, adjust the layout, adjust colours, or repair the application** are requests to inspect the supplied evidence and work on the actual project/source. They are **not** image-generation requests.
- When the user asks for a correction to the application shown in a screenshot, modify or diagnose the actual application/source code. **Do not create a mock-up, preview image, replacement screenshot, infographic, or redesigned image unless the user explicitly requests one.**
- Before any image-tool invocation, perform this mandatory check: **Does the user's current prompt explicitly request creation or editing of an image?** If the answer is not an unambiguous **YES**, image-tool invocation is prohibited.
- Previous image-generation requests do not carry forward to later prompts. Authorization must exist in the current prompt.
- If the current prompt contains screenshots plus code/repository instructions, the code/repository instructions control unless image creation is explicitly requested.

### General prompt handling

- Read the user's **entire current prompt carefully before taking any tool action**.
- Identify the requested artifact/action first; do not infer a different task merely because an attachment is present.
- When screenshots are supplied for inspection, inspect the marked area and surrounding UI first.
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
