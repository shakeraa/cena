---
persona: a11y
subject: ADR-0059
date: 2026-04-28
verdict: red
reviewer: claude-subagent-persona-a11y
---

## Summary

ADR-0059 carves out the right *contractual* seam (`Reference<T>` vs `Deliverable<T>`, consent-token, audit, arch-test) and the variant-tier split (parametric vs structural) is sound from a pricing/cost lens — but it does not specify a single screen-reader, keyboard, or RTL behaviour for the surface it introduces. The §3 disclosure card is described as "inline" without naming the ARIA pattern; the §5 two-tier picker has no defined keyboard contract; the reference-render path's handling of `<bdi dir="ltr">` for math + שאלון codes inside a Hebrew/Arabic page is not specified at all; and the entire surface inherits the unresolved VDatePicker-class concern from ADR-0050 — Vuetify components without a verified he/ar/screen-reader prototype default to silently broken. The "no answer affordances" rule (§2) is enforced visually but never described as announceable, so a screen-reader user gets a question with no obvious mode-difference from the practice question that follows. **Verdict: red. Three blockers, four required mitigations, ADR-0059 cannot accept until §3/§5 ARIA patterns are specified and a three-locale + screen-reader prototype of the reference page exists.**

## Section Q2 prompt answers

### Reference-page DOM walkable in screen readers (math-LTR + RTL prose)

**Red.** ADR-0059 §3 mentions `<bdi dir="ltr">` "around שאלון codes" once, in passing, in the localization paragraph — not as a normative invariant for the reference-render path. The corpus content (per PRR-242) carries Hebrew/Arabic prose with embedded math expressions (often KaTeX rendered) and Ministry-numeric שאלון codes. None of that is specified:

- **KaTeX inside RTL prose**: the math-always-LTR rule (memory: `feedback_math_always_ltr`) requires every math fragment in `<bdi dir="ltr">`. KaTeX renders MathML + visual fallback; screen readers (NVDA + VoiceOver) walk the MathML if the surrounding bidi context is sane. Without `<bdi>` wrapping, an expression like `f(x) = 2x+3` rendered inside `dir="rtl"` Hebrew prose is announced in reverse character order on at least one of {NVDA-Chrome, NVDA-Firefox, VoiceOver-Safari, TalkBack-Chrome}. This is the exact reversed-equation bug the user caught before. ADR-0059 does not require it for reference items, only mentions it for שאלון codes.
- **שאלון codes (035582, etc.)**: ADR-0059 §3 example copy gets this right ("Bagrut Math 5U, שאלון 035582 q3"). But §6's "Variant of Bagrut Math 5U, שאלון 035582 q3 (קיץ תשפ״ד) — numbers changed" provenance citation is not gated by the same `<bdi>` requirement. If the citation chip is rendered without `<bdi dir="ltr">` around `035582`, a Hebrew-locale screen reader announces "two-eight-five-five-three-zero" — wrong order, wrong meaning.
- **Hebrew-Arabic numerals inside שאלון codes**: ADR-0059 is silent on the numeral system. PRR-032 (numerals preference) is referenced in the prior multi-target review as not-yet-written, and `numeralSystem` does not exist in `onboardingStore.ts`. שאלון codes are **Ministry identifiers**, not student-display content — they should always render in Western digits regardless of student preference, because they're catalog primary keys (per ADR-0050 item 2). ADR-0059 does not say so. A naive PRR-032 implementation will mistakenly translate `035582` → `٠٣٥٥٨٢` on Arabic-locale, breaking copy-paste lookups against Ministry sites.

WCAG violations at risk: 1.3.2 (Meaningful Sequence), 3.1.2 (Language of Parts — KaTeX MathML lang attribute), 1.3.1 (Info and Relationships).

### Consent disclosure (§3) as `aria-live` not modal trap

**Yellow → Red without spec.** ADR-0059 §3 says "an inline disclosure card surfaces" before the student reaches the reference page. "Inline disclosure card" is ambiguous — three different implementations are all "inline":

