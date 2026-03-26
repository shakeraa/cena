## MOBILE / UX ARCHITECTURE REVIEW — Amir Naveh, Senior Mobile Architect

**Review scope:** All contracts in `contracts/mobile/lib/` (11 files) and `contracts/frontend/` (4 files).
**Reviewed against:** Flutter 3.24+, Riverpod 2.6, Drift 2.22, real-world Hebrew-locale STEM ed-tech on mid-range Android (Samsung A14, Xiaomi Redmi Note 12).

---

### CRITICAL (will cause crashes or data loss on device)

1. **SessionNotifier.submitAnswer force-unwraps null session**: `app_state.dart:119` — `state.currentSession!.id` is force-unwrapped. If the WebSocket delivers a late `QuestionPresented` after the session has been ended/nulled (race condition during disconnect), this crashes the app mid-session. The `exercise == null` guard one line above does not protect against a null session. **Fix:** Guard both `exercise` and `currentSession` before any access; return early and log the orphan event.

2. **SessionNotifier.startSession sends empty studentId**: `app_state.dart:105` — `studentId: ''` is hardcoded with a comment "Injected from UserNotifier". But there is no injection mechanism in the contract — `SessionNotifier` has no dependency on `UserNotifier` or `userProvider`. If implementation follows the contract literally, every `StartSession` command hits the server with an empty student ID, which will either throw 400 or silently create a ghost session. **Fix:** Accept `studentId` as a constructor dependency or read from `UserState` via a `Ref`.

3. **No offline queue write-ahead for answer submissions**: `app_state.dart:115-127` — `submitAnswer` calls `webSocketService.attemptConcept()` directly. If the WebSocket is disconnected, the contract says `send()` "queues the message for send-on-reconnect" (websocket_service.dart:389), but this is a volatile in-memory queue inside the WebSocket service — NOT the durable SQLite-backed `OfflineEventQueue`. A process kill between disconnect and reconnect loses the student's answer permanently. For an education app, losing a correct answer a student worked 5 minutes on is a 1-star review generator. **Fix:** All mutation commands must write to `OfflineEventQueue.enqueue()` FIRST, then send via WebSocket. The WebSocket layer should pull from the durable queue, not maintain its own volatile buffer.

4. **No SQLite corruption recovery path in Flutter contracts**: The frontend `offline-sync-client.ts` has `healthCheck()` and `reset()` for SQLite corruption (lines 399-406), but the Flutter `offline_sync_service.dart` has NO equivalent. Drift/SQLite on Android is susceptible to corruption from sudden power loss or SD-card unmount on budget phones (common among Israeli high-school students). Corruption means the entire offline queue is unrecoverable. **Fix:** Add `Future<bool> healthCheck()` and `Future<void> recreateDatabase()` to `OfflineEventQueue`. Run health check on every app cold start.

5. **KnowledgeGraphState.selectedNode uses firstWhere without null safety on .cast**: `app_state.dart:233-235` — `.cast<ConceptNode?>().firstWhere(...)` with `orElse: () => null` looks safe, but `.cast<ConceptNode?>()` on a `List<ConceptNode>` is a no-op and can throw `CastError` in edge cases with Dart's type system if the list has been modified concurrently. More critically, if `graph` is null, accessing `graph?.nodes` correctly returns null, but the pattern is fragile. **Fix:** Use `nodes.where((n) => n.conceptId == selectedNodeId).firstOrNull` (Dart 3.0+ collection extension).

6. **copyWith cannot null-out optional fields**: `app_state.dart` — Every `copyWith` implementation uses the `??` pattern (e.g., `error: error ?? this.error`). This means once `error` is set to a non-null value, you can NEVER clear it back to null — calling `copyWith(error: null)` preserves the old error. This will cause stale error banners that never dismiss and stale session references that prevent starting new sessions. This is a known anti-pattern with hand-rolled `copyWith` in Dart. **Fix:** Use freezed for state classes (like the domain models) or use a sentinel pattern: `Object? error = _sentinel` where `_sentinel` is a private const.

---

