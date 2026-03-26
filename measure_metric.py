#!/usr/bin/env python3
"""
Autoresearch metric Phase 10: Real Citations for Every Part.
Each methodology, algorithm, and design choice must cite its foundational research.
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

    so = all_text.get("system-overview.md", "")
    pr = all_text.get("product-research.md", "")
    ad = all_text.get("architecture-design.md", "")
    il = all_text.get("intelligence-layer.md", "")
    aspec = all_text.get("assessment-specification.md", "")
    mr = all_text.get("mastery-measurement-research.md", "")
    ca = all_text.get("content-authoring.md", "")

    # All docs combined for citation search
    all_combined = so + pr + ad + il + aspec + mr + ca

    # === METHODOLOGY CITATIONS (each of Cena's 8 methods needs a foundational paper) ===

    # 1. Cognitive Load Theory — Sweller (1988)
    if "cognitive load" in so.lower():
        if "Sweller" not in all_combined:
            flag(3, "Cognitive load management section has no citation. Need: Sweller (1988) 'Cognitive load during problem solving: Effects on learning', Cognitive Science 12(2), 257-285",
                 "system-overview.md", "METHODOLOGY")

    # 2. Retrieval Practice — Roediger & Karpicke (2006)
    if "retrieval practice" in so.lower() or "retrieval practice" in pr.lower():
        if "Roediger" not in all_combined and "Karpicke" not in all_combined:
            flag(3, "Retrieval practice methodology has no citation. Need: Roediger & Karpicke (2006) 'Test-Enhanced Learning', Psychological Science 17(3), 249-255. Testing > restudying for long-term retention",
                 "system-overview.md", "METHODOLOGY")

    # 3. Worked Examples with Fading — Renkl & Atkinson (2003)
    if "worked example" in so.lower() or "worked example" in pr.lower():
        if "Renkl" not in all_combined and "Atkinson" not in all_combined:
            flag(3, "Worked examples methodology has no citation. Need: Renkl & Atkinson (2003) 'Structuring the transition from example study to problem solving', Educational Psychologist 38, 15-22",
                 "system-overview.md", "METHODOLOGY")

    # 4. Analogy-Based Instruction — Gentner (1983)
    if "analogy" in so.lower():
        if "Gentner" not in all_combined:
            flag(2, "Analogy-based methodology has no citation. Need: Gentner (1983) 'Structure-Mapping: A Theoretical Framework for Analogy', Cognitive Science 7(2), 155-170",
                 "system-overview.md", "METHODOLOGY")

    # 5. Bloom's Taxonomy Progression — Anderson & Krathwohl (2001)
    if "bloom" in so.lower() or "Bloom" in aspec:
        if "Anderson" not in all_combined or "Krathwohl" not in all_combined:
            flag(2, "Bloom's taxonomy progression has no citation. Need: Anderson & Krathwohl (2001) 'A Taxonomy for Learning, Teaching, and Assessing' (Longman). Revised 6-level cognitive framework",
                 "system-overview.md", "METHODOLOGY")

    # 6. Project-Based Learning — Chen & Yang (2019)
    if "project-based" in so.lower() or "project-based" in pr.lower():
        if "Chen" not in all_combined or "Yang" not in all_combined:
            flag(2, "Project-based learning methodology has no citation. Need: Chen & Yang (2019) meta-analysis, Educational Research Review 26, 71-81. PBL effect size d=0.71 across 12,585 students",
                 "system-overview.md", "METHODOLOGY")

    # 7. Spaced Repetition Foundation — Cepeda et al. (2006)
    if "spaced repetition" in so.lower() or "spaced repetition" in ad.lower():
        if "Cepeda" not in all_combined and "Ebbinghaus" not in all_combined:
            flag(2, "Spaced repetition has Settles (2016) for HLR but needs foundational citation. Need: Cepeda et al. (2006) meta-analysis, Psychological Bulletin 132(3), 354-380 (839 assessments, 317 experiments on distributed practice)",
                 "architecture-design.md", "METHODOLOGY")

    # === ALGORITHM CITATIONS ===

    # 8. IRT for question calibration — Embretson & Reise (2000) or de Ayala (2009)
    if "Item Response Theory" in il or "IRT" in il:
        if "Embretson" not in all_combined and "de Ayala" not in all_combined and "Lord" not in all_combined:
            flag(2, "IRT question calibration has no citation. Need: Embretson & Reise (2000) 'Item Response Theory for Psychologists' (LEA) or de Ayala (2009) 'The Theory and Practice of IRT' (Guilford)",
                 "intelligence-layer.md", "ALGORITHM")

    # 9. Knowledge graphs in education — systematic review
    if "knowledge graph" in so.lower():
        has_kg_cite = any(p in all_combined for p in [
            "knowledge graph" and "systematic review",
            "knowledge graph" and "Heliyon",
            "KG applications in education",
        ])
        # Check for any education KG research citation
        if "Heliyon" not in all_combined and "knowledge graph construction and application" not in all_combined:
            flag(2, "Knowledge graph visualization claims need research backing. Need: cite systematic review, e.g., 'A systematic literature review of knowledge graph construction and application in education' (2024, Heliyon/ScienceDirect)",
                 "system-overview.md", "ALGORITHM")

    # 10. Socratic tutoring AI effectiveness
    if "socratic" in so.lower():
        has_socratic_cite = any(p in all_combined for p in [
            "Socratic" and "RCT", "Socratic" and "randomized",
            "Khanmigo" and "study", "AI tutoring" and "classroom",
        ])
        if "RCT" not in all_combined and "randomized" not in all_combined.lower():
            flag(2, "Socratic AI tutoring effectiveness claim needs citation. Need: 2024 UK classroom RCT (arxiv.org/html/2512.23633v1) showing AI Socratic tutors = human tutors on learning outcomes, +5.5pp on novel problem solving",
                 "system-overview.md", "ALGORITHM")

    # === PRINT ===
    print("=" * 70)
    print("REAL CITATIONS FOR EVERY PART (lower=better, target: 0)")
    print("=" * 70)

    for category in ["METHODOLOGY", "ALGORITHM"]:
        if category in results_by_category:
            items = results_by_category[category]
            cat_total = sum(w for w, _, _ in items)
            print(f"\n  [{category}: {cat_total} points]")
            for weight, desc, fname in items:
                print(f"    (w={weight}) {fname}: {desc}")

    print(f"\n{'=' * 70}")
    for category in ["METHODOLOGY", "ALGORITHM"]:
        if category in results_by_category:
            print(f"  {category}: {sum(w for w,_,_ in results_by_category[category])}")
    print(f"\n  TOTAL GAP: {total_score}")
    print(f"{'=' * 70}")
    print(f"\nMETRIC:{total_score}")


if __name__ == "__main__":
    main()
