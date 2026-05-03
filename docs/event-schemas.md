# Domain Event Schemas & Versioning Strategy

> **Status:** Specification
> **Last updated:** 2026-03-26
> **Applies to:** Learner, Pedagogy, Engagement bounded contexts (event-sourced aggregates)

---

## 1. Versioning Policy

### Rules
1. Every event type has an explicit version suffix: `ConceptAttempted_V1`
2. Event payloads are **append-only** — new fields may be added as optional; existing fields are never removed or renamed
3. If a field must be removed or its type changed, a new version is created (`ConceptAttempted_V2`) and an **upcaster** transforms V1 → V2 during replay
4. Upcasters are registered in the Marten event store configuration at startup
5. All events carry a `schemaVersion` field in their metadata envelope
6. Serialization format: **Protobuf** (compact, schema-enforced, backward-compatible by design)

### Event Envelope (wraps every domain event)

```protobuf
message EventEnvelope {
  string event_id = 1;           // UUID, globally unique
  string event_type = 2;         // e.g., "ConceptAttempted_V1"
  int32 schema_version = 3;      // monotonically increasing per event type
  string aggregate_id = 4;       // StudentProfile UUID
  string aggregate_type = 5;     // "StudentProfile" | "LearningSession"
  int64 sequence_number = 6;     // per-aggregate monotonic sequence
  google.protobuf.Timestamp timestamp = 7;
  string correlation_id = 8;     // traces a user action across events
  string causation_id = 9;       // the event/command that caused this event
  map<string, string> metadata = 10; // experiment cohort, device type, app version
  bytes payload = 11;            // the actual event, serialized per its schema
}
```

---

## 2. Learner Context Events (StudentProfile Aggregate)

### ConceptAttempted_V1

Emitted when a student submits an answer to any exercise.

```protobuf
message ConceptAttempted_V1 {
  string student_id = 1;
  string concept_id = 2;
  string session_id = 3;
  bool is_correct = 4;
  int32 response_time_ms = 5;
  string question_id = 6;
  string question_type = 7;       // "multiple_choice" | "numeric" | "expression" | "true_false_justification" | "ordering" | "fill_blank" | "diagram_labeling" | "free_text" (see docs/assessment-specification.md Section 1)
  string methodology_active = 8;  // "socratic" | "spaced_repetition" | "feynman" | "project_based" | "blooms_progression" | "worked_example" | "analogy" | "retrieval_practice"
  string error_type = 9;          // "procedural" | "conceptual" | "motivational" | "none" (classified by Kimi)
  double prior_mastery = 10;      // BKT P(known) before this attempt
  double posterior_mastery = 11;   // BKT P(known) after this attempt
  int32 hint_count_used = 12;     // hints requested before answering
  bool was_skipped = 13;
  string answer_hash = 14;        // hash of student's answer (not plaintext, for privacy)
  // Behavioral signals
  int32 backspace_count = 15;     // deletion/uncertainty signal
  int32 answer_change_count = 16; // times answer was changed before submit
  bool was_offline = 17;          // true if submitted during offline session
}
```

### ConceptMastered_V1

Emitted when a concept's mastery crosses the threshold (P(known) ≥ 0.85).

```protobuf
message ConceptMastered_V1 {
  string student_id = 1;
  string concept_id = 2;
  string session_id = 3;
  double mastery_level = 4;        // the P(known) that triggered mastery
  int32 total_attempts = 5;        // total attempts across all sessions to reach mastery
  int32 total_sessions = 6;        // sessions that included this concept
  string methodology_at_mastery = 7; // which method was active when mastered
  double half_life_hours = 8;      // initial spaced repetition half-life for this concept
  google.protobuf.Timestamp next_review_due = 9; // first scheduled review
}
```

### MasteryDecayed_V1

Emitted by spaced repetition timer when predicted recall drops below threshold.

```protobuf
message MasteryDecayed_V1 {
  string student_id = 1;
  string concept_id = 2;
  double predicted_recall = 3;     // p(t) = 2^(-Δ/h) at time of check
  double half_life_hours = 4;      // current half-life
  double hours_since_review = 5;   // Δ in the HLR formula
  double new_mastery_estimate = 6; // updated P(known)
  google.protobuf.Timestamp recommended_review_by = 7;
}
```

### StagnationDetected_V1

Emitted when composite stagnation score exceeds threshold for 3 consecutive sessions.

```protobuf
message StagnationDetected_V1 {
  string student_id = 1;
  repeated string concept_cluster = 2; // the concept IDs where stagnation is occurring
  double composite_score = 3;          // 0-1 normalized stagnation score
  double accuracy_plateau_score = 4;   // sub-signal: accuracy improvement rate
  double response_time_drift = 5;      // sub-signal: response time trend
  double session_abandonment_score = 6; // sub-signal: early session endings
  int32 error_repetition_count = 7;    // sub-signal: repeated error patterns
  double annotation_sentiment_score = 8; // sub-signal: frustration/confusion in notes
  string recommended_action = 9;       // "switch_methodology" | "reduce_difficulty" | "offer_break" | "review_prerequisites"
  int32 consecutive_sessions = 10;     // how many sessions the stagnation persisted
}
```

