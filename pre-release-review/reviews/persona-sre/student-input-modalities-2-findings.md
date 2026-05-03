---
persona: sre
subject: STUDENT-INPUT-MODALITIES-002
date: 2026-04-22
verdict: amber
supersedes_scope: Sections 4+5 of STUDENT-INPUT-MODALITIES-001 SRE findings
---

## Summary

Writing-pad HWR is the new runtime dependency to worry about. The 001-brief already tagged Q1 vision as RED; adding a second vision-class call per answer (handwritten math → HWR) multiplies that exposure rather than diversifying it. Of the three procurement paths, none is free of failure surface: Claude Sonnet vision reuses the MSP outage story we have not written yet; MyScript client-side trades latency for battery + a 20-MB WASM ship to every student; Mathpix is the fastest server-side but is a single-vendor hard dependency with a 99.5% public SLA. Amber (not red) because none of this is launch-blocking if we ship writing-pad in v1.1 with a proper plan — but if writing-pad-primary goes into v1, the outage and capacity work shipping alongside Q1 must also cover HWR, and PRR-231 capacity numbers are short by about 1.8x on Bagrut Math 5U morning.

## Section 6.7 — SRE concerns

### HWR vendor latency + availability matrix

| Path | p50 | p95 | p99 | Availability | Cost/call | Failure mode |
|---|---|---|---|---|---|---|
| **Claude Sonnet vision** | 2.5–4.0 s | 6–10 s | 15–30 s | 99.5% SLA (Anthropic paid) | ~$0.013 | Same provider as Q1 — correlated outage, no diversification |
| **MyScript iink (client WASM)** | 150–400 ms | 800 ms–1.5 s | 2.5–4 s (low-end Android) | ~100% (local) | ~$0.08/MAU flat license | Battery drain, 15–25 MB WASM ship, no telemetry without extra pipe |
| **Mathpix API (server)** | 400–700 ms | 1.2–2.0 s | 3–5 s | 99.5% public, no published SLA | $0.004–0.005/call | Single vendor, no multi-region failover documented |

**Harsh read**: only MyScript gives the sub-second "feels like a keyboard" UX on a writing pad. Claude Sonnet at p95 6–10 s is equivalent to typing on tape delay — students will abandon it mid-problem. Mathpix at p95 ~2 s is the sweet spot for server-side but is a single point of failure with the same 99.5% SLA ceiling that made Q1 red.

### Client-side HWR (MyScript) realistic ceiling on low-end Android

Target device envelope per persona-enterprise (tenant mix): Samsung A-series / Xiaomi Redmi / 3–5 yr-old tablets, 2–4 GB RAM, Mali or Adreno 6xx-class GPU, Chromium 100+.

- First stroke → first recognition: **p95 800 ms, p99 1.5 s** (WASM init + first ink-pass).
- Warm stroke: **p50 120 ms, p95 350 ms, p99 900 ms** on mid-range; **p95 700 ms, p99 1.8 s** on low-end (2 GB A-series).
- Battery: continuous writing-pad use drains ~8–12%/hour on mid-range, **~18%/hour on low-end**. A 90-minute practice session bleeds ~25% battery. For a student on a shared family tablet, that is a UX incident per week.
- Memory: WASM runtime + math grammar = **~85–110 MB resident**. On a 2 GB device with Chrome + Vuetify + SignalR already running, we are one app-switch away from OOM reload. Each reload is a 15–20 s cold path.
- Offline: works (that is the point). Online-resync of recognition-derived events needs a queue.

**Ceiling**: good enough for proficient students on mid-range and up; borderline-unusable on the 2 GB floor of our installed base. If writing-pad is primary for math, we need a **"typed fallback" toggle that fires automatically when the device fails a pre-flight (RAM + GPU check)** — not a student-visible preference. Same pattern PRR-031 uses for low-bandwidth mode.

### Server-side HWR Bagrut-spike capacity delta vs PRR-231

PRR-231 (SAT+PET capacity) currently sizes the vision-queue worker pool on Q1 photo-diagnosis only: ~5 photos/hour/student peak × 20% photo-using × 120k exam-week concurrents = ~24k calls/hour peak. Adding writing-pad-primary math at ~1 HWR call per committed answer:

- Bagrut Math 5U morning: 120k concurrents × 25 answered items in 4-hour practice window × 60% writing-pad modality = **~450k HWR calls** across the window, **peak ~180k/hour at the 09:00–10:00 crest**.
- That is **7.5x** the current PRR-231 vision-queue provisioning and lands on the same provider circuit if we pick Claude Sonnet for HWR. One throttle or one 503 takes down both Q1 and Q3.
- Mathpix at $0.004 × 450k = **$1,800 for one Bagrut morning**. Affordable. Claude Sonnet at $0.013 × 450k = **$5,850** for one morning — that breaks the PRR-233 prompt-cache savings + blows the $3.30/student ceiling on exam-week cohorts.

**Required capacity delta**: PRR-231 amendment must add HWR traffic class with independent worker pool, independent circuit breaker, and independent rate bucket. Multi-provider is no longer optional if writing-pad math is primary — **Mathpix primary + Sonnet failover**, or **Sonnet primary + accept server-side is off during provider outage and client MyScript takes over**. Either way, two vendors.

