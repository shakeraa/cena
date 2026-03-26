// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Redis Key Schema & TTL Contracts
// Layer: Data | Runtime: Node.js / TypeScript | Cache: Redis 7.x (ElastiCache)
// ═══════════════════════════════════════════════════════════════════════
//
// CONVENTIONS:
//   Namespace: cena:{context}:{entity}:{id}
//   All keys MUST use this module to construct names — no raw string concat.
//   TTLs are defined as constants and enforced at write time.
//
// OPERATIONAL NOTES:
//   - ElastiCache cluster mode enabled, hash-tag {student_id} for slot affinity.
//   - Max memory policy: allkeys-lfu (least frequently used eviction).
//   - NATS invalidation: knowledge graph cache subscribes to "cena.kg.invalidate"
//     and deletes affected keys on curriculum updates.
//   - All values are JSON-serialized unless noted otherwise (counters use INCR).
// ═══════════════════════════════════════════════════════════════════════

// ─────────────────────────────────────────────────────────────────────
// 1. TTL CONSTANTS
// ─────────────────────────────────────────────────────────────────────

/** TTL values in seconds. Single source of truth — never hardcode elsewhere. */
export const TTL = {
  /** Active session state. Short-lived; refreshed on every heartbeat. */
  SESSION: 30 * 60, // 30 minutes

  /** Offline sync idempotency keys. Must survive device reconnection windows. */
  IDEMPOTENCY: 72 * 60 * 60, // 72 hours

  /**
   * Daily token budget counter. Resets at midnight UTC.
   * Actual TTL is computed dynamically — see ttlUntilMidnightUtc().
   * This constant is the maximum (just under 24h) used as a safety cap.
   */
  TOKEN_BUDGET_MAX: 24 * 60 * 60, // 24 hours (cap)

  /** Sliding-window rate limiter. Window size per tier. */
  RATE_LIMIT_WINDOW: 60, // 60 seconds (default window)

  /** Knowledge graph cache. Invalidated via NATS before natural expiry. */
  KNOWLEDGE_GRAPH: 24 * 60 * 60, // 24 hours
} as const;

// ─────────────────────────────────────────────────────────────────────
// 2. KEY BUILDERS
// ─────────────────────────────────────────────────────────────────────

/**
 * All key builders use hash tags {id} around the student/entity ID
 * to guarantee slot affinity in Redis Cluster mode. This ensures that
 * all keys for a single student land on the same shard, enabling
 * MULTI/EXEC transactions and Lua scripts without CROSSSLOT errors.
 */

export const Keys = {
  // ── Session Cache ──────────────────────────────────────────────────
  // Stores the student's active session state: current concept, methodology,
  // fatigue score, question queue, and offline buffer metadata.
  // Written by SessionStarted_V1 handler, refreshed on every heartbeat,
  // deleted on SessionEnded_V1 or TTL expiry (abandoned session).
  //
  // Value type: JSON (SessionCachePayload)

  session: (studentId: string): string =>
    `cena:session:state:{${studentId}}`,

  // ── Idempotency Keys (Offline Sync) ────────────────────────────────
  // When a student works offline, the client buffers events locally and
  // replays them on reconnection. Each event carries a client-generated
  // idempotency key. The server checks this key before appending to the
  // Marten event store.
  //
  // Value type: "1" (existence check only — use SET NX)

  idempotency: (studentId: string, eventId: string): string =>
    `cena:idempotency:event:{${studentId}}:${eventId}`,

  // ── Daily Token Budget Counter ─────────────────────────────────────
  // Tracks LLM token consumption per student per day. Used by the
  // CognitiveLoadCooldown and cost-control systems.
  // Key expires at midnight UTC; use INCR for atomic updates.
  //
  // Value type: integer (INCR/DECRBY)
  // Partition: one key per student per calendar day (UTC).

  tokenBudget: (studentId: string, dateUtc: string): string =>
    `cena:budget:tokens:{${studentId}}:${dateUtc}`,

  // ── Rate Limiter (Sliding Window) ──────────────────────────────────
  // Implements a sliding window rate limiter using a Redis sorted set.
  // Score = request timestamp (epoch ms), member = unique request ID.
  // On each request: ZREMRANGEBYSCORE to trim expired entries, ZCARD to
  // count, ZADD if under limit.
  //
  // Scopes:
  //   api     — per-student API rate limit (e.g., 100 req/min)
  //   llm     — per-student LLM call rate limit (e.g., 20 req/min)
  //   sync    — per-device offline sync replay rate (e.g., 500 events/min)
  //
  // Value type: sorted set (ZADD/ZREMRANGEBYSCORE/ZCARD)

  rateLimit: (scope: RateLimitScope, studentId: string): string =>
    `cena:ratelimit:${scope}:{${studentId}}`,

  // ── Knowledge Graph Cache (Hot Path) ───────────────────────────────
  // Caches frequently-accessed Neo4j query results to avoid cross-network
  // round trips on the exercise-selection hot path.
  //
  // Invalidation: NATS subject "cena.kg.invalidate" publishes the affected
  // concept IDs; the subscriber deletes matching keys. Natural TTL is the
  // backstop — ensures eventual consistency even if NATS message is lost.
  //
  // Sub-keys:
  //   prereqs     — prerequisite chain for a concept
  //   mcm         — MCM recommendation for (errorType, category)
  //   neighbors   — adjacent concepts in the graph (next-step suggestions)

  kgPrereqs: (conceptId: string): string =>
    `cena:kg:prereqs:{${conceptId}}`,

  kgMcm: (errorType: string, categoryId: string): string =>
    `cena:kg:mcm:${errorType}:{${categoryId}}`,

  kgNeighbors: (conceptId: string): string =>
    `cena:kg:neighbors:{${conceptId}}`,
} as const;

