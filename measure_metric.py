#!/usr/bin/env python3
"""
Autoresearch metric Phase 15: Tasks ↔ Contracts Traceability.
Every interface, method, algorithm, and read model in contracts/
must have a corresponding task that explicitly implements it.
Target: 0.
"""

import os
import re
from pathlib import Path

CONTRACTS = Path(__file__).parent / "contracts"
TASKS = Path(__file__).parent / "tasks"


def main():
    total = 0
    results = {}

    def flag(w, desc, cat):
        nonlocal total
        total += w
        results.setdefault(cat, []).append((w, desc))

    # Load all task content
    task_text = ""
    for root, _, files in os.walk(TASKS):
        for f in files:
            if f.endswith(".md"):
                task_text += open(os.path.join(root, f)).read().lower()

    # Load contract content
    contracts = {}
    for root, _, files in os.walk(CONTRACTS):
        for f in files:
            if f.endswith(('.cs', '.py', '.ts', '.dart', '.proto', '.graphql', '.yaml', '.cypher')):
                contracts[f] = open(os.path.join(root, f)).read()

    # ═══════════════════════════════════════════════════
    # 1. DOMAIN SERVICES (each interface method needs a task)
    # ═══════════════════════════════════════════════════

    ds = contracts.get("domain-services.cs", "")

    services = [
        ("IPrerequisiteEnforcementService", "prerequisiteenforcement", 4),
        ("IBktService.BatchUpdate", "batchupdate", 3),
        ("IBktService.ReloadParametersAsync", "reloadparameters", 2),
        ("IHalfLifeRegressionService.ComputeReviewSchedule", "computereviewschedule", 2),
        ("ICognitiveLoadService.ComputeCooldownMinutes", "cooldownminutes", 2),
        ("ICognitiveLoadService.RecommendDifficultyAdjustment", "difficultyadjustment", 2),
    ]

    for svc, keyword, weight in services:
        if svc.split(".")[0] in ds and keyword not in task_text:
            flag(weight, f"{svc} in domain-services.cs has no task", "DOMAIN_SERVICE")

    # ═══════════════════════════════════════════════════
    # 2. READ MODEL VIEWS (each needs schema + Apply methods)
    # ═══════════════════════════════════════════════════

    views = [
        ("TeacherDashboardView", "teacherdashboardview"),
        ("ParentProgressView", "parentprogressview"),
        ("MethodologyEffectivenessView", "methodologyeffectivenessview"),
        ("RetentionCohortView", "retentioncohortview"),
    ]

    for view, keyword in views:
        if view in contracts.get("marten-event-store.cs", "") and keyword not in task_text:
            flag(2, f"{view} read model has no implementation task", "READ_MODEL")

    # ═══════════════════════════════════════════════════
    # 3. ACTOR TOPOLOGY (each actor in topology needs a task)
    # ═══════════════════════════════════════════════════

    topo = contracts.get("actor_system_topology.cs", "")

    topology_actors = [
        ("ActorSystemManager", "actorsystemmanager", 3),
        ("AnalyticsAggregatorActor", "analyticsaggregator", 2),
        ("DeadLetterWatcher", "deadletterwatcher", 2),
        ("OutreachDispatcherActor", "outreachdispatcher", 2),
        ("WhatsAppWorkerActor", "whatsappworker", 1),
    ]

    for actor, keyword, weight in topology_actors:
        if actor in topo and keyword not in task_text:
            flag(weight, f"{actor} in topology has no implementation task", "TOPOLOGY")

    # ═══════════════════════════════════════════════════
    # 4. TEST PATTERNS (contract defines test patterns)
    # ═══════════════════════════════════════════════════

    tests = contracts.get("actor_tests.cs", "")
    test_patterns = [
        ("BktPropertyTests", "propertybased", 2),
        ("StudentActorLoadTests", "loadtest", 2),
        ("ChaosTests", "chaostest", 2),
    ]

    for tp, keyword, weight in test_patterns:
        if tp in tests and keyword not in task_text:
            flag(weight, f"{tp} in actor_tests.cs has no task", "TEST_PATTERN")

    # ═══════════════════════════════════════════════════
    # 5. SUPERVISION (contract defines strategies)
    # ═══════════════════════════════════════════════════

    sup = contracts.get("supervision_strategies.cs", "")
    sup_items = [
        ("PoisonMessageAwareStrategy", "poisonmessageaware", 2),
        ("ActorCircuitBreaker", "actorcircuitbreaker", 2),
        ("ExponentialBackoffStrategy", "exponentialbackoff", 1),
    ]

    for item, keyword, weight in sup_items:
        if item in sup and keyword not in task_text:
            flag(weight, f"{item} in supervision_strategies.cs has no task", "SUPERVISION")

    # ═══════════════════════════════════════════════════
    # PRINT
    # ═══════════════════════════════════════════════════

    print("=" * 70)
    print("TASKS ↔ CONTRACTS TRACEABILITY (lower=better, target: 0)")
    print("=" * 70)

    for cat in ["DOMAIN_SERVICE", "READ_MODEL", "TOPOLOGY", "TEST_PATTERN", "SUPERVISION"]:
        if cat in results:
            items = results[cat]
            ct = sum(w for w, _ in items)
            print(f"\n  [{cat}: {ct} points]")
            for w, d in items:
                print(f"    (w={w}) {d}")

    print(f"\n{'=' * 70}")
    for cat in ["DOMAIN_SERVICE", "READ_MODEL", "TOPOLOGY", "TEST_PATTERN", "SUPERVISION"]:
        if cat in results:
            print(f"  {cat}: {sum(w for w,_ in results[cat])}")
    print(f"\n  TOTAL GAP: {total}")
    print(f"{'=' * 70}")
    print(f"\nMETRIC:{total}")


if __name__ == "__main__":
    main()
