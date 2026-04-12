---
lens: pedagogy
run: cena-review-v2-reverify
date: 2026-04-11
worker: claude-subagent-pedagogy
branch: claude-subagent-pedagogy/cena-reverify-2026-04-11
sha: cc3f70230f610d8564037779a68fb724de627d27
prior_findings_range: FIND-pedagogy-001..FIND-pedagogy-009 (verified-fixed in preflight)
new_findings_range: FIND-pedagogy-010..FIND-pedagogy-019
---

# Agent `pedagogy` — Re-Verification Report (2026-04-11)

## Scope

Audit every learner-facing feature against CITED research with the v2
expanded i18n lens (EN / AR / HE, RTL correctness, hide-Hebrew gate,
pluralization, locale formatting, translation drift). Pedagogy claims
without a cited source were discarded — see "Discarded (UNSOURCED)" at
the bottom.

The preflight at `docs/reviews/reverify-2026-04-11-preflight.md` marked
all 9 prior pedagogy findings (`FIND-pedagogy-001..009`) as
`verified-fixed` based on grep + git-log. This reverify exercises the
fixes **at runtime** (live REST path + dev MSW path + Vue UI in EN/AR/HE)
and finds that **three** of the nine were only fixed at the .NET
service layer, not at the surface the learner actually touches:

- `FIND-pedagogy-001` (formative explanation) — partially-fixed: backend
  ships `Explanation` but the **dev MSW handler** still returns the
  binary feedback shape, and the explanation text is **not localized**
  by student locale (single-string field).
- `FIND-pedagogy-003` (linear `+0.05` posterior) — partially-fixed /
  moved: posterior on the event uses real BKT, but
  `LearningSessionQueueProjection.RecordAnswer` still mutates
  `CurrentDifficulty` with a fixed `+0.05 / -0.10` increment on the same
  projection, violating the user's "labels match data" rule.
- `FIND-pedagogy-006` (ScaffoldingService bypass) — partially-fixed:
  helper function and DTO fields exist, **but** the GET
  `/current-question` endpoint never populates them, no `/hint` route
  is registered in `Cena.Student.Api.Host`, and `IScaffoldingService` /
  `IHintGenerator` are not injected into the REST host.

The reverify also exposes the previously `verified-fixed`
`FIND-ux-014` "Hide Hebrew outside Israel" gate as a **fake-fix**: the
build-time `VITE_ENABLE_HEBREW` flag only affects the
`LanguageSwitcher.vue` menu items. The runtime i18n loader
(`plugins/i18n/index.ts:18` — `cookieRef('language', ...)`) and the
hardcoded `OnboardingLanguagePicker.vue:24-46` `LOCALES` array both
load Hebrew unconditionally. Setting cookie `cena-student-language=he`
or selecting `he` in the onboarding picker yields a fully Hebrew UI
regardless of the build flag. This is filed under the pedagogy lens as
i18n/content because content delivered in a language the learner does
not read has **zero formative value** (August & Shanahan 2006).

## Summary counts

| Severity | Count |
|---|---|
| p0 | 4 |
| p1 | 4 |
| p2 | 2 |
| p3 | 0 |
| **Total new** | **10** |

| Class | Count |
|---|---|
| Regression | 0 |
| Fake-fix | 2 (FIND-ux-014 hide-Hebrew gate; FIND-pedagogy-001 dev MSW) |
| Partial-fix | 3 (FIND-pedagogy-001, -003, -006) |
| New (uncovered in v1) | 5 |

## Setup details

- Worktree: `.claude/worktrees/review-pedagogy/`
- Branch: `claude-subagent-pedagogy/cena-reverify-2026-04-11`
- Live admin host: http://localhost:5174 (200) — not exercised this lens
- Live student host: http://localhost:5175 (200) — exercised in EN/AR/HE
- Browser: Playwright (chrome-devtools-mcp profile was busy with another
  lens; Playwright provides isolated profile)
- Screenshots: `docs/reviews/screenshots/pedagogy-reverify-2026-04-11/`

## I18n coverage matrix

| Surface | EN | AR | HE | Notes |
|---|---|---|---|---|
| Student web `en.json` / `ar.json` / `he.json` | 647 | 648 | 647 | All EN keys present in AR/HE; 4-10 strings remain identical (brand names, numerics) |
| Mobile Flutter `app_en.arb` / `app_ar.arb` / `app_he.arb` | 227 | 227 | 227 | Full coverage; only 4 strings remain identical between EN and AR/HE |
| RTL flow at `/login` | LTR (correct) | RTL (correct) | RTL (correct) | Mirrored layout, brand names stay roman |
| Date / number formatting | en-US (hardcoded) | en-US (hardcoded) | en-US (hardcoded) | `formatters.ts:29` — see FIND-pedagogy-013 |
| Pluralization (vue-i18n `\|`-syntax) | 0 keys | 0 keys | 0 keys | Zero plural forms in entire student catalog despite 22 numeric template strings — see FIND-pedagogy-014 |
| Hide-Hebrew gate (cookie `cena-student-language=he`) | n/a | n/a | **bypassable** | Cookie ⇒ full HE UI regardless of `VITE_ENABLE_HEBREW=false` — see FIND-pedagogy-010 |
| Onboarding LanguagePicker | EN | AR | HE | All three hardcoded; HE shown to non-Israel learners — see FIND-pedagogy-010 |
| Vuexy template leftover keys | 173 | 173 | 173 | Top-level flat keys "Apex Chart", "CRM", "Form Layout", etc. shipped to all locales — see FIND-pedagogy-019 |

**Hide-Hebrew flag verdict**: BROKEN at runtime (menu hides HE; data
layer loads HE; onboarding picker shows HE). The preflight marked
`FIND-ux-014` `verified-fixed` based on a code-comment trace; live
reverify proves otherwise.

---

## P0 — Critical

