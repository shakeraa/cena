/**
 * Cena Adaptive Learning Platform — Client-Side Offline Sync Implementation Contract
 *
 * Defines the interfaces and types for the offline sync subsystem that runs
 * entirely on the client (React Native / React PWA). The sync subsystem is
 * responsible for:
 *
 *   1. Durably queuing events during offline learning sessions (SQLite-backed)
 *   2. Detecting reconnection and initiating the sync handshake
 *   3. Classifying events client-side (advisory; server re-validates)
 *   4. Handling the sync result and updating local state
 *   5. Detecting and compensating for client clock skew
 *
 * Implementation targets:
 *   - React Native: `expo-sqlite` or `react-native-sqlite-storage`
 *   - React PWA: `sql.js` (WASM) or IndexedDB fallback
 *
 * @see docs/offline-sync-protocol.md — Full server-side reconciliation specification
 * @see docs/event-schemas.md — Domain event definitions
 * @see contracts/frontend/state-contracts.ts — OfflineState shape
 * @module offline-sync-client
 */

// ─────────────────────────────────────────────────────────────────────────────
// 1. Event Queue Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Classification of how an offline event should be replayed on the server.
 *
 * @see docs/offline-sync-protocol.md Section 2 — Event Classification
 */
export type EventClassification =
  | 'unconditional'
  | 'conditional'
  | 'server-authoritative';

/**
 * Client-side resolution after comparing the event against server divergence data.
 * This is advisory — the server performs its own validation.
 *
 * @see docs/offline-sync-protocol.md Section 3.2 Step 3 — Client-Side Pre-Resolution
 */
export type ClientResolution =
  | 'full_weight'
  | 'reduced_weight'
  | 'historical_only'
  | 'server_decides';

/**
 * Queue entry status. Matches the SQLite `status` column.
 */
export type QueueEntryStatus = 'pending' | 'syncing' | 'synced' | 'failed';

/**
 * A single event in the offline queue.
 * Maps 1:1 to a row in the `offline_event_queue` SQLite table.
 *
 * @see docs/offline-sync-protocol.md Section 6.1 — Client-Side Event Queue Format
 */
export interface QueuedEvent {
  /** Auto-incrementing local ID (SQLite rowid). */
  readonly id: number;
  /** UUIDv7 idempotency key (time-ordered for natural sorting). */
  readonly idempotencyKey: string;
  /** Student aggregate ID. */
  readonly studentId: string;
  /** Unique device identifier. */
  readonly deviceId: string;
  /** Monotonically increasing per-device sequence number. */
  readonly clientSeq: number;
  /** Domain event type (e.g., "ExerciseAttempted", "AnnotationAdded"). */
  readonly eventType: string;
  /** JSON-serialized event payload. */
  readonly eventPayload: string;
  /** ISO 8601 timestamp from the client's local clock. */
  readonly offlineTimestamp: string;
  /** Clock offset from last server sync in milliseconds. */
  readonly clockOffsetMs: number;
  /** Current processing status. */
  status: QueueEntryStatus;
  /** Sync session ID; populated when sync begins. */
  syncSessionId: string | null;
  /** JSON-serialized server result; populated after sync completes. */
  serverResult: string | null;
  /** ISO 8601 timestamp when the event was queued. */
  readonly createdAt: string;
  /** ISO 8601 timestamp when the event was synced; `null` until synced. */
  syncedAt: string | null;
}

/**
 * Event classification lookup table.
 * Determines how each event type is classified for sync.
 *
 * @see docs/offline-sync-protocol.md Section 2.1 — Classification Table
 */
export const EVENT_CLASSIFICATION_MAP: Readonly<Record<string, EventClassification>> = {
  SessionStarted: 'unconditional',
  SessionEnded: 'unconditional',
  ExerciseAttempted: 'conditional',
  ExerciseCompleted: 'conditional',
  ConceptMastered: 'server-authoritative',
  AnnotationAdded: 'unconditional',
  HintRequested: 'unconditional',
  QuestionSkipped: 'conditional',
} as const;

