# ADR-0049 — Citation integrity and effect-size communication

- **Status**: Accepted
- **Date proposed**: 2026-04-21
- **Deciders**: Shaker (project owner), claude-code (coordinator), claude-subagent-prr019combo
- **Supersedes**: none
- **Related**:
  - [ADR-0002 (SymPy correctness oracle)](0002-sympy-correctness-oracle.md) — sister "honesty" rule: CAS verifies math; citations verify claims
  - [ADR-0032 (CAS-gated question ingestion)](0032-cas-gated-question-ingestion.md) — same verify-before-ship posture
  - [ADR-0043 (Bagrut reference-only enforcement)](0043-bagrut-reference-only-enforcement.md) — surrounding honesty doctrine
  - [ADR-0048 (exam-prep time framing)](0048-exam-prep-time-framing.md) — companion ADR from the same pre-release review
  - CLAUDE.md non-negotiable #3 — "Dark-pattern engagement mechanics are banned"
  - [docs/engineering/shipgate.md](../engineering/shipgate.md) — ship-gate CI policy
  - [pre-release-review/finding_assessment_dr_rami.md](../../pre-release-review/finding_assessment_dr_rami.md) — Dr. Rami's citation-integrity assessment (20 FD-findings: 9 PASS / 8 WARNING / 3 REJECT)
  - `~/.claude/projects/-Users-shaker-edu-apps-cena/memory/feedback_honest_not_complimentary.md` — harsh-honest numbers + CIs beat soft euphemism (R-28 posture)
- **Task**: prr-042 (pre-release review Epic D — Ship-gate scanner v2, banned-vocabulary expansion)
- **Lens consensus**: persona-cogsci, persona-educator, persona-redteam

---

## Context

The 2026-04-20 ten-persona pre-release review found three **REJECT**-verdict citation failures that had propagated into feature specs, persona-review YAMLs, and marketing-adjacent design docs:

| Finding | Failure mode |
|---------|--------------|
| FD-003 | "95% misconception resolution" — not found in any Eedi/DeepMind publication; actual reported rates are 60–80% across conditions. |
| FD-008 | "Yu et al. 2026" — no such publication exists; the year has not occurred. Appears fabricated or a 2016 typo. |
| FD-011 | d=1.16 effect size for culturally-contextualised problem generation — extreme upper-tail of a heterogeneous meta-analysis; representative estimates are d=0.4–0.6. |

Eight further **WARNING**-verdict findings exhibited softer versions of the same failure mode: cherry-picked upper-tail effect sizes, selective citation that omitted null results, or citations misattributed to adjacent authors.

The **enforcement gap** at the time of the review was that ship-gate's `banned-citations.yml` pack (landed in prr-005) blocked the three REJECT items by regex — it could not block effect-size claims the reviewer had not already anticipated. Any future author inventing a new "d=1.9 from Foo et al. 2027" figure would land in production because the regex did not know about Foo.

The R-28 retirement review established the project's posture on performance numbers: **harsh-honest numbers with confidence intervals beat soft euphemism** (see `feedback_honest_not_complimentary.md`). R-28 did not address the citation-integrity question; it assumed the numbers were correct. Dr. Rami's assessment is the companion rule: **numbers are only as honest as the citation they rest on.**

This ADR codifies a positive-allowlist for effect-size claims so the next author writing UI copy or marketing cannot cite a number without a pre-approved source, and the scanner can enforce the rule without having to know about every possible fabrication in advance.

---

## Decision

**Every effect-size claim in UI copy, marketing surfaces, or feature specs that feed production copy must be backed by an approved citation ID from `contracts/citations/approved-citations.yml`.** The ship-gate scanner fails any claim that is not so backed.

### Rule 1 — Positive allowlist, not regex blocklist

Reviewers cannot anticipate every possible fabrication. Instead of a blocklist of known-bad citations, the project maintains an **allowlist** of approved citations with their verified meta-analytic mean effect sizes and the CI bounds reported in the source meta-analysis. Any effect-size claim in a scanned surface must be tagged with a `citation_id` that resolves against this allowlist, or it fails the scanner.

