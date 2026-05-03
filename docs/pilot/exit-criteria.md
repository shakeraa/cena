# Arabic-first pilot — exit criteria (DRAFT)

> **🚨 DRAFT — Dr. Yael + Dr. Nadia + Dr. Rami sign-off required 🚨**
>
> These tripwires are quantitative and pre-registered. Any trip pauses
> or stops the pilot. No post-hoc relaxation.

- **Task**: RDY-079
- **Related**: `arabic-first-pilot-design.md` §5

## Why pre-register exit criteria

Without a written exit plan, a pilot tends to continue through weak
signals because nobody wants to be the one to stop it. The result is
wasted student time and compounding harm when something is wrong.

Pre-registered tripwires:
- Remove the "is this bad enough to stop?" judgement from the moment
  (when emotions + sunk-cost bias interfere)
- Give school principals + parents a durable commitment ("here is
  when we pause")
- Make it auditable — the pilot honored its plan, or explain why it
  did not

## Tripwires

### Tripwire 1 — Underperformance

**Definition**: at week 6 midpoint, matched-cohort ANCOVA of mastery
gain (primary outcome) shows the Cena-treatment cohort trails control
by ≥ 0.3 SD.

**Instrument**: abbreviated mid-pilot inventory, same item
distribution as baseline, 20 min. Administered to both treatment
and control.

**Action on trip**:
1. **Pause** treatment cohort product use immediately
2. Convene Dr. Yael + Dr. Nadia + Prof. Amjad within 5 business days
3. Review candidate root causes:
   - Arabic-lexicon accuracy (Prof. Amjad)
   - Pedagogy appropriateness (Dr. Nadia)
   - Statistical anomaly / sampling artifact (Dr. Yael)
4. Decide: resume / modify / stop

**Escalation**: if differential persists or worsens at a second
mid-pilot check at week 8 (re-measured), **stop**. Revert students
to existing instruction. Report findings honestly.

### Tripwire 2 — Distress

**Definition**: aggregate weekly in-platform affective self-check
(1 question, 3-point "how did this session feel?") shows reported
distress above baseline + 1 SD for **two consecutive weeks**.

**Instrument**: single-question self-check at end of each session.
3-point scale:
- 😊 "Good — I felt OK"
- 😐 "Okay — some frustration"
- 😟 "Hard — I felt stuck or upset"

Per ADR-0037, this is affective self-signal with the same retention
and visibility rules: 90-day retention, no per-student teacher view,
no ML training, aggregate only for the pilot dashboard.

**Action on trip**:
1. **Pause** immediately (same-day)
2. Investigate in parallel: is distress concentrated on a specific
   topic? A specific product surface? A specific time of day?
3. If tied to a single product surface, roll that surface back + resume
4. If diffuse, consult Dr. Nadia; may require stopping the pilot

### Tripwire 3 — School or Ministry objection

**Definition**: a principal, teacher, or Ministry representative
**formally objects** in writing, for any reason.

**Instrument**: written communication received by the pilot product
lead or DPO.

**Action on trip**:
1. **Pause** at the objecting school within 24 hours
2. Acknowledge receipt within 48 hours
3. Schedule meeting within 5 business days
4. Resume only with written agreement from the objecting party
5. If objection is Ministry-level (not school-level), stop the pilot
   entirely pending Ministry clarification

### Tripwire 4 — Engagement collapse

**Definition**: fewer than 30% of enrolled students log in at least
once per calendar week, measured across weeks 3 and 4.

**Instrument**: Cena login telemetry, filtered to pilot cohort.

**Action on trip**:
1. **Pause** enrolment expansion (don't enrol more students)
2. Investigate: onboarding failure? Tool mismatch? School
   scheduling collision?
3. If root cause fixable inside 2 weeks: fix + resume
4. If root cause structural (wrong tool for the cohort): stop
   honestly, report the negative finding

### Tripwire 5 — Safety / ship-gate violation

**Definition**: any ship-gate scanner (docs/engineering/shipgate.md)
detects a banned pattern (streak counter, loss-aversion copy,
variable-ratio reward, dark pattern) in a pilot-eligible build.

**Action on trip**:
1. Rollback to a known-clean build — same day
2. File a ship-gate-violation incident (internal, not public)
3. Resume pilot only after the build is re-verified clean

## Remediation on pause or stop

Whether the pilot pauses or stops, the remediation promise to
participants is unchanged:

- **No penalty**: students continue unimpeded access to existing
  school instruction
- **Goodwill access**: Cena offers continued one-on-one tutoring
  access for 8 weeks post-stop (not conditional on anything — not
  on re-consent, not on a positive review)
- **Honest report**: findings up to the stop point are documented in
  an internal summary; negative findings especially

## Governance

- **Owner of the dashboard**: Cena product lead (runs the weekly
  review)
- **Trip decision authority**: Exit Review Board (Dr. Yael + Dr. Nadia
  + Prof. Amjad); consensus required on Tripwire 1 decisions,
  majority on 2–5
- **Communication**: any trip is communicated to enrolled parents +
  school principals within 48 hours of the decision
- **Regulatory**: any trip involving distress or objection is
  disclosed to the DPO within 24 hours for notification-obligation
  assessment (see `docs/ops/runbooks/incident-response.md` §Stage 4)

## Monitoring implementation

The admin dashboard (`src/admin/full-version/src/pages/apps/pilot/`)
ships with:

- **Weekly mastery-gain delta tile**: auto-computed from baseline
  + in-platform proxy; alerts when |delta| crosses the trip threshold
- **Distress aggregate tile**: 7-day rolling mean of the 3-point
  self-check; alerts when > baseline + 1 SD
- **Engagement tile**: weekly login rate; alerts when < 30% for two
  consecutive weeks
- **Alert routing**: Slack + email to the product lead + DPO
  immediately; weekly summary to Exit Review Board

All tiles aggregate-only. No per-student identification anywhere in
the exit-criteria workflow.

## References

- Pilot design: `arabic-first-pilot-design.md`
- Baseline: `baseline-instrument.md`
- ADR-0037 (affective data): `docs/adr/0037-affective-signal-in-pedagogy.md`
- Ship-gate: `docs/engineering/shipgate.md`
- Incident response: `docs/ops/runbooks/incident-response.md`

---
**Status**: DRAFT — Exit Review Board sign-off required.
**Last touched**: 2026-04-19 (engineering draft)