// ─────────────────────────────────────────────────────────────────────────────
// 2. Sync Protocol Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SyncRequest sent from client to server (Step 1).
 *
 * @see docs/offline-sync-protocol.md Section 3.1 — Protocol Sequence, Step 1
 */
export interface SyncRequest {
  /** Student aggregate ID. */
  readonly studentId: string;
  /** Unique device identifier. */
  readonly deviceId: string;
  /**
   * The last event sequence number the client received from the server
   * before going offline. Used for divergence detection.
   */
  readonly lastKnownServerSeq: number;
  /**
   * Estimated clock offset from server in milliseconds.
   * Positive = client ahead; negative = client behind.
   */
  readonly clientClockOffsetMs: number;
  /** Ordered array of offline events with client-local sequence numbers. */
  readonly queuedEvents: ReadonlyArray<SyncEventEntry>;
  /** SHA-256 of the serialized event queue for integrity verification. */
  readonly queueChecksum: string;
}

/**
 * A single event entry within the SyncRequest / SyncCommit.
 */
export interface SyncEventEntry {
  /** UUIDv7 idempotency key. */
  readonly idempotencyKey: string;
  /** Domain event type. */
  readonly eventType: string;
  /** ISO 8601 timestamp from the client's local clock. */
  readonly offlineTimestamp: string;
  /** Per-device monotonic sequence number. */
  readonly clientSeq: number;
  /** Advisory client-side resolution (server re-validates). */
  readonly resolution: ClientResolution;
  /** JSON event payload. */
  readonly payload: Record<string, unknown>;
}

/**
 * SyncAck + ServerDivergence response from server (Step 2).
 *
 * @see docs/offline-sync-protocol.md Section 3.1 — Protocol Sequence, Step 2
 */
export interface SyncAck {
  /** Current server-side event sequence number. */
  readonly currentServerSeq: number;
  /** Events emitted by the server since `lastKnownServerSeq`. */
  readonly divergedEvents: ReadonlyArray<ServerDivergedEvent>;
  /**
   * Map of concept ID -> currently active methodology.
   * Used for client-side pre-classification of conditional events.
   */
  readonly activeMethodologyMap: Readonly<Record<string, string>>;
  /** Current knowledge graph version. */
  readonly knowledgeGraphVersion: string;
  /** Server-assigned sync session ID. */
  readonly syncSessionId: string;
}

/** A server-side event that the client missed during the offline period. */
export interface ServerDivergedEvent {
  /** Server sequence number. */
  readonly seq: number;
  /** Domain event type. */
  readonly type: string;
  /** Concept ID (if applicable). */
  readonly conceptId: string | null;
  /** ISO 8601 timestamp. */
  readonly timestamp: string;
  /** Summary metadata for client-side classification. */
  readonly metadata: Record<string, unknown>;
}

/**
 * SyncCommit sent from client to server (Step 4).
 * Contains the pre-resolved events after the client processes divergence.
 *
 * @see docs/offline-sync-protocol.md Section 3.1 — Protocol Sequence, Step 4
 */
export interface SyncCommit {
  /** Server-assigned sync session ID (from SyncAck). */
  readonly syncSessionId: string;
  /** Pre-resolved events ready for server validation. */
  readonly resolvedEvents: ReadonlyArray<SyncEventEntry>;
}

/**
 * SyncResult returned by the server (Step 5).
 * Contains the authoritative reconciled state.
 *
 * @see docs/offline-sync-protocol.md Section 3.1 — Protocol Sequence, Step 5
 */
export interface SyncResult {
  /** Events accepted by the server with their classifications. */
  readonly acceptedEvents: ReadonlyArray<AcceptedEvent>;
  /** Events rejected by the server. */
  readonly rejectedEvents: ReadonlyArray<RejectedEvent>;
  /** Recalculated authoritative state. */
  readonly recalculatedState: RecalculatedState;
  /** Notifications to show the user. */
  readonly notifications: ReadonlyArray<SyncNotification>;
  /** Outreach message corrections (e.g., "streak was maintained offline"). */
  readonly outreachCorrections: ReadonlyArray<OutreachCorrection>;
}

