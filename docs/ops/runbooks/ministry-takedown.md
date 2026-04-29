# Runbook — Ministry of Education takedown response

**Ticket source**: [PRR-254](../../../tasks/pre-release-review/TASK-PRR-254-ministry-takedown-runbook.md)
(extends [PRR-016 exam-day SLO change-freeze](../../../tasks/pre-release-review/TASK-PRR-016-publish-exam-day-slo-change-freeze-window-in-cd.md) — same SRE-lane pattern).
**ADR alignment**: [ADR-0059 §15.5 + §14.6 Q-D](../../adr/0059-bagrut-reference-browse-and-variant-generation.md), [ADR-0043](../../adr/0043-bagrut-reference-only-enforcement.md).
**Primary paging channel**: SRE PagerDuty → `#incident-ministry-takedown`.
**Owners**: cena-sre (primary), cena-platform (config + purge tooling), cena-legal-coord (counsel liaison), cena-product (student-comms lead).

---

## 1. Context

If the Israeli Ministry of Education sends a takedown notice (formal letter,
email, or DM-via-counsel) for our use of past-Bagrut content via the
reference library or via source-anchored variants, we have **a hard
30-minute commitment** to:

1. Disable both student-facing surfaces (browse + practice-from-variant)
   without waiting for code deploy.
2. Preserve the audit trail in legal-hold storage so counsel has the
   incident envelope.
3. Quarantine all persisted variants whose provenance traces back to
   the affected paper code.

This is the runbook ops follows. It is intentionally tabletop-drillable
without a real takedown — quarterly drills surface staleness in the
config flag, the purge tool, the legal-hold pipeline, and the comms.

Comparable platforms (Bagrut Plus, Bagrut Tikshoret, school-portal PDFs
per the persona-ministry research at
[`pre-release-review/reviews/persona-ministry/reference-library-findings.md`](../../../pre-release-review/reviews/persona-ministry/reference-library-findings.md))
have not been the target of a public Ministry takedown. We assume our
own incident profile is similar but defend with a 30-min response.

## 2. Trigger conditions

This runbook fires when **any** of:

- A formal takedown letter is received via email or postal channel addressed
  to Cena's domain owner or legal contact.
- A DM/email from Ministry counsel referencing a specific paper code or
  the platform name + a request to remove content.
- A media inquiry citing Ministry concern about Cena's display of past
  Bagrut content (treat as soft trigger — engage legal-coord first; do
  not flip the kill-switch on inquiries alone).
