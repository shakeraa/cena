# MULTI-TARGET-EXAM-PLAN-001: Multi-Target Student Exam Plan — Persona Discussion Brief

> **Status**: Discussion draft — pre-ADR, pre-task-split.
> **Decision captured (2026-04-21)**: Cena will model a student's learning plan as a **list of exam targets**, not a single target. Ship at Launch — no single-target → multi-target migration dance post-launch.
> **Next**: run the persona-lens review (educator, cogsci, privacy, ethics, a11y, enterprise, ministry, sre, redteam, finops) against this brief, then collapse findings into ADR-0049 + EPIC-PRR-F.
> **Related**: [PRR-148 (marked Done, false-done on onboarding wiring)](../../tasks/pre-release-review/done/TASK-PRR-148-student-input-ui-for-adaptivescheduler-deadline-weekly-ti.md), orphaned components [ExamPlanStep.vue](../../src/student/full-version/src/components/onboarding/ExamPlanStep.vue), [SyllabusMap.vue](../../src/student/full-version/src/components/onboarding/SyllabusMap.vue), [OnboardingCatalogPicker.vue](../../src/student/full-version/src/components/OnboardingCatalogPicker.vue).

---

## 1. Problem Statement

The current onboarding wizard ([onboarding.vue](../../src/student/full-version/src/pages/onboarding.vue)) collects role, language, a diagnostic quiz, and self-assessment. It does **not** collect:

1. Which exam(s) the student is preparing for.
2. At what level/track (e.g. Bagrut Math 3U vs 4U vs 5U).
3. When each exam is due.
4. How much time the student will give each exam per week.

Seed users carry these as Firebase custom claims (`grade`, `track`), but a real new signup has no way to set them. The `AdaptiveScheduler.SchedulerInputs` VO (src/actors/Cena.Actors/Mastery/AdaptiveScheduler.cs) has `DeadlineUtc` and `WeeklyTimeBudget` fields but assumes **one target**.

`StudentPlanConfig` from [PRR-148](../../tasks/pre-release-review/done/TASK-PRR-148-student-input-ui-for-adaptivescheduler-deadline-weekly-ti.md) codified that single-target assumption. It's wrong for a large fraction of real users.

## 2. Persona Reality Check

| Persona | Grade | Track | Target exam(s) |
|---|---|---|---|
| Grade-11 Israeli student | 11 | 5U | Bagrut Math 5U (Jun 2027) + Bagrut Physics 5U (Jun 2027) |
| Grade-12 student | 12 | 5U / 4U varies | Bagrut Math 5U (Jun 2026) + Psychometry (Aug 2026) |
| Gap-year student | n/a | n/a | Psychometry (Jul 2026) |
| Self-learner | n/a | n/a | SAT (quarterly) |
| Adult / career-switcher | n/a | n/a | Psychometry (Dec 2026) |
| Bagrut retake candidate | n/a | 5U (math only) | Bagrut Math 5U retake (Jan 2026) |

Three of six personas have **no meaningful "grade"**. Grade is a per-exam attribute (Bagrut-11 ≠ Bagrut-12 syllabus coverage), not a student attribute. Two personas have **multiple concurrent targets**.

## 3. Core Decision (this brief is the record)

**Student plan is a list of `ExamTarget` records. There is no `grade` on the student.**

```csharp
public record ExamTarget(
    ExamTargetId Id,
    ExamCode Exam,              // BAGRUT_MATH | BAGRUT_PHYSICS | ... | PSYCHOMETRY | SAT
    TrackCode? Track,           // "3U" | "4U" | "5U" | null for non-Bagrut
    DateTimeOffset Deadline,    // absolute UTC; the student picks a sitting
    int WeeklyHours,            // absolute hours/week dedicated to this target (1..40)
    DateTimeOffset CreatedAt,
    DateTimeOffset? ArchivedAt  // soft-archive after the exam has passed or been retired
);

public record StudentPlan(IReadOnlyList<ExamTarget> Targets);
```

Events on StudentActor successor aggregate (ADR-0012): `ExamTargetAdded`, `ExamTargetUpdated`, `ExamTargetArchived`.

## 4. Exam Catalog (MVP)

Catalog is **server-driven**, i18n'd, versioned, served via `GET /api/catalog/exams`. Per [ADR-0001](../adr/0001-tenant-isolation.md), globally scoped (no tenant variation at Launch) but the endpoint carries a tenant header for future override.

### Included (Launch)

| `ExamCode` | Exam | Track options | Sitting windows (per year) |
|---|---|---|---|
| `BAGRUT_MATH` | Bagrut Math | 3U, 4U, 5U | Jan, Jun, Aug |
| `BAGRUT_PHYSICS` | Bagrut Physics | 5U | Jun, Aug |
| `BAGRUT_CHEMISTRY` | Bagrut Chemistry | 5U | Jun, Aug |
| `BAGRUT_BIOLOGY` | Bagrut Biology | 5U | Jun, Aug |
| `BAGRUT_ENGLISH` | Bagrut English | 3U, 4U, 5U | Jan, Jun |
| `BAGRUT_CS` | Bagrut Computer Science | 5U | Jun |
| `PSYCHOMETRY` | Psychometric Entrance Test (PET) | — | Apr, Jul, Sep, Dec |
| `SAT` | SAT | — | Quarterly (Mar/May/Aug/Oct/Dec) |