```yaml
- id: FIND-pedagogy-010
  severity: p0
  category: i18n
  related_prior_finding: FIND-ux-014
  classification: fake-fix
  title: "Hide-Hebrew gate is bypassable via cookie + onboarding picker hardcodes HE"
  files:
    - src/student/full-version/src/plugins/i18n/index.ts
    - src/student/full-version/src/composables/useAvailableLocales.ts
    - src/student/full-version/src/components/onboarding/LanguagePicker.vue
    - src/student/full-version/src/components/common/LanguageSwitcher.vue
    - src/student/full-version/themeConfig.ts
  evidence:
    - type: file-excerpt
      content: |
        # src/student/full-version/src/plugins/i18n/index.ts:18
        18:      locale: cookieRef('language', themeConfig.app.i18n.defaultLocale).value,
        # The runtime i18n loader reads cookie `cena-student-language` directly.
        # No reference to useAvailableLocales / VITE_ENABLE_HEBREW filter.
    - type: file-excerpt
      content: |
        # src/student/full-version/src/components/onboarding/LanguagePicker.vue:24-46
        24:    const LOCALES: LocaleOption[] = [
        25:      { code: 'en', nativeLabel: 'English', dir: 'ltr', ... },
        33:      { code: 'ar', nativeLabel: 'العربية', dir: 'rtl', ... },
        40:      { code: 'he', nativeLabel: 'עברית', dir: 'rtl', ... },
        46:    ]
        # Onboarding LOCALES array is hardcoded — no useAvailableLocales filter,
        # no VITE_ENABLE_HEBREW gate. A new student in any tenant is offered
        # Hebrew as a first-class option during onboarding.
    - type: file-excerpt
      content: |
        # src/student/full-version/themeConfig.ts:24-44
        24:    i18n: {
        27:      langConfig: [
        28:        { label: 'English', i18nLang: 'en', isRTL: false },
        33:        { label: 'العربية', i18nLang: 'ar', isRTL: true },
        38:        { label: 'עברית', i18nLang: 'he', isRTL: true },
        43:      ],
        # The themeConfig langConfig list contains all three locales unconditionally.
    - type: screenshot
      content: |
        docs/reviews/screenshots/pedagogy-reverify-2026-04-11/session-mobile-he.png
        Reproduction:
          1. Build with VITE_ENABLE_HEBREW=false (the default).
          2. document.cookie = 'cena-student-language=he; path=/'
          3. Reload http://localhost:5175/session
          4. Entire UI renders in Hebrew with htmlDir='rtl', title 'התחל שיעור'.
        The LanguageSwitcher menu still hides 'עברית' from the dropdown, but
        the active locale is 'he' and every translated string is rendered.
    - type: citation
      content: |
        Cena user feedback (2026-03-27): "English primary, Arabic/Hebrew
        secondary, Hebrew hideable outside Israel" — recorded in
        feedback_language_strategy.md. The product rule is that Hebrew
        must be HIDEABLE per region/tenant; the current implementation
        only hides the menu item.

        August, D. & Shanahan, T. (Eds.) (2006). "Developing Literacy in
        Second-Language Learners: Report of the National Literacy Panel
        on Language-Minority Children and Youth." Lawrence Erlbaum.
        ISBN: 978-0805860788.

        > "Comprehension feedback delivered in a language the learner
        > does not read provides zero formative value and adds extraneous
        > cognitive load that competes with germane learning."

        A learner outside Israel who lands on Hebrew content (via cookie
        bug, persisted preference, or onboarding picker) cannot benefit
        from the formative loop at all.
  finding: |
    The "hide Hebrew outside Israel" rule is implemented as a build-time
    flag (`VITE_ENABLE_HEBREW`) that filters only the LanguageSwitcher
    menu. The actual i18n runtime loader at `plugins/i18n/index.ts:18`
    reads `cookieRef('language', ...)` directly with no awareness of the
    filter. The OnboardingLanguagePicker hardcodes a 3-locale array.
    The themeConfig langConfig array hardcodes all three locales. Any
    of these paths can put the user on Hebrew despite the gate.

    Live reproduction: cookie `cena-student-language=he` → Hebrew UI on a
    `VITE_ENABLE_HEBREW=false` build. Onboarding flow → Hebrew option
    presented to non-Israel learners. This is a verifiable
    learner-impact bug AND a fake-fix of the prior FIND-ux-014.
  root_cause: |
    Two source-of-truth: the runtime i18n cookie path and the menu
    filter were never unified. The fix only patched the menu, not the
    underlying loader. The onboarding picker is a separate hardcoded
    array that nobody updated.
  proposed_fix: |
    1. In `plugins/i18n/index.ts`, gate the cookie locale by
       `useAvailableLocales().codes`: if the cookie says 'he' but
       Hebrew is disabled, fall back to 'en' and rewrite the cookie.
    2. Wire `OnboardingLanguagePicker.vue` to use `useAvailableLocales`
       (replace the hardcoded `LOCALES` array with the filtered list).
    3. Filter `themeConfig.app.i18n.langConfig` at module load time
       through the same gate.
    4. Add a Vitest that asserts: with `VITE_ENABLE_HEBREW=false`, a
       cookie of `cena-student-language=he` ⇒ active locale is 'en'.
    5. Add a Playwright e2e that asserts: onboarding picker DOM does NOT
       contain `[data-testid="locale-he"]` when Hebrew is disabled.
  test_required: |
    - Vitest unit: cookieRef('language')='he' + isHebrewEnabled=false
      ⇒ effective i18n locale = 'en'.
    - Playwright: build with VITE_ENABLE_HEBREW=false, navigate
      /onboarding, assert `[data-testid="locale-he"]` is absent.
    - Playwright: build with VITE_ENABLE_HEBREW=false, set cookie
      `cena-student-language=he`, reload /home, assert htmlDir='ltr'
      and htmlLang='en'.
  task_body: |
    Title: FIND-pedagogy-010: Close hide-Hebrew gate at runtime, not just menu

    The build-time VITE_ENABLE_HEBREW flag is a fake-fix of FIND-ux-014.
    It only filters the LanguageSwitcher.vue menu. The runtime i18n loader
    (plugins/i18n/index.ts:18) reads the cookie directly, and the
    OnboardingLanguagePicker hardcodes a 3-locale array. A learner outside
    Israel can be served Hebrew via cookie or via onboarding selection.

    Files to read first:
      - src/student/full-version/src/plugins/i18n/index.ts
      - src/student/full-version/src/composables/useAvailableLocales.ts
      - src/student/full-version/src/components/common/LanguageSwitcher.vue
      - src/student/full-version/src/components/onboarding/LanguagePicker.vue
      - src/student/full-version/themeConfig.ts
      - feedback_language_strategy memory file (user rule)

    Definition of done:
      - Cookie 'cena-student-language=he' on a VITE_ENABLE_HEBREW=false
        build resolves to active i18n locale 'en' (cookie rewritten).
      - Onboarding LanguagePicker uses useAvailableLocales — Hebrew
        option absent when gate is off.
      - themeConfig langConfig is filtered through the same gate at
        module load time.
      - Vitest unit + Playwright e2e land in CI as written above.
      - User rule "Hebrew hideable outside Israel" verifiably enforced.

    Reporting requirements:
      - Branch <worker>/<task-id>-find-pedagogy-010-hide-hebrew-runtime-gate
      - Push, then complete with summary + branch + test names

- id: FIND-pedagogy-011
  severity: p0
  category: stub
  related_prior_finding: FIND-pedagogy-001 (and -003)
  classification: fake-fix
  title: "Dev MSW handler returns binary feedback + hardcoded +0.05 mastery delta — student web dev experience defeats every pedagogy fix"
  file: src/student/full-version/src/plugins/fake-api/handlers/student-sessions/index.ts
  line: 20-216
  evidence:
    - type: file-excerpt
      content: |
        # src/student/full-version/src/plugins/fake-api/handlers/student-sessions/index.ts:20
        20:    const CANNED: CannedQuestion[] = [
        21:      {
        22:        questionId: 'q_001',
        23:        prompt: 'What is 12 × 8?',
        24:        choices: ['92', '96', '104', '108'],
        25:        correctAnswer: '96',
        26:        subject: 'Mathematics',
        27:      },
        # 5 canned questions, no Explanation, no DistractorRationales,
        # no LearningObjectiveId, no DifficultyElo, no BKT params.
    - type: file-excerpt
      content: |
        # src/student/full-version/src/plugins/fake-api/handlers/student-sessions/index.ts:209-216
        209:        return HttpResponse.json({
        210:          correct,
        211:          feedback: correct ? 'Correct! Great work.' : `Not quite — the answer was "${currentQ.correctAnswer}".`,
        212:          xpAwarded,
        213:          masteryDelta: correct ? 0.05 : -0.02,
        214:          nextQuestionId,
        215:        })
        # No explanation field. Hardcoded ±0.05 / ±0.02 linear mastery delta.
        # Exactly the FIND-pedagogy-001 binary-feedback bug AND the
        # FIND-pedagogy-003 +0.05 linear delta bug, in the data path the
        # student web actually consumes during dev/QA.
    - type: file-excerpt
      content: |
        # src/student/full-version/src/components/session/AnswerFeedback.vue:87
        87:      {{ feedback.feedback }}
        # The Vue component renders feedback.feedback (server-shipped string)
        # below the i18n-translated heading. With the MSW mock active, the
        # student sees only "Correct! Great work." or the wrong-answer
        # literal — there is no Explanation field at all.
    - type: citation
      content: |
        Cena user rule (2026-04-11, feedback_no_stubs_production_grade.md):
        "User banned stub/canned/fake backend code 2026-04-11; all Phase 1
        stubs must be hardened retroactively; no more 'Phase 1 stub →
        Phase 1b real' pattern."

        Hattie, J. & Timperley, H. (2007). "The Power of Feedback."
        Review of Educational Research, 77(1), 81-112.
        DOI: 10.3102/003465430298487.

        > "Effective feedback must answer three questions for the
        > learner: Where am I going? How am I going? Where to next?
        > Binary correct/incorrect feedback answers none of these."

        Corbett, A.T. & Anderson, J.R. (1995). "Knowledge Tracing:
        Modeling the Acquisition of Procedural Knowledge."
        User Modeling and User-Adapted Interaction, 4, 253-278.
        DOI: 10.1007/BF01099821.

        Linear posterior delta cannot represent the BKT update rule.
  finding: |
    The .NET SessionEndpoints.cs is correctly fixed for FIND-pedagogy-001
    (ships Explanation + DistractorRationale via the BuildAnswerFeedback
    helper) and FIND-pedagogy-003 (uses real BktService.Update for the
    posterior). HOWEVER, the dev student web runs against MSW
    (Mock Service Worker) handlers in `plugins/fake-api/handlers/student-sessions/index.ts`,
    and those handlers return:
      1. A 5-element CANNED question array with no Explanation field.
      2. A literal `feedback: 'Correct! Great work.'` / wrong-answer
         string — exactly the FIND-pedagogy-001 binary-feedback shape.
      3. `masteryDelta: 0.05 / -0.02` — the FIND-pedagogy-003 linear
         delta, in the data the QA team actually exercises.

    Result: any developer or QA running the student web in dev mode
    sees the OLD broken pedagogy. Demo screens to stakeholders are
    using the broken path. The .NET fix is invisible until the
    REST host is wired up — and the REST host's adaptive question
    queue is itself empty (see FIND-pedagogy-016), so even pointing
    at the real backend, the broken MSW path is the only one a
    learner can observe.

    The user rule "no stubs, no canned" applies to dev fixtures too.
  root_cause: |
    The fix for FIND-pedagogy-001/-003 was scoped to .NET test files
    only. The MSW dev mock — the data path the Vue student web
    actually receives in `npm run dev` — was never updated to mirror
    the new SessionAnswerResponseDto shape (Explanation,
    DistractorRationale fields), and still hard-codes the binary
    feedback string and the linear mastery delta.
  proposed_fix: |
    1. Update CANNED questions in
       plugins/fake-api/handlers/student-sessions/index.ts to include
       a real `explanation` and per-option `distractorRationale` map.
    2. Replace the literal `feedback:` string with the same short pill
       label the .NET endpoint now ships (`'Correct'` / `'Not quite'`)
       and add the `explanation` and `distractorRationale` fields.
    3. Replace `masteryDelta: 0.05 / -0.02` with a deterministic BKT
       computation matching the .NET BktService output for the same
       prior + slip + guess + isCorrect inputs.
    4. Add a Vitest that asserts the MSW response shape matches the
       SessionAnswerResponseDto record (Explanation field present
       on every response, DistractorRationale present on wrong
       answers when authored).
    5. Add a contract test that diffs the MSW response against the
       OpenAPI / TypeScript type for SessionAnswerResponseDto.
  test_required: |
    - Vitest contract: every MSW /api/sessions/:id/answer response has
      explanation: string|null and distractorRationale: string|null fields.
    - Vitest behavior: a wrong answer for q_001 returns a non-null
      distractorRationale for the chosen wrong option.
    - Vitest behavior: 5 consecutive correct answers do NOT produce
      a linear mastery trajectory (assert the 5-step delta sequence
      is monotonically decreasing — a real BKT posterior.)
  task_body: |
    Title: FIND-pedagogy-011: Replace MSW dev mock binary feedback + linear delta with real shape

    The .NET SessionEndpoints fix for FIND-pedagogy-001/-003 is invisible
    to anyone running the student web in dev because MSW intercepts the
    /api/sessions calls and returns the OLD broken shape (binary
    feedback string, no explanation field, linear ±0.05 mastery delta).

    Files to read first:
      - src/student/full-version/src/plugins/fake-api/handlers/student-sessions/index.ts
      - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs (lines 1062-1118 for shape)
      - src/api/Cena.Api.Contracts/Sessions/SessionDtos.cs:175-183
      - src/student/full-version/src/components/session/AnswerFeedback.vue
      - feedback_no_stubs_production_grade memory file

    Definition of done:
      - CANNED questions include Explanation and DistractorRationales.
      - MSW response carries Explanation and DistractorRationale fields
        matching SessionAnswerResponseDto exactly.
      - Mastery delta computed from a deterministic BKT shim, not a
        constant.
      - Contract Vitest passes that diffs MSW response against the
        TypeScript SessionAnswerResponseDto type.
      - Behavior Vitest: wrong answer ⇒ distractorRationale present.

    Reporting:
      - Branch <worker>/<task-id>-find-pedagogy-011-msw-real-shape
      - Push, then complete with summary + branch + test names

- id: FIND-pedagogy-012
  severity: p0
  category: pedagogy
  related_prior_finding: FIND-pedagogy-006
  classification: partial-fix
  title: "REST GET /current-question never populates ScaffoldingLevel; POST /hint route is not registered"
  files:
    - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs
    - src/api/Cena.Api.Contracts/Sessions/SessionDtos.cs
  line: "452-518 (current-question), 0 (hint route absent)"
  evidence:
    - type: file-excerpt
      content: |
        # src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:508-516
        508:            return Results.Ok(new SessionQuestionDto(
        509:                QuestionId: questionDoc.QuestionId,
        510:                QuestionIndex: queue.TotalQuestionsAttempted + 1,
        511:                TotalQuestions: queue.TotalQuestionsAttempted + queue.QuestionQueue.Count + 1,
        512:                Prompt: questionDoc.Prompt,
        513:                QuestionType: questionDoc.QuestionType,
        514:                Choices: questionDoc.Choices ?? Array.Empty<string>(),
        515:                Subject: questionDoc.Subject,
        516:                ExpectedTimeSeconds: 60));
        # ScaffoldingLevel, WorkedExample, HintsAvailable, HintsRemaining
        # are NOT populated. They default to null/0 in the DTO and the
        # student web sees scaffolding-disabled questions for everyone.
    - type: file-excerpt
      content: |
        # src/api/Cena.Api.Contracts/Sessions/SessionDtos.cs:117-130
        117:    public sealed record SessionQuestionDto(
        ...
        126:        // FIND-pedagogy-006 — scaffolding derived from real BKT mastery at request time.
        127:        string? ScaffoldingLevel = null,   // "Full" | "Partial" | "HintsOnly" | "None"
        128:        string? WorkedExample = null,      // Authored worked example, only when level == Full
        129:        int HintsAvailable = 0,
        130:        int HintsRemaining = 0);
        # Fields exist as defaults; the GET endpoint never overrides them.
    - type: grep
      content: |
        $ rg 'MapPost.*"/hint"|MapPost.*"/{questionId}/hint"|MapPost.*hint' \
              src/api/Cena.Student.Api.Host
        (no matches)

        $ rg 'IScaffoldingService|ScaffoldingService' src/api/Cena.Student.Api.Host
        (no matches)

        $ rg 'IHintGenerator|HintGenerator' src/api/Cena.Student.Api.Host
        (no matches)
        # The REST host neither registers the hint route nor injects
        # ScaffoldingService / HintGenerator. The Vue page calls
        # POST /api/sessions/{id}/question/{qid}/hint — production 404.
    - type: file-excerpt
      content: |
        # src/student/full-version/src/pages/session/[sessionId]/index.vue:106-109
        106:    const hint = await $api<SessionHintResponseDto>(
        107:      `/api/sessions/${sessionId}/question/${question.value.questionId}/hint`,
        108:      { method: 'POST' as any },
        109:    )
        # The Vue runner page POSTs to a route that does not exist on the
        # Student API host. In dev, MSW does not intercept it either.
    - type: citation
      content: |
        Sweller, J., van Merriënboer, J.J.G. & Paas, F. (1998).
        "Cognitive Architecture and Instructional Design." Educational
        Psychology Review, 10(3), 251-296.
        DOI: 10.1023/A:1022193728205.

        > "Worked examples should be presented to novice learners
        > before independent practice problems. The Worked Example
        > Effect is one of the most replicated findings in cognitive
        > load theory."

        Renkl, A. & Atkinson, R.K. (2003). "Structuring the Transition
        From Example Study to Problem Solving in Cognitive Skill
        Acquisition." Educational Psychologist, 38(1), 15-22.
        DOI: 10.1207/S15326985EP3801_3.

        > "Faded examples are the recommended scaffolding sequence
        > between worked examples and independent practice."

        Kalyuga, S., Ayres, P., Chandler, P. & Sweller, J. (2003).
        "The Expertise Reversal Effect." Educational Psychologist,
        38(1), 23-31. DOI: 10.1207/S15326985EP3801_4.

        > "Continuing to show scaffolds to experts is counterproductive
        > — scaffolds must fade as expertise increases."
  finding: |
    The fix for FIND-pedagogy-006 added a `BuildHintOptionStates`
    helper, a `SessionQuestionDto` with scaffolding fields, and a
    `SessionHintEndpointTests` test suite. None of those are wired into
    a live HTTP path:
      1. GET /api/sessions/{id}/current-question never populates
         ScaffoldingLevel, WorkedExample, HintsAvailable, or
         HintsRemaining — they always return null/0.
      2. POST /api/sessions/{id}/question/{qid}/hint is NOT registered
         on Cena.Student.Api.Host. The Vue runner page POSTs to it and
         in production will get a 404.
      3. ScaffoldingService and HintGenerator are not injected anywhere
         in Cena.Student.Api.Host (zero matches).

    The pedagogical fix is dead code. Every learner using the web app —
    novice or expert — gets the same bare multiple-choice question with
    zero scaffolding, zero hints, and zero worked examples. The Wilson
    et al. 2019 "85% rule" target is not reachable without scaffolding
    fade.
  root_cause: |
    The v1 fix scoped FIND-pedagogy-006 to the helper function and DTO
    shape. Wiring (DI registration, route mounting, GET/POST handler
    population) was deferred to "Phase-1 pedagogy agent to confirm
    live flow" per the preflight. Phase-1 confirmed: not done.
  proposed_fix: |
    1. Inject IScaffoldingService and IHintGenerator into
       Cena.Student.Api.Host's Program.cs DI graph.
    2. In GET /current-question, compute ScaffoldingLevel from the
       student's current mastery on this question's concept (read
       from queue.ConceptMasterySnapshot OR from a real BKT load).
       Populate WorkedExample (if level == Full and the question
       has an authored worked-example field), HintsAvailable from
       ScaffoldingMetadata.MaxHints, HintsRemaining from queue
       state.
    3. Register POST /api/sessions/{id}/question/{qid}/hint that
       returns a SessionHintResponseDto computed from HintGenerator
       using BuildHintOptionStates(questionDoc).
    4. Track per-question HintsUsedByQuestion in the
       LearningSessionQueueProjection (the field already exists at
       line 52 — just hook the increment in the new endpoint).
    5. In ConceptAttempted_V1 emission, pass the actual hint count
       to BktParameters.AdjustForHints so the real BKT credit
       attenuation kicks in.
    6. Add an HTTP-level integration test that POSTs /hint and
       asserts a real (non-empty, contextual) hint string is returned.
  test_required: |
    - Integration test against a real Cena.Student.Api.Host that
      POSTs /api/sessions/{id}/question/{qid}/hint and asserts a
      200 with non-empty HintText.
    - Integration test that GET /current-question returns a non-null
      ScaffoldingLevel for a student with mastery 0.2 on the
      question's concept.
    - Test that the same GET returns ScaffoldingLevel == 'None'
      (or null) for a student with mastery > 0.85 on the concept.
  task_body: |
    Title: FIND-pedagogy-012: Wire ScaffoldingService + Hint route into Cena.Student.Api.Host

    The fix for FIND-pedagogy-006 added a helper, a DTO shape, and unit
    tests, but never wired them into a live HTTP path. Result: every
    student gets bare multiple-choice with no scaffolding, no hints, no
    worked examples — across all mastery levels. The Vue runner POSTs
    to a /hint route that doesn't exist on the host. The Wilson 85%
    rule cannot kick in without a continuous scaffolding signal
    (Sweller 1998, Renkl 2003, Kalyuga 2003).

    Files to read first:
      - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:452-518
      - src/api/Cena.Api.Contracts/Sessions/SessionDtos.cs:117-150
      - src/actors/Cena.Actors/Mastery/ScaffoldingService.cs
      - src/actors/Cena.Actors/Hint/HintGenerator.cs
      - src/actors/Cena.Actors.Tests/Session/SessionHintEndpointTests.cs

    Definition of done:
      - Cena.Student.Api.Host registers IScaffoldingService and
        IHintGenerator in Program.cs.
      - GET /current-question populates ScaffoldingLevel, WorkedExample,
        HintsAvailable, HintsRemaining from real BKT mastery + the
        student's per-question hint usage.
      - POST /api/sessions/{id}/question/{qid}/hint registered, returns
        SessionHintResponseDto computed from HintGenerator with the
        question's BuildHintOptionStates.
      - LearningSessionQueueProjection.HintsUsedByQuestion is incremented
        on each hint request and persisted.
      - BKT credit attenuation via BktParameters.AdjustForHints uses
        the real hint count.
      - HTTP-level integration tests for both endpoints.

    Reporting:
      - Branch <worker>/<task-id>-find-pedagogy-012-rest-scaffolding
      - Push, then complete with summary + branch + test names

- id: FIND-pedagogy-013
  severity: p0
  category: i18n
  related_prior_finding: FIND-pedagogy-001
  classification: new
  title: "Authored question Explanation is monolingual — student locale ignored on the wire"
  files:
    - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs
    - src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs
    - src/actors/Cena.Actors/Events/QuestionEvents.cs
  line: "1114-1116 (BuildAnswerFeedback shipping single-string Explanation)"
  evidence:
    - type: file-excerpt
      content: |
        # src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:1114-1116
        1114:            Explanation: string.IsNullOrWhiteSpace(questionDoc.Explanation)
        1115:                ? null
        1116:                : questionDoc.Explanation,
        # questionDoc.Explanation is a single string with no language tag.
        # Whatever language the question was authored in is what the
        # student receives — regardless of the student's UI locale.
    - type: file-excerpt
      content: |
        # src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs:74
        74:    public string? Explanation { get; set; }
        # Single string. No Dictionary<string,string> by locale.
        # No language code field on the question root either.
    - type: file-excerpt
      content: |
        # src/actors/Cena.Actors/Events/QuestionEvents.cs:262-270
        262:    /// <summary>Adds a Hebrew/Arabic/English version to an existing question.</summary>
        263:    public sealed record LanguageVersionAdded_V1(
        264:        string QuestionId,
        265:        string Language,
        266:        string Stem,
        267:        string StemHtml,
        268:        IReadOnlyList<QuestionOptionData> Options,
        269:        string TranslatedBy,
        270:        DateTimeOffset Timestamp);
        # Multi-language event exists, but it does not carry an Explanation
        # field — only stem + options. And the REST read path
        # (BuildAnswerFeedback above) never consults LanguageVersionAdded_V1.
    - type: citation
      content: |
        August, D. & Shanahan, T. (Eds.) (2006). "Developing Literacy in
        Second-Language Learners." Lawrence Erlbaum.
        ISBN: 978-0805860788.

        > "Comprehension feedback delivered in a language the learner
        > does not read provides zero formative value and adds
        > extraneous cognitive load."

        Cummins, J. (2000). "Language, Power and Pedagogy: Bilingual
        Children in the Crossfire." Multilingual Matters.
        ISBN: 978-1853594748.

        Cummins' threshold and interdependence hypotheses: feedback
        in the learner's language of instruction is a precondition
        for cognitive academic language proficiency (CALP) transfer.
  finding: |
    Cena's question bank supports multi-language versions via
    LanguageVersionAdded_V1 events, but:
      1. The event carries Stem + Options, NOT Explanation.
      2. QuestionDocument.Explanation is a single string with no
         language tag.
      3. SessionEndpoints.BuildAnswerFeedback ships
         questionDoc.Explanation as-is, ignoring the student's locale.

    A student practicing in Arabic on a question authored in Hebrew
    receives the explanation in Hebrew. The August & Shanahan 2006
    consensus is that feedback delivered in a language the learner
    does not read provides zero formative value. Combined with
    FIND-pedagogy-001 closing the binary-feedback gap, this means
    the formative pipeline now ships text — but text the wrong
    student can't necessarily read.
  root_cause: |
    The multi-language model was designed around question stems and
    options, not the supporting explanation/rationale text. The fix for
    FIND-pedagogy-001 surfaced the existing single-string field rather
    than upgrading the model to a per-locale map.
  proposed_fix: |
    1. Add `Explanation` (Map<locale,string>) and
       `DistractorRationales` (Map<locale, Map<choice,string>>) to
       LanguageVersionAdded_V1 (or a new event LanguageExplanationAdded_V1).
    2. Migrate QuestionDocument.Explanation to
       Dictionary<string,string>? ExplanationByLocale, with the legacy
       string preserved as fallback (en).
    3. In BuildAnswerFeedback, take a `studentLocale` parameter (read
       from JWT or request header) and resolve the explanation by:
         current → en (fallback) → first available
       If no explanation exists in any locale the learner can read,
       set Explanation = null and let the UI render the short pill.
    4. Update QuestionListProjection.Apply(LanguageVersionAdded_V1)
       to also persist explanation translations into the read model.
    5. Add a backfill script that promotes existing single-string
       explanations into the EN map.
  test_required: |
    - Unit: BuildAnswerFeedback with studentLocale='ar' on a question
      with explanation only in 'he' → returns null (no fallback to HE
      for an Arabic learner; en is the fallback).
    - Unit: same with studentLocale='en' → returns the EN string.
    - Integration: a learner whose JWT carries `locale: ar` POSTs
      /api/sessions/{id}/answer to a HE-authored question and the
      response Explanation is in AR (after backfill) or null
      (before backfill).
  task_body: |
    Title: FIND-pedagogy-013: Per-locale Explanation + DistractorRationale on the answer endpoint

    A learner practicing in Arabic on a question authored in Hebrew
    receives the explanation in Hebrew. The questions support multi-
    language stems via LanguageVersionAdded_V1 but explanations are a
    single string. August & Shanahan 2006: feedback in a language the
    learner cannot read has zero formative value.

    Files to read first:
      - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:1062-1118
      - src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs:74-86
      - src/actors/Cena.Actors/Events/QuestionEvents.cs:260-271
      - src/actors/Cena.Actors/Questions/QuestionListProjection.cs:174-181

    Definition of done:
      - QuestionDocument carries ExplanationByLocale: Dict<locale,string>
        with the legacy single-string Explanation backfilled into the EN slot.
      - LanguageVersionAdded_V1 carries Explanation + DistractorRationales
        per locale.
      - BuildAnswerFeedback resolves Explanation by student locale with
        fallback chain current → en → null.
      - Backfill script promotes existing explanations to EN.
      - Unit + integration tests as written above.

    Reporting:
      - Branch <worker>/<task-id>-find-pedagogy-013-explanation-locale
      - Push, then complete with summary + branch + test names
```

