#!/usr/bin/env python3
"""
Autoresearch metric Phase 9: Fundraising Readiness & Launch Realism.
Catches stale numbers in investor-facing docs, unvalidated assumptions,
and missing pre-launch validation plans.
Target: 0.
"""

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

    fp = all_text.get("fundraising-playbook.md", "")
    pr = all_text.get("product-research.md", "")
    so = all_text.get("system-overview.md", "")
    lr = all_text.get("llm-routing-strategy.md", "")
    ca = all_text.get("content-authoring.md", "")

    # 1. Fundraising playbook still says "Month 8: break-even" but research says Month 13-18
    if "Month 8: break-even" in fp or "Month 8: break" in fp:
        flag(5, "Fundraising playbook says 'Month 8: break-even' but product-research.md says Month 13-18 after structural churn modeling. Investors will catch this discrepancy",
             "fundraising-playbook.md", "STALE")

    # 2. Fundraising playbook missing LTV:CAC ratio
    if "fundraising-playbook.md" in all_text:
        has_ltv_cac = any(p in fp for p in ["LTV:CAC", "LTV/CAC", "lifetime value", "2.1:1"])
        if not has_ltv_cac:
            flag(4, "Fundraising playbook has no LTV:CAC ratio. product-research.md says 2.1:1 at launch (below 3:1 benchmark). Investors expect this number — omitting it looks like you're hiding it",
                 "fundraising-playbook.md", "STALE")

    # 3. 8% conversion assumption has no source
    if "8% free-to-paid" in pr or "8% conversion" in pr:
        has_source = any(p in pr for p in [
            "conversion source", "conversion benchmark source",
            "Monetizely", "Ptolemay", "conversion validated",
        ])
        # Check if there's at least a caveat about the assumption
        has_caveat = any(p in pr.lower() for p in [
            "assumption", "unvalidated", "to be validated",
            "validate conversion", "conversion hypothesis",
        ])
        if not has_source and not has_caveat:
            flag(4, "8% free-to-paid conversion cited as 'EdTech benchmark: 5-10%' but with no source. This drives the entire revenue forecast. Need: either cite the source or flag as hypothesis to validate",
                 "product-research.md", "UNVALIDATED")

    # 4. No Hebrew LLM quality testing plan
    if "Hebrew" in so or "Hebrew" in lr:
        has_hebrew_test = any(p in so + lr + ca for p in [
            "Hebrew quality", "Hebrew benchmark", "Hebrew LLM test",
            "Hebrew evaluation", "Hebrew validation",
            "Hebrew math terminology",
        ])
        if not has_hebrew_test:
            flag(5, "Product depends on LLMs doing Socratic dialogue + answer evaluation IN HEBREW FOR MATH but no Hebrew LLM quality testing plan exists. Need: pre-launch Hebrew math benchmark (10 concepts, evaluate quality of Socratic dialogue + answer grading)",
                 "llm-routing-strategy.md", "UNVALIDATED")

    # 5. Pre-seed ask still includes 1.2M as option despite runway risk
    if "1.2-1.5M" in fp or "1.2–1.5M" in fp:
        has_minimum = any(p in fp for p in [
            "1.5M minimum", "minimum 1.5M", "target 1.5M",
            "1.5M NIS minimum",
        ])
        if not has_minimum:
            flag(3, "Fundraising playbook says '1.2-1.5M NIS' but product-research.md recommends 1.5M minimum (Month 13-18 break-even makes 1.2M = only 14 months runway). Update ask to '1.5M NIS target'",
                 "fundraising-playbook.md", "STALE")

    # 6. Content timeline not integrated into launch milestone
    if "Month 3: MVP" in fp or "Month 3:" in fp:
        has_content_caveat = any(p in fp.lower() for p in [
            "50% of math", "partial content", "content subset",
            "expert review", "content readiness",
        ])
        if not has_content_caveat:
            flag(3, "Fundraising playbook says 'Month 3: MVP launch' but content-authoring.md says expert review takes 8-15 weeks per subject. Playbook should note: 'Month 3: MVP launch with ~50% of Math curriculum; full Math by Month 5'",
                 "fundraising-playbook.md", "STALE")

    # === PRINT ===
    print("=" * 70)
    print("FUNDRAISING READINESS & LAUNCH REALISM (lower=better, target: 0)")
    print("=" * 70)

    for category in ["STALE", "UNVALIDATED"]:
        if category in results_by_category:
            items = results_by_category[category]
            cat_total = sum(w for w, _, _ in items)
            print(f"\n  [{category}: {cat_total} points]")
            for weight, desc, fname in items:
                print(f"    (w={weight}) {fname}: {desc}")

    print(f"\n{'=' * 70}")
    for category in ["STALE", "UNVALIDATED"]:
        if category in results_by_category:
            print(f"  {category}: {sum(w for w,_,_ in results_by_category[category])}")
    print(f"\n  TOTAL GAP: {total_score}")
    print(f"{'=' * 70}")
    print(f"\nMETRIC:{total_score}")


if __name__ == "__main__":
    main()
