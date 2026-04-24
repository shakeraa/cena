# ADR-0041 — Parent auth role, age bands, and multi-institute parent visibility

- **Status**: Accepted
- **Date**: 2026-04-20
- **Decision Makers**: Shaker (project owner)
- **Task**: prr-014 (EPIC-PRR-C)
- **Related**: [ADR-0001](0001-multi-institute-enrollment.md), [ADR-0003](0003-misconception-session-scope.md), [ADR-0038](0038-event-sourced-right-to-be-forgotten.md), prr-009 (IDOR helper), prr-011 (session cookie), prr-155 (ConsentAggregate)

---

## Context

Cena has no Parent authentication role today. Parental involvement is currently approximated at runtime by one collapsed boolean — `MeGdprEndpoints.cs:529` checks `IsMinor<16` and gates behaviour on a single under-16 threshold. That is wrong in three directions at once:

- **GDPR-K / Art. 8** requires parental consent below age 16 by default (member states may lower to 13). The platform exposes to students in Israel (typical threshold 14 in Israeli interpretation) and to any DSGVO-governed users, so a single "under 16" bucket does not model the consent story correctly.
- **COPPA** requires **Verifiable Parental Consent (VPC)** for under-13 users. VPC is a specific category of consent with stricter evidential requirements than generic parental consent; it cannot be lumped with 13-15.
- **Israeli Privacy Protection Authority (PPA)** issued a February 2025 opinion emphasising **minor dignity at 16 and above** — a 17-year-old's privacy interest against blanket parental visibility is legally recognised. Treating a 17-year-old the same as an 11-year-old is the opposite of what PPA expects.

Additionally, parental visibility today has no multi-institute boundary. If a parent is authorised to view their child's data at institute A, the architecture does not actively prevent that parent's credentials from reaching data at institute B where they have no grant — it relies on nothing more than the absence of an endpoint. That is fragile; a single IDOR bug would bridge the tenant boundary.

## Decision

### Four age bands, not one

We replace the single `IsMinor<16` check with a four-band model:

| Band | Age range |
|---|---|
| `Under13` | 0–12 |
| `Teen13to15` | 13–15 |
| `Teen16to17` | 16–17 |
| `Adult` | 18+ |

The band is computed from the student's date of birth at request time. Birthday transitions re-band the student automatically on their next session (no manual migration step).

### Parent role authenticated via the same session-cookie pattern as students

Parents authenticate through the same session-cookie flow established in prr-011 for students. Their identity token carries a distinct claim, `parent_of = [<studentSubjectId>, ...]`, listing every student the parent is authorised to represent. That claim is the only authorisation substrate for parent-facing endpoints; expiry and renewal follow the same rules as student sessions.

The `parent_of` array is populated from the consent aggregate (prr-155) on login and re-derived on every session refresh, so that a revoked parental link takes effect at most one session cycle after revocation.

### Age-band authorisation matrix

| Age band | Student can withdraw consent? | Parent must grant consent? | Student sees what parent sees? |
|---|---|---|---|
| `Under13` | No | Yes, for all purposes (VPC-compliant) | Parent access is VPC-compliant; student is shown age-appropriate summaries |
| `Teen13to15` | For some purposes (limited) | Yes, for durable data (mastery, long-term profile) | Student is notified when parent views protected categories |
| `Teen16to17` | Yes, for all purposes except legally-required reporting | Optional; governed by MoE-defined defaults | Student can withhold specific categories from parent view (PPA minor-dignity) |
| `Adult` | Full control | N/A | N/A — parent access requires fresh adult consent |

Cells meaning:

- **Student can withdraw consent** — whether the student themselves can unilaterally revoke a consent that was originally granted (by parent or by the student). `Under13` cannot; the law reserves that power for the parent. `Teen16to17` can for most categories; this is the PPA minor-dignity decision.
- **Parent must grant consent** — whether parental consent is legally required for the platform to process the student's data at all. `Under13` → yes (COPPA VPC). `Adult` → no.
- **Student sees what parent sees** — transparency: whether the student is notified or has a say in what the parent can read. `Teen13to15` → notify-student-on-parent-view. `Teen16to17` → student can *withhold* categories (stronger right).

### Multi-institute parent visibility

Parent authorisation on institute A does **not** implicitly extend to institute B. A parent who has been granted visibility for their child's bagrut enrolment at school A has no parent-session visibility for that same child's SAT enrolment at private tutor B unless an explicit grant has been recorded for institute B.