/** An event accepted by the server during sync. */
export interface AcceptedEvent {
  /** The original idempotency key. */
  readonly idempotencyKey: string;
  /** How the event was classified by the server. */
  readonly classification: 'full_weight' | 'reduced_weight' | 'historical_only';
  /** Server-assigned sequence number. */
  readonly serverSeq: number;
}

/** An event rejected by the server during sync. */
export interface RejectedEvent {
  /** The original idempotency key. */
  readonly idempotencyKey: string;
  /** Machine-readable rejection reason. */
  readonly reason: string;
  /** Human-readable explanation. */
  readonly message: string;
}

/** Authoritative state recalculated by the server after sync. */
export interface RecalculatedState {
  /** Updated mastery overlay for concepts affected by the sync. */
  readonly masteryOverlay: Readonly<Record<string, {
    readonly masteryLevel: number;
    readonly predictedRecall: number;
    readonly status: string;
  }>>;
  /** XP delta from the sync. */
  readonly xpDelta: number;
  /** Updated total XP. */
  readonly totalXP: number;
  /** Streak status after sync. */
  readonly streakStatus: 'maintained' | 'broken' | 'restored' | 'extended';
  /** Updated streak count. */
  readonly currentStreak: number;
  /** Updated server sequence number. */
  readonly currentServerSeq: number;
}

/** Notification to show the user after sync. */
export interface SyncNotification {
  readonly type: 'sync_summary' | 'methodology_changed' | 'prerequisite_added' | 'content_updated';
  /** Human-readable message. Localized. */
  readonly message: string;
  /** Structured details for rich notification rendering. */
  readonly details: Record<string, unknown>;
}

/** Outreach correction for messages sent based on stale server state. */
export interface OutreachCorrection {
  readonly type: 'streak_restored' | 'review_completed_offline' | 'active_offline';
  /** Description of the original outreach message. */
  readonly originalMessage: string;
  /** Corrective action taken. */
  readonly correction: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// 3. OfflineEventQueue — SQLite-Backed Durable Queue
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Durable, ordered event queue backed by SQLite.
 *
 * Uses SQLite rather than AsyncStorage because:
 *   - Guaranteed write ordering under crash conditions
 *   - ACID transactions for atomic status updates during sync
 *   - Efficient range queries for batch operations
 *
 * SQLite schema matches `docs/offline-sync-protocol.md` Section 6.1.
 *
 * @example
 * ```tsx
 * const queue = new OfflineEventQueue(sqliteDb, studentId, deviceId);
 * await queue.initialize();
 *
 * // During offline session:
 * await queue.enqueue('ExerciseAttempted', { conceptId: '...', ... });
 *
 * // On reconnect:
 * const pending = await queue.getPendingEvents();
 * // ... sync ...
 * await queue.markSynced(syncSessionId, results);
 * ```
 */
export interface IOfflineEventQueue {
  /**
   * Initialize the queue — create tables if they don't exist,
   * recover from any interrupted sync (reset `syncing` -> `pending`).
   */
  initialize(): Promise<void>;

  /**
   * Enqueue a new event. Assigns an idempotency key (UUIDv7) and
   * auto-increments the client sequence number.
   *
   * @param eventType - Domain event type (e.g., "ExerciseAttempted").
   * @param payload - JSON-serializable event payload.
   * @returns The created QueuedEvent.
   */
  enqueue(eventType: string, payload: Record<string, unknown>): Promise<QueuedEvent>;

  /**
   * Get all pending events, ordered by client_seq.
   * Returns events with `status = 'pending'`.
   */
  getPendingEvents(): Promise<ReadonlyArray<QueuedEvent>>;

  /**
   * Get the count of pending events (for UI badge).
   */
  getPendingCount(): Promise<number>;

  /**
   * Get a summary of pending events grouped by type.
   * Returns e.g., `{ ExerciseAttempted: 12, AnnotationAdded: 3 }`.
   */
  getPendingSummary(): Promise<Readonly<Record<string, number>>>;