1. **Static panel above the reference list** (no focus management) — keyboard-friendly, but a screen reader walking down the page hits it as ordinary content with no announcement that it's a one-time disclosure that requires action.
2. **Dialog with `role="alertdialog"` + focus trap until "Acknowledge"** — meets WCAG 4.1.3 for status messages, but is exactly what the prompt forbids (modal trap).
3. **`aria-live="polite"` region + focusable "Acknowledge" CTA** — correct pattern: screen reader announces the disclosure on first render, focus is **not** stolen, the student can tab into the CTA when ready, Escape dismisses to the previous page (since the consent is required to proceed, not optional).

ADR-0059 picks none of these. It also doesn't say:

- What's the keyboard activation key? Enter and Space on the "I understand, show me references" CTA, presumably — but not specified.
- Is the disclosure dismissible without consent? If yes, where does Escape go? If no, what does Escape do (it must do something — silent Escape is a WCAG 2.1.2 No Keyboard Trap concern).
- Does the disclosure re-appear on token expiry (90-day per §3)? Yes per "Re-prompt on token expiry" — but the re-prompt path must use the same ARIA pattern, not a different one. Two consent UIs is two surfaces to test.

**Required**: ADR-0059 §3 must specify the ARIA pattern in normative text (not "inline disclosure card"). Recommended pattern: `aria-live="assertive"` on first render with `role="region"` + `aria-labelledby` to a heading, focus stays where it was, "Acknowledge" CTA is the next focusable element after the heading, Escape returns to the parent route.

### Variant-tier picker (§5) discoverable without a mouse

**Red.** §5 specifies the cost/rate split between parametric and structural variants but says **nothing** about the picker's interaction model. The text "The student picks: 'Same problem, different numbers' (parametric) vs 'Similar problem, different scenario' (structural)" is product copy, not an ARIA contract.

Open questions ADR-0059 doesn't answer:

- **Radio group or two buttons?** Semantically a radio group with two options (the student must pick exactly one before the variant generates) — `role="radiogroup"` with `aria-labelledby` on the group, two `role="radio"` children with `aria-checked`. Two buttons would imply both are independently actionable (wrong — only one fires per session).
- **Cost transparency badge**: "Tier-3 Sonnet/Opus, ~$0.005-0.015/call" should be exposed to free-tier users so they understand why structural is rate-limited. For paid-tier students at the cap, the structural radio must announce "0 of 3 remaining today" or similar via `aria-describedby`. Just visually graying the option violates 4.1.2 (Name, Role, Value).
- **Premium feature on free tier**: ADR-0059 doesn't say what happens when a free-tier student picks structural. Disable + tooltip? Disable + announce "premium feature; tap to see upgrade options"? Open the upgrade modal directly? The screen-reader story for each is different. **Required**: pick one and specify. Recommended: keep the radio focusable but `aria-disabled="true"` with `aria-describedby` pointing at a sibling text "Premium feature — tap to upgrade", and Enter/Space on that radio routes to the upgrade flow rather than firing the variant.
- **Rate-limit reached on paid tier**: when the 25/day structural cap is hit, what does the radio say? Same `aria-disabled` + `aria-describedby` pattern, with copy "Daily structural variant limit reached — try parametric instead, or come back tomorrow." Time-of-day phrasing is exam-prep-time-framing-safe (per ADR-0048) as long as it's "tomorrow" not "in 14 hours 23 minutes".

### Reduced-motion (PRR-070) on reference→variant transitions

**Yellow.** ADR-0059 says nothing about the transition between the reference page and the variant practice session. §6 routes to a "single-question practice session (`SessionMode = 'freestyle'`)" — that's a route change, not a UI animation, but if the implementer adds a slide/fade between them (likely default Vuetify behaviour) it must respect `prefers-reduced-motion: reduce`.

