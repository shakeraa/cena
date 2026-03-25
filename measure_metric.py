#!/usr/bin/env python3
"""
Autoresearch metric: Factual Accuracy & ROI Validity Score.
Each issue is a penalty point. Target: 0.

Categories:
1. FACTUAL ERRORS — claims that contradict verified data
2. MATH ERRORS — calculations that don't add up internally
3. INCONSISTENCIES — contradictions between sections or documents
4. UNVERIFIABLE CLAIMS — presented as facts but sourced from single unverifiable reports
5. OUTDATED DATA — figures that were correct but are now stale
"""

import re
from pathlib import Path

DOCS_DIR = Path(__file__).parent / "docs"

# Each check: (file_pattern, search_pattern, description, weight, category)
CHECKS = [
    # === FACTUAL ERRORS ===

    # 5-unit Math: claim says ~35,000 but Taub Center data shows 10.6-13.8% of cohort = ~15,000-20,000
    ("product-research.md", r"~?35,?000.*(?:5-unit|5 unit).*Math",
     "5-unit Math Bagrut students claimed as ~35,000; Taub Center data (2016) shows 13.8% of cohort = ~15,000-20,000", 5, "FACTUAL"),

    # 5-unit Physics: claim says ~20,000, likely ~8,500-10,000
    ("product-research.md", r"~?20,?000.*(?:5-unit|5 unit).*Physics",
     "5-unit Physics Bagrut students claimed as ~20,000; no public source supports this — Bank of Israel data suggests ~8,500-10,000", 5, "FACTUAL"),

    # Chegg gross margin: claim says 74%, actual GAAP full-year 2023 = 68%
    ("product-research.md", r"Chegg\s*=?\s*74%",
     "Chegg gross margin claimed as 74%; actual FY2023 GAAP = 68% (Chegg investor relations)", 4, "FACTUAL"),

    # Coursera gross margin: claim says 59%, actual GAAP = 53%
    ("product-research.md", r"Coursera\s*=?\s*59%",
     "Coursera gross margin claimed as 59%; actual FY2023-2024 GAAP = 53% (Coursera investor relations)", 4, "FACTUAL"),

    # Duolingo DAU outdated: says 34M, now 50M+ as of Q3 2025
    ("product-research.md", r"(?:34M|34\s*million).*DAU|DAU.*(?:34M|34\s*million)",
     "Duolingo DAU cited as 34M (2024 figure); now 50M+ as of Q3 2025 (Duolingo investor relations)", 3, "OUTDATED"),

    # Duolingo "4.5x over 4 years" — actually ~5 years (2019-2024)
    ("product-research.md", r"4\.5x\s*over\s*4\s*years",
     "Duolingo growth described as '4.5x over 4 years'; the timeframe was ~5 years (2019-2024)", 2, "FACTUAL"),

    # === MATH / INTERNAL CONSISTENCY ERRORS ===

    # NIS/USD rate inconsistency: 3,000 NIS = ~$800 implies 3.75 rate; 799 NIS = ~$200 implies 4.0 rate
    ("product-research.md", r"799.*\~?\$200|~?\$200.*799",
     "NIS/USD rate inconsistent: 3,000 NIS=$800 (rate 3.75) vs 799 NIS=$200 (rate 4.0); use consistent rate", 3, "MATH"),

    # Check revenue math: 59 NIS/mo × 12 × 100K × 12% = should be 8,496,000 not 5.99M
    # 59 × 12 = 708 annual. 708 × 100K × 0.12 = 8,496,000 NIS. Doc says 5.99M.
    # Actually: annual price is 499, not 59×12. So 499 × 100K × 0.12 = 5,988,000 ≈ 5.99M ✓
    # Let me check mid tier: 799 × 100K × 0.08 = 6,392,000 ≈ 6.39M ✓
    # Premium: 999 × 100K × 0.05 = 4,995,000 ≈ 4.99M ✓
    # Revenue math checks out. No penalty needed.

    # LTV calculation: 89 NIS/month × 18 months = 1,602 NIS, claimed as ~1,600 ✓
    # LTV:CAC = 1,600/150 = 10.67, claimed as >10:1 ✓
    # Payback period: 150 / (89 - 7 to 15) = 150/74 to 150/82 = 1.8-2.0 months, claimed <2 months ✓

    # Break-even: 120,000 / (74 to 82) = 1,463 to 1,621, claimed ~1,500-1,600 ✓
    # At 8% of 100K = 8,000 max. 1,600/8,000 = 20%. claimed ~20% ✓

    # Gross margin check: (89 - 7) / 89 = 92.1%; (89 - 15) / 89 = 83.1%. Claimed 83-92%. ✓

    # Blended ARPU check: If 8% convert at avg 799/yr, blended = 799 × 0.08 = 63.92.
    # Doc says 56-72. Range uses 699-899: 699×0.08=55.92, 899×0.08=71.92. ✓

    # Check funding runway: 120K-160K team + 15K-30K infra = 135K-190K/mo.
    # 18 months × 135K = 2.43M; 18 months × 190K = 3.42M.
    # But funding says 1.5-2.5M for 18 months. At low end 1.5M / 18 = 83K/mo — way too low for 120K team!
    # Even at 2.5M / 18 = 139K/mo — barely covers the low-end team alone (120K), no infra, marketing, legal.
    ("product-research.md", r"1\.5.?2\.5M NIS.*18 months|18 months.*1\.5.?2\.5M",
     "Funding of 1.5-2.5M NIS for 18mo doesn't cover stated costs: team alone is 120K-160K/mo × 18 = 2.16-2.88M, plus infra (15-30K/mo) + marketing (100K) + legal. Minimum realistic: 3.0-4.0M NIS", 5, "MATH"),

    # The funding section says "6-person team (12 months)" but headline says 18 months runway
    ("product-research.md", r"6-person team \(12 months\)",
     "Funding covers '6-person team (12 months)' but pre-seed target says '18 months of runway' — contradictory", 3, "INCONSISTENCY"),

    # === UNVERIFIABLE CLAIMS (presented as facts) ===

    # Israeli EdTech market $1.2B — check if caveat is present
    ("product-research.md", r"\$1\.2\s*billion(?!.*(?:estimate|Ken Research|single.source|not.*verified|unverified))",
     "Israeli EdTech market size '$1.2 billion' sourced from single Ken Research report — unverifiable; should be marked as estimate", 3, "UNVERIFIABLE"),

    # $120M government allocation — check if caveat is present
    ("product-research.md", r"allocated\s*~?\$120M",
     "~$120M government allocation for digital education — stated as fact without primary source caveat", 2, "UNVERIFIABLE"),

    # NIS to USD at $190-$245 for 699-899 NIS uses inconsistent rate
    ("product-research.md", r"\$190.?\$245",
     "ARPU '$190-$245' for 699-899 NIS uses rate ~3.67; should be consistent with other conversions in document", 2, "MATH"),

    # Duolingo pricing outdated: only flag if $84 is stated as current (not historical)
    ("product-research.md", r"Duolingo.*at\s*\$84/year(?!.*was\b)",
     "Duolingo annual pricing cited as $84/year (2024 price); now ~$60/year as of 2025", 2, "OUTDATED"),

    # 100K Bagrut students — only flag if not sourced with CBS caveat
    ("product-research.md", r"(?<!~85,000.{0,20})100,?000\s*Bagrut\s*students(?!.*CBS)",
     "100,000 Bagrut students/year is plausible but imprecise — needs CBS sourcing caveat", 2, "UNVERIFIABLE"),
]