  /**
   * Get the oldest pending event timestamp (for "offline since" display).
   * Returns `null` if the queue is empty.
   */
  getOldestPendingTimestamp(): Promise<string | null>;

  /**
   * Mark events as `syncing` and assign a sync session ID.
   * Called at the start of a sync operation.
   *
   * @param syncSessionId - Server-assigned sync session ID.
   * @param idempotencyKeys - Keys of events being synced.
   */
  markSyncing(syncSessionId: string, idempotencyKeys: ReadonlyArray<string>): Promise<void>;

  /**
   * Mark events as `synced` and store the server result.
   * Called after a successful sync.
   *
   * @param syncSessionId - The sync session ID.
   * @param results - Map of idempotency key -> server result JSON.
   */
  markSynced(syncSessionId: string, results: Readonly<Record<string, string>>): Promise<void>;

  /**
   * Mark events as `failed` for a sync session.
   * Called when sync fails. Events will be retried on next sync.
   *
   * @param syncSessionId - The sync session ID.
   */
  markFailed(syncSessionId: string): Promise<void>;

  /**
   * Reset events that are stuck in `syncing` status back to `pending`.
   * Called on app startup to recover from interrupted syncs.
   */
  recoverInterruptedSync(): Promise<number>;

  /**
   * Purge synced events older than the retention period.
   * Synced events are retained for debugging; purged after 7 days.
   *
   * @param retentionDays - Number of days to retain synced events. Default: 7.
   */
  purgeSynced(retentionDays?: number): Promise<number>;

  /**
   * Compute SHA-256 checksum of the pending event queue.
   * Used for integrity verification in the SyncRequest.
   */
  computeChecksum(): Promise<string>;

  /**
   * Check queue health. Returns false if SQLite is corrupted.
   * On corruption, the caller should recreate the database.
   */
  healthCheck(): Promise<boolean>;

  /**
   * Drop all data and recreate the schema.
   * Nuclear option — used when SQLite corruption is detected.
   */
  reset(): Promise<void>;
}

// ─────────────────────────────────────────────────────────────────────────────
// 4. SyncManager — Orchestrates the Sync Handshake
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Orchestrates the full sync handshake protocol.
 *
 * Lifecycle:
 *   1. Gather pending events from the queue
 *   2. Send SyncRequest to the server
 *   3. Receive SyncAck with server divergence
 *   4. Classify events client-side (advisory)
 *   5. Send SyncCommit with pre-resolved events
 *   6. Receive SyncResult
 *   7. Apply server corrections to local state
 *   8. Update queue status
 *
 * @see docs/offline-sync-protocol.md Section 3 — Sync Handshake Protocol
 */
export interface ISyncManager {
  /**
   * Whether a sync is currently in progress.
   * Only one sync can run at a time per student.
   */
  readonly isSyncing: boolean;

  /**
   * Execute the full sync handshake.
   *
   * @returns The sync result from the server.
   * @throws {SyncError} If any step of the handshake fails.
   */
  sync(): Promise<SyncResult>;

  /**
   * Cancel an in-progress sync.
   * Events are reset to `pending` status for retry.
   */
  cancel(): Promise<void>;