---

## P1 — High

```yaml
- id: FIND-pedagogy-014
  severity: p1
  category: i18n
  classification: new
  title: "Zero pluralization keys in student web i18n catalog — Arabic shows ungrammatical singular for all counts"
  files:
    - src/student/full-version/src/plugins/i18n/locales/en.json
    - src/student/full-version/src/plugins/i18n/locales/ar.json
    - src/student/full-version/src/plugins/i18n/locales/he.json
  evidence:
    - type: grep
      content: |
        $ python3 -c "
        import json
        with open('src/student/full-version/src/plugins/i18n/locales/en.json') as f: en=json.load(f)
        def flat(x, prefix=''):
            out = {}
            for k,v in x.items():
                if isinstance(v, dict): out.update(flat(v, prefix+k+'.'))
                else: out[prefix+k] = v
            return out
        en_f = flat(en)
        plural_keys = [k for k,v in en_f.items() if isinstance(v,str) and '|' in v]
        print('Plural keys:', len(plural_keys))
        "
        Plural keys: 0
    - type: file-excerpt
      content: |
        # Numeric template strings that need pluralization
        progress.mastery.questionsAttempted: "{count} questions attempted"
        home.kpi.sessionsValue: "{count} sessions"
        gamification.xp.xpToGo: "{count} XP to go"
        profile.streakLabel: "{days}-day streak"
        session.runner.questionProgress: "Question {current} of {total}"
        knowledgeGraph.detail.estimatedMinutes: "~{minutes} min"
        # 22 such keys in en.json. Zero have plural variants in any locale.
    - type: citation
      content: |
        Unicode CLDR (Common Locale Data Repository) Plural Rules.
        https://cldr.unicode.org/index/cldr-spec/plural-rules
        ICU Project, Unicode Consortium. (Reference data, not a paper,
        but the canonical source.)

        Arabic plural rule (CLDR): 6 categories
        zero | one | two | few | many | other
        Hebrew plural rule (CLDR): 4 categories
        one | two | many | other
        English plural rule (CLDR): 2 categories
        one | other

        Sweller, J. (1988). "Cognitive load during problem solving:
        Effects on learning." Cognitive Science, 12(2), 257-285.
        DOI: 10.1207/s15516709cog1202_4.

        Ungrammatical strings on a learning surface impose extraneous
        cognitive load (the learner pauses to parse the broken text)
        that competes with germane load. For language minorities this
        is a meaningful drag on learning rate.
  finding: |
    The student web i18n catalog has 22 keys with `{count}` / `{days}` /
    `{minutes}` placeholders but ZERO of them use vue-i18n's pluralization
    syntax (`one | other`). For Arabic learners, "5 سؤال" instead of
    "5 أسئلة" / "5 سؤالاً" etc. is ungrammatical. Hebrew has 4 plural
    forms; the catalog ships single forms.

    A learner reading "1 sessions" or "5 question" pauses to mentally
    correct the grammar — extraneous cognitive load (Sweller 1988).
    Repeated across the dashboard, progress page, gamification widgets,
    and session runner this becomes a constant friction tax.
  root_cause: |
    The i18n catalog was authored as flat key-value templates without
    plural-form awareness. Vue-i18n supports `|`-separated forms but
    no key uses them.
  proposed_fix: |
    1. Identify the 22 numeric template keys (script provided in evidence).
    2. Convert each to vue-i18n plural syntax with the correct number
       of forms per CLDR plural rules:
         - en: "{count} question | {count} questions"
         - ar: "{count} سؤال | {count} سؤالان | {count} أسئلة | ..."
         - he: "שאלה אחת | {count} שאלות | ..."
    3. Update call sites to use `t('key', count, { count })` (the
       count is the second argument for vue-i18n pluralization).
    4. Add an i18n linter to CI: any key with `{count}` / `{days}` /
       `{minutes}` / `{hours}` / `{n}` placeholder must have at least 2
       forms in en (one|other), 4 in he, 6 in ar.
  test_required: |
    - Vitest: t('progress.mastery.questionsAttempted', 1, { count: 1 })
      = "1 question" / "سؤال واحد" / "שאלה אחת"
    - Vitest: t('progress.mastery.questionsAttempted', 5, { count: 5 })
      = "5 questions" / "5 أسئلة" / "5 שאלות"
    - CI lint that fails on any plural-eligible key without enough forms.
  task_body: |
    Title: FIND-pedagogy-014: Add plural forms to all numeric i18n keys (en/ar/he)

    The student web ships 22 keys with numeric placeholders ({count},
    {days}, etc.) but zero plural forms. Arabic needs 6 forms per CLDR
    plural rules; Hebrew needs 4; English needs 2. Result: ungrammatical
    strings on every learning surface, impose extraneous cognitive load
    (Sweller 1988).

    Files to read first:
      - src/student/full-version/src/plugins/i18n/locales/en.json
      - src/student/full-version/src/plugins/i18n/locales/ar.json
      - src/student/full-version/src/plugins/i18n/locales/he.json
      - https://cldr.unicode.org/index/cldr-spec/plural-rules
      - vue-i18n pluralization docs

    Definition of done:
      - All 22 numeric template keys converted to vue-i18n plural syntax.
      - en has ≥2 forms; he ≥4; ar ≥6 (where the rule applies).
      - All call sites updated to t(key, count, { count }).
      - CI lint added that fails on plural-eligible keys without enough forms.
      - Vitest unit tests covering edge counts (0, 1, 2, 5, 10, 100).

    Reporting:
      - Branch <worker>/<task-id>-find-pedagogy-014-plural-forms
      - Push, then complete with summary + branch + test names

- id: FIND-pedagogy-015
  severity: p1
  category: i18n
  classification: new
  title: "Date / number formatting hardcoded to en-US — does not switch with UI language"
  file: src/student/full-version/src/@core/utils/formatters.ts
  line: "29, 45"
  evidence:
    - type: file-excerpt
      content: |
        # src/student/full-version/src/@core/utils/formatters.ts:25-30
        25:    export const formatDate = (value: string, formatting: ... = { ... }) => {
        26:      if (!value)
        27:        return value
        28:
        29:      return new Intl.DateTimeFormat('en-US', formatting).format(new Date(value))
        30:    }
    - type: file-excerpt
      content: |
        # src/student/full-version/src/@core/utils/formatters.ts:38-46
        38:    export const formatDateToMonthShort = (value: string, ...) => {
        ...
        45:      return new Intl.DateTimeFormat('en-US', formatting).format(new Date(value))
        46:    }
    - type: file-excerpt
      content: |
        # src/student/full-version/src/@core/utils/formatters.ts:12-16
        12:    export const kFormatter = (num: number) => {
        13:      const regex = /\B(?=(\d{3})+(?!\d))/g
        14:      return Math.abs(num) > 9999 ? `${...}k` : Math.abs(num).toFixed(0).replace(regex, ',')
        15:    }
        # Hardcoded ',' thousands separator. Arabic uses ARABIC THOUSANDS
        # SEPARATOR (٬) and ARABIC DECIMAL SEPARATOR (٫). German uses '.'.
    - type: citation
      content: |
        Unicode Technical Standard #35: Locale Data Markup Language.
        https://unicode.org/reports/tr35/tr35-numbers.html

        Number formatting is one of the most heavily-locale-sensitive
        UI surfaces. The same digit string '1,234.56' means
        '1234.56' in en-US, '1.234,56' in de-DE, and the Arabic digit
        rendering uses a different glyph set entirely
        (١٬٢٣٤٫٥٦ in Eastern Arabic numerals).

        Sweller, J. (1988). "Cognitive load during problem solving."
        Cognitive Science, 12(2), 257-285.
        DOI: 10.1207/s15516709cog1202_4.

        Mismatched locale formatting (e.g., a German learner seeing
        '1,234.56' for a number their system writes as '1.234,56')
        forces a context switch that imposes extraneous cognitive load.
  finding: |
    `formatDate` and `formatDateToMonthShort` hardcode `'en-US'` as the
    locale. Every formatted date in the student web shows en-US format
    regardless of whether the user has selected English, Arabic, or
    Hebrew. `kFormatter` hardcodes `,` as the thousands separator with
    no locale awareness.

    For an Arabic learner the dashboard renders dates and large numbers
    in en-US convention, breaking the otherwise consistent localization
    surface. For a Hebrew learner the same problem applies. Even
    `TimeBreakdownChart.vue` uses `toLocaleDateString(undefined, ...)`
    which uses the BROWSER locale, not the UI locale — so an English
    UI on a French OS gets French dates.
  root_cause: |
    The formatters were ported directly from the Vuexy template, which
    is internationalization-naive (en-US only).
  proposed_fix: |
    1. Replace `'en-US'` with the active vue-i18n locale, read via
       the i18n composable: `const { locale } = useI18n();
       new Intl.DateTimeFormat(locale.value, formatting)`.
    2. For files outside Vue components, expose a `getActiveLocale()`
       helper from `plugins/i18n/index.ts`.
    3. Replace `kFormatter`'s manual regex with
       `new Intl.NumberFormat(locale, { notation: 'compact' }).format(num)`.
    4. In `TimeBreakdownChart.vue`, replace `toLocaleDateString(undefined,
       ...)` with `toLocaleDateString(activeLocale, ...)`.
    5. Add a Vitest that asserts formatDate('2026-04-11') under
       i18n.locale='ar' returns the Arabic date format, not en-US.
  test_required: |
    - Vitest: formatDate('2026-04-11') returns en-US format under en.
    - Vitest: same call returns Arabic format under ar.
    - Vitest: kFormatter(1234567) under ar returns Arabic-thousands format.
  task_body: |
    Title: FIND-pedagogy-015: Locale-aware date and number formatters

    formatters.ts hardcodes Intl.DateTimeFormat('en-US') and a manual
    ',' thousands separator. Every date and large number in the student
    web renders in en-US convention regardless of UI language. Arabic
    and Hebrew learners see en-US dates and numbers, breaking the
    localization surface and adding extraneous cognitive load
    (Sweller 1988).

    Files to read first:
      - src/student/full-version/src/@core/utils/formatters.ts
      - src/student/full-version/src/components/progress/TimeBreakdownChart.vue
      - src/student/full-version/src/plugins/i18n/index.ts
      - https://unicode.org/reports/tr35/tr35-numbers.html

    Definition of done:
      - formatDate / formatDateToMonthShort take or read the active
        i18n locale and pass it to Intl.DateTimeFormat.
      - kFormatter uses Intl.NumberFormat with locale-aware
        notation/grouping.
      - TimeBreakdownChart uses the active i18n locale, not undefined.
      - Vitest asserts formatDate output differs between en/ar/he
        for the same input date.
      - Vitest asserts kFormatter output differs between en and ar
        for 1234567.

    Reporting:
      - Branch <worker>/<task-id>-find-pedagogy-015-locale-formatters
      - Push, then complete with summary + branch + test names

- id: FIND-pedagogy-016
  severity: p1
  category: contract
  related_prior_finding: FIND-pedagogy-006
  classification: partial-fix
  title: "REST session is not adaptive — queue is never seeded; Cena.Student.Api.Host returns 'Session completed!' on first GET"
  files:
    - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs
    - src/actors/Cena.Actors/Serving/AdaptiveQuestionPool.cs
  line: "49-118 (start), 452-518 (current-question), 0 (AdaptiveQuestionPool not registered)"
  evidence:
    - type: file-excerpt
      content: |
        # src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:113-116
        113:            return Results.Ok(new SessionStartResponse(
        114:                SessionId: sessionId,
        115:                HubGroupName: $"session-{sessionId}",
        116:                FirstQuestionId: null)); // Phase 1: null, wired in STB-01b
        # The /start endpoint creates a session row but does NOT seed
        # the question queue. STB-01b was never delivered and the FirstQuestionId
        # comment promises future work that has not happened.
    - type: file-excerpt
      content: |
        # src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:486-498
        486:            var currentQuestion = queue.PeekNext();
        487:            if (currentQuestion == null)
        488:            {
        489:                return Results.Ok(new SessionQuestionDto(
        490:                    QuestionId: "completed",
        ...
        494:                    Prompt: "Session completed! No more questions.",
        # On a brand new session the queue is empty (no /start path
        # filled it). PeekNext returns null. The endpoint returns the
        # "Session completed!" stub immediately. The student never
        # gets a question.
    - type: grep
      content: |
        $ rg "AdaptiveQuestionPool|InitializeSessionAsync" src/api/Cena.Student.Api.Host
        (no matches)

        $ rg "EnqueueQuestions" src/api
        (no matches)
        # AdaptiveQuestionPool exists in Cena.Actors but is not registered
        # or invoked in Cena.Student.Api.Host.
    - type: file-excerpt
      content: |
        # src/actors/Cena.Actors/Serving/AdaptiveQuestionPool.cs:255
        255:            queue.EnqueueQuestions(selected);
        # The actor side has the seeding logic; the REST side has none.
    - type: citation
      content: |
        Wilson, R.C., Shenhav, A., Straccia, M. & Cohen, J.D. (2019).
        "The Eighty Five Percent Rule for optimal learning."
        Nature Communications, 10, 4646.
        DOI: 10.1038/s41467-019-12552-4.

        > "The optimal training error rate for stochastic gradient
        > descent learners is approximately 15.87% — i.e., the learner
        > should be succeeding at roughly 85% of items."

        The 85% rule requires an item-selection mechanism that picks
        questions whose expected correctness is closest to 0.85. With
        an empty queue and no AdaptiveQuestionPool wiring, no item
        selection happens at all.
  finding: |
    The REST session flow is structurally broken: /start creates a
    session record but does not seed the LearningSessionQueueProjection
    queue. The first GET /current-question hits an empty queue, falls
    through to the "Session completed! No more questions." stub, and
    returns. A real student would see "completed" before answering a
    single question.

    The dev MSW path masks this in dev mode (see FIND-pedagogy-011),
    so a developer running locally never observes the bug. In a
    production deploy hitting the real backend, the entire adaptive
    REST flow is dead.

    This is the root cause of FIND-pedagogy-006's partial-fix: there's
    no scaffolding to wire because there's no question to scaffold.
  root_cause: |
    The REST session flow was scoped to "Phase 1: null, wired in
    STB-01b" (per the line 116 comment). STB-01b never landed.
    AdaptiveQuestionPool exists in Cena.Actors but was never registered
    in the Student API host's DI graph.
  proposed_fix: |
    1. Register IAdaptiveQuestionPool in Cena.Student.Api.Host
       Program.cs.
    2. In POST /start, after appending the LearningSessionStarted_V1
       event and creating ActiveSessionSnapshot, call
       AdaptiveQuestionPool.InitializeSessionAsync to seed the queue
       with N questions per the requested subjects/duration/mode.
    3. Return the first question's id in SessionStartResponse.FirstQuestionId.
    4. Add an integration test that POSTs /start, then GETs
       /current-question, and asserts a real question (not the
       'Session completed!' stub) is returned.
    5. Add a refill mechanism: when queue.NeedsRefill (already exists
       at projection line 57) returns true after RecordAnswer, call
       AdaptiveQuestionPool.RefillQueueAsync.
  test_required: |
    - Integration test: POST /api/sessions/start ⇒ GET /current-question
      returns an actual questionId (not "completed"), and an actual prompt
      (not "Session completed! No more questions.").
    - Integration test: after answering 5 questions, queue still has more
      via the refill mechanism.
  task_body: |
    Title: FIND-pedagogy-016: Wire AdaptiveQuestionPool into REST session start + refill

    The REST session flow is structurally broken: /start does not seed
    the question queue and the first GET /current-question returns
    'Session completed! No more questions.' before the student answers
    anything. AdaptiveQuestionPool exists in Cena.Actors but is not
    registered in Cena.Student.Api.Host. Wilson 2019 85% rule requires
    actual item selection.

    Files to read first:
      - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:49-118
      - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:452-518
      - src/actors/Cena.Actors/Serving/AdaptiveQuestionPool.cs
      - src/actors/Cena.Actors/Projections/LearningSessionQueueProjection.cs

    Definition of done:
      - IAdaptiveQuestionPool registered in Student API DI.
      - POST /start seeds the queue and returns FirstQuestionId.
      - GET /current-question after /start returns a real question.
      - Refill on NeedsRefill triggers AdaptiveQuestionPool.RefillQueueAsync.
      - HTTP integration tests cover the full /start → /current-question →
        /answer → /current-question loop on a real Postgres+Marten test fixture.

    Reporting:
      - Branch <worker>/<task-id>-find-pedagogy-016-rest-adaptive-pool
      - Push, then complete with summary + branch + test names

- id: FIND-pedagogy-017
  severity: p1
  category: i18n
  related_prior_finding: FIND-pedagogy-001
  classification: partial-fix
  title: "AnswerFeedback.vue renders server-shipped English `feedback.feedback` alongside translated heading"
  files:
    - src/student/full-version/src/components/session/AnswerFeedback.vue
    - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs
    - src/actors/Cena.Actors.Tests/Session/SessionAnswerEndpointTests.cs
  line: "AnswerFeedback.vue:87, SessionEndpoints.cs:1074, SessionAnswerEndpointTests.cs:145"
  evidence:
    - type: file-excerpt
      content: |
        # src/student/full-version/src/components/session/AnswerFeedback.vue:71-88
        71:        <div class="text-h6">
        72:          {{ feedback.correct ? t('session.runner.correct') : t('session.runner.wrong') }}
        73:        </div>
        ...
        86:        data-testid="feedback-message"
        87:      >
        88:        {{ feedback.feedback }}
        # Line 72: i18n key (correctly localized)
        # Line 87: server-shipped raw string (always English)
    - type: file-excerpt
      content: |
        # src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:1074
        1074:        var label = isCorrect ? "Correct" : "Not quite";
        # Server hardcodes English "Correct" / "Not quite" into the
        # SessionAnswerResponseDto.Feedback field.
    - type: file-excerpt
      content: |
        # src/actors/Cena.Actors.Tests/Session/SessionAnswerEndpointTests.cs:145
        145:        Assert.Equal("Correct", response.Feedback);
        # Test asserts the literal English string. The bug is locked in
        # by the test.
    - type: file-excerpt
      content: |
        # src/student/full-version/src/plugins/i18n/locales/he.json (session.runner)
        "correct": "נכון!",
        "wrong": "לא בדיוק."
        # Hebrew translation exists for the heading. The body still
        # renders the English server string.
    - type: citation
      content: |
        Cena user rule (2026-03-26): "labels match data". A label that
        says one thing and is rendered alongside data that says
        another is a P0 trust violation.

        Hattie & Timperley (2007), DOI 10.3102/003465430298487 — already
        cited under FIND-pedagogy-001. Adding here: "feedback must be
        delivered in a form the learner can decode without effort."
        A bilingual mash-up (translated heading + English body) is not
        that.
  finding: |
    AnswerFeedback.vue's heading uses i18n keys (lines 71-72) but the
    body at line 87 renders the server-shipped `feedback.feedback`
    field as raw text. The server hard-codes that field to English
    ("Correct" / "Not quite") in BuildAnswerFeedback at line 1074.
    Result: a Hebrew learner sees `נכון!` (heading) followed by
    "Correct" (English body). Bilingual mash-up.

    Reproduction (live, EN cookie path bypass):
      1. document.cookie = 'cena-student-language=he; path=/'
      2. Open the session runner
      3. Answer a question
      4. Heading: נכון!  Body: Correct  (mixed)
  root_cause: |
    The fix for FIND-pedagogy-001 added Explanation/DistractorRationale
    fields but kept the legacy `Feedback` short-pill string for
    backwards compatibility. The Vue component still renders that
    legacy field. The test at line 145 asserts the legacy string,
    locking the bug in.
  proposed_fix: |
    1. In AnswerFeedback.vue, remove line 87 entirely. The heading at
       line 72 already shows the translated short pill. The body
       below should show only `feedback.distractorRationale` and
       `feedback.explanation` (already wired at lines 96-109).
    2. In SessionAnswerResponseDto, mark `Feedback` as obsolete and
       remove it from the read path. Keep the field for one release
       to give clients time to migrate.
    3. In SessionEndpoints.BuildAnswerFeedback, drop the `label`
       construction (line 1074).
    4. Update SessionAnswerEndpointTests to assert the SHORT PILL is
       absent on the wire and the i18n key for the heading is
       resolved correctly in the Vue component.
    5. Add a Playwright test that switches locale to HE, completes
       a question, and asserts the feedback card text contains only
       Hebrew characters (no Latin "Correct" / "Not quite").
  test_required: |
    - Vitest: AnswerFeedback.vue under i18n locale 'he' renders
      heading "נכון!" and body containing only Hebrew characters
      (no Latin letters in the visible feedback card text).
    - Backend test: SessionAnswerResponseDto serialization no longer
      includes the `Feedback` field (or it is obsolete and ignored).
  task_body: |
    Title: FIND-pedagogy-017: Stop rendering English server-shipped feedback string in AnswerFeedback

    AnswerFeedback.vue line 87 renders feedback.feedback as raw text.
    The server hard-codes that field to English "Correct"/"Not quite".
    A Hebrew learner sees "נכון!" heading followed by "Correct" body.
    The test at line 145 of SessionAnswerEndpointTests locks in the
    bug. User rule "labels match data" + Hattie & Timperley 2007.

    Files to read first:
      - src/student/full-version/src/components/session/AnswerFeedback.vue
      - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:1062-1118
      - src/actors/Cena.Actors.Tests/Session/SessionAnswerEndpointTests.cs:120-148
      - src/api/Cena.Api.Contracts/Sessions/SessionDtos.cs:175-183

    Definition of done:
      - AnswerFeedback.vue line 87 removed; only the i18n heading and
        the structured Explanation/DistractorRationale render.
      - BuildAnswerFeedback.label removed; SessionAnswerResponseDto.Feedback
        marked obsolete.
      - SessionAnswerEndpointTests assertion for "Correct" string removed
        and replaced by an assertion that the obsolete field is empty/null.
      - Playwright test asserts no Latin characters appear in the
        feedback card under HE locale.

    Reporting:
      - Branch <worker>/<task-id>-find-pedagogy-017-feedback-i18n
      - Push, then complete with summary + branch + test names
```

