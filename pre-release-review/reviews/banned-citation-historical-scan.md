# Banned-citation historical scan report

**Run date**: 2026-04-20
**Scanner**: `scripts/shipgate/rulepack-scan.mjs` (pack=citations)
**Rule pack**: `scripts/shipgate/banned-citations.yml` (6 rules)
**Source authority**: `pre-release-review/finding_assessment_dr_rami.md`
**Scope**: production surfaces — `src/student/full-version/src/**`,
`src/admin/full-version/src/**`, `src/actors/**/*.cs`, `src/api/**/*.cs`,
`src/shared/**/*.cs`, `docs/feature-specs/**`, `docs/engineering/**`,
`docs/design/**`. Excluded: `tests/**`, `fixtures/**`, research docs.

## Rule coverage

| Rule ID | Finding | Reason |
|---|---|---|
| `fd003-95-percent-resolution` | FD-003 | "95% misconception resolution" near misconception/resolution context |
| `fd003-954-percent-literal` | FD-003 | "95.4%" literal specificity |
| `fd008-yu-2026` | FD-008 | "Yu et al. 2026" / "Yu 2026" / "Yu and colleagues 2026" |
| `fd011-d-116` | FD-011 | "d=1.16" / "effect size 1.16" / "d of 1.16" |
| `hattie-144-misuse` | hattie-misuse | "d=1.44" near self-reported/planning/reflective context |
| `interleaving-inflation` | interleaving-inflation | "d=0.5-0.8" near interleaving |

## Findings

**Total violations**: 0

Zero production-surface hits. Every reference to the rejected citations is
confined to the pre-release-review corpus (audit docs, retirement logs,
task bodies) or the positive-test fixture, all of which are whitelisted.

## Interpretation

The ship-gate citations rule pack is **clean on landing**. No feature specs,
design docs, actor code, or UI strings currently cite the rejected FD-003 /
FD-008 / FD-011 / Hattie-1.44 / interleaving-0.5-0.8 patterns. The pack will
now block any future PR attempting to introduce them.

If Dr. Rami's review identifies additional rejected citations in a future
pass, add rules to `scripts/shipgate/banned-citations.yml` and re-run this
scan.

## Follow-up tasks

None. The historical corpus is clean. Adjacent tasks that touch the same
citation set (prr-027 remove 95% claim, prr-028 replace Yu 2026, prr-121
retire d=1.16, prr-170 honest interleaving ES) remain scoped to the docs
where those citations already live — those docs are in the whitelist and
are expected to continue discussing the rejected claims as part of the
retirement-of-evidence process.

## Reproducibility

```bash
node scripts/shipgate/rulepack-scan.mjs --pack=citations --json
```

Exit 0 means clean.