PRR-070 is referenced in the prior multi-target review as audit-pending. Reference→variant adds another transition surface to that audit. **Required**: ADR-0059 must reference PRR-070 in §6 and any motion on this surface must be wrapped in the global reduced-motion guard. A second yellow item: the consent disclosure (§3) likely fades in on first render — same rule applies.

### Numerals preference for שאלון codes

**Red.** Already raised in Q2.1 above. ADR-0059 is silent. The two correct decisions are:

- **Ministry identifiers always Western digits** (catalog primary keys, per ADR-0050 item 2): שאלון codes (`035582`), `MoedSlug`, `AcademicYear` (`תשפ״ו` is letters not digits, fine), `MinistryQuestionPaperCode`. These never get translated to Hebrew-Arabic numerals.
- **Student-display numerals** (the math itself, the variant content, the page numbers, the "3 of 25 remaining today" counter): governed by PRR-032 numerals preference (when it ships). On Arabic locale with preference set, these render `٢٠٢٦` etc.

Without an explicit ADR-0059 rule, an implementer working from the body alone will either translate everything (breaks copy-paste) or translate nothing (breaks Arabic-numerals UX once PRR-032 ships). **Required**: ADR-0059 must add this distinction in §3 or a new §7 normative paragraph.

### "No answer affordances" rule (§2) — announceable, not just visual

**Red.** §2 strips input controls — no textbox, no radios, no submit, no "did I get this right?" — and adds a single CTA "Practice a variant of this question". Visually a sighted student sees a question card without inputs and infers "this is a reference, not a quiz." A screen-reader user walking the DOM gets:

> "Heading level 2: שאלון 035582 question 3. Paragraph: [Hebrew prose with math]. Button: Practice a variant of this question."

There is **no announcement** that this is a reference item, not an interactive question. The screen-reader user has to infer from the absence of inputs — which is exactly the wrong way around for assistive technology. Compare to a sighted user who sees the visual difference; the SR user gets a structurally identical DOM to a question card with the textbox stripped.

**Required**: every reference-item rendered via the `Reference<T>` path must carry one of:

- `role="region"` + `aria-roledescription="reference question, not graded"` on the outer element. `aria-roledescription` is supported across NVDA/JAWS/VoiceOver/TalkBack as of 2024 (caveat: localization of the role description is the implementer's job, not the SR's).
- A visually-hidden heading `<h2 class="sr-only">Reference material — for context only, not graded</h2>` at the top of each card. Lower-tech, more compatible, more verbose.

I prefer the latter because `aria-roledescription` is famously inconsistent in some older NVDA + Firefox combinations on RTL pages. ADR-0059 §2 must pick one and write it into the architecture-test extension or it won't be enforced.

## Additional findings (component-prototype risks)

1. **No prior reference page exists in the student app** — I checked. There's no `Cena.Api.Contracts.Reference.**` namespace in code today (PRR-245 is implementation), no precedent reference-render component, no SR-test fixture for "render Hebrew + math + שאלון code." Every layer of this is greenfield. The "VDatePicker risk class" applies: a Vuetify `VCard` + `VBtn` + `VRadioGroup` chain, all in he/ar with mixed-direction content, is **not** a verified path. Without a three-locale prototype it ships broken in at least one locale.

   Concrete known-bad list of Vuetify components on RTL + screen-reader pairs (from prior Cena audits and Vuetify GitHub issue tracker as of 2026-Q1):
   - `VRadioGroup` — `aria-checked` is correctly emitted, but the visual radio dot in some Hebrew themes inverts (the dot fills the right side of the circle in LTR, jumps to the left in RTL, but on heavy keyboard navigation Chrome occasionally repaints the LTR dot — minor visual, not a SR issue).
   - `VBtn` with `prepend-icon` — icon position relative to label is theme-RTL-aware in 3.5+, but custom icons inside `<bdi>` wrappers are not. The "Practice a variant" CTA likely has an icon — whoever picks it must verify icon-after-label behaviour in he/ar.
   - `VCard` — `role="article"` is the default; if the implementer passes `role="button"` or `tabindex="0"` for the whole card to make it click-to-expand, the keyboard interaction model is undefined for nested CTAs (the "Practice a variant" inner button). **Recommended**: cards stay `role="article"`, the inner CTA is the only focusable element. Don't let cards become click targets.
   - `VAlert` with `role="alert"` — used for ephemeral messages; if §3's consent disclosure picks this, it'll re-announce on every render, which is wrong. Don't use VAlert for the consent surface.