  /**
   * Register a progress callback.
   * Called with a 0.0 - 1.0 progress value during sync.
   *
   * @returns Unsubscribe function.
   */
  onProgress(callback: (progress: number) => void): () => void;
}

/**
 * Sync error with structured information for retry logic.
 */
export class SyncError extends Error {
  constructor(
    message: string,
    /** HTTP status code from the server (if applicable). */
    public readonly statusCode: number | null,
    /** Machine-readable error code. */
    public readonly code: SyncErrorCode,
    /** Whether the sync can be retried. */
    public readonly retryable: boolean,
    /** Suggested retry delay in milliseconds (from server `Retry-After`). */
    public readonly retryAfterMs: number | null,
  ) {
    super(message);
    this.name = 'SyncError';
  }
}

export type SyncErrorCode =
  | 'NETWORK_ERROR'
  | 'CHECKSUM_MISMATCH'
  | 'CONCURRENT_SYNC'
  | 'RATE_LIMITED'
  | 'UNAUTHORIZED'
  | 'SERVER_ERROR'
  | 'TIMEOUT'
  | 'QUEUE_CORRUPTION';

// ─────────────────────────────────────────────────────────────────────────────
// 5. ConflictResolver — Applies Server Corrections to Local State
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Applies the server's SyncResult to local client state.
 *
 * Responsibilities:
 *   - Replace local mastery overlay with server-provided recalculated state
 *   - Update XP and streak in the user state
 *   - Update the offline event queue with server results
 *   - Determine the sync summary severity for UI rendering
 *   - Emit notifications for user-visible conflicts
 *
 * @see docs/offline-sync-protocol.md Section 7 — Client UI for Conflict Resolution
 */
export interface IConflictResolver {
  /**
   * Apply the server's sync result to local state.
   *
   * This is the final step of the sync handshake. It atomically updates
   * all local state stores to match the server's authoritative state.
   *
   * @param result - The SyncResult from the server.
   * @returns Summary for the sync toast/card UI.
   */
  apply(result: SyncResult): Promise<ConflictResolutionOutcome>;
}

/**
 * Outcome of conflict resolution, used to drive the sync summary UI.
 *
 * @see docs/offline-sync-protocol.md Section 7.2 — UI Components
 */
export interface ConflictResolutionOutcome {
  /** UI severity level. */
  readonly severity: 'silent' | 'minor' | 'significant';
  /** Total events processed. */
  readonly totalEvents: number;
  /** Events accepted at full weight. */
  readonly acceptedFullWeight: number;
  /** Events accepted at reduced weight. */
  readonly acceptedReducedWeight: number;
  /** Events accepted as historical record only. */
  readonly acceptedHistoricalOnly: number;
  /** Events where server recalculated (e.g., ConceptMastered). */
  readonly serverRecalculated: number;
  /** XP delta from the sync. */
  readonly xpDelta: number;
  /** Whether the streak was maintained by the offline session. */
  readonly streakMaintained: boolean;
  /** Human-readable summary message. Localized. */
  readonly summaryMessage: string;
  /** Per-concept conflict details (for the expanded summary card). */
  readonly conceptDetails: ReadonlyArray<ConceptConflictDetail>;
  /** Outreach corrections to display. */
  readonly outreachCorrections: ReadonlyArray<OutreachCorrection>;
}

/** Detail about a conflict for a specific concept. */
export interface ConceptConflictDetail {
  readonly conceptId: string;
  readonly conceptName: string;
  /** What happened to this concept during the offline period. */
  readonly serverAction: 'mastery_decayed' | 'methodology_switched' | 'prerequisite_added' | 'content_updated' | 'none';
  /** How the student's offline work for this concept was classified. */
  readonly resolution: ClientResolution;
  /** Human-readable explanation for the student. */
  readonly explanation: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// 6. ClockSkewDetector — Client Clock Offset Estimation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Estimates the offset between the client clock and the server clock.
 *
 * Uses the NTP-style round-trip algorithm:
 *   `offset = server_time - (client_send_time + client_receive_time) / 2`
 *
 * Maintains a rolling window of offset measurements for stability.
 * The estimated offset is stored and sent with every SyncRequest.
 *
 * @see docs/offline-sync-protocol.md Section 6.3 — Clock Skew Handling
 */
export interface IClockSkewDetector {
  /**
   * Current estimated clock offset in milliseconds.
   * Positive = client ahead of server.
   * Negative = client behind server.
   */
  readonly currentOffsetMs: number;

  /**
   * Record a new round-trip measurement.
   * Called after every successful server response.
   *
   * @param serverTimestamp - ISO 8601 timestamp from the server response.
   * @param clientSendTime - `Date.now()` when the request was sent.
   * @param clientReceiveTime - `Date.now()` when the response was received.
   */
  recordMeasurement(
    serverTimestamp: string,
    clientSendTime: number,
    clientReceiveTime: number,
  ): void;