- Counsel (Shaker's retained counsel + project-owner Shaker) jointly
  decide to pre-emptively disable for safety pending review.

Out of scope: unrelated Ministry communications (general policy notices,
exam-schedule changes, etc.) that do not request removal.

## 3. 30-minute kill-switch procedure

### 3a. Immediate (T+0 → T+5 min)

1. **Acknowledge the alert** in `#incident-ministry-takedown` and page
   the SRE primary on-call. Cena-legal-coord is paged in parallel.
2. **Set incident severity** = **SEV1** (regulatory).
3. **Capture the trigger envelope** — original letter / email / DM
   uploaded to the legal-hold S3 bucket via:
   ```bash
   ./scripts/ops/legal-hold-upload.sh <evidence-file> ministry-takedown-$(date +%Y%m%d-%H%M)
   ```
   (See §6 for legal-hold bucket provisioning. The script is a thin
   `aws s3 cp` wrapper that tags the object with `legal-hold=true` so
   the lifecycle policy never deletes it.)

### 3b. Kill-switch (T+5 → T+15 min)

The kill-switch is **two flags** that must both go OFF:

| Flag | Disables | Storage |
|---|---|---|
| `Cena:ReferenceLibrary:Enabled` | Student browse of שאלון reference library + Reference<T> endpoints (PRR-267) | Config / .env / GrowthBook (whichever is operative) |
| `Cena:Variants:BagrutSeedToLlmEnabled` | Source-anchored variant generation pipeline (admin-side; PRR-249-gated) | Same |

Procedure:

1. **Set both flags to `false` via the config plane.** No code deploy
   required. The SPA + admin polls the `/feature-flags` endpoint; the
   server-side endpoint group filters trip on `false` and return 503
   `EXAM_PREP_RUNNER_DISABLED` / `REFERENCE_LIBRARY_DISABLED` (already
   wired by mock-exam Phase 1G + PRR-267 spec).
2. **Verify both surfaces are disabled** within 5 min of the flip:
   ```bash
   curl -s -H "Authorization: Bearer $STUDENT_TOKEN" \
     https://api.cena.app/api/v1/reference/papers
   # Expect: 503 with structured error code REFERENCE_LIBRARY_DISABLED.

   curl -s -H "Authorization: Bearer $ADMIN_TOKEN" \
     https://api.cena.app/api/admin/ingestion/jobs/feature-flags | jq .
   # Expect: bagrutSeedToLlmEnabled = false.
   ```
3. **Audit-log the flip** with the SEV1 incident id + actor identity.
   The flag-flip path emits a structured log line at `LogLevel.Warning`
   that the SIEM exporter forwards to the legal-hold log bucket
   (separate from the evidence bucket).

### 3c. Communication (T+10 → T+30 min)

1. **Internal**: post in `#incident-ministry-takedown` at T+10 with:
   `KILL-SWITCH ACTIVE — surfaces disabled at <UTC ts>; investigation in progress; ETA on student comms <T+45m>`.
2. **Student-facing comm**: cena-product drafts the in-app banner ("This
   feature is temporarily disabled while we coordinate with the Ministry
   of Education. Practice on AI-authored items remains available.").
   Banner shipped via the existing `feature-banner` config (no deploy).
3. **Counsel ack**: cena-legal-coord confirms counsel has the trigger
   envelope + the SEV1 timestamp by T+30. This anchors the legal
   timeline; without it, the response window is rebut-able.

## 4. Per-source-paper-code variant purge

After the kill-switch is active, purge persisted variants whose
provenance traces to the affected paper code. This is **separate** from
the kill-switch — kill-switch stops new serves; purge invalidates the
existing pool.

1. **Get the affected paper codes** from the takedown notice. Most
   notices name a specific שאלון code (e.g., `035582`) or a year-season
   range. Translate to the structured `Provenance.Source` form:
   ```
   ministry-bagrut/{paperCode}/{year}/{season}/{moed}/q{n}
   ```
   Example match pattern: `ministry-bagrut/035582/*` purges every
   variant derived from any question of paper 035582 across all years.

2. **Run the purge script** in dry-run first:
   ```bash
   ./scripts/ops/purge-variants-by-source.sh \
     --pattern 'ministry-bagrut/035582/*' \
     --dry-run
   ```
   Inspect the candidate list. Counsel sign-off **on the candidate list**
   before live run.

3. **Live purge**:
   ```bash
   ./scripts/ops/purge-variants-by-source.sh \
     --pattern 'ministry-bagrut/035582/*' \
     --reason "Ministry-takedown-$(date +%Y%m%d)"
   ```
   The script:
   - Selects every `QuestionDocument` where `SourceProvenance.Source`
     matches the pattern (LIKE on the JSONB column).
   - Sets `Status = "Withdrawn"` + writes a `WithdrawnReason` field.
   - Removes them from `LearningSessionQueueProjection`.
   - Does **NOT** delete `LearningSessionAttempt` rows for the
     variants — student practice attempts remain in the audit trail
     under the existing 180-day retention. This is intentional: the
     incident envelope must include "what students saw" for counsel.

4. **Invalidate the SPA cache**: bump `Cena:ReferenceLibrary:CacheBuster`
   so any cached client `/papers` responses purge on next refresh.

## 5. Legal-hold

Two storage classes:

| Class | What | Retention |
|---|---|---|
| **Evidence bucket** | Trigger envelope (letter / email / DM) + counsel correspondence + screenshots of the affected items as-they-were | **7 years** (Israeli statute of limitations + buffer) |
| **Audit-event store** | `BagrutReferenceItemRendered_V1` + `BagrutReferenceConsentGranted_V1` events from the affected window | **180 days** (already enforced by ADR-0059 §15.7 retention worker; on takedown, extend to 7 years for the affected paper codes via legal-hold tag) |

Provisioning (one-time, IaC-managed):

```bash
# AWS CLI illustration; production uses Terraform (separate repo).
aws s3api create-bucket \
  --bucket cena-legal-hold-ministry-takedown \
  --region eu-central-1 \
  --object-lock-enabled-for-bucket

aws s3api put-object-lock-configuration \
  --bucket cena-legal-hold-ministry-takedown \
  --object-lock-configuration '{
    "ObjectLockEnabled": "Enabled",
    "Rule": { "DefaultRetention": { "Mode": "COMPLIANCE", "Years": 7 } }
  }'
```

Bucket ARN goes in `appsettings.{Production,Staging}.json` under
`Cena:LegalHold:MinistryTakedownBucketArn`.

## 6. Re-enable procedure

After the takedown is resolved (counsel concludes the posture is
defensible, or licensing terms are negotiated, or a fall-back to
metadata-only mode per ADR-0059 §5 is approved):

1. **Counsel sign-off** in writing (email is fine; uploaded to evidence bucket).
2. **Verify ADR-0059 invariants still hold** for the re-enable path:
   - §15.5 — variant rate-limit caps active (PRR-265).
   - §15.6 — browse-history scope-limitation active (PRR-269).
   - §15.7 — 180-day retention worker active (PRR-266).
3. **Verify legal-hold integrity** — the 7-year extension on affected
   events is preserved through the re-enable.
4. **Flip both flags back ON.**
5. **Communicate**: in-app banner cleared; status-page incident closed;
   teacher/parent affected-tenants comm via the standard incident-resolved
   channel.

## 7. Tabletop drill

Quarterly drill on the SRE on-call calendar. Format:

1. Simulate a Ministry takedown letter (counsel-mock; not a real letter).
2. Run §3 + §4 end-to-end, measuring actual time-to-kill-switch.
3. Log the drill in `IncidentResponseLog` with:
   - Trigger received at T+0
   - Kill-switch active at T+X
   - Both flags verified OFF at T+Y
   - Student banner deployed at T+Z
   - Purge dry-run completed at T+W
4. **Pass criterion**: kill-switch active in <30 min, purge dry-run in <2h.
5. **Failure modes** (file as follow-up tasks if observed):
   - Config flag flip required deploy → remediation: move flag to
     runtime-config plane.
   - Purge script returned 0 candidates when fixture had 5 → script
     regression.
   - Banner copy not localized → i18n regression.

Drill calendar: `ops/calendars/sre-drills.yml`. Schedule: Q1 (Feb), Q2
(May), Q3 (Aug), Q4 (Nov), each on the second Thursday at 10:00 IST.

## 8. Out-of-band escalation

If the takedown is **broader** (Ministry challenges the platform's
existence, not just a specific paper):

1. SEV0 escalation; CEO + counsel + project owner all on the bridge.
2. Disable the entire reference library namespace (not per-paper purge).
3. Coordinate with cena-finance on tenant refunds for the affected
   surface.
4. Consider the metadata-only fallback mode (ADR-0059 §5) as the
   medium-term posture.

This branch is **not drilled quarterly** — it's an architecture-decision
trigger, not a runbook. The SRE-lane runbook above handles the
per-paper case which is the realistic incident profile per
persona-ministry §14.2.

## 9. Non-negotiable references

- ADR-0059 §15.5 + §14.6 Q-D — normative source
- ADR-0043 — Bagrut reference-only enforcement
- PRR-016 exam-day SLO — sibling SRE-lane pattern
- PRR-249 legal-delta memo — posture context
- `feedback_no_stubs_production_grade` — runbook is real, not aspirational
