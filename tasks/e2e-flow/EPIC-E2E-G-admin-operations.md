# EPIC-E2E-G — Admin operations (content, moderation, ops)

**Status**: Proposed
**Priority**: P1 (ops productivity + content-quality gate)
**Related ADRs**: [ADR-0002](../../docs/adr/0002-sympy-correctness-oracle.md), [ADR-0043](../../docs/adr/0043-bagrut-reference-only-enforcement.md), [RDY-034](../readiness/done/RDY-034-cas-oracle.md)

---

## Why this exists

Admin flows ship content to students. A regression here blows past CAS + quality + ship-gate in one go and lands wrong-math in front of a Ministry-level user. Every write path through the admin console MUST traverse CasGatedQuestionPersister (RDY-037).

## Workflows

### E2E-G-01 — Bagrut PDF ingestion (RDY-057)

**Journey**: admin uploads Bagrut PDF → OCR cascade runs → CAS gate verifies extracted items → reference-only bucket populated (never student-facing raw per ADR-0043) → admin reviews → approves → items available as *reference* for parametric recreation (ADR-0043).

**Boundaries**: DOM (upload progress, OCR preview, approval flow), storage (S3 PDF upload), DB (IngestionPipeline state transitions), bus (`BagrutIngestionCompletedV1`), ship-gate compliance check (no raw Ministry text ever marked "shippable").

**Regression caught**: raw Ministry text reaches student-facing path (ADR-0043 ship blocker); OCR silently falls back to garbage; PDF upload credentials leak.

### E2E-G-02 — Parametric template authoring (prr-202)

**Journey**: admin authors deterministic parametric template → live preview renders → CAS gate verifies → admin submits → batch generation runs → new questions persisted via CasGatedQuestionPersister.

**Boundaries**: DOM (authoring UI, preview, CAS-verified badge), DB (ParametricTemplate event stream), CAS round-trip (SymPy sidecar), no-LLM architecture test enforces Strategy 1 purity.

**Regression caught**: LLM slipping into parametric pipeline (architecture-test regression); unverified templates reach students; batch generation writes to wrong tenant.

### E2E-G-03 — Reference-calibrated recreation (RDY-019b / ADR-0043)

**Journey**: admin triggers POST `/api/admin/content/recreate-from-reference` → dry-run lists candidate AI-authored recreations → admin approves → batch runs through BatchGenerateAsync → each candidate CAS-gated → persisted.

**Boundaries**: admin audit (SUPER_ADMIN only), dry-run output, DB new QuestionDocument rows all with `source=recreated-from-reference`, event stream.

**Regression caught**: raw reference string leaks into generated question body; ADMIN (non-super) can trigger wet run; candidates bypass CAS gate.

### E2E-G-04 — CAS override (RDY-036 / RDY-045)

**Journey**: SUPER_ADMIN sees a CAS-verified-failed question → uses override endpoint with justification → override logged to SIEM + Slack security notifier → question becomes shippable with override flag.

**Boundaries**: DOM (override form with justification), DB (CasOverrideEventV1 sourced, cannot be mutated), SIEM audit, SMS+Slack security-notifier webhook.

**Regression caught**: ADMIN (non-super) uses override → ship blocker; override without justification accepted; security-notifier silently failing.

### E2E-G-05 — Question moderation queue

**Journey**: report from student "this question is wrong" → admin moderation queue shows report + question → admin reviews → marks "invalid" → question pulled from active pool.

**Boundaries**: DOM (queue listing + review UI), DB (QuestionModerationEvent), bus (`QuestionInvalidatedV1`), student-side pool excludes the question going forward.

**Regression caught**: invalid question still served; moderation action not broadcast to running sessions (students continue to see it mid-session).

### E2E-G-06 — Cultural-context review board DLQ (prr-034)

**Journey**: LLM content flagged for cultural-context review (e.g., localized example might offend) → goes to DLQ → admin reviews → fixes or rejects → outcome event-sourced.

**Boundaries**: DB CulturalContextReview row, admin UI queue, NATS DLQ topic subscriber count, action audit.

**Regression caught**: flagged content leaks past DLQ to students; admin action not idempotent; queue grows unbounded.

### E2E-G-07 — LLM cost dashboard (prr-112)

**Journey**: admin opens `/apps/system/llm-cost` → per-feature / per-cohort cost breakdown shown → filters by time range → CSV export.

**Boundaries**: DOM (chart renders, CSV downloads), DB (LlmCostMetric rollup), tenant scoping (cross-institute admin sees all, institute admin sees own).

**Regression caught**: cost leak across tenants; CSV missing rows; rollup service falls back to Null and shows zeros.

### E2E-G-08 — GDPR erasure admin trigger (FIND-arch-006)

**Journey**: admin receives a DSR (data subject request) from a parent → opens `/apps/gdpr/erasure` → enters student id → confirms → RightToErasureService runs (same cascade as parent-initiated, EPIC-E2E-E-08) → manifest downloadable.

**Boundaries**: DOM (action + manifest download), full cascade runs (same assertion as E-08), audit log records admin operator id.

**Regression caught**: admin triggers erasure without signed confirmation; manifest missing cascade step; cross-tenant admin triggers on wrong student.

### E2E-G-09 — Live session monitor SSE (ADM-026)

**Journey**: admin opens `/apps/system/live-monitor` → SSE stream shows active sessions across institutes → admin can drill into a session (read-only) → stream reconnects on backend restart.

**Boundaries**: DOM (list updates live), SSE reconnection behavior, DB read projections, no PII in the stream beyond what admin role permits.

**Regression caught**: stream frozen silently; reconnection fails; PII leak beyond admin entitlement.

## Out of scope

- Consent-audit export — covered by EPIC-E2E-E-04
- Rate-limit admin dashboard — surfaced in EPIC-E2E-J (resilience)

## Definition of Done

- [ ] 9 workflows green
- [ ] G-01, G-02, G-04 tagged `@content @ship-gate` — blocks merge
- [ ] G-08 (erasure) must match EPIC-E2E-E-08 behavior — two paths, one cascade
- [ ] Tests run SUPER_ADMIN and ADMIN roles separately; RBAC boundary asserted
