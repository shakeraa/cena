# MOB-002: Domain Models (Freezed + JSON Serialization)

**Priority:** P0 — every feature depends on typed models
**Blocked by:** MOB-001 (project scaffold, freezed dependency)
**Estimated effort:** 2 days
**Contract:** `contracts/mobile/lib/core/models/domain_models.dart`, `contracts/mobile/lib/features/diagrams/diagram_models.dart`

---

## Context
All domain models are immutable (freezed), null-safe, and JSON-serializable. The contract defines 7 enums and 20+ freezed classes across two files. These models are the shared language between the Flutter client, WebSocket messages, offline event queue, and Riverpod state. Every field name, type, and default must match the contract exactly — the backend .NET SignalR hub expects these JSON shapes.

## Subtasks

### MOB-002.1: Enums & Core Domain Models
**Files:**
- `lib/core/models/domain_models.dart`

**Acceptance:**
- [ ] `BloomLevel` enum with 6 values: `remember(1)`, `understand(2)`, `apply(3)`, `analyze(4)`, `evaluate(5)`, `create(6)` — annotated with `@JsonEnum(valueField: 'level')`
- [ ] `Subject` enum with 5 values: `math`, `physics`, `chemistry`, `biology`, `cs` — each `@JsonValue` annotated
- [ ] `QuestionType` enum: `multipleChoice('mcq')`, `freeText('free_text')`, `numeric('numeric')`, `proof('proof')`, `diagram('diagram')`
- [ ] `Methodology` enum: `spacedRepetition('spaced_repetition')`, `interleaved('interleaved')`, `blocked('blocked')`, `adaptiveDifficulty('adaptive_difficulty')`, `socratic('socratic')`
- [ ] `ErrorType` enum: `conceptual`, `procedural`, `careless`, `notation`, `incomplete`, `none`
- [ ] `ExperimentCohort` enum: `control`, `treatmentA('treatment_a')`, `treatmentB('treatment_b')`
- [ ] `EventClassification` enum: `unconditional`, `conditional`, `serverAuthoritative('server_authoritative')`
- [ ] `SyncStatus` enum: `idle`, `syncing`, `error`, `conflict`
- [ ] `Student` freezed class: fields `id`, `name`, `experimentCohort`, `streak` (default 0), `xp` (default 0), `lastActive`, `locale` (default 'he'), `level` (default 1)
- [ ] `Concept` freezed class: fields `id`, `name`, `nameHe`, `subject`, `difficulty`, `bloomLevel`, `prerequisiteIds` (default []), `bagrutReference`
- [ ] `MasteryState` freezed class: `conceptId`, `pKnown`, `isMastered` (default false), `lastAttempted`, `methodology`, `attemptCount` (default 0), `consecutiveCorrect` (default 0)
- [ ] `Session` freezed class: `id`, `startedAt`, `endedAt`, `methodology`, `questionsAttempted` (default 0), `fatigueScore` (default 0.0), `targetDurationMinutes` (default 25), `subject`
- [ ] `Exercise` freezed class: `id`, `conceptId`, `questionType`, `difficulty`, `content`, `options`, `diagram`, `hints` (default []), `timeLimitSeconds` (default 0)
- [ ] `AnswerResult` freezed class: `isCorrect`, `errorType`, `priorMastery`, `posteriorMastery`, `feedback`, `workedSolution`, `xpEarned` (default 0)
- [ ] All classes have `fromJson` factory constructors
- [ ] `dart run build_runner build` generates `.freezed.dart` and `.g.dart` files without errors