### MethodologySwitched_V1

Emitted when the system changes the active teaching methodology for a student.

```protobuf
message MethodologySwitched_V1 {
  string student_id = 1;
  string from_methodology = 2;
  string to_methodology = 3;
  string trigger = 4;              // "stagnation_detected" | "student_requested" | "mcm_recommendation" | "initial_assignment"
  string trigger_event_id = 5;     // the StagnationDetected event that caused this, if applicable
  repeated string affected_concepts = 6; // concepts the new methodology will apply to
  double confidence = 7;           // MCM graph confidence in the recommendation (0-1)
}
```

### AnnotationAdded_V1

Emitted when a student adds a note/thought to a concept.

```protobuf
message AnnotationAdded_V1 {
  string student_id = 1;
  string concept_id = 2;
  string annotation_id = 3;       // UUID for this annotation
  string annotation_text = 4;     // the student's text (encrypted at rest)
  string sentiment = 5;           // "positive" | "neutral" | "confused" | "frustrated" (classified by Kimi)
  double sentiment_confidence = 6;
  bool links_to_other_concept = 7;
  string linked_concept_id = 8;   // if student explicitly connected to another concept
}
```

---

## 3. Pedagogy Context Events (LearningSession Aggregate)

### SessionStarted_V1

```protobuf
message SessionStarted_V1 {
  string session_id = 1;
  string student_id = 2;
  string subject = 3;             // "mathematics" | "physics" | "chemistry" | "biology" | "computer_science"
  string methodology_active = 4;
  string device_type = 5;         // "ios" | "android" | "web"
  string app_version = 6;
  bool is_offline = 7;
  string experiment_cohort = 8;   // A/B test cohort ID
  double cognitive_load_budget = 9; // estimated session duration in minutes based on student profile
}
```

### SessionEnded_V1

```protobuf
message SessionEnded_V1 {
  string session_id = 1;
  string student_id = 2;
  int32 duration_seconds = 3;
  int32 concepts_attempted = 4;
  int32 concepts_mastered = 5;
  int32 xp_earned = 6;
  string end_reason = 7;          // "completed" | "cognitive_load_limit" | "student_quit" | "app_backgrounded" | "error"
  double avg_accuracy = 8;
  double avg_response_time_ms = 9;
  int32 hints_used = 10;
  int32 questions_skipped = 11;
  bool had_methodology_switch = 12;
  bool was_offline = 13;
}
```

### QuestionPresented_V1

```protobuf
message QuestionPresented_V1 {
  string session_id = 1;
  string student_id = 2;
  string question_id = 3;
  string concept_id = 4;
  string question_type = 5;       // "multiple_choice" | "numeric" | "expression" | "true_false_justification" | "ordering" | "fill_blank" | "diagram_labeling" | "free_text" (see docs/assessment-specification.md Section 1)
  string difficulty_level = 6;    // "recall" | "comprehension" | "application" | "analysis" (Bloom's)
  string selection_reason = 7;    // "gap_fill" | "spaced_review" | "prerequisite_check" | "mastery_probe"
  string methodology_active = 8;
}
```

---

## 4. Engagement Context Events

### XPEarned_V1

```protobuf
message XPEarned_V1 {
  string student_id = 1;
  int32 xp_delta = 2;
  string reason = 3;              // "concept_mastered" | "session_completed" | "streak_milestone" | "quiz_perfect_score" | "daily_goal_met"
  int32 total_xp = 4;            // running total after this event
  string source_event_id = 5;    // the ConceptMastered or SessionEnded that triggered this
}
```

### StreakUpdated_V1

```protobuf
message StreakUpdated_V1 {
  string student_id = 1;
  int32 previous_count = 2;
  int32 new_count = 3;
  string action = 4;             // "incremented" | "reset" | "frozen" | "restored"
  google.protobuf.Timestamp streak_deadline = 5; // when streak expires if no activity
}
```

### BadgeAwarded_V1

```protobuf
message BadgeAwarded_V1 {
  string student_id = 1;
  string badge_id = 2;
  string badge_name = 3;          // "mastered_integration" | "connected_50_concepts" | "30_day_streak" | "first_subject_complete"
  string badge_category = 4;      // "mastery" | "engagement" | "social" | "milestone"
  string trigger_event_id = 5;
}
```

---

## 5. Outreach Context Events

### OutreachTriggered_V1