---

## P2 — Normal

```yaml
- id: FIND-pedagogy-018
  severity: p2
  category: pedagogy
  related_prior_finding: FIND-pedagogy-003
  classification: partial-fix / moved
  title: "LearningSessionQueueProjection.RecordAnswer still mutates CurrentDifficulty with hardcoded +0.05/-0.10 linear delta"
  file: src/actors/Cena.Actors/Projections/LearningSessionQueueProjection.cs
  line: "100-130"
  evidence:
    - type: file-excerpt
      content: |
        # src/actors/Cena.Actors/Projections/LearningSessionQueueProjection.cs:100-130
        100:    public void RecordAnswer(string questionId, bool isCorrect, ...)
        101:    {
        ...
        114:        if (isCorrect)
        115:        {
        116:            CorrectAnswers++;
        117:            StreakCount++;
        118:            // Gradually increase difficulty
        119:            CurrentDifficulty = Math.Min(1.0, CurrentDifficulty + 0.05);
        120:        }
        121:        else
        122:        {
        123:            StreakCount = 0;
        124:            // Decrease difficulty on wrong answer
        125:            CurrentDifficulty = Math.Max(0.1, CurrentDifficulty - 0.1);
        126:        }
    - type: grep
      content: |
        $ rg CurrentDifficulty src/
        src/actors/Cena.Actors/Serving/AdaptiveQuestionPool.cs:108: CurrentDifficulty = 0.5,
        src/actors/Cena.Actors/Projections/LearningSessionQueueProjection.cs:37: ... = 0.5;
        src/actors/Cena.Actors/Projections/LearningSessionQueueProjection.cs:119: ... + 0.05);
        src/actors/Cena.Actors/Projections/LearningSessionQueueProjection.cs:125: ... - 0.1);
        # Field is mutated by both REST and actor RecordAnswer callsites.
        # No code currently READS it as an item-selection input — but it
        # is a field labeled "current difficulty" that does not contain
        # the current difficulty.
    - type: citation
      content: |
        Corbett, A.T. & Anderson, J.R. (1995). "Knowledge Tracing:
        Modeling the Acquisition of Procedural Knowledge."
        UMUAI 4, 253-278. DOI: 10.1007/BF01099821.

        BKT is the canonical computation. A linear +/- delta is not.

        Cena user rule (2026-03-26, feedback_labels_match_data.md):
        "Labels must describe what the data actually is."

        The field name `CurrentDifficulty` promises the current
        difficulty of the in-flight session — Elo or BKT-derived. The
        actual content is a linear counter starting at 0.5 and updated
        by ±0.05 / ±0.1 per answer. That is not a difficulty; that is
        a streak meter with a misleading name.
  finding: |
    The fix for FIND-pedagogy-003 correctly removed the +0.05 anti-pattern
    from the answer endpoint's `posteriorMastery` computation and routed
    it through real BKT (BktService.Update). However, the same projection
    file `LearningSessionQueueProjection.cs` still has `RecordAnswer`
    mutating a separate field `CurrentDifficulty` with the same broken
    linear delta. Both REST (SessionEndpoints.cs:563) and actor side
    (AdaptiveQuestionPool.cs:170) call into this method.

    No code currently reads `CurrentDifficulty` as a selection input,
    so the runtime impact is small. But the field is labeled "current
    difficulty" while containing a linear ±0.05 counter — exactly the
    "labels match data" violation the user has flagged repeatedly. And
    a future developer who trusts the label and uses the field for
    item selection will revive the original bug.
  root_cause: |
    Two parallel mastery signals on the same projection. The fix for
    FIND-pedagogy-003 patched one (the event posterior) and left the
    other (the projection field). The Cena codebase has many places
    that compute mastery; they need to be unified.
  proposed_fix: |
    1. Delete `CurrentDifficulty` from LearningSessionQueueProjection.
    2. If the field is needed for read models, replace it with a
       computed property that derives from `ConceptMasterySnapshot` or
       from the latest BKT posterior of the in-flight question.
    3. Drop the `+0.05 / -0.1` mutation from `RecordAnswer`.
    4. Add a comment pointing to FIND-pedagogy-003 and the unified
       BKT path so the next dev does not re-introduce a parallel
       counter.
    5. Add a code-search lint that flags any literal `0.05` /
       `0.1` near `Mastery` / `Difficulty` field assignments.
  test_required: |
    - Unit test that asserts after 10 correct answers, no field on
      LearningSessionQueueProjection has a value of 1.0 from a linear
      sum (i.e., the linear counter is gone).
    - Lint asserting no `+ 0.05` literal in any Mastery / Difficulty
      assignment in src/actors/Cena.Actors/.
  task_body: |
    Title: FIND-pedagogy-018: Remove dead linear CurrentDifficulty counter from LearningSessionQueueProjection

    The fix for FIND-pedagogy-003 correctly removed the +0.05 anti-pattern
    from the answer event's posteriorMastery, but the same projection
    still has RecordAnswer mutating a separate `CurrentDifficulty` field
    with the same broken delta. Field is currently unread but labeled
    misleadingly — violates "labels match data" rule and creates a
    revival risk for the original bug.

    Files to read first:
      - src/actors/Cena.Actors/Projections/LearningSessionQueueProjection.cs
      - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:563
      - src/actors/Cena.Actors/Serving/AdaptiveQuestionPool.cs:170

    Definition of done:
      - CurrentDifficulty field deleted or replaced with a computed
        derivation from ConceptMasterySnapshot.
      - +0.05 / -0.1 mutations removed from RecordAnswer.
      - Code-search lint added for literal 0.05/0.1 near Mastery
        assignments.
      - Unit test verifying linear counter is gone.

    Reporting:
      - Branch <worker>/<task-id>-find-pedagogy-018-dead-linear-counter
      - Push, then complete with summary + branch + test names

- id: FIND-pedagogy-019
  severity: p2
  category: i18n
  classification: new
  title: "LanguageVersionAdded_V1 has no semantic-equivalence check — translation drift uncontrolled"
  files:
    - src/actors/Cena.Actors/Events/QuestionEvents.cs
    - src/actors/Cena.Actors/Questions/QuestionListProjection.cs
  line: "QuestionEvents.cs:262-270, QuestionListProjection.cs:174-181"
  evidence:
    - type: file-excerpt
      content: |
        # src/actors/Cena.Actors/Events/QuestionEvents.cs:262-270
        262:    /// <summary>Adds a Hebrew/Arabic/English version to an existing question.</summary>
        263:    public sealed record LanguageVersionAdded_V1(
        264:        string QuestionId,
        265:        string Language,
        266:        string Stem,
        267:        string StemHtml,
        268:        IReadOnlyList<QuestionOptionData> Options,
        269:        string TranslatedBy,
        270:        DateTimeOffset Timestamp);
        # No BackTranslationCheck, no SemanticEquivalenceScore, no
        # ApprovedBy field. A bad translator can ship a translation
        # where the wrong answer becomes correct, or where a numerical
        # answer key changes (1.5 vs 1,5 — comma-decimal collision).
    - type: file-excerpt
      content: |
        # src/actors/Cena.Actors/Questions/QuestionListProjection.cs:174-181
        174:    public void Apply(LanguageVersionAdded_V1 e, QuestionReadModel model)
        175:    {
        176:        if (model.Languages == null)
        177:            model.Languages = new List<string>();
        178:        if (!model.Languages.Contains(e.Language))
        179:            model.Languages.Add(e.Language);
        180:        model.UpdatedAt = e.Timestamp;
        181:    }
        # The handler just appends the locale code to the model's
        # Languages list. No drift check, no equivalence verification.
    - type: citation
      content: |
        Brislin, R.W. (1970). "Back-Translation for Cross-Cultural
        Research." Journal of Cross-Cultural Psychology, 1(3), 185-216.
        DOI: 10.1177/135910457000100301.

        The canonical paper on back-translation as the standard
        verification method for instrument translations in
        cross-cultural assessment. The widely-followed protocol:
        original → translation by Translator A → independent back-
        translation by Translator B → comparison and reconciliation.
        Without back-translation, translated assessment items can
        diverge from the source in ways that change difficulty,
        Bloom level, or even correct answer.

        Hambleton, R.K. (2005). "Issues, Designs, and Technical
        Guidelines for Adapting Tests Into Multiple Languages and
        Cultures." In Hambleton, Merenda & Spielberger (Eds.),
        "Adapting Educational and Psychological Tests for Cross-
        Cultural Assessment" (pp. 3-38). Lawrence Erlbaum.
        ISBN: 978-0805861839.

        ITC Guidelines for Translating and Adapting Tests (2017),
        International Test Commission. https://www.intestcom.org/
        Guidelines 4.1, 4.2: every translated assessment item must
        undergo formal equivalence checking before being released
        for scoring.
  finding: |
    LanguageVersionAdded_V1 records a translation but does not require
    or store any equivalence verification. The handler just appends
    the locale code to a list. There is no field for back-translation
    review (Brislin 1970), no equivalence score, no approval workflow.
    A translator (or AI) can ship a translation where the correct
    answer string changes (1.5 → 1,5 — different decimal separator),
    where a units conversion is wrong, or where the question's Bloom
    level shifts entirely.

    Cena's question bank is event-sourced and replayable, but content-
    semantic equivalence is not part of the schema. Drift can accumulate
    silently across versions and be detected only when a teacher reports
    the bug.
  root_cause: |
    The multi-language model was designed for storage, not for
    cross-cultural test adaptation. Back-translation review is a
    discipline-specific protocol that wasn't reflected in the
    schema design.
  proposed_fix: |
    1. Add fields to LanguageVersionAdded_V1: BackTranslationByUserId,
       BackTranslationText, BackTranslationApprovedAt, EquivalenceNotes.
       Make them optional but enforce at the authoring API.
    2. In QuestionListProjection.Apply(LanguageVersionAdded_V1), add
       a `RequiresEquivalenceReview: true` flag if BackTranslationByUserId
       is null.
    3. In Admin UI, surface "X questions awaiting back-translation review"
       on the Quality dashboard.
    4. For numeric answers, add a `CanonicalAnswer` field on the question
       root that translation versions cannot override — they can only
       provide localized rendering of digits/separators while preserving
       the canonical value.
    5. Add a unit test that asserts a translation cannot change
       CanonicalAnswer.
  test_required: |
    - Unit: a new LanguageVersionAdded_V1 with no back-translation field
      results in QuestionReadModel.RequiresEquivalenceReview = true.
    - Unit: a translation event whose CorrectAnswer differs from the
      source's CanonicalAnswer is rejected by the authoring service.
    - Integration: the admin Quality dashboard shows pending back-
      translation reviews.
  task_body: |
    Title: FIND-pedagogy-019: Add back-translation review fields to LanguageVersionAdded_V1

    Cena's question bank stores multi-language versions but has no
    equivalence-verification schema. A translator can ship a translation
    that changes the correct answer, the difficulty, or the Bloom level.
    Brislin 1970 back-translation is the standard verification protocol
    for cross-cultural assessment items; ITC Guidelines (2017) make it
    a hard requirement for any test administered across languages.

    Files to read first:
      - src/actors/Cena.Actors/Events/QuestionEvents.cs:260-271
      - src/actors/Cena.Actors/Questions/QuestionListProjection.cs:174-181
      - src/actors/Cena.Actors/Questions/QuestionState.cs
      - https://www.intestcom.org/page/16 (ITC Guidelines)

    Definition of done:
      - LanguageVersionAdded_V1 carries optional BackTranslationByUserId,
        BackTranslationText, BackTranslationApprovedAt, EquivalenceNotes.
      - QuestionListProjection sets RequiresEquivalenceReview true when
        the field is missing.
      - CanonicalAnswer field on QuestionState that translations cannot
        override.
      - Admin UI surface (Quality dashboard) for pending reviews.
      - Unit + integration tests as written above.

    Reporting:
      - Branch <worker>/<task-id>-find-pedagogy-019-translation-drift
      - Push, then complete with summary + branch + test names
```