**Test:**
```dart
test('Student round-trips through JSON', () {
  final student = Student(
    id: 'stu-001',
    name: 'Test Student',
    experimentCohort: ExperimentCohort.control,
    lastActive: DateTime.parse('2026-01-15T10:30:00Z'),
    locale: 'he',
  );
  final json = student.toJson();
  final restored = Student.fromJson(json);
  expect(restored, equals(student));
  expect(restored.streak, equals(0));
  expect(restored.level, equals(1));
});

test('Exercise with MCQ options serializes correctly', () {
  final exercise = Exercise(
    id: 'ex-001',
    conceptId: 'algebra-1',
    questionType: QuestionType.multipleChoice,
    difficulty: 5,
    content: r'Solve: $2x + 3 = 7$',
    options: ['x = 1', 'x = 2', 'x = 3', 'x = 4'],
    hints: ['Try isolating x', 'Subtract 3 from both sides'],
  );
  final json = exercise.toJson();
  expect(json['questionType'], equals('mcq'));
  expect(json['options'], hasLength(4));
  final restored = Exercise.fromJson(json);
  expect(restored.hints, hasLength(2));
});

test('BloomLevel JSON uses integer level field', () {
  final concept = Concept(
    id: 'c-001',
    name: 'Derivatives',
    subject: Subject.math,
    difficulty: 7,
    bloomLevel: BloomLevel.apply,
  );
  final json = concept.toJson();
  expect(json['bloomLevel'], equals(3)); // apply = level 3
});

test('MasteryState defaults are correct', () {
  final mastery = MasteryState(conceptId: 'c-001', pKnown: 0.5);
  expect(mastery.isMastered, isFalse);
  expect(mastery.attemptCount, equals(0));
  expect(mastery.consecutiveCorrect, equals(0));
});

test('Methodology JSON values match backend expectations', () {
  final session = Session(
    id: 's-001',
    startedAt: DateTime.now(),
    methodology: Methodology.spacedRepetition,
  );
  final json = session.toJson();
  expect(json['methodology'], equals('spaced_repetition'));
});
```

**Edge Cases:**
- `DateTime` serialization must use ISO-8601 format to match .NET backend (`2026-01-15T10:30:00.000Z`)
- `null` optional fields must be omitted from JSON (not sent as `null`) — configure `json_serializable` with `includeIfNull: false`
- `BloomLevel` int deserialization from backend: verify `BloomLevel.fromJson(3)` yields `BloomLevel.apply`

---

### MOB-002.2: Knowledge Graph & Offline Sync Models
**Files:**
- `lib/core/models/domain_models.dart` (continued — KG and sync sections)

**Acceptance:**
- [ ] `ConceptNode` freezed class: `conceptId`, `label`, `labelHe`, `subject`, `mastery`, `isMastered`, `x`, `y`, `radius` (default 24.0), `isSelected` (default false), `isUnlocked` (default true)
- [ ] `PrerequisiteEdge` freezed class: `fromConceptId`, `toConceptId`, `weight` (default 1.0), `isSatisfied` (default false)
- [ ] `KnowledgeGraph` freezed class: `nodes` (List<ConceptNode>), `edges` (List<PrerequisiteEdge>), `masteryOverlay` (Map<String, MasteryState>), `subjectFilter`
- [ ] `OfflineEvent` freezed class: `idempotencyKey`, `clientTimestamp`, `eventType`, `payload`, `classification`, `sequenceNumber`, `retryCount` (default 0), `lastError`
- [ ] `SyncRequest` freezed class: `studentId`, `clockOffsetMs`, `events` (List<OfflineEvent>), `lastAcknowledgedSequence`
- [ ] `SyncResponse` freezed class: `acknowledgedUpTo`, `acceptedKeys` (default []), `corrections` (default []), `rejectedKeys` (default []), `serverTimestamp`
- [ ] `SyncCorrection` freezed class: `idempotencyKey`, `field`, `clientValue`, `serverValue`, `weight`, `reason`
- [ ] `Badge` freezed class: `id`, `name`, `nameHe`, `iconAsset`, `description`, `earnedAt`, `isNew` (default false)
- [ ] `SessionSummary` freezed class: `sessionId`, `questionsAttempted`, `correctAnswers`, `xpEarned`, `duration`, `conceptsMastered` (List<String>), `conceptsImproved` (List<String>), `badgeEarned`, `streakMaintained`

