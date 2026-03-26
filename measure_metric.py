#!/usr/bin/env python3
"""
Autoresearch metric Phase 11: Technical Stack & Architecture Gaps.
Catches missing specs, unacknowledged risks, and implementation blockers
in the technical architecture.
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

    ad = all_text.get("architecture-design.md", "")
    op = all_text.get("operations.md", "")
    fm = all_text.get("failure-modes.md", "")
    lr = all_text.get("llm-routing-strategy.md", "")
    osp = all_text.get("offline-sync-protocol.md", "")

    # 1. Proto.Actor maturity risk not acknowledged
    if "Proto.Actor" in ad:
        has_risk = any(p in ad + fm for p in [
            "Proto.Actor maturity", "Proto.Actor risk", "community size",
            "migration path", "Akka.NET fallback", "framework risk",
        ])
        if not has_risk:
            flag(5, "Proto.Actor is the foundation but its maturity risk is unacknowledged. Need: honest assessment of community size (~47 SO questions vs 800+ Akka.NET), production adoption, and migration path if framework stalls",
                 "architecture-design.md", "RISK")

    # 2. No actor passivation strategy
    if "StudentActor" in ad:
        has_passivation = any(p in ad + op for p in [
            "passivat", "deactivat", "idle timeout", "actor lifecycle",
            "memory pressure", "actor eviction",
        ])
        if not has_passivation:
            flag(4, "10K students = 10K actors = ~5GB RAM. No passivation strategy specified. Need: idle timeout (e.g., 30 min), reactivation from snapshot, memory pressure alerts, and growth projections at 50K/100K users",
                 "architecture-design.md", "MISSING_SPEC")

    # 3. Neo4j AuraDB cost not modeled at scale
    if "Neo4j AuraDB" in ad or "AuraDB" in ad:
        has_cost_scale = any(p in ad + op for p in [
            "self-hosted Neo4j", "Neo4j Community", "AuraDB cost at scale",
            "graph database cost", "Neo4j migration",
        ])
        if not has_cost_scale:
            flag(3, "Neo4j AuraDB at $65/GB/month. At 10K concepts with edges, this could be $500-2000/month. No cost projection at scale or self-hosted fallback plan documented",
                 "architecture-design.md", "COST")

    # 4. Missing CI/CD, IaC, secrets management
    if "operations.md" in all_text:
        has_cicd = any(p in op for p in [
            "GitHub Actions", "CI/CD pipeline", "Terraform",
            "CloudFormation", "infrastructure as code",
        ])
        has_secrets = any(p in op for p in [
            "Secrets Manager", "Vault", "secrets management",
            "credential rotation", "secret injection",
        ])
        if not has_cicd:
            flag(3, "No CI/CD pipeline specified. Need: build/test/deploy pipeline definition (GitHub Actions, image tagging strategy, promotion flow dev→staging→prod)",
                 "operations.md", "MISSING_SPEC")
        if not has_secrets:
            flag(2, "No secrets management strategy. LLM API keys, database passwords, NATS credentials — how are they stored and rotated? Need: AWS Secrets Manager or equivalent",
                 "operations.md", "MISSING_SPEC")

    # 5. LLM hard per-student rate limit not specified at protocol level
    if "50 LLM-powered interactions" in ad or "50 interaction" in lr or "50 LLM" in all_text.get("system-overview.md", ""):
        has_hard_limit = any(p in ad + lr + op for p in [
            "token budget", "hard limit", "DailyTokenBudget",
            "per-student daily", "token cap", "hard cap per student",
        ])
        if not has_hard_limit:
            flag(3, "50 interactions/day limit mentioned but no hard enforcement at LLM ACL layer. A bug or exploit could trigger 200+ LLM calls per student. Need: per-student daily token budget with hard cutoff → degraded mode",
                 "architecture-design.md", "MISSING_SPEC")

    # 6. Marten version not pinned
    if "Marten" in ad:
        has_version = any(p in ad for p in [
            "Marten 7", "Marten 8", "Marten v", "Marten version",
        ])
        if not has_version:
            flag(2, "Marten event store used but version not pinned. Marten has breaking changes between major versions. Need: pin to specific version (e.g., Marten 7.x or 8.x)",
                 "architecture-design.md", "MISSING_SPEC")

    # 7. Offline sync load test plan missing
    if "offline-sync-protocol.md" in all_text:
        has_load_test = any(p in osp + op for p in [
            "load test", "chaos test", "sync latency",
            "concurrent sync", "stress test", "performance test",
        ])
        if not has_load_test:
            flag(3, "Offline sync protocol is detailed but has no load/chaos test plan. Need: test 500 events from 1000 concurrent students, kill server mid-sync, corrupt SQLite, measure p50/p95/p99 sync latency",
                 "offline-sync-protocol.md", "MISSING_SPEC")

    # 8. No phased architecture approach (MVP simplification)
    if "event sourcing" in ad.lower():
        has_phased = any(p in ad for p in [
            "Phase 1", "MVP architecture", "simplified MVP",
            "phased approach", "incremental complexity",
            "start simple", "migrate to event sourcing",
        ])
        if not has_phased:
            flag(4, "Full event sourcing + CQRS + actors from Day 1 is 12-18 months to MVP. No phased approach documented. Need: define what's MVP-critical vs post-PMF, or explain why full complexity is justified from Day 1",
                 "architecture-design.md", "RISK")

    # === PRINT ===
    print("=" * 70)
    print("TECHNICAL STACK & ARCHITECTURE GAPS (lower=better, target: 0)")
    print("=" * 70)

    for category in ["RISK", "MISSING_SPEC", "COST"]:
        if category in results_by_category:
            items = results_by_category[category]
            cat_total = sum(w for w, _, _ in items)
            print(f"\n  [{category}: {cat_total} points]")
            for weight, desc, fname in items:
                print(f"    (w={weight}) {fname}: {desc}")

    print(f"\n{'=' * 70}")
    for category in ["RISK", "MISSING_SPEC", "COST"]:
        if category in results_by_category:
            print(f"  {category}: {sum(w for w,_,_ in results_by_category[category])}")
    print(f"\n  TOTAL GAP: {total_score}")
    print(f"{'=' * 70}")
    print(f"\nMETRIC:{total_score}")


if __name__ == "__main__":
    main()