def main():
    all_text = {}
    for md_file in sorted(DOCS_DIR.glob("*.md")):
        all_text[md_file.name] = md_file.read_text()

    total_score = 0
    results_by_category = {}

    for file_pattern, pattern, description, weight, category in CHECKS:
        if file_pattern in all_text:
            text = all_text[file_pattern]
            if re.search(pattern, text, re.IGNORECASE | re.DOTALL):
                total_score += weight
                if category not in results_by_category:
                    results_by_category[category] = []
                results_by_category[category].append((weight, description, file_pattern))

    # Print report
    print("=" * 70)
    print("FACTUAL ACCURACY & ROI VALIDITY (lower is better, target: 0)")
    print("=" * 70)

    for category in ["FACTUAL", "MATH", "INCONSISTENCY", "UNVERIFIABLE", "OUTDATED"]:
        if category in results_by_category:
            items = results_by_category[category]
            cat_total = sum(w for w, _, _ in items)
            print(f"\n  [{category}: {cat_total} points]")
            for weight, desc, fname in items:
                print(f"    (w={weight}) {fname}: {desc}")

    print(f"\n{'=' * 70}")
    for category in ["FACTUAL", "MATH", "INCONSISTENCY", "UNVERIFIABLE", "OUTDATED"]:
        if category in results_by_category:
            cat_total = sum(w for w, _, _ in results_by_category[category])
            print(f"  {category}: {cat_total}")
    print(f"\n  TOTAL FACTUAL ACCURACY GAP: {total_score}")
    print(f"{'=' * 70}")
    print(f"\nMETRIC:{total_score}")


if __name__ == "__main__":
    main()
