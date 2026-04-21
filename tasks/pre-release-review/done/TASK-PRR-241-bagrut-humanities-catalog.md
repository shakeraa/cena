# TASK-PRR-241: Catalog extension — Full Bagrut humanities

**Priority**: P1 — promoted to Launch 2026-04-21 (was Post-Launch)
**Effort**: XL (8-12 weeks content-engineering across multiple subjects)
**Lens consensus**: persona-educator, persona-ministry
**Source docs**: persona-educator findings (Hebrew/Lit/History/Tanakh/Civics/Arabic-L2 missing; homeroom teachers have nothing to assign)
**Assignee hint**: content-engineering lead + subject-matter experts per subject
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-g, priority=p1, catalog, content, humanities
**Status**: Blocked on PRR-217 + content budget
**Source**: User scope expansion 2026-04-21 — "all options on release day"
**Tier**: launch
**Epic**: [EPIC-PRR-G](EPIC-PRR-G-sat-pet-content-engineering.md)

---

## Goal

Add the full Bagrut humanities catalog — Hebrew language, Hebrew Literature, History, Tanakh, Civics, Arabic as second language (Hebrew-stream), Arabic Literature (Arab-stream) — with item banks sized for real use. Enables homeroom teachers to assign full class targets at launch.

## Scope

Subjects (per Israeli Bagrut syllabus):

| Subject | Tracks | Streams | Item bank minimum (launch) |
|---|---|---|---|
| Hebrew Language (לשון) | 2U, 3U | Hebrew + Arab (as L2) | ≥150 per track |
| Hebrew Literature (ספרות) | 2U, 3U, 5U | Hebrew | ≥150 per track |
| History (היסטוריה) | 2U, 3U, 5U | Hebrew + Arab (divergent) | ≥150 per track per stream |
| Tanakh (תנ"ך) | 2U, 3U, 5U | Hebrew (non-applicable for Arab stream; covered by Islamic Studies separately) | ≥150 per track |
| Civics (אזרחות) | 2U | Hebrew + Arab (some divergence) | ≥100 per track per stream |
| Arabic as L2 (ערבית — תלמידים יהודים) | 3U, 5U | Hebrew-stream | ≥120 per track |
| Arabic Literature (الأدب العربي) | 3U, 5U | Arab-stream | ≥120 per track |
| Islamic Studies / Christianity | 2U, 3U | Arab-stream (per sector) | ≥100 per track |

- `item_bank_status` tiered:
  - `full`: launch-ready (≥ above minimums).
  - `partial`: some coverage, marked as "in development" in UI (honest, not hidden).
  - `reference-only`: syllabus listed but no item bank yet — teacher can assign for planning/tracking; scheduler declines to run sessions with copy "items coming soon".
- No CAS oracle (non-math); rubric DSL (PRR-033) extended per subject with humanities-appropriate grading rubrics.
- Past-Bagrut corpus (PRR-242) is primary reference for item authoring where available.

## Files

- Catalog entries per subject + track + stream.
- Item-bank directories under `content/bagrut/humanities/`.
- Rubric DSL extensions (PRR-033 coordination).
- Tests: catalog returns correct options, humanities items render correctly in he/ar, item-bank-status flags correctly surfaced.

## Definition of Done

- All 8 subjects in catalog.
- Item banks reach `full` for at least Hebrew Lang, Hebrew Lit, History (the most-taken humanities).
- Other subjects reach at least `partial` at launch with honest UI copy.
- persona-educator sign-off on coverage.
- persona-ministry sign-off on שאלון code accuracy per subject/stream.

## Non-negotiable references

- Memory "Bagrut reference-only".
- Memory "Honest not complimentary" (honest item-bank-status tiering).
- Memory "No stubs — production grade" (no empty item banks ship as "full").
- ADR-0001 (tenancy).

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + coverage audit + sign-offs>"`

## Related

- PRR-220, PRR-242, PRR-033, PRR-239 (Arab-stream), EPIC-PRR-G.