2. **Variant practice screen inherits all of session-page a11y debt** — ADR-0059 §6 says the variant flows into the existing session-answer pipeline. The session-answer screen has its own a11y status (covered in prior reviews); this ADR doesn't make it worse, but it doesn't make it better. Worth noting that the provenance citation chip on the answer screen ("Variant of Bagrut Math 5U, שאלון 035582 q3 (קיץ תשפ״ד) — numbers changed.") is rendered for the first time inside a session screen, in three locales, with mixed-direction content. **Required**: the citation chip pattern needs the same `<bdi>` audit as §3.

3. **Token-expiry re-prompt as a focus event** — when the 90-day token expires mid-session (rare but possible: student opens reference page, walks away, comes back at minute 89.9999 days later, taps a card and the token is now expired) — the re-prompt must not steal focus from where the student was. This is a session-recovery pattern and ADR-0059 doesn't address it. **Recommended**: token-expiry mid-flight = redirect to consent disclosure with `?return=...` query, not an in-place modal swap.

4. **Search/filter on the reference list** — ADR-0059 §4 implies a filtered list (by `ExamTarget.QuestionPaperCodes`) but doesn't specify whether the student can further search/filter (e.g. by year, by question topic). If a search box exists, it needs `role="searchbox"`, `aria-controls` pointing at the result region, and an `aria-live` result-count announcement. Same pattern as flagged in the prior multi-target review's catalog search finding. **Recommended**: ADR-0059 §4 must say either "no in-page search; filtering is auto-applied from ExamTarget" (preferred) or "search is in scope; spec the ARIA pattern."

5. **Skip-to-content for the reference page** — a long list of שאלון cards on a single route needs skip-link semantics. The student app's existing skip-link implementation (if any — I didn't verify) must reach the reference list, not just the page header. Adding to PRR-070 scope or a sibling task.

6. **Free-tier students who can't see the reference library at all** — §4 says "Freestyle students see no reference library by default. They can opt-in via /settings/reference-library". The opt-in toggle must be keyboard-accessible and the post-opt-in state change must be SR-announced ("Reference library enabled. Catalog refreshed."). ADR-0059 doesn't spec the opt-in UX at all.

7. **Provenance citation in three locales** — §6's example "Variant of Bagrut Math 5U, שאלון 035582 q3 (קיץ תשפ״ד) — numbers changed." mixes Latin ("Bagrut Math 5U"), Hebrew ("שאלון", "קיץ תשפ״ד"), Western digits ("035582"), Hebrew letters as numerals ("תשפ״ד"), and a free-text English suffix ("numbers changed"). On an Arabic-locale page this becomes a five-direction-mixed string. Without `<bdi>` wrapping each segment (Latin LTR, Hebrew RTL, digits LTR, Hebrew-letter-numerals RTL, English LTR) the visual order will not match the announcement order. **Required**: §6 must spec the citation rendering as a list of typed segments, not a free-text string, so the implementer is forced to wrap each segment correctly.

8. **Heading hierarchy on the reference page** — ADR-0059 doesn't say what the page-level heading structure is. A correct reference page has: `h1` "Reference library" (or locale equivalent), `h2` per שאלון group, `h3` per question card. Skipped levels (`h1` → `h3`) are a WCAG 1.3.1 violation. The implementer must enforce this manually because Vuetify components don't pick heading levels for you. **Recommended**: PRR-245 acceptance criteria must include "DOM heading hierarchy validates against axe-core no-heading-skip rule on en/he/ar."

