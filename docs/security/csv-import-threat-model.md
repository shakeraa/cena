# CSV Roster Import — STRIDE Threat Model (prr-021)

**Status**: authoritative for the hardened `POST /api/admin/users/bulk-invite`
endpoint implemented in this task.
**Source personas**: `persona-redteam`, `persona-a11y`, `persona-ethics`
(pre-release review 2026-04-20).
**Related task body**: `tasks/pre-release-review/TASK-PRR-021-harden-csv-bulk-roster-import-size-injection-utf-8-normaliza.md`.

## Why this feature exists

Schools and pilot institutes provision 50 – 5000 students in one sitting,
typically right before a semester. The admin console accepts a CSV exported
from SIS/Excel/Sheets and turns each row into an invited user. The endpoint
is a classic "trusted admin uploads untrusted file" boundary: the admin is
authenticated, but the CSV contents are third-party data. Everything the CSV
touches downstream (email delivery, spreadsheet re-export, Firebase user
creation, Marten documents) is attack surface.

## Data flow (one sentence per hop)

1. Admin browser opens `multipart/form-data` PUT to `/api/admin/users/bulk-invite`.
2. ASP.NET form-binding materializes the file (size-capped in endpoint).
3. `AdminUserService.BulkInviteAsync` reads the stream.
4. `CsvRosterSanitizer.Parse` applies size / injection / UTF-8 / bidi /
   normalization defenses and returns a typed row list plus a rejection
   summary.
5. Each surviving row calls `InviteUserAsync`, which enforces tenant binding
   and delegates to Firebase.
6. An audit log row (`AuditEventDocument`) is persisted with
   `{institute_id, admin_user_id, row_count, byte_count, rejections_by_kind,
   trace_id}`.

## STRIDE per hop

### 2. Form-binding hop (multipart → IFormFile)

