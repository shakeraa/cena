#!/usr/bin/env python3
"""
Autoresearch metric Phase 5: Cross-Document Consistency & Completeness.
Catches inconsistencies between documents, missing specs, and weak points
that undermine investor confidence or block engineering implementation.
Target: 0.
"""

import re
from pathlib import Path

DOCS_DIR = Path(__file__).parent / "docs"

CHECKS = [
    # === CROSS-DOCUMENT INCONSISTENCIES ===

    # GPT-4o still referenced as fallback — actual architecture uses Kimi K2.5
    ("product-research.md", r"GPT-4o",
     "GPT-4o referenced as fallback — actual architecture uses Kimi K2.5 (see llm-routing-strategy.md)", 4, "CONSISTENCY"),

    # GPT-4o in adaptive-learning-architecture-research.md
    ("adaptive-learning-architecture-research.md", r"GPT-4o",
     "GPT-4o referenced in research doc — should reference Kimi K2.5 as the fallback/cheap tier", 3, "CONSISTENCY"),

    # Methodology types in api-contracts.md don't match event-schemas.md
    # api-contracts has 7 types (socratic-dialogue, worked-examples, scaffolded-practice, visual-spatial, analogy-based, error-analysis, spaced-retrieval)
    # event-schemas has 8 types (socratic, spaced_repetition, feynman, project_based, blooms_progression, worked_example, analogy, retrieval_practice)
    # Check: api-contracts is missing feynman, project_based, blooms_progression; has scaffolded-practice, visual-spatial, error-analysis instead
    ("api-contracts.md", r'"scaffolded-practice"',
     "api-contracts MethodologyType 'scaffolded-practice' not in event-schemas (which has 'feynman', 'project_based', 'blooms_progression'). Methodology type lists must be canonical across docs", 5, "CONSISTENCY"),

    # Fundraising playbook says break-even at ~1,600 but product-research says 1,620-2,340
    ("fundraising-playbook.md", r"1,600 subscribers",
     "Fundraising playbook says break-even at ~1,600 subscribers but product-research.md says 1,620-2,340. Use consistent number", 3, "CONSISTENCY"),

    # Operations monitoring chart shows 10% threshold line but alert is at 5%
    ("operations.md", r"10% threshold line",
     "Operations dashboard chart shows '10% threshold line' but the LLM error alert triggers at >5%. Chart should match alert threshold", 2, "CONSISTENCY"),

    # === MISSING SPECIFICATIONS ===

    # Stagnation signal weights — no default values specified anywhere
    ("intelligence-layer.md", r"(?:re-weight|Stagnation Signal Weights)(?![\s\S]{0,500}default_weight|[\s\S]{0,500}\|\s*Signal\s*\|\s*Default Weight)",
     "Stagnation signal weights mentioned but no default weights provided. Implementation blocked without initial values", 4, "MISSING_SPEC"),

    # Cognitive load threshold — no formula for what constitutes overload
    ("system-overview.md", r"cognitive load(?![\s\S]{0,800}threshold\s*[:=]|[\s\S]{0,800}overload\s*[:=]|[\s\S]{0,800}formula)",
     "Cognitive load profiling described but no threshold formula for when to end a session", 4, "MISSING_SPEC"),

    # Offline sync conflict resolution — no algorithm specified
    ("offline-sync-protocol.md", r"conflict.*resolution|Server-authoritative",
     "Offline sync mentions conflict resolution but no formal algorithm (LWW, vector clocks, CRDTs)", 4, "MISSING_SPEC"),

    # Performance SLAs not defined — no P50/P95/P99 latency targets
    ("system-overview.md",
     r"^(?![\s\S]*(?:P50|P95|P99|latency target|\bSLA\b|response time target))[\s\S]*Interactive sessions",
     "No performance SLAs defined (P50/P95/P99 latency targets for question generation, answer evaluation, page load)", 3, "MISSING_SPEC"),

    # API versioning/deprecation policy not specified
    ("api-contracts.md",
     r"^(?![\s\S]*(?:versioning policy|deprecation policy|backward.?compat|API version))[\s\S]*SignalR",
     "No API versioning or deprecation policy defined — critical for mobile app deployment where old clients persist", 3, "MISSING_SPEC"),

    # === CROSS-REFERENCE GAPS ===

    # Cross-doc audit fix tracker has empty Fixed? columns — all show just " |"
    ("cross-doc-audit.md", r"\| I\d+:.*\|\s*\|$",
     "Cross-doc audit Fix Tracker has unfilled 'Fixed?' column — should track which issues have been resolved", 3, "CROSS_REF"),

    # engagement-signals-research.md not referenced from architecture-design.md
    ("architecture-design.md",
     r"^(?![\s\S]*engagement-signals-research\.md)[\s\S]*StagnationDetector",
     "architecture-design.md references StagnationDetector but doesn't link to engagement-signals-research.md", 2, "CROSS_REF"),

    # competitor-eself-deep-dive.md not integrated into fundraising playbook
    ("fundraising-playbook.md",
     r"^(?![\s\S]*competitor-eself-deep-dive\.md|[\s\S]*eself.*deep.dive)[\s\S]*eSelf",
     "Fundraising playbook mentions eSelf but doesn't reference the detailed competitor-eself-deep-dive.md analysis", 2, "CROSS_REF"),
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
            if re.search(pattern, text, re.IGNORECASE | re.MULTILINE):
                total_score += weight
                if category not in results_by_category:
                    results_by_category[category] = []
                results_by_category[category].append((weight, description, file_pattern))

    print("=" * 70)
    print("CROSS-DOCUMENT CONSISTENCY & COMPLETENESS (lower=better, target: 0)")
    print("=" * 70)

    for category in ["CONSISTENCY", "MISSING_SPEC", "CROSS_REF"]:
        if category in results_by_category:
            items = results_by_category[category]
            cat_total = sum(w for w, _, _ in items)
            print(f"\n  [{category}: {cat_total} points]")
            for weight, desc, fname in items:
                print(f"    (w={weight}) {fname}: {desc}")

    print(f"\n{'=' * 70}")
    for category in ["CONSISTENCY", "MISSING_SPEC", "CROSS_REF"]:
        if category in results_by_category:
            print(f"  {category}: {sum(w for w,_,_ in results_by_category[category])}")
    print(f"\n  TOTAL GAP: {total_score}")
    print(f"{'=' * 70}")
    print(f"\nMETRIC:{total_score}")


if __name__ == "__main__":
    main()