9. **Focus management on variant generation** — §5's POST request returns `{variantQuestionId, sessionRouteHint}`. The student is then routed (§6) to a freestyle session. Two concerns: (a) the route transition must move keyboard focus to the new page's main heading, not leave it on the now-stale "Practice a variant" button (which no longer exists in the new DOM); (b) if the variant generation takes >500ms (parametric should be sub-100ms, structural can take 2-5s per ADR-0026), the wait state must be SR-announced via `aria-live="polite"` ("Generating variant... this may take a few seconds.") and the "Practice a variant" button must go to `aria-busy="true"` + disabled while in flight. Without this, screen-reader users hear silence and re-press the button, sending duplicate requests against the rate cap.

10. **Rate-limit error states** — when a free-tier student is at 20/20 parametric or 3/3 structural, the variant request 429s. The error must be SR-announced via `role="alert"` (assertive, takes priority), copy must be a positive-framing reset ("You've used all 3 structural variants for today. Try parametric, or come back tomorrow." — no countdown to reset, per ADR-0048). The same pattern for the IDOR rejection case (consent token invalid) and the institute-pricing-resolver override case (paid tier suspended). ADR-0059 §5 doesn't spec the error UX at all.

11. **`aria-roledescription` localization** — if mitigation 5 picks `aria-roledescription` instead of the visually-hidden header, the role description string needs en/he/ar translations. "Reference question, not graded" → "שאלת מקור, לא נבדקת" → "سؤال مرجعي، غير مُقيَّم". The translation must land in the i18n bundle before PRR-245 ships, not after. If Vuetify's locale plugin handles role descriptions automatically, that needs verification.

12. **Tab order on the reference list page** — with N cards, each card has at least 1 focusable element ("Practice a variant"). For a 50-card list this is 50 tab stops. Without a "skip past reference list" link or a roving tabindex pattern (one stop per group, arrow keys within), keyboard users have to tab 50 times to reach the next region. **Recommended**: roving tabindex with arrow-key navigation within the list; Tab moves to/from the list as a whole.

## Required mitigations (blocking ADR-0059 acceptance)

1. **Specify the §3 consent disclosure ARIA pattern in normative text.** "Inline disclosure card" is not a contract. Pick `aria-live` + non-trapping focus or `role="alertdialog"` + trap (recommended: the former) and write it into ADR-0059. Include keyboard contract: where Enter goes, where Escape goes, where focus lands after acknowledgment.

2. **Specify the §5 two-tier picker ARIA pattern.** Radio group with two `role="radio"` children, with a defined behaviour for free-tier-on-structural and paid-tier-at-cap. Cost transparency exposed via `aria-describedby`. Premium-feature path defined.

3. **Add a normative §7 (or extend §3) on RTL + math + numerals.** Three rules: (a) every math fragment in `<bdi dir="ltr">`; (b) every שאלון code, MoedSlug, MinistryQuestionPaperCode in `<bdi dir="ltr">` AND always rendered in Western digits regardless of PRR-032 numerals preference; (c) student-display numerals (counters, dates, math content) follow PRR-032 when it ships. Architecture-test must enforce (a) and (b) on any DTO leaving `Cena.Api.Contracts.Reference.**`.

4. **Three-locale + screen-reader prototype gate** — same shape as ADR-0050's VDatePicker prototype gate. Before PRR-245 implementation can ship, a working reference-page prototype must be tested against {NVDA-Chrome, NVDA-Firefox, VoiceOver-Safari, TalkBack-Chrome} × {en, he, ar} = 12 cells, with a documented pass/fail per cell. Failures fall back to a metadata-only render (citation + topic, no raw text) — same pattern as Q1's legal-fallback in §Open Questions. **Without this gate ADR-0059 ships silently broken in at least one locale.**

