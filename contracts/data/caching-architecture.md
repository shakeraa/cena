# Cena Platform — Caching Architecture Contract

> **Status:** Specification
> **Applies to:** All layers (actors, Redis, CDN, client)
> **Companion to:** `contracts/data/redis-contracts.ts`, `contracts/mobile/lib/features/diagrams/diagram_models.dart`

---

## 5-Layer Cache Hierarchy

| Layer | Technology | Data | TTL | Invalidation | Latency |
|-------|-----------|------|-----|-------------|---------|
| **L1** | Actor in-memory (`StudentState`) | Per-student mastery overlay, active session, methodology map | Until passivation (30min idle) | Actor restart / passivation | **< 1 microsecond** |
| **L2** | Redis (ElastiCache) | Session cache, idempotency keys, token budgets, KG prereq cache, rate limits | 30min / 72h / midnight / 24h / 60s | NATS `CurriculumPublished` → Redis DEL, TTL expiry | **< 1ms** |
| **L3** | CloudFront CDN | Pre-generated SVG diagrams, challenge card assets, curriculum Protobuf | 7 days (diagrams), 24h (curriculum) | CloudFront invalidation API on `CurriculumPublished` | **< 50ms** (edge) |
| **L4** | S3 | Curriculum Protobuf artifacts, Parquet analytics exports, diagram SVG/PNG originals | Versioned (never deleted, overwritten) | S3 versioning (new key = new version) | **< 200ms** |
| **L5** | Client-side (SQLite + Hive) | Offline event queue, session state, diagram cache (50MB/subject), KG layout cache | Queue: until synced. Diagrams: 7 days. Layout: until graph changes | Sync clears queue. `DiagramsPublished` clears diagram cache. Graph change recomputes layout | **0ms** (local) |

---

## Cache Invalidation Flow

When curriculum content is updated (new questions, corrected concepts, MCM confidence updates):

```
Content Author publishes → Neo4j AuraDB updated
                               │
                               ▼
                    NATS JetStream: CurriculumPublished_V1
                               │
              ┌────────────────┼────────────────┐
              ▼                ▼                 ▼
    Actor Cluster        Redis              CloudFront
    (hot-reload           (DEL keys)         (invalidation)
     in-memory graph)         │                   │
              │           ┌───┴───┐               │
              │           │       │               │
              │     KG cache   MCM cache          │
              │     (24h TTL)  (1h TTL)           │
              │                                   │
              ▼                                   ▼
    StudentActors get new                Client receives
    graph on next message                push notification:
    (lazy reload)                        "New content available"
                                               │
                                               ▼
                                    Client clears local
                                    diagram cache for
                                    affected concepts
                                    + refetches on next
                                    session start
```

### Invalidation Timing Guarantees

| Cache | Max staleness after curriculum update | Mechanism |
|-------|--------------------------------------|-----------|
| L1 (Actor) | Next message to actor (typically < 1 second) | NATS event → actor reloads graph |
| L2 (Redis) | Immediate for explicit DEL; up to TTL for natural expiry | NATS subscriber DELs affected keys |
| L3 (CDN) | Up to 15 minutes (CloudFront invalidation propagation) | API call to CloudFront `CreateInvalidation` |
| L4 (S3) | Immediate (new version uploaded) | S3 PUT with new key |
| L5 (Client) | Up to next app foreground (push notification triggers refresh) | FCM push → client clears affected concept caches |

---

## Diagram Caching Pipeline (Pre-generated Visualization Assets)

### Generation (overnight batch, 2:00 AM Israel time)

1. **Fetch concepts needing diagrams**: Query Neo4j for concepts with `diagram_version < curriculum_version` or `diagram_count < required_count`
2. **Generate SVGs**: Kimi K2.5 batch (max 20 concurrent, $50/run cost cap)
3. **Quality gate**: Auto-approve if Kimi confidence > 0.95. Reject if < 0.70. Queue for expert review if 0.70-0.95.
4. **Upload to S3**: Key = `diagrams/{subject}/v{curriculum_version}/{concept_id}-{type}-{bloom}.svg`
5. **Register CDN URL**: CloudFront distribution serves from S3 origin
6. **Emit NATS event**: `DiagramsPublished_V1` with list of concept IDs and CDN URLs

### Client Prefetch Strategy

```
On session end:
  1. Compute "frontier" — concepts student is likely to encounter next
     (prerequisites met, not yet mastered, sorted by graph distance)
  2. For each frontier concept (max 10):
     a. Check local diagram cache (Hive key-value)
     b. If not cached OR cache version < server version:
        - Fetch SVG from CDN (< 50ms edge latency)
        - Store in local cache (Hive)
        - If SVG < 50KB: also store inline for instant render
  3. Budget: max 50MB per subject in local diagram cache
  4. Eviction: LRU by last accessed, oldest first
```

### Offline Serving

When the device is offline:
- Diagrams served from Hive local cache (0ms latency)
- If diagram not cached: show concept without diagram (text-only fallback)
- On reconnect: prefetch missing diagrams for current frontier

### Cache Key Schema

```
# S3 / CDN
diagrams/{subject}/v{curriculum_version}/{concept_id}-{diagram_type}-{bloom_level}.svg
diagrams/{subject}/v{curriculum_version}/{concept_id}-{diagram_type}-{bloom_level}-thumb.png

# Redis (KG prerequisite cache)
cena:kg:prereqs:{concept_id}         TTL: 24h
cena:kg:mcm:{error_type}:{category}  TTL: 1h
cena:kg:frontier:{student_id}        TTL: 5min (recomputed frequently)

# Client (Hive)
diagram:{concept_id}:{diagram_type}  TTL: 7 days (or until DiagramsPublished invalidates)
layout:{subject}:{curriculum_version} TTL: until curriculum changes
```

---

## Interactive Hotspot Caching

Each diagram has interactive hotspots (tap targets) defined as JSON alongside the SVG:

```
# S3 path
diagrams/{subject}/v{version}/{concept_id}-{type}-{bloom}.hotspots.json

# Content
{
  "hotspots": [
    {
      "id": "hs1",
      "bounds": { "x": 0.3, "y": 0.5, "width": 0.1, "height": 0.1 },
      "label_he": "נקודת חיתוך",
      "label_ar": "نقطة تقاطع",
      "label_en": "Intercept",
      "explanation_he": "הנקודה שבה הגרף חותך את ציר ה-Y...",
      "explanation_ar": "النقطة التي يتقاطع فيها الرسم البياني مع المحور Y...",
      "explanation_en": "The point where the graph crosses the Y-axis...",
      "linked_concept_id": "math-linear-functions"
    }
  ]
}
```

Hotspot JSON is fetched alongside the SVG and cached in the same Hive entry. The Flutter `DiagramCacheService` fetches both atomically.

---

## Challenge Card Caching

Challenge cards (SmartyMe-style interactive questions with embedded diagrams) are pre-generated and cached:

```
# S3 path
challenges/{subject}/v{version}/{concept_id}-{tier}.json

# Contains: question_he/ar/en, options, expected_value, diagram reference, XP reward
# Cached alongside diagrams in client Hive store
# Same invalidation flow as diagrams
```

---

## Cache Warming on First Install

When a new student installs the app:
1. Diagnostic quiz runs (10-15 questions, no diagrams needed)
2. After quiz: knowledge graph computed, frontier identified
3. Background prefetch: top 20 frontier concept diagrams (parallel CDN fetches)
4. Total download: ~5-10MB (20 SVGs + hotspot JSONs + challenge cards)
5. App is fully cached for first 2-3 sessions within 30 seconds of quiz completion
