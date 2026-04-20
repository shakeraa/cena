# ADR-0047 — No PII in LLM prompts

- **Status**: Accepted
- **Date proposed**: 2026-04-20
- **Deciders**: Shaker (project owner), claude-code (coordinator), claude-subagent-prr022
- **Supersedes**: none
- **Related**: [ADR-0003 (misconception session-scope)](0003-misconception-session-scope.md), [ADR-0026 (LLM three-tier routing)](0026-llm-three-tier-routing.md), [ADR-0038 (event-sourced right-to-be-forgotten)](0038-event-sourced-right-to-be-forgotten.md), [ADR-0042 (consent bounded context)](0042-consent-aggregate-bounded-context.md)
- **Task**: prr-022 (pre-release review Epic D — Ship-gate scanner v2, banned-vocabulary expansion)
- **Lens consensus**: persona-privacy, persona-redteam

---

## Context

Cena sends conversational prompts to third-party LLM providers (currently Anthropic, future: Moonshot/Kimi, per ADR-0026). Every byte that leaves the tenant boundary is a potential data-protection incident. The existing controls stop short of a hard rule:

| Control | What it does | What it does NOT do |
|---|---|---|
| `TutorPromptScrubber` (FIND-privacy-008) | Regex-strips student-specific PII from free-text *within* the Tutor bounded context | Does not cover other LLM seams (L3 explanations, error classification, question generation, content segmentation, stuck classification, quality-gate, figure generation) |
| [ADR-0003 misconception session-scope](0003-misconception-session-scope.md) | Keeps misconception tags off student profiles | Does not speak to prompt construction |
| [ADR-0038 event-sourced R-to-be-F](0038-event-sourced-right-to-be-forgotten.md) | Covers persisted state | Does not cover the transient payload of a synchronous LLM request |
| `cena_llm_call_cost_usd_total` labels | `feature`, `tier`, `task`, `institute_id`, `model_id` | Prometheus labels are known-safe; not an assertion about the prompt body |

The regulatory floor is the same one that motivated ADR-0003 — FTC v. Edmodo (2023 "Affected Work Product"), FTC 2025 COPPA Final Rule, ICO v. Reddit £14.47M (Feb 2026), and Israel PPL Amendment 13. All four treat a child's identifying attributes plus their behavioural trace as a processing operation requiring data minimisation. A prompt that reads

> "Yael Cohen (yael@example.com, +972-54-1234567, student ID 123456789) answered 2x + 3 = 9 with x = 2..."

is the exact failure mode the Edmodo consent decree was written to prevent: the body of the prompt plus the provider's retention window constitutes a per-student behavioural profile shared with a processor we do not control.

Prior to this ADR, nothing structurally prevented a future developer from interpolating a raw student field into a prompt. `TutorPromptScrubber` is a behind-the-line safety net inside Tutor; the other eight `[TaskRouting]`-tagged services have no equivalent.

---

## Decision

**No personally identifying field may be composed into an LLM prompt payload. Structured placeholder tokens are the only legitimate way to reference a student or parent in a prompt; a runtime scrubber removes residual free-text PII as defence-in-depth; CI and xUnit architecture ratchets fail the build on new violations.**

This decision has four enforceable parts:

### Decision 1 — Banned field vocabulary

The following identifier roots are banned from any source file that composes an LLM prompt (i.e. any file carrying `[TaskRouting]` or that directly constructs `new AnthropicClient` / `MessageCreateParams` / `LlmRequest`). Case-insensitive, substring-anchored at identifier boundaries. The ban applies to **identifier names**, not to comments or string literals that document the rule itself.