5. **Specify the "no answer affordances" rule (§2) as announceable.** Add either `aria-roledescription="reference question, not graded"` or a visually-hidden header to every Reference<T>-rendered card. The architecture-test extension that enforces `[ReferenceContext]` attribute must also enforce one of these patterns is present in the render template.

## Recommended mitigations (yellow, not blocking)

1. **Provenance citation as typed segments (§6)** — render as a list of `{kind: "exam-name" | "shailon-code" | "moed" | "annotation", value: string}` segments, not a free-text string. Each segment carries its own bidi treatment.

2. **Reduced-motion guard on §6 transition** — explicit reference to PRR-070 in §6.

3. **Skip-link reaches the reference list** — confirmed in PRR-245 acceptance criteria.

4. **Token-expiry mid-flight is a redirect, not an in-place swap** — spec'd in §3.

5. **Opt-in UX for freestyle students (§4)** — keyboard-accessible toggle, post-action announcement.

6. **Search/filter scope decision in §4** — either "no in-page search" (preferred) or spec the ARIA contract for it.

7. **Roving tabindex on the card list** — arrow-key nav within the list, Tab to enter/exit the list region. Reduces keyboard-user friction by ~50x on long lists.

8. **`aria-busy` + `aria-live` on variant generation** — defined wait state during the 2-5s structural variant generation window.

9. **Rate-limit error UX spec'd in §5** — `role="alert"` copy, positive-framing reset language compatible with ADR-0048.

10. **Heading hierarchy on the reference page** — h1 / h2 per group / h3 per card; axe-core no-heading-skip rule in PRR-245 acceptance criteria.

11. **Locale-aware `aria-roledescription`** — translation lands in i18n bundle before PRR-245 ships.

## Test matrix the prototype gate must clear

For mitigation 4 to be considered passed, PRR-245's pre-implementation prototype must produce a documented pass/fail result for each cell of:

| Surface | Locale | Screen reader | Browser |
|---|---|---|---|
| Consent disclosure first-render | en | NVDA / JAWS / VO / TalkBack | Chrome / Firefox / Safari |
| Consent disclosure focus on Acknowledge | en, he, ar | NVDA / VO | Chrome / Safari |
| Reference card render with KaTeX | he, ar | NVDA / VO / TalkBack | Chrome / Safari |
| שאלון code reads in correct order | he, ar | NVDA / VO / TalkBack | Chrome / Safari |
| "Practice a variant" CTA keyboard | en, he, ar | NVDA / VO / TalkBack | Chrome / Safari |
| Tier picker as radio group | en, he, ar | NVDA / VO | Chrome / Safari |
| Free-tier-on-structural disabled state | en | NVDA | Chrome |
| Paid-tier-at-cap disabled state | en | NVDA | Chrome |
| Variant generation `aria-busy` | en | NVDA / VO | Chrome / Safari |
| Rate-limit 429 `role="alert"` | en, he, ar | NVDA / VO | Chrome / Safari |
| Token-expiry redirect | en | NVDA | Chrome |
| Reduced-motion respected | en | n/a (visual + animation API) | Chrome |
| Reference→variant route focus management | en | NVDA / VO | Chrome / Safari |

13 surfaces × 2-4 locale-SR-browser combos = ~30 cells. Estimated effort: 2-3 days of dedicated a11y QA, not bolted onto a developer's existing implementation work. If this isn't budgeted, the prototype gate is theatre.

## Architecture-test enforcement (a11y angle)

ADR-0059 §1 introduces a positive-list arch-test for `Cena.Api.Contracts.Reference.**` namespace + `[ReferenceContext]` attribute. The a11y mitigations above need their own enforcement layer or they will rot:

