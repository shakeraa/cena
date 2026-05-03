# 09 — Mastery Engine

> **Source:** `docs/mastery-engine-architecture.md`, `docs/mastery-measurement-research.md`
> **Bounded contexts:** Learner (core), Pedagogy (core), Curriculum (upstream)
> **Technology:** Proto.Actor .NET 9, PostgreSQL/Marten, Neo4j, Python (offline training)

## Overview

The Mastery Engine tracks what each student knows, when they'll forget it, and what they should learn next. It is the computational core of the Learner Context — every `ConceptAttempted` event flows through BKT → HLR → prerequisite propagation → effective mastery → threshold checks → event emission.

## Tasks

| ID | Name | Priority | Effort | Blocked By |
|----|------|----------|--------|------------|
| MST-001 | ConceptMasteryState value object | P0 | S | — |
| MST-002 | BKT engine | P0 | M | MST-001 |
| MST-003 | HLR decay engine | P0 | M | MST-001 |
| MST-004 | Prerequisite support calculator | P0 | M | MST-001, DATA-006 |
| MST-005 | Effective mastery compositor | P0 | S | MST-002, MST-003, MST-004 |
| MST-006 | StudentActor mastery handler | P0 | L | MST-005, ACT-002, DATA-002 |
| MST-007 | Decay timer (actor ReceiveTimeout) | P1 | M | MST-003, MST-006 |
| MST-008 | Review priority scheduler | P1 | M | MST-007, DATA-006 |
| MST-009 | Learning frontier calculator | P1 | M | MST-004, DATA-006 |
| MST-010 | Item selector (Elo + 85% rule) | P1 | L | MST-009, ACT-003 |
| MST-011 | Scaffolding level determiner | P1 | S | MST-005 |
| MST-012 | Mastery quality matrix classifier | P2 | S | MST-001 |
| MST-013 | KST onboarding diagnostic engine | P1 | L | DATA-006, MST-001 |
| MST-014 | Initial state populator | P1 | S | MST-013, MST-001 |
| MST-015 | BKT parameter trainer (Python) | P2 | M | DATA-002 (data) |
| MST-016 | HLR weight trainer (Python) | P2 | M | MST-003 (data) |
| MST-017 | Mastery GraphQL API | P1 | M | MST-006, DATA-006 |
| MST-018 | MIRT estimator (Python, Phase 2) | P3 | L | MST-015, DATA-006 (10K users) |

## Dependency Chain (Critical Path)

```
MST-001 → MST-002 → MST-005 → MST-006 → MST-007 → MST-008
                 ↗                   ↘
MST-003 ──────┘                     MST-009 → MST-010
MST-004 → MST-005                   MST-011
```

## Stage Mapping (from master plan)

- **Foundation (Weeks 1-4):** MST-001, MST-002, MST-003, MST-004, MST-005
- **Core Loop (Weeks 5-8):** MST-006, MST-007, MST-009, MST-010, MST-011
- **Intelligence (Weeks 9-12):** MST-008, MST-012, MST-013, MST-014, MST-017
- **Scale (Phase 2):** MST-015, MST-016, MST-018
