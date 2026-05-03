---
persona: enterprise
subject: MULTI-TARGET-EXAM-PLAN-001
date: 2026-04-21
verdict: yellow
---

## Summary

The multi-target model is architecturally correct and long-overdue — "grade is per-target, not per-student" is exactly right for a market where one account spans Bagrut + Psychometry + SAT. But the brief quietly treats the multi-institute rollout as background colour. It isn't. ADR-0001 is Phase 1 in-flight, Phase 2+3 blocked (memory: Tenancy rollout state). Every new aggregate, event, and endpoint landing before Phase 2 completes is a future-retrofit line-item. The brief lists one "tenant header for future override" on the catalog and calls that future-proofing. It isn't. Two structural gaps (no `source` discriminator on `ExamTarget`, no `EnrollmentId` linkage) and three integration gaps (Mashov passback, roster import, audit log) make this yellow, not green. All fixable in v1 at modest cost. Not fixing them ships technical debt straight onto the Phase 2/3 critical path.

## Section 9.5 answers

**Q1 — Does the exam catalog need per-tenant variation in v2, and is the API future-proof?**
Yes, it will need variation, and no, the proposed shape is not future-proof. "Globally scoped, tenant header for future override" is header theatre. A future Saudi tenant, a Jewish-diaspora tenant, or an Israeli private tutor that runs a custom PET prep track does not want rows in the global catalog they cannot opt out of. What breaks if we ship the current shape: (1) no `effective_catalog_id` or composition rule, so you can't express "global MINUS `BAGRUT_CS` PLUS `SAT_SUBJECT_MATH_2`"; (2) no `catalog_version` pinned per enrollment, so a tenant that adds or hides an exam silently rewrites plans for students mid-term; (3) `syllabusReferenceId` is a flat string, so tenant-specific rubric overrides (PRR-033 per-tenant) have nowhere to live. Fix: model it now as `GlobalCatalog + TenantCatalogOverlay` with additive/subtractive rules, version-pin the overlay on each `ExamTargetAdded`, keep the v1 overlay set empty. Ship the shape, defer the admin UI. Cost: one extra table and one extra field on the event. This is the cheapest moment to do it.

**Q2 — Classroom-assigned plan vs student picks. Override, merge, coexist?**
Coexist, with provenance. A teacher assigning a class to Bagrut Math 5U must NOT erase the student's personally added Psychometry target, and must NOT let the student quietly archive the classroom-assigned target either. The right model is: `ExamTarget.Source = Student | Classroom | Tenant`, plus `AssignedBy` (ClassroomId or TenantId). Student-owned targets are fully editable. Classroom/Tenant-owned targets are read-only to the student (can add personal note, cannot change track/deadline/hours — those are the teacher's responsibility; interacts with ADR-0044 teacher-schedule-override and PRR-058 accommodations). Student can request removal; teacher approves.

**Source discriminator — v1 bake-in, not retrofit.** Adding a `Source` enum + `AssignedById` to `ExamTarget` and the three events costs maybe 40 lines of code and one migration column. Retrofitting post-launch means a schema change on every existing event, a replay, and angry tenant admins for every week we delay. Bake it in. Default = `Student`. Classroom/Tenant paths can ship later — the field reserves the semantic space now.

## Additional findings

**Mashov integration impact (PRR-039, PRR-017).** Multi-target significantly complicates Mashov passback and Mashov does not know about Psychometry or SAT. Current PRR-037 grade-passback ADR is scoped to Bagrut; the discriminator matters because only `BAGRUT_*` targets are passback-eligible. Without a `catalog_family` (Bagrut | Standardized | Other) the passback filter becomes a string-prefix match on `ExamCode`, which is the kind of implicit contract that breaks the first time someone adds `BAGRUT_TANAKH` as a reference target. Recommend PRR-037 ADR add an explicit `passback_eligible: bool` on the catalog entry and a rule that only `Source=Classroom|Tenant` targets are eligible (a student-self-declared Bagrut target is not authoritative for passback). Mashov credentials (PRR-017) unchanged — same secret-manager story.

**Roster import (PRR-021).** PRR-021 is marked Done but its DoD does not cover target assignment. Current CSV shape is assumed to be `[student_id, email, classroom_code]`. Teachers doing bulk import for a Bagrut 5U class expect every imported student to land with a classroom-assigned `ExamTarget(BAGRUT_MATH, 5U, <class_deadline>, <suggested_hours>)`. Either (a) the import screen gains a "default target for this batch" field, or (b) classroom creation itself carries a default target that auto-applies on roster-join. Option (b) is cleaner and survives re-imports. Adds a `Classroom.DefaultExamTargetTemplate` field — modest cost, big teacher-UX win, respects "Labels match data" (a "Bagrut Math 5U class" that produces students with no math target is a lying label).

