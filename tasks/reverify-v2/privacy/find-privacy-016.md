---
id: FIND-PRIVACY-016
task_id: t_1245132fe3e4
severity: P1 — High
lens: privacy
tags: [reverify, privacy, GDPR, ICO-Children, processor, sentry]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-privacy-016: Sentry stub pre-wires student email to SaaS — gate before STU-W-OBS-SENTRY unblocks

## Summary

Sentry stub pre-wires student email to SaaS — gate before STU-W-OBS-SENTRY unblocks

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

framework: ICO-Children (Std 9 connected services), GDPR (Art 28 processor agreements)
severity: P1 (high)
lens: privacy
related_prior_finding: none

## Goal

Lock down the Sentry integration BEFORE the STU-W-OBS-SENTRY follow-up
unblocks it. Today `src/student/full-version/src/plugins/sentry.ts` is a
no-op stub, but the shim API includes `setUser({id, email})` — the wiring
is ready to send student id + email to a Sentry SaaS endpoint the moment
the DSN is provisioned.

## Background

`src/student/full-version/src/plugins/sentry.ts:13-40`:

```typescript
interface SentryShim {
  captureException: (err: unknown, context?: Record<string, unknown>) => void
  addBreadcrumb: (breadcrumb: Record<string, unknown>) => void
  setTag: (key: string, value: string) => void
  setUser: (user: { id: string; email?: string } | null) => void  // ⚠ ships email
}

export default function (__: App) {
  const dsn = (import.meta as any).env?.VITE_SENTRY_DSN
  if (dsn) {
    // Placeholder — real Sentry init lives in STU-W-OBS-SENTRY follow-up.
    void dsn
  }
}
```

There is no Sentry DPA, no privacy-policy disclosure, no consent gate, no
PII scrubbing. The follow-up task as written is a privacy time-bomb because
the integration contract pre-commits to sending identifiable PII.

## Files

- `src/student/full-version/src/plugins/sentry.ts` (rewrite shim API to
  remove email)
- `src/student/full-version/src/plugins/sentry.config.ts` (NEW — when the
  real init lands, use this config; bake in the safe defaults)
- `docs/legal/processor-agreements/sentry-dpa.md` (NEW placeholder until
  real DPA is signed)
- `docs/legal/privacy-policy.md` (add Sentry processor disclosure section
  — depends on FIND-privacy-002)

## Definition of Done

1. SentryShim API surface no longer accepts `email`. setUser only takes
   `{id_hash}` — and the caller hashes the student id with a per-tenant
   pepper before passing it.
2. The Sentry config (when initialized) uses these locked-down defaults:
   - `defaultPii: false`
   - `replaysSessionSampleRate: 0` (session replay disabled entirely for
     a child-serving product)
   - `replaysOnErrorSampleRate: 0`
   - `beforeSend` hook scrubs:
     - any `user.email`, `user.username`, `user.ip_address`
     - any localStorage contents
     - any URL query string parameters
     - any header values except trace-id
   - `tracePropagationTargets` set to localhost + Cena API only (do NOT
     forward Sentry trace headers to Anthropic, Firebase, Google Fonts)
3. Sentry initialization gated on `consent.observability = true` (depends
   on FIND-privacy-007). Without consent, the shim stays a no-op.
4. Privacy policy section discloses Sentry as a sub-processor with purpose,
   data categories, country, DPA reference (FIND-privacy-002).
5. Pact test against a mock Sentry that asserts no email, no full name, no
   IP address, no localStorage contents are forwarded.
6. The follow-up task STU-W-OBS-SENTRY has a checklist gate added: it
   cannot be marked done until this finding's DoD is met.

## Reporting requirements

Branch: `<worker>/<task-id>-privacy-016-sentry-lockdown`. Result must
include:

- the locked-down config source
- the Pact test result against a mock Sentry
- a screenshot of the gated initialization (consent.observability=false →
  no init)

## Out of scope

- The actual real-Sentry initialization (still STU-W-OBS-SENTRY)
- Admin Sentry integration (admin app does not currently include Sentry)


## Evidence & context

- Lens report: `docs/reviews/agent-privacy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_1245132fe3e4`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