### HIGH (will cause poor UX or 1-star reviews)

1. **Knowledge graph will drop to <15fps with 500+ nodes on mid-range phones**: `knowledge_graph_widget.dart` — The contract specifies that each `ConceptNode` is a `ConsumerStatefulWidget` with its own `AnimationController` (line 264). With 2000 curriculum nodes across 5 subjects (realistic for Bagrut), that is 2000 widget instances each ticking an animation controller. The `PrerequisiteEdgePainter` repaints on every `flowAnimationProgress` tick (line 356), iterating all edges. On a Samsung A14 (Mali-G57), this will hit thermal throttle within 30 seconds and drop to single-digit fps. **Fix:** (a) Render nodes as part of `CustomPainter`, not as individual widgets. (b) Implement viewport culling — only paint nodes visible in the current `InteractiveViewer` viewport. (c) Use `RepaintBoundary` aggressively. (d) Cap force-directed layout iterations. (e) Implement level-of-detail: at zoom < 0.5x, render nodes as colored dots without text.

2. **No session recovery after app kill / process death**: The `SessionScreen` starts a session in `initState` (contract comment, line 64-65), but session state is `autoDispose` (line 664). If Android kills the process while the student is mid-question (common on 3GB RAM phones when notifications arrive), the session state is gone. On restart, the student sees the home screen, not the session they were in. The server still has the session active. There is no mechanism to detect an orphan server session and resume it. **Fix:** (a) Persist `activeSessionId` and `currentExercise` to Hive/Drift on every state change. (b) On app startup, check for a persisted session and attempt to resume via a `ResumeSession` command (missing from the WebSocket contract). (c) Add `ResumeSession` to `CommandTargets`.

3. **Push notification deep link to session has no contract**: `app_state.dart:582` defines `AppNotification.actionRoute` (e.g., `/session/start` or `/graph/concept/123`), but there is NO navigation contract, no route parser, and no mechanism to handle cold-start deep links. A student tapping a "Your streak is about to expire!" push notification while the app is killed will launch to the home screen, not into a session. On iOS, the notification payload must be processed in `didFinishLaunchingWithOptions`; on Android, in the `FlutterActivity.onCreate` intent extras. None of this is addressed. **Fix:** Add a `DeepLinkRouter` contract with `handleRoute(String route)` that can be invoked from both warm and cold launch paths. Add a `PendingDeepLink` field to `OutreachState`.

4. **WebSocket reconnection during active session will lose current question state**: The reconnection config (websocket_service.dart:330-348) handles reconnection at the transport level, but there is no re-subscription or state reconciliation protocol. After reconnect, the `MessageRouter` handlers are presumably still registered, but the server may have timed out the session or advanced to a different question. The client has no way to know. **Fix:** Add a `SessionStateRequest` command that the client sends on every reconnect to get the current server-side session state. The server responds with the active question (if any) or a session-expired notification.

5. **RTL/LTR mixed content in math questions is unaddressed**: The `QuestionCard` contract mentions "LaTeX rendering via flutter_math_fork" and "RTL support: Hebrew text with proper bidi handling" (session_screen.dart:101-103), but there is no contract for how mixed-direction content is handled. Hebrew paragraph with embedded `$\frac{d}{dx} \sin(x) = \cos(x)$` LaTeX requires explicit `Directionality` wrapping — flutter_math_fork renders LTR by default. Without this, the math will render backwards or misaligned in RTL context. This is a known pain point for Hebrew STEM apps. **Fix:** Add a `MathTextWidget` contract that wraps `flutter_math_fork` in a `Directionality(textDirection: TextDirection.ltr)` block while keeping the surrounding text RTL. Define a parser for `$...$` delimited segments.

6. **Streak warning countdown has no live timer contract**: `streak_widget.dart:247-269` — `StreakWarning` takes an `expiresAt` DateTime but is a `ConsumerWidget` (stateless rebuild). There is no `Timer` or `Stream` to update the countdown display. The countdown will show the value at first build and never update — "Time remaining: 3 hours" will still say "3 hours" after 2 hours. **Fix:** Make `StreakWarning` a `ConsumerStatefulWidget` with a periodic `Timer` that triggers `setState` every minute (or use a `StreamBuilder` over a `Stream.periodic`).

