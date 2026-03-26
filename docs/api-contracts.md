# Cena Platform — API Contracts

> **Status:** Draft
> **Last updated:** 2026-03-26
> **Audience:** Engineering team, frontend developers, integration partners
> **Companion to:** `docs/architecture-design.md`

All timestamps are **ISO 8601 UTC** (`2026-03-26T14:30:00Z`).
All monetary values are in **NIS** unless explicitly noted.
All IDs are **UUIDv7** (time-sortable) unless explicitly noted.

---

## Table of Contents

1. [SignalR WebSocket Message Envelope](#1-signalr-websocket-message-envelope)
2. [gRPC Proto — LLM Anti-Corruption Layer](#2-grpc-proto--llm-anti-corruption-layer)
3. [GraphQL Schema — Read Side](#3-graphql-schema--read-side)
4. [REST API Surface](#4-rest-api-surface)
5. [NATS Subject Hierarchy](#5-nats-subject-hierarchy)

---

## 1. SignalR WebSocket Message Envelope

The React Native client connects to the Proto.Actor cluster via ASP.NET Core SignalR. All messages use a **discriminated union envelope** so the client can deserialize any frame with a single `switch` on `type`.

### 1.1 Envelope Shape (TypeScript)

```typescript
/**
 * Discriminated union envelope for all SignalR frames.
 * Direction: "command" = client -> server, "event" = server -> client.
 */
interface MessageEnvelope<T extends string, P> {
  /** Discriminator — doubles as the SignalR hub method name */
  type: T;
  /** Direction tag for client-side routing */
  direction: "command" | "event";
  /** Client-generated for commands; server echoes it on related events */
  correlationId: string; // UUIDv7
  /** ISO 8601 UTC — when the message was created */
  timestamp: string;
  /** Type-specific payload */
  payload: P;
}
```

### 1.2 Command Messages (Client -> Server)

#### `StartSession`

Begin a new learning session for the authenticated student.

```typescript
interface StartSessionPayload {
  /** Target subject (e.g., "math", "physics", "cs") */
  subjectId: string;
  /** Optional — resume a specific concept; null = system selects */
  conceptId: string | null;
  /** Client-reported device context */
  device: {
    platform: "ios" | "android" | "web";
    screenWidth: number;
    screenHeight: number;
    locale: string; // BCP 47 (e.g., "he-IL")
  };
}

type StartSession = MessageEnvelope<"StartSession", StartSessionPayload>;
```

#### `SubmitAnswer`

Submit an answer to the currently presented question.

```typescript
interface SubmitAnswerPayload {
  sessionId: string;
  questionId: string;
  /** Free-text answer — may contain LaTeX (delimited by $...$) */
  answer: string;
  /** Time in milliseconds the student spent on this question */
  responseTimeMs: number;
  /** Optional confidence self-report (1-5 scale, null if not shown) */
  confidence: number | null;
}

type SubmitAnswer = MessageEnvelope<"SubmitAnswer", SubmitAnswerPayload>;
```

#### `RequestHint`

Ask the system for a hint on the current question.

```typescript
interface RequestHintPayload {
  sessionId: string;
  questionId: string;
  /** Which hint level (1 = gentle nudge, 2 = partial reveal, 3 = full walkthrough) */
  hintLevel: 1 | 2 | 3;
}

type RequestHint = MessageEnvelope<"RequestHint", RequestHintPayload>;
```

#### `SkipQuestion`

Skip the current question and request the next one.

```typescript
interface SkipQuestionPayload {
  sessionId: string;
  questionId: string;
  /** Optional reason — helps the stagnation detector */
  reason: "too-hard" | "already-know" | "not-relevant" | "other" | null;
}

type SkipQuestion = MessageEnvelope<"SkipQuestion", SkipQuestionPayload>;
```

#### `AddAnnotation`

Add a free-text annotation (note/reflection) to the current concept.

```typescript
interface AddAnnotationPayload {
  sessionId: string;
  conceptId: string;
  /** Free-text note, supports Markdown */
  text: string;
  /** Annotation type */
  kind: "note" | "question" | "confusion" | "insight";
}

type AddAnnotation = MessageEnvelope<"AddAnnotation", AddAnnotationPayload>;
```

#### `EndSession`

Gracefully end the current learning session.

```typescript
interface EndSessionPayload {
  sessionId: string;
  /** Client-reported reason for ending */
  reason: "completed" | "tired" | "out-of-time" | "app-background" | "manual";
}

type EndSession = MessageEnvelope<"EndSession", EndSessionPayload>;
```

#### `RequestNextConcept`

Explicitly request the system to move to the next concept in the session.

```typescript
interface RequestNextConceptPayload {
  sessionId: string;
  /** Optional — specific concept to navigate to; null = system selects */
  targetConceptId: string | null;
}

type RequestNextConcept = MessageEnvelope<"RequestNextConcept", RequestNextConceptPayload>;
```

#### `UpdatePreferences`

Update real-time session preferences (e.g., toggle camera, change difficulty).

```typescript
interface UpdatePreferencesPayload {
  /** Preferred explanation style */
  explanationStyle?: "concise" | "detailed" | "visual" | "step-by-step";
  /** Preferred difficulty bias (-1 = easier, 0 = adaptive, 1 = harder) */
  difficultyBias?: -1 | 0 | 1;
  /** Language preference for generated content */
  contentLanguage?: "he" | "ar" | "en";
  /** Whether the student wants the camera-based attention detection active */
  cameraEnabled?: boolean;
}

type UpdatePreferences = MessageEnvelope<"UpdatePreferences", UpdatePreferencesPayload>;
```

### 1.3 Event Messages (Server -> Client)

#### `SessionStarted`

Confirms session creation and provides initial state.

```typescript
interface SessionStartedPayload {
  sessionId: string;
  subjectId: string;
  /** The concept the session will begin with */
  startingConceptId: string;
  startingConceptName: string;
  /** Active methodology for this concept */
  methodology: MethodologyType;
  /** Student's current XP at session start */
  currentXP: number;
  /** Current streak count */
  streakDays: number;
}

type MethodologyType =
  | "socratic-dialogue"
  | "worked-examples"
  | "scaffolded-practice"
  | "visual-spatial"
  | "analogy-based"
  | "error-analysis"
  | "spaced-retrieval";

type SessionStarted = MessageEnvelope<"SessionStarted", SessionStartedPayload>;
```

#### `QuestionPresented`

Server pushes the next question for the student to answer.

```typescript
interface QuestionPresentedPayload {
  sessionId: string;
  questionId: string;
  conceptId: string;
  conceptName: string;
  /** The question content — may contain LaTeX and Markdown */
  questionText: string;
  /** Optional rendered diagram (inline SVG string or URL) */
  diagram: string | null;
  /** Question format hint for the client UI */
  format: "free-text" | "multiple-choice" | "numeric" | "proof" | "graph-sketch";
  /** Multiple-choice options (null unless format = "multiple-choice") */
  options: { id: string; text: string }[] | null;
  /** Difficulty rating (1-10 scale) */
  difficulty: number;
  /** Active methodology for this question */
  methodology: MethodologyType;
  /** Position in session sequence */
  questionIndex: number;
  /** Optional hint that this is a spaced-repetition review item */
  isReview: boolean;
}

type QuestionPresented = MessageEnvelope<"QuestionPresented", QuestionPresentedPayload>;
```

#### `AnswerEvaluated`

Server evaluates the student's submitted answer.

```typescript
interface AnswerEvaluatedPayload {
  sessionId: string;
  questionId: string;
  /** Whether the answer is correct */
  correct: boolean;
  /** Correctness score (0.0 - 1.0, for partial credit) */
  score: number;
  /** LLM-generated explanation of the evaluation */
  explanation: string;
  /** Classified error type (null if correct) */
  errorType: ErrorType | null;
  /** Hint for the next pedagogical move */
  nextAction: "next-question" | "retry" | "hint" | "switch-methodology" | "concept-review";
  /** Updated mastery level for this concept (0.0 - 1.0) */
  updatedMastery: number;
  /** XP earned for this answer */
  xpEarned: number;
}

type ErrorType =
  | "conceptual-misunderstanding"
  | "computational-error"
  | "notation-error"
  | "incomplete-reasoning"
  | "off-topic"
  | "partial-understanding";

type AnswerEvaluated = MessageEnvelope<"AnswerEvaluated", AnswerEvaluatedPayload>;
```

#### `ConceptMastered`

Fired when the student reaches the mastery threshold for a concept.

```typescript
interface ConceptMasteredPayload {
  conceptId: string;
  conceptName: string;
  /** Final mastery level */
  masteryLevel: number;
  /** Number of attempts to reach mastery */
  attemptsToMastery: number;
  /** XP bonus for mastering the concept */
  xpBonus: number;
  /** Badge earned (null if none) */
  badgeEarned: { id: string; name: string; iconUrl: string } | null;
  /** Unlocked next concepts in the knowledge graph */
  unlockedConcepts: { conceptId: string; conceptName: string }[];
}

type ConceptMastered = MessageEnvelope<"ConceptMastered", ConceptMasteredPayload>;
```

#### `StagnationDetected`

The stagnation detector has identified a learning plateau.

```typescript
interface StagnationDetectedPayload {
  sessionId: string;
  conceptId: string;
  /** Which signal triggered the detection */
  signal: "accuracy-plateau" | "response-time-drift" | "error-repetition" | "annotation-sentiment" | "abandonment-pattern";
  /** Recommended action */
  recommendation: "switch-methodology" | "take-break" | "try-easier-concept" | "watch-video";
  /** Human-readable message for the student */
  message: string;
}

type StagnationDetected = MessageEnvelope<"StagnationDetected", StagnationDetectedPayload>;
```

#### `MethodologySwitched`

The system has changed the teaching methodology for the current concept.

```typescript
interface MethodologySwitchedPayload {
  sessionId: string;
  conceptId: string;
  previousMethodology: MethodologyType;
  newMethodology: MethodologyType;
  /** Human-readable explanation of why the switch occurred */
  reason: string;
}

type MethodologySwitched = MessageEnvelope<"MethodologySwitched", MethodologySwitchedPayload>;
```

#### `XPEarned`

Gamification event — student earned XP.

```typescript
interface XPEarnedPayload {
  /** XP earned in this event */
  amount: number;
  /** Source of the XP */
  source: "correct-answer" | "concept-mastered" | "streak-bonus" | "daily-login" | "annotation" | "speed-bonus";
  /** Total XP after this event */
  totalXP: number;
  /** Level progression */
  level: number;
  levelProgress: number; // 0.0 - 1.0 within current level
}

type XPEarned = MessageEnvelope<"XPEarned", XPEarnedPayload>;
```

#### `StreakUpdated`

Streak state changed.

```typescript
interface StreakUpdatedPayload {
  /** Current streak count in days */
  currentStreak: number;
  /** Longest streak ever */
  longestStreak: number;
  /** Whether the streak was extended, maintained, or broken */
  action: "extended" | "maintained" | "broken" | "restored";
  /** Streak freeze available (purchased or earned) */
  freezesRemaining: number;
  /** ISO 8601 UTC — deadline to maintain streak */
  expiresAt: string;
}

type StreakUpdated = MessageEnvelope<"StreakUpdated", StreakUpdatedPayload>;
```

#### `KnowledgeGraphUpdated`

The student's knowledge graph overlay has changed (mastery levels updated).

```typescript
interface KnowledgeGraphUpdatedPayload {
  /** Only the changed nodes — not the full graph */
  updatedNodes: {
    conceptId: string;
    conceptName: string;
    masteryLevel: number; // 0.0 - 1.0
    /** Predicted recall (half-life regression) */
    predictedRecall: number; // 0.0 - 1.0
    status: "not-started" | "in-progress" | "mastered" | "decaying";
  }[];
  /** Updated edges (prerequisites that are now unlocked) */
  updatedEdges: {
    fromConceptId: string;
    toConceptId: string;
    unlocked: boolean;
  }[];
}

type KnowledgeGraphUpdated = MessageEnvelope<"KnowledgeGraphUpdated", KnowledgeGraphUpdatedPayload>;
```

#### `CognitiveLoadWarning`

The system detects the student may be experiencing cognitive overload.

```typescript
interface CognitiveLoadWarningPayload {
  sessionId: string;
  /** Detected load level */
  level: "elevated" | "high" | "critical";
  /** Signals that contributed to the detection */
  signals: ("response-time-increase" | "error-rate-spike" | "hint-dependency" | "session-duration")[];
  /** Recommended action */
  recommendation: "take-break" | "reduce-difficulty" | "switch-to-review" | "end-session";
  /** Human-readable message */
  message: string;
  /** Suggested break duration in minutes (null if not a break recommendation) */
  suggestedBreakMinutes: number | null;
}

type CognitiveLoadWarning = MessageEnvelope<"CognitiveLoadWarning", CognitiveLoadWarningPayload>;
```

#### `SessionEnded`

Confirms session termination and provides summary.

```typescript
interface SessionEndedPayload {
  sessionId: string;
  /** Session duration in seconds */
  durationSeconds: number;
  /** Questions attempted */
  questionsAttempted: number;
  /** Questions answered correctly */
  correctAnswers: number;
  /** Concepts touched during session */
  conceptsTouched: { conceptId: string; conceptName: string; masteryDelta: number }[];
  /** Total XP earned during session */
  xpEarned: number;
  /** Whether the streak was maintained */
  streakMaintained: boolean;
  /** End reason */
  reason: "completed" | "tired" | "out-of-time" | "app-background" | "manual" | "cognitive-load" | "timeout";
}

type SessionEnded = MessageEnvelope<"SessionEnded", SessionEndedPayload>;
```

### 1.4 Error Envelope

Errors are delivered as a special event type. The `correlationId` maps to the command that caused the error.

```typescript
interface ErrorPayload {
  code: string;      // Machine-readable (e.g., "SESSION_NOT_FOUND")
  message: string;   // Human-readable (localized based on student locale)
  details: Record<string, unknown> | null;
}

type ErrorEvent = MessageEnvelope<"Error", ErrorPayload>;
```

**Error codes:**

| Code | Meaning |
|------|---------|
| `SESSION_NOT_FOUND` | Session ID does not exist or has expired |
| `SESSION_ALREADY_ACTIVE` | Student already has an active session |
| `QUESTION_EXPIRED` | Answer submitted for a question no longer active |
| `RATE_LIMITED` | Too many requests; back off |
| `UNAUTHORIZED` | JWT expired or invalid |
| `INTERNAL_ERROR` | Server-side failure; retry with exponential backoff |

---

## 2. gRPC Proto — LLM Anti-Corruption Layer

The .NET Proto.Actor cluster calls the Python FastAPI LLM service over gRPC. This is the **only** interface through which the cluster accesses LLM capabilities.

### 2.1 Protobuf Definition

```protobuf
syntax = "proto3";

package cena.llm.v1;

option csharp_namespace = "Cena.Llm.V1";
option go_package = "cena/llm/v1;llmv1";

import "google/protobuf/timestamp.proto";

// ─── Service ────────────────────────────────────────────────────

service LLMService {
  // Generate a Socratic-style question for a concept
  rpc GenerateSocraticQuestion(GenerateSocraticQuestionRequest)
      returns (GenerateSocraticQuestionResponse);

  // Evaluate a student's answer
  rpc EvaluateAnswer(EvaluateAnswerRequest)
      returns (EvaluateAnswerResponse);

  // Classify the type of error in an incorrect answer
  rpc ClassifyErrorType(ClassifyErrorTypeRequest)
      returns (ClassifyErrorTypeResponse);

  // Generate an explanation for a concept or answer
  rpc GenerateExplanation(GenerateExplanationRequest)
      returns (GenerateExplanationResponse);

  // Analyze a student annotation for sentiment and pedagogical signals
  rpc AnalyzeAnnotation(AnalyzeAnnotationRequest)
      returns (AnalyzeAnnotationResponse);

  // Generate an SVG diagram for a concept
  rpc GenerateDiagramSVG(GenerateDiagramSVGRequest)
      returns (GenerateDiagramSVGResponse);

  // Decide whether to switch the teaching methodology
  rpc DecideMethodologySwitch(DecideMethodologySwitchRequest)
      returns (DecideMethodologySwitchResponse);
}

// ─── Common Types ───────────────────────────────────────────────

// Which LLM tier the caller recommends (the ACL may override).
enum ModelTier {
  MODEL_TIER_UNSPECIFIED = 0;
  MODEL_TIER_FAST       = 1;  // Kimi K2.5 — classification, extraction
  MODEL_TIER_BALANCED   = 2;  // Claude Sonnet 4.6 — tutoring, explanation
  MODEL_TIER_REASONING  = 3;  // Claude Opus 4.6 — complex pedagogical reasoning
}

// Anonymized student context passed to the LLM. Never contains PII.
message StudentContext {
  // Opaque identifier (NOT a real student ID — anonymized by the caller)
  string anonymized_id           = 1;
  // Current mastery level for the target concept (0.0 - 1.0)
  double concept_mastery         = 2;
  // Number of prior attempts on this concept
  int32  prior_attempts          = 3;
  // Current active methodology
  string active_methodology      = 4;
  // Recent error types (last 5 attempts)
  repeated string recent_errors  = 5;
  // Preferred content language (BCP 47)
  string content_language        = 6;
  // Student's grade level (e.g., 10, 11, 12)
  int32  grade_level             = 7;
}

message ConceptContext {
  string concept_id    = 1;
  string concept_name  = 2;
  string subject       = 3;  // e.g., "math", "physics"
  string topic         = 4;  // e.g., "calculus", "mechanics"
  int32  difficulty    = 5;  // 1-10
  // Prerequisite concept names for context
  repeated string prerequisites = 6;
}

// ─── GenerateSocraticQuestion ───────────────────────────────────

message GenerateSocraticQuestionRequest {
  ModelTier         routing_hint    = 1;
  StudentContext    student         = 2;
  ConceptContext    concept         = 3;
  // Format constraint for the generated question
  string            desired_format  = 4;  // "free-text" | "multiple-choice" | "numeric" | "proof"
  // Previous questions in this session (to avoid repetition)
  repeated string   prior_questions = 5;
  // Hint level already given (0 = none, 1-3 = hint levels)
  int32             hints_given     = 6;
}

message GenerateSocraticQuestionResponse {
  string question_text       = 1;  // May contain LaTeX ($...$)
  string question_format     = 2;  // Echoes the chosen format
  // Multiple-choice options (empty if not multiple-choice)
  repeated MCOption options  = 3;
  string correct_answer      = 4;  // For server-side validation
  string svg_diagram         = 5;  // Optional inline SVG
  string model_used          = 6;  // Actual model that served the request
  int32  prompt_tokens       = 7;
  int32  completion_tokens   = 8;
}

message MCOption {
  string id   = 1;
  string text = 2;
}

// ─── EvaluateAnswer ─────────────────────────────────────────────

message EvaluateAnswerRequest {
  ModelTier       routing_hint   = 1;
  StudentContext  student        = 2;
  ConceptContext  concept        = 3;
  string          question_text  = 4;
  string          correct_answer = 5;
  string          student_answer = 6;
  // Elapsed time in ms (helps evaluate if answer was rushed)
  int32           response_time_ms = 7;
}

message EvaluateAnswerResponse {
  bool    correct            = 1;
  // Partial credit score (0.0 - 1.0)
  double  score              = 2;
  // Brief explanation of the evaluation
  string  explanation        = 3;
  // Classified error type (empty if correct)
  string  error_type         = 4;
  // Specific misconception identified (empty if none)
  string  misconception      = 5;
  // Suggested next pedagogical action
  string  suggested_action   = 6;  // "next-question" | "retry" | "hint" | "switch-methodology"
  string  model_used         = 7;
  int32   prompt_tokens      = 8;
  int32   completion_tokens  = 9;
}

// ─── ClassifyErrorType ──────────────────────────────────────────

message ClassifyErrorTypeRequest {
  ModelTier       routing_hint   = 1;
  ConceptContext  concept        = 2;
  string          question_text  = 3;
  string          correct_answer = 4;
  string          student_answer = 5;
}

message ClassifyErrorTypeResponse {
  // Primary error category
  string error_type        = 1;
  // Confidence score (0.0 - 1.0)
  double confidence        = 2;
  // Secondary error categories (if compound error)
  repeated string secondary_types = 3;
  // MCM graph lookup key — maps to recommended methodology
  string mcm_error_key     = 4;
  string model_used        = 5;
  int32  prompt_tokens     = 6;
  int32  completion_tokens = 7;
}

// ─── GenerateExplanation ────────────────────────────────────────

message GenerateExplanationRequest {
  ModelTier       routing_hint       = 1;
  StudentContext  student            = 2;
  ConceptContext  concept            = 3;
  // What to explain
  string          target             = 4;  // "concept" | "answer" | "error" | "hint"
  // The question/answer context (if explaining an answer or error)
  string          question_text      = 5;
  string          student_answer     = 6;
  string          correct_answer     = 7;
  // Desired explanation style
  string          style              = 8;  // "concise" | "detailed" | "visual" | "step-by-step"
  // Hint level (1 = gentle nudge, 2 = partial, 3 = full walkthrough)
  int32           hint_level         = 9;
}

message GenerateExplanationResponse {
  string explanation         = 1;  // Markdown with LaTeX support
  string svg_diagram         = 2;  // Optional diagram (inline SVG)
  // Related concepts the student should review
  repeated string related_concepts = 3;
  string model_used          = 4;
  int32  prompt_tokens       = 5;
  int32  completion_tokens   = 6;
}

// ─── AnalyzeAnnotation ──────────────────────────────────────────

message AnalyzeAnnotationRequest {
  ModelTier       routing_hint  = 1;
  StudentContext  student       = 2;
  ConceptContext  concept       = 3;
  string          annotation_text = 4;
  string          annotation_kind = 5;  // "note" | "question" | "confusion" | "insight"
}

message AnalyzeAnnotationResponse {
  // Sentiment score (-1.0 = frustrated, 0 = neutral, 1.0 = positive)
  double  sentiment           = 1;
  // Whether this annotation signals confusion or stagnation
  bool    signals_confusion   = 2;
  // Extracted question (if the annotation contains an implicit question)
  string  extracted_question  = 3;
  // Suggested pedagogical response
  string  suggested_response  = 4;
  string  model_used          = 5;
  int32   prompt_tokens       = 6;
  int32   completion_tokens   = 7;
}

// ─── GenerateDiagramSVG ─────────────────────────────────────────

message GenerateDiagramSVGRequest {
  ModelTier       routing_hint   = 1;
  ConceptContext  concept        = 2;
  // Type of diagram to generate
  string          diagram_type   = 3;  // "graph" | "function-plot" | "geometry" | "circuit" | "flowchart"
  // Specific parameters for the diagram (JSON-encoded)
  string          parameters_json = 4;
  // Target dimensions in pixels
  int32           width          = 5;
  int32           height         = 6;
  // RTL text layout
  bool            rtl            = 7;
}

message GenerateDiagramSVGResponse {
  string svg_content        = 1;
  string alt_text           = 2;  // Accessibility description
  string model_used         = 3;
  int32  prompt_tokens      = 4;
  int32  completion_tokens  = 5;
}

// ─── DecideMethodologySwitch ────────────────────────────────────

message DecideMethodologySwitchRequest {
  ModelTier       routing_hint          = 1;
  StudentContext  student               = 2;
  ConceptContext  concept               = 3;
  // Current methodology being used
  string          current_methodology   = 4;
  // Methodology effectiveness history for this student+concept
  repeated MethodologyRecord history    = 5;
  // Stagnation signals from the detector
  repeated string stagnation_signals    = 6;
  // MCM graph recommendations (from the domain graph)
  repeated string mcm_recommendations   = 7;
}

message MethodologyRecord {
  string methodology       = 1;
  int32  attempts          = 2;
  double accuracy_rate     = 3;
  double avg_response_ms   = 4;
  google.protobuf.Timestamp last_used = 5;
}

message DecideMethodologySwitchResponse {
  // Whether a switch is recommended
  bool   should_switch         = 1;
  // Recommended new methodology (empty if should_switch = false)
  string recommended_methodology = 2;
  // Confidence in the recommendation (0.0 - 1.0)
  double confidence            = 3;
  // Human-readable reasoning (for logging and explainability)
  string reasoning             = 4;
  // Fallback methodology if the recommended one also stalls
  string fallback_methodology  = 5;
  string model_used            = 6;
  int32  prompt_tokens         = 7;
  int32  completion_tokens     = 8;
}
```

### 2.2 Privacy Invariant

The LLM ACL **MUST** enforce: when `routing_hint = MODEL_TIER_FAST` (Kimi K2.5), the `StudentContext.anonymized_id` field is the **only** student identifier. No names, emails, or real IDs may appear in any field sent to Kimi. The ACL validates this before forwarding the request.

### 2.3 Cost Tracking

Every response includes `model_used`, `prompt_tokens`, and `completion_tokens`. The caller publishes these to NATS for the Analytics context to aggregate per-student and per-model costs.

---

## 3. GraphQL Schema — Read Side

Served by the Analytics context. Backed by Marten CQRS read model projections. Used by teacher dashboards, parent views, and the student knowledge graph visualization.

### 3.1 Schema Definition Language (SDL)

```graphql
# ─── Scalars ─────────────────────────────────────────────────────

scalar DateTime    # ISO 8601 UTC
scalar UUID        # UUIDv7

# ─── Core Types ──────────────────────────────────────────────────

type Student {
  id: UUID!
  displayName: String!
  gradeLevel: Int!
  schoolId: UUID
  subjects: [Subject!]!
  currentStreak: Int!
  longestStreak: Int!
  totalXP: Int!
  level: Int!
  levelProgress: Float!
  engagementStats: EngagementStats!
  createdAt: DateTime!
  lastActiveAt: DateTime!
}

type Subject {
  id: UUID!
  name: String!
  displayName: String!            # Localized (e.g., Hebrew)
  conceptCount: Int!
  masteredConceptCount: Int!
  overallMastery: Float!          # 0.0 - 1.0 aggregate
}

type Concept {
  id: UUID!
  name: String!
  displayName: String!            # Localized
  subject: Subject!
  topic: String!
  difficulty: Int!                # 1-10
  prerequisites: [Concept!]!
  dependents: [Concept!]!         # Concepts that require this one
}

type MasteryEdge {
  concept: Concept!
  masteryLevel: Float!            # 0.0 - 1.0
  predictedRecall: Float!         # 0.0 - 1.0 (HLR model)
  status: MasteryStatus!
  attemptsCount: Int!
  lastAttemptAt: DateTime
  lastMasteredAt: DateTime
  activeMethodology: String
}

enum MasteryStatus {
  NOT_STARTED
  IN_PROGRESS
  MASTERED
  DECAYING
}

type KnowledgeGraph {
  studentId: UUID!
  subject: Subject!
  nodes: [MasteryEdge!]!
  edges: [GraphEdge!]!
  overallMastery: Float!
  readyToLearn: [Concept!]!       # Concepts with met prerequisites
}

type GraphEdge {
  fromConceptId: UUID!
  toConceptId: UUID!
  unlocked: Boolean!
}

type LearningSession {
  id: UUID!
  studentId: UUID!
  subject: Subject!
  startedAt: DateTime!
  endedAt: DateTime
  durationSeconds: Int
  questionsAttempted: Int!
  correctAnswers: Int!
  accuracy: Float!
  xpEarned: Int!
  conceptsTouched: [SessionConceptSummary!]!
  endReason: String
  status: SessionStatus!
}

enum SessionStatus {
  ACTIVE
  COMPLETED
  ABANDONED
  TIMED_OUT
}

type SessionConceptSummary {
  concept: Concept!
  masteryBefore: Float!
  masteryAfter: Float!
  questionsAttempted: Int!
  correctAnswers: Int!
}

type EngagementStats {
  totalSessions: Int!
  totalTimeMinutes: Int!
  averageSessionMinutes: Float!
  totalQuestionsAttempted: Int!
  overallAccuracy: Float!
  conceptsMastered: Int!
  currentStreak: Int!
  longestStreak: Int!
  lastSevenDays: DailyStats!
  lastThirtyDays: DailyStats!
}

type DailyStats {
  sessionsCount: Int!
  timeMinutes: Int!
  questionsAttempted: Int!
  accuracy: Float!
  xpEarned: Int!
  conceptsMastered: Int!
}

type ClassRoom {
  id: UUID!
  name: String!
  schoolId: UUID!
  teacherId: UUID!
  subject: Subject!
  students: [Student!]!
  studentCount: Int!
  averageMastery: Float!
  averageStreak: Float!
  topKnowledgeGaps: [ConceptGap!]!
}

type ConceptGap {
  concept: Concept!
  """ Percentage of students below mastery threshold """
  studentsBelowThreshold: Float!
  averageMastery: Float!
}

type LeaderboardEntry {
  rank: Int!
  student: Student!
  xp: Int!
  streak: Int!
  conceptsMastered: Int!
}

type StreakStatus {
  currentStreak: Int!
  longestStreak: Int!
  freezesRemaining: Int!
  expiresAt: DateTime!
  streakHistory: [StreakDay!]!
}

type StreakDay {
  date: DateTime!
  active: Boolean!
  frozeUsed: Boolean!
}

# ─── Queries ─────────────────────────────────────────────────────

type Query {
  """
  Get a student's full profile. Teachers can query their students;
  parents can query their children; students can query themselves.
  """
  studentProfile(studentId: UUID!): Student

  """
  Get the knowledge graph overlay for a student in a given subject.
  """
  knowledgeGraph(studentId: UUID!, subjectId: UUID!): KnowledgeGraph

  """
  Get class-level overview for a teacher's classroom.
  """
  classOverview(classRoomId: UUID!): ClassRoom

  """
  Get a student's progress in a specific subject.
  """
  subjectProgress(studentId: UUID!, subjectId: UUID!): Subject

  """
  Get recent learning sessions for a student.
  """
  recentSessions(
    studentId: UUID!
    limit: Int = 10
    offset: Int = 0
  ): [LearningSession!]!

  """
  Get current streak status for a student.
  """
  streakStatus(studentId: UUID!): StreakStatus

  """
  Get the leaderboard for a classroom or global.
  """
  leaderboard(
    scope: LeaderboardScope!
    classRoomId: UUID
    subjectId: UUID
    limit: Int = 20
  ): [LeaderboardEntry!]!
}

enum LeaderboardScope {
  CLASS
  SCHOOL
  GLOBAL
}

# ─── Subscriptions (real-time via GraphQL over WebSocket) ────────

type Subscription {
  """
  Subscribe to knowledge graph updates for a student.
  Fires when mastery levels change during an active session.
  """
  onKnowledgeGraphUpdate(studentId: UUID!, subjectId: UUID!): KnowledgeGraphUpdate!

  """
  Subscribe to session progress events for a student.
  Used by parent/teacher views to watch a session in real-time.
  """
  onSessionProgress(studentId: UUID!): SessionProgressEvent!
}

type KnowledgeGraphUpdate {
  studentId: UUID!
  subjectId: UUID!
  updatedNodes: [MasteryEdge!]!
  updatedEdges: [GraphEdge!]!
  timestamp: DateTime!
}

type SessionProgressEvent {
  sessionId: UUID!
  studentId: UUID!
  eventType: SessionEventType!
  conceptId: UUID
  conceptName: String
  questionIndex: Int
  correct: Boolean
  masteryDelta: Float
  xpEarned: Int
  timestamp: DateTime!
}

enum SessionEventType {
  SESSION_STARTED
  QUESTION_ANSWERED
  CONCEPT_MASTERED
  METHODOLOGY_SWITCHED
  STAGNATION_DETECTED
  SESSION_ENDED
}
```

### 3.2 Authorization Model

| Role | Access |
|------|--------|
| **Student** | Own profile, own knowledge graph, own sessions, global leaderboard |
| **Parent** | Linked children's profiles, knowledge graphs, sessions (read-only) |
| **Teacher** | All students in their classrooms, class overview, class leaderboard |
| **Admin** | All data |

Authorization is enforced at the resolver layer via JWT claims. The `role` and `scope` claims determine data access.

---

## 4. REST API Surface

For non-realtime operations. Base URL: `https://api.cena.co.il/v1`

All requests require `Authorization: Bearer <jwt>` except where noted.
All responses use `Content-Type: application/json; charset=utf-8`.
Error responses follow [RFC 7807](https://tools.ietf.org/html/rfc7807) Problem Details.

### 4.1 Authentication

#### `POST /auth/register`

Create a new student account.

**Request:**

```json
{
  "email": "student@example.com",
  "password": "min12chars!",
  "displayName": "שמעון",
  "gradeLevel": 11,
  "locale": "he-IL"
}
```

**Response `201 Created`:**

```json
{
  "userId": "019489a0-...",
  "accessToken": "eyJhbG...",
  "refreshToken": "dGhpcyBpcyBh...",
  "expiresAt": "2026-03-26T15:30:00Z"
}
```

**Errors:**

| Status | Code | Meaning |
|--------|------|---------|
| `409` | `EMAIL_ALREADY_EXISTS` | Email already registered |
| `422` | `VALIDATION_ERROR` | Invalid email, password too short, etc. |

---

#### `POST /auth/login`

Authenticate with email and password.

**Request:**

```json
{
  "email": "student@example.com",
  "password": "min12chars!"
}
```

**Response `200 OK`:**

```json
{
  "userId": "019489a0-...",
  "accessToken": "eyJhbG...",
  "refreshToken": "dGhpcyBpcyBh...",
  "expiresAt": "2026-03-26T15:30:00Z",
  "role": "student"
}
```

**Errors:**

| Status | Code | Meaning |
|--------|------|---------|
| `401` | `INVALID_CREDENTIALS` | Wrong email or password |
| `423` | `ACCOUNT_LOCKED` | Too many failed attempts |

---

#### `POST /auth/refresh`

Refresh an expired access token.

**Request:**

```json
{
  "refreshToken": "dGhpcyBpcyBh..."
}
```

**Response `200 OK`:**

```json
{
  "accessToken": "eyJhbG...",
  "refreshToken": "bmV3IHJlZnJlc2g...",
  "expiresAt": "2026-03-26T16:30:00Z"
}
```

**Errors:**

| Status | Code | Meaning |
|--------|------|---------|
| `401` | `TOKEN_EXPIRED` | Refresh token expired — re-login required |
| `401` | `TOKEN_REVOKED` | Refresh token has been revoked |

---

#### `POST /auth/google`

Authenticate via Google OAuth2 (one-tap sign-in).

**Request:**

```json
{
  "idToken": "eyJhbGciOiJSUzI1NiIs...",
  "gradeLevel": 11,
  "locale": "he-IL"
}
```

`gradeLevel` and `locale` are only required on first login (account creation). Subsequent logins ignore them.

**Response `200 OK`:** Same shape as `/auth/login`.

---

### 4.2 Onboarding

#### `POST /onboarding/diagnostic`

Submit the results of the onboarding diagnostic assessment. The diagnostic uses Knowledge Space Theory to estimate the student's initial mastery overlay.

**Request:**

```json
{
  "subjectId": "019489a0-...",
  "responses": [
    {
      "questionId": "q-001",
      "answer": "x = 5",
      "responseTimeMs": 12400,
      "skipped": false
    },
    {
      "questionId": "q-002",
      "answer": null,
      "responseTimeMs": 0,
      "skipped": true
    }
  ]
}
```

**Response `200 OK`:**

```json
{
  "studentId": "019489a0-...",
  "subjectId": "019489a0-...",
  "estimatedMasteryNodes": [
    {
      "conceptId": "c-derivatives",
      "estimatedMastery": 0.72,
      "confidence": 0.85
    },
    {
      "conceptId": "c-integrals",
      "estimatedMastery": 0.15,
      "confidence": 0.60
    }
  ],
  "recommendedStartConcept": "c-chain-rule",
  "questionsAsked": 12,
  "questionsAnswered": 10
}
```

---

#### `GET /onboarding/subjects`

Get available subjects for onboarding.

**Response `200 OK`:**

```json
{
  "subjects": [
    {
      "id": "019489a0-...",
      "name": "math",
      "displayName": "מתמטיקה",
      "description": "מתמטיקה לבגרות — 5 יחידות",
      "iconUrl": "https://cdn.cena.co.il/icons/math.svg",
      "conceptCount": 247,
      "availableForDiagnostic": true
    }
  ]
}
```

---

### 4.3 Settings

#### `GET /settings/preferences`

Get the student's learning preferences.

**Response `200 OK`:**

```json
{
  "explanationStyle": "step-by-step",
  "difficultyBias": 0,
  "contentLanguage": "he",
  "dailyGoalMinutes": 30,
  "dailyGoalQuestions": 20,
  "soundEffects": true,
  "hapticFeedback": true,
  "theme": "system"
}
```

#### `PUT /settings/preferences`

Update learning preferences. Partial updates supported (send only changed fields).

**Request:** Same shape as `GET` response (all fields optional).

**Response `200 OK`:** Updated full preferences object.

---

#### `GET /settings/notifications`

Get notification preferences.

**Response `200 OK`:**

```json
{
  "pushEnabled": true,
  "whatsappEnabled": true,
  "telegramEnabled": false,
  "emailDigest": "weekly",
  "streakReminders": true,
  "reviewReminders": true,
  "quietHoursStart": "22:00",
  "quietHoursEnd": "07:00",
  "timezone": "Asia/Jerusalem"
}
```

#### `PUT /settings/notifications`

Update notification preferences. Partial updates supported.

**Request:** Same shape as `GET` response (all fields optional).

**Response `200 OK`:** Updated full notification preferences.

---

#### `GET /settings/privacy`

Get privacy settings, including camera consent.

**Response `200 OK`:**

```json
{
  "cameraConsentGiven": false,
  "cameraConsentTimestamp": null,
  "analyticsOptIn": true,
  "leaderboardVisible": true,
  "profileVisibleToClassmates": true,
  "dataExportRequested": false,
  "dataExportReadyUrl": null
}
```

#### `PUT /settings/privacy`

Update privacy settings. Changing `cameraConsentGiven` to `true` records an auditable consent timestamp.

**Request:**

```json
{
  "cameraConsentGiven": true,
  "leaderboardVisible": false
}
```

**Response `200 OK`:** Updated full privacy settings with `cameraConsentTimestamp` populated.

---

### 4.4 Content

#### `GET /concepts/{id}`

Get concept metadata.

**Response `200 OK`:**

```json
{
  "id": "c-derivatives",
  "name": "derivatives",
  "displayName": "נגזרות",
  "subject": {
    "id": "019489a0-...",
    "name": "math",
    "displayName": "מתמטיקה"
  },
  "topic": "calculus",
  "difficulty": 6,
  "prerequisites": [
    { "id": "c-limits", "displayName": "גבולות" }
  ],
  "dependents": [
    { "id": "c-chain-rule", "displayName": "כלל השרשרת" }
  ],
  "description": "הנגזרת של פונקציה מתארת את קצב השינוי שלה...",
  "estimatedMinutes": 45
}
```

---

#### `GET /concepts/{id}/diagram`

Get a pre-rendered SVG diagram for a concept.

**Query Parameters:**

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `width` | int | 400 | Diagram width in pixels |
| `height` | int | 300 | Diagram height in pixels |
| `rtl` | bool | true | Right-to-left text layout |

**Response `200 OK`:**

```
Content-Type: image/svg+xml

<svg xmlns="http://www.w3.org/2000/svg" ...>...</svg>
```

---

#### `GET /concepts/{id}/video`

Get the pre-generated explainer video URL for a concept.

**Response `200 OK`:**

```json
{
  "conceptId": "c-derivatives",
  "videoUrl": "https://cdn.cena.co.il/videos/c-derivatives/v1.mp4",
  "thumbnailUrl": "https://cdn.cena.co.il/videos/c-derivatives/thumb.jpg",
  "durationSeconds": 180,
  "language": "he",
  "generatedAt": "2026-03-20T10:00:00Z"
}
```

**Errors:**

| Status | Code | Meaning |
|--------|------|---------|
| `404` | `VIDEO_NOT_FOUND` | No video generated for this concept yet |

---

### 4.5 Common Error Response Format

All errors follow RFC 7807:

```json
{
  "type": "https://api.cena.co.il/errors/VALIDATION_ERROR",
  "title": "Validation Error",
  "status": 422,
  "detail": "Field 'email' must be a valid email address.",
  "instance": "/auth/register",
  "traceId": "019489a0-..."
}
```

---

## 5. NATS Subject Hierarchy

### 5.1 Subject Naming Convention

All NATS JetStream subjects follow the pattern:

```
cena.{bounded-context}.{entity-id}.{channel}
```

Where:
- `bounded-context` — the publishing bounded context
- `entity-id` — the entity the event pertains to (student ID, session ID)
- `channel` — the event category (`events`, `triggers`, `commands`)

### 5.2 Subject Definitions

#### Learner Domain Events

```
cena.learner.{studentId}.events
```

Published by: **Learner Context** (StudentActor)

| Event Type | Description |
|------------|-------------|
| `ConceptAttempted` | Student attempted a concept exercise |
| `ConceptMastered` | Mastery threshold reached for a concept |
| `MasteryDecayed` | Predicted recall dropped below 0.85 threshold |
| `MethodologySwitched` | Teaching methodology changed for a concept |
| `StagnationDetected` | Learning plateau identified by detector |
| `AnnotationAdded` | Student added a note or reflection |
| `ProfileUpdated` | Student preferences or settings changed |
| `DiagnosticCompleted` | Onboarding diagnostic assessment finished |

**Envelope:**

```json
{
  "eventId": "019489a0-...",
  "eventType": "ConceptMastered",
  "studentId": "019489a0-...",
  "timestamp": "2026-03-26T14:30:00Z",
  "sequenceNumber": 1042,
  "payload": { }
}
```

---

#### Pedagogy Session Events

```
cena.pedagogy.{sessionId}.events
```

Published by: **Pedagogy Context** (LearningSessionActor)

| Event Type | Description |
|------------|-------------|
| `SessionStarted` | New learning session began |
| `SessionCompleted` | Session ended normally |
| `SessionAbandoned` | Session ended due to timeout or disconnect |
| `ExerciseAttempted` | Student submitted an answer |
| `ExerciseEvaluated` | Answer was evaluated by the LLM ACL |
| `HintRequested` | Student requested a hint |
| `QuestionSkipped` | Student skipped a question |
| `MethodologySwitchTriggered` | Methodology switch initiated within session |

---

#### Engagement Events

```
cena.engagement.{studentId}.events
```

Published by: **Engagement Context**

| Event Type | Description |
|------------|-------------|
| `XPEarned` | Student earned experience points |
| `LevelUp` | Student reached a new level |
| `BadgeEarned` | Student earned a badge/achievement |
| `StreakExtended` | Daily streak extended by one day |
| `StreakBroken` | Streak was broken (missed a day) |
| `StreakRestored` | Streak restored via freeze |
| `LeaderboardPositionChanged` | Student's rank changed on leaderboard |

---

#### Outreach Trigger Events

```
cena.outreach.{studentId}.triggers
```

Published by: **Learner Context**, **Engagement Context**
Consumed by: **Outreach Context**

| Trigger Type | Source | Description |
|-------------|--------|-------------|
| `StreakExpiring` | Engagement | Streak expires within configured window |
| `ReviewDue` | Learner (HLR) | Concept recall predicted below threshold |
| `StagnationDetected` | Learner | Learning plateau — proactive intervention needed |
| `SessionAbandoned` | Pedagogy | Student left mid-session |
| `CognitiveLoadCooldownComplete` | Learner | Break period ended, student may be ready |
| `InactivityThreshold` | Engagement | Student hasn't opened the app in N days |
| `MilestoneApproaching` | Engagement | Student is close to a badge or level-up |

**Trigger envelope:**

```json
{
  "triggerId": "019489a0-...",
  "triggerType": "StreakExpiring",
  "studentId": "019489a0-...",
  "timestamp": "2026-03-26T14:30:00Z",
  "priority": "high",
  "channelPreference": ["whatsapp", "push"],
  "payload": {
    "currentStreak": 7,
    "expiresAt": "2026-03-26T23:59:59Z"
  }
}
```

### 5.3 JetStream Stream Configuration

| Stream Name | Subjects | Retention | Max Age | Replicas |
|-------------|----------|-----------|---------|----------|
| `LEARNER_EVENTS` | `cena.learner.>` | Limits (disk) | 90 days | 1 (managed NATS) |
| `PEDAGOGY_EVENTS` | `cena.pedagogy.>` | Limits (disk) | 30 days | 1 |
| `ENGAGEMENT_EVENTS` | `cena.engagement.>` | Limits (disk) | 90 days | 1 |
| `OUTREACH_TRIGGERS` | `cena.outreach.>` | WorkQueue | 7 days | 1 |

### 5.4 Consumer Groups

Each bounded context that subscribes to events has a **durable consumer group** to ensure at-least-once delivery and allow horizontal scaling of consumers.

| Consumer Group | Stream | Filter Subject | Deliver Policy | Ack Wait |
|---------------|--------|---------------|----------------|----------|
| `engagement-learner-consumer` | `LEARNER_EVENTS` | `cena.learner.*.events` | All | 30s |
| `engagement-pedagogy-consumer` | `PEDAGOGY_EVENTS` | `cena.pedagogy.*.events` | All | 30s |
| `outreach-trigger-consumer` | `OUTREACH_TRIGGERS` | `cena.outreach.*.triggers` | All | 60s |
| `outreach-stagnation-consumer` | `LEARNER_EVENTS` | `cena.learner.*.events` | All (filtered by eventType in handler) | 60s |
| `analytics-all-consumer` | `LEARNER_EVENTS` | `cena.learner.>` | All | 120s |
| `analytics-pedagogy-consumer` | `PEDAGOGY_EVENTS` | `cena.pedagogy.>` | All | 120s |
| `analytics-engagement-consumer` | `ENGAGEMENT_EVENTS` | `cena.engagement.>` | All | 120s |
| `school-learner-consumer` | `LEARNER_EVENTS` | `cena.learner.*.events` | All | 30s |

### 5.5 Ordering Guarantee

Events within a single student or session are **ordered** (same NATS subject = same stream sequence). Cross-student ordering is **not guaranteed** and not required — each student is an independent aggregate.

---

## Appendix A: Type Reference

### Methodology Types

| Value | Description |
|-------|-------------|
| `socratic-dialogue` | Question-led discovery (Claude Sonnet generates probing questions) |
| `worked-examples` | Step-by-step solved examples with explanation |
| `scaffolded-practice` | Gradually increasing difficulty with support |
| `visual-spatial` | Diagram-heavy, geometry-oriented explanations |
| `analogy-based` | Explanations via real-world analogies |
| `error-analysis` | Learn by analyzing common mistakes |
| `spaced-retrieval` | Review-focused, spaced repetition retrieval practice |

### Error Types

| Value | Description |
|-------|-------------|
| `conceptual-misunderstanding` | Fundamental concept not grasped |
| `computational-error` | Arithmetic or algebraic mistake |
| `notation-error` | Correct idea, wrong mathematical notation |
| `incomplete-reasoning` | Right direction but missing steps |
| `off-topic` | Answer doesn't address the question |
| `partial-understanding` | Some aspects correct, others missing |
