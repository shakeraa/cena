#!/usr/bin/env python3
"""
Autoresearch metric: AI-Agent Development Model Accuracy.
Flags assumptions that don't account for AI coding agents (Claude Code, Kimi)
used by an experienced architect. Target: 0.
"""

import re
from pathlib import Path

DOCS_DIR = Path(__file__).parent / "docs"

CHECKS = [
    # === TEAM SIZE ASSUMPTIONS ===
    # 6-person pre-launch team is pre-AI-agent thinking — only flag if it's the ACTIVE plan, not a comparison
    ("product-research.md", r"^\|\s*\*\*Total pre-launch team\*\*\s*\|\s*\*\*6\*\*",
     "Team sized at 6 people — with AI coding agents (Claude Code, Kimi) + experienced architect, core team should be 2-3 people", 5, "TEAM"),

    # 2 full-stack engineers — AI agents replace most of this (only flag in active team table)
    ("product-research.md", r"^\|\s*Full-Stack Engineer\s*\|\s*2\s*\|",
     "2 full-stack engineers assumed — architect + AI agents can do the work of 2-3 developers", 4, "TEAM"),

    # Dedicated ML/AI engineer — architect with AI agents handles this (only flag in active team table)
    ("product-research.md", r"^\|\s*ML/AI Engineer\s*\|\s*1\s*\|",
     "Dedicated ML/AI engineer — experienced architect with Claude Code handles LLM integration, prompt engineering, and pipeline work", 3, "TEAM"),

    # === TIMELINE ASSUMPTIONS ===
    # 6-month pre-launch — should be 2-3 months with AI agents
    ("product-research.md", r"Pre-Launch.*Months 1.?6|Months 1.?6.*Pre-Launch",
     "6-month pre-launch timeline — AI agents compress this to 2-3 months for experienced architect", 5, "TIMELINE"),

    # Monthly team costs at 120K-160K
    ("product-research.md", r"120,?000.?160,?000/month|~120,?000.?160,?000",
     "Monthly team cost 120K-160K NIS assumes 6-person team — should be 50K-70K for 2-3 person team", 4, "COST"),

    # Pre-seed 3.0-4.0M NIS — way too high for lean AI-augmented team
    ("product-research.md", r"3\.0.?4\.0M NIS",
     "Pre-seed 3.0-4.0M NIS sized for 6-person team — lean AI-augmented team needs 1.2-1.8M NIS", 5, "COST"),

    # Fixed costs 140K-350K/month in growth trajectory
    ("product-research.md", r"Monthly Fixed Costs.*140,?000|140,?000.*Monthly Fixed Costs",
     "Monthly fixed costs of 140K NIS assumes traditional team — AI-augmented team runs at 50-70K initially", 4, "COST"),

    # Growth team of 4 additional people — only flag if dedicated Content Engineer role exists
    ("product-research.md", r"^\|\s*Content Engineer\s*\|\s*1\s*\|",
     "Growth team includes dedicated Content Engineer — AI agents handle content engineering via established pipeline", 3, "TEAM"),

    # system-overview: no mention of AI development tools — only flag if Claude Code/Kimi NOT mentioned in platform section
    ("system-overview.md", r"React Native for iOS and Android(?!.*(?:Claude Code|Kimi|AI.*agent))",
     "Platform section doesn't account for AI-accelerated development approach — should note Claude Code/Kimi agent workflow for development velocity", 2, "TIMELINE"),

    # Break-even at month 12 — only flag if stated as the active target (not comparison)
    ("product-research.md", r"Months to break.?even\s*\|\s*≤12\b",
     "Break-even at Month 12 assumes high burn — with AI-lean team, break-even at Month 6-8", 4, "COST"),

    # Infrastructure costs don't include AI dev tool costs — only flag if AI dev tools line is absent from file
    ("product-research.md", r"^(?![\s\S]*AI development tools)[\s\S]*Infrastructure Costs",
     "Infrastructure costs missing AI development tools line item (Claude Max, Kimi Pro, GitHub Copilot: ~2,000-3,000 NIS/month)", 3, "COST"),

    # Knowledge graph construction 4 weeks per subject — only flag if stated as active plan (not as comparison with "vs.")
    ("product-research.md", r"(?<!vs\.\s)(?<!vs )~?4 weeks per subject",
     "4 weeks per subject for KG construction — with Claude Code + structured extraction pipelines, 1-2 weeks per subject is realistic", 3, "TIMELINE"),
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
    print("AI-AGENT DEVELOPMENT MODEL ACCURACY (lower=better, target: 0)")
    print("=" * 70)

    for category in ["TEAM", "TIMELINE", "COST"]:
        if category in results_by_category:
            items = results_by_category[category]
            cat_total = sum(w for w, _, _ in items)
            print(f"\n  [{category}: {cat_total} points]")
            for weight, desc, fname in items:
                print(f"    (w={weight}) {fname}: {desc}")

    print(f"\n{'=' * 70}")
    for category in ["TEAM", "TIMELINE", "COST"]:
        if category in results_by_category:
            print(f"  {category}: {sum(w for w,_,_ in results_by_category[category])}")
    print(f"\n  TOTAL GAP: {total_score}")
    print(f"{'=' * 70}")
    print(f"\nMETRIC:{total_score}")


if __name__ == "__main__":
    main()