**Admin audit log (PRR-062).** Target changes absolutely need logging — both the student-driven ones and the teacher/tenant-driven ones, but especially the latter. If a teacher bulk-adds a target to 30 students and one of them disputes it, the audit trail is load-bearing for Ministry reporting and parent inquiries (EPIC-PRR-C). The event store already captures `ExamTargetAdded|Updated|Archived`, but the audit-log projection needs `actor_role` and `source` fields on every event envelope (event-sourced RTBF, ADR coming from PRR-003a, will need this too for crypto-shredding correctness). **Who can edit a student's targets post-classroom-assignment?** Student: their own. Teacher of their classroom: classroom-sourced targets only. Institute admin: any target within their institute. Platform admin: break-glass with audit. Parent: **never** (EPIC-PRR-C is read-only on plan data — do not open a write path). Write this down in ADR-0049 or it gets invented incorrectly by the first implementer.

## Section 10 positions

**#5 classroom-assigned targets — v1 or v2?** v2 for UI and workflow; v1 for the data model. Ship the `Source` discriminator + `AssignedById` + `Classroom.DefaultExamTargetTemplate` columns/events in v1, gated off in UI. Zero cost to defer the UI, high cost to retrofit the schema.

**#3 max targets cap — 4 or higher?** 4 is wrong. Real Grade-12: Math 5U + Physics 5U + English 5U + CS 5U + Psychometry = 5. Bagrut retake + next-year plan = 2 Bagrut Math entries with different deadlines. Cap at 6 server-side, soft-warn at 4 in UI. Hard cap exists to bound scheduler cost, not to shame ambitious students.

## Recommended new PRR tasks

1. **PRR-217 — Tenant-catalog overlay model (v1 schema, v2 UI).** Global catalog + per-tenant additive/subtractive overlay, `catalog_version` pinned on each `ExamTargetAdded` event. Ship schema + empty overlay in v1. Tenant admin UI deferred. Owner: human-architect. Priority P1 — not P0 because empty overlay works, but shipping without the shape is a Phase 2 retrofit.
2. **PRR-218 — `ExamTarget.Source` discriminator + `AssignedById` provenance.** Add to data model, events, and serialization. v1 defaults to `Source=Student`. Owner: kimi-coder after ADR-0049 is signed. Priority P0 — cannot retrofit cheaply.
3. **PRR-219 — Classroom default-target template + roster-import target assignment.** `Classroom.DefaultExamTargetTemplate`; on roster join, auto-create a classroom-sourced ExamTarget. Extends PRR-021. Owner: kimi-coder. Priority P1.
4. **PRR-220 — Audit log target-change coverage.** Extend PRR-062 projection to cover ExamTarget events with `actor_role`, `source`, `tenant_id`, `reason`. Owner: kimi-coder. Priority P1.
5. **PRR-221 — Grade-passback eligibility flag on catalog + passback filter by source.** Extend PRR-037 ADR. Owner: human-architect. Priority P1.

## Blockers / non-negotiables

- **Blocker (ADR-0001 alignment):** `ExamTarget` must reference the owning `EnrollmentId` when the target is classroom- or tenant-sourced. Bare `StudentId` breaks tenant-scope filtering when a student is in two institutes both running Bagrut Math. Brief says "scoped per-student, inherits tenant isolation via the aggregate" — that is correct for student-sourced targets and wrong for the other two sources. Non-negotiable: `ExamTarget` aggregate must carry `EnrollmentId?`.
- **Non-negotiable (ADR-0048 / shipgate):** "X days to Bagrut" anywhere in the new /settings/study-plan page will trip the countdown scanner. Coordinate with ethics lens — brief already flags this but implementers won't remember.
- **Non-negotiable (no-stubs):** section-4 SAT + PET content commitment is real; a catalog entry without a resolvable `syllabusReferenceId` pointing to actual items is a stub and fails the 2026-04-11 ban.

## Questions back to decision-holder

1. Confirm classroom/tenant-sourced targets are **read-only to the student** (cannot archive their own classroom assignment)? Alternative: student-initiated archive queued for teacher approval.
2. For the retake case (two `BAGRUT_MATH 5U` targets with different deadlines), do we treat them as separate `ExamTarget`s or one target with a history? Separate is cleaner for events; one-with-history matches how students think.
3. `Classroom.DefaultExamTargetTemplate` — does it back-apply to existing students in the classroom on edit, or only to new joiners? (Silent back-apply is a dark pattern; explicit "apply to existing" confirmation is correct.)
4. Parent visibility of classroom-sourced targets vs student-sourced — same default, or different? (Suggest: classroom-sourced visible by default, student-sourced per-consent, aligned with EPIC-PRR-C.)
