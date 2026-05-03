# Ship-gate positive-test fixture: citation-integrity cross-reference (ADR-0049)

**Purpose**: This file deliberately contains:

1. A `citation_id=<unknown-id>` that does not exist in the manifest.
2. A `citation_id=<valid-id>` attached to a numeric claim that exceeds the
   manifest's `max_cited_es` bound.

The CI test suite (`tests/shipgate/citation-integrity.spec.mjs`) asserts that
`citation-integrity-scan.mjs` reports both classes of violation.

**Do not clean this file.** Do not correct the IDs. This file is referenced
by the scanner fixture-mode code path and is not scanned by the production
ship-gate paths.

---

## Case 1 — unknown-citation

- Claim with bogus tag: retention improves (d=0.40, citation_id=fabricated-et-al-3000).

## Case 2 — exceeds-max-es

- Brummair & Richter 2019 mean is d=0.34; claim d=0.90 exceeds cap
  (d=0.90, citation_id=brummair-richter-2019-interleaving).

## Case 3 — valid citation (control; should NOT fire)

- Meta-analytic mean d=0.34 for interleaved mathematics practice
  (citation_id=brummair-richter-2019-interleaving).

- Formative assessment range d=0.55 across studies
  (citation_id=black-wiliam-1998-formative).
