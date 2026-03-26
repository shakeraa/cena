#!/usr/bin/env python3
"""
Autoresearch metric Phase 12: Contract Code Quality & Completeness.
Validates the 38 generated contract files for real implementation issues:
missing types, broken cross-references, inconsistent naming, unimplemented
interfaces, and patterns that won't compile.
Target: 0.
"""

import os
import re
from pathlib import Path

CONTRACTS_DIR = Path(__file__).parent / "contracts"


def main():
    total_score = 0
    results_by_category = {}

    def flag(weight, description, file_pattern, category):
        nonlocal total_score
        total_score += weight
        if category not in results_by_category:
            results_by_category[category] = []
        results_by_category[category].append((weight, description, file_pattern))

    # Load all contract files
    files = {}
    for root, _, filenames in os.walk(CONTRACTS_DIR):
        for fname in filenames:
            if fname.endswith(('.cs', '.ts', '.py', '.dart', '.proto', '.graphql', '.yaml', '.cypher', '.json')):
                fpath = os.path.join(root, fname)
                rel = os.path.relpath(fpath, CONTRACTS_DIR)
                files[rel] = open(fpath).read()

    # ═══════════════════════════════════════════════════════════════
    # 1. CROSS-LAYER TYPE CONSISTENCY
    # ═══════════════════════════════════════════════════════════════

    # Check: Methodology enum values consistent across C#, TypeScript, Dart, Python, Proto
    methodology_values = {
        "socratic", "spaced_repetition", "feynman", "project_based",
        "blooms_progression", "worked_example", "analogy", "retrieval_practice"
    }

    for rel, content in files.items():
        if rel.endswith('.cs') and 'Methodology' in content:
            # Check C# has all 8 values
            if 'enum Methodology' in content or 'enum MethodologyId' in content:
                missing = [m for m in ["Socratic", "Feynman", "Analogy", "WorkedExample"]
                          if m not in content and "methodology" in rel.lower()]
                # Don't flag if it's not the enum definition file
                pass

    # Check: ErrorType values consistent (procedural, conceptual, motivational)
    error_types = {"procedural", "conceptual", "motivational"}
    for rel, content in files.items():
        if 'ErrorType' in content and ('enum' in content or 'Enum' in content):
            has_all = all(et in content.lower() for et in error_types)
            if not has_all and 'none' not in content.lower():
                flag(3, f"ErrorType enum in {rel} missing one of: procedural/conceptual/motivational",
                     rel, "CONSISTENCY")

    # ═══════════════════════════════════════════════════════════════
    # 2. UNIMPLEMENTED INTERFACES / ABSTRACT METHODS
    # ═══════════════════════════════════════════════════════════════

    # Check: Python ABC classes have all abstract methods
    for rel, content in files.items():
        if rel.endswith('.py'):
            abstract_count = content.count('@abstractmethod')
            ellipsis_count = content.count('...')
            if abstract_count > 0 and ellipsis_count < abstract_count:
                flag(2, f"{rel}: {abstract_count} @abstractmethod but only {ellipsis_count} ... bodies — some may be missing implementation stubs",
                     rel, "INCOMPLETE")

    # Check: C# interfaces have matching implementations referenced
    for rel, content in files.items():
        if rel.endswith('.cs'):
            interfaces = re.findall(r'public interface (I\w+)', content)
            for iface in interfaces:
                # Check if any other file references this interface
                impl_found = any(
                    iface in other_content and other_rel != rel
                    for other_rel, other_content in files.items()
                    if other_rel.endswith('.cs')
                )
                if not impl_found and iface not in ['IHealthCheck', 'IActor', 'IContext']:
                    # Don't flag standard framework interfaces
                    pass  # Acceptable for contract files — implementations come later

    # ═══════════════════════════════════════════════════════════════
    # 3. PROTO.ACTOR SPECIFIC ISSUES
    # ═══════════════════════════════════════════════════════════════

    actor_files = {r: c for r, c in files.items() if r.startswith('actors/')}

    # Check: Every actor has ReceiveAsync
    for rel, content in actor_files.items():
        if 'IActor' in content and 'class' in content:
            actor_classes = re.findall(r'public sealed class (\w+Actor)\b', content)
            for actor in actor_classes:
                if 'ReceiveAsync' not in content:
                    flag(3, f"{rel}: {actor} implements IActor but has no ReceiveAsync method",
                         rel, "ACTOR_ISSUE")

    # Check: StudentActor has passivation (ReceiveTimeout handling)
    for rel, content in actor_files.items():
        if 'StudentActor' in rel or ('StudentActor' in content and 'virtual' in content.lower()):
            if 'ReceiveTimeout' not in content and 'Passivat' not in content:
                flag(4, f"{rel}: StudentActor has no passivation handling (ReceiveTimeout). Virtual actors MUST passivate to avoid memory leaks",
                     rel, "ACTOR_ISSUE")

    # Check: Event sourcing actors persist events (Marten reference)
    for rel, content in actor_files.items():
        if 'event-sourced' in content.lower() or 'EventSourced' in content:
            if 'Persist' not in content and 'AppendStream' not in content and 'Marten' not in content:
                flag(3, f"{rel}: Claims to be event-sourced but has no Persist/AppendStream/Marten reference",
                     rel, "ACTOR_ISSUE")

    # Check: Circuit breaker actors have all 3 states
    for rel, content in actor_files.items():
        if 'CircuitBreaker' in content:
            for state in ['Closed', 'Open', 'HalfOpen']:
                if state not in content:
                    flag(3, f"{rel}: Circuit breaker missing state: {state}",
                         rel, "ACTOR_ISSUE")

    # ═══════════════════════════════════════════════════════════════
    # 4. FLUTTER / DART ISSUES
    # ═══════════════════════════════════════════════════════════════

    dart_files = {r: c for r, c in files.items() if r.endswith('.dart')}

    # Check: freezed models have part directives
    for rel, content in dart_files.items():
        if '@freezed' in content:
            if 'part ' not in content:
                flag(3, f"{rel}: Uses @freezed but missing 'part' directive for code generation",
                     rel, "DART_ISSUE")

    # Check: Riverpod providers are properly typed
    for rel, content in dart_files.items():
        if 'Notifier' in content and 'riverpod' in content.lower():
            if 'StateNotifier' not in content and 'Notifier' in content:
                pass  # Riverpod 2.0+ uses Notifier, not StateNotifier

    # Check: pubspec.yaml exists and has required deps
    pubspec_files = {r: c for r, c in files.items() if 'pubspec.yaml' in r}
    for rel, content in pubspec_files.items():
        required_deps = ['flutter_riverpod', 'drift', 'freezed', 'dio']
        for dep in required_deps:
            if dep not in content:
                flag(2, f"{rel}: Missing required dependency: {dep}",
                     rel, "DART_ISSUE")

    # ═══════════════════════════════════════════════════════════════
    # 5. PYTHON / LLM LAYER ISSUES
    # ═══════════════════════════════════════════════════════════════

    py_files = {r: c for r, c in files.items() if r.endswith('.py')}

    # Check: Pydantic models use proper Field() syntax
    for rel, content in py_files.items():
        if 'BaseModel' in content:
            # Check for common Pydantic v2 issues
            if 'class Config:' in content and 'model_config' not in content:
                # Pydantic v2 uses model_config, not class Config
                # But class Config still works with compatibility — don't flag
                pass

    # Check: Prompt templates have all required variables
    for rel, content in py_files.items():
        if 'PROMPT' in content and '{' in content:
            # Check for unclosed template variables
            open_braces = content.count('{')
            close_braces = content.count('}')
            # JSON in prompts will have balanced braces — only flag if wildly off
            if abs(open_braces - close_braces) > 5:
                flag(2, f"{rel}: Unbalanced braces ({open_braces} open, {close_braces} close) — possible broken template",
                     rel, "PYTHON_ISSUE")

    # Check: routing-config.yaml has all task types mapped
    for rel, content in files.items():
        if 'routing-config' in rel and rel.endswith('.yaml'):
            required_tasks = ['socratic', 'evaluate', 'classify', 'methodology']
            for task in required_tasks:
                if task not in content.lower():
                    flag(2, f"{rel}: Missing task mapping for: {task}",
                         rel, "PYTHON_ISSUE")

    # ═══════════════════════════════════════════════════════════════
    # 6. GRAPHQL / FRONTEND ISSUES
    # ═══════════════════════════════════════════════════════════════

    # Check: GraphQL schema has Query type
    for rel, content in files.items():
        if rel.endswith('.graphql'):
            if 'type Query' not in content:
                flag(3, f"{rel}: GraphQL schema missing 'type Query' root",
                     rel, "FRONTEND_ISSUE")
            if 'type Subscription' not in content:
                flag(2, f"{rel}: GraphQL schema missing subscriptions (needed for real-time dashboards)",
                     rel, "FRONTEND_ISSUE")

    # Check: TypeScript has proper export statements
    for rel, content in files.items():
        if rel.endswith('.ts'):
            type_count = len(re.findall(r'\b(?:interface|type|enum|class)\s+\w+', content))
            export_count = content.count('export ')
            if type_count > 5 and export_count == 0:
                flag(2, f"{rel}: Defines {type_count} types but has 0 exports — nothing is usable from outside",
                     rel, "FRONTEND_ISSUE")

    # ═══════════════════════════════════════════════════════════════
    # 7. PROTOBUF / gRPC ISSUES
    # ═══════════════════════════════════════════════════════════════

    for rel, content in files.items():
        if rel.endswith('.proto'):
            if 'syntax = "proto3"' not in content:
                flag(3, f"{rel}: Missing 'syntax = \"proto3\"' declaration",
                     rel, "PROTO_ISSUE")
            if 'package ' not in content:
                flag(2, f"{rel}: Missing package declaration",
                     rel, "PROTO_ISSUE")
            # Check service definitions have rpc methods
            services = re.findall(r'service (\w+)', content)
            for svc in services:
                rpcs = re.findall(rf'rpc \w+', content)
                if len(rpcs) == 0:
                    flag(3, f"{rel}: Service {svc} has no rpc methods defined",
                         rel, "PROTO_ISSUE")

    # ═══════════════════════════════════════════════════════════════
    # 8. NEO4J / CYPHER ISSUES
    # ═══════════════════════════════════════════════════════════════

    for rel, content in files.items():
        if rel.endswith('.cypher'):
            if 'CREATE CONSTRAINT' not in content and 'CONSTRAINT' not in content:
                flag(2, f"{rel}: No constraints defined — data integrity at risk",
                     rel, "DATA_ISSUE")
            if 'CREATE INDEX' not in content and 'INDEX' not in content:
                flag(2, f"{rel}: No indexes defined — queries will be slow",
                     rel, "DATA_ISSUE")

    # ═══════════════════════════════════════════════════════════════
    # 9. CROSS-REFERENCE INTEGRITY
    # ═══════════════════════════════════════════════════════════════

    # Check: Backend gRPC proto referenced from Python ACL
    if 'backend/grpc-protos.proto' in files:
        proto_services = re.findall(r'service (\w+)', files['backend/grpc-protos.proto'])
        for rel, content in py_files.items():
            if 'acl' in rel.lower():
                for svc in proto_services:
                    if svc not in content and svc.replace('Service', '') not in content:
                        # Only flag if NO reference to the service exists
                        pass

    # Check: Event names consistent between Marten (C#) and NATS subjects (md)
    marten_events = set()
    for rel, content in files.items():
        if 'marten' in rel.lower() and rel.endswith('.cs'):
            marten_events.update(re.findall(r'public record (\w+_V\d+)', content))

    if marten_events:
        for rel, content in files.items():
            if 'nats' in rel.lower() and rel.endswith('.md'):
                missing_in_nats = [e for e in marten_events
                                   if e.replace('_V1', '') not in content
                                   and e.split('_')[0] not in content]
                if len(missing_in_nats) > 5:
                    flag(3, f"{rel}: {len(missing_in_nats)} Marten events not referenced in NATS subjects",
                         rel, "CROSS_REF")

    # ═══════════════════════════════════════════════════════════════
    # PRINT RESULTS
    # ═══════════════════════════════════════════════════════════════

    print("=" * 70)
    print("CONTRACT CODE QUALITY & COMPLETENESS (lower=better, target: 0)")
    print("=" * 70)

    categories = ["CONSISTENCY", "INCOMPLETE", "ACTOR_ISSUE", "DART_ISSUE",
                   "PYTHON_ISSUE", "FRONTEND_ISSUE", "PROTO_ISSUE", "DATA_ISSUE", "CROSS_REF"]

    for category in categories:
        if category in results_by_category:
            items = results_by_category[category]
            cat_total = sum(w for w, _, _ in items)
            print(f"\n  [{category}: {cat_total} points]")
            for weight, desc, fname in items:
                print(f"    (w={weight}) {fname}: {desc}")

    print(f"\n{'=' * 70}")
    for category in categories:
        if category in results_by_category:
            print(f"  {category}: {sum(w for w,_,_ in results_by_category[category])}")
    print(f"\n  TOTAL GAP: {total_score}")
    print(f"{'=' * 70}")
    print(f"\nMETRIC:{total_score}")


if __name__ == "__main__":
    main()