### SAT + Psychometry — full Launch inclusion (decided 2026-04-21)

User decision: SAT and Psychometry ship **fully functional** on day one, not behind "coming soon" flags. This is a significant content-engineering commitment that extends beyond the plan-aggregate epic. What it requires:

- **SAT item bank**: SAT Math (no-calc + calc), Reading, Writing & Language, Essay (optional). US-curriculum math topics (linear functions, data analysis, passport to advanced math, etc.) — different scope from Bagrut. English-primary content (aligns with the Language Strategy memory: English primary, Arabic/Hebrew secondary).
- **Psychometry (PET) item bank**: four sections — Quantitative Reasoning (Hebrew/Arabic/English versions), Verbal Reasoning (Hebrew native + Arabic native variants; not just translation), English, and the combined Quantitative+Verbal score logic. PET verbal cannot be machine-translated — the sections are language-native.
- **Per-target syllabus reference IDs** in the catalog (section 4) → must resolve to real syllabi for both SAT and PET in the rubric DSL ([PRR-033](../../tasks/pre-release-review/TASK-PRR-033-ministry-bagrut-rubric-dsl-version-pinning-per-track-sign-of.md)) and coverage matrix ([PRR-072](../../tasks/pre-release-review/post-launch/TASK-PRR-072-item-bank-coverage-matrix-vs-bagrut-syllabus.md)) from day one.
- **CAS oracle ([ADR-0002](../adr/0002-sympy-correctness-oracle.md))** applies to SAT math + PET quantitative exactly as to Bagrut math — SymPy verifies every student-facing item.
- **No-stubs rule**: per the user's 2026-04-11 ban on stub/canned/fake backend, SAT + PET sessions must produce real adaptive output from day one. No placeholder items, no "10 seed questions and hope nobody notices."
- This **blocks Launch on SAT + PET content readiness**, not just on the plan-aggregate epic. A parallel content-engineering epic (separate from EPIC-PRR-F) must track item-bank authoring for both.

### Explicit non-goals (Launch)

- ACT, GCE A-Levels, IB — no item-bank coverage and no user-demand signal.
- Other Bagrut subjects (Hebrew, Arabic, History, Tanakh, etc.) — **not at Launch**; item bank doesn't cover.
- Israeli-specific non-Bagrut assessments (Mekhina, army-prep) — not at Launch.

### Catalog metadata per entry

- Display name (en, he, ar) — marked with `<bdi dir="ltr">` around track codes per math-always-LTR rule.
- Per-track syllabus reference ID (ties to PRR-072 coverage matrix + PRR-033 rubric DSL, both of which must become per-target).
- Default lead-time recommendation (6 months for Bagrut, 3 months for Psychometry) — used only to pre-fill the date picker, not enforced.

## 5. Onboarding UX Change

### New flow

```
welcome → role → exam-targets → per-target-plan → language → diagnostic → self-assessment → confirm
```

Two new steps. Total length goes from 6 → 8 steps. This is the primary UX cost.

### Step `exam-targets` (multi-select from catalog)

