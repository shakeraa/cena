# EPIC-E2E-I — GDPR / COPPA / Ministry compliance

**Status**: Proposed
**Priority**: P0 (legal risk surface — failures here are reportable incidents)
**Related ADRs**: [ADR-0003](../../docs/adr/0003-misconception-session-scope.md), [ADR-0038](../../docs/adr/0038-event-sourced-right-to-be-forgotten.md), [ADR-0042](../../docs/adr/0042-consent-aggregate-bounded-context.md), [ADR-0047](../../docs/adr/0047-no-pii-in-llm-prompts.md)

---

## Why this exists

Compliance invariants are *negative* properties — "this field never appears", "this audit row always exists", "this retention never exceeds 30 days". Negative properties rot silently; only an adversarial test catches them.

## Workflows

### E2E-I-01 — Misconception retention ≤ 30 days (ADR-0003)

**Journey**: student session emits misconception events → 30 days pass (test uses IClock seam to fast-forward) → cleanup job runs → misconception rows pruned from the store → admin DB scan confirms zero rows > 30 days.

**Boundaries**: DB scan query (`SELECT count(*) FROM misconception_store WHERE created_at < NOW() - INTERVAL '30 days'` returns 0), bus (`MisconceptionsPrunedV1`).

**Regression caught**: retention extended silently; cleanup job stopped running; misconception leaked to StudentProfile (ADR-0003 ship blocker).

### E2E-I-02 — Misconception never attached to student profile (ADR-0003)

**Journey**: student triggers a misconception via a wrong answer → StudentProfile document fetched → profile contains NO misconception fields. Event-stream scan for `MisconceptionEventV1` with `studentId` populated → zero rows.

**Boundaries**: DB assertion on StudentProfile shape + event-stream scan.

**Regression caught**: misconception data leaks to profile (ship blocker); event sourced with studentId (should be session_id only).

### E2E-I-03 — Right-to-erasure cascade (ADR-0038)

**Journey**: see EPIC-E2E-E-08 (parent-initiated) and EPIC-E2E-G-08 (admin-initiated). This epic asserts the **crypto-shred invariant**: after erasure, the encrypted columns for personal data are un-decryptable (pepper key rotated out).

**Boundaries**: DB scan — every personal column post-erasure contains ciphertext that decrypts to nothing (wrong key); manifest lists every cascade target; aggregates preserved as tombstones (not deleted) for replay-ability.

**Regression caught**: personal data left in plaintext; cascade missed a projection; aggregates hard-deleted (can't replay).

### E2E-I-04 — Consent audit export completeness (prr-130)

**Journey**: over a 6-month test window → student undergoes ~12 consent flips (grant / revoke different scopes) → admin CSV export → CSV row count = 12; every row has timestamp + actor + scope + new-state.

**Boundaries**: CSV column structure, row count, event order preserved (newest last), tenant filter honored.

**Regression caught**: rows missing from export; order scrambled; scope labels inconsistent.

### E2E-I-05 — PII never in LLM prompts (ADR-0047)

**Journey**: student input with various PII patterns (email, phone, address, Israeli ID, UK postcode) → observation: captured LLM payload contains zero PII.

**Boundaries**: test-mode LLM recorder captures payloads, regex scan per PII pattern, DB audit shows `scrubbed=true`.

**Regression caught**: new PII pattern introduced (e.g., passport numbers) not scrubbed; regex regression drops a case; scrubber bypassed on a new code path.

### E2E-I-06 — Age-band field filter consistency (prr-052)

**Journey**: child ages from 12 → 13 → 14 (test clock advances) → dashboard field set shrinks at each threshold → prior-year exports don't leak fields through.

**Boundaries**: DOM field set at each age assertion, historical snapshot export respects the **current** age-band filter (not the snapshot's original filter).

**Regression caught**: field hidden on current dashboard but visible in historical export; band transition not triggered until next session.

### E2E-I-07 — Ministry-reference enforcement (ADR-0043)

**Journey**: admin tries to mark a raw Ministry-reference question as student-facing → backend rejects (ADR-0043 enforcement); parametric-recreated version from that reference IS shippable.

**Boundaries**: DB constraint / service-layer refusal, admin UI error message accurately reflects policy.

**Regression caught**: Ministry raw text slips to students (ship blocker + Ministry-level compliance breach); recreation pipeline bypassed.

### E2E-I-08 — Observability-consent gate on Sentry events (FIND-privacy-016)

**Journey**: student without consent → Sentry plugin stays in no-op mode → no event reaches Sentry backend; student flips consent on → Sentry plugin initializes → events flow.

**Boundaries**: observed outbound HTTP calls (captured by test MITM) — zero to `sentry.io` when consent off; non-zero after consent on; user.id is always `id_hash` (never raw).

**Regression caught**: consent bypassed; raw user id leaks; session-replay enabled (banned per ADR-0058 §2).

## Out of scope

- Cookie-banner consent (covered by EPIC-E2E-E-04 parent + student consent flows)
- Server-log retention policies — belongs in ops, not E2E

## Definition of Done

- [ ] All 8 workflows green
- [ ] I-01, I-02, I-03, I-05, I-07, I-08 tagged `@compliance @p0` — blocks merge
- [ ] Negative-property specs (I-01, I-02) run a DB scan with an assertion of `count === 0`, not just "spot-check"
- [ ] LLM-payload recorder (I-05) is the same harness used by EPIC-E2E-D-05 — don't duplicate
