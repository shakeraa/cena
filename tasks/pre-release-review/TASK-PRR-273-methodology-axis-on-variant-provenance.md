# TASK-PRR-273: Methodology axis on variant provenance (G3; QuestionDocument extension)

**Priority**: P0 — coverage-SLO methodology cells under-fill; coverage-counting honesty
**Effort**: M (3-5 days; data-model + LLM prompt + projection + tests)
**Lens consensus**: claude-5 self-audit 2026-04-29 G3; claude-1 ACK on m_0485821c116e (no objection); coverage matrix axis (per `contracts/coverage/coverage-targets.yml`)
**Source docs**: [contracts/coverage/coverage-targets.yml](../../contracts/coverage/coverage-targets.yml) (methodology axis: Halabi | Rabinovitch), [ADR-0050](../../docs/adr/0050-multi-target-student-exam-plan.md) (per-target track-policy), [ADR-0043](../../docs/adr/0043-bagrut-reference-only-enforcement.md)
**Assignee hint**: backend (QuestionDocument owner); coordinate with PRR-272 owner since both touch the projection
**Tags**: source=claude-5-audit-2026-04-29,epic=epic-prr-n,priority=p0,coverage-slo,methodology,backend
**Status**: Ready
**Tier**: launch-adjacent
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

The coverage matrix (per `coverage-targets.yml`) requires a `methodology` axis with values `Halabi` (Israeli teaching default) and `Rabinovitch` (secondary). Variants generated from a Ministry source today carry **no methodology** — `BagrutAlignment` does not encode it (Halabi vs Rabinovitch is a teaching-tradition distinction not in Ministry exam codes).

Result: variants from Ministry sources default-bucket on methodology. Methodology-specific cells under-fill; the global default inflates. Coverage SLO reads inaccurate per-methodology counts.

This task introduces methodology as an authoring-time tag on the variant generation path, persists it on `QuestionDocument`, and updates the coverage projection.

## Scope

### Data model

1. **Add `Methodology: string?`** field to `QuestionDocument` (or wherever variants persist; coordinate with PRR-272). Values: `"Halabi"` | `"Rabinovitch"` | `null` (methodology-agnostic).
2. **Mirror onto `QuestionReadModel`** so the coverage projection consumes it.

### Authoring path

3. **LLM prompt context for variant generation**: extend `VariantPromptBuilder` (or wherever the Tier-3 structural variant prompt is assembled) to include methodology context. Source of truth (in priority order):
   1. `ExamTarget.TrackPolicy.Methodology` if the active target encodes one.
   2. `InstitutePricingResolver.MethodologyDefault` if the institute has a default (extend resolver if needed; coordinate with PRR-253).
   3. `null` (methodology-agnostic) — variant prompt makes no methodology claim; persisted as `null`.
4. **Parametric variants** carry the methodology of their source if available (no LLM-driven inference; pure passthrough).

### Coverage projection

5. **`CoverageTargetManifest` cell key** uses the methodology field directly. Variants with `Methodology = null` count toward the methodology-agnostic default cells, not Halabi or Rabinovitch specifically.
6. **Backfill rebuild**: existing rows get `Methodology = null` (cannot infer post-hoc from source); admin can manually re-tag via a separate task if needed.

### Tests

7. Unit: variant authored with `Methodology = "Halabi"` lands in the Halabi-keyed cell, not the global default.
8. Unit: variant authored with `Methodology = null` does NOT contaminate Halabi or Rabinovitch counts.
9. Architecture test: `CoverageMethodologyAxisTest.cs` — every coverage cell with `Methodology` in {`Halabi`, `Rabinovitch`} MUST receive contributions only from variants with matching `Methodology` field. Fails build if the projection cross-bucks.
10. E2E: 100 variants authored under Halabi context produce 100 Halabi cell contributions and 0 Rabinovitch contributions.

## Files

### Modified
- `src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs` — add `Methodology` field.
- `src/actors/Cena.Actors/Questions/QuestionReadModel.cs` — mirror.
- `src/actors/Cena.Actors/Llm/VariantPromptBuilder.cs` (or equivalent) — methodology context injection.
- `src/actors/Cena.Actors/QuestionBank/Coverage/CoverageTargetManifest.cs` — methodology-axis cell-keying.
- `src/actors/Cena.Actors/Pricing/InstitutePricingResolver.cs` — IF resolver extension is needed (coordinate with PRR-253).

### New
- `src/actors/Cena.Actors.Tests/Architecture/CoverageMethodologyAxisTest.cs` (arch test).
- `src/actors/Cena.Actors.Tests/Llm/VariantMethodologyTaggingTests.cs` (unit + integration).

## Definition of Done

- `QuestionDocument.Methodology` populated on every new variant (Halabi / Rabinovitch / null).
- LLM prompt context honors methodology hierarchy (target → institute → null).
- Coverage cells fill correctly per methodology.
- Arch test green.
- Full `Cena.Actors.sln` build green.

## Blocking

- Coordinate with PRR-272 on `QuestionDocument` projection extensions (both touch the same surface).
- Coordinate with PRR-253 on `InstitutePricingResolver` extension (if needed).

## Non-negotiable references

- `contracts/coverage/coverage-targets.yml` (methodology axis spec)
- ADR-0050 §15.5 (per-target track-policy)
- Memory "Senior Architect mindset" — read coverage system first

## Reporting

`node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + projection extension sha + LLM prompt context test sha>"`
