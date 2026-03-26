#!/usr/bin/env python3
"""
Autoresearch metric Phase 8: Ground Claims in Real Research.
Every key claim must be backed by a verifiable academic citation.
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
    aspec = all_text.get("assessment-specification.md", "")
    mr = all_text.get("mastery-measurement-research.md", "")

    # === 1. ITS effectiveness claim needs Ma et al. (2014) meta-analysis ===
    if "intelligent tutoring" in pr.lower() or "adaptive learning" in pr.lower():
        has_ma = any(p in pr for p in ["Ma et al", "Ma, Adesope", "g = .42", "g = 0.42", "14,321"])
        if not has_ma:
            flag(4, "Claims about ITS effectiveness but doesn't cite Ma et al. (2014) meta-analysis (107 effect sizes, 14,321 participants, g=0.42 vs teacher-led). This is THE foundational evidence for ITS",
                 "product-research.md", "CITATION_NEEDED")

    # === 2. BKT mastery threshold needs Corbett & Anderson (1994) with 0.95 acknowledgment ===
    if "0.85" in aspec:
        has_corbett_full = ("Corbett" in aspec and ("0.95" in aspec or "95%" in aspec))
        if not has_corbett_full:
            flag(4, "Uses P(known) >= 0.85 mastery threshold but doesn't acknowledge that Corbett & Anderson (1994) standard is 0.95. Need: cite the original, explain why Cena chose 0.85 (faster progression, A/B testable)",
                 "assessment-specification.md", "CITATION_NEEDED")

    # === 3. HLR needs Settles & Meeder (2016) ACL paper ===
    if "half-life regression" in ad.lower() or "half-life regression" in so.lower():
        has_settles = any(p in ad + so + il for p in ["Settles", "ACL 2016", "Settles & Meeder", "Settles and Meeder"])
        if not has_settles:
            flag(3, "Uses Half-Life Regression for spaced repetition but doesn't cite Settles & Meeder (2016) 'A Trainable Spaced Repetition Model for Language Learning', ACL. This is the foundational HLR paper from Duolingo",
                 "architecture-design.md", "CITATION_NEEDED")

    # === 4. KST/ALEKS needs Doignon & Falmagne ===
    if "Knowledge Space Theory" in aspec or "ALEKS" in aspec:
        has_doignon = any(p in aspec for p in ["Doignon", "Falmagne", "1999", "Springer-Verlag"])
        if not has_doignon:
            flag(3, "Uses Knowledge Space Theory (ALEKS-inspired) but doesn't cite Doignon & Falmagne (1999) 'Knowledge Spaces' (Springer). This is the mathematical foundation",
                 "assessment-specification.md", "CITATION_NEEDED")

    # === 5. eSelf pilot results need proper citation ===
    if "3.94" in pr or "eSelf" in pr:
        has_eself_cite = any(p in pr for p in [
            "prnewswire", "Morningstar", "2,031 students", "1,841",
            "Harvard", "MIT Media Lab", "August 2025",
        ])
        if not has_eself_cite:
            flag(3, "Cites eSelf's 3.94-point improvement but without proper source. Need: cite the August 2025 PR Newswire/Morningstar study (2,031 students, Harvard/MIT Media Lab validated)",
                 "product-research.md", "CITATION_NEEDED")

    # === 6. Squirrel AI MCM attribution ===
    if "MCM" in ad:
        has_squirrel_mcm = any(p in ad + il for p in ["Squirrel AI", "Squirrel Ai", "IALS"])
        if not has_squirrel_mcm:
            flag(3, "Uses 'MCM graph' (Mode x Capability x Methodology) but doesn't attribute to Squirrel AI's proprietary MCM model. Need: acknowledge the inspiration and differentiate Cena's adaptation",
                 "architecture-design.md", "CITATION_NEEDED")

    # === 7. RL for pedagogical strategy selection needs Chi et al. (2011) ===
    if "methodology switch" in so.lower() and "stagnation" in so.lower():
        has_chi = any(p in so + pr + il for p in [
            "Chi et al", "Chi, VanLehn", "reinforcement learning",
            "pedagogical policy", "IJAIED 2011",
        ])
        if not has_chi:
            flag(3, "Methodology switching algorithm selects pedagogical strategies but doesn't reference Chi, VanLehn & Litman (2011) who used RL to induce pedagogical policies in ITS (IJAIED). This is the closest prior work to Cena's approach",
                 "system-overview.md", "CITATION_NEEDED")

    # === 8. Gamification risks need Deci & Ryan SDT citation ===
    if "intrinsic motivation" in pr.lower() and "gamification" in pr.lower():
        has_sdt = any(p in pr for p in ["Deci", "Ryan", "self-determination", "SDT"])
        if not has_sdt:
            flag(2, "Discusses gamification risks to intrinsic motivation but doesn't cite Deci & Ryan's Self-Determination Theory. Already cites Sailer & Homner (2020) which is good, but SDT is the foundational framework",
                 "product-research.md", "CITATION_NEEDED")

    # === 9. TOPSIS for strategy selection — acknowledge recent work ===
    if "MCM" in ad and "methodology" in so.lower():
        has_topsis = any(p in pr + so + ad + il for p in [
            "TOPSIS", "multi-criteria decision", "MCDM",
        ])
        if not has_topsis:
            flag(2, "Methodology selection algorithm could benefit from citing recent TOPSIS-based instructional strategy selection research (2025, MDPI Information) which validated multi-criteria approach for exactly this problem (g=0.49 normalized gain)",
                 "product-research.md", "CITATION_NEEDED")

    # === 10. Knowledge graph visualization effect on motivation ===
    if "knowledge graph" in so.lower() and "motivation" in so.lower():
        has_kg_cite = any(p in so + pr for p in [
            "104.75%", "147.89%", "learning efficiency",
            "knowledge graph visualization" and "research",
        ])
        if not has_kg_cite:
            flag(2, "Claims knowledge graph visualization is motivating but doesn't cite research. Recent studies show KG-planned learning paths increase efficiency 104-148% (2024 systematic review in Electronics/MDPI)",
                 "system-overview.md", "CITATION_NEEDED")

    # === PRINT RESULTS ===
    print("=" * 70)
    print("GROUND CLAIMS IN REAL RESEARCH (lower=better, target: 0)")
    print("=" * 70)

    if "CITATION_NEEDED" in results_by_category:
        items = results_by_category["CITATION_NEEDED"]
        cat_total = sum(w for w, _, _ in items)
        print(f"\n  [CITATION_NEEDED: {cat_total} points]")
        for weight, desc, fname in items:
            print(f"    (w={weight}) {fname}: {desc}")

    print(f"\n{'=' * 70}")
    print(f"\n  TOTAL GAP: {total_score}")
    print(f"{'=' * 70}")
    print(f"\nMETRIC:{total_score}")


if __name__ == "__main__":
    main()
