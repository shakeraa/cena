---
id: FIND-PRIVACY-015
task_id: t_ec5f4fe6d4a1
severity: P1 — High
lens: privacy
tags: [reverify, privacy, GDPR, ICO-Children, minimisation]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-privacy-015: Raw client IPs persisted with no truncation, no disclosure

## Summary

Raw client IPs persisted with no truncation, no disclosure

## Severity

**P1 — High**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

framework: GDPR (Art 5(1)(c) data minimisation, Recital 30 IPs as PII), ICO-Children (Std 8)
severity: P1 (high)
lens: privacy
related_prior_finding: none

## Goal

Truncate IPv4 addresses to /24 and IPv6 to /64 BEFORE persistence everywhere
the platform stores client IPs. Today raw IPs are persisted in
StudentRecordAccessLog and in password-reset logs with no minimisation, no
disclosure, no retention enforcement.

## Background

Sources of raw IP capture:

```
src/shared/Cena.Infrastructure/Compliance/StudentDataAuditMiddleware.cs:76:
    IpAddress = context.Connection.RemoteIpAddress?.ToString()
src/api/Cena.Student.Api.Host/Program.cs:197:
    : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
src/api/Cena.Student.Api.Host/Endpoints/AuthEndpoints.cs:102-105:
    var clientIp = ctx.Request.Headers.TryGetValue("X-Forwarded-For", out var fwd) && fwd.Count > 0
        ? fwd[0]
        : ctx.Connection.RemoteIpAddress?.ToString();
```

Under GDPR Recital 30 IPs are personal data; under ICO Std 8 they must be
minimised. For a child-serving product, raw IPs are also a household
re-identification vector that must be justified.

Today there is:
- No truncation
- No documented retention specific to IP fields
- No disclosure in any privacy policy (which doesn't exist anyway,
  FIND-privacy-002)

## Files

- `src/shared/Cena.Infrastructure/Network/IpAddressNormalizer.cs` (NEW —
  truncate IPv4 to /24, IPv6 to /64)
- `src/shared/Cena.Infrastructure/Compliance/StudentDataAuditMiddleware.cs`
  (call normalizer)
- `src/api/Cena.Student.Api.Host/Endpoints/AuthEndpoints.cs` (call normalizer
  before logging)
- `src/api/Cena.Student.Api.Host/Program.cs` (call normalizer where IPs are
  used)
- `tests/.../IpAddressNormalizerTests.cs` (NEW)

## Definition of Done

1. IpAddressNormalizer.Normalize(IPAddress) returns:
   - IPv4 → first 3 octets + ".0" (e.g. 203.0.113.42 → 203.0.113.0)
   - IPv6 → first 4 hextets + "::" (preserves the /64 prefix per RFC 4193)
   - Unknown / null → "unknown"
2. Every persistence point reads the normalized form, never the raw.
3. The pre-normalisation raw IP IS still available within the request scope
   for in-memory abuse detection (rate limiter, fail-fast on a single IP)
   but is never persisted.
4. The privacy policy (FIND-privacy-002) discloses IP processing.
5. Unit test for the normaliser covering IPv4, IPv6, IPv4-mapped IPv6, null,
   "unknown", malformed.
6. Integration test that StudentRecordAccessLog entries created after this
   change never contain a full IPv4 (last octet always "0").
7. (Optional but recommended) Migration: hash any existing IP addresses in
   StudentRecordAccessLog to the truncated form.

## Reporting requirements

Branch: `<worker>/<task-id>-privacy-015-ip-minimization`. Result must
include:

- the normaliser unit test results
- a sample StudentRecordAccessLog row showing the truncated IP
- the migration plan for existing rows

## Out of scope

- IP-based geolocation features (none exist today)
- Tor/proxy detection (out of scope; the truncation does not impede the
  abuse detection use case)


## Evidence & context

- Lens report: `docs/reviews/agent-privacy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_ec5f4fe6d4a1`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