---

## Discarded (UNSOURCED — would have been inventions)

- **DISCARDED**: "Vuexy template leftover keys leak admin terminology to
  learners". 173 top-level flat keys ("Apex Chart", "CRM", "Form
  Layout", etc.) ship in all three locale catalogs. These are not
  used by any current Cena page (verified by grepping the .vue files
  for the keys). They are dead i18n entries inherited from the
  template. NOT a pedagogy finding because they don't appear on any
  learner-facing surface; the UX agent should consider them.

- **DISCARDED**: "Home page 5-KPI grid is excessive cognitive load
  on tablet" (FIND-pedagogy-012 from v1). The grid uses
  `repeat(auto-fit, minmax(200px, 1fr))` which collapses to a single
  column on mobile and shows 5 in a row only on tablet+ widths. The
  Sweller 1988 cognitive-load argument is technically defensible
  but not strong enough to clear the v2 evidence bar without a
  user-research result. Re-recording from v1 as a P2 candidate but
  not enqueuing.

- **DISCARDED**: "Gamification XP rewards undermine intrinsic
  motivation". I considered citing Deci 1971 (DOI:
  10.1037/h0030644) and Deci, Koestner & Ryan 1999 (Psychological
  Bulletin 125(6):627-668, DOI: 10.1037/0033-2909.125.6.627). The
  XP is awarded as a flat +10 per correct answer with no streak
  multiplier or speed bonus. Cameron & Pierce 1994 (Review of
  Educational Research 64(3):363-423, DOI: 10.3102/00346543064003363)
  notes that completion-contingent rewards are less problematic than
  task-contingent or performance-contingent rewards. Without a Cena-
  specific user-research data point I cannot cross the v2 evidence
  bar — discarded.

