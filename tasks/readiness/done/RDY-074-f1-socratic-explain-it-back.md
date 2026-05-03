# RDY-074 — F1: Socratic "explain-it-back" with LLM judge + CAS invariant check

- **Wave**: D (needs LLM-judge architecture + ground-truth set)
- **Priority**: MED
- **Effort**: 4-5 engineer-weeks + 2 weeks pedagogy data labeling
- **Dependencies**: 200-item labeled ground-truth set (Dr. Nadia demand); LLM-judge sidecar architecture
- **Source**: [panel review](../../docs/research/cena-panel-review-user-personas-2026-04-17.md) Round 2.F1

## Problem

Cena is assessment-only today. Students can't tell if they understood or just pattern-matched (pain cluster C1 across Noa, Amir, Daniel, Ofir). Self-explanation effect (Chi 1994, Renkl 1997) is the strongest robust finding in math learning research — but we must not false-positive a "you understand!" when the student explanation is just fluff.

## Scope

**Prompt trigger**: after a student answers correctly, ~20% of the time (rate to be calibrated empirically, NOT hardcoded), ask "why did step 3 work?"

**Judging pipeline**:
1. Student types natural-language explanation (L1)
2. CAS checks: did the student's claimed transformation hold symbolically?
3. LLM judge (sidecar, circuit-broken) checks: did the explanation match the rule invoked?
4. Both must pass for a "you got the reasoning right" affirmation
5. Fallback on LLM timeout (p99 > 2.5s): skip the prompt, don't stall the student

**Ground-truth set** (Dr. Nadia's ship-blocker):
- 200 labeled student explanations across 8 topics
- Each labeled: correct / partially-correct / wrong / irrelevant
- Judge accuracy against set must be ≥ 85% before pilot

**Data handling** (Ran's demand):
- Explanation text NEVER persisted beyond judgment cycle
- Session-scoped per ADR-0003
- No training data, no logs beyond judgment outcome

## Files to Create / Modify

- `src/shared/Cena.Domain/Pedagogy/ExplainItBack.cs`
- `src/shared/Cena.Infrastructure/LlmJudge/LlmJudgeSidecar.cs` — circuit-broken
- `docker/llm-judge-sidecar/` — new sidecar service
- `src/student/full-version/src/components/pedagogy/ExplainPrompt.vue`
- `docs/content/explain-it-back-ground-truth.csv` — 200-item labeled set
- `ops/grafana/llm-judge-dashboard.json` — latency + accuracy monitoring

## Acceptance Criteria

- [ ] Ground-truth set of 200 labeled explanations, 8 topics, 3 languages
- [ ] Judge accuracy ≥ 85% on held-out 40-item test set
- [ ] LLM p99 latency < 2.5s enforced via circuit breaker; falls back silently
- [ ] Explanation text never persisted beyond judgment cycle (audit log confirms)
- [ ] Prompt-fatigue gate: same student never gets > 1 prompt per session
- [ ] Session-scoped data handling per ADR-0003 (Ran verification)

## Success Metrics

- **Judge calibration**: accuracy ≥ 85%, false-positive rate ≤ 10%, false-negative rate ≤ 15%
- **Mastery delta**: students exposed to explain-it-back vs control group: target measurable uplift in delayed post-test
- **Engagement retention**: prompt adoption should not drop session-completion rate more than 3pp
- **Prompt-fatigue signal**: self-reported annoyance < 20% in post-session survey

## ADR Alignment

- ADR-0002: CAS verifies symbolic invariant
- ADR-0003: explanation text session-scoped, never persisted
- GD-004: no shame framing on wrong explanations ("try again, think about X" not "you're wrong")

## Out of Scope

- Automated ground-truth generation (human labeling required for v1)
- Language-specific LLM judges beyond en/he/ar (v2)
- Cross-session pattern detection (forbidden by ADR-0003)

## Assignee

Unassigned; Dr. Nadia leads design + labeling; Dina for sidecar architecture; Iman for circuit breaker + dashboard.