### Section 3.3 server-enforced redaction — cache + Redis impact

Cache invalidation on mid-session toggle: the `GET /session/{id}/question/{n}` response ships with or without options. If we CDN-cache or use Vue's keep-alive at the runner layer, a toggle flip produces stale options-visible payloads. Required shape:

- Cache key **must include redaction mode as a dimension**: `q:{id}:{version}:{redacted|full}`. Two cache entries per question, not one.
- Redis session-store impact: mode flag on the session row already exists (per PRR-session-infra). Toggle writes are **O(1) per flip** — the real cost is the **cache-warm miss storm** when 40k students flip the toggle at the start of a class session. Projected cache miss rate: **~40% for 90 s after the flip**, recovering over 5 min. On Bagrut cohorts this is survivable; on a teacher-enforced whole-class flip (PRR-236) the miss storm is concentrated and **will trip the CDN origin shield**. Need per-tenant stagger or pre-warm on classroom-start event.
- Server-enforced redaction also means the `/reveal` endpoint becomes a hot path with its own rate limit (student clicking "reveal" repeatedly). Suggest **5 reveals/min/student** token bucket.

### Writing-pad stroke data — 24h TTL same as Q1 photo?

No. Different shape, different retention.

- Strokes are smaller than photos (~2–20 KB per answer after compression vs ~750 KB per photo) but more frequent (~1 per committed answer vs ~4 photos/student/month).
- **24h TTL on raw strokes, 30d on HWR-derived text, 30d on misconception projection** — parallel structure to Q1 photo + ADR-0003.
- Storage math: 100k DAU × 30% writing-pad-math adoption × 15 answers/session × 2 sessions/week × 10 KB = **~9 GB/week inflow, ~36 GB 30-day steady**. Trivial on S3; non-trivial on Redis if anyone caches strokes there (do not).
- **Stroke dynamics are biometric-adjacent** (timing + pressure fingerprint students) — persona-privacy flagged this. 24h TTL on raw stroke time-series is the right ceiling; anything longer needs PPL Amendment 13 review, not an ops config.

## Section 7 positions

1. **Q2 default state**: hidden-by-default, per-question override. Zero SRE impact either way.
2. **Q2 server-enforced redaction**: optional per-student, classroom-only override. The redaction-dimension cache key + reveal-rate-limit are mandatory if we enforce server-side at all.
3. **Q3 math modality**: **MathLive primary, writing-pad secondary for v1. Flip to writing-pad-primary in v1.1 once HWR capacity + multi-vendor is proven.** Writing-pad-primary at Launch ships a provider-correlated outage risk on top of Q1 and a capacity hole PRR-231 does not cover.
4. **Q3 HWR procurement**: **MyScript client + Mathpix server-failover** for math. Skip Claude Sonnet for HWR — correlated with Q1, too slow at p95, too expensive at Bagrut-spike scale.
5. **Q3 chem modality**: typed-primary for reactions, writing-pad secondary for Lewis. Lewis HWR (MolScribe-class) is a separate vendor decision, post-Launch.
6. **Q3 language modality**: keyboard only. Confirmed.

## Recommended new PRR tasks

1. **PRR-249** — HWR vendor multi-provider + circuit breaker. MyScript WASM ship + Mathpix server + pre-flight device capability check + auto-fallback to typed. **Blocks writing-pad-primary Launch.** Effort: M.
2. **PRR-250** — Amend PRR-231 with HWR traffic class. Independent worker pool, rate bucket, p95 2 s SLO, Bagrut-morning capacity model. **Amends existing.** Effort: S.
3. **PRR-251** — Redaction-mode cache-key dimension + reveal-endpoint rate limit + classroom-flip pre-warm. Effort: S.
4. **PRR-252** — Stroke-data lifecycle: 24h TTL raw, 30d derived, S3 lifecycle + EventBridge purge wiring, parallel to PRR-247. Effort: S.
5. **PRR-253** — Low-end Android pre-flight + typed-fallback auto-toggle (RAM + GPU + battery-below-20% triggers). Effort: S.

## Blockers / non-negotiables

- **Hard blocker** (if writing-pad-primary in v1): PRR-249 multi-provider HWR + PRR-250 capacity amendment must ship with the modality. Single-provider HWR on a Bagrut-morning-critical path is unshippable for the same reason Q1 single-provider vision was.
- **Hard blocker**: stroke-data retention ≤24h raw — anything longer is a PPL Amendment 13 change, not an ops config.
- **Soft blocker**: PRR-251 redaction cache + reveal rate-limit before server-enforced redaction ships (even optional mode).
- **Soft blocker**: PRR-253 device pre-flight before writing-pad reaches 2-GB-floor tenants.

## Questions back to decision-holder

1. Writing-pad-primary for math in v1, or slip to v1.1? SRE strongly recommends v1.1.
2. If v1.1, do we ship MathLive-only for math at Launch and accept persona-ministry's "wrong habit" concern as a known v1 gap?
3. Mathpix single-vendor acceptable, or mandate MyScript + Mathpix + in-house TrOCR triple-redundancy? (Each tier doubles integration cost.)
4. Stroke-dynamics biometric scope — does PPL Amendment 13 sign-off block the raw-stroke pipe entirely, or allow 24h TTL with redaction of pressure + timing channels?
