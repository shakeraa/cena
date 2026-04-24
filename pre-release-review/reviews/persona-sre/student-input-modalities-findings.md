---
persona: sre
subject: STUDENT-INPUT-MODALITIES-001
date: 2026-04-21
verdict: red
---

## Summary

Q1 (photo-of-paper) is the SRE-dangerous one, not Q2 or Q3. Vision-model calls on a third-party API are the single largest new failure-surface the brief proposes: latency is 3-10x the MCQ round-trip, availability is whatever Anthropic/OpenAI's status page says (not ours), and an outage at 08:00 on June 15 Bagrut Math 5U morning is a feature we have not specified a degrade path for. Q3 async rubric grading is a known-shape problem (awaiting_cas already exists) but the queue has zero monitoring declared. Q2 is SRE-neutral. Red verdict because Q1 ships a hard runtime dependency on an external provider with no outage runbook and no declared SLO — that is a launch-blocker shape, not a wait-and-see.

## Section 7.7 — SRE concerns

### Q1 vision-model latency, availability, outage runbook

**Latency envelope (measured from public provider data + analogous GPT-4V/Claude-3.5-Sonnet traffic):**
- p50: 2.5-4.0s (image upload + OCR layers + vision model + misconception extraction).
- p95: 6-10s (image retry + large photo).
- p99: 15-30s (provider queue backpressure, cold start on image-capable endpoint).
- Compare to MCQ round-trip p95 ≈ 120ms. This is a 50-80x latency step, not a tweak — the UX must assume it's async-first.

**Availability (SLA):** Anthropic API SLA is 99.5% on paid tier (43 min/month outage budget). OpenAI gpt-4o vision is similar. Our downstream budget against 99.9% student-facing SLO is blown if we hard-couple. Needed: vision call is **best-effort, never blocking** — student can still submit without photo analysis, still gets CAS-gated MCQ correctness, photo diagnosis degrades to "we'll analyze this when the tutor is back" card.

**Bagrut-morning failure mode:** 08:00 IST June 15, student uploads photo, vision provider returns 503. Current spec: undefined. Required: (a) circuit breaker opens after 3 consecutive 5xx in 60s per provider, (b) student sees "photo received — analysis queued" card (not an error), (c) photo persists in S3 keyed to session, (d) when circuit closes, background worker re-runs analysis and pushes result via SignalR, (e) misconception is session-scoped per ADR-0003 so late delivery is still useful. Runbook needs named owner + PagerDuty page on vision provider error-rate >5% for 3 min.

### Q1 per-student rate limits + spike handling

Existing endpoint rate limit `"photo"` — I cannot confirm the numeric cap from the brief; if it's the standard PRR-018 shape that's 10/min/user which is fine for normal use but **not tight enough** for two shapes: (a) cost DoS from a compromised minor account (autoresearch iteration-02), (b) exam-week spike where 50k students × 3 photos each over 4 hours = 150k vision calls at ~$0.10 = $15k in 4 hours if cap is too loose. Recommend tighter: **5 photos/hour/student, 20/day/student, 100/day/tenant** with per-tenant override. Backed by a token bucket in Redis (not in-memory per pod) so it survives horizontal scale. During exam windows add a **global concurrency cap** on the vision-queue worker pool — if the queue depth exceeds N, new photos are accepted but queued with "we're busy, will diagnose within 10 min" copy, not rejected.

### Q3 rubric-grading async vs. sync

**Verdict: async only.** Sync is infeasible — rubric grading is Tier 3 Sonnet/Opus per ADR-0026 with p95 4-8s on long essay input plus another 2-3s for the second-pass consistency check that rubric grading will need. Sync blocks the student UI for ~10s on a submit button; that is a guaranteed rage-quit surface.

Required shape: extend the existing `awaiting_cas` pattern to `awaiting_rubric`. Student submits, sees "received, grading in progress" state, rubric result streams back via SignalR within 5-30s. Queue monitoring: (a) Prometheus counter `rubric_grading_queue_depth` with Grafana alert at >500, (b) p95 `rubric_grading_latency_seconds` SLO 30s, alert at 60s, (c) `rubric_grading_dlq_size` alert at >0 — any DLQ message is a human-review ticket because the student is owed feedback. Queue backend must be durable (NATS JetStream not core NATS) — losing a grading request means losing student work, which is a correctness incident, not an ops one.

### Storage growth from photos