7. **FeedbackOverlay auto-dismiss timer fires after dispose**: `session_screen.dart:339` — `_autoDismissTimer = Timer(widget.displayDuration, widget.onDismiss)`. If the user navigates away before the timer fires, `widget.onDismiss` is called on a disposed widget's context. While the timer IS cancelled in `dispose()`, there is a race if `onDismiss` triggers a navigation or state change that causes the dispose — the callback closure captures the widget reference. **Fix:** Guard the timer callback: `_autoDismissTimer = Timer(widget.displayDuration, () { if (mounted) widget.onDismiss(); })`.

8. **sessionProvider is autoDispose — will destroy mid-session if all listeners detach**: `app_state.dart:664` — `StateNotifierProvider.autoDispose`. If the student navigates from `SessionScreen` to the knowledge graph (e.g., to check a concept) and back, the session provider may be disposed if no other widget is watching it. All session state, WebSocket subscriptions, and question history are lost. The student returns to find a blank session. **Fix:** Either remove `autoDispose` from `sessionProvider`, or use `ref.keepAlive()` while a session is active. The `userProvider` correctly does NOT use autoDispose (line 681) — `sessionProvider` should follow the same pattern during an active session.

9. **No error boundary or retry UX for WebSocket send failures**: `SessionNotifier.startSession` (line 101-111) catches errors and sets `state.error`, but `submitAnswer`, `requestHint`, `skipQuestion`, and `switchApproach` have NO try-catch. If the WebSocket send throws (connection dropped exactly at send time), the exception propagates unhandled to the UI layer, which likely means an unhandled Future error crash or a frozen "submitting" state. **Fix:** Wrap all WebSocket send calls in try-catch with consistent error handling. Provide a retry mechanism in the UI.

10. **Analytics events use concrete classes, not freezed — no toJson consistency guarantee**: `analytics_service.dart` — All analytics event classes are plain Dart classes with hand-written `toJson()`. Unlike the domain models (which use freezed/json_serializable with generated code), these are manually maintained. A single typo in a key name (e.g., `'hased_student_id'`) will silently corrupt analytics data without any compile-time safety. **Fix:** Use `@JsonSerializable()` annotation on analytics events or convert to freezed.

---

### MEDIUM (technical debt, will slow development)

1. **Two separate state management paradigms (Flutter: Riverpod, Frontend: Zustand)**: The Flutter contracts use Riverpod with `StateNotifier`, while the frontend contracts use Zustand with slices. This is architecturally fine for separate apps, but the state shapes have diverged: the Flutter `SessionState` has `hintsUsed` while the frontend `SessionState` has `hintsUsedThisSession` + `questionsSkippedThisSession`. The WebSocket messages also diverge — Flutter has `AttemptConcept` while the frontend has `SubmitAnswer` with additional `behavioralSignals`. This means bug-for-bug parity across platforms is unlikely. **Fix:** Establish a canonical state shape document that both platforms must conform to. Consider using a shared protobuf definition for state.

2. **Methodology enum mismatch between Flutter and frontend**: Flutter `Methodology` enum (domain_models.dart:60-71) has 5 values: `spacedRepetition, interleaved, blocked, adaptiveDifficulty, socratic`. Frontend `MethodologyType` (signalr-messages.ts:66-74) has 8 values: `socratic, spaced-repetition, project-based, blooms-progression, feynman, worked-example, analogy, retrieval-practice`. The Flutter app will crash or silently drop data when the server sends a methodology the client doesn't know about (e.g., `feynman`). **Fix:** Align enums across platforms. Add an `unknown` fallback to the Flutter enum with graceful degradation.

