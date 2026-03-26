#!/usr/bin/env python3
"""
Autoresearch metric Phase 13: Fix All Architect-Identified CRITICALs.
25 CRITICAL issues from 5 architect reviews. Fix them all → 10/10.
Target: 0.
"""

import os
from pathlib import Path

CONTRACTS_DIR = Path(__file__).parent / "contracts"


def read_all():
    files = {}
    for root, _, filenames in os.walk(CONTRACTS_DIR):
        for fname in filenames:
            if fname.startswith('REVIEW_'):
                continue
            fpath = os.path.join(root, fname)
            rel = os.path.relpath(fpath, CONTRACTS_DIR)
            try:
                files[rel] = open(fpath).read()
            except:
                pass
    return files


def main():
    files = read_all()
    total = 0
    results = {}

    def flag(w, desc, f, cat):
        nonlocal total
        total += w
        results.setdefault(cat, []).append((w, desc, f))

    # Helper to get file content
    def get(pattern):
        for k, v in files.items():
            if pattern in k:
                return v
        return ""

    sa = get("student_actor")
    marten = get("marten-event-store")
    topo = get("actor_system_topology")
    session = get("learning_session")
    stag = get("stagnation_detector")
    outreach = get("outreach_scheduler")
    domain = get("domain-services")
    acl = get("acl-interfaces")
    prompts = get("prompt-templates")
    routing = get("routing-config")
    cost = get("cost-tracking")
    dart_models = get("domain_models.dart")
    dart_sync = get("offline_sync_service")
    dart_state = get("app_state")
    dart_kg = get("knowledge_graph_widget")
    dart_session = get("session_screen")
    dart_streak = get("streak_widget")
    dart_config = get("app_config")
    graphql = get("graphql-schema")
    signalr = get("signalr-messages")
    grpc = get("grpc-protos")
    nats = get("nats-subjects")
    neo4j = get("neo4j-schema")

    # ═══════════════════════════════════════════════════════════════
    # EVENT SOURCING CRITICALS
    # ═══════════════════════════════════════════════════════════════

    # ES-1: Apply uses DateTimeOffset.UtcNow instead of event timestamp
    if "DateTimeOffset.UtcNow" in marten and "Apply(" in marten:
        flag(4, "Apply methods use DateTimeOffset.UtcNow — must use event timestamp for deterministic replay", "marten-event-store.cs", "ES")

    # ES-2: Non-atomic multi-event writes (separate sessions per event)
    if sa and "PersistAndPublish" in sa:
        # Check if there's batch support
        if "AppendBatch" not in sa and "events.ToArray()" not in sa and "Append(_studentId, events)" not in sa:
            flag(4, "PersistAndPublish creates separate sessions — must batch all events from one command into single Append", "student_actor.cs", "ES")

    # ES-3: No expected version on Marten append (no optimistic concurrency)
    if sa and "Append(" in sa and "expectedVersion" not in sa.lower() and "ExpectedVersion" not in sa:
        flag(3, "Marten Append without expected version — no optimistic concurrency protection", "student_actor.cs", "ES")

    # ES-4: Offline sync no idempotency
    if sa and "HandleSyncOffline" in sa:
        if "idempotency" not in sa.lower() and "IdempotencyKey" not in sa and "SET NX" not in sa:
            flag(4, "Offline sync processes events without idempotency check — retry = double everything", "student_actor.cs", "ES")

    # ═══════════════════════════════════════════════════════════════
    # EDTECH CRITICALS (pedagogical soundness)
    # ═══════════════════════════════════════════════════════════════

    # ET-1: Single mastery threshold — need dual (0.85 progression, 0.95 prereq gate)
    if domain:
        has_dual = "prerequisite" in domain.lower() and ("0.95" in domain or "PrerequisiteGate" in domain)
        if not has_dual:
            flag(4, "Single mastery threshold 0.85 — need dual: 0.85 for progression, 0.95 for prerequisite gates (Corbett & Anderson standard)", "domain-services.cs", "EDTECH")

    # ET-2: BKT P(forget)=0 contradicts HLR
    if domain and "p_forget" in domain.lower():
        if "= 0" in domain or "forget = 0.0" in domain.lower():
            flag(3, "BKT P(forget)=0 contradicts HLR spaced repetition — mastery should decay in BKT too", "domain-services.cs", "EDTECH")
    elif domain and "PForget" not in domain and "p_forget" not in domain:
        flag(3, "BKT has no P(forget) parameter — mastery decay not modeled in BKT (only in HLR)", "domain-services.cs", "EDTECH")

    # ET-3: No prerequisite enforcement service
    if domain:
        has_prereq = "IPrerequisiteEnforcementService" in domain or "PrerequisiteGate" in domain or "CheckPrerequisites" in domain
        if not has_prereq:
            flag(4, "No prerequisite enforcement service — knowledge graph is decorative without gate checks", "domain-services.cs", "EDTECH")

    # ET-4: Error classification on Kimi (cheapest) despite being linchpin
    if routing and "error_classification" in routing.lower():
        if "kimi" in routing.lower() and "sonnet" not in routing[routing.lower().find("error_classification"):routing.lower().find("error_classification")+200].lower():
            flag(3, "Error classification routed to cheapest model (Kimi) — this is the linchpin of adaptive routing, needs higher quality", "routing-config.yaml", "EDTECH")

    # ET-5: Stagnation threshold not personalized
    if stag:
        has_personal = "per-student" in stag.lower() or "personalized_threshold" in stag.lower() or "student-specific" in stag.lower() or "adaptive threshold" in stag.lower()
        if not has_personal:
            flag(3, "Stagnation 5% improvement threshold is fixed — slow learners will trigger false positives. Need per-student adaptive threshold", "stagnation_detector_actor.cs", "EDTECH")

    # ET-6: XP rewards volume not mastery depth
    if marten and "XpAwarded" in marten:
        has_difficulty_xp = "difficulty" in marten[marten.find("XpAwarded"):marten.find("XpAwarded")+300].lower() if "XpAwarded" in marten else False
        if not has_difficulty_xp:
            flag(2, "XP award has no difficulty multiplier — students farm easy questions. Need: XP = base * difficulty_level", "marten-event-store.cs", "EDTECH")

    # ═══════════════════════════════════════════════════════════════
    # SECURITY CRITICALS
    # ═══════════════════════════════════════════════════════════════

    # SEC-1: No answer sanitization before LLM routing
    if acl:
        has_sanitize = "sanitize" in acl.lower() or "sanitization" in acl.lower() or "InputSanitizer" in acl
        if not has_sanitize:
            flag(4, "No input sanitization contract for student answers before LLM routing — prompt injection risk", "acl-interfaces.py", "SECURITY")

    # SEC-2: GraphQL no auth enforcement in schema
    if graphql:
        has_auth = "@auth" in graphql or "directive @auth" in graphql or "authorization" in graphql.lower()
        if not has_auth:
            flag(3, "GraphQL schema has no @auth directive — IDOR risk (student A queries student B data)", "graphql-schema.graphql", "SECURITY")

    # ═══════════════════════════════════════════════════════════════
    # MOBILE CRITICALS
    # ═══════════════════════════════════════════════════════════════

    # MOB-1: No durable command queue (app crash = lost work)
    if dart_sync:
        has_cmd_queue = "CommandQueue" in dart_sync or "command_queue" in dart_sync or "DurableCommandQueue" in dart_sync
        if not has_cmd_queue:
            flag(3, "No durable command queue — app crash between answer submit and server ack = lost work", "offline_sync_service.dart", "MOBILE")

    # MOB-2: No accessibility contracts
    if dart_kg:
        has_a11y = "Semantics" in dart_kg or "semanticLabel" in dart_kg or "accessibility" in dart_kg.lower()
        if not has_a11y:
            flag(3, "Knowledge graph widget has zero accessibility — invisible to screen readers", "knowledge_graph_widget.dart", "MOBILE")

    # MOB-3: No streak freeze / vacation mode
    if dart_streak:
        has_freeze = "freeze" in dart_streak.lower() or "vacation" in dart_streak.lower() or "Shabbat" in dart_streak
        if not has_freeze:
            flag(2, "No streak freeze / vacation mode — punishes observant students on Shabbat/holidays", "streak_widget.dart", "MOBILE")

    # ═══════════════════════════════════════════════════════════════
    # DISTRIBUTED SYSTEMS CRITICALS
    # ═══════════════════════════════════════════════════════════════

    # DS-1: NATS publish before Marten commit (outbox pattern missing)
    if sa and "PublishToNats" in sa:
        if "outbox" not in sa.lower() and "after.*SaveChanges" not in sa.lower():
            has_outbox = "OutboxPublisher" in sa or "nats_published_at" in sa
            if not has_outbox:
                flag(3, "NATS publish may fire before Marten commit — need outbox pattern", "student_actor.cs", "DISTRIBUTED")

    # ═══════════════════════════════════════════════════════════════
    # PRINT
    # ═══════════════════════════════════════════════════════════════

    print("=" * 70)
    print("FIX ALL ARCHITECT CRITICALS (lower=better, target: 0)")
    print("=" * 70)

    for cat in ["ES", "EDTECH", "SECURITY", "MOBILE", "DISTRIBUTED"]:
        if cat in results:
            items = results[cat]
            ct = sum(w for w, _, _ in items)
            print(f"\n  [{cat}: {ct} points]")
            for w, d, f in items:
                print(f"    (w={w}) {f}: {d}")

    print(f"\n{'=' * 70}")
    for cat in ["ES", "EDTECH", "SECURITY", "MOBILE", "DISTRIBUTED"]:
        if cat in results:
            print(f"  {cat}: {sum(w for w,_,_ in results[cat])}")
    print(f"\n  TOTAL GAP: {total}")
    print(f"{'=' * 70}")
    print(f"\nMETRIC:{total}")


if __name__ == "__main__":
    main()