  /**
   * Adjust a client-local timestamp by the current offset estimate.
   * Returns an ISO 8601 string adjusted to approximate server time.
   *
   * @param clientTimestamp - ISO 8601 timestamp from the client clock.
   * @returns Adjusted ISO 8601 timestamp.
   */
  adjustTimestamp(clientTimestamp: string): string;

  /**
   * Get the current `Date.now()` adjusted for estimated server time.
   */
  serverNow(): number;

  /**
   * Whether the current offset exceeds the sanity bound (8 hours).
   * If true, timestamps should be flagged and server-receive time used instead.
   *
   * @see docs/offline-sync-protocol.md Section 6.3 — Sanity bound
   */
  isExcessive(): boolean;

  /**
   * Reset the measurement history.
   * Called on login (new session, fresh measurements).
   */
  reset(): void;
}

/**
 * Configuration for the ClockSkewDetector.
 */
export interface ClockSkewConfig {
  /** Number of measurements to keep in the rolling window. Default: 10. */
  readonly windowSize: number;
  /**
   * Maximum allowed offset in milliseconds before flagging as excessive.
   * Default: 28,800,000 (8 hours).
   *
   * @see docs/offline-sync-protocol.md Section 6.3 — Sanity bound
   */
  readonly maxOffsetMs: number;
  /**
   * Outlier rejection threshold in standard deviations.
   * Measurements beyond this threshold are discarded. Default: 2.0.
   */
  readonly outlierSigma: number;
}

/** Default clock skew detector configuration. */
export const DEFAULT_CLOCK_SKEW_CONFIG: ClockSkewConfig = {
  windowSize: 10,
  maxOffsetMs: 8 * 60 * 60 * 1_000, // 8 hours
  outlierSigma: 2.0,
} as const;

// ─────────────────────────────────────────────────────────────────────────────
// 7. Sync Configuration
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Client-side sync configuration.
 * Maps to server-side parameters from `docs/offline-sync-protocol.md` Appendix B.
 */
export interface SyncConfig {
  /** REST endpoint for sync handshake. */
  readonly syncEndpoint: string;
  /**
   * Maximum events to send in a single SyncRequest before the server
   * switches to async processing (202 response).
   * Default: 50.
   */
  readonly asyncThreshold: number;
  /**
   * Maximum time in ms to wait for a sync response before timeout.
   * Default: 30000 (30 seconds).
   */
  readonly syncTimeoutMs: number;
  /**
   * Retry configuration for failed syncs.
   */
  readonly retry: {
    /** Maximum retry attempts. Default: 3. */
    readonly maxRetries: number;
    /** Base delay in ms before first retry. Default: 2000. */
    readonly baseDelayMs: number;
    /** Maximum delay cap in ms. Default: 60000. */
    readonly maxDelayMs: number;
    /** Backoff multiplier. Default: 2.0. */
    readonly backoffMultiplier: number;
  };
  /**
   * Whether to automatically sync on reconnection.
   * Default: true.
   */
  readonly autoSyncOnReconnect: boolean;
  /**
   * Minimum interval in ms between automatic sync attempts.
   * Prevents sync storms when connectivity is flapping.
   * Default: 10000 (10 seconds).
   */
  readonly minSyncIntervalMs: number;
  /**
   * Number of days to retain synced events in SQLite before purging.
   * Default: 7.
   */
  readonly syncedRetentionDays: number;
  /** Clock skew detector configuration. */
  readonly clockSkew: ClockSkewConfig;
}

/** Default sync configuration. */
export const DEFAULT_SYNC_CONFIG: SyncConfig = {
  syncEndpoint: '/api/v1/sync',
  asyncThreshold: 50,
  syncTimeoutMs: 30_000,
  retry: {
    maxRetries: 3,
    baseDelayMs: 2_000,
    maxDelayMs: 60_000,
    backoffMultiplier: 2.0,
  },
  autoSyncOnReconnect: true,
  minSyncIntervalMs: 10_000,
  syncedRetentionDays: 7,
  clockSkew: DEFAULT_CLOCK_SKEW_CONFIG,
} as const;

// ─────────────────────────────────────────────────────────────────────────────
// 8. Event Classifier (Client-Side Advisory)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Client-side event classifier.
 *
 * Pre-classifies offline events against the server divergence data (SyncAck).
 * This classification is advisory — the server performs its own validation.
 * The purpose is to reduce server-side processing by providing hints.
 *
 * @see docs/offline-sync-protocol.md Section 3.2 Step 3 — Client-Side Pre-Resolution
 */
export interface IEventClassifier {
  /**
   * Classify a single offline event against the server divergence data.
   *
   * @param event - The queued event to classify.
   * @param syncAck - The server's SyncAck containing divergence data.
   * @returns The advisory resolution.
   */
  classify(event: QueuedEvent, syncAck: SyncAck): ClientResolution;