This matches ADR-0001's multi-institute separation and is the defensible answer to the commercial-competitor question: institute A and institute B may be competitors, and institute A's parent grant is not institute A's to give away on institute B's behalf. A parent who wants cross-institute visibility has to be granted it at each institute separately.

Mechanically: `parent_of` claims are per-`(studentSubjectId, instituteId)` pairs, not per-`studentSubjectId` alone. A single parent with three children at two institutes carries up to six entries in `parent_of`.

### IDOR enforcement helper

All parent-facing endpoints route authorisation through a single seam:

```
ParentAuthorizationGuard.AssertCanAccess(
    parentActorId,
    studentSubjectId,
    institute
)
```

This helper is the only path by which a parent's access is validated. It reads the `parent_of` claim, confirms that `(studentSubjectId, institute)` is present, confirms the grant has not been revoked in the consent aggregate, and throws `UnauthorizedAccessException` otherwise. No endpoint is allowed to short-circuit around the guard — the prr-009 IDOR-sweep architecture test enforces that any endpoint decorated as parent-facing calls `ParentAuthorizationGuard` exactly once before touching student data.

The guard is the single controlled seam; if an IDOR vulnerability ever surfaces, it surfaces in exactly one file.

### Consent events

Consent grants, revocations, and age-band transitions all emit events through the new `ConsentAggregate` (prr-155). Specifically:

- `ParentalConsentGranted_V1 (parentSubjectId, studentSubjectId, institute, purposes[], grantedAt, evidence)` — evidence field records the VPC mechanism used, if applicable.
- `ParentalConsentWithdrawn_V1 (parentSubjectId, studentSubjectId, institute, purposes[], withdrawnAt, initiator)` — initiator is `parent` or `student` (for Teen16to17 self-withdraw).
- `StudentAgeBandChanged_V1 (studentSubjectId, fromBand, toBand, effectiveAt)` — emitted when a birthday rolls the student into a new band; consumers react by re-evaluating cached authorisations.

All three events are PII-classified under [ADR-0038](0038-event-sourced-right-to-be-forgotten.md) and crypto-shredded on subject erasure.

## Consequences

### Positive

- Correct modelling of the three distinct legal regimes (COPPA VPC under 13, GDPR-K 13–15 default, PPA minor-dignity 16+) replaces the single under-16 bucket.
- A parent role that actually exists in the type system closes a class of authorisation bugs that previously could not be expressed at all.
- Multi-institute parent separation matches ADR-0001's tenant boundary and defends against cross-tenant IDOR.
- One controlled seam (`ParentAuthorizationGuard.AssertCanAccess`) is the entire parent-authz surface, which makes audit and fix work tractable.
- Age-band transitions are event-sourced via `StudentAgeBandChanged_V1`, so "what rights did this student have on this date" is a replayable query, not a derived assumption.

### Negative

- Birthday-transition logic (`Teen13to15 → Teen16to17` on the day a student turns 16) is an edge case that must be tested — a student may be mid-session when their band flips. The implementation treats the band as fixed for the duration of a session and re-evaluates on session refresh, which is defensible but needs a test case.
- Four bands and two consent layers (parent-granted vs student-granted) is more complex than one boolean. Runbooks and admin UI both need to represent the new model.
- Multi-institute parental visibility means a parent who expected "log in once, see all my kid's work" discovers they need per-institute grants. That is the legally correct behaviour but it is a UX surprise that product docs need to pre-empt.

### Neutral

- The consent aggregate (prr-155) was scheduled to land regardless of this ADR; this ADR just pins down what shape its events take for parental cases.

## References

- [ADR-0001](0001-multi-institute-enrollment.md) — multi-institute tenant boundary; parent visibility inherits the same per-institute separation.
- [ADR-0003](0003-misconception-session-scope.md) — student-scoped misconception data is *never* visible to a parent regardless of grant, because it is session-scoped and 30-day-bounded.
- [ADR-0038](0038-event-sourced-right-to-be-forgotten.md) — consent events and parent-authorisation events are PII-classified and crypto-shredded.
- GDPR Art. 8 (child-age consent threshold).
- Israeli Privacy Protection Law (PPL) — general consent framework.
- Israeli Privacy Protection Authority, February 2025 opinion on minor dignity.
- prr-009 — IDOR-enforcement helper sweep.
- prr-011 — session cookie authentication for students; reused here for parents.
- prr-155 — `ConsentAggregate` event design.
- `docs/tasks/pre-release-review/TASK-PRR-014.md` — task body.