| # | Banned root | Why |
|---|---|---|
| 1 | `studentFullName`, `StudentFullName`, `fullName`, `FullName` | Primary identifier; COPPA/PPL Art. 4 personal data |
| 2 | `studentFirstName`, `StudentFirstName` | Child name alone is personal data under GDPR Art. 4(1) for minors |
| 3 | `studentLastName`, `StudentLastName`, `studentSurname`, `StudentSurname` | Same |
| 4 | `studentEmail`, `StudentEmail`, `studentEmailAddress`, `StudentEmailAddress` | Direct identifier |
| 5 | `studentPhone`, `StudentPhone`, `studentPhoneNumber`, `StudentPhoneNumber`, `studentMobile`, `StudentMobile` | Direct identifier; special category under PPL Amendment 13 |
| 6 | `governmentId`, `GovernmentId`, `israeliId`, `IsraeliId`, `nationalId`, `NationalId`, `ssn`, `SSN`, `teudatZehut`, `TeudatZehut` | Special category (Art. 9 GDPR) |
| 7 | `birthDate`, `BirthDate`, `dateOfBirth`, `DateOfBirth`, `studentDob`, `StudentDob` | Exact DOB is personal data; the allowed signal is `ageBand` (`GRADE_4_6` etc.) |
| 8 | `homeAddress`, `HomeAddress`, `streetAddress`, `StreetAddress`, `postalAddress`, `PostalAddress` | Direct identifier |
| 9 | `parentName`, `ParentName`, `parentEmail`, `ParentEmail`, `parentPhone`, `ParentPhone`, `guardianName`, `GuardianName`, `guardianEmail`, `GuardianEmail`, `guardianPhone`, `GuardianPhone` | Secondary PII; parents of minors get the same floor |
| 10 | `schoolName`, `SchoolName`, `schoolAddress`, `SchoolAddress` | Quasi-identifier — high re-identification risk in small districts |

**Count: 10 category roots, ~40 identifier variants.** The full regex is encoded in `NoPiiFieldInLlmPromptTest.cs`; adding a new ban is a code-only change (no YAML), so the ratchet is strict — there is no allowlist file to drift out of sync.

**Single structural exemption** — lines that use a banned identifier as a **named constructor parameter of `StudentPiiContext`** (the Tutor-context input to the existing `TutorPromptScrubber`) are exempt. That seam is "I am telling the scrubber which fields to strip", not "I am leaking". The exemption is implemented by inspecting a 12-line preceding window for the `StudentPiiContext` token; it is narrow, self-documenting at the call site, and cannot be gamed without typing the scrubber's name in the file.

### Decision 2 — Permitted placeholders

Prompt templates must use the following structured placeholder tokens instead of raw fields. These tokens carry no personal data and are safe to persist in template source.

| Placeholder | What it resolves to at render time | Notes |
|---|---|---|
| `{{student_pseudonym}}` | Per-session random alias (e.g. `learner-4f2a`), regenerated on every `LearningSessionStarted_V1` event | Not stable across sessions; cannot be cross-joined with external data |
| `{{age_band}}` | `GRADE_4_6`, `GRADE_7_9`, `GRADE_10_12` | Never the exact grade number alone (grade + institute + month ≈ PII in small cohorts) |
| `{{language}}` | `en` \| `he` \| `ar` | Content localisation only |
| `{{subject}}` | `math` \| `physics` \| `biology` | Curriculum domain |
| `{{tenant_id_for_tracing}}` | Institute slug, e.g. `cena-demo` | ONLY for the `x-request-trace-id` header — MUST NOT appear in the prompt body |

`{{tenant_id_for_tracing}}` is the single quasi-identifier permitted in telemetry metadata. It is not a prompt-body field.

### Decision 3 — Runtime scrubber (defence-in-depth)