**Test:**
```dart
test('KnowledgeGraph serializes with nested models', () {
  final graph = KnowledgeGraph(
    nodes: [
      ConceptNode(
        conceptId: 'c-001',
        label: 'Derivatives',
        labelHe: 'נגזרות',
        subject: Subject.math,
        mastery: 0.72,
        isMastered: false,
        x: 100.0,
        y: 200.0,
      ),
    ],
    edges: [
      PrerequisiteEdge(
        fromConceptId: 'c-000',
        toConceptId: 'c-001',
        weight: 0.8,
        isSatisfied: true,
      ),
    ],
    masteryOverlay: {
      'c-001': MasteryState(conceptId: 'c-001', pKnown: 0.72),
    },
  );
  final json = graph.toJson();
  final restored = KnowledgeGraph.fromJson(json);
  expect(restored.nodes, hasLength(1));
  expect(restored.edges.first.isSatisfied, isTrue);
  expect(restored.masteryOverlay['c-001']!.pKnown, equals(0.72));
});

test('OfflineEvent classification round-trips', () {
  final event = OfflineEvent(
    idempotencyKey: 'abc-123:42',
    clientTimestamp: DateTime.now(),
    eventType: 'AttemptConcept',
    payload: '{"answer": "x=2"}',
    classification: EventClassification.conditional,
    sequenceNumber: 42,
  );
  final json = event.toJson();
  expect(json['classification'], equals('conditional'));
  final restored = OfflineEvent.fromJson(json);
  expect(restored.retryCount, equals(0));
  expect(restored.classification, equals(EventClassification.conditional));
});

test('SyncResponse handles corrections', () {
  final response = SyncResponse(
    acknowledgedUpTo: 50,
    acceptedKeys: ['key-1', 'key-2'],
    corrections: [
      SyncCorrection(
        idempotencyKey: 'key-3',
        field: 'pKnown',
        clientValue: '0.85',
        serverValue: '0.82',
        weight: 0.75,
        reason: 'Server recalculated with more recent data',
      ),
    ],
    rejectedKeys: ['key-4'],
    serverTimestamp: DateTime.now(),
  );
  final json = response.toJson();
  final restored = SyncResponse.fromJson(json);
  expect(restored.corrections, hasLength(1));
  expect(restored.corrections.first.weight, equals(0.75));
});

test('Badge.isNew defaults to false', () {
  final badge = Badge(
    id: 'b-001',
    name: 'First Step',
    iconAsset: 'assets/icons/first_step.svg',
    description: 'Completed first exercise',
  );
  expect(badge.isNew, isFalse);
  expect(badge.earnedAt, isNull);
});

test('SessionSummary captures all stats', () {
  final summary = SessionSummary(
    sessionId: 's-001',
    questionsAttempted: 20,
    correctAnswers: 15,
    xpEarned: 150,
    duration: const Duration(minutes: 22),
    conceptsMastered: ['c-001', 'c-003'],
    conceptsImproved: ['c-002'],
    streakMaintained: true,
  );
  final json = summary.toJson();
  final restored = SessionSummary.fromJson(json);
  expect(restored.conceptsMastered, hasLength(2));
  expect(restored.streakMaintained, isTrue);
});
```

**Edge Cases:**
- `Duration` is not natively JSON-serializable — implement a custom `JsonConverter` that serializes to integer milliseconds
- `Map<String, MasteryState>` in `KnowledgeGraph.masteryOverlay` must serialize as a JSON object with string keys — verify this works with `json_serializable`
- Backend sends `null` for optional fields — ensure all nullable fields deserialize gracefully from `null`

---

### MOB-002.3: Diagram Models
**Files:**
- `lib/features/diagrams/models/diagram_models.dart`