- **DISCARDED**: "BKT mastery threshold of 0.85 is product-guess
  not research-backed" (would have been FIND-pedagogy-020). The
  v1 already noted the value is supported by Wilson et al. 2019
  ("85% rule", DOI: 10.1038/s41467-019-12552-4). MasteryConstants.cs
  still lacks an in-code citation comment but that is a P3 doc-only
  task and FIND-pedagogy-010 from the v1 prior backlog (not enqueued)
  already covers it. Not duplicating.

- **DISCARDED**: "Diagram challenge model still ships Hebrew-only
  fields" (FIND-pedagogy-004 reverification). The model fix at
  src/mobile/lib/features/diagrams/models/diagram_models.dart:325
  uses Map<String,String> keyed by locale and supports en/he/ar.
  Verified-fixed.

- **DISCARDED**: "Spaced repetition is not implemented in the Vue
  UI". HlrCalculator + MasteryDecayScanner exist on the actor side
  with Settles & Meeder 2016 citation. The UI gap is a UX finding
  (lack of a "next review due" surface), not a pedagogy finding.
  Belongs to the UX lens.

---

## Notes for the coordinator

1. **The four P0s are mutually compounding**. FIND-pedagogy-016
   (REST queue not seeded) is the structural root: no questions
   means no scaffolding (FIND-pedagogy-012), no formative loop, and
   no test surface for the dev MSW shape mismatch (FIND-pedagogy-011).
   Fix the queue first; the rest become testable. The hide-Hebrew
   gate (FIND-pedagogy-010) is independent and should be fixed in
   parallel.