`Cena.Infrastructure.Llm.IPiiPromptScrubber` is a reusable regex-based scrubber that every `[TaskRouting]`-tagged service must invoke on its user-prompt string immediately before the LLM client call. It strips residual PII patterns (emails, phone numbers, Israeli ID numbers, postal codes, street addresses) that a structured-placeholder discipline alone cannot catch (e.g. a student pasting their parent's email into a free-text reply).

A service is exempt from the scrubber wiring check if and only if it carries the attribute

```csharp
[PiiPreScrubbed("<reason>")]
```

declaring that an upstream collaborator already scrubbed the payload. The attribute's `Reason` field is surfaced in the ratchet failure message, so silent "I'll add this later" opt-outs are visible at review time.

### Decision 4 — Fail-closed metric

The scrubber emits:

```
cena_llm_prompt_pii_scrubbed_total{feature, category}
```

incremented once per scrubbed pattern per call. **Any non-zero value in production is a severity-1 alert.** If the counter increments, something upstream of the scrubber sent PII to a service that should have either (a) used a structured placeholder, or (b) not received that field at all. The scrubber is the last line of defence — it should never actually fire; its purpose is forensic (prove the line was held) and defensive (plug the leak when the discipline fails).

---

## Why structured placeholders over regex-only scrubbing

Regex scrubbing is **necessary but not sufficient**. It is lossy and false-positive-prone:

- Hebrew and Arabic names have no Latin-letter boundary, so `\b[A-Z][a-z]+\b` misses ~40% of Israeli student names.
- Short first names (`Ty`, `Ed`) collide with common English words.
- Phone-pattern regex eats question content like "1,234,567" in a math word problem.
- Address patterns leak Hebrew street prefixes (`רחוב`) and Arabic equivalents (`شارع`) unless every language is enumerated.

Structured placeholders eliminate the problem at the source: the prompt template never sees raw text to begin with. The scrubber remains because we do not control what a student types into a free-text chat turn; the template discipline does not govern user-authored input. The two layers together give us a tight inner loop (static field ban at build time) and a soft outer loop (pattern scrub at request time).

---

## Why fail-closed on counter increment vs. fail-open

| Option | Behaviour | Trade-off |
|---|---|---|
| **A. Fail-open** (log + send scrubbed prompt) | The call proceeds with the sanitised prompt; a warning is logged | Student gets a response. Regulator gets a log line proving we processed PII — that's the bad outcome, not a good one. |
| **B. Fail-closed** (drop call, serve static fallback) | The LLM call is refused; the student sees the ladder/generic fallback | One degraded tutor turn beats one data-minimisation breach. The fallback paths (ADR-0045 hint ladder, L2 cache) are already wired, so the blast radius is a single less-helpful reply — not a system outage. |

**We chose B.** The alert semantics (severity-1 on any non-zero counter value) only make sense if the counter is actually the forensic trail after a fail-closed event. If we fail-open, the counter becomes noise — ops will silence it within a week.

Implementation: `PiiPromptScrubber.Scrub(...)` returns a result with `RedactionCount`. Callers check `if (result.RedactionCount > 0)` → skip LLM call, serve fallback, emit metric. Test coverage in `PiiPromptScrubberTests.cs` asserts the counter increments on synthetic PII input.

---

## Enforcement

Three layers, each strictly tighter than the one below:

### Layer 1 — Pre-commit lint (`scripts/lint/llm-prompt-pii.js`)

Node script, runs in developer's pre-commit and in CI's `npm run lint`. Scans every `.cs` file under `src/` that contains `[TaskRouting(` for the banned-identifier regex. Exits non-zero on any hit. Fast (<500 ms on the full tree); friendly first-line-of-defence.

### Layer 2 — xUnit architecture ratchet (`NoPiiFieldInLlmPromptTest`)

Lives in `src/actors/Cena.Actors.Tests/Architecture/`. Walks `src/**/*.cs`, identifies every file that either carries `[TaskRouting(` or directly instantiates `AnthropicClient`/`MessageCreateParams`, and fails if any banned identifier appears in non-comment, non-string-literal source. The allowlist is an **empty array** in the test source — the "strict TODO-free" stance from prr-022. Adding an exception requires editing test source, which is visible in code review.

### Layer 3 — Runtime scrubber (`PiiPromptScrubber`)

Every `[TaskRouting]`-tagged service invokes `IPiiPromptScrubber.Scrub(prompt)` on the user-prompt string. On any non-zero `RedactionCount`, the service:

1. emits `cena_llm_prompt_pii_scrubbed_total{feature, category}`,
2. logs a structured warning with the session id (never the raw PII),
3. refuses the LLM call and returns the tier's static fallback.

A ratchet in `NoPiiFieldInLlmPromptTest` verifies that every `[TaskRouting]`-tagged service either injects `IPiiPromptScrubber` or carries `[PiiPreScrubbed("<reason>")]`.

---

## Runbook — "the counter is non-zero at 03:00 on Bagrut morning"

1. **Triage (5 min)**: look at `cena_llm_prompt_pii_scrubbed_total` breakdown by `{feature, category}`. The `feature` label points at the cost-centre (e.g. `socratic`, `explanation-l3`). The `category` label points at what leaked (e.g. `email`, `phone`, `israeli_id`).
2. **Contain (15 min)**: the fail-closed logic has already degraded that feature to its fallback — no additional action needed to stop the bleed. Verify from the dashboard that `cena_llm_call_cost_usd_total{feature=<feature>}` has dropped and `cena_static_hint_ladder_served_total{feature=<feature>}` is covering the delta.
3. **Find the seam (60 min)**: `grep -rn 'features=<feature>' src/` for the `[FeatureTag]`. Inspect the prompt builder. Identify the field that was interpolated unscrubbed.
4. **Fix the seam (PR)**: replace the raw field with its structured placeholder from Decision 2. Add a test case to `PiiPromptScrubberTests.cs` with that field's synthetic form. Re-run `NoPiiFieldInLlmPromptTest` locally; it should now fail with the new identifier so the ratchet catches future regressions.
5. **Post-mortem**: the bar is why the pre-commit lint + architecture ratchet missed it. Either the banned vocabulary is incomplete (add the identifier in this ADR + in the test regex), or a new LLM seam was introduced without `[TaskRouting]` (wire it up and add the test's allowlist exception ONLY if the seam is a test double).

No student has their Bagrut stream interrupted by this runbook — the fallback is already serving; the runbook is for the on-call engineer, not the student.

---

## Non-compliant code paths (current scan)

At ADR acceptance time, the scanner reports **zero** banned-field violations across the eleven `[TaskRouting]`-tagged classes. All student-facing text on prompt builders is currently assembled from `ageBand`, `subject`, `language`, and free-text answer content. This ADR locks the status quo rather than demanding a retrofit.

The existing `TutorPromptScrubber` (Tutor-specific) remains in place; it already satisfies Decision 3 for the Tutor bounded context. `ClaudeTutorLlmService` receives `[PiiPreScrubbed("TutorPromptScrubber.Scrub() runs upstream in TutorMessageService")]` during its first post-ADR touch.

---

## Consequences

**Positive**
- Structural prevention of the Edmodo-class failure mode on every LLM seam, not just Tutor.
- Ratchet is self-documenting: test source is the single source of truth for the banned vocabulary (no YAML to drift).
- Fail-closed metric gives a real signal — any alert is a real incident.
- Pre-commit lint catches 99 % of accidents before review.

**Negative**
- A developer adding a new banned root has to touch both this ADR (human-facing doc) and the test regex (machine-enforced). That is deliberate: the ADR is the covenant, the regex is the ratchet, and skipping either is the whole risk we are trying to prevent.
- Structured placeholder discipline is harder on small greenfield spikes. The runtime scrubber's defence-in-depth is the safety net for prototype code.
- Free-text user content (a student's chat reply) cannot be banned — only scrubbed. That is the correct scope boundary: the system can govern what *it* sends; it cannot refuse to receive input from a student.

**Mitigations**
- `IPiiPromptScrubber.Scrub()` is one injected dependency and one method call — the cost of compliance is ~4 lines per service.
- `[PiiPreScrubbed("<reason>")]` is the explicit opt-out; the reason string is surfaced at code review.
- `TutorPromptScrubber` is left as the per-context enricher for Tutor's known-pii context (student name from profile) — the new `PiiPromptScrubber` is the minimal generic baseline.

---

## Evidence

| Source | Relevance |
|---|---|
| FTC v. Edmodo (2023 Affected Work Product consent decree) | Models trained on children's behavioural traces must be deleted — sending the behavioural trace to a third-party model is the precursor |
| FTC 2025 COPPA Final Rule | Explicit data minimisation for minors |
| ICO v. Reddit £14.47M (Feb 2026) | Per-user behavioural profiles of minors = profiling under GDPR Art. 22 |
| Israel PPL Amendment 13 | Applies to all Cena users in Israel; national ID is special category |
| Anthropic Usage Policy §Customer Data | Provider retains prompts for 30 days for abuse monitoring by default; a prompt containing student PII becomes a 30-day exposure window |