**Acceptance:**
- [ ] `DiagramType` enum with 9 values: `functionPlot`, `circuit`, `geometry`, `molecular`, `biological`, `flowchart`, `physicsVector`, `workedExample`, `challengeCard` — all `@JsonValue` annotated
- [ ] `DiagramFormat` enum: `svg`, `png`, `rive`, `remotionVideo`
- [ ] `ConceptDiagram` freezed class with all contract fields: `id`, `conceptId`, `subject`, `type`, `format`, `bloomLevel`, `assetUrl`, `thumbnailUrl`, `inlineSvg`, `hotspots` (default []), `titleHe`, `titleAr`, `titleEn`, `descriptionHe`, `descriptionAr`, `descriptionEn`, `formulas` (default []), `generationMeta`, `cacheMeta`
- [ ] `DiagramHotspot` freezed class: `id`, `svgElementId`, `bounds`, `labelHe`, `labelAr`, `labelEn`, `explanationHe`, `explanationAr`, `explanationEn`, `linkedConceptId`, `style` (default `HotspotStyle.outline`)
- [ ] `HotspotBounds` freezed class: `x`, `y`, `width`, `height` (all doubles, 0.0-1.0 normalized)
- [ ] `HotspotStyle` enum: `outline`, `highlight`, `numbered`, `hidden`
- [ ] `ChallengeCard` freezed class: `id`, `diagram`, `tier`, `questionHe`, `answerType`, `options` (default []), `expectedValue`, `tolerance`, `expectedExpression`, `hintHe`, `xpReward` (default 10), `nextCardId`
- [ ] `ChallengeTier` enum: `beginner`, `intermediate`, `advanced`, `expert`
- [ ] `ChallengeAnswerType` enum: `multipleChoice`, `numeric`, `expression`, `dragLabel`, `tapHotspot`
- [ ] `ChallengeOption` freezed class: `id`, `textHe`, `isCorrect`, `feedbackHe`
- [ ] `DiagramGenerationMeta` freezed class: `model`, `generatedAt`, `curriculumVersion`, `reviewStatus`, `reviewedBy`, `inputTokens`, `outputTokens`
- [ ] `DiagramReviewStatus` enum: `pending`, `approved`, `rejected`, `autoApproved`
- [ ] `DiagramCacheMeta` freezed class: `s3Key`, `cdnUrl`, `contentHash`, `sizeBytes`, `publishedAt`, `clientCacheTtlHours` (default 168), `prefetchForOffline` (default false)
- [ ] `SubjectDiagramPalette` class with static palettes for math, physics, chemistry, biology, cs and `forSubject()` factory

**Test:**
```dart
test('ConceptDiagram with hotspots round-trips through JSON', () {
  final diagram = ConceptDiagram(
    id: 'diag-001',
    conceptId: 'c-derivatives',
    subject: 'math',
    type: DiagramType.functionPlot,
    format: DiagramFormat.svg,
    bloomLevel: 'apply',
    assetUrl: 'https://cdn.cena.education/diagrams/math/derivatives-001.svg',
    thumbnailUrl: 'https://cdn.cena.education/diagrams/math/derivatives-001-thumb.png',
    inlineSvg: '<svg>...</svg>',
    hotspots: [
      DiagramHotspot(
        id: 'hs-1',
        bounds: HotspotBounds(x: 0.3, y: 0.4, width: 0.1, height: 0.1),
        labelHe: 'נקודת קיצון',
        explanationHe: 'בנקודה זו הנגזרת מתאפסת',
      ),
    ],
    titleHe: 'גרף פונקציה ונגזרתה',
    descriptionHe: 'זהה את נקודות הקיצון',
    formulas: [r'f(x) = x^3 - 3x', r"f'(x) = 3x^2 - 3"],
    generationMeta: DiagramGenerationMeta(
      model: 'kimi-k2.5',
      generatedAt: DateTime.now(),
      curriculumVersion: '1.2.0',
      reviewStatus: DiagramReviewStatus.approved,
    ),
    cacheMeta: DiagramCacheMeta(
      s3Key: 'diagrams/math/v1.2.0/derivatives-001.svg',
      cdnUrl: 'https://cdn.cena.education/diagrams/math/derivatives-001.svg?h=abc123',
      contentHash: 'sha256-abc123def456',
      sizeBytes: 24000,
      publishedAt: DateTime.now(),
    ),
  );
  final json = diagram.toJson();
  final restored = ConceptDiagram.fromJson(json);
  expect(restored.hotspots, hasLength(1));
  expect(restored.hotspots.first.labelHe, equals('נקודת קיצון'));
  expect(restored.formulas, hasLength(2));
  expect(restored.cacheMeta.clientCacheTtlHours, equals(168));
});

test('SubjectDiagramPalette resolves correct palette', () {
  final mathPalette = SubjectDiagramPalette.forSubject('math');
  expect(mathPalette.primary, equals(0xFF0891B2));

  final physicsPalette = SubjectDiagramPalette.forSubject('physics');
  expect(physicsPalette.primary, equals(0xFFD97706));

  // Unknown subject falls back to math
  final unknownPalette = SubjectDiagramPalette.forSubject('art');
  expect(unknownPalette.primary, equals(0xFF0891B2));
});

test('ChallengeCard with MCQ options', () {
  final card = ChallengeCard(
    id: 'cc-001',
    diagram: _testDiagram(),
    tier: ChallengeTier.intermediate,
    questionHe: 'מה ההתנגדות הכוללת?',
    answerType: ChallengeAnswerType.multipleChoice,
    options: [
      ChallengeOption(id: 'a', textHe: '5Ω', isCorrect: false, feedbackHe: 'לא נכון'),
      ChallengeOption(id: 'b', textHe: '10Ω', isCorrect: true),
    ],
    xpReward: 15,
  );
  final json = card.toJson();
  expect(json['tier'], equals('intermediate'));
  expect(json['xpReward'], equals(15));
});

test('HotspotBounds values are 0-1 normalized', () {
  final bounds = HotspotBounds(x: 0.5, y: 0.5, width: 0.2, height: 0.15);
  expect(bounds.x, inInclusiveRange(0.0, 1.0));
  expect(bounds.y, inInclusiveRange(0.0, 1.0));
  expect(bounds.width, inInclusiveRange(0.0, 1.0));
  expect(bounds.height, inInclusiveRange(0.0, 1.0));
});
```