3. **SubjectDiagramPalette uses raw int colors instead of Color**: `diagram_models.dart:416-419` — `final int primary` instead of `Color`. Every usage site will need `Color(palette.primary)` wrapping. Meanwhile, `SubjectColors` in `knowledge_graph_widget.dart:23` uses `Color` directly. Two different color systems for the same subjects, with slightly different hex values (e.g., math: `0xFF0097A7` vs `0xFF0891B2`). **Fix:** Consolidate to a single `SubjectDesignTokens` class used by both the diagram system and the knowledge graph.

4. **ErrorType enum divergence**: Flutter: `conceptual, procedural, careless, notation, incomplete, none`. Frontend TS: `conceptual-misunderstanding, computational-error, notation-error, incomplete-reasoning, off-topic, partial-understanding`. GraphQL: `PROCEDURAL, CONCEPTUAL, MOTIVATIONAL, NONE`. Three different error taxonomies across three contract files. **Fix:** Define once in a shared schema (protobuf or GraphQL enum) and generate platform-specific code.

5. **No pagination or virtualization contract for knowledge graph nodes**: The `KnowledgeGraph` model loads ALL nodes into memory (domain_models.dart:343). The GraphQL schema returns all nodes in one query (`nodes: [MasteryEdge!]!` with no pagination). For a full Bagrut curriculum (estimated 1500-2000 concepts across 5 subjects), this is 2000 nodes + 5000 edges deserialized from JSON into freezed objects on the main isolate. On a mid-range phone, this will cause a 2-3 second jank spike on first load. **Fix:** Add cursor-based pagination to the `KnowledgeGraph` GraphQL query. Load only the visible subject's nodes. Deserialize on a background isolate.

6. **No contract for keyboard avoidance in session screen**: The `SessionScreen` layout has `AnswerInput` at the bottom (session_screen.dart:44), but for `FreeTextAnswerInput` and `NumericAnswerInput`, the soft keyboard will cover the input field on phones without a physical keyboard (all of them). Hebrew keyboards are particularly tall due to additional character rows. **Fix:** Wrap the session screen in a `Scaffold` with `resizeToAvoidBottomInset: true` or use `SingleChildScrollView` with `reverse: true` for the answer area. Document this in the contract.

7. **OutreachNotifier has no limit on pendingNotifications list**: `app_state.dart:591-595` — `addNotification` appends without bound. If push notifications accumulate while the app is in background (common for streak warnings + methodology changes), the list grows unbounded. More importantly, `markRead` creates a new `AppNotification` instance for every notification on every mark-read call (line 602-609) instead of using a `copyWith` or immutable update. **Fix:** Cap at 50 notifications with FIFO eviction. Use immutable update or make `AppNotification` a freezed class.

8. **XpLevelCalculator.levelForXp has an off-by-one bug**: `streak_widget.dart:97-104` — The loop increments `level` BEFORE adding `xpForLevel(level)` to `accumulated`. After `level++`, the check adds `xpForLevel` of the NEW level, not the one just incremented to. This means `totalXpForLevel(level)` and `levelForXp(totalXp)` are NOT inverses. Specifically: `levelForXp(totalXpForLevel(5))` will NOT return 5. **Fix:** The loop body should be `accumulated += xpForLevel(level); level++;` or restructured to ensure the accumulation and level increment are consistent.

9. **No accessibility contracts**: None of the widget contracts specify `Semantics`, `ExcludeSemantics`, `MergeSemantics`, or any accessibility labels. The knowledge graph (the hero feature) is a `CustomPainter` with no semantic tree — it is completely invisible to TalkBack/VoiceOver. Israeli accessibility law (Shivyon Zkhuyot) requires digital education tools to be accessible. **Fix:** Add `SemanticsNode` annotations for all interactive elements. For the knowledge graph, provide an accessible list alternative.

10. **FeatureFlags are not runtime-updatable**: `app_config.dart:80-141` — Feature flags are compile-time constants per environment. There is no mechanism for server-driven feature flags (remote config). The frontend `state-contracts.ts` has `featureFlags: Record<string, boolean | string | number>` received from the server, but the Flutter side has no equivalent. This means A/B experiment changes require an app release. **Fix:** Add a `RemoteFeatureFlags` provider that fetches from the server on app start and merges with local defaults.