Form of a claim:

```text
Interleaving improves cumulative retention (d=0.34, 95% CI 0.20–0.48;
  citation_id=rohrer-2020-interleaving).
```

Form of a claim that fails:

```text
Interleaving boosts learning (d=0.8).
```

### Rule 2 — Honest CI ranges, not point estimates

Every effect-size claim must ship with a **95% confidence interval** reported in the source meta-analysis — not a single point estimate. The CI must be the actual CI reported in the source, not a rounded or inflated range. The `approved-citations.yml` entry records both the mean and the CI; the scanner verifies the claim's CI matches within rounding tolerance.

This rule operationalises R-28's posture: numbers are uncertain; CIs make the uncertainty legible.

### Rule 3 — Never inflate beyond the meta-analytic mean

Claims must not assert an effect size **above** the meta-analytic mean recorded in `approved-citations.yml`. A primary study showing d=0.83 cannot be cited as representative when the meta-analysis reports d=0.34 — this is the FD-001 / FD-011 failure mode. If the project genuinely needs to cite an upper-tail primary study, it must cite the study **and** the meta-analytic mean alongside, so the reader sees the heterogeneity. The `approved-citations.yml` entry may carry a `max_cited_es` bound that mirrors the meta-analytic upper CI; the scanner rejects any numeric claim beyond that bound.

### Rule 4 — Ship-gate scanner enforces every rule

The enforcement is a new rule pack `scripts/shipgate/effect-size-citations.yml` consumed by the existing `scripts/shipgate/rulepack-scan.mjs`, plus a companion cross-reference scanner `scripts/shipgate/citation-integrity-scan.mjs` that verifies every `citation_id=` reference in the scanned surfaces against the `approved-citations.yml` manifest. Failures surface as PR comments and block merge.

### Rule 5 — The manifest is the single source of truth

`contracts/citations/approved-citations.yml` lives in the `contracts/` directory alongside other domain contracts. It is reviewed per-PR like a database migration: adding a citation requires the source link, the reported mean, the reported CI, and a one-line reviewer note. Removing a citation retires every claim that depends on it (the scanner catches dangling references on the next scan).

---

## Scope — what this ADR covers and does not cover

### Covered (scanner enforces)

- UI copy in student-web locale bundles (`src/student/full-version/src/plugins/i18n/locales/*.json`)
- UI copy in admin/full-version locale bundles (`src/admin/full-version/src/plugins/i18n/locales/*.json`)
- Vue templates and TypeScript source in both frontends
- Feature specs in `docs/feature-specs/**`, `docs/engineering/**`, `docs/design/**`
- C# backend prompts and hard-coded user-facing strings

### Not covered (out of scope)

- Academic citations in research notes under `docs/research/**` and `pre-release-review/**` — these routinely analyse the rejected claims and are whitelisted.
- ADRs themselves — ADR rationale may quote the banned/rejected citations in their "alternatives considered" or "context" sections.
- Test assertion strings that verify banned copy is absent (`tests/**`, `src/actors/Cena.Actors.Tests/**`).
- Git history — this ADR is forward-looking.

The whitelist in `scripts/shipgate/effect-size-citations-whitelist.yml` mirrors the pattern established by the banned-mechanics and banned-citations packs.

---

## Enforcement layers

Five independent layers prevent a regression:

1. **Citation manifest** (`contracts/citations/approved-citations.yml`) — authoritative list of approved citations with verified meta-analytic mean + CI. Per-PR review.
2. **Effect-size rule pack** (`scripts/shipgate/effect-size-citations.yml`) — regex rules that flag bare effect-size claims (`d=0.34`, `ES of 0.5`, `effect size 1.16`, etc.) that lack a `citation_id=` tag. Consumed by `rulepack-scan.mjs`.
3. **Citation-integrity scanner** (`scripts/shipgate/citation-integrity-scan.mjs`) — cross-references every `citation_id=` tag in scanned files against the manifest. Fails on unknown IDs, retired IDs, or claims whose ES exceeds the manifest's `max_cited_es` bound.
4. **Whitelist discipline** (`scripts/shipgate/effect-size-citations-whitelist.yml`) — policy docs, ADRs, research notes, and persona reviews are the only surfaces allowed to reference unbacked effect-size claims. Reviewed per-PR.
5. **Architecture test** (`tests/shipgate/effect-size-citations.spec.mjs` + `tests/shipgate/citation-integrity.spec.mjs`) — asserts the scanner fires on every rule against the positive-test fixture, and that the manifest round-trips through the cross-reference scanner.