- Catalog grouped by family: **Bagrut** / **Standardized tests**.
- Cards with exam name + icon. Tap to select; checkmark.
- Minimum selection: **1**. Maximum: **4** (keeps the per-target step manageable; hard cap to avoid "I selected 12 exams" edge cases).
- Search/filter box for long catalogs.
- Arabic/Hebrew RTL: card layout mirrors; `<bdi dir="ltr">` around "3U/4U/5U" track codes.
- "Not sure yet? [Skip]" — creates a placeholder target that the student is prompted to complete on first home visit. Prevents abandoning onboarding; aligned with honest-UX ("Labels match data" — we don't pretend they picked a target).

### Step `per-target-plan` (loop, one page per selected target)

For each selected target:
1. **Track picker** (if catalog entry has `trackOptions.length > 1`) — radio: 3U / 4U / 5U. Helper copy: "Not sure? Pick the unit-count your school assigned."
2. **Deadline** — date picker, pre-filled with the next natural sitting. Min = today + 7d (matches server validation in PRR-148 `StudyPlanSettingsEndpoints`). Max = today + 3y.
3. **Weekly hours** — slider 1..40h. Default = 8h. Below the slider: a live "**Total across all targets: X h/week**" counter with a gentle warning if total > 40h/week ("This is a lot — most students find 15–25h sustainable").
4. Optional per-target note (≤200 chars) — e.g. "retake, got 85 last time".

### Existing steps unchanged

- `role`: unchanged. Kept because role still affects copy/scheduling defaults (test-prep = tighter deadlines on average).
- `language`: unchanged.
- `diagnostic`: **extends naturally** — `DiagnosticQuiz.vue` already takes `subjects[]`; pass the selected exam targets' subjects, runs a single unified diagnostic across them. If total target count ≥ 3, offer per-target-skip ("just do math now, physics later").
- `self-assessment`: unchanged.
- `confirm`: summary now lists each target with its plan.

### Edit-later settings

New route `/settings/study-plan` (replaces PRR-148's half-shipped `StudyPlanSettings.vue`):
- Table of active targets with inline edit.
- "Add a target" button → reopens exam-targets + per-target-plan flow for the single new target.
- "Archive target" (soft-archive post-exam; preserves history). No hard-delete.

## 6. Scheduler Impact

`SchedulerInputs` gains `ActiveExamTargetId`. At session start, the picker runs:

1. **Deadline-proximity rule**: if any target's deadline is within 14 days, lock to that target (exam-week focus).
2. Else **weighted round-robin**: weights = `WeeklyHours[i] / sum(WeeklyHours)`. Picker is deterministic given RNG seed = hash(userId, dayOfYear).
3. **Student override**: "Study [different exam] instead today" button. Logs an `ExamTargetOverrideApplied` event for later analysis. No penalty, no nudging.

**Out of scope for Launch**: multi-target interleaving *within a single session*. A session is single-target. Cross-target interleaving across sessions is enough to get the spacing benefit.

### What the scheduler does not do

- Does **not** auto-balance weekly hours based on diagnostic performance. The student set the allocation; the scheduler respects it. Deviating from declared plan without asking is a dark pattern ([ADR-0048](../adr/0048-exam-prep-time-framing.md)).
- Does **not** refuse to schedule a target whose deadline is past — it archives it on the next session-start with a toast ("Bagrut Math 5U sitting is past; target archived").

## 7. Data Migration

Existing seed users + the tiny PRR-148 single-target population:

- On first login post-deploy, read legacy `StudentPlanConfig`. If present, upcast:
  `Targets = [ExamTarget(inferredExam, inferredTrack, legacyDeadline, legacyWeeklyHours, createdAt=legacyCreatedAt)]`.
- `inferredExam` comes from Firebase `grade`+`track` claims when present; defaults to `BAGRUT_MATH` otherwise.
- One-shot, idempotent, logged. No double-conversion.

Legacy `StudentPlanConfig` VO stays readable for 1 release cycle, then removed.

## 8. Tenant & Privacy

- `ExamTarget` is scoped per-student; inherits tenant isolation via the aggregate.
- **No PII.** Exam codes, tracks, dates, hours — no free-text identity markers.
- Per-target note field is free-text ≤200 chars. Subject to [ADR-0022](../adr/0022-ban-pii-in-llm-prompts.md) scrubbing when surfaced in LLM prompts (e.g. hint generation may reference the target; it must not echo the free-text note without PII scrub).
- Archived targets retained indefinitely on the student (they're academic history). **Not** subject to misconception-data 30-day rule ([ADR-0003](../adr/0003-misconception-session-scope.md)) because this isn't misconception data — it's declared-plan data.
- Right-to-be-forgotten ([ADR coming from PRR-003a](../../tasks/pre-release-review/TASK-PRR-003a-adr-event-sourced-right-to-be-forgotten.md)): `ExamTarget*` events are erasable via the same crypto-shredding pathway.

## 9. Persona-Lens Discussion Prompts

Each lens should answer the prompts below. File findings under `pre-release-review/reviews/persona-<lens>/multi-target-exam-plan-findings.md` using the same YAML-ish schema as existing axis files.

### 9.1 persona-educator (Ministry-curriculum + teacher workflow)

- Is the exam catalog **complete** for the Launch market, or are there critical Bagrut subjects missing for teacher classrooms (Hebrew language? History?)?
- Is "3U/4U/5U" the right abstraction, or does the Ministry use a different labeling that we'd be misaligned with?
- Does the deadline picker surface **all** legitimate Bagrut sitting windows (including the מועד ב' retake window)?
- If a teacher assigns a whole class to Bagrut Math 5U, does the class roster override the student's personal picks, or coexist? (Interacts with PRR-058 teacher accommodations + PRR-037 grade-passback policy.)
- Are the "not sure yet? skip" and "add later" paths acceptable under teacher workflows, or will teachers want a locked-down mode where plans can't be edited mid-term?

### 9.2 persona-cogsci (Pedagogy, mechanics, learning science)

- Is weighted round-robin across targets **interleaving**, or is it just alternation? Does the cognitive-science benefit of interleaving require within-session rotation (which we're deferring)?
- Is the single-target-per-session simplification losing the desirable-difficulty effect for students with 2+ targets?
- Diagnostic quiz spans all targets at onboarding — does this create a cold-start bias where the student's first target's diagnostic shapes session allocation unfairly?
- Exam-week lock (14-day proximity rule) — is this evidence-backed (massed practice before exam) or a dark-pattern countdown ([ADR-0048](../adr/0048-exam-prep-time-framing.md))? Draw the line.
- How does this interact with [retrieval-prompt reframe (PRR-066)](../../tasks/pre-release-review/post-launch/TASK-PRR-066-retrieval-prompt-reframe-formulas-application-triggers.md)?

### 9.3 persona-ethics (Dark-pattern watch, intrinsic motivation)

- Is per-target "weekly hours" a benign study-plan input or a loss-aversion surface ("you're falling behind your 10h/week plan")?
- The total-hours counter with warning at >40h — is this paternalism, or honest accommodation of the time budget?
- Deadline countdown UI in the settings page — does this violate the [Bagrut countdown ban (PRR-019)](../../tasks/pre-release-review/done/TASK-PRR-019-ship-gate-ban-on-bagrut-score-prediction-countdown-copy-scan.md)? If we show "32 days to Bagrut Math" anywhere, the shipgate scanner will flag it. Is there a ship-safe way to surface urgency?
- Target-archive UX post-exam — is there a version of "you completed your Bagrut Math plan" that's celebratory without being reward-addictive (loss-aversion mirror)?

### 9.4 persona-a11y (Accessibility)

- Date picker in three locales (en/he/ar) — does our Vuetify + i18n stack actually deliver a correct RTL Hebrew/Arabic date picker, or does it silently fall back to LTR? (User caught reversed-equation bug previously; this is the same failure mode.)
- Slider for weekly hours — screen-reader-announcable with unit, min/max, current value?
- Multi-step wizard with 8 steps — keyboard-only flow viable? Does every step have a logical back target?
- Arabic/Hebrew numerals preference ([PRR-032](../../tasks/pre-release-review/TASK-PRR-032-ship-arabic-rtl-math-delta-notation-profile-numerals-toggle.md)) applied to the weekly-hours slider value?
- Reduced-motion ([PRR-070](../../tasks/pre-release-review/post-launch/TASK-PRR-070-reduced-motion-toggle-respected-across-animations.md)) respected in step transitions?

### 9.5 persona-enterprise (Multi-institute, tenancy)

- Does the exam catalog need to vary per tenant in Post-Launch (e.g. Saudi tenants need a non-Bagrut catalog)? If so, is the catalog API shape future-proof?
- Classroom-code onboarding path ([ExamPlanStep.vue](../../src/student/full-version/src/components/onboarding/ExamPlanStep.vue) currently hardcodes `classroomCode: null`) — how does a classroom-assigned plan interact with student-chosen targets?
- Tenant-admin-forced plans (e.g. "all students in this school must target Bagrut Math 5U") — is this a Launch or Post-Launch capability? If Post-Launch, do we need a `source: student|classroom|tenant` discriminator on `ExamTarget` now so we don't retrofit it?

### 9.6 persona-ministry (Ministry-facing, Bagrut compliance)

- Does the catalog's `BAGRUT_MATH` + `5U` label match the Ministry's canonical exam code? Misalignment breaks reporting.
- Per-target syllabus reference (section 4) needs a Ministry curriculum version pin (PRR-033). Who owns catalog-version-to-curriculum-version mapping?
- Bagrut moadim (sittings) — is a simple date picker sufficient, or does the student need to pick a **named sitting** (קיץ תשפ"ו / מועד ב') that the Ministry uses in reporting?
- מגן / magen grade (the teacher-assessed component): is this a per-target concept we should capture, or out of scope?

### 9.7 persona-sre (Reliability, ops)

- Catalog endpoint — is this a hot path on every session-start? Needs CDN / edge cache? What's the refresh strategy when we add a new exam to the catalog?
- Migration (section 7) one-shot on first login — what's the failure mode if this throws mid-upcast? Is the student locked out of the app, or degrades gracefully?
- Event volume: `ExamTargetUpdated` on every slider change vs debounced batch. What's the expected events-per-student per year? Is this sane for the event store?
- Exam-day freeze window ([PRR-016](../../tasks/pre-release-review/TASK-PRR-016-publish-exam-day-slo-change-freeze-window-in-cd.md)) — deploying a catalog change during Bagrut week — allowed or blocked?
- Time-zone correctness — deadlines are UTC but students are in Israel (IST) or diaspora (varies). Does the UI + scheduler respect the student's local date boundary, or do we have off-by-one-day bugs at midnight?

### 9.8 persona-redteam (Abuse, tampering, malicious input)

- `ExamTarget.WeeklyHours = 10000` — is the server validator tight, or can a malicious client submit absurd values that break scheduler math?
- Multi-target cap of 4 — client-enforced or server-enforced? Can a scripted client create 10000 targets?
- Catalog API — unauthenticated? If so, can an attacker fingerprint user base by sampling it?
- Free-text target note — XSS/prompt-injection surface when surfaced to LLM? ([PRR-022 ban-pii-in-llm-prompts](../../tasks/pre-release-review/done/TASK-PRR-022-ban-pii-in-llm-prompts-lint-rule-adr.md))
- IDOR: can student A read/edit student B's exam targets? ([PRR-009](../../tasks/pre-release-review/TASK-PRR-009-parent-child-claims-binding-idor-enforcement-helper.md))

### 9.9 persona-privacy (Data minimization, consent)

- Is collecting "planning to take SAT" a sensitive signal that, combined with locale, could infer immigration intent or diaspora identity? Do we need a consent surface, or is this plainly benign?
- Exam-target archive: we're retaining academic-plan history indefinitely. Is that minimal, or do we need a "forget my plan history" user control?
- Per-target free-text note (≤200 chars) — high PII risk ("retaking because my father died last year..."). Is the note field worth the privacy exposure, or should we drop it?
- Does this data flow to the parent dashboard ([EPIC-PRR-C](../../tasks/pre-release-review/EPIC-PRR-C-parent-aggregate-consent.md))? If parents can see their child's exam plan, is that ok for adult-age students (18+, post-army)?

### 9.10 persona-finops (Cost, LLM spend)

- Does per-target personalization multiply LLM prompt-generation cost per session?
- Hint-generation prompts ([ADR-0026 3-tier routing](../adr/0026-llm-three-tier-routing.md)): does per-target context (syllabus, track, deadline) inflate tokens into Tier-3 Sonnet/Opus range more often?
- Prompt cache hit rate ([PRR-047](../../tasks/pre-release-review/done/TASK-PRR-047-llm-prompt-cache-enforcement-hit-rate-slo.md)): multi-target students have wider context variation — does cache hit rate drop?
- Does the server-driven catalog add meaningful cost vs. hardcoding? (Answer: probably not — it's a tiny payload, but confirm.)

## 10. Open Product Questions (need your call before ADR draft)

1. ~~**SAT at Launch?**~~ **Resolved 2026-04-21**: SAT + Psychometry both ship fully functional at Launch. See section 4 for the commitments this creates. Blocks launch on content-engineering readiness for both.
2. **Per-target free-text note field?** Section 5 step 4 includes it; persona-privacy 9.9 flags the risk. Drop entirely, or keep with aggressive PII scrub?
3. **Maximum targets cap — 4 or higher?** Real students may legitimately do 3 Bagrut subjects + Psychometry = 4. Retake candidates may want 5. Where's the ceiling?
4. **"Not sure yet, skip" during onboarding** — creates an incomplete plan. Is it better to require at least one target (friction, potential abandonment) or allow skip (incomplete state, need a nag)?
5. **Classroom-assigned targets** (persona-enterprise 9.5) — Launch or Post-Launch?
6. **Parent visibility of exam plan** (persona-privacy 9.9) — default visible, default hidden, or per-student-consent?

## 11. What This Replaces / Retires

- **Retires** PRR-148's single-target `StudentPlanConfig` VO. PRR-148 is marked Done but its DoD is not met on onboarding wiring — this epic supersedes it.
- **Retires** orphaned components: [OnboardingCatalogPicker.vue](../../src/student/full-version/src/components/OnboardingCatalogPicker.vue) (replaced by the new catalog-driven step), [SyllabusMap.vue](../../src/student/full-version/src/components/onboarding/SyllabusMap.vue) (unless persona-educator argues it's still needed in per-target-plan).
- **Wires in** [ExamPlanStep.vue](../../src/student/full-version/src/components/onboarding/ExamPlanStep.vue) — but split into `ExamTargetsStep` (select) + `PerTargetPlanStep` (configure). Current single-screen component is insufficient for the list case.

## 12. Proposed Task Decomposition (post-persona-review)

*Pending persona findings that may add/remove tasks:*

1. **ADR-0049** — Multi-target student exam plan (data model + "grade is per-target" principle).
2. **EPIC-PRR-F** — Multi-target onboarding + plan aggregate (umbrella, tracks children below).
3. **PRR-211** — Exam catalog service + i18n + versioning.
4. **PRR-212** — `StudentPlan` aggregate events + one-shot migration from legacy `StudentPlanConfig`.
5. **PRR-213** — Onboarding steps `exam-targets` + `per-target-plan` (replaces orphaned components).
6. **PRR-214** — `/settings/study-plan` edit UI.
7. **PRR-215** — Scheduler `ActiveExamTargetId` + session-start selection policy.
8. **PRR-216** — Close out PRR-148 honestly (mark superseded).
9. **Coordinate-only** — PRR-072 coverage matrix + PRR-033 rubric DSL become per-target (annotate, no new task).

## 13. Next Step

Run personas against sections 9.1–9.10. File findings under each `pre-release-review/reviews/persona-<lens>/` folder. Decision-holder (Shaker) resolves the section-10 open questions and section 9 conflicts. Then draft ADR-0049 + EPIC-PRR-F + the 7 PRR tasks.

---

**History**:
- 2026-04-21: Draft created after `/pages/onboarding.vue` audit surfaced the missing grade+exam-target gap; user decided full multi-target over honest-single-target-Bagrut-only Launch.
- 2026-04-21: User resolved open question #1 — SAT and Psychometry both ship fully functional at Launch, not behind "coming soon" flags. Adds a parallel content-engineering epic as a launch blocker; extends CAS oracle + no-stubs rule to both exam families.
- 2026-04-21: 10-persona review completed. Verdicts: 2 red (a11y, ministry), 1 yellow-leaning-red (privacy), 7 yellow. Synthesis below revises the data model and task slate before ADR lock.

---

## 14. Persona Review Synthesis (2026-04-21)

### 14.1 Verdict roll-up

| Persona | Verdict | Findings file |
|---|---|---|
| educator | yellow | [multi-target-exam-plan-findings.md](../../pre-release-review/reviews/persona-educator/multi-target-exam-plan-findings.md) |
| cogsci | yellow | [multi-target-exam-plan-findings.md](../../pre-release-review/reviews/persona-cogsci/multi-target-exam-plan-findings.md) |
| ethics | yellow | [multi-target-exam-plan-findings.md](../../pre-release-review/reviews/persona-ethics/multi-target-exam-plan-findings.md) |
| a11y | **red** | [multi-target-exam-plan-findings.md](../../pre-release-review/reviews/persona-a11y/multi-target-exam-plan-findings.md) |
| enterprise | yellow | [multi-target-exam-plan-findings.md](../../pre-release-review/reviews/persona-enterprise/multi-target-exam-plan-findings.md) |
| ministry | **red** | [multi-target-exam-plan-findings.md](../../pre-release-review/reviews/persona-ministry/multi-target-exam-plan-findings.md) |
| sre | yellow | [multi-target-exam-plan-findings.md](../../pre-release-review/reviews/persona-sre/multi-target-exam-plan-findings.md) |
| redteam | yellow | [multi-target-exam-plan-findings.md](../../pre-release-review/reviews/persona-redteam/multi-target-exam-plan-findings.md) |
| privacy | yellow → red | [multi-target-exam-plan-findings.md](../../pre-release-review/reviews/persona-privacy/multi-target-exam-plan-findings.md) |
| finops | yellow | [multi-target-exam-plan-findings.md](../../pre-release-review/reviews/persona-finops/multi-target-exam-plan-findings.md) |

### 14.2 Convergent findings (multi-lens agreement — load-bearing)

1. **Kill the free-text per-target note** *(ethics + privacy + redteam + finops converge)*. Replace with a closed enum `ReasonTag ∈ {Retake, NewSubject, ReviewOnly, Enrichment}`. Eliminates PII exposure, prompt-injection surface, and scrub-cost.
2. **Sittings are named tuples, not free dates** *(educator + ministry)*. Replace `Deadline: DateTimeOffset` with `SittingCode` → `{AcademicYear, Season, Moed}` tuple that dereferences to a canonical date + Ministry סמל שנה שאלון mapping. Free dates collapse the moed taxonomy (מועד א / ב / ג / מיוחד) and misalign with every Ministry reporting artefact downstream.
3. **Ministry numeric codes are the primary key**, not display labels *(ministry emphatic)*. `BAGRUT_MATH + 5U` is a label; the real key is `{035581 | 035582 | 035583}` — Math 5U is **three** שאלונים, not one. Catalog entries carry `ministrySubjectCode` + `ministryQuestionPaperCodes[]`.
4. **Rename `PSYCHOMETRY` → `PET`** *(ministry)*. NITE-owned, not Ministry-of-Education — different regulator, different compliance path.
5. **`ExamTarget.Source ∈ {Student, Classroom, Tenant}` + `AssignedById` + `EnrollmentId?` in Launch schema** *(educator + enterprise converge)*. Classroom-write UI defers to Post-Launch, but the event schema discriminator must land at Launch to avoid a post-launch event-sourced migration.
6. **Targets cap = 5, not 4** *(educator=5, enterprise=6)*. Real Grade-12 students exceed 4 (3 STEM + 1 humanity + 1 standardized). Compromise: server cap 5, soft-warn at 4.
7. **Track enum extended** *(educator)*: include `"2U"` (mandatory humanities baseline); English uses Modules A–G, not units — add `ModuleCode` as parallel track-shape.
8. **Exam-week lock stays silent** *(cogsci + ethics agree)*. 14-day proximity rule is scheduler behavior, not UX copy. No countdown, no label, no "days-remaining" identifier anywhere in `src/`. Extend shipgate scanner to catch `daysUntil`, `countdown`, `streak`.
9. **Diagnostic must be per-target blocks, not unified** *(cogsci)*. Unified diagnostic creates order-effect bias. 6-8 items per target + shared 3-item warmup; priors reported per-target.
10. **Mastery state keyed on `skillId`, not `(targetId, skillId)`** *(cogsci blocker)*. Otherwise Bagrut-Math ↔ PET-Quant overlap gives phantom weakness. Skills are catalog-global.
11. **Migration one-shot is unsafe** *(sre blocker)*. Staged feature-flagged upcast with retry + DLQ, not first-login-throws-everyone-out.
12. **Catalog needs `GlobalCatalog + TenantCatalogOverlay` at Launch** *(enterprise)*. Header-only tenancy is theatre; schema must be overlay-ready from day one.
13. **Archived-target retention = 24 months, not "indefinite"** *(privacy)*. PPL Amendment 13 + GDPR Art. 5(1)(e) purpose-limitation. User-extendable opt-in.
14. **RTBF cascade** *(privacy)*. `ExamTarget*` events crypto-shredded + all downstream derivations (coverage matrix, mastery projection, scheduler state) purged. Ties to PRR-003a.
15. **Aggregate invariants server-enforced** *(redteam)*. `sum(Targets.WeeklyHours) ≤ 40`, `count(Targets) ≤ 5`, `target.Archived ⇒ immutable`. Not UI-only.
16. **VDatePicker RTL is unverified** *(a11y red)*. Hebrew calendar has no Vuetify coverage; Arabic-Umm-al-Qura is opt-in. Prototype in all three locales + screen reader before ADR lock, or stay with native `<input type="date">` wrapped `<bdi>`.
17. **Per-target-plan nested-step SR announcement undefined** *(a11y red)*. Flatten to top-level steps or define `aria-live` source-of-truth. Not defer-able.
18. **PRR-032 is a ghost reference** *(a11y)*. The brief cites `numeralsPreference` from PRR-032 but the PRR-032 task file doesn't exist and `onboardingStore.ts` has no `numeralSystem` field. Create the task or stop citing it.
19. **Pre-existing streak-leak in `src/student/full-version/src/pages/progress/time.vue:40-54`** *(ethics)*. Renders `dayStreakCount` — a GD-004 / ADR-0048 violation that the shipgate scanner at `scripts/shipgate/scan.mjs:26` should catch but isn't. Remove before PRR-F merges.
20. **PRR-053 capacity plan is stale** *(sre)*. SAT+PET at Launch turns a 2-spike (Jun/Aug) into a 7-window compound calendar. Amend before launch.
21. **Prompt cache hit rate at PRR-047 SLO floor** *(finops)*. 4-target variation drops hit rate from ~85% → ~68-72% (SLO = 70%). Observability per-target required; no action if we stay above floor, but instrumented.
22. **SAT+PET content-engineering budget unowned** *(finops)*. ~$10-15k one-shot (PET Hebrew/Arabic verbal $8-12k dominates) + $500-1500/quarter refresh. Must be named owner + approved line before EPIC-PRR-G starts.

### 14.3 Resolved open questions (sections 10 + persona positions)

| Question | Resolution | Source |
|---|---|---|
| 10.1 SAT-at-Launch? | **Yes, full** | user 2026-04-21 |
| 10.2 Free-text note? | **Drop, replace with ReasonTag enum** | ethics + privacy + redteam + finops |
| 10.3 Max targets cap? | **5 server, soft-warn at 4** | educator + enterprise |
| 10.4 Skip-during-onboarding? | **Allowed, conditional on classroomCode absence; strict nag-copy guardrails** | ethics + educator |
| 10.5 Classroom-assigned Launch/Post-Launch? | **Schema at Launch, teacher UI Post-Launch** | educator + enterprise |
| 10.6 Parent visibility? | **Default hidden 13+, visible <13, student-grants at 18+** | privacy |

### 14.4 Revised task slate (supersedes section 12)

**Epics**:
- **EPIC-PRR-F** — Multi-target onboarding + plan aggregate (engineering umbrella).
- **EPIC-PRR-G** — SAT + PET content engineering (content-eng umbrella; launch-blocker parallel).

**P0 (blocker) tasks**:
- PRR-217 — ADR-0049 Multi-target exam plan + Ministry codes + sitting-tuple (foundation ADR).
- PRR-218 — StudentPlan aggregate events (source, EnrollmentId, sittingCode, ReasonTag, no free-text).
- PRR-219 — Migration safety net (feature-flagged staged upcast + retry + DLQ).
- PRR-220 — Catalog service with Global + TenantOverlay + offline fallback + CDN runbook.
- PRR-221 — Onboarding UI exam-targets + per-target-plan steps (with a11y scaffolding + VDatePicker RTL prototype-or-fallback).
- PRR-222 — Skill-keyed mastery state (not target-scoped) + dedup invariant.
- PRR-223 — RTBF cascade for ExamTarget + derived projections.
- PRR-224 — Shipgate scanner v2: ban `streak`, `countdown`, `daysUntil`, `days-remaining`, `timeLeft` identifier-names + amber/red CSS in onboarding nag.
- PRR-225 — Remove pre-existing streak leak in `src/student/full-version/src/pages/progress/time.vue`.

**P1 tasks**:
- PRR-226 — Scheduler ActiveExamTargetId + silent exam-week lock + TZ-safe determinism.
- PRR-227 — `/settings/study-plan` edit UI (archive + add + edit).
- PRR-228 — Per-target diagnostic blocks (replaces unified diagnostic).
- PRR-229 — 24-month post-archive retention policy + user-extend opt-in.
- PRR-230 — Parent visibility default-hidden 13+ (consent surface).
- PRR-231 — Amend PRR-053 capacity plan (SAT+PET 7-window compound calendar).
- PRR-232 — Realize PRR-032 (numerals preference task — create the ghost reference).
- PRR-233 — Prompt cache hit SLO per target + finops observability.

**P2 tasks**:
- PRR-234 — Close out PRR-148 (mark superseded + delete legacy `StudentPlanConfig` in next cycle).
- PRR-235 — Ministry reporting export endpoint shape (future-proof; deferred implementation, spec only at Launch).

**Deferred (Post-Launch)**:
- Magen/מגן capture — stays deferred. Ministry persona red-flag: we are not source of truth (school is). Capturing a magen value creates false-authority risk. Revisit post-launch only if formal Mashov integration lands with school as authoritative source.

### 14.4.1 Scope expansion 2026-04-21 (user directive: "all options on release day")

Previously-deferred items promoted to Launch + new corpus enabler task added. Honest budget + timeline delta flagged at decision.

**Promoted to Launch**:
- PRR-236 — Classroom-assigned target teacher UI (was Post-Launch schema-only).
- PRR-237 — Within-session cross-target interleaving (was Post-Launch).
- PRR-238 — Retake-cohort surface + retrieval-strength framing (was Post-Launch).

**New catalog extensions (Launch)**:
- PRR-239 — Arab-stream (המגזר הערבי) Bagrut variants — full שאלון code set + content tagging.
- PRR-240 — PET Russian-verbal section — native authoring (not translation).
- PRR-241 — Full Bagrut humanities catalog (Hebrew Lang, Lit, History, Tanakh, Civics, Arabic-L2, Arabic-L1, Islamic Studies).

**New content accelerator (P0)**:
- PRR-242 — Past-Bagrut corpus ingestion. User-proposed 2026-04-21; per memory "Bagrut reference-only" this is legitimate reference material for AI-authored recreations. Leverages existing Phase 1A OCR pipeline. **Cuts EPIC-PRR-G content budget estimate from ~$40-60k → ~$20-30k** (reference-anchored authoring vs. cold generation).

### 14.4.2 Revised budget + timeline

- **EPIC-PRR-F engineering timeline**: ~8 weeks → **~12-14 weeks** with promoted tasks (classroom UI + interleaving + retake).
- **EPIC-PRR-G content-engineering budget**: original ~$10-15k → expansion forecast ~$40-60k → **revised ~$20-30k** with PRR-242 corpus enabler.
- **EPIC-PRR-G content-engineering timeline**: ~8 weeks → **~16-20 weeks** across parallel SMEs, corpus-anchored.
- Content-engineering budget owner + approval still open (brief §14.5 Q4).

### 14.5 Outstanding questions back to decision-holder

From persona findings, items that did NOT converge and still need your call:

1. **Arab-stream (המגזר הערבי) variant question-paper codes** — different numeric codes per stream. Launch covers both or one? *(ministry)*
2. **PET Russian-verbal variant** — olim population. Launch scope or Post-Launch? *(educator)*
3. **Tenant-admin-forced plans under what lawful basis** — when a school admin assigns targets to a student with parent consent only? *(privacy)*
4. **Content-engineering budget line owner + approval** for the $10-15k one-shot on SAT+PET item banks *(finops)*.
5. **Paid-tier pricing floor** given per-student-per-month ceiling of ~$3.30 LLM spend *(finops)*.

These are blocking ADR-0049 draft, not the task files.

### 14.6 Decision record — 2026-04-21 (decision-holder)

All 5 questions resolved. ADR-0049 draft can proceed.

1. **Arab-stream Bagrut variants → A (both at launch).** IL-Hebrew-sector + IL-Arabic-sector variants land together. Content cost 1.8× Hebrew-only, but topic taxonomy is shared and only item pools diverge. Rationale: Arabic is already a first-class locale; shipping without its exam codes would make the localization work symbolic. Catalog service (PRR-220) must carry both variant codes day 1.
2. **PET Russian-verbal → B (post-launch with visible roadmap).** PET as a category is in the launch catalog; Russian-verbal variant ships Q3 2026 with a "Coming Q3" placeholder tile visible in the picker. Document in catalog YAML with `availability: "roadmap"` flag so UI can gray-out + disable select.
3. **Tenant-admin-forced plans — D + C belt-and-braces.** Primary lawful basis: Israeli Compulsory Education Law §4 (statutory). Secondary: explicit parent consent captured per assigned target (revocable). For future non-IL tenants, default to C-only (parent consent). PRR-230 (parent visibility default-hidden 13+) surfaces the per-target consent item in the parent dashboard as revocable. ADR-0049 documents the basis selection logic.
4. **Content-engineering budget — phased, Product owns PO, $15k pre-approved.** Phase order: SAT first (highest non-Bagrut demand signal), then PET Hebrew verbal, then IB Math HL AA/AI. IB gated on validated international-cohort conversion signal. Budget line: `content-eng.exam-catalogs.2026`, Product-team-owned PO.
5. **Paid-tier pricing — recommended defaults, per-institute configurable.**
   - **Defaults (SaaS-global):** $19/month student individual tier; $14/seat institutional (≥20 seats); free-tier 10 sessions/month with tier-2-only LLM (Haiku).
   - **NEW REQUIREMENT:** Super-admin can override all three values per institute in Admin UI. Use case: subsidized regional rollout, enterprise school-network pricing, free-tier expansion for underserved cohorts. Pricing surface is tenant-scoped config with audit trail (every override logs {super_admin_id, institute_id, old_price, new_price, justification, timestamp}).
   - **Effect:** institutes default to the global pricing; overrides appear in both billing + finance dashboards so the override is visible to finance review. Filed as new task **PRR-243** (see §14.7).

### 14.7 Follow-up task added by the Q5 decision

**PRR-243 — Per-institute pricing override (super-admin configurable)**

- Super-admin UI surface (admin app, SUPER_ADMIN-role-gated) with three editable fields: student monthly price, institutional per-seat price (≥N seats), free-tier monthly session cap.
- Audit event `InstitutePricingOverridden_V1` on every change with full old-value / new-value / justification / actor / institute_id / timestamp.
- Architecture ratchet: every pricing-bearing code path must read from `IInstitutePricingResolver.ResolveAsync(instituteId)` — no hard-coded prices; arch test `NoHardcodedPricingTest` scans for literal dollar amounts.
- Arch ratchet: the resolver falls back to global defaults if no override recorded for the institute.
- Blocks on PRR-217 (same as other EPIC-PRR-F tasks — needs the ADR to record the default-pricing constants and the override policy).
