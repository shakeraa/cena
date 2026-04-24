# Cena Architecture Audit — Pre-Investment Gap Analysis

> **Date:** 2026-03-26
> **Status:** Gaps identified, pending resolution
> **Auditor:** Automated pre-investment technical audit

---

## CRITICAL GAPS (5 found — would block implementation or cause major rework)

### C1. No API Contract Definition
Every service boundary is undefined. No REST endpoints, no gRPC protobuf schemas, no GraphQL schema, no WebSocket message envelope format. When AI coding agents start implementation, they will hallucinate contracts and you'll spend weeks reconciling them.

**Fix:** Define at minimum: (1) SignalR message envelope (command/event discriminated union), (2) gRPC proto for LLM ACL service, (3) GraphQL schema for teacher/parent dashboards, (4) REST API surface for mobile client non-realtime calls (auth, onboarding, settings).

### C2. No Event Schema Versioning Strategy
Event sourcing commits to event names but no payload schemas and no evolution strategy. If `ConceptAttempted` needs a new required field in Month 4, every historical event is missing it. Marten supports upcasting but the strategy must be designed upfront.

**Fix:** Define full payload schema for every domain event. Adopt versioning convention (`ConceptAttempted_V1`, `ConceptAttempted_V2`). Document upcasting rules.

### C3. Proto.Actor Cluster Failure Modes Unaddressed
Missing: split-brain/network partition handling, actor activation storm during rolling deploys, poison message recovery, timer skew on actor migration.

**Fix:** Dedicated failure mode analysis. Document Proto.Actor cluster config for split-brain detection (gossip/suspect/dead timeouts). Define poison message dead-letter queue. Specify actor activation rate limit and backpressure.

### C4. Offline Conflict Resolution Underspecified
"Server state is authoritative" is a hand-wave. Student works offline for 45 minutes, server emits `MasteryDecayed` events simultaneously. What happens to the 12 offline exercises on reconnect?

**Fix:** Define conflict resolution protocol: (1) event types safe to replay regardless of server state, (2) types requiring re-validation, (3) XP/streak credit handling during conflicts, (4) how client UI communicates conflicts.

### C5. Content Creation Pipeline Entirely Absent
Architecture describes how content is served and adapted but not how it's created. No authoring context, no CMS, no editorial workflow, no mention of how errors get corrected after students interact with affected nodes.

**Fix:** Add ninth bounded context: **Content Authoring Context**. Define authoring tool (even minimal admin UI). Define workflow: LLM extraction → SME review → QA → staging → production. Define content correction propagation.

---

## IMPORTANT GAPS (10 found — should be resolved before development)

### I1. Question/Assessment Format Undefined
"Exercises" and "mastery checks" mentioned throughout but never defined. Multiple choice? Free text? Math expression input? This determines the entire evaluation pipeline.

### I2. Diagnostic Quiz Algorithm Insufficiently Specified
"ALEKS-inspired KST" with 10-15 questions claimed but no specific KST variant, item selection algorithm, prior construction, or stopping criterion defined.

### I3. Notification Throttling Dangerous Gap
Five independent trigger events can each send WhatsApp messages. No per-student daily budget, no deduplication, no quiet hours, no opt-down. One student screenshotting 6 messages/day kills market reputation.

### I4. Backup/DR/RTO/RPO Absent
Five data stores, zero backup specifications. No frequency, retention, cross-region replication, restore procedures, or recovery objectives.

### I5. Monitoring Has Metrics But No Alerts
OpenTelemetry listed as concern but not a single alert defined. No dashboard specified. No SLO stated. No on-call rotation.

### I6. Parent Experience Undefined
Parents are the purchase decision-maker. "Read-only view of child's progress" is a placeholder, not a product spec.

### I7. CI/CD Pipeline Not Described
Polyglot architecture (.NET, Python, Node.js, React Native) across 5 deployment targets with no pipeline. AI agents generate code fast — fast without CI/CD gates = fast path to production bugs.

### I8. Data Moat Claimed But Not Engineered
No mechanism for aggregate student data to feed back into improving the system. No MCM graph update pipeline, no ongoing BKT retraining, no flywheel. Without this, Cena is a well-engineered app, not a data business.

### I9. Search Is Missing
No full-text or semantic search for concepts. Students can't search "integration by parts" — only navigate the graph visualization.

### I10. Teacher Dashboard Is a Placeholder
B2B2C school partnerships depend on teacher dashboards but no product spec exists for what teachers see.

---

## NICE-TO-HAVE GAPS (5 found)

### N1. Accessibility Not Mentioned
No screen reader support, color blindness accommodation for knowledge graph, keyboard navigation, or motor impairment accommodations.

### N2. Post-Syllabus Experience Undefined
What happens when a student masters every node? They hit a wall.

### N3. Competitive Response Analysis Surface-Level
"Low likelihood" for competitor launching similar features is optimistic. No analysis of replication cost.

### N4. Load Testing Strategy Absent
Bagrut exam season creates guaranteed 10x DAU spikes. No load testing plan.

### N5. Teacher Dashboard Is a Placeholder
(Overlaps with I10 — listed separately for completeness)

---

## The Three Things That Would Stop a Technical Investor Cold

1. **C5 (Content authoring)** — "Who writes the 10,000 questions?" is the first question every EdTech investor asks
2. **C2 (Event schema versioning)** — Any CTO who's operated event-sourced systems will ask this immediately
3. **I8 (Data moat)** — Without the flywheel, you've built a tutoring tool, not a learning intelligence platform

---

## Summary

| Classification | Count | Effort Range |
|---------------|-------|-------------|
| CRITICAL | 5 | Low to High |
| IMPORTANT | 10 | Low to Medium |
| NICE-TO-HAVE | 5 | Low to Medium |
| **Total gaps** | **20** | |
