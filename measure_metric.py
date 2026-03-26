#!/usr/bin/env python3
"""
Autoresearch metric Phase 7: Methodology Switching — Deep Validation.
Catches unverified claims, incomplete switching algorithm, underpowered
A/B test, and missing learning transfer validation.
Target: 0.
"""

import re
from pathlib import Path

DOCS_DIR = Path(__file__).parent / "docs"


def main():
    all_text = {}
    for md_file in sorted(DOCS_DIR.glob("*.md")):
        all_text[md_file.name] = md_file.read_text()

    total_score = 0
    results_by_category = {}

    def flag(weight, description, file_pattern, category):
        nonlocal total_score
        total_score += weight
        if category not in results_by_category:
            results_by_category[category] = []
        results_by_category[category].append((weight, description, file_pattern))

    pr = all_text.get("product-research.md", "")
    so = all_text.get("system-overview.md", "")
    ad = all_text.get("architecture-design.md", "")
    il = all_text.get("intelligence-layer.md", "")

    # === UNVERIFIED CITATION ===

    # 1. IJAIED citation — "2024 IJAIED systematic review" with no author, title, DOI
    if "IJAIED systematic review" in pr or "IJAIED review" in pr:
        # Check for proper academic citation near "IJAIED"
        ijaied_idx = pr.find("IJAIED systematic review")
        ijaied_context = pr[max(0,ijaied_idx-200):ijaied_idx+500] if ijaied_idx >= 0 else ""
        has_proper_cite = any(phrase in ijaied_context for phrase in [
            "doi.org", "DOI:", "et al.", "(20", "Vol.",
        ])
        if not has_proper_cite:
            flag(5, "IJAIED citation is unverifiable — 'confirmed by 2024 IJAIED systematic review' but no author, title, volume, DOI. Either find and properly cite, or downgrade to 'based on our analysis of 7 competitors'",
                 "product-research.md", "CITATION")

    # === INCOMPLETE SWITCHING ALGORITHM ===

    # 2. No tie-breaking when multiple error types present
    if "error-type-driven" in so.lower() or "error type" in so.lower():
        has_tiebreak = any(phrase in so + ad for phrase in [
            "tie-break", "tiebreak", "precedence",
            "multiple error types", "conflicting error",
            "dominant error", "primary error type",
        ])
        if not has_tiebreak:
            flag(4, "Switching rules map error types → methodologies but don't specify tie-breaking when a student shows BOTH procedural and conceptual errors simultaneously. Need: precedence rules or weighted scoring",
                 "system-overview.md", "ALGORITHM")

    # 3. No cycling prevention (what if all methodologies fail)
    if "methodology switch" in so.lower():
        has_cycling = any(phrase in so + ad for phrase in [
            "cycling prevention", "already tried", "exhausted",
            "all methodologies", "method history", "previously attempted",
            "backtrack", "method exclusion",
        ])
        if not has_cycling:
            flag(4, "No cycling prevention: if student tried Feynman→drill→Socratic and all failed, does system loop back? Need: method attempt history per student per concept, escalation when all methods exhausted",
                 "system-overview.md", "ALGORITHM")

    # 4. MCM graph structure unspecified
    if "MCM" in ad or "Mode x Capability x Methodology" in ad:
        has_mcm_spec = any(phrase in ad + il for phrase in [
            "MCM lookup", "MCM query", "MCM structure",
            "MCM table", "MCM format", "MCM schema",
            "methodology_for(error_type, concept_category)",
        ])
        if not has_mcm_spec:
            flag(3, "MCM graph (Mode x Capability x Methodology) is referenced as the switching decision backbone but its structure, lookup algorithm, and fallback logic are never specified. An engineer cannot implement 'consult MCM graph' without this",
                 "architecture-design.md", "ALGORITHM")

    # === UNDERPOWERED A/B TEST ===

    # 5. Sample size too small for moderate effect sizes
    if "100 students" in pr and "a/b test" in pr.lower():
        has_power = any(phrase in pr.lower() for phrase in [
            "power analysis", "effect size", "cohen",
            "statistical power", "sample size calculation",
            "250 per group", "200 per group",
        ])
        if not has_power:
            flag(4, "A/B test uses n=100 per group but no power analysis. EdTech effects are typically d=0.3-0.5 (moderate). Need n=200+ per group for 80% power. Current design has ~35-45% power — will likely miss real effects",
                 "product-research.md", "VALIDATION")

    # 6. No pre-registration or analysis plan
    if "A/B test" in pr:
        has_preregister = any(phrase in pr.lower() for phrase in [
            "pre-register", "preregister", "osf.io", "analysis plan",
            "pre-specified analysis", "ancova", "mixed effects",
        ])
        if not has_preregister:
            flag(3, "A/B test has no pre-registration or pre-specified statistical analysis plan. Without this, risk of p-hacking. Need: pre-register on OSF, specify analysis (ANCOVA with baseline covariate)",
                 "product-research.md", "VALIDATION")

    # 7. No diagnostic metrics for switching behavior itself
    if "Methodology switching A/B" in pr or "methodology switching A/B" in pr:
        has_diagnostics = any(phrase in pr.lower() for phrase in [
            "switches per student", "switch count", "switch frequency",
            "diagnostic metric", "treatment fidelity",
            "avg switches", "average switches",
        ])
        if not has_diagnostics:
            flag(3, "A/B test measures outcomes but not treatment fidelity: how many methodology switches actually happened per student? If system bugs prevent switching, test fails for wrong reason. Need: avg switches per student as diagnostic metric",
                 "product-research.md", "VALIDATION")

    # === MISSING LEARNING TRANSFER ===

    # 8. No validation against real exam outcomes
    if "mastery velocity" in pr.lower() and "bagrut" in pr.lower():
        has_transfer = any(phrase in pr.lower() for phrase in [
            "bagrut score", "exam performance", "learning transfer",
            "end-of-unit exam", "external validation",
            "exam correlation", "bagrut improvement",
        ])
        # Check if there's BOTH internal metric AND external validation plan
        if not has_transfer:
            flag(3, "A/B test primary metric (mastery velocity) is internal to Cena — doesn't prove Bagrut exam improvement. eSelf validated against real exam scores. Need: even a proxy (end-of-unit quiz from actual Bagrut material) as secondary metric",
                 "product-research.md", "VALIDATION")

    # === PRINT RESULTS ===

    print("=" * 70)
    print("METHODOLOGY SWITCHING DEEP VALIDATION (lower=better, target: 0)")
    print("=" * 70)

    for category in ["CITATION", "ALGORITHM", "VALIDATION"]:
        if category in results_by_category:
            items = results_by_category[category]
            cat_total = sum(w for w, _, _ in items)
            print(f"\n  [{category}: {cat_total} points]")
            for weight, desc, fname in items:
                print(f"    (w={weight}) {fname}: {desc}")

    print(f"\n{'=' * 70}")
    for category in ["CITATION", "ALGORITHM", "VALIDATION"]:
        if category in results_by_category:
            print(f"  {category}: {sum(w for w,_,_ in results_by_category[category])}")
    print(f"\n  TOTAL GAP: {total_score}")
    print(f"{'=' * 70}")
    print(f"\nMETRIC:{total_score}")


if __name__ == "__main__":
    main()
