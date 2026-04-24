# Coverage Rung Status (prr-210)

**Generated**: 2026-04-20T23:38:18.410Z
**Targets**: `contracts/coverage/coverage-targets.yml` (v1)
**Snapshot**: `ops/reports/coverage-variants-snapshot.json` (prr-210-bootstrap-snapshot, runAt=2026-04-20T00:00:00Z)
**Declared cells**: 10 (9 active, 1 draft)
**Active failing**: 0
**Advisory failing (draft)**: 0
**Undeclared cells in snapshot**: 0

Legend: ✅ meets SLO · ❌ below SLO (active, gating) · ⚠️  below SLO (draft, advisory)

## Cells

| Status | Active | Topic | Difficulty | Methodology | Track | Q-type | Variants | Required | Gap | Curator task | Notes |
|--------|--------|-------|------------|-------------|-------|--------|----------|----------|-----|--------------|-------|
| ✅ | active | algebra.linear_equations | Easy | Halabi | FourUnit | multiple-choice | 12 | 10 | 0 | — | 4-unit linear equations, Halabi-Easy MCQ — foundational rung |
| ✅ | active | algebra.linear_equations | Medium | Halabi | FourUnit | multiple-choice | 14 | 10 | 0 | — |  |
| ✅ | active | algebra.linear_equations | Hard | Halabi | FourUnit | multiple-choice | 15 | 10 | 0 | — |  |
| ✅ | active | algebra.linear_equations | Easy | Rabinovitch | FourUnit | multiple-choice | 11 | 10 | 0 | — | Rabinovitch parallel — ADR-0040 methodology independence. |
| ✅ | active | algebra.linear_equations | Medium | Rabinovitch | FourUnit | multiple-choice | 10 | 10 | 0 | — |  |
| ✅ | active | algebra.quadratic_equations | Medium | Halabi | FiveUnit | multiple-choice | 10 | 10 | 0 | — |  |
| ✅ | active | algebra.quadratic_equations | Hard | Halabi | FiveUnit | step-solver | 5 | 5 | 0 | — |  |
| ✅ | active | calculus.differentiation | Medium | Halabi | FiveUnit | step-solver | 6 | 5 | 0 | — |  |
| ✅ | active | calculus.differentiation | Hard | Halabi | FiveUnit | step-solver | 7 | 5 | 0 | — |  |
| ✅ | draft | physics.free_body_diagrams | Medium | Halabi | FiveUnit | fbd-construct | 3 | 3 | 0 | — | Draft cell — FBD authoring still ramping; advisory until prr |

---

Regenerate: `node scripts/shipgate/coverage-slo.mjs`. See `ops/slo/coverage-rung-slo.md` for policy.