```protobuf
message OutreachTriggered_V1 {
  string student_id = 1;
  string trigger_type = 2;        // "streak_expiring" | "review_due" | "stagnation_nudge" | "session_abandoned" | "cooldown_complete" | "weekly_summary"
  string channel = 3;             // "whatsapp" | "telegram" | "push" | "voice" | "email"
  string message_template_id = 4;
  int32 priority = 5;             // 1=critical (streak), 2=high (review), 3=normal (nudge), 4=low (summary)
  string idempotency_key = 6;     // prevents duplicate sends
  bool was_delivered = 7;
  bool was_suppressed = 8;        // true if notification budget was exhausted
  string suppression_reason = 9;  // "daily_budget_exceeded" | "quiet_hours" | "cooldown_period" | "student_opted_out"
}
```

### StreakExpiring_V1

Emitted by the `OutreachSchedulerActor` when a student's streak is about to expire.

```protobuf
message StreakExpiring_V1 {
  string student_id = 1;
  int32 current_streak = 2;
  google.protobuf.Timestamp expires_at = 3;   // when the streak will break
  string idempotency_key = 4;
}
```

### ReviewDue_V1

Emitted by the Half-Life Regression timer when a concept's predicted recall drops below the review threshold (0.85).

```protobuf
message ReviewDue_V1 {
  string student_id = 1;
  string concept_id = 2;
  double predicted_recall = 3;
  double half_life_hours = 4;
  google.protobuf.Timestamp recommended_review_by = 5;
  string idempotency_key = 6;
}
```

### CognitiveLoadCooldownComplete_V1

Emitted by the `OutreachSchedulerActor` when a student's cognitive load cooldown period has ended and they are ready for a new session.

```protobuf
message CognitiveLoadCooldownComplete_V1 {
  string student_id = 1;
  string previous_session_id = 2;
  int32 cooldown_duration_minutes = 3;
  string idempotency_key = 4;
}
```

**Note on SessionAbandoned:** Session abandonment is signaled by `SessionEnded_V1` with `end_reason = "student_quit"` or `"app_backgrounded"`. A separate `SessionAbandoned` event is not needed; the Outreach Context subscribes to `SessionEnded_V1` and filters by `end_reason`.

---

## 6. Upcasting Rules

### Policy
- Upcasters are pure functions: `V1 → V2` transformation with no side effects
- Registered in Marten's `StoreOptions.Events.Upcast<TEvent>()` at startup
- Tested with a dedicated "event replay" integration test suite that replays real production event streams through all upcasters

### Example: ConceptAttempted_V1 → ConceptAttempted_V2

If in the future we add a `difficulty_bloom_level` field (Bloom's taxonomy level of the question):

```csharp
// Upcaster: V1 → V2
public class ConceptAttemptedV1ToV2 : IEventUpcaster<ConceptAttempted_V1, ConceptAttempted_V2>
{
    public ConceptAttempted_V2 Upcast(ConceptAttempted_V1 old)
    {
        return new ConceptAttempted_V2
        {
            // Copy all V1 fields...
            StudentId = old.StudentId,
            ConceptId = old.ConceptId,
            // ... (all fields)

            // New field gets a sensible default
            DifficultyBloomLevel = "unknown" // V1 events didn't track this
        };
    }
}
```

### Upcasting Decision Tree

```
New optional field added?
  → NO upcaster needed. Protobuf handles missing optional fields as defaults.

Existing field type changed?
  → Create new version + upcaster. Example: response_time changed from int32 (ms) to double (seconds).

Field removed?
  → Create new version. Upcaster drops the field. Old events still deserialize (Protobuf ignores unknown fields).

Field renamed?
  → Create new version + upcaster that maps old → new name.

Semantic meaning changed?
  → Create new version + upcaster. Document the semantic change clearly.
```

### Snapshot Strategy
- Snapshot every 100 events per aggregate
- Snapshot includes the current schema version so replay starts from the right upcaster chain
- On schema migration: bulk re-snapshot all aggregates (background job, idempotent)

---

## 7. GDPR Crypto-Shredding

Events containing PII (AnnotationAdded_V1.annotation_text, answer content referenced by hash) are encrypted with a per-student key stored in a separate key management table.

On deletion request:
1. Delete the student's encryption key from the key store
2. All events remain in the event store but PII fields become undecryptable
3. Non-PII fields (mastery scores, timing data, methodology effectiveness) remain readable for aggregate analytics
4. Projections rebuilt: student-specific read models are purged, aggregate models retain anonymized data

---

## 8. Event Schema Registry

All event schemas are stored in the repository at `src/events/schemas/` as `.proto` files. A CI check validates:
1. No breaking changes to existing versions (protobuf compatibility check)
2. Every new version has a corresponding upcaster registered
3. Every upcaster has an integration test
4. The upcaster chain is complete (no gaps: V1→V2→V3, never V1→V3 without V2)
