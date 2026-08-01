# PC-SPA Diagnostic Data Dictionary

## Diagnostic event

| Field | Purpose | Privacy treatment |
|---|---|---|
| `ReferenceId` | Short support reference such as `ERR-...` | Random and non-identifying |
| `InstallationId` | Groups local records from one installation | Random GUID; no hardware fingerprint |
| `TimestampUtc` | Orders events and supports retention | UTC timestamp |
| `Severity` | Information, Warning, Error, or Fatal | No personal data |
| `Feature` | Area involved in the failure | Sanitized |
| `OperationStage` | Stage where the failure surfaced | Sanitized |
| `ExceptionType` | .NET exception type | No message content |
| `Message` | Sanitized exception message | Paths and email-like values redacted |
| `StackTrace` | Sanitized technical trace | Personal and unknown quoted paths redacted |
| `Recovered` | Whether the application continued safely | Boolean |
| `UserDataMayHaveBeenAffected` | Explicit risk indicator | Boolean; conservative use only |
| `Environment` | Version and runtime context | Data-minimized summary |

## Environment summary

| Field | Included locally | Included in export |
|---|---:|---:|
| Application version | Yes | Yes |
| Build identifier | Yes | Yes |
| Windows version | Yes | Yes |
| .NET runtime version | Yes | Yes |
| Elevation state | Yes | Yes |
| Available memory | Yes | Yes |
| System-drive free space | Yes | Yes |
| CPU model | Yes | Only with user-selected hardware-summary option |
| Installed physical memory | Yes | Only with user-selected hardware-summary option |

## Installation identity

`installation.json` contains only:

- a random 32-character GUID representation
- creation timestamp

It does not contain a user name, email address, licence key, disk serial number, motherboard identifier, MAC address, or Windows product identifier.

## Export package

A manual ZIP can contain:

```text
README.txt
manifest.json
environment.json
events/<reference>.json
```

Malformed local event files are skipped rather than copied.

## Excluded information

The implementation does not intentionally store or export:

- document contents or file samples
- browser history or cookies
- password or credential data
- email addresses
- full personal paths
- unrelated process lists or command lines
- machine serial numbers
- network identifiers
- cloud account data
- payment or licensing data
