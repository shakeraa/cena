# TASK-PRR-240: Catalog extension — PET Russian-verbal section

**Priority**: P1 — promoted to Launch 2026-04-21 (was Post-Launch)
**Effort**: M (4-6 weeks content-engineering)
**Lens consensus**: persona-educator
**Source docs**: persona-educator open-question (Russian-verbal olim population)
**Assignee hint**: content-engineering lead + Russian-native SME
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-g, priority=p1, catalog, content, pet, russian
**Status**: Blocked on PRR-217 + content budget
**Source**: User scope expansion 2026-04-21
**Tier**: launch
**Epic**: [EPIC-PRR-G](EPIC-PRR-G-sat-pet-content-engineering.md)

---

## Goal

Add PET Verbal Reasoning — Russian native section to the catalog + item bank. Serves the FSU-olim population taking the Psychometric Entrance Test in Russian.

## Scope

- Catalog: PET exam entry exposes `verbal_language_options: [hebrew, arabic, russian]`.
- Item bank: ~200 Russian-native verbal reasoning items authored by native-Russian SME. **NOT machine-translated** from Hebrew.
- Item format parallels Hebrew-verbal + Arabic-verbal sections: vocabulary, analogies, sentence completion, logic, reading comprehension.
- Scoring: composite scoring logic accounts for Russian-verbal similarly to other-language verbal sections.
- Student UX: language selection per-target-plan step (PRR-221) defaults to profile locale but explicit for PET targets.
- NITE alignment: verify Russian-verbal section structure matches official PET format.

## Files

- Catalog entry extension.
- `content/pet/verbal-russian/` item-bank directory (new).
- Item-authoring pipeline Russian path.
- Tests: Russian-verbal items render correctly, score per PET composite, no machine-translation detection in content audit.

## Definition of Done

- ≥200 Russian-verbal items authored + reviewed.
- PET composite scoring validates correctly for Russian-verbal test-taker.
- Native-Russian SME sign-off on item quality.
- CAS oracle does not apply (non-math), but PET-specific quality checks documented.
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- Memory "Language Strategy" (Russian not in primary three but olim population warrants first-class treatment).
- Memory "No stubs — production grade".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + SME sign-off>"`

## Related

- PRR-220, PRR-221 (language picker per target), EPIC-PRR-G.