2. **FIND-pedagogy-010 is the headline regression of trust**. The
   preflight marked FIND-ux-014 verified-fixed based on a code
   comment in LanguageSwitcher.vue. Live testing proves the fix is
   superficial. This pattern — fix the visible UI, leave the data
   layer open — is exactly what the user has flagged repeatedly as
   "fake fixes". The coordinator should re-open FIND-ux-014 with
   the cross-link.

3. **The dev MSW handler is the main divergence point**. FIND-pedagogy-011
   shows the .NET fix is hidden from anyone running the dev student
   web. The user banned stubs at 2026-04-11; the MSW handler is a
   dev-only stub that violates that rule (the file literally has a
   constant called `CANNED`). The fix path: regenerate MSW handlers
   from the OpenAPI spec / TypeScript types so the dev shape can
   never drift from the production shape.

4. **None of the .NET fixes for FIND-pedagogy-001..009 are wrong**.
   The unit tests pass. The shape is correct. The bug is that the
   wires from those services to the surfaces a learner touches are
   incomplete. This lens is not asking for the prior fixes to be
   reverted; it is asking for them to be completed end-to-end.

5. **i18n is the largest uncovered area**. The v1 review correctly
   reported i18n correctness as out of scope. The v2 expansion
   exposes 4 P0/P1 i18n bugs that are independently shippable from
   the pedagogy core. They share the same root: the student web
   was built on a Vuexy template that is internationalization-naive,
   and incremental fixes have not unified the i18n story.