// ─────────────────────────────────────────────────────────────────────
// 3. VALUE TYPES
// ─────────────────────────────────────────────────────────────────────

/** Stored at Keys.session(). */
export interface SessionCachePayload {
  studentId: string;
  sessionId: string;
  startedAt: string; // ISO 8601
  currentConceptId: string;
  activeMethodology: string;
  fatigueScore: number; // 0.0 - 1.0
  questionsAttempted: number;
  questionsCorrect: number;
  avgResponseTimeMs: number;
  deviceType: string;
  isOffline: boolean;
  experimentCohort: string | null;
  /** Client-reported app version for compatibility checks. */
  appVersion: string;
  /** Buffered offline event count awaiting sync. */
  pendingOfflineEvents: number;
}

/** Stored at Keys.kgPrereqs(). */
export interface KgPrereqsCachePayload {
  conceptId: string;
  prerequisites: Array<{
    conceptId: string;
    conceptName: string;
    strength: number;
    depth: number;
    bloomLevel: string;
  }>;
  cachedAt: string; // ISO 8601
}

/** Stored at Keys.kgMcm(). */
export interface KgMcmCachePayload {
  errorType: string;
  categoryId: string;
  recommendations: Array<{
    methodology: string;
    confidence: number;
    rank: number;
    minAttemptsForSwitch: number;
  }>;
  cachedAt: string; // ISO 8601
}

/** Stored at Keys.kgNeighbors(). */
export interface KgNeighborsCachePayload {
  conceptId: string;
  neighbors: Array<{
    conceptId: string;
    conceptName: string;
    relationship: "prerequisite_of" | "prerequisite_for" | "sibling";
    difficulty: number;
    bloomLevel: string;
  }>;
  cachedAt: string; // ISO 8601
}

// ─────────────────────────────────────────────────────────────────────
// 4. RATE LIMIT CONFIGURATION
// ─────────────────────────────────────────────────────────────────────

export type RateLimitScope = "api" | "llm" | "sync";

export interface RateLimitConfig {
  /** Maximum number of requests in the window. */
  maxRequests: number;
  /** Window size in seconds. */
  windowSeconds: number;
}

/**
 * Rate limit tiers. The adaptive engine reads these at startup.
 * Override per-student via feature flags (not stored in Redis).
 */
export const RATE_LIMITS: Record<RateLimitScope, RateLimitConfig> = {
  api: { maxRequests: 100, windowSeconds: 60 },
  llm: { maxRequests: 20, windowSeconds: 60 },
  sync: { maxRequests: 500, windowSeconds: 60 },
};

