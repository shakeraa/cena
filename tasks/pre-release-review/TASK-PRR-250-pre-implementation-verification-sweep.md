# TASK-PRR-250: Pre-implementation verification sweep for ADR-0059 + PRR-245

**Priority**: P1 — verifies the faith-based references in PRR-245's design
**Effort**: S (1-2 days; pure investigation, no code change)
**Lens consensus**: implied — "verify data E2E" memory rule
**Source docs**: ADR-0059, PRR-245, this conversation's gap analysis
**Assignee hint**: claude-1 (low-risk investigation pattern matches her recent work) OR claude-code main session
**Tags**: source=gap-analysis-2026-04-28, epic=epic-prr-n, priority=p1, investigation, verification, no-code
**Status**: Ready
**Tier**: launch-adjacent
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

PRR-245's design references several artifacts I (claude-code) cited from filenames + adjacent ADRs without reading the actual code. Before PRR-245 implementation kicks off, verify each reference is real, document its API surface, and flag any blockers for the planned design.

## Scope (pure read; no code changes; no git changes)

### 1. `IInstitutePricingResolver` (PRR-244)

- Confirm: file exists, the resolver interface has the methods required by ADR-0059 §5 (variant rate-limit overrides per institute).
- Document: actual method signatures + how a "rate-limit override" maps onto its data shape.
- Flag: if the resolver doesn't expose rate-limit overrides today, file a follow-up extension task.

### 2. `BagrutCorpusItemDocument` (PRR-242 output)

- Confirm: file exists, document carries `{paperCode, year, moed, questionNumber, subject, stream, body, metadata}` (or whatever fields the OCR pipeline actually produced).
- Document: actual field names + types. Compare to ADR-0059's assumptions (paperCode + year + moed + questionNumber).
- Verify with Marten: corpus is populated in dev env (count of documents > 0; sample 5 documents to inspect tagging quality).
- Flag: any field mismatches between corpus shape and ADR-0059 reference-page filtering needs.

### 3. PRR-218 + PRR-221 status

- PRR-218 (StudentPlan aggregate events) — done, in-flight, or pending? `git log` for recent commits referencing `ExamTarget` + `StudentPlan`.
- PRR-221 (onboarding `exam-targets` + `per-target-plan` steps) — same.
- Verify: is `ExamTarget.QuestionPaperCodes` actually populated for any real student in dev? Run a sample query against StudentActor to check.
- Flag: if onboarding hasn't shipped, ADR-0059's §4 filter scope is moot until it does.

### 4. Feature-flag plumbing

- Locate: does Cena have a feature-flag system (LaunchDarkly, OpenFeature, custom service, etc.)?
- Confirm: the `reference_library_enabled` flag named in PRR-245 can plug into an existing flag-resolver path; if not, a follow-up task is needed.
- Flag: if no flag system exists, escalate — PRR-245's "default-off at Launch" depends on this.

### 5. `RateLimitedEndpoint` decorator

- Locate: does this exist? What's the actual decorator/middleware shape?
- Document: how it integrates with endpoint registration in `Cena.Student.Api.Host.Program`.
- Flag: if not, surface the rate-limit gap.

### 6. `CasGatedQuestionPersister`

- Locate + read: confirm the persister works for student-initiated variants (not just admin-initiated). Document any auth/role-checks that might reject a student caller.

### 7. EPIC numbering verification

- Confirm: ADR-0059 + PRR-245 + EPIC-PRR-N references all match (no dangling links from earlier `EPIC-PRR-H` mistake).
- Verify: any other ADR or epic that referenced the dropped name.

## Files

### New
- `tasks/pre-release-review/reviews/PRR-250-verification-sweep-findings.md` — 1-page report with per-section findings, flags, follow-up tasks

### Modified (only if findings warrant)
- ADR-0059 — adjust references if §3 reveals filter source isn't populated
- PRR-245 — adjust file references if §1, §2, §5, §6 surface API mismatches

## Definition of Done

- All 7 sections answered with file paths + line numbers + verdict (verified | mismatch | missing).
- Any blockers surfaced as new follow-up tasks in the queue.
- Findings file filed and linked from ADR-0059 §History.

## Blocking

- None.

## Non-negotiable references

- Memory "Verify data E2E"
- Memory "Senior Architect mindset" (this task IS the trace-data-flows discipline)

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<findings sha + count of follow-up tasks created>"`

## Related

- ADR-0059, PRR-245, EPIC-PRR-N, PRR-242, PRR-243, PRR-244.
