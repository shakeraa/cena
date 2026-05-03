# TASK-PRR-239: Catalog extension — Arab-stream (המגזר הערבי) Bagrut variants

**Priority**: P1 — promoted to Launch 2026-04-21 (was Post-Launch)
**Effort**: L (4-6 weeks content-engineering)
**Lens consensus**: persona-educator, persona-ministry
**Source docs**: persona-ministry open-question (Arab-stream שאלון codes), persona-educator (bilingual student base)
**Assignee hint**: content-engineering lead + Arabic-stream SME
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-g, priority=p1, catalog, content, arab-stream
**Status**: Blocked on PRR-217 (ADR-0049 catalog shape), PRR-220 (catalog service), content budget approval
**Source**: User scope expansion 2026-04-21
**Tier**: launch
**Epic**: [EPIC-PRR-G](EPIC-PRR-G-sat-pet-content-engineering.md)

---

## Goal

Extend the canonical exam catalog with Arab-stream (המגזר הערבי) Bagrut variants that have different שאלון numeric codes from the Hebrew-stream variants. Ensure item bank tagged per-stream where content differs (not just translation).

## Scope

- Catalog entries per subject carry both streams:
  - `question_papers[]` includes stream-specific entries: `{code: "035581", stream: "hebrew"}`, `{code: "0XXXXX", stream: "arab"}` for every subject where the streams differ.
  - Arabic-stream item bank drawn from Arab-stream שאלונים as reference corpus (PRR-242 feeds it).
- Subjects where streams differ meaningfully in content (not just language):
  - Literature (Hebrew vs. Arabic literature curricula).
  - History (sometimes divergent).
  - Civics (divergent on some topics).
  - Language arts (native-language specific).
- Subjects where the content is parallel (Math, Physics, Chemistry, Biology): same item bank, rendered in Arabic with `<bdi dir="ltr">` for math.
- Item tagging: every item carries `streams: ["hebrew"|"arab"|"both"]`. Scheduler filters by student's catalog selection.
- `persona-ministry` sign-off on שאלון code accuracy per stream.

## Files

- `src/api/Cena.Student.Api.Host/Catalog/CatalogData/global-catalog.json` — stream-aware entries.
- Item-bank metadata schema extension.
- Item-authoring pipeline extension (PRR-242 corpus feeds both streams).
- Tests: catalog returns correct stream options, item filtering by stream, Arabic rendering verified.

## Definition of Done

- All Arab-stream Bagrut subjects represented.
- שאלון codes validated by persona-ministry.
- Item bank for divergent-content subjects reaches PRR-G minimum sizes.
- persona-educator sign-off on content parity.
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- Memory "Language Strategy" (EN primary, AR/HE secondary but both first-class for Israeli Bagrut).
- Memory "Bagrut reference-only" (Arab-stream שאלונים treated the same way).
- ADR-0001 (tenancy).

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + sign-offs>"`

## Related

- PRR-220, PRR-242, PRR-033.