- **`<bdi>` audit on contract DTOs** is mostly impossible at the C# layer (the wrapping happens in the Vue render template). What the arch-test CAN enforce is that any DTO field carrying a שאלון code, MoedSlug, or MinistryQuestionPaperCode value is typed as a value object (`MinistryQuestionPaperCode` struct, not raw `string`) so the implementer is forced to look up the rendering rule. **Recommended**: PRR-245 introduces `MinistryQuestionPaperCode`, `MoedSlug` value objects with a `[BidiTreatment(BidiTreatment.LtrIsolate)]` attribute that the Vue codegen reads.
- **`[ReferenceContext]` attribute extension** — extend the marker to require an `[A11yContract]` attribute that names the ARIA role + roledescription + heading-level for the DTO's render. Architecture test fails if absent. Lightweight, readable, enforceable.
- **CI lint for `<bdi dir="ltr">`** — extend the existing shipgate scanner (per ADR-0048 + PRR-224) with a positive rule: any Vue component that imports a `Reference<*>` DTO and renders a `MinistryQuestionPaperCode` or KaTeX block without a parent `<bdi dir="ltr">` fails CI. This is a stricter version of the math-always-LTR rule that has been a manual review concern until now.

If these three layers don't ship with PRR-245, the a11y mitigations pass code review on day one and silently regress by week three. The architecture-test discipline ADR-0043 / ADR-0059 builds for the Ministry-text leak is the right model — copy it for the bidi/SR rules.

## Cross-persona convergence notes

Three other personas (per ADR-0059 §Q2) will surface mitigations that compound or conflict with mine:

- **persona-privacy** on consent-token: their concern is the 90-day expiry + RTBF cascade. My consent-disclosure ARIA pattern depends on theirs converging — if they require an extra step in the consent flow (e.g. "I confirm I am ≥13"), the ARIA pattern needs another focusable control. Coordinate before locking §3.
- **persona-redteam** on rate-limit bypass + IDOR: their concern overlaps with my rate-limit error UX (mitigation 9). If redteam requires the 429 to be deliberately uninformative ("rate limited" rather than "3 of 3 used"), that conflicts with my SR-announcement pattern. Recommend: keep the count visible to the legitimate student (their UI), strip it from any external API response surface (redteam's domain). Two surfaces, two contracts.
- **persona-cogsci** on variant cadence: if they push for lower per-day caps (e.g. 2/day structural instead of 3), the disabled-state announcement is hit more often by free-tier students. UX copy stays the same; cadence change is invisible to a11y as long as the error path is spec'd once.

No conflict expected with persona-finops or persona-ministry on a11y axes.

## Sign-off

**Verdict: red.** ADR-0059's contractual carve-out is correct — `Reference<T>` vs `Deliverable<T>`, consent-token, audit, two-tier variant gating, rate-limited cost ceiling. None of the a11y-specific surface behaviour is specified. The five required mitigations above are blocking; the prototype gate (mitigation 4) is the highest-cost single item but the only way to honour the math-always-LTR rule end-to-end on a greenfield surface.

If mitigations 1, 2, 3, and 5 land in the ADR text **and** mitigation 4 is added as a normative gate on PRR-245, this review flips to **yellow** (some items still require prototype data to fully clear). It cannot flip to green until the three-locale × four-screen-reader matrix has documented pass results.

Cross-references for the implementer:

- ADR-0050 §10 + Risks accepted (VDatePicker prototype gate) — same pattern to apply here.
- ADR-0048 (exam-prep time framing) — rate-limit copy must say "tomorrow" not "in N hours".
- ADR-0042 (consent aggregate) — consent record event-sourcing applies; the a11y surface around consent acknowledgement is what this review covers.
- Memory `feedback_math_always_ltr` — the canonical rule the §3 disclosure pays lip service to but doesn't generalize.
- Memory `feedback_no_stubs_production_grade` — implication: the metadata-only fallback (Q1's legal-delta path, mitigation 4 a11y fallback) cannot ship as a stub. If it's the path forward, it must be production-grade from day one.

Reviewer: claude-subagent-persona-a11y
Date: 2026-04-28
