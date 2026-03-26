#!/usr/bin/env python3
"""
Autoresearch metric Phase 14: Task Plan Quality & Completeness.
Validates that every task has strict acceptance criteria, tests,
dependencies, and full domain coverage.
Target: 0.
"""

import re
from pathlib import Path

TASKS_DIR = Path(__file__).parent / "tasks"


def main():
    total = 0
    results = {}

    def flag(w, desc, f, cat):
        nonlocal total
        total += w
        results.setdefault(cat, []).append((w, desc, f))

    files = {}
    for f in sorted(TASKS_DIR.glob("*.md")):
        files[f.name] = f.read_text()

    # ═══════════════════════════════════════════════════════════════
    # 1. STRUCTURAL CHECKS (every task file)
    # ═══════════════════════════════════════════════════════════════

    for fname, content in files.items():
        if fname == "00-master-plan.md":
            continue

        # Count tasks (## TASK-NNN or ## XXX-NNN patterns)
        tasks = re.findall(r'^## \w+-\d+', content, re.MULTILINE)
        if len(tasks) < 3:
            flag(3, f"{fname}: Only {len(tasks)} tasks defined — expected at least 5", fname, "COVERAGE")

        # Every task must have acceptance criteria checkboxes
        checkbox_count = content.count("- [ ]")
        if checkbox_count < len(tasks) * 3:
            flag(3, f"{fname}: {checkbox_count} checkboxes for {len(tasks)} tasks — need at least 3 per task", fname, "CRITERIA")

        # Every task must have a Test section
        test_count = len(re.findall(r'\*\*Test', content))
        if test_count < len(tasks):
            flag(2, f"{fname}: {test_count} tests for {len(tasks)} tasks — every task needs a test", fname, "TESTING")

        # Every task must have Blocked by
        blocked_count = len(re.findall(r'Blocked by', content, re.IGNORECASE))
        if blocked_count < len(tasks):
            flag(2, f"{fname}: {blocked_count} 'Blocked by' for {len(tasks)} tasks — missing dependencies", fname, "DEPS")

        # Every task must have Priority
        priority_count = len(re.findall(r'Priority.*P[0-3]', content))
        if priority_count < len(tasks):
            flag(1, f"{fname}: {priority_count} priorities for {len(tasks)} tasks — all need P0-P3", fname, "PRIORITY")

    # ═══════════════════════════════════════════════════════════════
    # 2. DOMAIN COVERAGE (all 8 domains must exist)
    # ═══════════════════════════════════════════════════════════════

    required_domains = {
        "01-data-layer.md": "PostgreSQL, Marten, Neo4j, Redis",
        "02-actor-system.md": "Proto.Actor, event sourcing",
        "03-llm-layer.md": "FastAPI, Claude, Kimi",
        "04-mobile-app.md": "Flutter, Dart",
        "05-frontend-web.md": "React, TypeScript",
        "06-infrastructure.md": "AWS, NATS, CI/CD",
        "07-content-pipeline.md": "Neo4j, Kimi batch",
        "08-security-compliance.md": "GDPR, Auth, PII",
    }

    for domain_file, tech in required_domains.items():
        if domain_file not in files:
            flag(4, f"Missing domain file: {domain_file} ({tech})", domain_file, "COVERAGE")

    # ═══════════════════════════════════════════════════════════════
    # 3. CROSS-DOMAIN DEPENDENCY INTEGRITY
    # ═══════════════════════════════════════════════════════════════

    all_content = "\n".join(files.values())

    # Check: Data tasks reference contract files
    if "01-data-layer.md" in files:
        data = files["01-data-layer.md"]
        if "marten-event-store.cs" not in data:
            flag(2, "Data tasks don't reference marten-event-store.cs contract", "01-data-layer.md", "CONTRACT_REF")
        if "neo4j-schema.cypher" not in data:
            flag(2, "Data tasks don't reference neo4j-schema.cypher contract", "01-data-layer.md", "CONTRACT_REF")

    # Check: Actor tasks reference actor contracts
    if "02-actor-system.md" in files:
        actor = files["02-actor-system.md"]
        if "student_actor" not in actor.lower():
            flag(2, "Actor tasks don't reference student_actor contract", "02-actor-system.md", "CONTRACT_REF")

    # Check: Mobile tasks have Arabic support task
    if "04-mobile-app.md" in files:
        mobile = files["04-mobile-app.md"]
        if "arabic" not in mobile.lower() and "ar" not in mobile:
            flag(3, "Mobile tasks missing Arabic language support task", "04-mobile-app.md", "ARABIC")

    # Check: Security tasks cover all 5 architect-identified attack vectors
    if "08-security-compliance.md" in files:
        sec = files["08-security-compliance.md"]
        attack_vectors = ["IDOR", "prompt injection", "PII", "GDPR", "rate limit"]
        for av in attack_vectors:
            if av.lower() not in sec.lower():
                flag(2, f"Security tasks missing coverage for: {av}", "08-security-compliance.md", "SEC_COVERAGE")

    # ═══════════════════════════════════════════════════════════════
    # 4. TASK QUALITY CHECKS
    # ═══════════════════════════════════════════════════════════════

    # Check: P0 tasks have concrete test code (not just "Test: ...")
    for fname, content in files.items():
        if fname == "00-master-plan.md":
            continue
        # Find P0 tasks
        p0_sections = re.split(r'^## ', content, flags=re.MULTILINE)
        for section in p0_sections:
            if "P0" in section:
                if "```" not in section:
                    task_match = re.search(r'(\w+-\d+)', section)
                    task_id = task_match.group(1) if task_match else "unknown"
                    flag(2, f"{fname}: P0 task {task_id} has no code block in test — needs runnable test", fname, "TEST_QUALITY")

    # Check: Master plan has stage timeline
    if "00-master-plan.md" in files:
        master = files["00-master-plan.md"]
        if "Week" not in master:
            flag(2, "Master plan missing weekly timeline", "00-master-plan.md", "PLANNING")

    # ═══════════════════════════════════════════════════════════════
    # PRINT
    # ═══════════════════════════════════════════════════════════════

    print("=" * 70)
    print("TASK PLAN QUALITY & COMPLETENESS (lower=better, target: 0)")
    print("=" * 70)

    cats = ["COVERAGE", "CRITERIA", "TESTING", "DEPS", "PRIORITY",
            "CONTRACT_REF", "ARABIC", "SEC_COVERAGE", "TEST_QUALITY", "PLANNING"]

    for cat in cats:
        if cat in results:
            items = results[cat]
            ct = sum(w for w, _, _ in items)
            print(f"\n  [{cat}: {ct} points]")
            for w, d, f in items:
                print(f"    (w={w}) {f}: {d}")

    print(f"\n{'=' * 70}")
    for cat in cats:
        if cat in results:
            print(f"  {cat}: {sum(w for w,_,_ in results[cat])}")
    print(f"\n  TOTAL GAP: {total}")
    print(f"{'=' * 70}")
    print(f"\nMETRIC:{total}")


if __name__ == "__main__":
    main()