// ─────────────────────────────────────────────────────────────────────
// 5. HELPER UTILITIES
// ─────────────────────────────────────────────────────────────────────

/**
 * Computes seconds remaining until midnight UTC.
 * Used as the TTL for daily token budget counters so they auto-expire
 * at the start of the next billing day.
 */
export function ttlUntilMidnightUtc(): number {
  const now = new Date();
  const midnight = new Date(Date.UTC(
    now.getUTCFullYear(),
    now.getUTCMonth(),
    now.getUTCDate() + 1,
    0, 0, 0, 0,
  ));
  const seconds = Math.ceil((midnight.getTime() - now.getTime()) / 1000);
  // Safety: clamp to [1, TTL.TOKEN_BUDGET_MAX] to avoid zero/negative TTL
  // at the exact moment of midnight.
  return Math.max(1, Math.min(seconds, TTL.TOKEN_BUDGET_MAX));
}

/**
 * Returns today's date string in YYYY-MM-DD format (UTC).
 * Used as the date partition in token budget keys.
 */
export function todayUtc(): string {
  return new Date().toISOString().slice(0, 10);
}

// ─────────────────────────────────────────────────────────────────────
// 6. USAGE EXAMPLES (for implementors)
// ─────────────────────────────────────────────────────────────────────
//
// import Redis from "ioredis";
// import { Keys, TTL, ttlUntilMidnightUtc, todayUtc, RATE_LIMITS } from "./redis-contracts";
//
// const redis = new Redis.Cluster([{
//   host: "cena-cache.xxxxx.use1.cache.amazonaws.com", port: 6379
// }]);
//
// // -- Write session cache --
// await redis.set(
//   Keys.session(studentId),
//   JSON.stringify(sessionPayload),
//   "EX", TTL.SESSION,
// );
//
// // -- Check idempotency (SET NX = only if not exists) --
// const isNew = await redis.set(
//   Keys.idempotency(studentId, eventId),
//   "1",
//   "EX", TTL.IDEMPOTENCY,
//   "NX",
// );
// if (!isNew) {
//   // Duplicate event — skip processing
//   return;
// }
//
// // -- Increment daily token budget --
// const key = Keys.tokenBudget(studentId, todayUtc());
// const used = await redis.incr(key);
// if (used === 1) {
//   // First increment of the day — set TTL to midnight
//   await redis.expire(key, ttlUntilMidnightUtc());
// }
//
// // -- Sliding window rate limiter --
// const rlKey = Keys.rateLimit("llm", studentId);
// const config = RATE_LIMITS.llm;
// const now = Date.now();
// const windowStart = now - config.windowSeconds * 1000;
// await redis.zremrangebyscore(rlKey, 0, windowStart);
// const count = await redis.zcard(rlKey);
// if (count >= config.maxRequests) {
//   throw new Error("Rate limit exceeded");
// }
// await redis.zadd(rlKey, now, `${now}-${Math.random()}`);
// await redis.expire(rlKey, config.windowSeconds);
//
// // -- Knowledge graph cache with NATS invalidation --
// const cached = await redis.get(Keys.kgMcm("procedural", "cat-arithmetic"));
// if (!cached) {
//   const fresh = await neo4jDriver.executeRead(mcmQuery);
//   await redis.set(
//     Keys.kgMcm("procedural", "cat-arithmetic"),
//     JSON.stringify(fresh),
//     "EX", TTL.KNOWLEDGE_GRAPH,
//   );
// }
//
// // -- NATS invalidation subscriber (separate process) --
// natsConnection.subscribe("cena.kg.invalidate", async (msg) => {
//   const { conceptIds, categories } = JSON.parse(msg.data);
//   const pipeline = redis.pipeline();
//   for (const id of conceptIds) {
//     pipeline.del(Keys.kgPrereqs(id));
//     pipeline.del(Keys.kgNeighbors(id));
//   }
//   for (const cat of categories) {
//     // Delete MCM keys for affected categories.
//     // In production, maintain a set of active MCM keys per category
//     // to avoid SCAN in cluster mode.
//     for (const errType of ["procedural", "conceptual", "motivational"]) {
//       pipeline.del(Keys.kgMcm(errType, cat));
//     }
//   }
//   await pipeline.exec();
// });