  /**
   * Classify all pending events in batch.
   *
   * @param events - All pending events from the queue.
   * @param syncAck - The server's SyncAck containing divergence data.
   * @returns Map of idempotency key -> advisory resolution.
   */
  classifyBatch(
    events: ReadonlyArray<QueuedEvent>,
    syncAck: SyncAck,
  ): Readonly<Record<string, ClientResolution>>;
}

// ─────────────────────────────────────────────────────────────────────────────
// 9. Factory Interface
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Factory for creating offline sync subsystem instances.
 * Abstracts platform-specific dependencies (SQLite implementation, fetch, etc.).
 *
 * @example
 * ```tsx
 * // React Native
 * import * as SQLite from 'expo-sqlite';
 * const factory = createOfflineSyncFactory({
 *   openDatabase: () => SQLite.openDatabaseAsync('cena-offline.db'),
 *   fetch: globalThis.fetch,
 *   getAccessToken: () => useStore.getState().accessToken,
 *   getDeviceId: () => DeviceInfo.getUniqueId(),
 * });
 *
 * const queue = factory.createEventQueue(studentId);
 * const syncManager = factory.createSyncManager(queue);
 * ```
 */
export interface IOfflineSyncFactory {
  /** Create an offline event queue for a student. */
  createEventQueue(studentId: string): IOfflineEventQueue;

  /** Create a sync manager using the given queue. */
  createSyncManager(queue: IOfflineEventQueue): ISyncManager;

  /** Create a conflict resolver. */
  createConflictResolver(): IConflictResolver;

  /** Create a clock skew detector. */
  createClockSkewDetector(config?: Partial<ClockSkewConfig>): IClockSkewDetector;

  /** Create an event classifier. */
  createEventClassifier(): IEventClassifier;
}

/**
 * Platform-specific dependencies required by the factory.
 */
export interface OfflineSyncDependencies {
  /** Open or create a SQLite database. */
  readonly openDatabase: (name: string) => Promise<SQLiteDatabase>;
  /** Fetch function (platform-native or polyfill). */
  readonly fetch: typeof globalThis.fetch;
  /** Get the current JWT access token from auth state. */
  readonly getAccessToken: () => string | null;
  /** Get the unique device identifier. */
  readonly getDeviceId: () => string;
  /** Get the current student ID from user state. */
  readonly getStudentId: () => string | null;
  /** Sync configuration overrides. */
  readonly config?: Partial<SyncConfig>;
}

/**
 * Minimal SQLite database interface.
 * Abstractions over expo-sqlite, react-native-sqlite-storage, or sql.js.
 */
export interface SQLiteDatabase {
  /** Execute a SQL statement with optional parameters. */
  executeSql(sql: string, params?: unknown[]): Promise<SQLiteResultSet>;
  /** Execute multiple statements in a transaction. */
  transaction(callback: (tx: SQLiteTransaction) => void): Promise<void>;
  /** Close the database connection. */
  close(): Promise<void>;
}

export interface SQLiteTransaction {
  executeSql(sql: string, params?: unknown[]): void;
}

export interface SQLiteResultSet {
  readonly rows: ReadonlyArray<Record<string, unknown>>;
  readonly rowsAffected: number;
  readonly insertId?: number;
}