Back-of-envelope at 30-day retention per ADR-0003:
- Assume 100k DAU, 20% ever use photo-diagnosis (realistic for opt-in feature) = 20k photo-using students.
- Average 4 photos/student/month (not 5 — generous upper bound from the brief's Q1 finops math is 5, real usage will be lower).
- Avg photo size after EXIF-strip + optional server-side re-encode to JPEG 85% at 2048px max = **~600-900 KB**. Use 750 KB.
- Monthly inflow: 20k × 4 × 750 KB = **60 GB/month**.
- 30-day rolling: **~60 GB steady-state**.
- Annual peak (exam season March-June doubles usage): **~120 GB peak**.

S3 Standard at ~$0.023/GB/mo = **$1.40/mo steady, $2.80/mo peak**. Trivial. However: EXIF-strip re-encode happens on upload hot path, which is CPU-bound — size each pod accordingly. **Purge is not free** — S3 lifecycle rules on `session-scope/photos/{tenant}/{session-id}/` prefix with 30-day expiration + eventbridge notification on delete so the misconception-derived event-stream record is also purged. Without the notification, ADR-0003 compliance breaks at the audit layer. Archival: **no archival**. Session-scope data does not get archived — it gets purged. Anything kept longer violates ADR-0003.

## Q1 "photo" rate limit tightness

Not tight enough in its current default shape for adversarial use. See recommendations above. Minimum asks: per-hour + per-day + per-tenant stacked limits, Redis-backed not in-memory, and tenant-override capability so enterprise customers can disable photo entirely (ties to persona-enterprise 7.5).

## Section 8 positions

1. **Q1 framing**: **Narrow only for v1.** Broad framing multiplies vision cost by ~5x, multiplies prompt-injection surface (persona-redteam 7.8), and has no outage runbook. Narrow + diagnose-wrong-answer keeps blast radius small.
2. **Q2 implementation**: **B (per-session student toggle).** Zero ops impact, no backend state, no scheduler coupling. C is pedagogically tempting but adds scheduler-picker complexity that interacts with PRR-220 timezone work.
3. **Q3 architecture**: **Shared `FreeformInputField<T>` with per-subject adapters.** Fewer components = fewer moving parts = fewer outage surfaces.
4. **Q3 chem Launch-scope**: Slip chem free-form to v1.1. MC-only ships a promise we cannot keep; better to ship honest scope.
5. **Q3 humanities Launch-scope**: Same — slip. Rubric grader at scale needs a burn-in period that v1 timeline cannot absorb.
6. **Cost cap**: Q1+Q3 **will** push through the $3.30 cap at broad framing. At narrow framing + Q3 essays capped at 2/week/student with aggressive prompt-cache (PRR-233), we stay inside. SRE position: ship narrow + cap or do not ship.

## Recommended new PRR tasks

1. **PRR-244** — Vision-model outage runbook + circuit breaker. Deliverables: circuit breaker per provider, "analysis queued" UX, SignalR late-delivery path, PagerDuty integration, named on-call owner. **Blocks Q1 launch.** Effort: M.
2. **PRR-245** — Vision-call SLO + cost guardrails. Deliverables: p95 latency SLO 8s, availability SLO 99% (downstream of provider 99.5%), per-student+per-tenant+global rate limits in Redis token bucket, cost-per-student Grafana panel, auto-throttle at 80% budget. Effort: M.
3. **PRR-246** — Async rubric-grading queue. Deliverables: NATS JetStream queue, `awaiting_rubric` UX state, Prometheus metrics (queue depth, p95, DLQ), 30s p95 SLO, DLQ ticket automation. Effort: M.
4. **PRR-247** — Photo storage lifecycle + ADR-0003 purge enforcement. Deliverables: S3 lifecycle rules, EventBridge purge-notification wiring to misconception event-stream, monthly audit job, tenant-disable toggle. Effort: S.
5. **PRR-248** — Amend PRR-231 SAT+PET capacity plan with vision-queue exam-week spike forecast. **Amends existing task.** Effort: S.

## Blockers / non-negotiables

- **Hard blocker**: PRR-244 (vision outage runbook + circuit breaker) must ship with Q1. Hard-coupled to external provider with no degrade path is unshippable for a Bagrut-morning-critical system.
- **Hard blocker**: Q3 sync rubric grading is off the table. Any spec that shows a synchronous rubric grade in the student UI must be returned for rework before task-split.
- **Hard blocker**: Session-scope purge wiring (PRR-247) is an ADR-0003 compliance gate, not just ops hygiene.
- **Soft blocker**: PRR-245 cost guardrails before broad-framing can be considered in v1.1.
- **Soft blocker**: PRR-248 capacity amendment before Bagrut June 2026 and SAT October 2026.

## Questions back to decision-holder

1. Narrow vs broad framing for Q1 v1 — SRE strongly recommends narrow. Confirm?
2. Photo retention — ADR-0003 says 30 days session-scope. Is there any product case for keeping a photo beyond session for the student's own reference? If yes, that's an ADR-0003 amendment, not an ops config.
3. Rubric grading DLQ — who owns the human-review ticket? Operator role unassigned.
4. Vision provider diversity — single provider (Anthropic) or multi-provider failover? Multi = 2x integration cost, 2x redteam surface, but removes the hard SLA ceiling. Product call.
