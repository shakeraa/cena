# TASK-PRR-011: Session JWT in httpOnly SameSite=Strict cookie (not localStorage)

**Priority**: P0 — ship-blocker (lens consensus: 1)
**Effort**: S — 1-2 days
**Lens consensus**: persona-redteam
**Source docs**: `AXIS_10_Operational_Integration_Features.md:L116`
**Assignee hint**: kimi-coder
**Tags**: source=pre-release-review-2026-04-20, lens=redteam
**Status**: Not Started
**Source**: Synthesized from 10-persona pre-release review (2026-04-20) — see `/pre-release-review/reviews/SYNTHESIS.md`
**Tier**: mvp

---

## Goal

Close XSS-driven session-theft by moving authentication out of any JS-accessible storage.

### Code-reality correction (2026-04-20 verification)

- **Production**: Firebase Auth (`plugins/firebase.ts`) stores session in IndexedDB — JS-accessible, XSS-drainable
- **Mock path**: `authStore.ts:40` uses localStorage (`MOCK_AUTH_STORAGE_KEY`) for E2E/dev — deployment-config risk if it leaks to prod

Both are real problems. Requires architectural change: BFF session-exchange endpoint (Firebase Auth doesn't natively issue httpOnly cookies).

### User decision 2026-04-20 — tightened DoD, effort S → M

**Scope A — strip mock-auth from production bundles**:
- Vite `define` flag + build-time assertion; mock code path gated by `import.meta.env.DEV || VITE_USE_MOCK_AUTH === 'true'`; env var rejected at config-load in production

**Scope B — BFF httpOnly session cookie**:
- New `POST /api/auth/session` endpoint: accepts Firebase ID token (bearer, this one call only), verifies server-side, sets `cena_session` cookie (HttpOnly + Secure + SameSite=Strict)
- Subsequent API calls use cookie automatically; remove `Authorization: Bearer` from client
- Backend middleware validates cookie; populates user context
- `POST /api/auth/session/logout` → expire cookie + server-side revocation-list entry (TTL matching cookie Max-Age)
- SameSite=Strict handles CSRF for now; document double-submit token as future work

### Hard constraints

- **Arch test**: production bundle contains zero `window.localStorage.setItem` with keys matching `auth|token|jwt|session|bearer` — build fails
- **API arch test**: no endpoint except `/api/auth/session` accepts `Authorization: Bearer`
- **E2E test**: login flow asserts `cena_session` cookie present but unreadable (httpOnly) + `fetch('/api/me')` succeeds without Authorization header

## Files

- `src/api/Cena.Student.Api.Host/Auth/SessionExchangeEndpoint.cs` (new)
- `src/api/Cena.Student.Api.Host/Auth/CookieAuthMiddleware.cs` (new)
- `src/student/full-version/src/stores/authStore.ts` (drop localStorage behind build guard)
- `src/admin/full-version/src/stores/auth*.ts`
- `src/student/full-version/vite.config.ts` + admin equivalent (define flag)
- `tests/arch/NoLocalStorageAuthTest.cs`
- `tests/arch/NoBearerTokenEndpoints.cs`
- `tests/e2e/login-httponly-cookie.spec.ts`
- `docs/runbooks/auth-cookie-rotation.md`

## Definition of Done

1. Production bundles (student + admin) contain zero JS-accessible auth storage; dev bundles retain mock mode
2. Session-exchange endpoint operational end-to-end
3. All non-session endpoints authenticate via cookie middleware
4. Both arch tests green
5. E2E test asserts cookie invisibility
6. Logout clears cookie + server revokes session-JWT
7. **Effort revised S → M** (BFF work not accounted for in original estimate)
8. Full `Cena.Actors.sln` builds; existing tests pass

## Blocks / Coordinate

- **Blocks EPIC-PRR-C** (Parent Aggregate auth — same cookie pattern)
- Coordinate with prr-014 (parent auth role ADR) — same middleware

## Reporting

complete via: node .agentdb/kimi-queue.js complete <id> --worker kimi-coder --result "<branch>"

---

## Non-negotiable references
None

## Implementation Protocol — Senior Architect

Implementation of this task must be driven by a senior-architect mindset, not a checklist. Before writing any code, the implementer (human or agent) must answer both sets of questions in writing — either in a task-comment, the PR description, or a `docs/decisions/` note:

### Ask why
- **Why does this task exist?** Read the source-doc lines cited above and the persona reviews in `/pre-release-review/reviews/persona-*/` that raised it. If you cannot restate the motivation in one sentence, do not start coding.
- **Why this priority?** Read the lens-consensus list. Understand which persona lens raised it and what evidence they cited.
- **Why these files?** Trace the data flow end-to-end. Verify the files listed are the right seams. A bad seam invalidates the whole task.
- **Why are the non-negotiables above relevant?** Show understanding of how each constrains the solution, not just that they exist.

### Ask how
- **How does this interact with existing aggregates and bounded contexts?** Name them.
- **How does it respect tenant isolation (ADR-0001), event sourcing, the CAS oracle (ADR-0002), and session-scoped misconception data (ADR-0003)?**
- **How will it fail?** What's the runbook at 03:00 on a Bagrut exam morning? If you cannot describe the failure mode, the design is incomplete.
- **How will it be verified end-to-end, with real data?** Not mocks. Query the DB, hit the APIs, compare field names and tenant scoping — see user memory "Verify data E2E" and "Labels match data".
- **How does it honor the <500 LOC per file rule, the no-stubs-in-prod rule, and the full `Cena.Actors.sln` build gate?**

### Before committing
- Full `Cena.Actors.sln` must build cleanly (branch-only builds miss cross-project errors — learned 2026-04-13).
- Tests cover golden path **and** edge cases surfaced in the persona reviews.
- No cosmetic patches over root causes. No "Phase 1 stub → Phase 1b real" pattern (banned 2026-04-11).
- No dark-pattern copy (ship-gate scanner must pass).
- If the task as-scoped is wrong in light of what you find, **push back** and propose the correction via a task comment — do not silently expand scope, shrink scope, or ship a stub.

### If blocked
- Fail loudly: `node .agentdb/kimi-queue.js fail <task-id> --worker <you> --reason "<specific blocker, not 'hard'>"`.
- Do not silently reduce scope. Do not skip a non-negotiable. Do not bypass a hook with `--no-verify`.

### Definition of done is higher than the checklist above
- Labels match data (UI label = API key = DB column intent).
- Root cause fixed, not masked.
- Observability added (metrics, structured logs with tenant/session IDs, runbook entry).
- Related personas' cross-lens handoffs addressed or explicitly deferred with a new task ID.

**Reference**: full protocol and its rationale live in [`/tasks/pre-release-review/README.md`](../../tasks/pre-release-review/README.md#implementation-protocol-senior-architect) (this section is duplicated there for skimming convenience).

---

## Related
- [Full synthesis](../../pre-release-review/reviews/SYNTHESIS.md)
- [Retired proposals](../../pre-release-review/reviews/retired.md)
- [Conflicts needing decision](../../pre-release-review/reviews/conflicts.md)
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (id: prr-011)