**Edge Cases:**
- `DiagramFormat.remotionVideo` may not be supported on all platforms — model must serialize/deserialize regardless of platform support
- `inlineSvg` can be very large (50KB+) — ensure JSON serialization handles large strings without truncation
- `SubjectDiagramPalette.forSubject()` must handle case-insensitive input and aliases ('mathematics' -> math, 'computer_science' -> cs)

---

## Integration Test

```dart
void main() {
  group('MOB-002 Integration: All domain models serialize/deserialize correctly', () {
    test('all freezed models generate copyWith', () {
      final student = Student(
        id: 's1',
        name: 'A',
        experimentCohort: ExperimentCohort.control,
        lastActive: DateTime.now(),
      );
      final updated = student.copyWith(name: 'B', xp: 100);
      expect(updated.name, equals('B'));
      expect(updated.xp, equals(100));
      expect(updated.id, equals('s1')); // unchanged fields preserved
    });

    test('nested model graph (KnowledgeGraph) deep-equals after round-trip', () {
      final graph = _buildFullTestGraph();
      final json = graph.toJson();
      final jsonString = jsonEncode(json);
      final decoded = jsonDecode(jsonString) as Map<String, dynamic>;
      final restored = KnowledgeGraph.fromJson(decoded);
      expect(restored, equals(graph));
    });

    test('all enums have exhaustive JsonValue coverage', () {
      // Verify every enum value can round-trip through JSON
      for (final subject in Subject.values) {
        final concept = Concept(
          id: 'test',
          name: 'test',
          subject: subject,
          difficulty: 1,
          bloomLevel: BloomLevel.remember,
        );
        final restored = Concept.fromJson(concept.toJson());
        expect(restored.subject, equals(subject));
      }
      for (final qt in QuestionType.values) {
        final exercise = Exercise(
          id: 'test',
          conceptId: 'c',
          questionType: qt,
          difficulty: 1,
          content: 'test',
        );
        final restored = Exercise.fromJson(exercise.toJson());
        expect(restored.questionType, equals(qt));
      }
    });

    test('build_runner generates all .freezed.dart and .g.dart files', () async {
      final result = await Process.run(
        'dart',
        ['run', 'build_runner', 'build', '--delete-conflicting-outputs'],
      );
      expect(result.exitCode, equals(0));
    });
  });
}
```

## Rollback Criteria
- If freezed code generation becomes prohibitively slow (>60s): split `domain_models.dart` into per-feature model files (session_models.dart, kg_models.dart, sync_models.dart)
- If `json_serializable` produces incompatible JSON with the .NET backend: manually implement `toJson`/`fromJson` for affected classes
- If `DiagramGenerationMeta` and `DiagramCacheMeta` add too much serialization overhead: make them lazy-loaded (not included in default JSON responses)

## Definition of Done
- [ ] All 3 subtasks pass their individual tests
- [ ] `dart run build_runner build` generates all files cleanly
- [ ] Every freezed class round-trips through `toJson()` / `fromJson()` without data loss
- [ ] All JSON field names match the backend .NET SignalR hub contract
- [ ] `flutter analyze` reports zero issues on model files
- [ ] PR reviewed by mobile lead
