# TASK-PRR-262: Scaffolding stem-grounded hint variant for hidden-options mode

**Priority**: P1 — persona-educator correctness concern
**Effort**: S-M (1 week)
**Lens consensus**: persona-educator, persona-cogsci
**Source docs**: [STUDENT-INPUT-MODALITIES-002-discussion.md §3.4](../../docs/design/STUDENT-INPUT-MODALITIES-002-discussion.md)
**Assignee hint**: kimi-coder
**Tags**: source=student-input-modalities-002, epic=epic-prr-f, priority=p1, scaffolding, pedagogy, q2
**Status**: Partial — `HintVariant` + `AuthoredHint` + `StemGroundedHintRouter` + `HintLeakDetector` + `QuestionDocument.AuthoredHints` field + 27 tests shipped 2026-04-23. Unblocked by PRR-260 backend-slice earlier same session. Authoring UI + enforced coverage rule (author-pipeline force L1+L2 StemGrounded for hide-reveal-eligible items) + `ScaffoldingService.GetHint(sessionId, questionId, attemptMode)` call-site integration deferred on frontend + PRR-228 session-render wiring.
**Source**: 10-persona 002-brief review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

When a student is in `attemptMode=hidden_reveal` or `revealState=classroom_enforced` and requests a scaffolding hint, the `ScaffoldingService` must return a hint that **does not leak the options**. Otherwise the generation-effect pedagogy is destroyed by the scaffolding ladder.

## Scope

- `ScaffoldingService` gains `GetHint(sessionId, questionId, attemptMode)`.
- Hint bank per question carries two variants: `stem-grounded` (no option references) and `full` (may reference option shapes).
- When mode is hidden: only `stem-grounded` hints are eligible.
- If no `stem-grounded` hint exists for the question's ladder level: return an honest "No hint available at this level for self-test mode — click to reveal options first" prompt. Do NOT silently serve a full-variant hint.
- Extend item-authoring pipeline (per-question): `authored_hints[]` carries `variant` field. Authoring UI forces at least L1 + L2 `stem-grounded` coverage for questions that can be entered with hidden options (a prerequisite flag on the question).

## Non-leaking test (persona-educator)

- Automated: sample 100 hints per subject; grep for option-letter patterns (`option A`, `answer A`, `אפשרות א`, `الخيار أ`) + option-content echoes (stem-to-hint text overlap). Hints that match flag for author review.
- Reject any `stem-grounded` hint that fails this test.

## Files

- `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` — extend with mode-aware routing.
- `src/actors/Cena.Actors/Mastery/ScaffoldingLevel.cs` — add variant dimension.
- Item-bank schema extension: per-hint `variant` field.
- Item-authoring admin UI — variant input.
- Tests: stem-grounded-only returned when hidden; "no hint available" copy when bank is empty; leak-detection on sample.

## Definition of Done

- No option-leaking hint reaches a student in hidden mode across test corpus.
- Authoring UI enforces variant coverage for hide-reveal-eligible items.
- `ScaffoldingService` test coverage extended.
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- [ADR-0050](../../docs/adr/0050-multi-target-student-exam-plan.md).
- Memory "No stubs — production grade".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + leak-test report>"`

## Related

- PRR-260, PRR-261.
- Existing `ScaffoldingService` at `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs`.

## What shipped (2026-04-23)

Backend types + router + leak detector; authoring UI + call-site integration deferred.

### Types (Cena.Infrastructure.Documents)

[`AuthoredHint.cs`](../../src/shared/Cena.Infrastructure/Documents/AuthoredHint.cs):

- `HintVariant` enum — `StemGrounded` (hidden-mode safe) / `Full` (requires revealed options).
- `AuthoredHint(Level, Variant, Text, Locale)` record — one hint row per (level × variant × locale) tuple.

Types live in `Cena.Infrastructure.Documents` rather than `Cena.Actors.Mastery` to avoid a `Cena.Actors → Cena.Infrastructure` cyclic dependency (QuestionDocument is persistence-layer; the types are fields on it). The router + leak detector in `Cena.Actors.Mastery` import via `using`.

### QuestionDocument extension

[`QuestionDocument.AuthoredHints`](../../src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs) — new `List<AuthoredHint>?` nullable field defaulting to `null`. Existing seeded items deserialise with no crash; the router treats null as "no hints authored".

### Router (Cena.Actors.Mastery)