| Threat | Mitigation |
|---|---|
| **Tampering** — attacker ships a 1 GB CSV to DoS the process. | Endpoint checks `file.Length` against `RosterImportOptions.MaxBytes` (default 10 MiB, per-tenant override). |
| **DoS** — zip-bomb style hyper-compressible payload. | Reject content types other than `text/csv` / `application/vnd.ms-excel`; no decompression is performed. |
| **Spoofing** — filename traversal `../../etc/passwd`. | `InputSanitizer.SanitizeFileName` strips `..`, `/`, `\`. |
| **EoP** — an unauthenticated caller. | `CenaAuthPolicies.AdminOnly` policy required before the handler runs. |

### 3. Stream read hop

| Threat | Mitigation |
|---|---|
| **Tampering** — file grows mid-stream past the cap. | We re-count bytes in-flight and short-circuit at `MaxBytes`; form-limit is the outer bound but we also enforce an explicit sanitizer byte budget. |
| **DoS** — millions of rows with 1-byte lines. | `RosterImportOptions.MaxRows` (default 5000, per-tenant override) hard-caps row count; excess rejected. |

### 4. Sanitizer hop (the one we are writing)

| STRIDE class | Attack | Mitigation |
|---|---|---|
| **Tampering** | CSV formula injection: `=cmd\|' /c calc'!A1`, `+HYPERLINK(...)`, `-2+3`, `@SUM(...)`, tab/CR prefix to trigger lookup. | `CsvRosterSanitizer` strips leading `=`, `+`, `-`, `@`, `\t`, `\r` from every cell after quote-unwrapping. Re-adds a single leading apostrophe is **not** used (that only helps Excel viewers; our guarantee is that the cell, when re-exported verbatim, is safe); stripping is the bright-line rule. |
| **Tampering** | Null bytes `\0` inside cells. | All control characters (except `\n`, `\r`, `\t`) dropped via the same pass that removes bidi. |
| **Spoofing** | Homoglyph identity swap: `аdmin@` (Cyrillic а) vs `admin@`. | Unicode **NFC** normalization applied to every cell; then cells are scanned for **NFKC-sensitive drift** — if NFKC(cell) ≠ NFC(cell), we reject the row with kind `homoglyph_suspect` rather than silently accepting the deceptive form. |
| **Information disclosure** | Right-to-left override `U+202E` embedded in a student name so a CSV export to teacher looks like `CharlieEvil.exe` vs `Charliexe.eviL`. | Strip bidi control chars `U+202A..U+202E`, `U+2066..U+2069`, `U+061C` from every cell. |
| **Repudiation** | Attacker argues the row they uploaded was different from what landed in the DB. | Every rejection is counted in `rejections_by_kind`, the full byte count is recorded, and the admin user's id + tenant id + trace id is audited. |
| **DoS** | BOM-less file in GBK / Latin-1 — causes mojibake across the roster. | We require either UTF-8 BOM, or a successful strict UTF-8 decode. Illegal byte sequences reject the whole file before any row write. |
| **EoP** | Inject `__proto__`-style JSON keys via a header cell. | The sanitizer operates on values, but the CSV parser treats the first line as header; header names are whitelisted against the expected `name,email,role` schema. Unknown headers ⇒ reject file. |

### 5. Invite hop

| Threat | Mitigation |
|---|---|
| **EoP** — admin from `school-A` provisions a user into `school-B`. | `InviteUserAsync` calls `TenantScope.GetSchoolFilter` and refuses a `targetSchool` mismatch. Bulk path forces `targetSchool = callerSchoolId` (SUPER_ADMIN only can span schools). |
| **DoS** — attacker runs the import 1000 times/hour to burn Firebase quota. | Dedicated rate-limit policy `admin-roster-import`: 5 imports/hour/institute, partitioned by `school_id`. |
| **Information disclosure** — a failure for one email leaks whether the email already exists on another tenant. | `BulkInviteFailure.Error` is normalized to generic `"invite_failed"` for cross-tenant email collisions; the precise reason is logged but not returned. |

### 6. Audit hop

| Threat | Mitigation |
|---|---|
| **Repudiation** | Dedicated `audit:admin-action:roster-import:*` document recording `{tenant_id, admin_user_id, row_count, byte_count, rejections_by_kind, trace_id}`. Failures to write audit do NOT fail the request (non-blocking), but they log error at `LogError` so ops can alert. |

## Failure mode at 03:00 on a Bagrut exam morning

- Upload fails with `file_too_large` or `too_many_rows` ⇒ admin sees the
  precise cap and the configured override path. No retries burn Firebase
  quota.
- Upload fails with `malformed_utf8` or `homoglyph_suspect` ⇒ admin sees a
  per-row rejection summary; nothing has been written to Firebase or Marten
  so the retry is idempotent.
- Audit-persistence fails (Marten down) ⇒ the import itself still succeeds
  and is emitted to the structured log stream (Serilog → Loki) so the forensic
  trail is preserved out-of-band.
- Rate-limit hits ⇒ 429 with `Retry-After: 3600`; admin can verify via the
  /stats endpoint whether earlier runs actually completed.

## Non-goals for prr-021

- We do NOT attempt to detect semantic homograph attacks across scripts
  (e.g. Greek `ο` vs Latin `o`). NFKC drift detection is the bright line; a
  higher-fidelity IDNA / Unicode-security check is deferred.
- We do NOT support CSV dialect autodetection. Comma separator, `\n` or
  `\r\n` line endings, double-quote escaping only.
- We do NOT support Excel `.xlsx`. The endpoint already rejects non-CSV
  content types; a future task can add a proper XLSX reader.

## Verification

- Unit tests cover every STRIDE cell above under
  `src/shared/Cena.Infrastructure.Tests/Security/CsvRosterSanitizerTests.cs`.
- Integration tests hit the real endpoint via `TestApiFixture` under
  `src/api/Cena.Admin.Api.Tests/BulkRosterImportHardeningTests.cs`.
- Full `Cena.Actors.sln` build + `dotnet test` on both projects before
  merging.