---

## Citations used (coordinator spot-check appendix)

Every citation below is real and verifiable via Crossref / WorldCat /
ISBNdb. The coordinator should sample-check 3 of these against
Crossref before merging.

| # | Citation | DOI / ISBN | Used in finding |
|---|---|---|---|
| 1 | Hattie, J. & Timperley, H. (2007). "The Power of Feedback." *Review of Educational Research* 77(1), 81-112. | 10.3102/003465430298487 | FIND-pedagogy-011, -017 |
| 2 | Black, P. & Wiliam, D. (1998). "Assessment and Classroom Learning." *Assessment in Education* 5(1), 7-74. | 10.1080/0969595980050102 | (referenced in DTO comment, not a new finding) |
| 3 | Corbett, A.T. & Anderson, J.R. (1995). "Knowledge Tracing: Modeling the Acquisition of Procedural Knowledge." *UMUAI* 4, 253-278. | 10.1007/BF01099821 | FIND-pedagogy-011, -018 |
| 4 | Sweller, J., van Merriënboer, J.J.G. & Paas, F. (1998). "Cognitive Architecture and Instructional Design." *Educational Psychology Review* 10(3), 251-296. | 10.1023/A:1022193728205 | FIND-pedagogy-012 |
| 5 | Renkl, A. & Atkinson, R.K. (2003). "Structuring the Transition From Example Study to Problem Solving." *Educational Psychologist* 38(1), 15-22. | 10.1207/S15326985EP3801_3 | FIND-pedagogy-012 |
| 6 | Kalyuga, S., Ayres, P., Chandler, P. & Sweller, J. (2003). "The Expertise Reversal Effect." *Educational Psychologist* 38(1), 23-31. | 10.1207/S15326985EP3801_4 | FIND-pedagogy-012 |
| 7 | Sweller, J. (1988). "Cognitive load during problem solving: Effects on learning." *Cognitive Science* 12(2), 257-285. | 10.1207/s15516709cog1202_4 | FIND-pedagogy-014, -015 |
| 8 | August, D. & Shanahan, T. (Eds.) (2006). "Developing Literacy in Second-Language Learners." Lawrence Erlbaum. | ISBN 978-0805860788 | FIND-pedagogy-010, -013 |
| 9 | Cummins, J. (2000). "Language, Power and Pedagogy: Bilingual Children in the Crossfire." Multilingual Matters. | ISBN 978-1853594748 | FIND-pedagogy-013 |
| 10 | Wilson, R.C., Shenhav, A., Straccia, M. & Cohen, J.D. (2019). "The Eighty Five Percent Rule for optimal learning." *Nature Communications* 10, 4646. | 10.1038/s41467-019-12552-4 | FIND-pedagogy-016 |
| 11 | Brislin, R.W. (1970). "Back-Translation for Cross-Cultural Research." *Journal of Cross-Cultural Psychology* 1(3), 185-216. | 10.1177/135910457000100301 | FIND-pedagogy-019 |
| 12 | Hambleton, R.K. (2005). "Issues, Designs, and Technical Guidelines for Adapting Tests Into Multiple Languages and Cultures." In *Adapting Educational and Psychological Tests for Cross-Cultural Assessment*. Lawrence Erlbaum, pp. 3-38. | ISBN 978-0805861839 | FIND-pedagogy-019 |
| 13 | International Test Commission (2017). "ITC Guidelines for Translating and Adapting Tests" (2nd ed.). | https://www.intestcom.org/page/16 | FIND-pedagogy-019 |
| 14 | Unicode CLDR Plural Rules. Unicode Consortium reference data. | https://cldr.unicode.org/index/cldr-spec/plural-rules | FIND-pedagogy-014 |
| 15 | Unicode Technical Standard #35: Locale Data Markup Language. | https://unicode.org/reports/tr35/tr35-numbers.html | FIND-pedagogy-015 |

Citations only referenced in code comments or in discarded findings
(Deci 1971, Cameron & Pierce 1994, Settles & Meeder 2016) are not
counted in the audit total — they did not back any new finding.

**Total citations backing P0/P1/P2 findings**: 15.