11. **l10n configuration is commented out**: `pubspec.yaml:163-167` — The entire `l10n` section is commented out. `flutter_localizations` is a dependency but `generate: true` without a configured ARB directory means localization is not actually wired up. The Hebrew strings in `MethodologyIndicator` (session_screen.dart:428-441) are hardcoded inline, not from ARB files. This makes translation maintenance impossible. **Fix:** Uncomment and configure l10n. Move all user-facing strings to ARB files.

12. **DiagramCacheService has no bandwidth-aware prefetch strategy**: `diagram_models.dart:381-386` — `prefetchForFrontier` takes concept IDs but has no awareness of network conditions. On cellular data (typical for Israeli teens commuting), prefetching 50MB of SVGs per subject will burn through data plans. **Fix:** Add a `NetworkAwarePrefetch` strategy that checks connectivity type (WiFi vs cellular) and respects a configurable cellular data budget.

---

### WHAT'S ACTUALLY GOOD

1. **Three-tier event classification (unconditional/conditional/server-authoritative)** is genuinely thoughtful. This is the right abstraction for offline sync in an educational app where mastery calculations must be server-authoritative but annotations should always land. The weight system (1.0/0.75/0.0) is elegant.

2. **Clock skew detection with NTP-style estimation** is a detail most mobile architects skip entirely. Getting this right means offline timestamps are meaningful, which is critical for Bayesian Knowledge Tracing where time-between-attempts matters.

3. **Idempotency keys with UUID + sequence number** provide both uniqueness AND ordering guarantees. This is exactly right for offline replay — you need both properties and most teams implement only one.

4. **Cognitive load / fatigue tracking with break suggestions** is pedagogically sound and rare in ed-tech. The 0.7 threshold with a non-blocking break screen (student can skip) respects both the science and the user's autonomy.

5. **Freezed + json_serializable for all domain models** is the correct choice. Immutable, null-safe, with generated serialization. The `@JsonValue` annotations on enums ensure wire-format stability independent of Dart naming conventions.

6. **Privacy-first analytics with SHA-256 hashed student IDs and per-install salt** is a strong privacy architecture. The fact that the hash is NOT correlatable across devices without server-side mapping shows real GDPR/privacy thinking.

7. **The gamification intensity enum (minimal/standard/full)** is a smart A/B testing lever. Being able to dial gamification up or down per cohort — instead of all-or-nothing — will generate excellent experimental data on what helps vs. what annoys Israeli students.

8. **The `EventClassifier.defaultClassifications` map** is well-designed. Making classification explicit and data-driven (rather than buried in if-else chains) means adding new event types is a one-line change with no risk of misclassification.

9. **WebSocket `MessageRouter` with typed handlers and `fromJson` factory** is a clean pattern for SignalR message routing. The separation of transport (`WebSocketService`) from message routing (`MessageRouter`) is correct.

10. **Diagram system with pre-generated SVGs, CDN caching, and interactive hotspots** is architecturally excellent. Pre-generating diagrams overnight avoids LLM latency during sessions. The hotspot model with normalized coordinates is resolution-independent. The cache TTL with content-hash integrity verification is production-grade.

---

### SUMMARY

The contracts show strong backend/protocol thinking (offline sync, event sourcing, BKT) but have significant gaps in mobile-specific concerns. The critical issues around data loss (answer submissions not durably queued, session state not surviving process death, copyWith null-clearing bug) must be fixed before any user testing. The performance issues with the knowledge graph rendering will be visible on day one with real curriculum data. The RTL/LTR mixed content problem is a known hard problem in Hebrew STEM apps and needs a dedicated widget contract.

**Top 3 actions before first user test:**
1. Route ALL mutation commands through `OfflineEventQueue` (not just WebSocket send buffer)
2. Remove `autoDispose` from `sessionProvider` and persist session state for process-death recovery
3. Prototype the knowledge graph with 1000 nodes on a Samsung A14 — the current widget-per-node architecture will not survive

**Estimated effort to address all CRITICAL + HIGH issues:** 3-4 engineering weeks for a senior Flutter developer.