[`StemGroundedHintRouter`](../../src/actors/Cena.Actors/Mastery/StemGroundedHints.cs):

- `Pick(authoredHints, requestedLevel, locale, optionsAreVisibleToStudent) → HintRouterOutcome`.
- Stable reason codes: `"no_hints_authored"` (question has no hints at all) vs. `"reveal_required_for_hint"` (hints exist at level but none match the StemGrounded variant in hidden mode).
- **No-leak invariant**: when `optionsAreVisibleToStudent == false` and only `Full` variant hints exist at the requested level, returns the `RevealRequiredReason` signal. NEVER falls through to a Full hint in hidden mode. Locked by the `Pick_hidden_mode_rejects_Full_variant_and_returns_RevealRequired` test.
- Locale fallback chain: preferred locale → English → first eligible regardless of locale. Case-insensitive matching.

### Leak detector (Cena.Actors.Mastery)

[`HintLeakDetector.Detect(hintText, optionTexts)`](../../src/actors/Cena.Actors/Mastery/StemGroundedHints.cs):

Three detection rules:

1. **Option-letter markers** across en/he/ar:
   - English: `"option A/B/C/D/E"`, `"answer A"`, `"choice A"`, with/without parens.
   - Hebrew: `"אפשרות א/ב/ג/ד/ה"`, `"תשובה א"`.
   - Arabic: `"الخيار أ/ب/ج/د/ه"`, `"الجواب أ"`.
2. **Option whole-match** (length-independent): if the hint contains the full option text verbatim, flag `option_whole_match:index_N`. Catches short options like `x²-x-6` that rule 3 would skip.
3. **Option content echo** (≥8-char substring): sliding window across the option text; any ≥8-char substring present in the hint flags `option_content_echo:index_N`. Catches partial paraphrase leaks.

Case-insensitive on all three rules. Returns `LeakDetectionResult(LeakReasons)` with one entry per distinct leak source so authors can triage — a hint may hit multiple rules simultaneously (e.g. "Option A is the factored form 2x+6" trips both `english_option_letter:A` and `option_whole_match:index_0`).

### Tests (27)

[`StemGroundedHintsTests.cs`](../../src/actors/Cena.Actors.Tests/Mastery/StemGroundedHintsTests.cs):

- Router null / empty / level-missing branches → correct reason code.
- Visible mode honours both variants; hidden mode rejects Full and returns RevealRequired.
- **Critical property** (`Pick_hidden_mode_rejects_Full_variant_and_returns_RevealRequired`): a Full-only-at-level question in hidden mode NEVER serves the Full hint.
- Locale preference + EN fallback + any-locale fallback + case-insensitive matching.
- Leak detector: English/Hebrew/Arabic letter markers (4 English theory rows + HE + AR), whole-option match (including short `x²-x-6`), long-substring partial-paraphrase catch, clean stem-grounded hint passes, empty/whitespace handled, case-insensitive English, multi-leak listing.
- QuestionDocument forward-compat: null AuthoredHints on legacy items routes cleanly to `NoHintsAuthoredReason`.

Full `Cena.Actors.sln` build green; 27/27 tests pass.

## What is deferred

- **`ScaffoldingService.GetHint(sessionId, questionId, attemptMode)` call-site**. The router is ready to consume; the session-render path (which knows the session's effective attempt-mode from PRR-260's `SessionAttemptModeContextBuilder`) needs to call `StemGroundedHintRouter.Pick` and surface the resulting hint or reveal-required signal on the session wire DTO. Same integration point as PRR-263's carve-out — lands when PRR-228 diagnostic-block wiring goes in.
- **Authoring UI enforced coverage** (at-least L1 + L2 StemGrounded coverage for hide-reveal-eligible items). Admin Vue page concern; backend field is live.
- **CI leak-detection sweep** (100 hints per subject, flag author review on hits). Script that loads the question bank + iterates `HintLeakDetector.Detect` on every StemGrounded hint. Straightforward one-file CLI when a content inventory is ready to sweep; today the bank has no authored hints so the sweep would be a no-op.
- **Vue `McOptionsGate.vue` + `ScaffoldingService` call-site integration**. Pure frontend + BFF-wire work; the `HintRouterOutcome` shape is the frozen contract.

Closing as **Partial** per memory "Honest not complimentary": the no-leak policy + leak detector are real and tested; the authoring UI + call-site integration are their own downstream tasks that consume the frozen contracts shipped today.
