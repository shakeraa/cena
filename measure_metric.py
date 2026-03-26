#!/usr/bin/env python3
"""
Autoresearch metric Phase 6: Honest Business Model & Specification Gaps.
Catches real investor red flags, contradicting financial assumptions, and
specs too vague to implement. No softball checks.
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
    bv = all_text.get("business-viability-assessment.md", "")
    so = all_text.get("system-overview.md", "")
    il = all_text.get("intelligence-layer.md", "")
    fp = all_text.get("fundraising-playbook.md", "")
    aspec = all_text.get("assessment-specification.md", "")
    ca = all_text.get("content-authoring.md", "")

    # === CRITICAL: BUSINESS MODEL CONTRADICTIONS ===

    # 1. Customer lifetime: product-research says 18 months, business-viability says 6-12 months
    if "18 months" in pr and "Average subscription duration" in pr:
        if "realistic: 6-12 months" in bv or "realistic: 6–12 months" in bv:
            # Both exist and contradict — flag unless product-research acknowledges the range
            if "6-12 month" not in pr and "6–12 month" not in pr:
                flag(6, "Customer lifetime: product-research.md claims 18 months, business-viability.md says realistic 6-12 months. LTV, break-even, and LTV:CAC all depend on this — pick one and recalculate",
                     "product-research.md", "BUSINESS_MODEL")

    # 2. Pricing: most expensive AI tool but positioned as accessible savings
    if "most expensive" in bv.lower() and "94% saving" in pr:
        # Check if product-research explicitly names the pricing strategy and addresses the tension
        has_strategy = ("pricing strategy" in pr.lower() and "premium" in pr.lower() and
                       ("not a volume" in pr.lower() or "not volume" in pr.lower() or
                        "premium tutoring replacement" in pr.lower()))
        if not has_strategy:
            flag(5, "Pricing contradiction: business-viability says 'most expensive AI learning tool' but product-research positions as '94% savings'. These are opposite GTM strategies — clarify if this is premium or volume play",
                 "product-research.md", "BUSINESS_MODEL")

    # 3. Break-even doesn't account for structural churn
    if "structural churn" in bv.lower() or "Structural churn" in bv:
        # Check if product-research break-even section addresses churn
        if "churn" not in pr[pr.find("Break-Even"):pr.find("Break-Even")+2500] if "Break-Even" in pr else True:
            flag(5, "Break-even analysis (product-research.md) doesn't model structural churn from student graduation. business-viability.md says 'realistic: 6-12 months' lifetime but break-even assumes steady growth without cohort replacement",
                 "product-research.md", "BUSINESS_MODEL")

    # 4. eSelf/CET free competitor not surfaced in fundraising materials
    if "eSelf" in fp:
        # Check if fundraising playbook mentions the FREE aspect and CET institutional backing
        if "free" not in fp.lower() or "CET" not in fp:
            flag(4, "Fundraising playbook mentions eSelf but omits that it's FREE with CET institutional backing (10,000 students). Investors will discover this — address it proactively",
                 "fundraising-playbook.md", "BUSINESS_MODEL")

    # === HIGH: METHODOLOGY SWITCHING VALIDATION ===

    # 5. Core differentiator has no validation plan
    # Check across all docs that claim methodology switching as differentiator
    claims_switching = ("methodology switching" in pr.lower() and
                       ("novel" in pr.lower() or "no one" in pr.lower() or "none switch" in pr.lower()))
    if claims_switching or ("genuinely novel" in fp.lower()):
        # Check if there's a concrete pilot/validation plan anywhere
        all_docs = pr + so + il + fp
        has_validation = any(phrase in all_docs.lower() for phrase in [
            "methodology switching pilot", "validate methodology switching",
            "pilot with", "pre-launch validation of methodology",
            "methodology a/b test", "methodology switching a/b",
        ])
        if not has_validation:
            flag(5, "Methodology switching is THE core differentiator but has NO concrete validation plan (no pilot size, no timeline, no success metric). Need: 'Pilot with N students, measure X, by date Y'",
                 "product-research.md", "VALIDATION")

    # === HIGH: SPEC GAPS THAT BLOCK IMPLEMENTATION ===

    # 6. Stagnation signal normalization undefined (binary vs continuous)
    if "normalized to [0, 1]" in il or "normalized to [0, 1]" in so:
        # Check if normalization METHOD is specified (not just "normalized to [0,1]")
        has_normalization = any(phrase in il + so for phrase in [
            "sigmoid", "linear interpolation", "min-max", "binary encoding",
            "continuous encoding", "normalization formula",
        ])
        if not has_normalization:
            flag(4, "Stagnation signals are 'normalized to [0,1]' but HOW? Binary (0 or 1 if threshold crossed) vs continuous (proportional to deviation) gives completely different behavior. Specify normalization functions",
                 "intelligence-layer.md", "SPEC_GAP")

    # 7. Cognitive load baselines undefined (when set, what window)
    if "baseline_accuracy" in so:
        # Check if baseline definition is specified
        has_baseline_def = any(phrase in so for phrase in [
            "baseline is set", "baseline window", "baseline computed",
            "trailing average", "sessions to establish baseline",
            "baseline_accuracy is the",
        ])
        if not has_baseline_def:
            flag(4, "Cognitive load formula uses 'baseline_accuracy' and 'baseline_rt' but never defines when/how they're established. First session? Trailing 20-question average? Per-concept or per-student? Two engineers will implement this differently",
                 "system-overview.md", "SPEC_GAP")

    # 8. Mastery threshold 0.85 not justified (check assessment-spec and event-schemas)
    es = all_text.get("event-schemas.md", "")
    if "0.85" in aspec or "0.85" in es:
        has_justification = any(phrase in aspec + so + il for phrase in [
            "why 0.85", "threshold chosen", "sensitivity analysis",
            "Corbett & Anderson", "BKT literature", "threshold justification",
            "threshold rationale",
        ])
        if not has_justification:
            flag(3, "Mastery threshold P(known) >= 0.85 is used everywhere but never justified. BKT literature uses 0.95 (Corbett & Anderson 1994). Why 0.85? What's the sensitivity to +/- 0.05? Add justification or A/B testing plan",
                 "assessment-specification.md", "SPEC_GAP")

    # 9. BKT p_slip/p_guess calibration plan missing
    if "p_slip" in aspec or "p_guess" in aspec:
        has_calibration = any(phrase in aspec + il for phrase in [
            "calibration plan", "empirically fit", "maximum likelihood",
            "pre-launch calibration", "calibrate p_slip",
        ])
        if not has_calibration:
            flag(3, "BKT parameters p_slip=0.10 and p_guess=0.25 are hardcoded defaults with no pre-launch calibration plan. Need: collect N diagnostic attempts, fit parameters via MLE, compare to defaults",
                 "assessment-specification.md", "SPEC_GAP")

    # 10. Content authoring error recovery undefined
    # "content correction" exists but that's about propagating corrections, not the operational procedure
    if "content-authoring.md" in all_text:
        has_error_procedure = any(phrase in ca.lower() for phrase in [
            "rejection rate", "expert rejects", "student reports bug",
            "question reported", "content hotfix", "emergency content",
            "error recovery procedure", "content rollback",
        ])
        if not has_error_procedure:
            flag(3, "Content authoring pipeline specifies creation flow but not error recovery: what happens when expert rejects 50% of LLM-generated questions? When a student finds a bug in a published question? Need operational procedures",
                 "content-authoring.md", "SPEC_GAP")

    # === PRINT RESULTS ===

    print("=" * 70)
    print("HONEST BUSINESS MODEL & SPEC GAPS (lower=better, target: 0)")
    print("=" * 70)

    for category in ["BUSINESS_MODEL", "VALIDATION", "SPEC_GAP"]:
        if category in results_by_category:
            items = results_by_category[category]
            cat_total = sum(w for w, _, _ in items)
            print(f"\n  [{category}: {cat_total} points]")
            for weight, desc, fname in items:
                print(f"    (w={weight}) {fname}: {desc}")

    print(f"\n{'=' * 70}")
    for category in ["BUSINESS_MODEL", "VALIDATION", "SPEC_GAP"]:
        if category in results_by_category:
            print(f"  {category}: {sum(w for w,_,_ in results_by_category[category])}")
    print(f"\n  TOTAL GAP: {total_score}")
    print(f"{'=' * 70}")
    print(f"\nMETRIC:{total_score}")


if __name__ == "__main__":
    main()