---

## Consequences

- **Positive**: authors writing new UI copy can no longer cite a fabricated or cherry-picked figure without the scanner catching it at PR time. Reviewers have a manifest to audit against rather than having to remember every paper in the evidence base. The honesty posture from R-28 (`feedback_honest_not_complimentary.md`) is operationalised end-to-end: numbers come with CIs, CIs come from verified meta-analyses, and the verification chain is testable.
- **Negative**: every new effect-size claim now requires a manifest entry, which is a small tax on feature development. The tax is deliberate — an author who cannot find an approved citation is being told they do not yet have the evidence to make the claim. If the evidence exists, the 5-line manifest entry costs less than a week's worth of review cycles. The first iteration of the manifest seeds only the citations already vetted by Dr. Rami's assessment (the 9 PASS verdicts); WARNING-verdict citations need a revision cycle before they earn a manifest entry.
- **Operational**: the scanner is advisory-capable via `--advisory`, but the default CI mode is enforcing. A legitimate-use whitelist entry is the escape hatch for research / ADR / test-assertion contexts, not for new production copy. The manifest file is small enough (< 50 entries at MVP) that `git diff` review remains tractable.

---

## Alternatives considered

### Alt 1 — Extend `banned-citations.yml` with every known-bad citation

Same failure mode as the original gap: reviewers cannot anticipate every fabrication. A positive allowlist is strictly stronger than a negative blocklist for this class of problem. Rejected.

### Alt 2 — Block every numeric claim in UI copy

Catches everything but kills legitimate uses like "85% of students completed the unit" where no citation is needed because the number is descriptive of the local dataset, not a research claim. Too broad. Rejected.

### Alt 3 — LLM review of every PR description for citation integrity

Slow, non-deterministic, and not auditable. Shifts the enforcement from CI into a black box. Rejected.

### Alt 4 — Manifest + scanner (CHOSEN)

Deterministic, auditable, fast, and the manifest review is a natural fit for per-PR human review (it looks like a schema migration). Chosen.

---

## Manifest schema

`contracts/citations/approved-citations.yml` has this minimal shape:

```yaml
citations:
  - id: rohrer-2020-interleaving
    authors: "Rohrer et al."
    year: 2020
    title: "Interleaved mathematics practice, cumulative retention"
    source_type: "primary"        # primary | meta-analysis | review
    meta_analytic_mean: 0.34       # Brummair & Richter 2019 meta-analysis
    reported_ci_low: 0.20
    reported_ci_high: 0.48
    max_cited_es: 0.50             # upper CI bound — hard cap for claims
    notes: "Rohrer primary study is d=0.83; meta-analytic mean is d=0.34. Cite the mean."
    added_by: "prr-042"
    approved_on: "2026-04-21"
```

The scanner treats each entry as a contract: a claim tagged with `citation_id=rohrer-2020-interleaving` may assert any effect size up to `max_cited_es`, must ship with a CI that overlaps `[reported_ci_low, reported_ci_high]`, and fails if the claim's numeric assertion exceeds `max_cited_es`.

---

## Review / rotation

- **Manifest** is the living artefact. Additions require PR review; removals trigger a scanner re-run that catches dangling references.
- **Rule pack** (`effect-size-citations.yml`) evolves as new failure patterns emerge. Additions do not require re-opening this ADR.
- **ADR revisit trigger**: if a future pre-release review surfaces a systemic failure mode that the manifest does not capture (e.g. "citations exist but are interpreted out of context"), reopen this ADR and extend the rule-set.
