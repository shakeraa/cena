# TASK-PRR-256: PRR-247 frontend completion — SessionSetupForm mode toggle + i18n

**Priority**: P0 — PRR-247 task body items 7-9 were skipped; contract has fields no client emits
**Effort**: S (2-3 days; frontend + i18n + Vitest + E2E)
**Source docs**: claude-code self-audit 2026-04-28, [TASK-PRR-247](done/TASK-PRR-247-adr-0060-session-mode-wiring.md) §Frontend (items 7-9), [ADR-0060](../../docs/adr/0060-session-mode-exam-prep-vs-freestyle.md)
**Assignee hint**: claude-3 (frontend pattern fits her STU-W-11 / E2E-K work)
**Tags**: source=claude-code-audit-2026-04-28,epic=epic-prr-f,priority=p0,frontend,i18n,e2e
**Status**: Ready
**Tier**: launch
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

PRR-247 was marked complete with the contract change merged but the frontend chunk skipped. The `SessionStartRequest` shape now carries `examScope` + `activeExamTargetId` fields, and the server validator rejects malformed combinations, but **no client emits these fields today**. Until the frontend ships the toggle, exam-prep students get the same target-blind pool as freestyle (the original 2026-04-27 bug remains user-visible).

## Scope

1. **Mode toggle in `SessionSetupForm.vue`** — Vuetify segmented button with two options (`Exam prep` / `Freestyle`). Smart default: ExamPrep when student has ≥1 active `ExamTarget`, Freestyle otherwise.
2. **Active-target picker** — when ExamPrep is selected, surface an active-target picker (dropdown defaulting to most-recently-active `ExamTarget` from the student's plan; chevron opens the multi-target list with שאלון codes wrapped in `<bdi dir="ltr">`). When Freestyle, hide the target picker.
3. **i18n in en/he/ar** — copy for the toggle labels, helper text ("Targeted exam practice" / "Open practice — no specific exam"), accessibility labels for screen readers. No countdown / streak / days-until copy per [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md).
4. **API client emit** — when the form submits, populate `examScope: 'exam-prep' | 'freestyle'` and `activeExamTargetId: string | null` on the request body. Map nullable target id correctly to omit-when-null per the JSON shape.
5. **Vitest** — toggle behavior, default-to-ExamPrep when targets exist, default-to-Freestyle when not, target-picker reactivity, screen-reader announcement on toggle change.
6. **E2E** — student with active target launches ExamPrep session; student switches to Freestyle and launches; student without targets sees Freestyle as the only option (ExamPrep disabled with helper text).
7. **Real-browser RTL test** — Hebrew + Arabic locales, math-LTR rendering of שאלון codes, no `<bdi>` regression on the picker.

## Files

### Modified
- `src/student/full-version/src/components/session/SessionSetupForm.vue`
- `src/student/full-version/src/api/types/common.ts` (or wherever `SessionStartRequest` is mirrored on the TS side; verify naming `examScope` not `ExamScope` — wire is camelCase)
- `src/student/full-version/src/plugins/i18n/locales/{en,he,ar}.json`

### New
- `src/student/full-version/tests/unit/components/SessionSetupForm.spec.ts` (extended)
- `tests/e2e-flow/specs/session-mode-toggle.spec.ts` (new)

## Definition of Done

- Mode toggle renders + works in en/he/ar.
- Real-browser E2E green (per memory "Real browser E2E with diagnostics" — chrome console + page errors + failed requests captured).
- A11y review: keyboard, screen reader, reduced-motion all pass.
- Validator tests pass: ExamPrep without target id → 400; Freestyle with target id → 400.

## Blocking

- None — contract is on main.

## Reporting

`node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + E2E spec sha + a11y review notes>"`
